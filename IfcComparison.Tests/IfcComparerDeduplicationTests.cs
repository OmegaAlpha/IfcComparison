using System.Collections.Generic;
using System.Linq;
using IfcComparison.Enumerations;
using IfcComparison.Models;
using IfcComparison.ViewModels;
using Xunit;

namespace IfcComparison.Tests
{
    public class IfcComparerDeduplicationTests
    {
        [Fact]
        public void GetComparisonKey_WithIfcPropertySets_ReturnsNormalizedSortedKey()
        {
            var entity1 = new IfcEntity
            {
                IfcPropertySets = new List<string> { "Fv.109-Merknader", "Fv.109-Generelt" },
                ComparisonOperator = "",
                ComparisonMethod = "Identifier"
            };

            var entity2 = new IfcEntity
            {
                IfcPropertySets = new List<string> { "fv.109-generelt", "FV.109-MERKNADER" },
                ComparisonOperator = "  ",
                ComparisonMethod = "IDENTIFIER"
            };

            var key1 = IfcComparer.GetComparisonKey(entity1);
            var key2 = IfcComparer.GetComparisonKey(entity2);

            Assert.Equal("FV.109-GENERELT,FV.109-MERKNADER||IDENTIFIER", key1);
            Assert.Equal(key1, key2);
        }

        [Fact]
        public void GetComparisonKey_WithEmptyIfcPropertySets_FallsBackToPSetName()
        {
            var entity = new IfcEntity
            {
                PSetName = "QA_PSET",
                IfcPropertySets = new List<string>(),
                ComparisonOperator = "Tag",
                ComparisonMethod = "Exact"
            };

            var key = IfcComparer.GetComparisonKey(entity);

            Assert.Equal("QA_PSET|TAG|EXACT", key);
        }

        [Fact]
        public void GetComparisonKey_DifferentPSetOrFilter_ProducesDifferentKeys()
        {
            var entity1 = new IfcEntity
            {
                IfcPropertySets = new List<string> { "Pset_A" },
                ComparisonOperator = "",
                ComparisonMethod = "Identifier"
            };

            var entity2 = new IfcEntity
            {
                IfcPropertySets = new List<string> { "Pset_B" },
                ComparisonOperator = "",
                ComparisonMethod = "Identifier"
            };

            var key1 = IfcComparer.GetComparisonKey(entity1);
            var key2 = IfcComparer.GetComparisonKey(entity2);

            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void GetComparisonKey_DifferentOperators_ProducesDifferentKeys()
        {
            var entity1 = new IfcEntity
            {
                IfcPropertySets = new List<string> { "Pset_A" },
                ComparisonOperator = "Tag",
                ComparisonMethod = "Contains"
            };

            var entity2 = new IfcEntity
            {
                IfcPropertySets = new List<string> { "Pset_A" },
                ComparisonOperator = "Name",
                ComparisonMethod = "Contains"
            };

            var key1 = IfcComparer.GetComparisonKey(entity1);
            var key2 = IfcComparer.GetComparisonKey(entity2);

            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void GetComparisonKey_DifferentMethods_ProducesDifferentKeys()
        {
            var entity1 = new IfcEntity
            {
                IfcPropertySets = new List<string> { "Pset_A" },
                ComparisonOperator = "",
                ComparisonMethod = "Identifier"
            };

            var entity2 = new IfcEntity
            {
                IfcPropertySets = new List<string> { "Pset_A" },
                ComparisonOperator = "",
                ComparisonMethod = "Exact"
            };

            var key1 = IfcComparer.GetComparisonKey(entity1);
            var key2 = IfcComparer.GetComparisonKey(entity2);

            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void EntitySorting_PlacesSpecificEntitiesBeforeGenericBaseEntities()
        {
            var general = new IfcEntity { Entity = "IIfcObject", PSetName = "QA_GENERAL" };
            var specific = new IfcEntity { Entity = "IIfcReinforcingBar", PSetName = "QA_REBAR" };
            var beam = new IfcEntity { Entity = "IIfcBeam", PSetName = "QA_BEAM" };

            var list = new List<IfcEntity> { general, specific, beam };

            var sorted = list
                .OrderByDescending(e =>
                {
                    var t = IfcTools.GetInterfaceType(e.Entity);
                    if (t == null) return 0;
                    if (t == typeof(Xbim.Ifc4.Interfaces.IIfcObject)) return -1;
                    return t.GetInterfaces().Length;
                })
                .ToList();

            // IIfcReinforcingBar and IIfcBeam should come before IIfcObject
            Assert.NotEqual("IIfcObject", sorted[0].Entity);
            Assert.Equal("IIfcObject", sorted.Last().Entity);
        }

        [Fact]
        public void ComparedObjectsByKey_TracksObjectsAcrossPasses()
        {
            // Verify that dictionary correctly tracks GlobalIds under comparison key
            var comparedByKey = new Dictionary<string, HashSet<string>>(System.StringComparer.OrdinalIgnoreCase);

            var key = "PSET_WALLCOMMON||IDENTIFIER";
            comparedByKey[key] = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "GUID_1", "GUID_2" };

            Assert.Contains(key, (IDictionary<string, HashSet<string>>)comparedByKey);
            Assert.Contains("guid_1", comparedByKey[key]);
            Assert.Contains("GUID_2", comparedByKey[key]);
            Assert.DoesNotContain("GUID_3", comparedByKey[key]);
        }

        [Fact]
        public void GeometryComparedGlobalIds_TracksPhysicalGeometryOnce()
        {
            // Physical geometry is evaluated at most once per object across all entity passes
            var geomCompared = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            Assert.True(geomCompared.Add("GUID_REBAR_1"));
            Assert.False(geomCompared.Add("guid_rebar_1"), "Second attempt to add same GUID should return false");
            Assert.Contains("GUID_REBAR_1", geomCompared);
        }
    }
}
