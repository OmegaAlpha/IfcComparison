using System;
using System.IO;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;

namespace IfcComparison.Geometry
{
    public static class TriangulationCalculator
    {
        /// <summary>
        /// Computes geometric metrics (Volume, Area, Centroid, BoundingBox) from an XbimShapeTriangulation
        /// transformed to world coordinates via the specified instance transformation matrix.
        /// When oneMeter is specified (e.g. from model.ModelFactors.OneMeter), coordinates are scaled to meters.
        /// Volume is calculated using the discrete divergence theorem (signed tetrahedral volume summation).
        /// To prevent catastrophic floating-point cancellation in high-coordinate systems (e.g. UTM / GIS),
        /// coordinates are shifted to a local origin before computing signed tetrahedral volumes and moments.
        /// </summary>
        public static GeometryMetrics ComputeMetrics(XbimShapeTriangulation triangulation, XbimMatrix3D transform, double oneMeter = 1.0)
        {
            if (triangulation == null || triangulation.Vertices == null || triangulation.Vertices.Count == 0 ||
                triangulation.Faces == null || triangulation.Faces.Count == 0)
            {
                return GeometryMetrics.Empty;
            }

            if (oneMeter <= 0)
                oneMeter = 1.0;

            double scale = 1.0 / oneMeter;

            // Pre-transform all vertices to world coordinates in meters once for speed
            var worldVertices = new XbimPoint3D[triangulation.Vertices.Count];
            for (int i = 0; i < triangulation.Vertices.Count; i++)
            {
                var pt = transform.Transform(triangulation.Vertices[i]);
                worldVertices[i] = new XbimPoint3D(pt.X * scale, pt.Y * scale, pt.Z * scale);
            }

            // Use the first transformed vertex as a local reference origin to eliminate catastrophic
            // floating-point cancellation when coordinates are large (e.g. UTM / GIS map coordinates).
            var origin = worldVertices[0];
            var localVertices = new XbimPoint3D[worldVertices.Length];
            for (int i = 0; i < worldVertices.Length; i++)
            {
                localVertices[i] = new XbimPoint3D(
                    worldVertices[i].X - origin.X,
                    worldVertices[i].Y - origin.Y,
                    worldVertices[i].Z - origin.Z
                );
            }

            double totalSignedVolume = 0.0;
            double totalArea = 0.0;
            double weightedCentroidX = 0.0;
            double weightedCentroidY = 0.0;
            double weightedCentroidZ = 0.0;
            int totalTriangles = 0;

            var bbox = XbimRect3D.Empty;

            foreach (var face in triangulation.Faces)
            {
                if (face.Indices == null || face.Indices.Count < 3)
                    continue;

                int count = face.Indices.Count;
                for (int i = 0; i <= count - 3; i += 3)
                {
                    int idx1 = face.Indices[i];
                    int idx2 = face.Indices[i + 1];
                    int idx3 = face.Indices[i + 2];

                    if (idx1 >= worldVertices.Length || idx2 >= worldVertices.Length || idx3 >= worldVertices.Length)
                        continue;

                    var wa = worldVertices[idx1];
                    var wb = worldVertices[idx2];
                    var wc = worldVertices[idx3];

                    if (bbox.IsEmpty)
                        bbox = new XbimRect3D(wa);
                    bbox.Union(wa);
                    bbox.Union(wb);
                    bbox.Union(wc);

                    var a = localVertices[idx1];
                    var b = localVertices[idx2];
                    var c = localVertices[idx3];

                    // Signed volume of tetrahedron formed by local origin (0,0,0) and triangle (a, b, c):
                    // v_tet = (1/6) * dot(a, cross(b, c))
                    double crossX = b.Y * c.Z - b.Z * c.Y;
                    double crossY = b.Z * c.X - b.X * c.Z;
                    double crossZ = b.X * c.Y - b.Y * c.X;
                    double vTet = (a.X * crossX + a.Y * crossY + a.Z * crossZ) / 6.0;

                    totalSignedVolume += vTet;

                    // Triangle area: 0.5 * |(b - a) x (c - a)|
                    double abX = b.X - a.X; double abY = b.Y - a.Y; double abZ = b.Z - a.Z;
                    double acX = c.X - a.X; double acY = c.Y - a.Y; double acZ = c.Z - a.Z;
                    double fCrossX = abY * acZ - abZ * acY;
                    double fCrossY = abZ * acX - abX * acZ;
                    double fCrossZ = abX * acY - abY * acX;
                    double triArea = Math.Sqrt(fCrossX * fCrossX + fCrossY * fCrossY + fCrossZ * fCrossZ) * 0.5;
                    totalArea += triArea;

                    // Weighted centroid component (each tetrahedron centroid is at (a + b + c) / 4 in local coordinates)
                    weightedCentroidX += (a.X + b.X + c.X) * 0.25 * vTet;
                    weightedCentroidY += (a.Y + b.Y + c.Y) * 0.25 * vTet;
                    weightedCentroidZ += (a.Z + b.Z + c.Z) * 0.25 * vTet;

                    totalTriangles++;
                }
            }

            double finalVolume = Math.Abs(totalSignedVolume);
            XbimPoint3D finalCentroid;

            if (finalVolume > 1e-12 && Math.Abs(totalSignedVolume) > 1e-12)
            {
                var localCentroid = new XbimPoint3D(
                    weightedCentroidX / totalSignedVolume,
                    weightedCentroidY / totalSignedVolume,
                    weightedCentroidZ / totalSignedVolume
                );

                finalCentroid = new XbimPoint3D(
                    origin.X + localCentroid.X,
                    origin.Y + localCentroid.Y,
                    origin.Z + localCentroid.Z
                );

                // Centroid Sanity Check: The centroid of any solid body must lie inside its bounding box.
                // If a non-watertight mesh or inverted normal artifact pushes the centroid outside
                // the bounding box (with a small relative tolerance), fall back to the bounding box centroid.
                if (!bbox.IsEmpty)
                {
                    double maxDim = Math.Max(bbox.SizeX, Math.Max(bbox.SizeY, bbox.SizeZ));
                    double tol = Math.Max(0.01, 0.05 * maxDim);
                    if (finalCentroid.X < bbox.X - tol || finalCentroid.X > bbox.X + bbox.SizeX + tol ||
                        finalCentroid.Y < bbox.Y - tol || finalCentroid.Y > bbox.Y + bbox.SizeY + tol ||
                        finalCentroid.Z < bbox.Z - tol || finalCentroid.Z > bbox.Z + bbox.SizeZ + tol)
                    {
                        finalCentroid = bbox.Centroid();
                    }
                }
            }
            else if (!bbox.IsEmpty)
            {
                finalCentroid = bbox.Centroid();
            }
            else
            {
                finalCentroid = XbimPoint3D.Zero;
            }

            return new GeometryMetrics
            {
                HasGeometry = totalTriangles > 0,
                Volume = finalVolume,
                SurfaceArea = totalArea,
                Centroid = finalCentroid,
                BoundingBox = bbox,
                TriangleCount = totalTriangles,
                VertexCount = worldVertices.Length
            };
        }

        /// <summary>
        /// Reads an XbimShapeTriangulation from raw shape geometry bytes and computes its metrics.
        /// </summary>
        public static GeometryMetrics ComputeMetrics(byte[] shapeData, XbimMatrix3D transform, double oneMeter = 1.0)
        {
            if (shapeData == null || shapeData.Length == 0)
                return GeometryMetrics.Empty;

            try
            {
                using var ms = new MemoryStream(shapeData);
                using var br = new BinaryReader(ms);
                var triangulation = br.ReadShapeTriangulation();
                return ComputeMetrics(triangulation, transform, oneMeter);
            }
            catch
            {
                return GeometryMetrics.Empty;
            }
        }
    }
}
