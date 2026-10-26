using System;
using Xbim.Common.Geometry;

namespace IfcComparison.Geometry
{
    public class GeometryMetrics
    {
        public bool HasGeometry { get; set; }
        public double Volume { get; set; } // in cubic meters (m³)
        public double SurfaceArea { get; set; } // in square meters (m²)
        public XbimPoint3D Centroid { get; set; } // in world coordinates (meters)
        public XbimRect3D BoundingBox { get; set; } // in world coordinates (meters)
        public int TriangleCount { get; set; }
        public int VertexCount { get; set; }

        public double VolumeMilliliters => Volume * 1_000_000.0;
        public double VolumeLiters => Volume * 1_000.0;

        public static GeometryMetrics Empty => new GeometryMetrics
        {
            HasGeometry = false,
            Volume = 0,
            SurfaceArea = 0,
            Centroid = XbimPoint3D.Zero,
            BoundingBox = XbimRect3D.Empty,
            TriangleCount = 0,
            VertexCount = 0
        };
    }
}
