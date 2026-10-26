using System.Collections.Generic;
using System.Linq;
using IfcComparison.Models;
using Xunit;

namespace IfcComparison.Tests
{
    public class PropertyComparisonTests
    {
        [Theory]
        [InlineData("QA_GeometryStatus", true)]
        [InlineData("QA_Volume_Old_m3", true)]
        [InlineData("QA_Volume_New_m3", true)]
        [InlineData("QA_Volume_Diff_m3", true)]
        [InlineData("QA_Volume_Diff_ml", true)]
        [InlineData("QA_Volume_Change_Percent", true)]
        [InlineData("QA_Centroid_Displacement_mm", true)]
        [InlineData("QA_BoundingBox_Change", true)]
        [InlineData("QA_3D_CustomMetric", true)]
        [InlineData("Length", false)]
        [InlineData("Profile", false)]
        [InlineData("Material", false)]
        [InlineData("LoadBearing", false)]
        [InlineData("FireRating", false)]
        public void IsToolGeneratedQaProperty_CorrectlyIdentifiesGeometryMetrics(string key, bool expected)
        {
            // Act
            bool result = IfcTools.IsToolGeneratedQaProperty(key);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        // Unchanged / neutral values
        [InlineData("Length", "Equal", false)]
        [InlineData("Length", "equal", false)]
        [InlineData("Profile", "Null", false)]
        [InlineData("Profile", "null", false)]
        [InlineData("Material", "N/A", false)]
        [InlineData("Material", "", false)]
        [InlineData("Material", null, false)]
        // Geometry metrics must NOT be treated as property changes (the core bug)
        [InlineData("QA_GeometryStatus", "Equal", false)]
        [InlineData("QA_GeometryStatus", "VolumeChanged", false)]
        [InlineData("QA_Volume_Old_m3", "1.234567", false)]
        [InlineData("QA_Volume_New_m3", "1.234567", false)]
        [InlineData("QA_Volume_Diff_m3", "+0.000000", false)]
        [InlineData("QA_Volume_Diff_ml", "+0.00", false)]
        [InlineData("QA_Volume_Change_Percent", "+0.00%", false)]
        [InlineData("QA_Centroid_Displacement_mm", "0.00", false)]
        [InlineData("QA_Centroid_Displacement_mm", "12.50", false)]
        [InlineData("QA_BoundingBox_Change", "Old: (0,0,0) New: (0,0,0)", false)]
        // Real property deltas
        [InlineData("Length", "Changed from \"1000\" to \"1200\"", true)]
        [InlineData("Material", "Changed from \"Concrete\" to \"Steel\"", true)]
        [InlineData("FireRating", "Changed from \"<undefined>\" to \"EI30\"", true)]
        [InlineData("OldProperty", "Missing \"SomeValue\"", true)]
        public void IsActualPropertyChange_EvaluatesCorrectly(string key, string value, bool expected)
        {
            // Act
            bool result = IfcTools.IsActualPropertyChange(key, value);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ObjectWithOnlyGeometryMetricsAndEqualProperties_IsNotFlaggedAsPropertyChange()
        {
            // Arrange - simulate ComparedIfcObjects for an object whose geometry and properties are equal
            var properties = new Dictionary<string, string>
            {
                { "Length", "Equal" },
                { "Profile", "Equal" },
                { "Material", "Equal" },
                { "QA_GeometryStatus", "Equal" },
                { "QA_Volume_Old_m3", "0.045000" },
                { "QA_Volume_New_m3", "0.045000" },
                { "QA_Volume_Diff_m3", "+0.000000" },
                { "QA_Volume_Diff_ml", "+0.00" },
                { "QA_Volume_Change_Percent", "+0.00%" },
                { "QA_Centroid_Displacement_mm", "0.00" }
            };

            // Act
            bool hasActualChange = properties.Any(p => IfcTools.IsActualPropertyChange(p.Key, p.Value));

            // Assert
            Assert.False(hasActualChange, "An object with identical geometry metrics and equal properties must NOT be flagged as property-changed!");
        }

        [Fact]
        public void ObjectWithRealPropertyChange_IsFlaggedAsPropertyChange()
        {
            // Arrange - simulate ComparedIfcObjects for an object with a real property change
            var properties = new Dictionary<string, string>
            {
                { "Length", "Equal" },
                { "Profile", "Changed from \"IPE200\" to \"IPE300\"" },
                { "Material", "Equal" },
                { "QA_GeometryStatus", "Equal" },
                { "QA_Volume_Old_m3", "0.045000" },
                { "QA_Volume_New_m3", "0.045000" }
            };

            // Act
            bool hasActualChange = properties.Any(p => IfcTools.IsActualPropertyChange(p.Key, p.Value));

            // Assert
            Assert.True(hasActualChange, "An object with a changed property must be flagged as property-changed!");
        }

        [Fact]
        public void ObjectWithMissingProperty_IsFlaggedAsPropertyChange()
        {
            // Arrange - simulate ComparedIfcObjects for an object with a missing property
            var properties = new Dictionary<string, string>
            {
                { "Length", "Equal" },
                { "OldNote", "Missing \"Review required\"" },
                { "QA_GeometryStatus", "Equal" }
            };

            // Act
            bool hasActualChange = properties.Any(p => IfcTools.IsActualPropertyChange(p.Key, p.Value));

            // Assert
            Assert.True(hasActualChange, "An object with a missing property in the new revision must be flagged as property-changed!");
        }
    }
}
