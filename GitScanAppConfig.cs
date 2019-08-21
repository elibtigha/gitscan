using System;
using System.IO;
using IniParser.Model;
using IniParser.Parser;

namespace OctokitDemo
{
    public class GitScanAppConfig
    {
        private const string ConfigFilePath = @"C:\GitScanConfigFiles\gitscan.ini";

        private static readonly Lazy<IniData> ParsedData = new Lazy<IniData>(
            () =>
            {
                var parser = new IniDataParser();
                IniData parsedData;
                using (FileStream fs = File.Open(ConfigFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (StreamReader sr = new StreamReader(fs, System.Text.Encoding.UTF8))
                    {
                        parsedData = parser.Parse(sr.ReadToEnd());
                    }
                }

                return parsedData;
            });

        public static string GetValue(string section, string key)
        {
            return ParsedData.Value[section][key];
        }
    }
}