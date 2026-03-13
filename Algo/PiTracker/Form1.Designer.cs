namespace PiTracker
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            imageSourceC = new TextBox();
            browseC = new Button();
            seqSourceC = new RadioButton();
            camSourceC = new RadioButton();
            panel1 = new Panel();
            startC = new Button();
            SuspendLayout();
            // 
            // textBox1
            // 
            imageSourceC.Location = new Point(17, 23);
            imageSourceC.Name = "textBox1";
            imageSourceC.Size = new Size(601, 31);
            imageSourceC.TabIndex = 0;
            // 
            // browseC
            // 
            browseC.Location = new Point(624, 20);
            browseC.Name = "browseC";
            browseC.Size = new Size(112, 34);
            browseC.TabIndex = 1;
            browseC.Text = "Browse";
            browseC.UseVisualStyleBackColor = true;
            // 
            // seqSourceC
            // 
            seqSourceC.AutoSize = true;
            seqSourceC.Checked = true;
            seqSourceC.Location = new Point(17, 98);
            seqSourceC.Name = "seqSourceC";
            seqSourceC.Size = new Size(113, 29);
            seqSourceC.TabIndex = 2;
            seqSourceC.TabStop = true;
            seqSourceC.Text = "Sequence";
            seqSourceC.UseVisualStyleBackColor = true;
            // 
            // camSourceC
            // 
            camSourceC.AutoSize = true;
            camSourceC.Location = new Point(145, 98);
            camSourceC.Name = "camSourceC";
            camSourceC.Size = new Size(97, 29);
            camSourceC.TabIndex = 2;
            camSourceC.Text = "Camera";
            camSourceC.UseVisualStyleBackColor = true;
            // 
            // panel1
            // 
            panel1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panel1.Location = new Point(9, 138);
            panel1.Name = "panel1";
            panel1.Size = new Size(727, 443);
            panel1.TabIndex = 3;
            // 
            // startC
            // 
            startC.Location = new Point(624, 60);
            startC.Name = "startC";
            startC.Size = new Size(112, 67);
            startC.TabIndex = 1;
            startC.Text = "Start";
            startC.UseVisualStyleBackColor = true;
            startC.Click += startC_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(755, 593);
            Controls.Add(panel1);
            Controls.Add(camSourceC);
            Controls.Add(seqSourceC);
            Controls.Add(startC);
            Controls.Add(browseC);
            Controls.Add(imageSourceC);
            Name = "Form1";
            Text = "Form1";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox imageSourceC;
        private Button browseC;
        private RadioButton seqSourceC;
        private RadioButton camSourceC;
        private Panel panel1;
        private Button startC;
    }
}
