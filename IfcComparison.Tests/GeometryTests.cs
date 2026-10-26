using System;
using System.Linq;
using IfcComparison.Geometry;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Common.Step21;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.IO;
using Xbim.ModelGeometry.Scene;
using Xunit;

namespace IfcComparison.Tests
{
    public class GeometryTests
    {
        private static XbimShapeTriangulation CreateCubeTriangulation(double dx, double dy, double dz)
        {
            double hx = dx / 2.0;
            double hy = dy / 2.0;
            double hz = dz / 2.0;

            var vertices = new System.Collections.Generic.List<XbimPoint3D>
            {
                new XbimPoint3D(-hx, -hy, -hz), // 0
                new XbimPoint3D( hx, -hy, -hz), // 1
                new XbimPoint3D( hx,  hy, -hz), // 2
                new XbimPoint3D(-hx,  hy, -hz), // 3
                new XbimPoint3D(-hx, -hy,  hz), // 4
                new XbimPoint3D( hx, -hy,  hz), // 5
                new XbimPoint3D( hx,  hy,  hz), // 6
                new XbimPoint3D(-hx,  hy,  hz)  // 7
            };

            var face = new XbimFaceTriangulation(12, 0);
            int[] indices = new int[]
            {
                // Bottom (-Z)
                0, 2, 1,  0, 3, 2,
                // Top (+Z)
                4, 5, 6,  4, 6, 7,
                // Front (-Y)
                0, 1, 5,  0, 5, 4,
                // Back (+Y)
                3, 6, 2,  3, 7, 6,
                // Left (-X)
                0, 4, 7,  0, 7, 3,
                // Right (+X)
                1, 2, 6,  1, 6, 5
            };

            foreach (var idx in indices)
            {
                face.Indices.Add(idx);
            }

            var faces = new System.Collections.Generic.List<XbimFaceTriangulation> { face };
            return new XbimShapeTriangulation(vertices, faces, 1);
        }

        [Fact]
        public void BarGroup_HighCoordinates_DoesNotProduceFalsePositiveMoved()
        {
            string oldPath = @"C:\Users\gg\OneDrive - Multiconsult\Oppdrag\Fv 109\Konstruksjon\06-RIBKO KS\K10\2026-10-13 TG rev. 3\f-bru_k10-raabekken-gangkulvert-armering.ifc";
            string newPath = @"C:\TeklaStructuresModels\Models\K10-\IFC\Edited\f-bru_k10-raabekken-gangkulvert-armering.ifc";

            if (!System.IO.File.Exists(oldPath) || !System.IO.File.Exists(newPath))
                return;

            using var oldStore = IfcStore.Open(oldPath);
            using var newStore = IfcStore.Open(newPath);

            using var oldExtractor = new IfcGeometryExtractor(oldStore);
            using var newExtractor = new IfcGeometryExtractor(newStore);

            string targetGuid = "3Bw56jaA1D29AftmQQ$$Ad";
            var oldBar = oldStore.Instances.OfType<Xbim.Ifc4.Interfaces.IIfcReinforcingBar>().FirstOrDefault(b => b.GlobalId == targetGuid);
            var newBar = newStore.Instances.OfType<Xbim.Ifc4.Interfaces.IIfcReinforcingBar>().FirstOrDefault(b => b.GlobalId == targetGuid);

            Assert.NotNull(oldBar);
            Assert.NotNull(newBar);

            var oldMetrics = oldExtractor.ExtractMetrics(oldBar);
            var newMetrics = newExtractor.ExtractMetrics(newBar);

            Assert.True(oldMetrics.HasGeometry);
            Assert.True(newMetrics.HasGeometry);

            var comparer = new IfcGeometryComparer
            {
                PositionToleranceMm = 1.0,
                VolumeToleranceM3 = 0.001,
                VolumePercentTolerance = 1.0
            };

            var result = comparer.Compare(oldMetrics, newMetrics);

            // Verify that the false positive Moved status (which previously showed 2.8 km displacement)
            // is completely resolved and the element is classified as Equal.
            Assert.Equal(GeometryStatus.Equal, result.Status);
            Assert.False(result.HasGeometricDifference);
            Assert.True(result.CentroidDisplacementMm < 1.0, $"Expected < 1.0 mm displacement, got {result.CentroidDisplacementMm:F2} mm");
        }

        [Fact]
        public void TriangulationCalculator_HighCoordinates_UTM_DoesNotSufferCancellation()
        {
            // Create a 1 m³ cube translated to UTM coordinates: X=70,367 m, Y=1,138,613 m, Z=14 m
            var cube = CreateCubeTriangulation(1.0, 1.0, 1.0);
            var transform = XbimMatrix3D.CreateTranslation(70367.0, 1138613.0, 14.0);

            var metrics = TriangulationCalculator.ComputeMetrics(cube, transform);

            Assert.True(metrics.HasGeometry);
            // Volume must be exact 1.0 m³ without cancellation noise
            Assert.True(Math.Abs(metrics.Volume - 1.0) < 1e-9, $"Expected 1.0 m³, got {metrics.Volume}");
            // Centroid must match translation
            Assert.True(Math.Abs(metrics.Centroid.X - 70367.0) < 1e-6);
            Assert.True(Math.Abs(metrics.Centroid.Y - 1138613.0) < 1e-6);
            Assert.True(Math.Abs(metrics.Centroid.Z - 14.0) < 1e-6);

            // Centroid must be inside the bounding box
            Assert.True(metrics.Centroid.X >= metrics.BoundingBox.X && metrics.Centroid.X <= metrics.BoundingBox.X + metrics.BoundingBox.SizeX);
            Assert.True(metrics.Centroid.Y >= metrics.BoundingBox.Y && metrics.Centroid.Y <= metrics.BoundingBox.Y + metrics.BoundingBox.SizeY);
            Assert.True(metrics.Centroid.Z >= metrics.BoundingBox.Z && metrics.Centroid.Z <= metrics.BoundingBox.Z + metrics.BoundingBox.SizeZ);
        }

