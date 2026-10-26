using IfcComparison.Enumerations;
using IfcComparison.Geometry;
using IfcComparison.Logging;
using IfcComparison.ViewModels;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc2x3.Kernel;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.PropertyResource;
using Xbim.Ifc4.UtilityResource;

namespace IfcComparison.Models
{
    public class IfcComparer
    {
        public IfcComparerObjects OldObjects { get; set; }
        public IfcComparerObjects NewObjects { get; set; }
        public IfcStore OldModel { get; }
        public IfcStore NewModelQA { get; }
        public string FileNameSaveAs { get; }
        public string TransactionText { get; }
        public List<IfcEntity> Entities { get; }
        public IfcComparerResult IfcComparisonResult { get; private set; } = new IfcComparerResult();
        public IfcWriter IfcWriter { get; set; }
        public IfcGeometryExtractor OldGeomExtractor { get; set; }
        public IfcGeometryExtractor NewGeomExtractor { get; set; }
        private Dictionary<IIfcObject, Dictionary<string, string>> AllComparedObjects { get; set; } = new Dictionary<IIfcObject, Dictionary<string, string>>();

        /// <summary>
        /// Tracks which object GlobalIds (or entity labels) have already been compared
        /// for each comparison key (PropertySetName + ComparisonOperator + ComparisonMethod).
        /// Key: ComparisonKey -> Set of object GlobalIds
        /// </summary>
        public Dictionary<string, HashSet<string>> ComparedObjectsByKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks which object GlobalIds have already had their 3D geometry compared.
        /// Geometry is physical and only needs to be compared once per object across all entity passes.
        /// </summary>
        public HashSet<string> GeometryComparedGlobalIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Direct mapping of compared objects to their configured PSetName.
        /// </summary>
        public Dictionary<IIfcObject, string> ObjectToPSetMap { get; set; } = new();

        private readonly ILogger<IfcComparer> _logger;

        // Private constructor for the factory method
        private IfcComparer(IfcStore oldModel, IfcStore newModelQA, string fileNameSaveAs, string transactionText, List<IfcEntity> entities)
        {
            OldModel = oldModel;
            NewModelQA = newModelQA;
            FileNameSaveAs = fileNameSaveAs;
            TransactionText = transactionText;
            Entities = entities;
            _logger = LoggingService.CreateLogger<IfcComparer>();
        }

        // Public async factory method with List<IfcEntity>
        public static async Task<IfcComparer> CreateAsync(IfcStore oldModel, IfcStore newModelQA, string fileNameSaveAs, string transactionText, List<IfcEntity> entities)
        {
            var instance = new IfcComparer(oldModel, newModelQA, fileNameSaveAs, transactionText, entities);
            await instance.InitializeAsync();
            return instance;
        }

        private async Task InitializeAsync()
        {
            // No need to initialize all entity objects at once
            // They will be processed one by one in CompareAllRevisions
        }

