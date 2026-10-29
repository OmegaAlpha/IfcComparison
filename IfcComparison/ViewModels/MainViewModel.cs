using IfcComparison.Enumerations;
using IfcComparison.Logging;
using IfcComparison.Models;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IfcComparison.Utils;
using Xbim.Ifc;
using Xbim.IO.Xml.BsConf;

namespace IfcComparison.ViewModels
{
    public class MainViewModel : NotifierBase, IUserSettings
    {

        #region Properties

        private string _user = Environment.UserName;
        private string _semanticVersion = string.Empty;
        private string _appName = Assembly.GetExecutingAssembly().GetName().Name;

        private string mFilePathOldIFC;
        public string FilePathOldIFC { get => mFilePathOldIFC; set => SetNotify(ref mFilePathOldIFC, value); }

        private string mFilePathNewIFC;
        public string FilePathNewIFC { get => mFilePathNewIFC; set => SetNotify(ref mFilePathNewIFC, value); }

        private string mFilePathIFCToQA;
        public string FilePathIFCToQA { get => mFilePathIFCToQA; set => SetNotify(ref mFilePathIFCToQA, value); }

        private string mLastUsedFolderOld;
        public string LastUsedFolderOld { get => mLastUsedFolderOld; set => SetNotify(ref mLastUsedFolderOld, value); }

        private string mLastUsedFolderNew;
        public string LastUsedFolderNew { get => mLastUsedFolderNew; set => SetNotify(ref mLastUsedFolderNew, value); }

        private string _loadedOldFilePath;
        private string _loadedNewFilePath;

        private string mIsOldIFCLoaded;
        public string IsOldIFCLoaded { get => mIsOldIFCLoaded; set => SetNotify(ref mIsOldIFCLoaded, value); }

        private string mIsNewIFCLoaded;
        public string IsNewIFCLoaded { get => mIsNewIFCLoaded; set => SetNotify(ref mIsNewIFCLoaded, value); }

        private string mIsNewIFCQALoaded;
        public string IsNewIFCQALoaded { get => mIsNewIFCQALoaded; set => SetNotify(ref mIsNewIFCQALoaded, value); }

        private string mOutputConsole;
        public string OutputConsole { get => mOutputConsole; set => SetNotify(ref mOutputConsole, value); }

        private string mUserSettingsPath;
        public string UserSettingsPath { get => mUserSettingsPath; set => SetNotify(ref mUserSettingsPath, value); }

        private string mVersion;
        public string Version { get => mVersion; set => SetNotify(ref mVersion, value); }
        public Window SearchWindow { get; set; }

        public IfcStore OldModel { get; set; }
        public IfcStore NewModel { get; set; }
        public IfcStore NewModelQA { get; set; }
        public List<Task<IfcStore>> ModelTaskList { get; set; }

        private DataGridCellInfo mCurrentCell;
        public DataGridCellInfo CurrentCell { get => mCurrentCell; set => SetNotify(ref mCurrentCell, value); }

        private IfcEntity mCurrentItem;
        public IfcEntity CurrentItem { get => mCurrentItem; set => SetNotify(ref mCurrentItem, value); }

        public ObservableCollection<string> ComparisonMethodCol { get; set; } = new ObservableCollection<string>();

        public ObservableCollection<IfcEntity> DataGridContentIFCEntities { get; set; } = new ObservableCollection<IfcEntity>();

        private bool isGeneratingIFCPset = false;
        public bool IsGeneratingIFCPset 
        { 
            get => isGeneratingIFCPset; 
            set => SetNotify(ref isGeneratingIFCPset, value); 
        }

        #endregion

        #region Commands