        [Fact]
        public void IfcGeometryComparer_StationaryBoundingBox_RejectsCentroidNoiseAsMoved()
        {
            // Bounding box at the same location
            var bboxOld = new XbimRect3D(10.0, 20.0, 30.0, 2.0, 2.0, 2.0);
            var bboxNew = new XbimRect3D(10.0, 20.0, 30.0, 2.0, 2.0, 2.0);

            // Centroid slightly shifted by 2 mm due to internal tessellation variation
            var cOld = new XbimPoint3D(11.0, 21.0, 31.0);
            var cNew = new XbimPoint3D(11.002, 21.0, 31.0); // 2 mm shift

            var metricsOld = new GeometryMetrics
            {
                HasGeometry = true,
                Volume = 8.0,
                SurfaceArea = 24.0,
                Centroid = cOld,
                BoundingBox = bboxOld
            };
            var metricsNew = new GeometryMetrics
            {
                HasGeometry = true,
                Volume = 8.0,
                SurfaceArea = 24.0,
                Centroid = cNew,
                BoundingBox = bboxNew
            };

            var comparer = new IfcGeometryComparer
            {
                PositionToleranceMm = 1.0, // 1 mm tolerance
                VolumeToleranceM3 = 0.001
            };

            var result = comparer.Compare(metricsOld, metricsNew);

            // Because bounding box didn't move (0.0 mm), the 2 mm centroid shift is not classified as Moved
            Assert.Equal(GeometryStatus.Equal, result.Status);
            Assert.False(result.HasGeometricDifference);
        }

        [Fact]
        public void TriangulationCalculator_UnitCube_CalculatesExactMetrics()
        {
            var cube = CreateCubeTriangulation(1.0, 1.0, 1.0);
            var metrics = TriangulationCalculator.ComputeMetrics(cube, XbimMatrix3D.Identity);

            Assert.True(metrics.HasGeometry);
            Assert.True(Math.Abs(metrics.Volume - 1.0) < 1e-9, $"Expected 1.0 m³, got {metrics.Volume}");
            Assert.True(Math.Abs(metrics.SurfaceArea - 6.0) < 1e-9, $"Expected 6.0 m², got {metrics.SurfaceArea}");
            Assert.Equal(12, metrics.TriangleCount);
            Assert.Equal(8, metrics.VertexCount);

            Assert.True(Math.Abs(metrics.Centroid.X) < 1e-9);
            Assert.True(Math.Abs(metrics.Centroid.Y) < 1e-9);
            Assert.True(Math.Abs(metrics.Centroid.Z) < 1e-9);
        }

        [Fact]
        public void TriangulationCalculator_WithTransform_AppliesTranslationAndRotation()
        {
            var cube = CreateCubeTriangulation(1.0, 1.0, 1.0);
            var transform = XbimMatrix3D.CreateTranslation(10.0, 20.0, 30.0);
            var metrics = TriangulationCalculator.ComputeMetrics(cube, transform);

            Assert.True(Math.Abs(metrics.Volume - 1.0) < 1e-9);
            Assert.True(Math.Abs(metrics.Centroid.X - 10.0) < 1e-9);
            Assert.True(Math.Abs(metrics.Centroid.Y - 20.0) < 1e-9);
            Assert.True(Math.Abs(metrics.Centroid.Z - 30.0) < 1e-9);
        }

        [Fact]
        public void TriangulationCalculator_WithModelFactorsOneMeter_ScalesCorrectly()
        {
            // Cube defined in millimeters: 1000 mm x 1000 mm x 1000 mm = 1 m³
            var cubeMm = CreateCubeTriangulation(1000.0, 1000.0, 1000.0);
            var metrics = TriangulationCalculator.ComputeMetrics(cubeMm, XbimMatrix3D.Identity, oneMeter: 1000.0);

            Assert.True(metrics.HasGeometry);
            Assert.True(Math.Abs(metrics.Volume - 1.0) < 1e-6, $"Expected 1.0 m³, got {metrics.Volume}");
            Assert.True(Math.Abs(metrics.SurfaceArea - 6.0) < 1e-6, $"Expected 6.0 m², got {metrics.SurfaceArea}");
            Assert.True(Math.Abs(metrics.BoundingBox.SizeX - 1.0) < 1e-6);
        }

        [Fact]
        public void IfcGeometryComparer_IdenticalElements_ReturnsEqual()
        {
            var cubeA = CreateCubeTriangulation(1.0, 1.0, 1.0);
            var cubeB = CreateCubeTriangulation(1.0, 1.0, 1.0);

            var metricsA = TriangulationCalculator.ComputeMetrics(cubeA, XbimMatrix3D.Identity);
            var metricsB = TriangulationCalculator.ComputeMetrics(cubeB, XbimMatrix3D.Identity);

            var comparer = new IfcGeometryComparer();
            var result = comparer.Compare(metricsA, metricsB);

            Assert.Equal(GeometryStatus.Equal, result.Status);
            Assert.False(result.HasGeometricDifference);
        }

