// See https://aka.ms/new-console-template for more information
using System;
using System.Runtime.ExceptionServices;
using XwaSmootherEngine;

namespace MyApp // Note: actual namespace depends on the project name.
{
    internal class XwaAutoSmoother
    {
        private static void ParseArgs(string[] args, ref bool force)
        {
            if (args.Length == 0)
            {
                force = false;
                return;
            }

            if (args.Length == 1)
            {
                if (args[0].ToLower().Equals("--force"))
                {
                    force = true;
                }
                return;
            }

            force = false;
            return;
        }

        static void Main(string[] args)
        {
            // Assume we're running inside the FlightModels directory and there's a Thresholds directory
            // with all the profiles to be applied
            string sThreshPath = ".\\Thresholds";
            string sOPTPath = ".\\";
            bool force = false;
            int totalOPTs = 0;
            ParseArgs(args, ref force);

            if (!Directory.Exists(sThreshPath))
            {
                Console.WriteLine("Path: " + sThreshPath + " does not exist. Aborting");
                return;
            }

            if (!Directory.Exists(sOPTPath))
            {
                Console.WriteLine("Path: " + sOPTPath + " does not exist. Aborting");
                return;
            }

            // Enumerate all the threshold profiles
            string[] sThreshFiles = Directory.GetFiles(sThreshPath);
            foreach (string sThreshFile in sThreshFiles)
            {
                //Console.WriteLine(sThreshFile);
                if (SmootherEngine.ApplyThresholdProfile(sThreshFile, sOPTPath, force))
                    totalOPTs++;
            }
            Console.WriteLine(totalOPTs + " OPTs smoothed");
        }
        
    }
}
