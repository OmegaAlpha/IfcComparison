using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using IfcComparison.Logging;
using IfcComparison.Models;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcComparison.Geometry
{
    public enum GhostStyleType
    {
        Deleted,         // Translucent Red
        Inserted,        // Solid Green
        ModifiedNew,     // Solid Yellow
        ModifiedOld,     // Translucent Purple
        PropertyChanged  // Solid Cyan/Blue
    }

    public class IfcGhostService
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, IPersistEntity> _styleCache = new();

        public IfcGhostService()
        {
            _logger = LoggingService.CreateLogger<IfcGhostService>();
        }

        /// <summary>
        /// Creates or retrieves a cached IfcSurfaceStyle with the specified RGBA values.
        /// </summary>
        public IPersistEntity GetOrCreateStyle(IfcStore model, GhostStyleType styleType)
        {
            string cacheKey = $"{model.SchemaVersion}_{styleType}";
            if (_styleCache.TryGetValue(cacheKey, out var cachedStyle))
                return cachedStyle;

            double r, g, b, transparency;
            string name;

            switch (styleType)
            {
                case GhostStyleType.Deleted:
                    r = 1.0; g = 0.1; b = 0.1; transparency = 0.5; name = "QA_Style_Deleted";
                    break;
                case GhostStyleType.Inserted:
                    r = 0.1; g = 0.85; b = 0.2; transparency = 0.0; name = "QA_Style_Inserted";
                    break;
                case GhostStyleType.ModifiedNew:
                    r = 1.0; g = 0.7; b = 0.0; transparency = 0.0; name = "QA_Style_ModifiedNew";
                    break;
                case GhostStyleType.PropertyChanged:
                    r = 0.1; g = 0.6; b = 0.95; transparency = 0.0; name = "QA_Style_PropertyChanged";
                    break;
                case GhostStyleType.ModifiedOld:
                default:
                    r = 0.6; g = 0.2; b = 0.85; transparency = 0.6; name = "QA_Style_ModifiedOldGhost";
                    break;
            }

            IPersistEntity style;
            if (model.SchemaVersion == XbimSchemaVersion.Ifc2X3)
            {
                style = CreateIfc2x3Style(model, name, r, g, b, transparency);
            }
            else
            {
                style = CreateIfc4Style(model, name, r, g, b, transparency);
            }

            _styleCache[cacheKey] = style;
            return style;
        }

        private IPersistEntity CreateIfc4Style(IfcStore model, string name, double r, double g, double b, double transparency)
        {
            var rgb = model.Instances.New<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb>(c =>
            {
                c.Red = r;
                c.Green = g;
                c.Blue = b;
            });

            var rendering = model.Instances.New<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyleRendering>(rend =>
            {
                rend.SurfaceColour = rgb;
                rend.Transparency = new Xbim.Ifc4.MeasureResource.IfcNormalisedRatioMeasure(transparency);
            });

            var surfaceStyle = model.Instances.New<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle>(s =>
            {
                s.Name = name;
                s.Side = Xbim.Ifc4.Interfaces.IfcSurfaceSide.BOTH;
                s.Styles.Add(rendering);
            });

            var assignment = model.Instances.New<Xbim.Ifc4.PresentationAppearanceResource.IfcPresentationStyleAssignment>(a =>
            {
                a.Styles.Add(surfaceStyle);
            });

            return assignment;
        }

        private IPersistEntity CreateIfc2x3Style(IfcStore model, string name, double r, double g, double b, double transparency)
        {
            var rgb = model.Instances.New<Xbim.Ifc2x3.PresentationResource.IfcColourRgb>(c =>
            {
                c.Red = r;
                c.Green = g;
                c.Blue = b;
            });

            var rendering = model.Instances.New<Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyleRendering>(rend =>
            {
                rend.SurfaceColour = rgb;
                rend.Transparency = new Xbim.Ifc2x3.MeasureResource.IfcNormalisedRatioMeasure(transparency);
            });

            var surfaceStyle = model.Instances.New<Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyle>(s =>
            {
                s.Name = name;
                s.Side = Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceSide.BOTH;
                s.Styles.Add(rendering);
            });

            var assignment = model.Instances.New<Xbim.Ifc2x3.PresentationAppearanceResource.IfcPresentationStyleAssignment>(a =>
            {
                a.Styles.Add(surfaceStyle);
            });

            return assignment;
        }

        /// <summary>
        /// Applies the presentation style to all representation items of an IfcProduct.
        /// </summary>
        public void ApplyStyle(IfcStore model, IIfcProduct product, GhostStyleType styleType)
        {
            if (product == null) return;

            // If product decomposes children (e.g. assembly), also apply style to each child product
            if (product.IsDecomposedBy != null)
            {
                foreach (var rel in product.IsDecomposedBy)
                {
                    if (rel?.RelatedObjects == null) continue;
                    foreach (var child in rel.RelatedObjects.OfType<IIfcProduct>())
                    {
                        ApplyStyle(model, child, styleType);
                    }
                }
            }

            if (product.Representation?.Representations == null)
                return;

            try
            {
                var styleAssignment = GetOrCreateStyle(model, styleType);

                foreach (var rep in product.Representation.Representations)
                {
                    if (rep?.Items == null) continue;

                    foreach (var item in rep.Items)
                    {
                        if (model.SchemaVersion == XbimSchemaVersion.Ifc2X3)
                        {
                            var ifc2x3Item = item as Xbim.Ifc2x3.GeometryResource.IfcRepresentationItem;
                            var ifc2x3Style = styleAssignment as Xbim.Ifc2x3.PresentationAppearanceResource.IfcPresentationStyleAssignment;
                            if (ifc2x3Item != null && ifc2x3Style != null)
                            {
                                var existing = ifc2x3Item.StyledByItem?.ToList();
                                if (existing != null && existing.Any())
                                {
                                    foreach (var s in existing)
                                    {
                                        s.Styles.Clear();
                                        s.Styles.Add(ifc2x3Style);
                                    }
                                }
                                else
                                {
                                    model.Instances.New<Xbim.Ifc2x3.PresentationAppearanceResource.IfcStyledItem>(styled =>
                                    {
                                        styled.Item = ifc2x3Item;
                                        styled.Styles.Add(ifc2x3Style);
                                    });
                                }
                            }
                        }
                        else
                        {
                            var ifc4Item = item as Xbim.Ifc4.GeometryResource.IfcRepresentationItem;
                            var ifc4Style = styleAssignment as Xbim.Ifc4.PresentationAppearanceResource.IfcPresentationStyleAssignment;
                            if (ifc4Item != null && ifc4Style != null)
                            {
                                var existing = ifc4Item.StyledByItem?.ToList();
                                if (existing != null && existing.Any())
                                {
                                    foreach (var s in existing)
                                    {
                                        s.Styles.Clear();
                                        s.Styles.Add(ifc4Style);
                                    }
                                }
                                else
                                {
                                    model.Instances.New<Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem>(styled =>
                                    {
                                        styled.Item = ifc4Item;
                                        styled.Styles.Add(ifc4Style);
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to apply style {StyleType} to product #{Label}: {Message}",
                    styleType, ((IPersistEntity)product).EntityLabel, ex.Message);
            }
        }

        /// <summary>
        /// Clones a source product from another model into targetModel, applies ghost styling,
        /// and links it to the spatial structure (or specified target storey).
        /// </summary>
        public IIfcProduct InsertGhostProduct(
            IfcStore targetModel,
            IIfcProduct sourceProduct,
            GhostStyleType ghostStyle,
            string namePrefix = "[GHOST]",
            IIfcSpatialStructureElement targetSpatialContainer = null)
        {
            if (sourceProduct == null) return null;

            try
            {
                var map = new XbimInstanceHandleMap(sourceProduct.Model, targetModel);
                var copiedEntity = targetModel.InsertCopy(sourceProduct, map, null, false, false);
                if (copiedEntity is not IIfcProduct ghostProduct)
                {
                    return null;
                }

                // Assign a new GUID to avoid duplicate GlobalId collisions with active elements
                ghostProduct.GlobalId = Guid.NewGuid();

                // Prefix name for clarity in IFC viewers
                if (ghostProduct.Name != null)
                {
                    ghostProduct.Name = $"{namePrefix} {ghostProduct.Name}";
                }
                else
                {
                    ghostProduct.Name = $"{namePrefix} {ghostProduct.GetType().Name}";
                }

                // Apply ghost presentation style (translucent red or purple)
                ApplyStyle(targetModel, ghostProduct, ghostStyle);

                // If source product decomposes children (e.g. assembly), clone and link children so ghost has 3D geometry
                if (sourceProduct.IsDecomposedBy != null)
                {
                    var childGhosts = new List<IIfcProduct>();
                    foreach (var rel in sourceProduct.IsDecomposedBy)
                    {
                        if (rel?.RelatedObjects == null) continue;
                        foreach (var child in rel.RelatedObjects.OfType<IIfcProduct>())
                        {
                            var childCopy = targetModel.InsertCopy(child, map, null, false, false);
                            if (childCopy is IIfcProduct childGhost)
                            {
                                childGhost.GlobalId = Guid.NewGuid();
                                ApplyStyle(targetModel, childGhost, ghostStyle);
                                childGhosts.Add(childGhost);
                            }
                        }
                    }

                    if (childGhosts.Any())
                    {
                        LinkChildrenToGhostAssembly(targetModel, ghostProduct, childGhosts);
                    }
                }

                // Ensure it is linked to the spatial hierarchy (target storey or fallback)
                if (targetSpatialContainer != null)
                {
                    AttachProductToStorey(targetModel, ghostProduct, targetSpatialContainer);
                }
                else
                {
                    LinkToSpatialStructure(targetModel, ghostProduct);
                }

                // Attach QA_GHOST property set with ghost tracking metadata
                var ghostProps = new Dictionary<string, string>
                {
                    { "QA_GhostType", ghostStyle.ToString() },
                    { "QA_OriginalGlobalId", sourceProduct.GlobalId.ToString() }
                };
                IfcTools.GeneratePropertySetIfc(targetModel, ghostProduct, ghostProps, "QA_GHOST", targetModel.SchemaVersion);

                return ghostProduct;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to insert ghost product from #{SourceLabel}: {Message}",
                    ((IPersistEntity)sourceProduct).EntityLabel, ex.Message);
                return null;
            }
        }

        private void LinkChildrenToGhostAssembly(IfcStore targetModel, IIfcProduct ghostAssembly, List<IIfcProduct> childGhosts)
        {
            try
            {
                if (targetModel.SchemaVersion == XbimSchemaVersion.Ifc2X3)
                {
                    if (ghostAssembly is Xbim.Ifc2x3.Kernel.IfcObjectDefinition parent2x3)
                    {
                        targetModel.Instances.New<Xbim.Ifc2x3.Kernel.IfcRelAggregates>(r =>
                        {
                            r.RelatingObject = parent2x3;
                            foreach (var c in childGhosts.OfType<Xbim.Ifc2x3.Kernel.IfcObjectDefinition>())
                            {
                                r.RelatedObjects.Add(c);
                            }
                        });
                    }
                }
                else
                {
                    if (ghostAssembly is Xbim.Ifc4.Kernel.IfcObjectDefinition parent4)
                    {
                        targetModel.Instances.New<Xbim.Ifc4.Kernel.IfcRelAggregates>(r =>
                        {
                            r.RelatingObject = parent4;
                            foreach (var c in childGhosts.OfType<Xbim.Ifc4.Kernel.IfcObjectDefinition>())
                            {
                                r.RelatedObjects.Add(c);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to link child ghosts to assembly: {Message}", ex.Message);
            }
        }

        private void LinkToSpatialStructure(IfcStore targetModel, IIfcProduct product)
        {
            // Check if already linked
            if (product.Decomposes?.Any() == true) return;

            var spatialElement = targetModel.Instances.OfType<IIfcSpatialStructureElement>().FirstOrDefault();
            if (spatialElement == null) return;

            if (targetModel.SchemaVersion == XbimSchemaVersion.Ifc2X3)
            {
                var rel = targetModel.Instances.OfType<Xbim.Ifc2x3.ProductExtension.IfcRelContainedInSpatialStructure>()
                    .FirstOrDefault(r => r.RelatingStructure == spatialElement);

                if (rel != null && product is Xbim.Ifc2x3.Kernel.IfcProduct prod2x3)
                {
                    rel.RelatedElements.Add(prod2x3);
                }
                else if (product is Xbim.Ifc2x3.Kernel.IfcProduct p2 && spatialElement is Xbim.Ifc2x3.ProductExtension.IfcSpatialStructureElement s2)
                {
                    targetModel.Instances.New<Xbim.Ifc2x3.ProductExtension.IfcRelContainedInSpatialStructure>(r =>
                    {
                        r.RelatingStructure = s2;
                        r.RelatedElements.Add(p2);
                    });
                }
            }
            else
            {
                var rel = targetModel.Instances.OfType<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>()
                    .FirstOrDefault(r => r.RelatingStructure == spatialElement);

                if (rel != null && product is Xbim.Ifc4.Kernel.IfcProduct prod4)
                {
                    rel.RelatedElements.Add(prod4);
                }
                else if (product is Xbim.Ifc4.Kernel.IfcProduct p4 && spatialElement is Xbim.Ifc4.ProductExtension.IfcSpatialStructureElement s4)
                {
                    targetModel.Instances.New<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>(r =>
                    {
                        r.RelatingStructure = s4;
                        r.RelatedElements.Add(p4);
                    });
                }
            }
        }

        /// <summary>
        /// Finds or creates a dedicated IfcBuildingStorey with the specified name under the model's building/site.
        /// Used to isolate comparison categories (Changed, Added, Deleted) in BIM viewer model trees (e.g. Solibri).
        /// </summary>
        public IIfcSpatialStructureElement GetOrCreateStorey(IfcStore targetModel, string storeyName)
        {
            if (targetModel == null || string.IsNullOrWhiteSpace(storeyName)) return null;

            // 1. Check if a storey with this name already exists
            var existing = targetModel.Instances.OfType<IIfcBuildingStorey>()
                .FirstOrDefault(s => string.Equals(s.Name?.ToString(), storeyName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing;

            try
            {
                if (targetModel.SchemaVersion == XbimSchemaVersion.Ifc2X3)
                {
                    var building = targetModel.Instances.OfType<Xbim.Ifc2x3.ProductExtension.IfcBuilding>().FirstOrDefault();
                    var site = targetModel.Instances.OfType<Xbim.Ifc2x3.ProductExtension.IfcSite>().FirstOrDefault();
                    var project = targetModel.Instances.OfType<Xbim.Ifc2x3.Kernel.IfcProject>().FirstOrDefault();

                    var parent = (Xbim.Ifc2x3.Kernel.IfcObjectDefinition)building ??
                                 (Xbim.Ifc2x3.Kernel.IfcObjectDefinition)site ??
                                 (Xbim.Ifc2x3.Kernel.IfcObjectDefinition)project;

                    var storey2x3 = targetModel.Instances.New<Xbim.Ifc2x3.ProductExtension.IfcBuildingStorey>(s =>
                    {
                        s.Name = storeyName;
                        s.CompositionType = Xbim.Ifc2x3.ProductExtension.IfcElementCompositionEnum.ELEMENT;
                    });

                    if (parent != null)
                    {
                        var agg = targetModel.Instances.OfType<Xbim.Ifc2x3.Kernel.IfcRelAggregates>()
                            .FirstOrDefault(r => r.RelatingObject == parent);
                        if (agg != null)
                        {
                            agg.RelatedObjects.Add(storey2x3);
                        }
                        else
                        {
                            targetModel.Instances.New<Xbim.Ifc2x3.Kernel.IfcRelAggregates>(r =>
                            {
                                r.RelatingObject = parent;
                                r.RelatedObjects.Add(storey2x3);
                            });
                        }
                    }

                    return storey2x3;
                }
                else
                {
                    var building = targetModel.Instances.OfType<Xbim.Ifc4.ProductExtension.IfcBuilding>().FirstOrDefault();
                    var site = targetModel.Instances.OfType<Xbim.Ifc4.ProductExtension.IfcSite>().FirstOrDefault();
                    var project = targetModel.Instances.OfType<Xbim.Ifc4.Kernel.IfcProject>().FirstOrDefault();

                    var parent = (Xbim.Ifc4.Kernel.IfcObjectDefinition)building ??
                                 (Xbim.Ifc4.Kernel.IfcObjectDefinition)site ??
                                 (Xbim.Ifc4.Kernel.IfcObjectDefinition)project;

                    var storey4 = targetModel.Instances.New<Xbim.Ifc4.ProductExtension.IfcBuildingStorey>(s =>
                    {
                        s.Name = storeyName;
                        s.CompositionType = Xbim.Ifc4.Interfaces.IfcElementCompositionEnum.ELEMENT;
                    });

                    if (parent != null)
                    {
                        var agg = targetModel.Instances.OfType<Xbim.Ifc4.Kernel.IfcRelAggregates>()
                            .FirstOrDefault(r => r.RelatingObject == parent);
                        if (agg != null)
                        {
                            agg.RelatedObjects.Add(storey4);
                        }
                        else
                        {
                            targetModel.Instances.New<Xbim.Ifc4.Kernel.IfcRelAggregates>(r =>
                            {
                                r.RelatingObject = parent;
                                r.RelatedObjects.Add(storey4);
                            });
                        }
                    }

                    return storey4;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to create storey '{StoreyName}': {Message}", storeyName, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Moves an IFC product into the specified spatial structure element (e.g. IfcBuildingStorey),
        /// removing it from its previous spatial containment so it appears in the new location in the model tree.
        /// Also cleans up child parts of assemblies so they nest cleanly under the assembly.
        /// </summary>
        public void MoveProductToStorey(IfcStore targetModel, IIfcProduct product, IIfcSpatialStructureElement targetStorey)
        {
            if (product == null || targetStorey == null) return;

            try
            {
                if (targetModel.SchemaVersion == XbimSchemaVersion.Ifc2X3)
                {
                    if (product is Xbim.Ifc2x3.Kernel.IfcProduct p2x3 &&
                        targetStorey is Xbim.Ifc2x3.ProductExtension.IfcSpatialStructureElement s2x3)
                    {
                        // 1. Remove product from any existing spatial container
                        RemoveSpatialContainment(targetModel, p2x3);

                        // 2. Add product to targetStorey's IfcRelContainedInSpatialStructure
                        var rel = targetModel.Instances.OfType<Xbim.Ifc2x3.ProductExtension.IfcRelContainedInSpatialStructure>()
                            .FirstOrDefault(r => r.RelatingStructure == s2x3);
                        if (rel != null)
                        {
                            if (!rel.RelatedElements.Contains(p2x3))
                                rel.RelatedElements.Add(p2x3);
                        }
                        else
                        {
                            targetModel.Instances.New<Xbim.Ifc2x3.ProductExtension.IfcRelContainedInSpatialStructure>(r =>
                            {
                                r.RelatingStructure = s2x3;
                                r.RelatedElements.Add(p2x3);
                            });
                        }

                        // 3. For any decomposed children, remove separate spatial links so they nest cleanly under the parent assembly
                        RemoveChildrenSpatialContainment(targetModel, product);
                    }
                }
                else
                {
                    if (product is Xbim.Ifc4.Kernel.IfcProduct p4 &&
                        targetStorey is Xbim.Ifc4.ProductExtension.IfcSpatialStructureElement s4)
                    {
                        // 1. Remove product from any existing spatial container
                        RemoveSpatialContainment(targetModel, p4);

                        // 2. Add product to targetStorey's IfcRelContainedInSpatialStructure
                        var rel = targetModel.Instances.OfType<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>()
                            .FirstOrDefault(r => r.RelatingStructure == s4);
                        if (rel != null)
                        {
                            if (!rel.RelatedElements.Contains(p4))
                                rel.RelatedElements.Add(p4);
                        }
                        else
                        {
                            targetModel.Instances.New<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>(r =>
                            {
                                r.RelatingStructure = s4;
                                r.RelatedElements.Add(p4);
                            });
                        }

                        // 3. For any decomposed children, remove separate spatial links so they nest cleanly under the parent assembly
                        RemoveChildrenSpatialContainment(targetModel, product);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to move product #{Label} to storey '{Storey}': {Message}",
                    ((IPersistEntity)product).EntityLabel, targetStorey.Name, ex.Message);
            }
        }

        private void RemoveSpatialContainment(IfcStore targetModel, IIfcProduct product)
        {
            if (targetModel.SchemaVersion == XbimSchemaVersion.Ifc2X3)
            {
                if (product is Xbim.Ifc2x3.Kernel.IfcProduct p2x3)
                {
                    var existingRels = targetModel.Instances.OfType<Xbim.Ifc2x3.ProductExtension.IfcRelContainedInSpatialStructure>()
                        .Where(r => r.RelatedElements.Contains(p2x3))
                        .ToList();
                    foreach (var er in existingRels)
                    {
                        er.RelatedElements.Remove(p2x3);
                    }
                }
            }
            else
            {
                if (product is Xbim.Ifc4.Kernel.IfcProduct p4)
                {
                    var existingRels = targetModel.Instances.OfType<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>()
                        .Where(r => r.RelatedElements.Contains(p4))
                        .ToList();
                    foreach (var er in existingRels)
                    {
                        er.RelatedElements.Remove(p4);
                    }
                }
            }
        }

        /// <summary>
        /// Attaches an IFC product directly to the specified spatial structure element (e.g. IfcBuildingStorey)
        /// without scanning and removing previous spatial relationships. Used for newly created ghost elements.
        /// </summary>
        public void AttachProductToStorey(IfcStore targetModel, IIfcProduct product, IIfcSpatialStructureElement targetStorey)
        {
            if (product == null || targetStorey == null) return;

            try
            {
                if (targetModel.SchemaVersion == XbimSchemaVersion.Ifc2X3)
                {
                    if (product is Xbim.Ifc2x3.Kernel.IfcProduct p2x3 &&
                        targetStorey is Xbim.Ifc2x3.ProductExtension.IfcSpatialStructureElement s2x3)
                    {
                        var rel = targetModel.Instances.OfType<Xbim.Ifc2x3.ProductExtension.IfcRelContainedInSpatialStructure>()
                            .FirstOrDefault(r => r.RelatingStructure == s2x3);
                        if (rel != null)
                        {
                            rel.RelatedElements.Add(p2x3);
                        }
                        else
                        {
                            targetModel.Instances.New<Xbim.Ifc2x3.ProductExtension.IfcRelContainedInSpatialStructure>(r =>
                            {
                                r.RelatingStructure = s2x3;
                                r.RelatedElements.Add(p2x3);
                            });
                        }
                    }
                }
                else
                {
                    if (product is Xbim.Ifc4.Kernel.IfcProduct p4 &&
                        targetStorey is Xbim.Ifc4.ProductExtension.IfcSpatialStructureElement s4)
                    {
                        var rel = targetModel.Instances.OfType<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>()
                            .FirstOrDefault(r => r.RelatingStructure == s4);
                        if (rel != null)
                        {
                            rel.RelatedElements.Add(p4);
                        }
                        else
                        {
                            targetModel.Instances.New<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>(r =>
                            {
                                r.RelatingStructure = s4;
                                r.RelatedElements.Add(p4);
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to attach product #{Label} to storey '{Storey}': {Message}",
                    ((IPersistEntity)product).EntityLabel, targetStorey.Name, ex.Message);
            }
        }

        private void RemoveChildrenSpatialContainment(IfcStore targetModel, IIfcProduct product, HashSet<int> visited = null)
        {
            if (product.IsDecomposedBy == null) return;

            visited ??= new HashSet<int>();
            var label = ((IPersistEntity)product).EntityLabel;
            if (!visited.Add(label)) return;

            foreach (var decomp in product.IsDecomposedBy)
            {
                if (decomp?.RelatedObjects == null) continue;
                foreach (var child in decomp.RelatedObjects.OfType<IIfcProduct>())
                {
                    var childLabel = ((IPersistEntity)child).EntityLabel;
                    if (!visited.Contains(childLabel))
                    {
                        RemoveSpatialContainment(targetModel, child);
                        RemoveChildrenSpatialContainment(targetModel, child, visited);
                    }
                }
            }
        }

        /// <summary>
        /// Creates an IfcGroup containing the specified products via IfcRelAssignsToGroup.
        /// Enables grouping in BIM viewers (e.g. Solibri Groups view).
        /// </summary>
        public void CreateGroup(IfcStore targetModel, string groupName, IEnumerable<IIfcProduct> products)
        {
            if (products == null || !products.Any()) return;

            try
            {
                if (targetModel.SchemaVersion == XbimSchemaVersion.Ifc2X3)
                {
                    var group = targetModel.Instances.New<Xbim.Ifc2x3.Kernel.IfcGroup>(g => g.Name = groupName);
                    targetModel.Instances.New<Xbim.Ifc2x3.Kernel.IfcRelAssignsToGroup>(r =>
                    {
                        r.RelatingGroup = group;
                        foreach (var p in products.OfType<Xbim.Ifc2x3.Kernel.IfcProduct>())
                        {
                            r.RelatedObjects.Add(p);
                        }
                    });
                }
                else
                {
                    var group = targetModel.Instances.New<Xbim.Ifc4.Kernel.IfcGroup>(g => g.Name = groupName);
                    targetModel.Instances.New<Xbim.Ifc4.Kernel.IfcRelAssignsToGroup>(r =>
                    {
                        r.RelatingGroup = group;
                        foreach (var p in products.OfType<Xbim.Ifc4.Kernel.IfcProduct>())
                        {
                            r.RelatedObjects.Add(p);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to create group '{GroupName}': {Message}", groupName, ex.Message);
            }
        }
    }
}
