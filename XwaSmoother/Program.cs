using JeremyAnsel.Xwa.Opt;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using XwaSmoother;

namespace XwaOpter
{
    class Program
    {
#if DISABLED
        static string GetOpenFile()
        {
            var dialog = new OpenFileDialog();
            dialog.DefaultExt = ".opt";
            dialog.CheckFileExists = true;
            dialog.Filter = "OPT files (*.opt)|*.opt";

            Config config = Config.ReadConfigFile();

            if (!string.IsNullOrEmpty(config.OpenOptDirectory))
            {
                dialog.InitialDirectory = config.OpenOptDirectory;
            }

            if (dialog.ShowDialog() == true)
            {
                config.OpenOptDirectory = Path.GetDirectoryName(dialog.FileName);
                config.SaveConfigFile();

                return dialog.FileName;
            }

            return null;
        }
#endif

#if DISABLED
        static string GetSaveAsFile(string fileName)
        {
            fileName = Path.GetFullPath(fileName);
            var dialog = new SaveFileDialog();
            dialog.AddExtension = true;
            dialog.DefaultExt = ".opt";
            dialog.Filter = "OPT files (*.opt)|*.opt";
            dialog.InitialDirectory = Path.GetDirectoryName(fileName);
            dialog.FileName = Path.GetFileName(fileName);

            Config config = Config.ReadConfigFile();

            if (!string.IsNullOrEmpty(config.SaveOptDirectory))
            {
                dialog.InitialDirectory = config.SaveOptDirectory;
            }

            if (dialog.ShowDialog() == true)
            {
                config.SaveOptDirectory = Path.GetDirectoryName(dialog.FileName);
                config.SaveConfigFile();

                return dialog.FileName;
            }

            return null;
        }
#endif

        [STAThread]
        static void TestThresholdParser(string[] args)
        {
            List<string> sThresholds = new List<string>();
            sThresholds.Add("1,2, 4, 7:30");
            sThresholds.Add("13 .. 18 : 50");
            //sThresholds.Add("-1 : 25");
            Dictionary<int, float> Thresholds = SmootherEngine.ParseIndices(sThresholds, out string sError);
            Console.WriteLine("Final thresholds:\n");
            foreach (var key in Thresholds.Keys)
            {
                Console.WriteLine("Key: " + key + " --> " + Thresholds[key]);
            }
            Console.WriteLine("Error: " + sError);
            Console.ReadKey();
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