        // Method to process all entities in the list
        public async Task CompareAllRevisions()
        {
            if (Entities == null || !Entities.Any())
            {
                throw new InvalidOperationException("No entities provided for comparison.");
            }

            _logger.LogInformation("Starting comparison of all entities...");
            
            // Create combined result object
            var combinedResult = new IfcComparerResult
            {
                OldObjectsNotInNew = new List<IfcObjectStorage>(),
                NewObjectsNotInOld = new List<IfcObjectStorage>(),
                ComparedIfcObjects = new Dictionary<IIfcObject, Dictionary<string, string>>(),
                ModifiedNewObjects = new HashSet<IIfcProduct>(),
                ModifiedOldGhostObjects = new List<IIfcProduct>(),
                DeletedGhostObjects = new List<IIfcProduct>(),
                InsertedObjects = new HashSet<IIfcProduct>(),
                ModifiedPropertyOnlyObjects = new HashSet<IIfcProduct>()
            };

            // Expand multi-value / wildcard Entity and PSetName fields into concrete single entries
            var expandedEntities = Entities.SelectMany(e => e.Expand()).ToList();

            // Filter to only entities whose type exists in at least one of the two models.
            // This avoids pointless iterations when "*" expands to hundreds of entity types.
            var filteredEntities = expandedEntities
                .Where(e =>
                {
                    var t = IfcTools.GetInterfaceType(e.Entity);
                    if (t == null) return false;
                    return IfcTools.HasAnyInstances(OldModel, t) || IfcTools.HasAnyInstances(NewModelQA, t);
                })
                // Sort entities so more specific types (e.g. IIfcReinforcingBar) are evaluated before
                // general base types (e.g. IIfcProduct, IIfcObject). LINQ OrderByDescending is stable.
                .OrderByDescending(e =>
                {
                    var t = IfcTools.GetInterfaceType(e.Entity);
                    if (t == null) return 0;
                    if (t == typeof(IIfcObject)) return -1;
                    return t.GetInterfaces().Length;
                })
                .ToList();

            int skipped = expandedEntities.Count - filteredEntities.Count;
            if (skipped > 0)
                _logger.LogInformation($"Skipped {skipped} entity type(s) with no instances in either model.");

            int totalEntityCount = filteredEntities.Count;

            // Initialize 3D geometry contexts if any entity requires geometry comparison
            bool anyCompareGeometry = filteredEntities.Any(e => e.CompareGeometry);
            IfcGeometryExtractor oldGeom = null;
            IfcGeometryExtractor newGeom = null;
            if (anyCompareGeometry)
            {
                _logger.LogInformation("Initializing 3D geometry contexts for models...");
                oldGeom = new IfcGeometryExtractor(OldModel);
                newGeom = new IfcGeometryExtractor(NewModelQA);
                oldGeom.EnsureContextCreated();
                newGeom.EnsureContextCreated();
                OldGeomExtractor = oldGeom;
                NewGeomExtractor = newGeom;
            }

            var seenDeletedGlobalIds = new HashSet<string>();
            var seenModifiedOldGlobalIds = new HashSet<string>();
            var seenModifiedNewGlobalIds = new HashSet<string>();
            var seenInsertedGlobalIds = new HashSet<string>();
            var seenOldNotInNewGlobalIds = new HashSet<string>();
            var seenNewNotInOldGlobalIds = new HashSet<string>();
            var seenModifiedPropertyGlobalIds = new HashSet<string>();

            // Process entities sequentially to ensure thread safety with XBIM IfcStore and OpenCascade 3D contexts
            for (int index = 0; index < filteredEntities.Count; index++)
            {
                var entity = filteredEntities[index];
                var currentEntityIndex = index + 1;
                _logger.LogInformation($"Processing entity {currentEntityIndex}/{totalEntityCount}: {entity.Entity}");
                _logger.LogInformation($"Entity '{entity.Entity}': 3D geometry comparison is {(entity.CompareGeometry ? "ENABLED" : "DISABLED")}");
                
                // Initialize objects for this specific entity
                var oldObjects = await IfcComparerObjects.CreateAsync(OldModel, entity);
                var newObjects = await IfcComparerObjects.CreateAsync(NewModelQA, entity);

                _logger.LogInformation($"Found {oldObjects.IfcStorageObjects?.Count ?? 0} objects in old model and {newObjects.IfcStorageObjects?.Count ?? 0} objects in new model");

                // Create a temp IfcComparer for this entity to reuse existing comparison logic
                var tempComparer = new IfcComparer(OldModel, NewModelQA, FileNameSaveAs, TransactionText, new List<IfcEntity> { entity })
                {
                    OldObjects = oldObjects,
                    NewObjects = newObjects,
                    OldGeomExtractor = oldGeom,
                    NewGeomExtractor = newGeom,
                    ComparedObjectsByKey = ComparedObjectsByKey,
                    GeometryComparedGlobalIds = GeometryComparedGlobalIds,
                    ObjectToPSetMap = ObjectToPSetMap
                };

                // Process this entity
                _logger.LogInformation($"Comparing '{entity.Entity}' objects using {entity.ComparisonMethod} method...");
                await tempComparer.CompareEntityInternal();

                // Log comparison results
                int oldNotInNewCount = tempComparer.IfcComparisonResult.OldObjectsNotInNew?.Count ?? 0;
                int newNotInOldCount = tempComparer.IfcComparisonResult.NewObjectsNotInOld?.Count ?? 0;
                int comparedObjectsCount = tempComparer.IfcComparisonResult.ComparedIfcObjects?.Count ?? 0;
                
                _logger.LogInformation($"Results for '{entity.Entity}': {oldNotInNewCount} removed objects, {newNotInOldCount} new objects, {comparedObjectsCount} compared objects");

                // Deduplicate and accumulate results into combinedResult
                if (tempComparer.IfcComparisonResult.OldObjectsNotInNew != null)
                {
                    foreach (var storage in tempComparer.IfcComparisonResult.OldObjectsNotInNew)
                    {
                        bool alreadySeen = storage.IfcObjects.Keys.Any(id => seenOldNotInNewGlobalIds.Contains(id.ToString()));
                        if (!alreadySeen)
                        {
                            foreach (var id in storage.IfcObjects.Keys)
                            {
                                seenOldNotInNewGlobalIds.Add(id.ToString());
                            }
                            combinedResult.OldObjectsNotInNew.Add(storage);
                        }
                    }
                }

                if (tempComparer.IfcComparisonResult.NewObjectsNotInOld != null)
                {
                    foreach (var storage in tempComparer.IfcComparisonResult.NewObjectsNotInOld)
                    {
                        bool alreadySeen = storage.IfcObjects.Keys.Any(id => seenNewNotInOldGlobalIds.Contains(id.ToString()));
                        if (!alreadySeen)
                        {
                            foreach (var id in storage.IfcObjects.Keys)
                            {
                                seenNewNotInOldGlobalIds.Add(id.ToString());
                            }
                            combinedResult.NewObjectsNotInOld.Add(storage);
                        }
                    }
                }

                if (tempComparer.IfcComparisonResult.ComparedIfcObjects != null)
                {
                    foreach (var kvp in tempComparer.IfcComparisonResult.ComparedIfcObjects)
                    {
                        if (combinedResult.ComparedIfcObjects.TryGetValue(kvp.Key, out var existingProps))
                        {
                            foreach (var prop in kvp.Value)
                            {
                                existingProps[prop.Key] = prop.Value;
                            }
                        }
                        else
                        {
                            combinedResult.ComparedIfcObjects[kvp.Key] = new Dictionary<string, string>(kvp.Value);
                        }

                        if (!ObjectToPSetMap.ContainsKey(kvp.Key) && !string.IsNullOrEmpty(entity.PSetName))
                        {
                            ObjectToPSetMap[kvp.Key] = entity.PSetName;
                        }
                    }
                }

                if (tempComparer.IfcComparisonResult.ModifiedNewObjects != null)
                {
                    foreach (var obj in tempComparer.IfcComparisonResult.ModifiedNewObjects)
                    {
                        if (obj != null && seenModifiedNewGlobalIds.Add(obj.GlobalId.ToString()))
                        {
                            combinedResult.ModifiedNewObjects.Add(obj);
                        }
                    }
                }

                if (tempComparer.IfcComparisonResult.ModifiedOldGhostObjects != null)
                {
                    foreach (var obj in tempComparer.IfcComparisonResult.ModifiedOldGhostObjects)
                    {
                        if (obj != null && seenModifiedOldGlobalIds.Add(obj.GlobalId.ToString()))
                        {
                            combinedResult.ModifiedOldGhostObjects.Add(obj);
                        }
                    }
                }

                if (tempComparer.IfcComparisonResult.DeletedGhostObjects != null)
                {
                    foreach (var obj in tempComparer.IfcComparisonResult.DeletedGhostObjects)
                    {
                        if (obj != null && seenDeletedGlobalIds.Add(obj.GlobalId.ToString()))
                        {
                            combinedResult.DeletedGhostObjects.Add(obj);
                        }
                    }
                }

                if (tempComparer.IfcComparisonResult.InsertedObjects != null)
                {
                    foreach (var obj in tempComparer.IfcComparisonResult.InsertedObjects)
                    {
                        if (obj != null && seenInsertedGlobalIds.Add(obj.GlobalId.ToString()))
                        {
                            combinedResult.InsertedObjects.Add(obj);
                        }
                    }
                }

                if (tempComparer.IfcComparisonResult.ModifiedPropertyOnlyObjects != null)
                {
                    foreach (var obj in tempComparer.IfcComparisonResult.ModifiedPropertyOnlyObjects)
                    {
                        if (obj != null && seenModifiedPropertyGlobalIds.Add(obj.GlobalId.ToString()))
                        {
                            combinedResult.ModifiedPropertyOnlyObjects.Add(obj);
                        }
                    }
                }
            }

            // Store the combined results
            IfcComparisonResult = combinedResult;

            // Log final totals
            _logger.LogInformation($"Comparison complete. Total results: " + 
                $"{combinedResult.OldObjectsNotInNew?.Count ?? 0} removed objects, " +
                $"{combinedResult.NewObjectsNotInOld?.Count ?? 0} new objects, " +
                $"{combinedResult.ComparedIfcObjects?.Count ?? 0} compared objects, " +
                $"{combinedResult.ModifiedNewObjects?.Count ?? 0} modified geometry objects, " +
                $"{combinedResult.ModifiedPropertyOnlyObjects?.Count ?? 0} modified property-only objects, " +
                $"{combinedResult.DeletedGhostObjects?.Count ?? 0} deleted ghost objects, " +
                $"{combinedResult.InsertedObjects?.Count ?? 0} inserted objects");

            // Set up the IfcWriter with combined results
            IfcWriter = new IfcWriter(IfcComparisonResult, NewModelQA.SchemaVersion, FileNameSaveAs);

            // Now write all results to file once
            if (filteredEntities.Any())
            {
                _logger.LogInformation("Writing results to file...");
                
                // Create a dictionary to map objects to their appropriate PSetNames
                var objectPSetMap = new Dictionary<Xbim.Ifc4.Interfaces.IIfcObject, string>();

                foreach (var kvp in combinedResult.ComparedIfcObjects)
                {
                    if (ObjectToPSetMap.TryGetValue(kvp.Key, out var psetName))
                    {
                        objectPSetMap[kvp.Key] = psetName;
                    }
                    else
                    {
                        var matchingEntity = filteredEntities.FirstOrDefault(e => kvp.Key.GetType().GetInterfaces().Any(i => i.Name == e.Entity));
                        objectPSetMap[kvp.Key] = matchingEntity?.PSetName ?? "QA_PSET";
                    }
                }

                // Pass the mapping to IfcWriter
                await IfcWriter.WriteToFileAsync(NewModelQA, objectPSetMap);
                _logger.LogInformation("Results successfully written to file");
            }
        }

