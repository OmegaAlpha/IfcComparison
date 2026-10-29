using IfcComparison.ViewModels;
using System;
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
            LastUsedFolderOld = vm.LastUsedFolderOld;
            LastUsedFolderNew = vm.LastUsedFolderNew;
            DataGridContentIFCEntities = vm.DataGridContentIFCEntities;
        }
    }
}