        [Fact]
        public void IfcGeometryComparer_VolumeDifferenceAbove1ml_DetectsVolumeChanged()
        {
            var cubeOld = CreateCubeTriangulation(1.0, 1.0, 1.0); // 1.000000 m³
            var cubeNew = CreateCubeTriangulation(1.0, 1.0, 1.000002); // 1.000002 m³ -> +2 ml

            var metricsOld = TriangulationCalculator.ComputeMetrics(cubeOld, XbimMatrix3D.Identity);
            var metricsNew = TriangulationCalculator.ComputeMetrics(cubeNew, XbimMatrix3D.Identity);

            var comparer = new IfcGeometryComparer { VolumeToleranceM3 = 0.000001 }; // 1 ml tolerance
            var result = comparer.Compare(metricsOld, metricsNew);

            Assert.Equal(GeometryStatus.VolumeChanged, result.Status);
            Assert.True(result.HasGeometricDifference);
            Assert.True(result.VolumeDiffMl > 1.5 && result.VolumeDiffMl < 2.5, $"Volume diff was {result.VolumeDiffMl} ml");

            var props = result.ToPropertiesDictionary();
            Assert.Equal("VolumeChanged", props["QA_GeometryStatus"]);
            Assert.Contains("+2.00", props["QA_Volume_Diff_ml"]);
        }

        [Fact]
        public void IfcGeometryComparer_VolumeDifferenceBelow1ml_RemainsEqual()
        {
            var cubeOld = CreateCubeTriangulation(1.0, 1.0, 1.0);
            var cubeNew = CreateCubeTriangulation(1.0, 1.0, 1.0000004); // +0.4 ml (< 1 ml)

            var metricsOld = TriangulationCalculator.ComputeMetrics(cubeOld, XbimMatrix3D.Identity);
            var metricsNew = TriangulationCalculator.ComputeMetrics(cubeNew, XbimMatrix3D.Identity);

            var comparer = new IfcGeometryComparer
            {
                VolumeToleranceM3 = 0.000001, // 1 ml
                DimensionToleranceMm = 1.0 // 1 mm
            };
            var result = comparer.Compare(metricsOld, metricsNew);

            Assert.Equal(GeometryStatus.Equal, result.Status);
        }

        [Fact]
        public void IfcGeometryComparer_TranslatedElement_DetectsMoved()
        {
            var cubeOld = CreateCubeTriangulation(1.0, 1.0, 1.0);
            var cubeNew = CreateCubeTriangulation(1.0, 1.0, 1.0);

            var metricsOld = TriangulationCalculator.ComputeMetrics(cubeOld, XbimMatrix3D.Identity);
            var metricsNew = TriangulationCalculator.ComputeMetrics(cubeNew, XbimMatrix3D.CreateTranslation(0.005, 0, 0)); // 5 mm

            var comparer = new IfcGeometryComparer
            {
                VolumeToleranceM3 = 0.000001,
                PositionToleranceMm = 1.0 // 1 mm threshold
            };
            var result = comparer.Compare(metricsOld, metricsNew);

            Assert.True(result.HasGeometricDifference);
            Assert.Equal(GeometryStatus.Moved, result.Status);
            Assert.True(result.CentroidDisplacementMm > 4.9 && result.CentroidDisplacementMm < 5.1);

            var props = result.ToPropertiesDictionary();
            Assert.Equal("Moved", props["QA_GeometryStatus"]);
            Assert.Equal("5.00", props["QA_Centroid_Displacement_mm"]);
        }

        [Fact]
        public void IfcGeometryComparer_RelativeVolumePercentage_FiltersTessellationNoise()
        {
            // A 10 m³ slab with 2 liters (0.02%) tessellation difference
            var metricsOld = new GeometryMetrics { HasGeometry = true, Volume = 10.0, SurfaceArea = 20.0, Centroid = XbimPoint3D.Zero };
            var metricsNew = new GeometryMetrics { HasGeometry = true, Volume = 10.002, SurfaceArea = 20.0, Centroid = XbimPoint3D.Zero };

            var comparer = new IfcGeometryComparer
            {
                VolumeToleranceM3 = 0.001, // 1 liter threshold
                VolumePercentTolerance = 1.0 // 1% threshold
            };

            var result = comparer.Compare(metricsOld, metricsNew);
            // 2L exceeds 1L, but 0.02% does not exceed 1% -> Equal
            Assert.Equal(GeometryStatus.Equal, result.Status);
            Assert.False(result.HasGeometricDifference);
        }

        [Fact]
        public void IfcGhostService_CreatesAndCachesStyles_Ifc4AndIfc2x3()
        {
            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "Test",
                ApplicationFullName = "Test",
                ApplicationIdentifier = "Test",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Test",
                EditorsGivenName = "Test",
                EditorsOrganisationName = "Test"
            };

            var ghostService = new IfcGhostService();