        // Private method to compare a single entity (reused from original CompareRevisions logic)
        private async Task CompareEntityInternal()
        {
            // Ensure both OldObjects and NewObjects are initialized
            if (OldObjects == null || NewObjects == null)
            {
                throw new InvalidOperationException("OldObjects or NewObjects are not initialized.");
            }

            var entity = Entities.FirstOrDefault();
            if (entity == null)
            {
                throw new InvalidOperationException("Entity is not initialized.");
            }

            var comparisonMethod = ResolveComparisonMethod(entity.ComparisonMethod);
            _logger.LogInformation($"Using comparison method: {comparisonMethod}");
            await Compare();
        }

        // Original CompareRevisions method maintained for backward compatibility
        public async Task CompareRevisions()
        {
            // Ensure both OldObjects and NewObjects are initialized
            if (OldObjects == null || NewObjects == null)
            {
                throw new InvalidOperationException("OldObjects or NewObjects are not initialized.");
            }

            var entity = Entities.FirstOrDefault();
            if (entity == null)
            {
                throw new InvalidOperationException("Entity is not initialized.");
            }

            if (entity.CompareGeometry)
            {
                if (OldGeomExtractor == null)
                {
                    OldGeomExtractor = new IfcGeometryExtractor(OldModel);
                    OldGeomExtractor.EnsureContextCreated();
                }
                if (NewGeomExtractor == null)
                {
                    NewGeomExtractor = new IfcGeometryExtractor(NewModelQA);
                    NewGeomExtractor.EnsureContextCreated();
                }
            }

            var comparisonMethod = ResolveComparisonMethod(entity.ComparisonMethod);
            _logger.LogInformation($"Using comparison method: {comparisonMethod}");
            await Compare();

            if (IfcWriter != null)
            {
                await Task.Run(() => IfcWriter.WriteToFileAsync(NewModelQA, entity.PSetName));
            }
        }

