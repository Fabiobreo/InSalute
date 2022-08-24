using NETCore.Encrypt;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;

namespace InSalute.Utilities
{
    public static class Settings
    {
        public static string saved_username;
        public static string saved_password;

        public static void LoadSettings()
        {
            saved_username = ConfigurationManager.AppSettings["Username"];
            saved_password = ConfigurationManager.AppSettings["Password"];
        }

        public static void SaveSettings()
        {
            EditSetting("Username", EncryptProvider.Base64Encrypt(saved_username));
            EditSetting("Password", EncryptProvider.Base64Encrypt(saved_password));
        }

        public static void EditSetting(string key, string value)
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            config.AppSettings.Settings[key].Value = value;
            config.Save(ConfigurationSaveMode.Modified);
        }
    }
}