        private ICommand mBrowseOldIFCFileCommand { get; set; }
        public ICommand BrowseOldIFCFileCommand
        {
            get { return mBrowseOldIFCFileCommand ?? (mBrowseOldIFCFileCommand = new CommandHandler(() => GetIFCByName(nameof(BrowseOldIFCFileCommand)), true)); }
        }
        private ICommand mBrowseNewIFCFileCommand { get; set; }
        public ICommand BrowseNewIFCFileCommand
        {
            get { return mBrowseNewIFCFileCommand ?? (mBrowseNewIFCFileCommand = new CommandHandler(() => GetIFCByName(nameof(BrowseNewIFCFileCommand)), true)); }
        }
        private ICommand mBrowseQAIFCFileCommand { get; set; }
        public ICommand BrowseQAIFCFileCommand
        {
            get { return mBrowseQAIFCFileCommand ?? (mBrowseQAIFCFileCommand = new CommandHandler(() => GetIFCByName(nameof(BrowseQAIFCFileCommand)), true)); }
        }

        private ICommand mGenerateIFCPsetCommand { get; set; }
        public ICommand GenerateIFCPsetCommand
        {
            get { return mGenerateIFCPsetCommand ?? (mGenerateIFCPsetCommand = new CommandHandler(async () => await GenerateIFCPsetAsync(), true)); }
        }

        private ICommand mClearOutputCommand { get; set; }
        public ICommand ClearOutputCommand
        {
            get { return mClearOutputCommand ?? (mClearOutputCommand = new CommandHandler(() => ClearOutputText(), true)); }
        }

        private ICommand mSaveUserSettingsCommand { get; set; }
        public ICommand SaveUserSettingsCommand
        {
            get { return mSaveUserSettingsCommand ?? (mSaveUserSettingsCommand = new CommandHandler(() => SaveUserSettings(), true)); }
        }

        private ICommand mLoadUserSettingsCommand { get; set; }
        public ICommand LoadUserSettingsCommand
        {
            get { return mLoadUserSettingsCommand ?? (mLoadUserSettingsCommand = new CommandHandler(() => LoadUserSettings(), true)); }
        }

        private ICommand mLoadModelsCommand { get; set; }
        public ICommand LoadModelsCommand
        {
            get { return mLoadModelsCommand ?? (mLoadModelsCommand = new CommandHandler(() => GetIFCByName(), true)); }
        }

        private ICommand mCopyOutputCommand { get; set; }
        public ICommand CopyOutputCommand
        {
            get;
        }

        private RelayCommand mGetIfcEntityCommand { get; set; }
        public RelayCommand GetIfcEntityCommand
        {
            get { return mGetIfcEntityCommand ?? (mGetIfcEntityCommand = new RelayCommand(p => { GetIfcEntityFromTable(); }, GetIfcEntityFromTableCanUse)); }
        }
        private RelayCommand mBrowseUserSettingsCommand { get; set; }
        public RelayCommand BrowseUserSettingsCommand
        {
            get { return mBrowseUserSettingsCommand ?? (mBrowseUserSettingsCommand = new RelayCommand(p => { GetEntities(); }, p => true)); }
        }

        #endregion

        public MainViewModel()
        {
            // TODO: Check if files is stored in OneDrive. If so, warn user of possible issues of processes taking the file lock.

            var compList = Enum.GetNames(typeof(ComparisonEnumeration))
                .Cast<string>().ToList();
            foreach (var item in compList)
            {
                ComparisonMethodCol.Add(item);
            }

            // Ensure logger is initialized
            LoggerInitializer.EnsureInitialized();

            // Set up the logging service to append to OutputConsole asynchronously
            LoggingService.SetOutputConsoleAction(message =>
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        OutputConsole += message + Environment.NewLine;
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            });

            LoadUserSettings(@"IfcComparisonSettings\\DefaultSettings.json");
            UserSettingsPath = string.Empty;
            AutoLoadUserSettings();
            ClearOutputText();

            IsOldIFCLoaded = "Not Loaded";
            IsNewIFCLoaded = "Not Loaded";
            IsNewIFCQALoaded = "Not Loaded";

            var assVersion = Assembly.GetExecutingAssembly().GetName().Version;
            _semanticVersion = assVersion.ToString(3); // Get major, minor, and build version
            Version = $"{_appName} Version: {_semanticVersion}";

            GetEntities();
        }