            using (var model4 = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
            {
                using (var txn = model4.BeginTransaction("Create Styles"))
                {
                    var sDeleted = ghostService.GetOrCreateStyle(model4, GhostStyleType.Deleted);
                    var sInserted = ghostService.GetOrCreateStyle(model4, GhostStyleType.Inserted);
                    var sModNew = ghostService.GetOrCreateStyle(model4, GhostStyleType.ModifiedNew);
                    var sModOld = ghostService.GetOrCreateStyle(model4, GhostStyleType.ModifiedOld);

                    Assert.NotNull(sDeleted);
                    Assert.NotNull(sInserted);
                    Assert.NotNull(sModNew);
                    Assert.NotNull(sModOld);

                    // Verify cache returns identical instance
                    var sDeletedCached = ghostService.GetOrCreateStyle(model4, GhostStyleType.Deleted);
                    Assert.Same(sDeleted, sDeletedCached);

                    txn.Commit();
                }
            }

            using (var model2x3 = IfcStore.Create(editor, XbimSchemaVersion.Ifc2X3, XbimStoreType.InMemoryModel))
            {
                using (var txn = model2x3.BeginTransaction("Create Styles 2x3"))
                {
                    var sDeleted = ghostService.GetOrCreateStyle(model2x3, GhostStyleType.Deleted);
                    var sInserted = ghostService.GetOrCreateStyle(model2x3, GhostStyleType.Inserted);
                    var sModNew = ghostService.GetOrCreateStyle(model2x3, GhostStyleType.ModifiedNew);
                    var sModOld = ghostService.GetOrCreateStyle(model2x3, GhostStyleType.ModifiedOld);

                    Assert.NotNull(sDeleted);
                    Assert.NotNull(sInserted);
                    Assert.NotNull(sModNew);
                    Assert.NotNull(sModOld);

                    var sDeletedCached = ghostService.GetOrCreateStyle(model2x3, GhostStyleType.Deleted);
                    Assert.Same(sDeleted, sDeletedCached);

                    txn.Commit();
                }
            }
        }

        [Fact]
        public void IfcGhostService_InsertGhostProduct_ClonesProductWithUniqueGuidAndQAGhostProps()
        {
            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "Test",
                ApplicationFullName = "Test",
                ApplicationIdentifier = "Test",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Test",
                EditorsGivenName = "Test",
                EditorsOrganisationName = "Test"
            };

            var ghostService = new IfcGhostService();

            using var modelOld = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
            using var modelNew = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);

