using System;
using System.IO;
using Newtonsoft.Json;

namespace IfcComparison.Utils
{
    public class ReadAndWriteJson
    {
        public string UserSettingFilePath { get; set; }
        public bool SettingsFileExist { get; set; } = false;

        private string cowiBridgeFolder;
        private string fileName;

        public ReadAndWriteJson(string folderName, string fileName, object settings)
        {
            UserSettingFilePath = Path.Combine(folderName, fileName);
            SetFilePath(folderName, fileName);
            if (!SettingsFileExist)
            {
                WriteAndSerializeAtStartup(settings, UserSettingFilePath);
            }
        }

        public ReadAndWriteJson(string fullPath, object settings)
        {
            UserSettingFilePath = fullPath;
            if (!SettingsFileExist)
            {
                WriteAndSerializeAtStartup(settings, UserSettingFilePath);
            }
        }

        private void SetFilePath(string folderName, string fileName)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            this.cowiBridgeFolder = folderName;

            if (!Path.HasExtension(fileName))
            {
                this.fileName = fileName + ".json";
            }
            else
            {
                this.fileName = fileName;
            }

            UserSettingFilePath = Path.Combine(appData, cowiBridgeFolder, this.fileName);
            var dir = Path.GetDirectoryName(UserSettingFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            if (!File.Exists(UserSettingFilePath))
            {
                File.Create(UserSettingFilePath).Close();
                SettingsFileExist = false;
            }
            else
            {
                SettingsFileExist = true;
            }
        }

        /// <summary>
        /// Read and deserialize UserSettings file
        /// </summary>
        public static object ReadAndDeserialize<T>(string filePath)
        {
            if (File.Exists(filePath))
            {
                var jsonText = File.ReadAllText(filePath);
                var settings = JsonConvert.DeserializeObject<T>(jsonText);
                return settings;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Write and serialize json at startup if UserSettings file doesn't exist
        /// </summary>
        public static void WriteAndSerializeAtStartup(object userSettings, string filePath)
        {
            File.WriteAllText(filePath, JsonConvert.SerializeObject(userSettings, Formatting.Indented));
        }

        /// <summary>
        /// Write string path to specified property
        /// </summary>
        public static void WriteAndSerializeElement(string path, string property, object settings, string filePath)
        {
            var json = File.ReadAllText(filePath);
            dynamic jsonObj = JsonConvert.DeserializeObject(json);
            jsonObj[property] = path;
            string output = JsonConvert.SerializeObject(jsonObj, Formatting.Indented);
            File.WriteAllText(filePath, output);
        }
    }
}
