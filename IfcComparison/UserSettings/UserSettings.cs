using IfcComparison.ViewModels;
using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IfcComparison
{
    public class UserSettings : IUserSettings
    {
        public string FilePathOldIFC { get; set; }
        public string FilePathNewIFC { get; set; }
        public string FilePathIFCToQA { get; set; }
        public string LastUsedFolderOld { get; set; }
        public string LastUsedFolderNew { get; set; }
        public ObservableCollection<IfcEntity> DataGridContentIFCEntities { get; set; }

        public UserSettings()
        {

        }

        internal UserSettings(MainViewModel vm)
        {
            FilePathOldIFC = vm.FilePathOldIFC;
            FilePathNewIFC = vm.FilePathNewIFC;
            FilePathIFCToQA = vm.FilePathIFCToQA;
            LastUsedFolderOld = !string.IsNullOrWhiteSpace(vm.LastUsedFolderOld)
                ? vm.LastUsedFolderOld
                : (!string.IsNullOrWhiteSpace(vm.FilePathOldIFC) ? GetDirectorySafely(vm.FilePathOldIFC) : string.Empty);
            LastUsedFolderNew = !string.IsNullOrWhiteSpace(vm.LastUsedFolderNew)
                ? vm.LastUsedFolderNew
                : (!string.IsNullOrWhiteSpace(vm.FilePathNewIFC) ? GetDirectorySafely(vm.FilePathNewIFC) : string.Empty);
            DataGridContentIFCEntities = vm.DataGridContentIFCEntities;
        }

        private static string GetDirectorySafely(string path)
        {
            try
            {
                return Path.GetDirectoryName(path) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