        public static string GetInitialDirectory(string lastUsedFolder, string currentFilePath)
        {
            if (!string.IsNullOrWhiteSpace(lastUsedFolder))
            {
                try
                {
                    if (Directory.Exists(lastUsedFolder))
                    {
                        return lastUsedFolder;
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(currentFilePath))
            {
                try
                {
                    var dir = Path.GetDirectoryName(currentFilePath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        return dir;
                    }
                }
                catch { }
            }

            return string.Empty;
        }

        public static string GetAutoSaveSettingsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "IfcComparison");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return Path.Combine(dir, "UserSettings.json");
        }

        public void AutoSaveUserSettings()
        {
            try
            {
                var path = GetAutoSaveSettingsPath();
                var settings = new UserSettings(this);
                ReadAndWriteJson.WriteAndSerializeAtStartup(settings, path);
            }
            catch
            {
                // Non-critical background save
            }
        }

        public void AutoLoadUserSettings()
        {
            try
            {
                var path = GetAutoSaveSettingsPath();
                if (File.Exists(path))
                {
                    var settings = (UserSettings)ReadAndWriteJson.ReadAndDeserialize<UserSettings>(path);
                    if (settings != null)
                    {
                        if (!string.IsNullOrWhiteSpace(settings.LastUsedFolderOld))
                            LastUsedFolderOld = settings.LastUsedFolderOld;
                        if (!string.IsNullOrWhiteSpace(settings.LastUsedFolderNew))
                            LastUsedFolderNew = settings.LastUsedFolderNew;

                        if (!string.IsNullOrWhiteSpace(settings.FilePathOldIFC) && File.Exists(settings.FilePathOldIFC))
                            FilePathOldIFC = settings.FilePathOldIFC;
                        if (!string.IsNullOrWhiteSpace(settings.FilePathNewIFC) && File.Exists(settings.FilePathNewIFC))
                            FilePathNewIFC = settings.FilePathNewIFC;
                        if (!string.IsNullOrWhiteSpace(settings.FilePathIFCToQA))
                            FilePathIFCToQA = settings.FilePathIFCToQA;
                    }
                }
            }
            catch
            {
                // Non-critical background load
            }

            if (string.IsNullOrWhiteSpace(LastUsedFolderOld) && !string.IsNullOrWhiteSpace(FilePathOldIFC))
            {
                try { LastUsedFolderOld = Path.GetDirectoryName(FilePathOldIFC); } catch { }
            }
            if (string.IsNullOrWhiteSpace(LastUsedFolderNew) && !string.IsNullOrWhiteSpace(FilePathNewIFC))
            {
                try { LastUsedFolderNew = Path.GetDirectoryName(FilePathNewIFC); } catch { }
            }
        }

        private void GetEntities()
        {
            // This method is called during initialization to ensure IfcTools.IfcEntities is populated
            // The entities list is accessed statically when needed for the search window
            var entities = IfcTools.IfcEntities;
        }

