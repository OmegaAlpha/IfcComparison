using System;
using System.IO;
using Microsoft.Win32;

namespace IfcComparison.Utils
{
    public class Common
    {
        public static string OpenFileDialogPath(string fileFilter, string initialDir)
        {
            var openFileDialog = new OpenFileDialog();
            var filter = string.Empty;
            if (fileFilter == "xlsx")
            {
                filter = "xlsx files (*.xlsx)|*.xlsx|All files (*.*)|*.*";
            }
            else if (fileFilter == "ifc")
            {
                filter = "ifc files (*.ifc)|*.ifc|All files (*.*)|*.*";
            }
            else if (fileFilter == "xml")
            {
                filter = "xml files (*.xml)|*.xml|All files (*.*)|*.*";
            }
            else if (fileFilter == "json")
            {
                filter = "json files (*.json)|*.json|All files (*.*)|*.*";
            }
            else
            {
                filter = $"{fileFilter} files (*{fileFilter})|*{fileFilter}|All files (*.*)|*.*";
            }

            var filePath = string.Empty;

            openFileDialog.InitialDirectory = initialDir;
            openFileDialog.Filter = filter;
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() == true)
            {
                filePath = openFileDialog.FileName;
            }
            return filePath;
        }

        public static string OpenFolderDialogPath()
        {
            var folderDialog = new OpenFolderDialog();
            var folderPath = string.Empty;

            if (folderDialog.ShowDialog() == true)
            {
                folderPath = folderDialog.FolderName;
            }
            return folderPath;
        }

        public static bool IsValidFolderPath(string folderPath)
        {
            try
            {
                return Directory.Exists(folderPath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool IsValidFilePath(string filePath)
        {
            try
            {
                return File.Exists(filePath);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
