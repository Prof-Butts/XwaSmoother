using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

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
            string sInFileName = inputFileTextBox.Text;
            string sOutFileName = outputFileTextBox.Text;
            string sThreshold = thresholdsTextBox.Text.Replace(" ", "");

            if (!ValidateInputOutputTextBoxes())
                return;

            if (sThreshold.Length == 0)
            {
                MessageBox.Show("No thresholds have been specified", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            List<string> sThresholds = new List<string>();
            sThresholds.Add(sThreshold);
            Dictionary<int, float> Thresholds = SmootherEngine.ParseIndices(sThresholds, out string sError);
            if (Thresholds == null)
            {
                if (sError == null)
                    MessageBox.Show("Could not parse thresholds", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    MessageBox.Show(sError, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (Thresholds.Count == 0)
            {
                MessageBox.Show("No thresholds could be parse", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int NumMeshes = SmootherEngine.Smooth(sInFileName, sOutFileName, Thresholds);
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

            LBVH.ComputeBVH(sInFileName, sOutFileName, out sError);
            if (sError.Length > 0)
            {
                MessageBox.Show(sError, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show("Saved " + sOutFileName, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
