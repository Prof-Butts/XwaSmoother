using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using XwaSmootherEngine;

namespace XwaSmoother
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void inputFileButton_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string sInFileName = openFileDialog.FileName;
                inputFileTextBox.Text = sInFileName;
                // Automatically populate a suggested output file
                string sInPath = Path.GetDirectoryName(sInFileName);
                string sOutFileName = Path.GetFileNameWithoutExtension(sInFileName);
                if (overwriteCheckBox.Checked)
                    sOutFileName = Path.Combine(sInPath, sOutFileName + ".opt");
                else
                    sOutFileName = Path.Combine(sInPath, sOutFileName + "-new.opt");
                outputFileTextBox.Text = sOutFileName;
                if (File.Exists(sOutFileName) && !overwriteCheckBox.Checked)
                {
                    MessageBox.Show("Suggested output file already exists. It will be overwritten",
                        "Output File Exists", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void outputFileButton_Click(object sender, EventArgs e)
        {
            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                outputFileTextBox.Text = saveFileDialog.FileName;
            }
        }

        private bool ValidateInputTextBox()
        {
            string sInFileName = inputFileTextBox.Text;

            if (sInFileName.Length == 0)
            {
                MessageBox.Show("Missing input file", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (!File.Exists(sInFileName))
            {
                MessageBox.Show("File: " + sInFileName + ", does not exist", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        private bool ValidateInputOutputTextBoxes()
        {
            string sOutFileName = outputFileTextBox.Text;

            if (!ValidateInputTextBox())
                return false;

            if (sOutFileName.Length == 0)
            {
                MessageBox.Show("Missing output file", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        private void smoothButton_Click(object sender, EventArgs e)
        {
            if (!ValidateInputOutputTextBoxes())
                return;

            string sInFileName = inputFileTextBox.Text;
            string sOutFileName = outputFileTextBox.Text;
            string[] sLines = thresholdsTextBox.Lines;

            string sInPath = Path.GetDirectoryName(sInFileName);
            string sInRootFileName = Path.GetFileNameWithoutExtension(sInFileName);
            string sThreshOutPath = Path.Combine(sInPath, "Thresholds");
            string sThreshOutFileName = Path.Combine(sThreshOutPath, sInRootFileName + ".thr");

            if (!Directory.Exists(sThreshOutPath))
                Directory.CreateDirectory(sThreshOutPath);

            Console.WriteLine("Saving thresholds file: " + sThreshOutFileName);
            StreamWriter file = new StreamWriter(sThreshOutFileName);

            // Save the name of the OPT to the thresholds file
            file.WriteLine(sInRootFileName);
            // ... and save the CRC too
            uint crc = SmootherEngine.GetCRC(sInFileName);
            file.WriteLine("0x" + crc.ToString("x"));

            List<string> sThresholds = new List<string>();
            int lineCounter = 0;
            foreach (string sLine in sLines)
            {
                string sThreshold = sLine.Replace(" ", "");
                Console.WriteLine("Applying sThreshold: " + sThreshold);
                lineCounter++;

                if (sThreshold.Length == 0)
                {
                    //MessageBox.Show("No thresholds have been specified", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    //return;
                    Console.WriteLine("No thresholds have been specified in line: " + lineCounter);
                    continue;
                }

                sThresholds.Add(sThreshold);
                file.WriteLine(sLine);
            }
            file.Close();

            Dictionary<int, float> Thresholds = SmootherEngine.ParseIndices(sThresholds, out string sError);
            if (Thresholds == null)
            {
                if (sError == null)
                    MessageBox.Show("Could not parse thresholds at line: " + lineCounter,
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    MessageBox.Show(sError, "Error at line: " + lineCounter, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (Thresholds.Count == 0)
            {
                MessageBox.Show("No thresholds could be parsed", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int NumMeshes = SmootherEngine.Smooth(sInFileName, sOutFileName, Thresholds);
            Console.WriteLine(NumMeshes + " meshes smoothed");
            MessageBox.Show(NumMeshes + " meshes smoothed", "Success", MessageBoxButtons.OK);
        }

        private void BVHButton_Click(object sender, EventArgs e)
        {
            string sInFileName = inputFileTextBox.Text;
            string sError = "";

            if (!ValidateInputTextBox())
                return;

            string sInPath = Path.GetDirectoryName(sInFileName);
            string sOutFileName = Path.GetFileNameWithoutExtension(sInFileName);
            sOutFileName = Path.Combine(sInPath, sOutFileName + ".bvh");

            if (LBVH.g_BuildMultiBLAS)
                LBVH.ComputeMultiBLAS(sInFileName, sOutFileName, out sError);
            else
                LBVH.ComputeBVH(sInFileName, sOutFileName, out sError);

            if (sError.Length > 0)
            {
                MessageBox.Show(sError, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show("Saved " + sOutFileName, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void applyThrButton_Click(object sender, EventArgs e)
        {
            string sOPTPath = Path.GetDirectoryName(inputFileTextBox.Text);
            string sThreshPath = Path.Combine(sOPTPath, "Thresholds");
            Console.WriteLine("Enumerating files in directory:\n" + sThreshPath);
            Console.WriteLine("OPT path: " + sOPTPath);

            string[] sThreshFiles = Directory.GetFiles(sThreshPath);
            foreach (string sThreshFile in sThreshFiles)
            {
                SmootherEngine.ApplyThresholdProfile(sThreshFile, sOPTPath, false);
            }

            /*
            if (SmootherEngine.ApplyThresholdProfile(inputFileTextBox.Text, false))
            {
                Console.WriteLine("Thresholds applied");
                MessageBox.Show("Applied thresholds", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Console.WriteLine("Could not apply thresholds profile");
                MessageBox.Show("Thresholds could not be applied", "Failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            */
        }
    }
}