        private void SaveUserSettings()
        {
            try
            {
                if (File.Exists(UserSettingsPath))
                {
                    var userSettings = new UserSettings(this);
                    ReadAndWriteJson.WriteAndSerializeAtStartup(userSettings, UserSettingsPath);
                    OutputConsole += $"Settings saved at: {UserSettingsPath}" + Environment.NewLine;
                }
                else
                {
                    SaveFileDialog saveFileDialog = new SaveFileDialog();
                    saveFileDialog.FileName = "Settings.json";
                    saveFileDialog.Filter = "Json files (*.json)|*.json";
                    if (saveFileDialog.ShowDialog() == true)
                    {
                        UserSettingsPath = saveFileDialog.FileName;
                        OutputConsole += $"Setting saved at: {UserSettingsPath}" + Environment.NewLine;
                        ReadAndWriteJson.WriteAndSerializeAtStartup(new UserSettings(this), UserSettingsPath);
                    }
                }
                AutoSaveUserSettings();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadUserSettings(string userSettingsPath = "")
        {
            if (string.IsNullOrEmpty(userSettingsPath))
            {
                UserSettingsPath = Common.OpenFileDialogPath("json", "");
            }
            else
            {
                UserSettingsPath = userSettingsPath;
            }

            if (!string.IsNullOrEmpty(UserSettingsPath))
            {
                var userSettings = new UserSettings();
                PropertyInfo[] uSetProps;

                userSettings = (UserSettings)ReadAndWriteJson.ReadAndDeserialize<UserSettings>(UserSettingsPath);
                if (userSettings != null)
                {
                    uSetProps = userSettings.GetType().GetProperties();
                    if (uSetProps != null)
                    {
                        foreach (var uSetProp in uSetProps)
                        {
                            var uSetval = uSetProp.GetValue(userSettings);
                            var propName = uSetProp.Name;
                            var vmProp = this.GetType().GetProperty(propName);
                            if (vmProp != null)
                            {
                                if (!(uSetval is ICollection))
                                {
                                    vmProp.SetValue(this, uSetval);
                                }
                                else
                                {
                                    this.DataGridContentIFCEntities.Clear();
                                    foreach (var item in (ICollection<IfcEntity>)uSetval)
                                    {
                                        // Normalize entity names to interface format (with leading "I")
                                        // This ensures backward compatibility with old settings that may have either format
                                        if (!string.IsNullOrEmpty(item.Entity))
                                        {
                                            item.Entity = IfcTools.DisplayNameToInterfaceName(item.Entity);
                                        }
                                        this.DataGridContentIFCEntities.Add(item);
                                    }
                                }
                            }
                        }

                        if (string.IsNullOrWhiteSpace(LastUsedFolderOld) && !string.IsNullOrWhiteSpace(FilePathOldIFC))
                        {
                            try { LastUsedFolderOld = Path.GetDirectoryName(FilePathOldIFC); } catch { }
                        }
                        if (string.IsNullOrWhiteSpace(LastUsedFolderNew) && !string.IsNullOrWhiteSpace(FilePathNewIFC))
                        {
                            try { LastUsedFolderNew = Path.GetDirectoryName(FilePathNewIFC); } catch { }
                        }
                        AutoSaveUserSettings();

                        OutputConsole += $"Settings loaded from {UserSettingsPath}" + Environment.NewLine;
                    }
                    else
                    {
                        MessageBox.Show("Json file doesn't seem to be valid.");
                        OutputConsole += $"Settings loaded from {UserSettingsPath} failed." + Environment.NewLine;
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("Json file doesn't seem to be valid.");
                    OutputConsole += $"Settings loaded from {UserSettingsPath} failed." + Environment.NewLine;
                    return;
                }
            }
        }

        private void ClearOutputText()
        {
            OutputConsole = "";
        }

        private async void GetIFCByName(string commandName)
        {
            var strLoaded = "Loaded";
            var strNotLoaded = "Not Loaded";

            try
            {
                if (commandName == nameof(BrowseOldIFCFileCommand))
                {
                    var initialDir = GetInitialDirectory(LastUsedFolderOld, FilePathOldIFC);
                    var fileName = Common.OpenFileDialogPath("ifc", initialDir);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        return;
                    }

                    LastUsedFolderOld = Path.GetDirectoryName(fileName);
                    AutoSaveUserSettings();

                    if (OldModel != null)
                    {
                        try { OldModel.Close(); } catch { }
                        OldModel = null;
                    }

                    IsOldIFCLoaded = strNotLoaded;
                    FilePathOldIFC = fileName;
                    OutputConsole += $"IFC file: {Path.GetFileName(FilePathOldIFC)} is loading..." + Environment.NewLine;
                    OldModel = await OpenIFCModelAsync(FilePathOldIFC);
                    if (OldModel == null)
                    {
                        return;
                    }
                    _loadedOldFilePath = FilePathOldIFC;
                    IsOldIFCLoaded = strLoaded;
                    OutputConsole += $"IFC file: {Path.GetFileName(FilePathOldIFC)} {strLoaded}" + Environment.NewLine;
                }
                else if (commandName == nameof(BrowseNewIFCFileCommand))
                {
                    var initialDir = GetInitialDirectory(LastUsedFolderNew, FilePathNewIFC);
                    var fileName = Common.OpenFileDialogPath("ifc", initialDir);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        return;
                    }

                    LastUsedFolderNew = Path.GetDirectoryName(fileName);
                    AutoSaveUserSettings();

                    if (NewModelQA != null)
                    {
                        try { NewModelQA.Close(); } catch { }
                        NewModelQA = null;
                    }

                    IsNewIFCLoaded = strNotLoaded;
                    IsNewIFCQALoaded = strNotLoaded;
                    FilePathNewIFC = fileName;
                    OutputConsole += $"IFC file: {Path.GetFileName(FilePathNewIFC)} is loading..." + Environment.NewLine;
                    FilePathIFCToQA = Path.Combine(Path.GetDirectoryName(fileName), Path.GetFileNameWithoutExtension(fileName) + "_QA" + Path.GetExtension(fileName));

                    var qaDir = Path.GetDirectoryName(FilePathIFCToQA);
                    if (!string.IsNullOrEmpty(qaDir) && !Directory.Exists(qaDir))
                    {
                        Directory.CreateDirectory(qaDir);
                    }

                    File.Copy(FilePathNewIFC, FilePathIFCToQA, true);
                    OutputConsole += $"IFC file: {Path.GetFileName(FilePathIFCToQA)} is loading..." + Environment.NewLine;

                    IsNewIFCLoaded = strLoaded;
                    OutputConsole += $"IFC file: {Path.GetFileName(FilePathNewIFC)} {strLoaded}" + Environment.NewLine;

                    NewModelQA = await OpenIFCModelAsync(FilePathIFCToQA);
                    if (NewModelQA == null)
                    {
                        return;
                    }
                    _loadedNewFilePath = FilePathNewIFC;
                    IsNewIFCQALoaded = strLoaded;
                    OutputConsole += $"IFC file: {Path.GetFileName(FilePathIFCToQA)} {strLoaded}" + Environment.NewLine;
                }
            }
            catch (Exception ex)
            {
                // async void: any unhandled exception here would terminate the application.
                OutputConsole += $"Error loading IFC file: {ex.Message}" + Environment.NewLine;
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task GetIFCByName()
        {
            var strLoaded = "Loaded";
            var strNotLoaded = "Not Loaded";

            if (string.IsNullOrEmpty(FilePathOldIFC) || string.IsNullOrEmpty(FilePathNewIFC))
            {
                return;
            }

            if (OldModel != null)
            {
                try { OldModel.Close(); } catch { }
                OldModel = null;
            }
            if (NewModelQA != null)
            {
                try { NewModelQA.Close(); } catch { }
                NewModelQA = null;
            }

            try
            {
                if (string.IsNullOrEmpty(FilePathIFCToQA))
                {
                    FilePathIFCToQA = Path.Combine(Path.GetDirectoryName(FilePathNewIFC), Path.GetFileNameWithoutExtension(FilePathNewIFC) + "_QA" + Path.GetExtension(FilePathNewIFC));
                }

                var qaDir = Path.GetDirectoryName(FilePathIFCToQA);
                if (!string.IsNullOrEmpty(qaDir) && !Directory.Exists(qaDir))
                {
                    Directory.CreateDirectory(qaDir);
                }

                File.Copy(FilePathNewIFC, FilePathIFCToQA, true);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return;
            }

            IsOldIFCLoaded = strNotLoaded;
            OutputConsole += $"IFC file: {Path.GetFileName(FilePathOldIFC)} is loading..." + Environment.NewLine;

            IsNewIFCLoaded = strNotLoaded;
            OutputConsole += $"IFC file: {Path.GetFileName(FilePathNewIFC)} is loading..." + Environment.NewLine;

            IsNewIFCQALoaded = strNotLoaded;
            OutputConsole += $"IFC file: {Path.GetFileName(FilePathIFCToQA)} is loading..." + Environment.NewLine;

            var tasks = new List<Task<IfcStore>>();
            tasks.Add(OpenIFCModelAsync(FilePathOldIFC));
            tasks.Add(OpenIFCModelAsync(FilePathIFCToQA));
            var tasksArr = tasks.ToArray();

            var results = Task.WhenAll(tasksArr);
            await results;

            OldModel = await tasks[0];
            NewModelQA = await tasks[1];

            if (OldModel == null || NewModelQA == null)
            {
                return;
            }

            _loadedOldFilePath = FilePathOldIFC;
            _loadedNewFilePath = FilePathNewIFC;

            IsOldIFCLoaded = strLoaded;
            OutputConsole += $"IFC file: {Path.GetFileName(FilePathOldIFC)} {strLoaded}" + Environment.NewLine;

            IsNewIFCLoaded = strLoaded;
            OutputConsole += $"IFC file: {Path.GetFileName(FilePathNewIFC)} {strLoaded}" + Environment.NewLine;

            IsNewIFCQALoaded = strLoaded;
            OutputConsole += $"IFC file: {Path.GetFileName(FilePathIFCToQA)} {strLoaded}" + Environment.NewLine;
        }

        private async Task GenerateIFCPsetAsync()
        {
            try
            {
                // Set busy indicator
                IsGeneratingIFCPset = true;

                if (string.IsNullOrEmpty(FilePathOldIFC) || !File.Exists(FilePathOldIFC))
                {
                    MessageBox.Show("Old IFC file does not exist! Please verify the path.");
                    return;
                }

                if (string.IsNullOrEmpty(FilePathNewIFC) || !File.Exists(FilePathNewIFC))
                {
                    MessageBox.Show("New IFC file does not exist! Please verify the path.");
                    return;
                }

                if (DataGridContentIFCEntities.Count == 0)
                {
                    MessageBox.Show("No information set in the IFC Entity settings!");
                    return;
                }

                if (string.IsNullOrEmpty(FilePathIFCToQA))
                {
                    FilePathIFCToQA = Path.Combine(
                        Path.GetDirectoryName(FilePathNewIFC),
                        Path.GetFileNameWithoutExtension(FilePathNewIFC) + "_QA" + Path.GetExtension(FilePathNewIFC));
                }

                var qaDir = Path.GetDirectoryName(FilePathIFCToQA);
                if (!string.IsNullOrEmpty(qaDir) && !Directory.Exists(qaDir))
                {
                    Directory.CreateDirectory(qaDir);
                }

                Action<string> reportProgress = (message) => 
                {
                    Application.Current?.Dispatcher?.Invoke(() => 
                    {
                        OutputConsole += message + Environment.NewLine;
                    });
                };

                reportProgress("Starting IFC Comparison...");

                // 1. Ensure any previous QA model instance is closed and unlocked before overwriting
                if (NewModelQA != null)
                {
                    try { NewModelQA.Close(); } catch { }
                    NewModelQA = null;
                }

                // 2. Always prepare a fresh copy from New IFC so that re-running comparison begins from a clean baseline
                reportProgress($"Preparing fresh QA model from {Path.GetFileName(FilePathNewIFC)}...");
                File.Copy(FilePathNewIFC, FilePathIFCToQA, true);

                reportProgress($"Opening QA model: {Path.GetFileName(FilePathIFCToQA)}...");
                NewModelQA = await OpenIFCModelAsync(FilePathIFCToQA);
                if (NewModelQA == null)
                {
                    reportProgress("Failed to open QA model.");
                    return;
                }
                _loadedNewFilePath = FilePathNewIFC;
                IsNewIFCQALoaded = "Loaded";

                // 3. Ensure OldModel is loaded and matches FilePathOldIFC
                if (OldModel == null || _loadedOldFilePath != FilePathOldIFC)
                {
                    if (OldModel != null)
                    {
                        try { OldModel.Close(); } catch { }
                        OldModel = null;
                    }

                    reportProgress($"Opening old IFC model: {Path.GetFileName(FilePathOldIFC)}...");
                    OldModel = await OpenIFCModelAsync(FilePathOldIFC);
                    if (OldModel == null)
                    {
                        reportProgress("Failed to open Old IFC model.");
                        return;
                    }
                    _loadedOldFilePath = FilePathOldIFC;
                    IsOldIFCLoaded = "Loaded";
                }

                var entitiesList = DataGridContentIFCEntities.ToList();

                // 4. Run the heavy comparison operation on a background thread
                await Task.Run(async () =>
                {
                    reportProgress("Creating IFC comparer...");
                    var comparer = await Models.IfcComparer.CreateAsync(
                        OldModel,
                        NewModelQA,
                        FilePathIFCToQA,
                        "Transaction",
                        entitiesList);
                    
                    reportProgress("Starting comparison of all entities...");
                    await comparer.CompareAllRevisions();
                });

                reportProgress("IFC Comparison completed and written to file: " + FilePathIFCToQA);
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher?.Invoke(() => 
                {
                    OutputConsole += $"Error during IFC comparison: {ex.Message}" + Environment.NewLine;
                    MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                // Always close NewModelQA after saving so FilePathIFCToQA is immediately unlocked on disk
                // and cannot pollute memory or a subsequent comparison run.
                if (NewModelQA != null)
                {
                    try { NewModelQA.Close(); } catch { }
                    NewModelQA = null;
                }
                IsNewIFCQALoaded = "Loaded";

                // Always reset busy indicator
                IsGeneratingIFCPset = false;
            }
        }

        private async Task GenerateIFCPset()
        {
            await GenerateIFCPsetAsync();
        }

        private async Task<IfcStore> OpenIFCModelAsync(string fileName)
        {
            try
            {
                return await Task.Run(() => OpenModel(fileName));
            }
            catch (Exception ex)
            {
                var schema = GetIfcSchemaFromHeader(fileName);
                var schemaInfo = string.IsNullOrEmpty(schema) ? "" : $" (schema: {schema}, supported: IFC2X3, IFC4, IFC4X3)";
                var message = $"Failed to open IFC file {Path.GetFileName(fileName)}{schemaInfo}: {ex.Message}";
                OutputConsole += message + Environment.NewLine;
                MessageBox.Show(message, "Error opening IFC", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private static string GetIfcSchemaFromHeader(string fileName)
        {
            try
            {
                foreach (var line in File.ReadLines(fileName).Take(50))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"FILE_SCHEMA\s*\(\s*\(\s*'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                        return match.Groups[1].Value;
                }
            }
            catch
            {
                // Header sniffing is best-effort only.
            }
            return null;
        }

        private IfcStore OpenModel(string fileName)
        {
            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "FKJN",
                ApplicationFullName = _appName,
                ApplicationIdentifier = "QA BIM APP",
                ApplicationVersion = _semanticVersion,
                EditorsFamilyName = _user,
                EditorsGivenName = _user,
                EditorsOrganisationName = "COWI AS"
            };

            return IfcStore.Open(fileName, editor, accessMode: Xbim.IO.XbimDBAccess.ReadWrite);
        }

        private void GetIfcEntityFromTable()
        {
            if (SearchWindow == null || !SearchWindow.IsVisible)
            {
                SearchWindow = new SearchWindow(CurrentCell);
            }

            SearchWindow.Focus();
            var cell = new DataGridCell();

            var curCell = CurrentCell;
            var point = new Point();
            if (curCell != null)
            {
                var cellContent = curCell.Column.GetCellContent(curCell.Item);
                if (cellContent != null)
                {
                    cell = cellContent.Parent as DataGridCell;
                    if (cell != null)
                    {
                        // Get DPI scaling factors to handle non-100% Windows display scaling
                        var source = System.Windows.PresentationSource.FromVisual(cell);
                        if (source != null)
                        {
                            var dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                            var dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                            
                            point = cell.PointToScreen(point);
                            // Convert physical pixels back to DIPs for WPF window positioning
                            SearchWindow.Left = point.X / dpiScaleX;
                            SearchWindow.Top = point.Y / dpiScaleY + cell.ActualHeight;
                        }
                    }
                }
            }
            SearchWindow.Show();
        }

        private bool GetIfcEntityFromTableCanUse(object obj)
        {
            var cell = new DataGridCell();
            var curCell = CurrentCell;
            DataGridColumn col = null;

            if (curCell != null)
            {
                var cellContent = curCell.Column.GetCellContent(curCell.Item);
                if (cellContent != null)
                {
                    cell = cellContent.Parent as DataGridCell;
                    col = cell.Column;
                }
            }

            if ((string)col?.Header == "IFC Entity")
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