        public static string GetComparisonKey(IfcEntity entity)
        {
            var psetList = entity?.IfcPropertySets?
                .Select(p => (p ?? string.Empty).Trim().ToUpperInvariant())
                .Where(p => !string.IsNullOrEmpty(p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            var psets = (psetList != null && psetList.Any())
                ? string.Join(",", psetList)
                : (entity?.PSetName ?? string.Empty).Trim().ToUpperInvariant();
            var op = (entity?.ComparisonOperator ?? string.Empty).Trim().ToUpperInvariant();
            var method = ResolveComparisonMethod(entity?.ComparisonMethod).ToString().ToUpperInvariant();

            return $"{psets}|{op}|{method}";
        }

        private static ComparisonEnumeration ResolveComparisonMethod(string methodString)
        {
            if (string.IsNullOrWhiteSpace(methodString))
                return ComparisonEnumeration.Identifier;

            if (Enum.TryParse<ComparisonEnumeration>(methodString, true, out var method))
                return method;

            if (string.Equals(methodString, "GlobalId", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(methodString, "Guid", StringComparison.OrdinalIgnoreCase))
            {
                return ComparisonEnumeration.Identifier;
            }

            if (string.Equals(methodString, "Exact", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(methodString, "Equals", StringComparison.OrdinalIgnoreCase))
            {
                return ComparisonEnumeration.Exact;
            }

            if (string.Equals(methodString, "Contains", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(methodString, "StartsWith", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(methodString, "EndsWith", StringComparison.OrdinalIgnoreCase))
            {
                return ComparisonEnumeration.Contains;
            }

            return ComparisonEnumeration.Identifier;
        }

        private async Task Compare()
        {
            var ifcComparerResult = new IfcComparerResult();

            // Get the comparison operator from the entity
            var entity = Entities.FirstOrDefault();
            var comparisonOperator = entity?.ComparisonOperator ?? string.Empty;
            var comparisonMethod = ResolveComparisonMethod(entity?.ComparisonMethod);

            _logger.LogInformation($"Starting comparison using operator '{comparisonOperator}' and method '{comparisonMethod}'");

            // Run the comparison tasks in parallel
            _logger.LogInformation("Checking for objects not in the other model...");
            var oldObjectsNotInNew = CheckIfIfcObjectsAreInIfcObjects(OldObjects, NewObjects, comparisonOperator, comparisonMethod, "new");
            var newObjectsNotInOld = CheckIfIfcObjectsAreInIfcObjects(NewObjects, OldObjects, comparisonOperator, comparisonMethod, "old");

            // First we will check the properties and geometry
            _logger.LogInformation("Comparing properties between models...");
            var propertyCompareResult = PropertyCompare(NewObjects, OldObjects, comparisonOperator, comparisonMethod, ifcComparerResult);

            // Wait for tasks to complete
            _logger.LogInformation("Waiting for all comparison tasks to complete...");
            await Task.WhenAll(oldObjectsNotInNew, newObjectsNotInOld, propertyCompareResult);

            // Add the results to the IfcComparerResult
            ifcComparerResult.OldObjectsNotInNew = await oldObjectsNotInNew;
            ifcComparerResult.NewObjectsNotInOld = await newObjectsNotInOld;
            ifcComparerResult.ComparedIfcObjects = await propertyCompareResult;

            // Track deleted and inserted ghost objects if ghost overlays are enabled
            if (entity != null && entity.EnableGhostOverlays)
            {
                foreach (var storage in ifcComparerResult.OldObjectsNotInNew)
                {
                    foreach (var ifcObj in storage.IfcObjects.Values)
                    {
                        if (ifcObj is IIfcProduct product)
                        {
                            ifcComparerResult.DeletedGhostObjects.Add(product);
                        }
                    }
                }

                foreach (var storage in ifcComparerResult.NewObjectsNotInOld)
                {
                    foreach (var ifcObj in storage.IfcObjects.Values)
                    {
                        if (ifcObj is IIfcProduct product)
                        {
                            ifcComparerResult.InsertedObjects.Add(product);
                        }
                    }
                }
            }

            // Track property-only changed objects (metadata changed, but 3D geometry not modified and not inserted)
            if (ifcComparerResult.ComparedIfcObjects != null)
            {
                foreach (var kvp in ifcComparerResult.ComparedIfcObjects)
                {
                    var obj = kvp.Key;
                    var props = kvp.Value;

                    if (obj is IIfcProduct prod &&
                        (ifcComparerResult.ModifiedNewObjects == null || !ifcComparerResult.ModifiedNewObjects.Contains(prod)) &&
                        (ifcComparerResult.InsertedObjects == null || !ifcComparerResult.InsertedObjects.Contains(prod)))
                    {
                        if (props != null && props.Any(p => IfcTools.IsActualPropertyChange(p.Key, p.Value)))
                        {
                            ifcComparerResult.ModifiedPropertyOnlyObjects.Add(prod);
                        }
                    }
                }
            }

            _logger.LogInformation($"Comparison results: {ifcComparerResult.OldObjectsNotInNew.Count} old objects not in new, " +
                $"{ifcComparerResult.NewObjectsNotInOld.Count} new objects not in old, " +
                $"{ifcComparerResult.ComparedIfcObjects.Count} objects compared, " +
                $"{ifcComparerResult.ModifiedNewObjects.Count} modified geometry objects, " +
                $"{ifcComparerResult.ModifiedPropertyOnlyObjects.Count} modified property-only objects, " +
                $"{ifcComparerResult.DeletedGhostObjects.Count} deleted ghost objects, " +
                $"{ifcComparerResult.InsertedObjects.Count} inserted objects");

            IfcWriter = new IfcWriter(ifcComparerResult, NewModelQA.SchemaVersion, FileNameSaveAs);

            // Store the results
            IfcComparisonResult = ifcComparerResult;
        }

        private async Task<Dictionary<IIfcObject, Dictionary<string, string>>> PropertyCompare(
            IfcComparerObjects newObjects, 
            IfcComparerObjects oldObjects, 
            string comparisonOperator, 
            ComparisonEnumeration comparisonEnumeration,
            IfcComparerResult ifcComparerResult = null)
        {
            _logger.LogInformation($"Starting property comparison using {comparisonEnumeration} method");
            
            var result = new Dictionary<IIfcObject, Dictionary<string, string>>();
            var requiredPSets = Entities.FirstOrDefault()?.IfcPropertySets;
            var entity = Entities.FirstOrDefault();
            var targetEntityType = entity != null ? IfcTools.GetInterfaceType(entity.Entity) : null;

            _logger.LogInformation($"Target entity type: {targetEntityType?.FullName ?? "NULL"}, Entity name: {entity?.Entity ?? "NULL"}");

            var comparisonKey = GetComparisonKey(entity);
            if (!ComparedObjectsByKey.TryGetValue(comparisonKey, out var comparedSet))
            {
                comparedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ComparedObjectsByKey[comparisonKey] = comparedSet;
            }

            int skippedDuplicatesCount = 0;

            if (comparisonEnumeration != ComparisonEnumeration.Identifier)
            {
                _logger.LogInformation("Using property-based comparison with OPTIMIZED forward lookups");
                
                // PERFORMANCE OPTIMIZATION: Build lookups using forward relationships (PropertySet Ã¢â€ â€™ Objects)
                // This is O(n) instead of O(nÃ‚Â²) when using inverse IsDefinedBy lookups
                _logger.LogInformation("Building optimized lookup for old model using forward relationships...");
                var (oldObjectLookup, oldPropertySetsCache) = IfcTools.BuildComparisonLookupWithPropertySets(
                    OldModel, 
                    comparisonOperator, 
                    requiredPSets, 
                    targetEntityType);
                _logger.LogInformation($"Created lookup with {oldObjectLookup.Count} unique values and {oldPropertySetsCache.Count} objects from old model");

                _logger.LogInformation("Building optimized lookup for new model using forward relationships...");
                var (newObjectLookup, newPropertySetsCache) = IfcTools.BuildComparisonLookupWithPropertySets(
                    NewModelQA, 
                    comparisonOperator, 
                    requiredPSets, 
                    targetEntityType);
                _logger.LogInformation($"Created lookup with {newObjectLookup.Count} unique values and {newPropertySetsCache.Count} objects from new model");

                // Step 2: Compare objects using the pre-built lookups
                int matchCount = 0;
                _logger.LogInformation("Comparing new objects against old objects using cached data");
                
                foreach (var kvp in newObjectLookup)
                {
                    var comparisonValue = kvp.Key;
                    var newIfcObjects = kvp.Value;

                    // Check if we have matching old objects
                    if (oldObjectLookup.TryGetValue(comparisonValue, out var oldMatches))
                    {
                        foreach (var newIfcObj in newIfcObjects)
                        {
                            var objId = !string.IsNullOrEmpty(newIfcObj.GlobalId.ToString()) ? newIfcObj.GlobalId.ToString() : ((IPersistEntity)newIfcObj).EntityLabel.ToString();
                            bool alreadyComparedProps = comparedSet.Contains(objId);
                            bool alreadyComparedGeom = GeometryComparedGlobalIds.Contains(objId);

                            if (alreadyComparedProps && (alreadyComparedGeom || !entity.CompareGeometry))
                            {
                                skippedDuplicatesCount++;
                                continue;
                            }

                            if (!newPropertySetsCache.TryGetValue(newIfcObj, out var newPsets))
                                continue;

                            foreach (var oldMatch in oldMatches)
                            {
                                if (!oldPropertySetsCache.TryGetValue(oldMatch, out var oldPsets))
                                    continue;

                                // Compare property sets between new and old objects
                                if (!alreadyComparedProps)
                                {
                                    CompareAndAddPropertySets(newIfcObj, newPsets, oldPsets, result);
                                    comparedSet.Add(objId);
                                    ObjectToPSetMap[newIfcObj] = entity?.PSetName ?? "QA_PSET";
                                }

                                // Compare 3D geometry if requested
                                if (entity != null && entity.CompareGeometry && OldGeomExtractor != null && NewGeomExtractor != null && !alreadyComparedGeom)
                                {
                                    CompareAndAddGeometry(newIfcObj, oldMatch, entity, result, ifcComparerResult);
                                    GeometryComparedGlobalIds.Add(objId);
                                }

                                matchCount++;
                            }
                        }
                    }
                }
                _logger.LogInformation($"Completed comparison with {matchCount} property set comparisons");
            }
            else
            {
                _logger.LogInformation("Using GlobalId-based comparison with OPTIMIZED forward lookups");
                
                // PERFORMANCE OPTIMIZATION: Build lookups using forward relationships
                _logger.LogInformation("Building optimized GlobalId lookup for old model...");
                var (oldObjectLookup, oldPropertySetsCache) = IfcTools.BuildGlobalIdLookupWithPropertySets(
                    OldModel, 
                    requiredPSets, 
                    targetEntityType);
                _logger.LogInformation($"Created lookup with {oldObjectLookup.Count} objects from old model by GlobalId");

                _logger.LogInformation("Building optimized GlobalId lookup for new model...");
                var (newObjectLookup, newPropertySetsCache) = IfcTools.BuildGlobalIdLookupWithPropertySets(
                    NewModelQA, 
                    requiredPSets, 
                    targetEntityType);
                _logger.LogInformation($"Created lookup with {newObjectLookup.Count} objects from new model by GlobalId");

                // Step 2: Compare objects using GlobalId lookups
                int matchCount = 0;
                foreach (var kvp in newObjectLookup)
                {
                    var globalId = kvp.Key;
                    var newIfcObj = kvp.Value;

                    var objId = globalId ?? ((IPersistEntity)newIfcObj).EntityLabel.ToString();
                    bool alreadyComparedProps = comparedSet.Contains(objId);
                    bool alreadyComparedGeom = GeometryComparedGlobalIds.Contains(objId);

                    if (alreadyComparedProps && (alreadyComparedGeom || !entity.CompareGeometry))
                    {
                        skippedDuplicatesCount++;
                        continue;
                    }

                    if (!newPropertySetsCache.TryGetValue(newIfcObj, out var newPsets))
                        continue;

                    // Check if this GlobalId exists in the old objects
                    if (oldObjectLookup.TryGetValue(globalId, out var matchingOldObject))
                    {
                        if (!oldPropertySetsCache.TryGetValue(matchingOldObject, out var oldPsets))
                            continue;

                        // Compare property sets between the new and old objects
                        if (!alreadyComparedProps)
                        {
                            CompareAndAddPropertySets(newIfcObj, newPsets, oldPsets, result);
                            comparedSet.Add(objId);
                            ObjectToPSetMap[newIfcObj] = entity?.PSetName ?? "QA_PSET";
                        }

                        // Compare 3D geometry if requested
                        if (entity != null && entity.CompareGeometry && OldGeomExtractor != null && NewGeomExtractor != null && !alreadyComparedGeom)
                        {
                            CompareAndAddGeometry(newIfcObj, matchingOldObject, entity, result, ifcComparerResult);
                            GeometryComparedGlobalIds.Add(objId);
                        }

                        matchCount++;
                    }
                }
                _logger.LogInformation($"Completed comparison with {matchCount} GlobalId matches");
            }

            if (skippedDuplicatesCount > 0)
            {
                _logger.LogInformation($"Skipped {skippedDuplicatesCount} object(s) already compared for property set '{comparisonKey}'");
            }

            _logger.LogInformation($"Property comparison complete with {result.Count} results");
            await Task.CompletedTask; // No need for artificial delay
            return result;
        }

        // Helper method to compare and add geometry deltas
        private void CompareAndAddGeometry(
            IIfcObject newIfcObj,
            IIfcObject oldMatch,
            IfcEntity entity,
            Dictionary<IIfcObject, Dictionary<string, string>> result,
            IfcComparerResult ifcComparerResult)
        {
            if (newIfcObj is not IIfcProduct newProduct || oldMatch is not IIfcProduct oldProduct)
                return;

            try
            {
                var oldMetrics = OldGeomExtractor.ExtractMetrics(oldProduct);
                var newMetrics = NewGeomExtractor.ExtractMetrics(newProduct);

                var geomComparer = new IfcGeometryComparer
                {
                    VolumeToleranceM3 = entity.VolumeToleranceM3,
                    VolumePercentTolerance = entity.VolumePercentTolerance,
                    PositionToleranceMm = entity.PositionToleranceMm
                };

                var geomResult = geomComparer.Compare(oldMetrics, newMetrics);

                // If there's a geometric difference or if the object already has property changes
                if (geomResult.HasGeometricDifference || result.ContainsKey(newIfcObj))
                {
                    var geomProps = geomResult.ToPropertiesDictionary();

                    if (!result.ContainsKey(newIfcObj))
                    {
                        result[newIfcObj] = geomProps;
                    }
                    else
                    {
                        var existingProperties = result[newIfcObj];
                        foreach (var kvp in geomProps)
                        {
                            existingProperties[kvp.Key] = kvp.Value;
                        }
                    }
                }

                if (geomResult.HasGeometricDifference)
                {
                    _logger.LogInformation($"3D geometry difference detected in {newProduct.GetType().Name} '{newProduct.Name}' (#{((IPersistEntity)newProduct).EntityLabel}, GUID={newProduct.GlobalId}): Status={geomResult.Status}, VolDiff={geomResult.VolumeDiffM3:F6}m3, Displacement={geomResult.CentroidDisplacementMm:F2}mm");

                    if (ifcComparerResult != null && entity.EnableGhostOverlays)
                    {
                        lock (ifcComparerResult)
                        {
                            ifcComparerResult.ModifiedNewObjects.Add(newProduct);
                            ifcComparerResult.ModifiedOldGhostObjects.Add(oldProduct);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error comparing geometry for object #{((IPersistEntity)newIfcObj).EntityLabel}: {ex.Message}");
            }
        }

        // Helper method to compare and add property sets
        private void CompareAndAddPropertySets(
            IIfcObject newIfcObj,
            List<IIfcPropertySet> newPsets,
            List<IIfcPropertySet> oldPsets,
            Dictionary<IIfcObject, Dictionary<string, string>> result)
        {
            foreach (var newPset in newPsets)
            {
                var oldPset = oldPsets.FirstOrDefault(p => p?.Name != null && string.Equals(p.Name.ToString().Trim(), newPset.Name?.ToString().Trim(), StringComparison.OrdinalIgnoreCase));
                if (oldPset == null)
                    continue;

                // Compare the property sets
                var propertySetResult = CompareQAPropertySets(newPset.HasProperties, oldPset.HasProperties);

                // Add or merge into result
                if (!result.ContainsKey(newIfcObj))
                {
                    result[newIfcObj] = new Dictionary<string, string>(propertySetResult, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    var existingProperties = result[newIfcObj];

                    foreach (var kvp in propertySetResult)
                    {
                        if (!existingProperties.ContainsKey(kvp.Key))
                        {
                            existingProperties[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
        }

        private static string GetPropertyValue(IIfcProperty prop)
        {
            if (prop == null) return null;

            if (prop is IIfcPropertySingleValue singleVal)
            {
                return singleVal.NominalValue?.ToString();
            }
            if (prop is IIfcPropertyEnumeratedValue enumVal)
            {
                if (enumVal.EnumerationValues == null || !enumVal.EnumerationValues.Any())
                    return null;
                return string.Join(", ", enumVal.EnumerationValues.Select(v => v?.ToString()));
            }
            if (prop is IIfcPropertyListValue listVal)
            {
                if (listVal.ListValues == null || !listVal.ListValues.Any())
                    return null;
                return string.Join(", ", listVal.ListValues.Select(v => v?.ToString()));
            }
            if (prop is IIfcPropertyBoundedValue boundedVal)
            {
                return $"{boundedVal.LowerBoundValue?.ToString()} - {boundedVal.UpperBoundValue?.ToString()}";
            }
            return null;
        }

        private static bool ArePropertyValuesEqual(string val1, string val2)
        {
            if (val1 == null && val2 == null) return true;
            if (val1 == null || val2 == null) return false;

            var s1 = val1.Trim();
            var s2 = val2.Trim();

            if (string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase))
                return true;

            // Normalize comma to dot for numerical comparison (e.g. Norwegian / European locale)
            var n1 = s1.Replace(',', '.');
            var n2 = s2.Replace(',', '.');
            if (double.TryParse(n1, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d1) &&
                double.TryParse(n2, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d2))
            {
                return Math.Abs(d1 - d2) < 1e-6;
            }

            return false;
        }

        private Dictionary<string, string> CompareQAPropertySets(IItemSet<IIfcProperty> newPropertySet, IItemSet<IIfcProperty> oldPropertySet)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (newPropertySet == null && oldPropertySet == null)
                return result;

            if (newPropertySet != null)
            {
                foreach (var newProperty in newPropertySet)
                {
                    if (newProperty?.Name == null) continue;
                    var propName = newProperty.Name.ToString().Trim();
                    if (string.IsNullOrEmpty(propName)) continue;

                    IIfcProperty oldProperty = null;
                    if (oldPropertySet != null)
                    {
                        oldProperty = oldPropertySet
                            .FirstOrDefault(p => p?.Name != null && string.Equals(p.Name.ToString().Trim(), propName, StringComparison.OrdinalIgnoreCase));
                    }

                    var sValProp = GetPropertyValue(newProperty);
                    string valToWrite;

                    if (oldPropertySet != null)
                    {
                        if (oldProperty != null)
                        {
                            var oValProp = GetPropertyValue(oldProperty);

                            if (sValProp == null && oValProp == null)
                            {
                                valToWrite = "Equal";
                            }
                            else if (sValProp == null && oValProp != null)
                            {
                                valToWrite = $"Changed from \"{oValProp}\" to \"<null>\"";
                            }
                            else if (sValProp != null && oValProp == null)
                            {
                                valToWrite = $"Changed from \"<null>\" to \"{sValProp}\"";
                            }
                            else if (ArePropertyValuesEqual(sValProp, oValProp))
                            {
                                valToWrite = "Equal";
                            }
                            else
                            {
                                valToWrite = $"Changed from \"{oValProp}\" to \"{sValProp}\"";
                            }
                        }
                        else
                        {
                            valToWrite = $"Changed from \"<undefined>\" to \"{sValProp ?? "<null>"}\"";
                        }
                    }
                    else
                    {
                        valToWrite = "N/A";
                    }

                    result[propName] = valToWrite;
                }
            }

            // Detect properties that existed in the old revision but are missing from the new revision
            if (oldPropertySet != null)
            {
                foreach (var oldProperty in oldPropertySet)
                {
                    if (oldProperty?.Name == null) continue;
                    var propName = oldProperty.Name.ToString().Trim();
                    if (string.IsNullOrEmpty(propName)) continue;

                    if (!result.ContainsKey(propName))
                    {
                        var oldValue = GetPropertyValue(oldProperty) ?? "<null>";
                        result[propName] = $"Missing \"{oldValue}\"";
                    }
                }
            }

            return result;
        }

        private static string GetPropertyNominalValue(string comparisonOperator, IfcObjectStorage newObject)
        {
            // Try the per-object cache first (populated from ALL PSets by IfcComparerObjects).
            // This handles the case where the comparisonOperator property lives in a PSet
            // that is not listed in IfcPropertySets.
            if (newObject.ObjectComparisonIdCache != null && newObject.ObjectComparisonIdCache.Count > 0)
            {
                var firstObj = newObject.IfcObjects?.Values.FirstOrDefault();
                if (firstObj != null && newObject.ObjectComparisonIdCache.TryGetValue(firstObj, out var cached))
                    return cached ?? string.Empty;
            }

            // Fall back to scanning the stored PropertySet directly
            var idValue = (IIfcPropertySingleValue)newObject.PropertySet?.HasProperties
                .FirstOrDefault(prop => prop.Name.ToString().Contains(comparisonOperator));
            return idValue?.NominalValue?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Method to check if IfcObjects in the old list are not present in the new list based on a comparison operator.
        /// PERFORMANCE OPTIMIZED: Uses HashSet for O(1) lookups instead of O(n) nested loops
        /// </summary>
        /// <param name="oldObjects"></param>
        /// <param name="newObjects"></param>
        /// <param name="comparisonOperator"></param>
        /// <returns></returns>
        private async Task<List<IfcObjectStorage>> CheckIfIfcObjectsAreInIfcObjects(IfcComparerObjects oldObjects, IfcComparerObjects newObjects, string comparisonOperator, ComparisonEnumeration comparisonEnumeration, string newOld)
        {
            var result = new List<IfcObjectStorage>();

            if (comparisonEnumeration != ComparisonEnumeration.Identifier)
            {
                // PERFORMANCE IMPROVEMENT: Build HashSet of new nominal values ONCE - O(n) instead of O(nÃ‚Â²)
                var newNominalValues = new HashSet<string>(
                    newObjects.IfcStorageObjects
                        .Select(obj => GetPropertyNominalValue(comparisonOperator, obj))
                        .Where(val => !string.IsNullOrEmpty(val))
                );

                foreach (var oldObject in oldObjects.IfcStorageObjects)
                {
                    var oldIdNomValue = GetPropertyNominalValue(comparisonOperator, oldObject);
                    
                    // O(1) lookup instead of O(n) loop
                    if (!string.IsNullOrEmpty(oldIdNomValue) && !newNominalValues.Contains(oldIdNomValue))
                    {
                        result.Add(oldObject);
                        _logger.LogDebug($"Object with nominal value '{oldIdNomValue}' not found in {newOld} objects.");
                    }
                }
            }
            else
            {
                // PERFORMANCE IMPROVEMENT: Build HashSet of new GlobalIds ONCE
                var newGlobalIds = new HashSet<string>(
                    newObjects.IfcStorageObjects
                        .SelectMany(obj => obj.IfcObjects.Keys.Select(k => k.ToString()))
                );

                foreach (var oldObject in oldObjects.IfcStorageObjects)
                {
                    var oldIdNomValues = oldObject.IfcObjects.Keys.Select(k => k.ToString());
                    
                    // Check if any old GlobalId exists in new set
                    if (!oldIdNomValues.Any(id => newGlobalIds.Contains(id)))
                    {
                        result.Add(oldObject);
                        _logger.LogDebug($"Object not found in {newOld} objects.");
                    }
                }
            }

            _logger.LogInformation($"Found {result.Count} {newOld} object(s) not in the other model.");
            return await Task.FromResult(result);
        }
    }
}
