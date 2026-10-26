using System;
using System.Collections.Generic;
using System.Globalization;

namespace IfcComparison.Geometry
{
    public enum GeometryStatus
    {
        Equal,
        VolumeChanged,
        Moved,
        Resized,
        ShapeChanged,
        AddedGeometry,
        RemovedGeometry,
        NoGeometry
    }

    public class GeometricComparisonResult
    {
        public GeometryStatus Status { get; set; } = GeometryStatus.Equal;
        public double? VolumeOldM3 { get; set; }
        public double? VolumeNewM3 { get; set; }
        public double? VolumeDiffM3 { get; set; }
        public double? VolumeDiffMl { get; set; }
        public double? VolumeChangePercent { get; set; }
        public double? CentroidDisplacementMm { get; set; }
        public string BoundingBoxDiff { get; set; }

        public bool HasGeometricDifference => Status != GeometryStatus.Equal && Status != GeometryStatus.NoGeometry;

        public Dictionary<string, string> ToPropertiesDictionary()
        {
            var props = new Dictionary<string, string>();
            var culture = CultureInfo.InvariantCulture;

            props["QA_GeometryStatus"] = Status.ToString();

            if (VolumeOldM3.HasValue)
                props["QA_Volume_Old_m3"] = VolumeOldM3.Value.ToString("F6", culture);

            if (VolumeNewM3.HasValue)
                props["QA_Volume_New_m3"] = VolumeNewM3.Value.ToString("F6", culture);

            if (VolumeDiffM3.HasValue)
            {
                var sign = VolumeDiffM3.Value >= 0 ? "+" : "";
                props["QA_Volume_Diff_m3"] = $"{sign}{VolumeDiffM3.Value.ToString("F6", culture)}";
            }

            if (VolumeDiffMl.HasValue)
            {
                var sign = VolumeDiffMl.Value >= 0 ? "+" : "";
                props["QA_Volume_Diff_ml"] = $"{sign}{VolumeDiffMl.Value.ToString("F2", culture)}";
            }

            if (VolumeChangePercent.HasValue)
            {
                var sign = VolumeChangePercent.Value >= 0 ? "+" : "";
                props["QA_Volume_Change_Percent"] = $"{sign}{VolumeChangePercent.Value.ToString("F2", culture)}%";
            }

            if (CentroidDisplacementMm.HasValue)
                props["QA_Centroid_Displacement_mm"] = CentroidDisplacementMm.Value.ToString("F2", culture);

            if (!string.IsNullOrEmpty(BoundingBoxDiff))
                props["QA_BoundingBox_Change"] = BoundingBoxDiff;

            return props;
        }
    }
}
