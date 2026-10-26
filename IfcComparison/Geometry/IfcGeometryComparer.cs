using System;
using System.Globalization;
using Xbim.Common.Geometry;

namespace IfcComparison.Geometry
{
    public class IfcGeometryComparer
    {
        public double VolumeToleranceM3 { get; set; } = 0.001; // 1 liter = 0.001 m³
        public double VolumePercentTolerance { get; set; } = 0.0; // 0.0 = only check VolumeToleranceM3 unless configured
        public double PositionToleranceMm { get; set; } = 1.0; // 1 mm
        public double DimensionToleranceMm { get; set; } = 1.0; // 1 mm
        public double AreaToleranceM2 { get; set; } = 0.01; // 1 dm² = 0.01 m²
        public double AreaPercentTolerance { get; set; } = 1.0; // 1%

        public GeometricComparisonResult Compare(GeometryMetrics oldMetrics, GeometryMetrics newMetrics)
        {
            var result = new GeometricComparisonResult();

            bool oldHasGeom = oldMetrics != null && oldMetrics.HasGeometry;
            bool newHasGeom = newMetrics != null && newMetrics.HasGeometry;

            if (!oldHasGeom && !newHasGeom)
            {
                result.Status = GeometryStatus.NoGeometry;
                return result;
            }

            if (!oldHasGeom && newHasGeom)
            {
                result.Status = GeometryStatus.AddedGeometry;
                result.VolumeNewM3 = newMetrics.Volume;
                result.VolumeDiffM3 = newMetrics.Volume;
                result.VolumeDiffMl = newMetrics.VolumeMilliliters;
                return result;
            }

            if (oldHasGeom && !newHasGeom)
            {
                result.Status = GeometryStatus.RemovedGeometry;
                result.VolumeOldM3 = oldMetrics.Volume;
                result.VolumeDiffM3 = -oldMetrics.Volume;
                result.VolumeDiffMl = -oldMetrics.VolumeMilliliters;
                return result;
            }

            // Both have geometry
            double vOld = oldMetrics.Volume;
            double vNew = newMetrics.Volume;
            double vDiff = vNew - vOld;
            double vDiffMl = vDiff * 1_000_000.0;

            result.VolumeOldM3 = vOld;
            result.VolumeNewM3 = vNew;
            result.VolumeDiffM3 = vDiff;
            result.VolumeDiffMl = vDiffMl;

            if (vOld > 1e-9)
            {
                result.VolumeChangePercent = (vDiff / vOld) * 100.0;
            }

            // Centroid displacement (in mm)
            var cOld = oldMetrics.Centroid;
            var cNew = newMetrics.Centroid;
            double dx = (cNew.X - cOld.X) * 1000.0;
            double dy = (cNew.Y - cOld.Y) * 1000.0;
            double dz = (cNew.Z - cOld.Z) * 1000.0;
            double displacementMm = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            result.CentroidDisplacementMm = displacementMm;

            // Bounding box dimension deltas and position displacement (in mm)
            var bOld = oldMetrics.BoundingBox;
            var bNew = newMetrics.BoundingBox;
            double dLength = 0;
            double dWidth = 0;
            double dHeight = 0;
            double bboxDisplacementMm = 0.0;
            bool bothHaveBoxes = !bOld.IsEmpty && !bNew.IsEmpty;

            if (bothHaveBoxes)
            {
                dLength = (bNew.SizeX - bOld.SizeX) * 1000.0;
                dWidth = (bNew.SizeY - bOld.SizeY) * 1000.0;
                dHeight = (bNew.SizeZ - bOld.SizeZ) * 1000.0;

                var bcOld = bOld.Centroid();
                var bcNew = bNew.Centroid();
                double bdx = (bcNew.X - bcOld.X) * 1000.0;
                double bdy = (bcNew.Y - bcOld.Y) * 1000.0;
                double bdz = (bcNew.Z - bcOld.Z) * 1000.0;
                bboxDisplacementMm = Math.Sqrt(bdx * bdx + bdy * bdy + bdz * bdz);
            }

            var culture = CultureInfo.InvariantCulture;
            result.BoundingBoxDiff = $"dX={dLength:+0.0;-0.0;0.0}mm, dY={dWidth:+0.0;-0.0;0.0}mm, dZ={dHeight:+0.0;-0.0;0.0}mm";

            // Determine if volume has changed:
            // - If relative percent tolerance is enabled (> 0) and the object has a valid baseline volume,
            //   flag VolumeChanged if the percentage difference meets or exceeds VolumePercentTolerance
            //   above a 1 ml (1e-6 m³) numerical noise floor.
            // - If percent tolerance is disabled (<= 0) or baseline volume cannot be computed,
            //   compare the absolute difference against VolumeToleranceM3.
            bool volumeChanged;
            if (VolumePercentTolerance > 0.0 && result.VolumeChangePercent.HasValue)
            {
                volumeChanged = Math.Abs(result.VolumeChangePercent.Value) >= VolumePercentTolerance &&
                                Math.Abs(vDiff) >= 1e-6; // 1 ml numerical noise floor
            }
            else
            {
                volumeChanged = Math.Abs(vDiff) > VolumeToleranceM3;
            }

            bool centroidMoved = displacementMm > PositionToleranceMm;
            bool bboxMoved = bothHaveBoxes && bboxDisplacementMm > PositionToleranceMm;
            // An element has physically moved in space if its centroid moved. If both elements have valid bounding boxes,
            // the bounding box must also have moved; if the bounding box is stationary (within tolerance),
            // any minor centroid shift is due to internal tessellation variation rather than physical translation.
            bool positionChanged = bothHaveBoxes ? (centroidMoved && bboxMoved) : centroidMoved;

            bool dimensionsChanged = bothHaveBoxes &&
                                     (Math.Abs(dLength) > DimensionToleranceMm ||
                                      Math.Abs(dWidth) > DimensionToleranceMm ||
                                      Math.Abs(dHeight) > DimensionToleranceMm);

            double areaDiff = Math.Abs(newMetrics.SurfaceArea - oldMetrics.SurfaceArea);
            double areaPct = oldMetrics.SurfaceArea > 1e-6 ? (areaDiff / oldMetrics.SurfaceArea) * 100.0 : 0.0;
            bool shapeChanged = areaDiff > AreaToleranceM2 && (AreaPercentTolerance <= 0.0 || areaPct >= AreaPercentTolerance);

            // An object is shown as Moved ONLY if its volume is completely equal.
            // If the volume actually changed, it must be classified as VolumeChanged.
            if (volumeChanged)
            {
                result.Status = GeometryStatus.VolumeChanged;
            }
            else if (positionChanged)
            {
                result.Status = GeometryStatus.Moved;
            }
            else if (dimensionsChanged)
            {
                result.Status = GeometryStatus.Resized;
            }
            else if (shapeChanged)
            {
                result.Status = GeometryStatus.ShapeChanged;
            }
            else
            {
                result.Status = GeometryStatus.Equal;
            }

            return result;
        }
    }
}
