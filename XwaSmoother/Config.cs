using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace XwaOpter
{
    class Config
    {
        private const string ConfigFilename = "XwaOpter.cfg";

        private static readonly Encoding _encoding = Encoding.GetEncoding("iso-8859-1");

        public string OpenOptDirectory { get; set; }

        public string SaveOptDirectory { get; set; }

        public static Config ReadConfigFile()
        {
            var config = new Config();

            if (!File.Exists(ConfigFilename))
            {
                return config;
            }

            string[] lines = File.ReadAllLines(ConfigFilename, _encoding);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                switch (i)
                {
                    case 0:
                        config.OpenOptDirectory = line;
                        break;

                    case 1:
                        config.SaveOptDirectory = line;
                        break;
                }
            }

            return config;
        }

        public void SaveConfigFile()
        {
            var lines = new List<string>();

            lines.Add(this.OpenOptDirectory);
            lines.Add(this.SaveOptDirectory);

            File.WriteAllLines(ConfigFilename, lines.ToArray(), _encoding);
        }
    }
}
