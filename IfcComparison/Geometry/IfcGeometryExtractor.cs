using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IfcComparison.Logging;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;

namespace IfcComparison.Geometry
{
    public class IfcGeometryExtractor : IDisposable
    {
        private readonly IfcStore _model;
        private readonly ILogger _logger;
        private readonly double _oneMeter;
        private Xbim3DModelContext _context;
        private readonly ConcurrentDictionary<int, GeometryMetrics> _metricsCache = new();
        private bool _isContextCreated;

        public IfcGeometryExtractor(IfcStore model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _logger = LoggingService.CreateLogger<IfcGeometryExtractor>();
            _oneMeter = _model.ModelFactors?.OneMeter > 0 ? _model.ModelFactors.OneMeter : 1.0;
        }

        public void EnsureContextCreated()
        {
            if (_isContextCreated) return;

            try
            {
                _logger.LogInformation("Creating 3D geometry context for model (Schema: {Schema})...", _model.SchemaVersion);
                _context = new Xbim3DModelContext(_model);
                _context.CreateContext();
                _isContextCreated = true;
                _logger.LogInformation("3D geometry context successfully created.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create 3D geometry context: {Message}", ex.Message);
                throw;
            }
        }

        public GeometryMetrics ExtractMetrics(IIfcProduct product)
        {
            if (product == null)
                return GeometryMetrics.Empty;

            var entityLabel = ((IPersistEntity)product).EntityLabel;
            if (_metricsCache.TryGetValue(entityLabel, out var cached))
                return cached;

            EnsureContextCreated();

            try
            {
                var instances = GetProductAndChildrenShapeInstances(product);
                if (!instances.Any())
                {
                    var empty = GeometryMetrics.Empty;
                    _metricsCache[entityLabel] = empty;
                    return empty;
                }

                double combinedVolume = 0.0;
                double combinedArea = 0.0;
                double weightedCentroidX = 0.0;
                double weightedCentroidY = 0.0;
                double weightedCentroidZ = 0.0;
                int totalTriangles = 0;
                int totalVertices = 0;
                var totalBbox = XbimRect3D.Empty;

                foreach (var instance in instances)
                {
                    var shapeGeom = (IXbimShapeGeometryData)_context.ShapeGeometry(instance);
                    if (shapeGeom?.ShapeData == null || shapeGeom.ShapeData.Length == 0)
                        continue;

                    var metrics = TriangulationCalculator.ComputeMetrics(shapeGeom.ShapeData, instance.Transformation, _oneMeter);
                    if (!metrics.HasGeometry)
                        continue;

                    combinedVolume += metrics.Volume;
                    combinedArea += metrics.SurfaceArea;
                    totalTriangles += metrics.TriangleCount;
                    totalVertices += metrics.VertexCount;

                    if (totalBbox.IsEmpty)
                        totalBbox = metrics.BoundingBox;
                    else
                        totalBbox.Union(metrics.BoundingBox);

                    weightedCentroidX += metrics.Centroid.X * metrics.Volume;
                    weightedCentroidY += metrics.Centroid.Y * metrics.Volume;
                    weightedCentroidZ += metrics.Centroid.Z * metrics.Volume;
                }

                XbimPoint3D finalCentroid;
                if (combinedVolume > 1e-12)
                {
                    finalCentroid = new XbimPoint3D(
                        weightedCentroidX / combinedVolume,
                        weightedCentroidY / combinedVolume,
                        weightedCentroidZ / combinedVolume
                    );
                }
                else if (!totalBbox.IsEmpty)
                {
                    finalCentroid = totalBbox.Centroid();
                }
                else
                {
                    finalCentroid = XbimPoint3D.Zero;
                }

                // Centroid Sanity Check: Combined centroid must lie within the combined bounding box.
                if (!totalBbox.IsEmpty)
                {
                    double maxDim = Math.Max(totalBbox.SizeX, Math.Max(totalBbox.SizeY, totalBbox.SizeZ));
                    double tol = Math.Max(0.01, 0.05 * maxDim);
                    if (finalCentroid.X < totalBbox.X - tol || finalCentroid.X > totalBbox.X + totalBbox.SizeX + tol ||
                        finalCentroid.Y < totalBbox.Y - tol || finalCentroid.Y > totalBbox.Y + totalBbox.SizeY + tol ||
                        finalCentroid.Z < totalBbox.Z - tol || finalCentroid.Z > totalBbox.Z + totalBbox.SizeZ + tol)
                    {
                        finalCentroid = totalBbox.Centroid();
                    }
                }

                var result = new GeometryMetrics
                {
                    HasGeometry = totalTriangles > 0,
                    Volume = combinedVolume,
                    SurfaceArea = combinedArea,
                    Centroid = finalCentroid,
                    BoundingBox = totalBbox,
                    TriangleCount = totalTriangles,
                    VertexCount = totalVertices
                };

                _metricsCache[entityLabel] = result;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting geometry metrics for EntityLabel #{Label}: {Message}", entityLabel, ex.Message);
                var empty = GeometryMetrics.Empty;
                _metricsCache[entityLabel] = empty;
                return empty;
            }
        }

        public GeometryMetrics ExtractMetrics(int entityLabel)
        {
            if (_metricsCache.TryGetValue(entityLabel, out var cached))
                return cached;

            var product = _model.Instances[entityLabel] as IIfcProduct;
            if (product == null)
            {
                var empty = GeometryMetrics.Empty;
                _metricsCache[entityLabel] = empty;
                return empty;
            }

            return ExtractMetrics(product);
        }

        private List<XbimShapeInstance> GetProductAndChildrenShapeInstances(IIfcProduct product)
        {
            var instances = _context.ShapeInstancesOf(product).ToList();
            if (instances.Any())
                return instances;

            // If the product itself has no direct shape instances (e.g. IfcElementAssembly or composite element),
            // recursively gather shape instances from its decomposed children.
            var visited = new HashSet<int> { ((IPersistEntity)product).EntityLabel };
            CollectChildrenShapeInstances(product, instances, visited);
            return instances;
        }

        private void CollectChildrenShapeInstances(IIfcProduct product, List<XbimShapeInstance> instances, HashSet<int> visited)
        {
            if (product.IsDecomposedBy == null) return;

            foreach (var rel in product.IsDecomposedBy)
            {
                if (rel?.RelatedObjects == null) continue;

                foreach (var child in rel.RelatedObjects.OfType<IIfcProduct>())
                {
                    var childLabel = ((IPersistEntity)child).EntityLabel;
                    if (!visited.Add(childLabel)) continue;

                    var childInstances = _context.ShapeInstancesOf(child).ToList();
                    instances.AddRange(childInstances);

                    // Recurse if child also decomposes further
                    CollectChildrenShapeInstances(child, instances, visited);
                }
            }
        }

        public void Dispose()
        {
            _metricsCache.Clear();
        }
    }
}
