namespace XwaSmoother
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.inputFileTextBox = new System.Windows.Forms.TextBox();
            this.inputFileButton = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.outputFileButton = new System.Windows.Forms.Button();
            this.outputFileTextBox = new System.Windows.Forms.TextBox();
            this.openFileDialog = new System.Windows.Forms.OpenFileDialog();
            this.saveFileDialog = new System.Windows.Forms.SaveFileDialog();
            this.thresholdsTextBox = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.smoothButton = new System.Windows.Forms.Button();
            this.tangentMapButton = new System.Windows.Forms.Button();
            this.overwriteCheckBox = new System.Windows.Forms.CheckBox();
            this.label4 = new System.Windows.Forms.Label();
            this.xwaDirTextBox = new System.Windows.Forms.TextBox();
            this.xwaDirBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.selectXWADirButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // inputFileTextBox
            // 
            this.inputFileTextBox.Location = new System.Drawing.Point(12, 25);
            this.inputFileTextBox.Name = "inputFileTextBox";
            this.inputFileTextBox.Size = new System.Drawing.Size(885, 20);
            this.inputFileTextBox.TabIndex = 0;
            // 
            // inputFileButton
            // 
            this.inputFileButton.Location = new System.Drawing.Point(903, 22);
            this.inputFileButton.Name = "inputFileButton";
            this.inputFileButton.Size = new System.Drawing.Size(98, 23);
            this.inputFileButton.TabIndex = 1;
            this.inputFileButton.Text = "&Input...";
            this.inputFileButton.UseVisualStyleBackColor = true;
            this.inputFileButton.Click += new System.EventHandler(this.inputFileButton_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(53, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "Input File:";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(12, 54);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(61, 13);
            this.label2.TabIndex = 5;
            this.label2.Text = "Output File:";
            // 
            // outputFileButton
            // 
            this.outputFileButton.Location = new System.Drawing.Point(903, 67);
            this.outputFileButton.Name = "outputFileButton";
            this.outputFileButton.Size = new System.Drawing.Size(98, 23);
            this.outputFileButton.TabIndex = 4;
            this.outputFileButton.Text = "&Output...";
            this.outputFileButton.UseVisualStyleBackColor = true;
            this.outputFileButton.Click += new System.EventHandler(this.outputFileButton_Click);
            // 
            // outputFileTextBox
            // 
            this.outputFileTextBox.Location = new System.Drawing.Point(12, 70);
            this.outputFileTextBox.Name = "outputFileTextBox";
            this.outputFileTextBox.Size = new System.Drawing.Size(885, 20);
            this.outputFileTextBox.TabIndex = 3;
            // 
            // openFileDialog
            // 
            this.openFileDialog.DefaultExt = "OPT";
            this.openFileDialog.Filter = "OPT files|*.opt|All files|*.*";
            // 
            // saveFileDialog
            // 
            this.saveFileDialog.DefaultExt = "OPT";
            this.saveFileDialog.Filter = "OPT files|*.opt|All files|*.*";
            // 
            // thresholdsTextBox
            // 
            this.thresholdsTextBox.Location = new System.Drawing.Point(12, 123);
            this.thresholdsTextBox.Name = "thresholdsTextBox";
            this.thresholdsTextBox.Size = new System.Drawing.Size(885, 20);
            this.thresholdsTextBox.TabIndex = 6;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(12, 102);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(762, 13);
            this.label3.TabIndex = 7;
            this.label3.Text = "Thresholds. Specify mesh indices or ranges followed by a colon and an angle in de" +
    "grees (e.g. 0,1,5..8:40). Use -1 to apply the threshold to all meshes (e.g. -1:2" +
    "5)";
            // 
            // smoothButton
            // 
            this.smoothButton.Location = new System.Drawing.Point(903, 121);
            this.smoothButton.Name = "smoothButton";
            this.smoothButton.Size = new System.Drawing.Size(98, 23);
            this.smoothButton.TabIndex = 7;
            this.smoothButton.Text = "&Smooth Normals";
            this.smoothButton.UseVisualStyleBackColor = true;
            this.smoothButton.Click += new System.EventHandler(this.smoothButton_Click);
            // 
            // tangentMapButton
            // 
            this.tangentMapButton.Location = new System.Drawing.Point(903, 201);
            this.tangentMapButton.Name = "tangentMapButton";
            this.tangentMapButton.Size = new System.Drawing.Size(98, 23);
            this.tangentMapButton.TabIndex = 10;
            this.tangentMapButton.Text = "Save &Tangents";
            this.tangentMapButton.UseVisualStyleBackColor = true;
            this.tangentMapButton.Click += new System.EventHandler(this.tangentMapButton_Click);
            // 
            // overwriteCheckBox
            // 
            this.overwriteCheckBox.AutoSize = true;
            this.overwriteCheckBox.Location = new System.Drawing.Point(903, 98);
            this.overwriteCheckBox.Name = "overwriteCheckBox";
            this.overwriteCheckBox.Size = new System.Drawing.Size(71, 17);
            this.overwriteCheckBox.TabIndex = 5;
            this.overwriteCheckBox.Text = "&Overwrite";
            this.overwriteCheckBox.UseVisualStyleBackColor = true;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(12, 151);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(106, 13);
            this.label4.TabIndex = 12;
            this.label4.Text = "XWA Root Directory:";
            // 
            // xwaDirTextBox
            // 
            this.xwaDirTextBox.Location = new System.Drawing.Point(12, 167);
            this.xwaDirTextBox.Name = "xwaDirTextBox";
            this.xwaDirTextBox.Size = new System.Drawing.Size(885, 20);
            this.xwaDirTextBox.TabIndex = 8;
            // 
            // xwaDirBrowserDialog
            // 
            this.xwaDirBrowserDialog.RootFolder = System.Environment.SpecialFolder.ProgramFilesX86;
            // 
            // selectXWADirButton
            // 
            this.selectXWADirButton.Location = new System.Drawing.Point(903, 165);
            this.selectXWADirButton.Name = "selectXWADirButton";
            this.selectXWADirButton.Size = new System.Drawing.Size(98, 23);
            this.selectXWADirButton.TabIndex = 9;
            this.selectXWADirButton.Text = "Select &Dir";
            this.selectXWADirButton.UseVisualStyleBackColor = true;
            this.selectXWADirButton.Click += new System.EventHandler(this.selectXWADirButton_Click);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1015, 236);
            this.Controls.Add(this.selectXWADirButton);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.xwaDirTextBox);
            this.Controls.Add(this.overwriteCheckBox);
            this.Controls.Add(this.tangentMapButton);
            this.Controls.Add(this.smoothButton);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.thresholdsTextBox);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.outputFileButton);
            this.Controls.Add(this.outputFileTextBox);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.inputFileButton);
            this.Controls.Add(this.inputFileTextBox);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(1);
            this.Name = "MainForm";
            this.Text = "OPT Smoother 1.0";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox inputFileTextBox;
        private System.Windows.Forms.Button inputFileButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button outputFileButton;
        private System.Windows.Forms.TextBox outputFileTextBox;
        private System.Windows.Forms.OpenFileDialog openFileDialog;
        private System.Windows.Forms.SaveFileDialog saveFileDialog;
        private System.Windows.Forms.TextBox thresholdsTextBox;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Button smoothButton;
        private System.Windows.Forms.Button tangentMapButton;
        private System.Windows.Forms.CheckBox overwriteCheckBox;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox xwaDirTextBox;
        private System.Windows.Forms.FolderBrowserDialog xwaDirBrowserDialog;
        private System.Windows.Forms.Button selectXWADirButton;
    }
}