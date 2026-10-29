using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IfcComparison.Models;
using IfcComparison.ViewModels;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.IO;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace IfcComparison.Tests
{
    public class QualityOfLifeImprovementsTests
    {
        [Fact]
        public void GetInitialDirectory_WithExistingLastUsedFolder_ReturnsLastUsedFolder()
        {
            var tempFolder = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var result = MainViewModel.GetInitialDirectory(tempFolder, "C:\\SomeOtherFolder\\file.ifc");

            Assert.Equal(tempFolder, result);
        }

        [Fact]
        public void GetInitialDirectory_WithEmptyLastUsedFolder_FallsBackToFileDirectory()
        {
            var tempFolder = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var filePath = Path.Combine(tempFolder, "model.ifc");

            var result = MainViewModel.GetInitialDirectory("", filePath);

            Assert.Equal(tempFolder, result);
        }

        [Fact]
        public void GetInitialDirectory_WithNonExistentLastUsedFolder_FallsBackToFileDirectory()
        {
            var tempFolder = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var filePath = Path.Combine(tempFolder, "model.ifc");
            var nonExistentFolder = @"C:\NonExistent_Folder_12345_XYZ";

            var result = MainViewModel.GetInitialDirectory(nonExistentFolder, filePath);

            Assert.Equal(tempFolder, result);
        }

        [Fact]
        public void GetInitialDirectory_WithInvalidPaths_ReturnsEmptyString()
        {
            var result = MainViewModel.GetInitialDirectory(@"C:\NonExistent_1", @"C:\NonExistent_2\model.ifc");
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void UserSettings_Serialization_PreservesLastUsedFolders()
        {
            var settings = new UserSettings
            {
                FilePathOldIFC = @"C:\Old\model.ifc",
                FilePathNewIFC = @"C:\New\model.ifc",
                FilePathIFCToQA = @"C:\New\model_QA.ifc",
                LastUsedFolderOld = @"C:\Old",
                LastUsedFolderNew = @"C:\New",
                DataGridContentIFCEntities = new ObservableCollection<IfcEntity>()
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var deserialized = JsonSerializer.Deserialize<UserSettings>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(@"C:\Old", deserialized.LastUsedFolderOld);
            Assert.Equal(@"C:\New", deserialized.LastUsedFolderNew);
            Assert.Equal(@"C:\Old\model.ifc", deserialized.FilePathOldIFC);
            Assert.Equal(@"C:\New\model.ifc", deserialized.FilePathNewIFC);
        }

        [Fact]
        public void UserSettings_FromViewModel_CopiesLastUsedFolders()
        {
            var vm = new MainViewModel
            {
                FilePathOldIFC = @"C:\Old\model.ifc",
                FilePathNewIFC = @"C:\New\model.ifc",
                FilePathIFCToQA = @"C:\New\model_QA.ifc",
                LastUsedFolderOld = @"C:\Old",
                LastUsedFolderNew = @"C:\New"
            };

            var settings = new UserSettings(vm);

            Assert.Equal(@"C:\Old", settings.LastUsedFolderOld);
            Assert.Equal(@"C:\New", settings.LastUsedFolderNew);
        }

        [Fact]
        public async Task Comparison_RunTwiceOnSameFiles_ProducesIdenticalResults()
        {
            // Setup temporary IFC models
            var tempDir = Path.Combine(Path.GetTempPath(), "IfcRepeatTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var oldFile = Path.Combine(tempDir, "ModelOld.ifc");
                var newFile = Path.Combine(tempDir, "ModelNew.ifc");
                var qaFile = Path.Combine(tempDir, "ModelNew_QA.ifc");

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

                // Create identical models with one beam
                using (var mOld = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
                {
                    using (var txn = mOld.BeginTransaction("Create beam"))
                    {
                        var proj = mOld.Instances.New<Xbim.Ifc4.Kernel.IfcProject>(p => p.Name = "Project");
                        var beam = mOld.Instances.New<Xbim.Ifc4.SharedBldgElements.IfcBeam>(b =>
                        {
                            b.Name = "TestBeam";
                            b.GlobalId = Guid.NewGuid();
                        });
                        txn.Commit();
                    }
                    mOld.SaveAs(oldFile);
                }

                // Copy old to new so they start identical
                File.Copy(oldFile, newFile, true);

                var entities = new List<IfcEntity>
                {
                    new IfcEntity
                    {
                        Entity = "IIfcBeam",
                        PSetName = "QA_PSET",
                        ComparisonMethod = "Identifier",
                        CompareGeometry = false,
                        EnableGhostOverlays = true
                    }
                };

                // FIRST RUN
                File.Copy(newFile, qaFile, true);
                using (var oldStore1 = IfcStore.Open(oldFile, editor, accessMode: Xbim.IO.XbimDBAccess.ReadWrite))
                using (var qaStore1 = IfcStore.Open(qaFile, editor, accessMode: Xbim.IO.XbimDBAccess.ReadWrite))
                {
                    var comparer1 = await IfcComparer.CreateAsync(oldStore1, qaStore1, qaFile, "Run1", entities);
                    await comparer1.CompareAllRevisions();

                    Assert.Empty(comparer1.IfcComparisonResult.ModifiedNewObjects);
                    Assert.Empty(comparer1.IfcComparisonResult.DeletedGhostObjects);
                    Assert.Empty(comparer1.IfcComparisonResult.InsertedObjects);
                }

                // SECOND RUN (freshly copying newFile to qaFile, simulating MainViewModel's new behavior)
                File.Copy(newFile, qaFile, true);
                using (var oldStore2 = IfcStore.Open(oldFile, editor, accessMode: Xbim.IO.XbimDBAccess.ReadWrite))
                using (var qaStore2 = IfcStore.Open(qaFile, editor, accessMode: Xbim.IO.XbimDBAccess.ReadWrite))
                {
                    var comparer2 = await IfcComparer.CreateAsync(oldStore2, qaStore2, qaFile, "Run2", entities);
                    await comparer2.CompareAllRevisions();

                    // Verify the second run yields exactly identical results (no moved objects, no ghosts)
                    Assert.Empty(comparer2.IfcComparisonResult.ModifiedNewObjects);
                    Assert.Empty(comparer2.IfcComparisonResult.DeletedGhostObjects);
                    Assert.Empty(comparer2.IfcComparisonResult.InsertedObjects);
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
            }
        }
    }
}