            Xbim.Ifc4.Kernel.IfcProduct sourceProduct;
            using (var txnOld = modelOld.BeginTransaction("Create source"))
            {
                var project = modelOld.Instances.New<Xbim.Ifc4.Kernel.IfcProject>(p => p.Name = "Old Project");
                sourceProduct = modelOld.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b =>
                {
                    b.Name = "Beam-01";
                });
                txnOld.Commit();
            }

            using (var txnNew = modelNew.BeginTransaction("Create spatial and ghost"))
            {
                var project = modelNew.Instances.New<Xbim.Ifc4.Kernel.IfcProject>(p => p.Name = "New Project");
                var site = modelNew.Instances.New<Xbim.Ifc4.ProductExtension.IfcSite>(s => s.Name = "Site");
                var building = modelNew.Instances.New<Xbim.Ifc4.ProductExtension.IfcBuilding>(b => b.Name = "Building");
                var storey = modelNew.Instances.New<Xbim.Ifc4.ProductExtension.IfcBuildingStorey>(st => st.Name = "Storey 1");
                
                var ghost = ghostService.InsertGhostProduct(modelNew, sourceProduct, GhostStyleType.Deleted, "[GHOST_DELETED]");
                Assert.NotNull(ghost);
                Assert.Equal("[GHOST_DELETED] Beam-01", ghost.Name);
                Assert.NotEqual(sourceProduct.GlobalId, ghost.GlobalId);

                txnNew.Commit();
            }

            // Verify QA_GHOST property set was created on the ghost product
            var ghostRel = modelNew.Instances.OfType<Xbim.Ifc4.Kernel.IfcRelDefinesByProperties>()
                .FirstOrDefault(r => r.RelatingPropertyDefinition.PropertySetDefinitions
                    .OfType<Xbim.Ifc4.Kernel.IfcPropertySet>()
                    .Any(ps => ps.Name == "QA_GHOST"));

            Assert.NotNull(ghostRel);
            var ghostPSet = ghostRel.RelatingPropertyDefinition.PropertySetDefinitions
                .OfType<Xbim.Ifc4.Kernel.IfcPropertySet>()
                .First(ps => ps.Name == "QA_GHOST");

            var propType = ghostPSet.HasProperties.OfType<Xbim.Ifc4.PropertyResource.IfcPropertySingleValue>()
                .FirstOrDefault(p => p.Name == "QA_GhostType");
            Assert.NotNull(propType);
            Assert.Equal("Deleted", propType.NominalValue.ToString());

            var propOriginalId = ghostPSet.HasProperties.OfType<Xbim.Ifc4.PropertyResource.IfcPropertySingleValue>()
                .FirstOrDefault(p => p.Name == "QA_OriginalGlobalId");
            Assert.NotNull(propOriginalId);
            Assert.Equal(sourceProduct.GlobalId.ToString(), propOriginalId.NominalValue.ToString());
        }

        [Fact]
        public void TestTrekkerorAssemblyGeometryComparison()
        {
            var oldFile = @"\\nsv2-nasuni-01\fredrikstad\Prosjekt\O10253\10253324-01\10253324-01-03_ARBEIDSOMRAADE\11_m_fag\02_bru\KS\2024.11.29_Rev.1\f-bru_K01-undergang-Tune-kirke.ifc";
            var newFile = @"\\nsv2-nasuni-01\fredrikstad\Prosjekt\O10253\10253324-01\10253324-01-03_ARBEIDSOMRAADE\11_m_fag\02_bru\KS\2026.02.13_Rev.B - Form - Justert trekkerør for lys i kulvert\f-bru_K01-undergang-Tune-kirke.ifc";

            if (!System.IO.File.Exists(oldFile) || !System.IO.File.Exists(newFile))
                return;

            using var oldStore = IfcStore.Open(oldFile);
            using var newStore = IfcStore.Open(newFile);

            var oldExtractor = new IfcGeometryExtractor(oldStore);
            oldExtractor.EnsureContextCreated();
            var newExtractor = new IfcGeometryExtractor(newStore);
            newExtractor.EnsureContextCreated();

            // Find Trekkerør assembly #2828 in newStore (GUID: 0cEghp4Db4j9NQaoBEvUB7)
            var newAssembly = newStore.Instances.OfType<Xbim.Ifc4.Interfaces.IIfcProduct>()
                .FirstOrDefault(p => p.GlobalId == "0cEghp4Db4j9NQaoBEvUB7");
            var oldAssembly = oldStore.Instances.OfType<Xbim.Ifc4.Interfaces.IIfcProduct>()
                .FirstOrDefault(p => p.GlobalId == "0cEghp4Db4j9NQaoBEvUB7");

            Assert.NotNull(newAssembly);
            Assert.NotNull(oldAssembly);

            var oldMetrics = oldExtractor.ExtractMetrics(oldAssembly);
            var newMetrics = newExtractor.ExtractMetrics(newAssembly);

            Assert.True(oldMetrics.HasGeometry, "Old assembly should have geometry from decomposed children");
            Assert.True(newMetrics.HasGeometry, "New assembly should have geometry from decomposed children");

            var comparer = new IfcGeometryComparer
            {
                VolumeToleranceM3 = 0.001,
                VolumePercentTolerance = 1.0,
                PositionToleranceMm = 1.0
            };
            var result = comparer.Compare(oldMetrics, newMetrics);

            Assert.True(result.HasGeometricDifference, "Trekkerør assembly should have geometric difference");
            // Conduit was adjusted in Rev.B (volume changed by +6.82%, 177ml), so it must be VolumeChanged (not Moved)
            Assert.Equal(GeometryStatus.VolumeChanged, result.Status);
            Assert.True(result.CentroidDisplacementMm > 300.0, $"Expected >300mm displacement, got {result.CentroidDisplacementMm}");
        }

        [Fact]
        public void TestUnalteredGroundingPoint_DoesNotProduceFalsePositive()
        {
            var oldFile = @"\\nsv2-nasuni-01\fredrikstad\Prosjekt\O10253\10253324-01\10253324-01-03_ARBEIDSOMRAADE\11_m_fag\02_bru\KS\2024.11.29_Rev.1\f-bru_K01-undergang-Tune-kirke.ifc";
            var newFile = @"\\nsv2-nasuni-01\fredrikstad\Prosjekt\O10253\10253324-01\10253324-01-03_ARBEIDSOMRAADE\11_m_fag\02_bru\KS\2026.02.13_Rev.B - Form - Justert trekkerør for lys i kulvert\f-bru_K01-undergang-Tune-kirke.ifc";

            if (!System.IO.File.Exists(oldFile) || !System.IO.File.Exists(newFile))
                return;

            using var oldStore = IfcStore.Open(oldFile);
            using var newStore = IfcStore.Open(newFile);

            var oldExtractor = new IfcGeometryExtractor(oldStore);
            oldExtractor.EnsureContextCreated();
            var newExtractor = new IfcGeometryExtractor(newStore);
            newExtractor.EnsureContextCreated();

            // Find Jordingspunkt #108216 (GUID: 3jYh1YqAr1ueXPcCoA73J5)
            var newJord = newStore.Instances.OfType<Xbim.Ifc4.Interfaces.IIfcProduct>()
                .FirstOrDefault(p => p.GlobalId == "3jYh1YqAr1ueXPcCoA73J5");
            var oldJord = oldStore.Instances.OfType<Xbim.Ifc4.Interfaces.IIfcProduct>()
                .FirstOrDefault(p => p.GlobalId == "3jYh1YqAr1ueXPcCoA73J5");

            Assert.NotNull(newJord);
            Assert.NotNull(oldJord);

            var oldMetrics = oldExtractor.ExtractMetrics(oldJord);
            var newMetrics = newExtractor.ExtractMetrics(newJord);

            Assert.True(oldMetrics.HasGeometry);
            Assert.True(newMetrics.HasGeometry);

            var comparer = new IfcGeometryComparer
            {
                VolumeToleranceM3 = 0.001,
                VolumePercentTolerance = 1.0,
                PositionToleranceMm = 1.0
            };
            var result = comparer.Compare(oldMetrics, newMetrics);

            // Unaltered grounding point must be Equal (no false positive)
            Assert.Equal(GeometryStatus.Equal, result.Status);
            Assert.False(result.HasGeometricDifference);
            Assert.True(result.CentroidDisplacementMm < 0.1, $"Expected sub-millimeter jitter (<0.1mm), got {result.CentroidDisplacementMm}mm");
        }

        [Fact]
        public void IfcGhostService_GetOrCreateStorey_CreatesStoreyUnderBuilding_Ifc2x3AndIfc4()
        {
            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "Test",
                ApplicationFullName = "Test",
                ApplicationIdentifier = "Test",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Test",
                EditorsGivenName = "Test",
                EditorsOrganisationName = "Test"
            };

            var ghostService = new IfcGhostService();

            // Test IFC4
            using (var model4 = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
            {
                using (var txn = model4.BeginTransaction("Setup hierarchy"))
                {
                    var proj = model4.Instances.New<Xbim.Ifc4.Kernel.IfcProject>(p => p.Name = "P4");
                    var building = model4.Instances.New<Xbim.Ifc4.ProductExtension.IfcBuilding>(b => b.Name = "B4");
                    model4.Instances.New<Xbim.Ifc4.Kernel.IfcRelAggregates>(r =>
                    {
                        r.RelatingObject = proj;
                        r.RelatedObjects.Add(building);
                    });

                    var storey1 = ghostService.GetOrCreateStorey(model4, "QA - Changed (New)");
                    var storey2 = ghostService.GetOrCreateStorey(model4, "QA - Changed (New)"); // idempotency check

                    Assert.NotNull(storey1);
                    Assert.Same(storey1, storey2);
                    Assert.Equal("QA - Changed (New)", storey1.Name);

                    // Check aggregates relationship
                    var relAgg = model4.Instances.OfType<Xbim.Ifc4.Kernel.IfcRelAggregates>()
                        .FirstOrDefault(r => r.RelatingObject == building && r.RelatedObjects.Contains((Xbim.Ifc4.Kernel.IfcObjectDefinition)storey1));
                    Assert.NotNull(relAgg);

                    txn.Commit();
                }
            }

            // Test IFC2X3
            using (var model2x3 = IfcStore.Create(editor, XbimSchemaVersion.Ifc2X3, XbimStoreType.InMemoryModel))
            {
                using (var txn = model2x3.BeginTransaction("Setup hierarchy 2x3"))
                {
                    var proj = model2x3.Instances.New<Xbim.Ifc2x3.Kernel.IfcProject>(p => p.Name = "P2x3");
                    var building = model2x3.Instances.New<Xbim.Ifc2x3.ProductExtension.IfcBuilding>(b => b.Name = "B2x3");
                    model2x3.Instances.New<Xbim.Ifc2x3.Kernel.IfcRelAggregates>(r =>
                    {
                        r.RelatingObject = proj;
                        r.RelatedObjects.Add(building);
                    });

                    var storey1 = ghostService.GetOrCreateStorey(model2x3, "QA - Changed (New)");
                    var storey2 = ghostService.GetOrCreateStorey(model2x3, "QA - Changed (New)");

                    Assert.NotNull(storey1);
                    Assert.Same(storey1, storey2);
                    Assert.Equal("QA - Changed (New)", storey1.Name);

                    var relAgg = model2x3.Instances.OfType<Xbim.Ifc2x3.Kernel.IfcRelAggregates>()
                        .FirstOrDefault(r => r.RelatingObject == building && r.RelatedObjects.Contains((Xbim.Ifc2x3.Kernel.IfcObjectDefinition)storey1));
                    Assert.NotNull(relAgg);

                    txn.Commit();
                }
            }
        }

        [Fact]
        public void IfcGhostService_MoveProductToStorey_ReassignsSpatialContainment_Ifc4()
        {
            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "Test",
                ApplicationFullName = "Test",
                ApplicationIdentifier = "Test",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Test",
                EditorsGivenName = "Test",
                EditorsOrganisationName = "Test"
            };

            var ghostService = new IfcGhostService();

            using var model = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
            using (var txn = model.BeginTransaction("Setup and move"))
            {
                var building = model.Instances.New<Xbim.Ifc4.ProductExtension.IfcBuilding>(b => b.Name = "Building");
                var originalStorey = model.Instances.New<Xbim.Ifc4.ProductExtension.IfcBuildingStorey>(st => st.Name = "Level 1");
                var beam = model.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b => b.Name = "Beam-01");

                // Assign beam to Level 1
                model.Instances.New<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>(r =>
                {
                    r.RelatingStructure = originalStorey;
                    r.RelatedElements.Add(beam);
                });

                // Create QA storey and move beam
                var qaStorey = ghostService.GetOrCreateStorey(model, "QA - Changed (New)");
                ghostService.MoveProductToStorey(model, beam, qaStorey);

                // Beam should NOT be contained in originalStorey anymore
                var originalContainment = model.Instances.OfType<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>()
                    .Where(r => r.RelatingStructure == originalStorey && r.RelatedElements.Contains(beam))
                    .ToList();
                Assert.Empty(originalContainment);

                // Beam MUST be contained in qaStorey
                var newContainment = model.Instances.OfType<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>()
                    .Where(r => r.RelatingStructure == qaStorey && r.RelatedElements.Contains(beam))
                    .ToList();
                Assert.Single(newContainment);

                txn.Commit();
            }
        }

        [Fact]
        public void IfcGhostService_CreateGroup_AssignsProductsToGroup_Ifc4()
        {
            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "Test",
                ApplicationFullName = "Test",
                ApplicationIdentifier = "Test",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Test",
                EditorsGivenName = "Test",
                EditorsOrganisationName = "Test"
            };

            var ghostService = new IfcGhostService();

            using var model = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
            using (var txn = model.BeginTransaction("Create group test"))
            {
                var beam1 = model.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b => b.Name = "Beam-01");
                var beam2 = model.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b => b.Name = "Beam-02");

                ghostService.CreateGroup(model, "QA - Changed (New)", new[] { beam1, beam2 });

                var group = model.Instances.OfType<Xbim.Ifc4.Kernel.IfcGroup>()
                    .FirstOrDefault(g => g.Name == "QA - Changed (New)");
                Assert.NotNull(group);

                var relAssign = model.Instances.OfType<Xbim.Ifc4.Kernel.IfcRelAssignsToGroup>()
                    .FirstOrDefault(r => r.RelatingGroup == group);
                Assert.NotNull(relAssign);
                Assert.Contains(beam1, relAssign.RelatedObjects);
                Assert.Contains(beam2, relAssign.RelatedObjects);

                txn.Commit();
            }
        }

        [Fact]
        public void IfcWriter_ApplyGhostOverlays_SeparatesCategoriesInModelTreeAndCreatesGroups()
        {
            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "Test",
                ApplicationFullName = "Test",
                ApplicationIdentifier = "Test",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Test",
                EditorsGivenName = "Test",
                EditorsOrganisationName = "Test"
            };

            using var modelOld = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
            using var modelNew = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);

            Xbim.Ifc4.SharedBldgElements.IfcBeam prodModifiedOld;
            Xbim.Ifc4.SharedBldgElements.IfcBeam prodDeleted;

            using (var txn = modelOld.BeginTransaction("Old model setup"))
            {
                var proj = modelOld.Instances.New<Xbim.Ifc4.Kernel.IfcProject>(p => p.Name = "Old Project");
                prodModifiedOld = modelOld.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b => b.Name = "ModOld");
                prodDeleted = modelOld.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b => b.Name = "DelOld");
                txn.Commit();
            }

            Xbim.Ifc4.ProductExtension.IfcBuildingStorey defaultStorey;
            Xbim.Ifc4.SharedBldgElements.IfcBeam prodModifiedNew;
            Xbim.Ifc4.SharedBldgElements.IfcBeam prodInserted;
            Xbim.Ifc4.SharedBldgElements.IfcBeam prodPropChanged;
            Xbim.Ifc4.SharedBldgElements.IfcBeam prodUnchanged;

            using (var txn = modelNew.BeginTransaction("New model setup"))
            {
                var proj = modelNew.Instances.New<Xbim.Ifc4.Kernel.IfcProject>(p => p.Name = "New Project");
                var building = modelNew.Instances.New<Xbim.Ifc4.ProductExtension.IfcBuilding>(b => b.Name = "Building");
                defaultStorey = modelNew.Instances.New<Xbim.Ifc4.ProductExtension.IfcBuildingStorey>(s => s.Name = "Default Level");

                modelNew.Instances.New<Xbim.Ifc4.Kernel.IfcRelAggregates>(r =>
                {
                    r.RelatingObject = proj;
                    r.RelatedObjects.Add(building);
                });
                modelNew.Instances.New<Xbim.Ifc4.Kernel.IfcRelAggregates>(r =>
                {
                    r.RelatingObject = building;
                    r.RelatedObjects.Add(defaultStorey);
                });

                prodModifiedNew = modelNew.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b => b.Name = "ModNew");
                prodInserted = modelNew.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b => b.Name = "AddedNew");
                prodPropChanged = modelNew.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b => b.Name = "PropChanged");
                prodUnchanged = modelNew.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b => b.Name = "Unchanged");

                // Assign all to default storey initially
                modelNew.Instances.New<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>(r =>
                {
                    r.RelatingStructure = defaultStorey;
                    r.RelatedElements.Add(prodModifiedNew);
                    r.RelatedElements.Add(prodInserted);
                    r.RelatedElements.Add(prodPropChanged);
                    r.RelatedElements.Add(prodUnchanged);
                });

                txn.Commit();
            }

            var compResult = new IfcComparison.Models.IfcComparerResult { ComparedIfcObjects = new System.Collections.Generic.Dictionary<Xbim.Ifc4.Interfaces.IIfcObject, System.Collections.Generic.Dictionary<string, string>>() };
            compResult.ModifiedNewObjects.Add(prodModifiedNew);
            compResult.InsertedObjects.Add(prodInserted);
            compResult.ModifiedOldGhostObjects.Add(prodModifiedOld);
            compResult.DeletedGhostObjects.Add(prodDeleted);

            compResult.ComparedIfcObjects[prodModifiedNew] = new System.Collections.Generic.Dictionary<string, string> { { "PropA", "Equal" } };
            compResult.ComparedIfcObjects[prodPropChanged] = new System.Collections.Generic.Dictionary<string, string> { { "PropA", "Changed from 1 to 2" } };
            compResult.ComparedIfcObjects[prodUnchanged] = new System.Collections.Generic.Dictionary<string, string> { { "PropA", "Equal" } };

            var writer = new IfcComparison.Models.IfcWriter(compResult, XbimSchemaVersion.Ifc4, "dummy.ifc");
            writer.ApplyGhostOverlays(modelNew);

            // 1. Verify storeys exist
            var storeys = modelNew.Instances.OfType<Xbim.Ifc4.ProductExtension.IfcBuildingStorey>().ToList();
            Assert.Contains(storeys, s => s.Name == "QA - Changed (New)");
            Assert.Contains(storeys, s => s.Name == "QA - Added (New)");
            Assert.Contains(storeys, s => s.Name == "QA - Changed (Old Ghost)");
            Assert.Contains(storeys, s => s.Name == "QA - Deleted (Ghost)");
            Assert.Contains(storeys, s => s.Name == "QA - Changed (Properties)");

            var storeyModNew = storeys.First(s => s.Name == "QA - Changed (New)");
            var storeyAdded = storeys.First(s => s.Name == "QA - Added (New)");
            var storeyModOld = storeys.First(s => s.Name == "QA - Changed (Old Ghost)");
            var storeyDeleted = storeys.First(s => s.Name == "QA - Deleted (Ghost)");
            var storeyProps = storeys.First(s => s.Name == "QA - Changed (Properties)");

            // 2. Verify products were moved / placed into the correct storeys
            var rels = modelNew.Instances.OfType<Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure>().ToList();

            Assert.Contains(rels, r => r.RelatingStructure == storeyModNew && r.RelatedElements.Contains(prodModifiedNew));
            Assert.Contains(rels, r => r.RelatingStructure == storeyAdded && r.RelatedElements.Contains(prodInserted));
            Assert.Contains(rels, r => r.RelatingStructure == storeyProps && r.RelatedElements.Contains(prodPropChanged));

            // Verify unchanged product remained in defaultStorey
            Assert.Contains(rels, r => r.RelatingStructure == defaultStorey && r.RelatedElements.Contains(prodUnchanged));
            // Verify unchanged product is NOT in any QA storey
            Assert.DoesNotContain(rels, r => r.RelatingStructure != defaultStorey && r.RelatedElements.Contains(prodUnchanged));

            // Verify ghosts are in the ghost storeys
            var relModOld = rels.FirstOrDefault(r => r.RelatingStructure == storeyModOld);
            Assert.NotNull(relModOld);
            Assert.Single(relModOld.RelatedElements);
            Assert.StartsWith("[GHOST_MODIFIED]", relModOld.RelatedElements.First().Name);

            var relDeleted = rels.FirstOrDefault(r => r.RelatingStructure == storeyDeleted);
            Assert.NotNull(relDeleted);
            Assert.Single(relDeleted.RelatedElements);
            Assert.StartsWith("[GHOST_DELETED]", relDeleted.RelatedElements.First().Name);

            // 3. Verify IfcGroups exist
            var groups = modelNew.Instances.OfType<Xbim.Ifc4.Kernel.IfcGroup>().ToList();
            Assert.Contains(groups, g => g.Name == "QA - Changed (New)");
            Assert.Contains(groups, g => g.Name == "QA - Added (New)");
            Assert.Contains(groups, g => g.Name == "QA - Changed (Old Ghost)");
            Assert.Contains(groups, g => g.Name == "QA - Deleted (Ghost)");
            Assert.Contains(groups, g => g.Name == "QA - Changed (Properties)");
        }

        [Fact]
        public void Compare_WhenVolumeChangedAndPositionMoved_ReturnsVolumeChanged()
        {
            // Reproduces exact case from user: Rebar with 1.85% volume change (+58.46 ml) and 50mm displacement
            var oldMetrics = new GeometryMetrics
            {
                HasGeometry = true,
                Volume = 0.003158,
                Centroid = new XbimPoint3D(0, 0, 0),
                BoundingBox = new XbimRect3D(0, 0, 0, 1.0, 0.016, 0.016)
            };
            var newMetrics = new GeometryMetrics
            {
                HasGeometry = true,
                Volume = 0.003216,
                Centroid = new XbimPoint3D(0.05, 0, 0), // 50 mm displacement
                BoundingBox = new XbimRect3D(0.0383, 0.0045, 0.0923, 1.0383, 0.0205, 0.1083)
            };

            var comparer = new IfcGeometryComparer
            {
                VolumeToleranceM3 = 0.001,
                VolumePercentTolerance = 1.0,
                PositionToleranceMm = 1.0
            };

            var result = comparer.Compare(oldMetrics, newMetrics);

            // Must be VolumeChanged, NOT Moved, because volume actually changed (+1.85%)
            Assert.Equal(GeometryStatus.VolumeChanged, result.Status);
            Assert.True(result.HasGeometricDifference);
            Assert.NotNull(result.CentroidDisplacementMm);
            Assert.NotNull(result.VolumeChangePercent);
            Assert.Equal(50.0, result.CentroidDisplacementMm.Value, 1);
            Assert.Equal(1.84, result.VolumeChangePercent.Value, 1);
        }

        [Fact]
        public void Compare_WhenVolumeCompletelyEqualAndPositionMoved_ReturnsMoved()
        {
            // When volume is completely equal, an element with displacement must be classified as Moved
            var oldMetrics = new GeometryMetrics
            {
                HasGeometry = true,
                Volume = 0.003158,
                Centroid = new XbimPoint3D(0, 0, 0),
                BoundingBox = new XbimRect3D(0, 0, 0, 1.0, 0.016, 0.016)
            };
            var newMetrics = new GeometryMetrics
            {
                HasGeometry = true,
                Volume = 0.003158, // Completely equal volume
                Centroid = new XbimPoint3D(0.05, 0, 0), // 50 mm displacement
                BoundingBox = new XbimRect3D(0.05, 0, 0, 1.05, 0.016, 0.016) // translated bbox
            };

            var comparer = new IfcGeometryComparer
            {
                VolumeToleranceM3 = 0.001,
                VolumePercentTolerance = 1.0,
                PositionToleranceMm = 1.0
            };

            var result = comparer.Compare(oldMetrics, newMetrics);

            // Shows as Moved ONLY if the volume is completely equal
            Assert.Equal(GeometryStatus.Moved, result.Status);
            Assert.True(result.HasGeometricDifference);
            Assert.NotNull(result.CentroidDisplacementMm);
            Assert.Equal(50.0, result.CentroidDisplacementMm.Value, 1);
        }
    }
}
