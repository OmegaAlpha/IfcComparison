using IfcComparison.Geometry;
using IfcComparison.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcComparison.Models
{
    public class IfcWriter
    {
        public IfcComparerResult IfcComparisonResult { get; private set; }
        public XbimSchemaVersion IfcSchemaVersion { get; private set; }
        public string FilePath { get; set; }
        private readonly ILogger<IfcWriter> _logger;

        public IfcWriter(IfcComparerResult ifcComparerResult, XbimSchemaVersion xbimSchemaVersion, string filePath) 
        { 
            IfcComparisonResult = ifcComparerResult;
            IfcSchemaVersion = xbimSchemaVersion;
            FilePath = filePath;
            _logger = LoggingService.CreateLogger<IfcWriter>();
        }

        public async Task<bool> WriteToFileAsync(IfcStore ifcModelQA, Dictionary<Xbim.Ifc4.Interfaces.IIfcObject, string> objectPSetMap)
        {
            bool isWritten = false;
            int processedCount = 0;
            int errorCount = 0;
            const int batchSize = 500; // Commit every 500 objects to avoid ESENT memory issues

            return await Task.Run(() =>
            {
                try
                {
                    var totalCount = IfcComparisonResult.ComparedIfcObjects.Count;
                    _logger.LogInformation($"Starting to write {totalCount} objects to file in batches of {batchSize}...");
                    
                    var objectsList = IfcComparisonResult.ComparedIfcObjects.ToList();
                    int batchNumber = 0;

                    for (int i = 0; i < objectsList.Count; i += batchSize)
                    {
                        batchNumber++;
                        var batch = objectsList.Skip(i).Take(batchSize).ToList();
                        
                        using (var transaction = ifcModelQA.BeginTransaction($"Save IFC Comparison Result - Batch {batchNumber}"))
                        {
                            foreach (var ifcObj in batch)
                            {
                                var ifcObject = ifcObj.Key;
                                var properties = ifcObj.Value;
                                
                                try
                                {
                                    // Validate that the object belongs to this model
                                    var entityLabel = ((IPersistEntity)ifcObject).EntityLabel;
                                    var modelEntity = ifcModelQA.Instances[entityLabel];
                                    
                                    if (modelEntity == null)
                                    {
                                        _logger.LogWarning($"Object with EntityLabel {entityLabel} not found in target model, skipping...");
                                        errorCount++;
                                        continue;
                                    }

                                    // Use the correct PSetName for this object or fallback to a default
                                    string pSetName = "QA_PSET";
                                    if (objectPSetMap.TryGetValue(ifcObject, out var mappedPSetName))
                                    {
                                        pSetName = mappedPSetName;
                                    }

                                    IfcTools.GeneratePropertySetIfc(ifcModelQA, ifcObject, properties, pSetName, IfcSchemaVersion);
                                    processedCount++;
                                    
                                    if (processedCount % 1000 == 0)
                                    {
                                        _logger.LogInformation($"Processed {processedCount}/{totalCount} objects...");
                                    }
                                }
                                catch (Exception objEx)
                                {
                                    var entityLabel = ((IPersistEntity)ifcObject).EntityLabel;
                                    _logger.LogWarning($"Error processing object {entityLabel}: {objEx.Message}");
                                    errorCount++;
                                    // Continue with next object instead of failing entirely
                                }
                            }
                            
                            transaction.Commit();
                            _logger.LogDebug($"Committed batch {batchNumber} with {batch.Count} objects");
                        }
                    }

                    _logger.LogInformation($"All batches committed. Total: {processedCount} objects processed, {errorCount} errors.");

                    // Apply 3D visual styles and ghost overlays
                    ApplyGhostOverlays(ifcModelQA);

                    _logger.LogInformation($"Saving file to {FilePath}...");
                    ifcModelQA.SaveAs(FilePath);
                    isWritten = true;
                    _logger.LogInformation($"File saved successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error saving IFC file: {ex.Message}");
                    throw new InvalidOperationException($"Error saving IFC file: {ex.Message}", ex);
                }
                return isWritten;
            });
        }

        // Keep the original method for backward compatibility
        public async Task<bool> WriteToFileAsync(IfcStore ifcModelQA, string pSetName)
        {
            bool isWritten = false;
            int processedCount = 0;
            int errorCount = 0;
            const int batchSize = 500; // Commit every 500 objects to avoid ESENT memory issues

            return await Task.Run(() =>
            {
                try
                {
                    var totalCount = IfcComparisonResult.ComparedIfcObjects.Count;
                    _logger.LogInformation($"Starting to write {totalCount} objects to file in batches of {batchSize}...");
                    
                    var objectsList = IfcComparisonResult.ComparedIfcObjects.ToList();
                    int batchNumber = 0;

                    for (int i = 0; i < objectsList.Count; i += batchSize)
                    {
                        batchNumber++;
                        var batch = objectsList.Skip(i).Take(batchSize).ToList();
                        
                        using (var transaction = ifcModelQA.BeginTransaction($"Save IFC Comparison Result - Batch {batchNumber}"))
                        {
                            foreach (var ifcObj in batch)
                            {
                                var ifcObject = ifcObj.Key;
                                var properties = ifcObj.Value;

                                try
                                {
                                    // Validate that the object belongs to this model
                                    var entityLabel = ((IPersistEntity)ifcObject).EntityLabel;
                                    var modelEntity = ifcModelQA.Instances[entityLabel];
                                    
                                    if (modelEntity == null)
                                    {
                                        _logger.LogWarning($"Object with EntityLabel {entityLabel} not found in target model, skipping...");
                                        errorCount++;
                                        continue;
                                    }

                                    IfcTools.GeneratePropertySetIfc(ifcModelQA, ifcObject, properties, pSetName, IfcSchemaVersion);
                                    processedCount++;
                                    
                                    if (processedCount % 1000 == 0)
                                    {
                                        _logger.LogInformation($"Processed {processedCount}/{totalCount} objects...");
                                    }
                                }
                                catch (Exception objEx)
                                {
                                    var entityLabel = ((IPersistEntity)ifcObject).EntityLabel;
                                    _logger.LogWarning($"Error processing object {entityLabel}: {objEx.Message}");
                                    errorCount++;
                                }
                            }
                            
                            transaction.Commit();
                            _logger.LogDebug($"Committed batch {batchNumber} with {batch.Count} objects");
                        }
                    }

                    _logger.LogInformation($"All batches committed. Total: {processedCount} objects processed, {errorCount} errors.");

                    // Apply 3D visual styles and ghost overlays
                    ApplyGhostOverlays(ifcModelQA);

                    _logger.LogInformation($"Saving file to {FilePath}...");
                    ifcModelQA.SaveAs(FilePath);
                    isWritten = true;
                    _logger.LogInformation($"File saved successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error saving IFC file: {ex.Message}");
                    throw new InvalidOperationException($"Error saving IFC file: {ex.Message}", ex);
                }
                return isWritten;
            });
        }

        public void ApplyGhostOverlays(IfcStore ifcModelQA)
        {
            if (IfcComparisonResult == null) return;

            bool hasModifiedNew = IfcComparisonResult.ModifiedNewObjects != null && IfcComparisonResult.ModifiedNewObjects.Any();
            bool hasInserted = IfcComparisonResult.InsertedObjects != null && IfcComparisonResult.InsertedObjects.Any();
            bool hasModifiedOld = IfcComparisonResult.ModifiedOldGhostObjects != null && IfcComparisonResult.ModifiedOldGhostObjects.Any();
            bool hasDeleted = IfcComparisonResult.DeletedGhostObjects != null && IfcComparisonResult.DeletedGhostObjects.Any();

            // Find any property-only changed products (objects with modified metadata but no 3D geometry change)
            var propertyOnlyProducts = new List<IIfcProduct>();
            if (IfcComparisonResult.ModifiedPropertyOnlyObjects != null && IfcComparisonResult.ModifiedPropertyOnlyObjects.Any())
            {
                foreach (var prod in IfcComparisonResult.ModifiedPropertyOnlyObjects)
                {
                    var entityLabel = ((IPersistEntity)prod).EntityLabel;
                    var modelProd = ifcModelQA.Instances[entityLabel] as IIfcProduct;
                    if (modelProd != null)
                    {
                        propertyOnlyProducts.Add(modelProd);
                    }
                }
            }
            else if (IfcComparisonResult.ComparedIfcObjects != null)
            {
                foreach (var kvp in IfcComparisonResult.ComparedIfcObjects)
                {
                    var obj = kvp.Key;
                    var props = kvp.Value;

                    // Check if any property actually changed (ignoring QA geometry evaluation and identical values)
                    bool hasActualPropertyDiff = props != null && props.Any(p => IfcTools.IsActualPropertyChange(p.Key, p.Value));

                    if (hasActualPropertyDiff &&
                        obj is IIfcProduct prod &&
                        (IfcComparisonResult.ModifiedNewObjects == null || !IfcComparisonResult.ModifiedNewObjects.Contains(prod)) &&
                        (IfcComparisonResult.InsertedObjects == null || !IfcComparisonResult.InsertedObjects.Contains(prod)))
                    {
                        var entityLabel = ((IPersistEntity)prod).EntityLabel;
                        var modelProd = ifcModelQA.Instances[entityLabel] as IIfcProduct;
                        if (modelProd != null)
                        {
                            propertyOnlyProducts.Add(modelProd);
                        }
                    }
                }
            }

            bool hasPropertyOnly = propertyOnlyProducts.Any();

            if (!hasModifiedNew && !hasInserted && !hasModifiedOld && !hasDeleted && !hasPropertyOnly)
                return;

            _logger.LogInformation("Organizing model tree and applying 3D visual styles and ghost overlays...");
            var ghostService = new IfcGhostService();

            using (var txn = ifcModelQA.BeginTransaction("Apply 3D Ghost Overlays and Model Tree Structure"))
            {
                // 1. Style & Move Modified New Objects (Solid Yellow) -> "QA - Changed (New)"
                if (hasModifiedNew)
                {
                    var storeyModifiedNew = ghostService.GetOrCreateStorey(ifcModelQA, "QA - Changed (New)");
                    var modifiedNewList = new List<IIfcProduct>();

                    foreach (var prod in IfcComparisonResult.ModifiedNewObjects)
                    {
                        var entityLabel = ((IPersistEntity)prod).EntityLabel;
                        var modelProd = ifcModelQA.Instances[entityLabel] as IIfcProduct;
                        if (modelProd != null)
                        {
                            ghostService.ApplyStyle(ifcModelQA, modelProd, GhostStyleType.ModifiedNew);
                            ghostService.MoveProductToStorey(ifcModelQA, modelProd, storeyModifiedNew);
                            modifiedNewList.Add(modelProd);
                        }
                    }

                    if (modifiedNewList.Any())
                    {
                        ghostService.CreateGroup(ifcModelQA, "QA - Changed (New)", modifiedNewList);
                        _logger.LogInformation("Moved {Count} modified (new) objects to storey 'QA - Changed (New)'", modifiedNewList.Count);
                    }
                }

                // 2. Style & Move Inserted Objects (Solid Green) -> "QA - Added (New)"
                if (hasInserted)
                {
                    var storeyInserted = ghostService.GetOrCreateStorey(ifcModelQA, "QA - Added (New)");
                    var insertedList = new List<IIfcProduct>();

                    foreach (var prod in IfcComparisonResult.InsertedObjects)
                    {
                        var entityLabel = ((IPersistEntity)prod).EntityLabel;
                        var modelProd = ifcModelQA.Instances[entityLabel] as IIfcProduct;
                        if (modelProd != null)
                        {
                            ghostService.ApplyStyle(ifcModelQA, modelProd, GhostStyleType.Inserted);
                            ghostService.MoveProductToStorey(ifcModelQA, modelProd, storeyInserted);
                            insertedList.Add(modelProd);
                        }
                    }

                    if (insertedList.Any())
                    {
                        ghostService.CreateGroup(ifcModelQA, "QA - Added (New)", insertedList);
                        _logger.LogInformation("Moved {Count} inserted (new) objects to storey 'QA - Added (New)'", insertedList.Count);
                    }
                }

                // 3. Insert & Link Modified Old Ghost Objects (Translucent Purple) -> "QA - Changed (Old Ghost)"
                if (hasModifiedOld)
                {
                    var storeyModifiedOld = ghostService.GetOrCreateStorey(ifcModelQA, "QA - Changed (Old Ghost)");
                    var modifiedOldList = new List<IIfcProduct>();

                    int count = 0;
                    int total = IfcComparisonResult.ModifiedOldGhostObjects.Count;
                    _logger.LogInformation($"Cloning {total} modified old ghost objects...");

                    foreach (var oldProd in IfcComparisonResult.ModifiedOldGhostObjects)
                    {
                        var ghost = ghostService.InsertGhostProduct(ifcModelQA, oldProd, GhostStyleType.ModifiedOld, "[GHOST_MODIFIED]", storeyModifiedOld);
                        if (ghost != null)
                        {
                            modifiedOldList.Add(ghost);
                        }
                        count++;
                        if (count % 250 == 0 || count == total)
                        {
                            _logger.LogInformation($"Cloned {count}/{total} modified old ghost objects...");
                        }
                    }

                    if (modifiedOldList.Any())
                    {
                        ghostService.CreateGroup(ifcModelQA, "QA - Changed (Old Ghost)", modifiedOldList);
                        _logger.LogInformation("Inserted {Count} modified old ghost objects into storey 'QA - Changed (Old Ghost)'", modifiedOldList.Count);
                    }
                }

                // 4. Insert & Link Deleted Ghost Objects (Translucent Red) -> "QA - Deleted (Ghost)"
                if (hasDeleted)
                {
                    var storeyDeleted = ghostService.GetOrCreateStorey(ifcModelQA, "QA - Deleted (Ghost)");
                    var deletedList = new List<IIfcProduct>();

                    int count = 0;
                    int total = IfcComparisonResult.DeletedGhostObjects.Count;
                    _logger.LogInformation($"Cloning {total} deleted ghost objects...");

                    foreach (var oldProd in IfcComparisonResult.DeletedGhostObjects)
                    {
                        var ghost = ghostService.InsertGhostProduct(ifcModelQA, oldProd, GhostStyleType.Deleted, "[GHOST_DELETED]", storeyDeleted);
                        if (ghost != null)
                        {
                            deletedList.Add(ghost);
                        }
                        count++;
                        if (count % 250 == 0 || count == total)
                        {
                            _logger.LogInformation($"Cloned {count}/{total} deleted ghost objects...");
                        }
                    }

                    if (deletedList.Any())
                    {
                        ghostService.CreateGroup(ifcModelQA, "QA - Deleted (Ghost)", deletedList);
                        _logger.LogInformation("Inserted {Count} deleted ghost objects into storey 'QA - Deleted (Ghost)'", deletedList.Count);
                    }
                }

                // 5. Move Property-Only Changed Objects -> "QA - Changed (Properties)"
                if (hasPropertyOnly)
                {
                    var storeyProps = ghostService.GetOrCreateStorey(ifcModelQA, "QA - Changed (Properties)");
                    foreach (var prod in propertyOnlyProducts)
                    {
                        ghostService.MoveProductToStorey(ifcModelQA, prod, storeyProps);
                    }
                    ghostService.CreateGroup(ifcModelQA, "QA - Changed (Properties)", propertyOnlyProducts);
                    _logger.LogInformation("Moved {Count} property-changed objects to storey 'QA - Changed (Properties)'", propertyOnlyProducts.Count);
                }

                txn.Commit();
            }

            _logger.LogInformation("Model tree organization and 3D visual styles applied successfully.");
        }
    }
}
