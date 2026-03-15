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
            startC = new Button();
            frameC = new PictureBox();
            seekbarC = new TrackBar();
            playedC = new CheckBox();
            consoleC = new TextBox();
            hoverCoordsC = new Label();
            playTrackingC = new CheckBox();
            stepTrackingC = new Button();
            debugs = new FlowLayoutPanel();
            trackerInfoC = new Label();
            ((System.ComponentModel.ISupportInitialize)frameC).BeginInit();
            ((System.ComponentModel.ISupportInitialize)seekbarC).BeginInit();
            SuspendLayout();
            // 
            // imageSourceC
            // 
            imageSourceC.Location = new Point(17, 23);
            imageSourceC.Name = "imageSourceC";
            imageSourceC.Size = new Size(601, 31);
            imageSourceC.TabIndex = 0;
            imageSourceC.Text = "C:\\Users\\Tayyaba\\source\\repos\\PiTracker\\Algo\\20260311_132636_frames";
            // 
            // browseC
            // 
            browseC.Location = new Point(624, 20);
            browseC.Name = "browseC";
            browseC.Size = new Size(111, 33);
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
            camSourceC.Location = new Point(146, 98);
            camSourceC.Name = "camSourceC";
            camSourceC.Size = new Size(97, 29);
            camSourceC.TabIndex = 2;
            camSourceC.Text = "Camera";
            camSourceC.UseVisualStyleBackColor = true;
            // 
            // startC
            // 
            startC.Location = new Point(624, 60);
            startC.Name = "startC";
            startC.Size = new Size(111, 67);
            startC.TabIndex = 1;
            startC.Text = "Start";
            startC.UseVisualStyleBackColor = true;
            startC.Click += startC_Click;
            // 
            // frameC
            // 
            frameC.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            frameC.BackgroundImageLayout = ImageLayout.Zoom;
            frameC.Location = new Point(17, 138);
            frameC.Margin = new Padding(4, 5, 4, 5);
            frameC.Name = "frameC";
            frameC.Size = new Size(1070, 796);
            frameC.TabIndex = 3;
            frameC.TabStop = false;
            frameC.Click += frameC_Click;
            frameC.MouseMove += frameC_MouseMove;
            // 
            // seekbarC
            // 
            seekbarC.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            seekbarC.Location = new Point(17, 944);
            seekbarC.Margin = new Padding(4, 5, 4, 5);
            seekbarC.Name = "seekbarC";
            seekbarC.Size = new Size(1049, 69);
            seekbarC.TabIndex = 4;
            // 
            // playedC
            // 
            playedC.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            playedC.AutoSize = true;
            playedC.Checked = true;
            playedC.CheckState = CheckState.Checked;
            playedC.Location = new Point(1065, 946);
            playedC.Margin = new Padding(4, 5, 4, 5);
            playedC.Name = "playedC";
            playedC.Size = new Size(22, 21);
            playedC.TabIndex = 5;
            playedC.UseVisualStyleBackColor = true;
            // 
            // consoleC
            // 
            consoleC.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            consoleC.Location = new Point(1091, 624);
            consoleC.Margin = new Padding(4, 5, 4, 5);
            consoleC.Multiline = true;
            consoleC.Name = "consoleC";
            consoleC.Size = new Size(243, 357);
            consoleC.TabIndex = 6;
            // 
            // hoverCoordsC
            // 
            hoverCoordsC.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hoverCoordsC.Location = new Point(935, 98);
            hoverCoordsC.Margin = new Padding(4, 0, 4, 0);
            hoverCoordsC.Name = "hoverCoordsC";
            hoverCoordsC.Size = new Size(153, 35);
            hoverCoordsC.TabIndex = 7;
            hoverCoordsC.Text = "--";
            hoverCoordsC.TextAlign = ContentAlignment.BottomRight;
            // 
            // playTrackingC
            // 
            playTrackingC.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            playTrackingC.AutoSize = true;
            playTrackingC.Location = new Point(926, 23);
            playTrackingC.Name = "playTrackingC";
            playTrackingC.Size = new Size(139, 29);
            playTrackingC.TabIndex = 8;
            playTrackingC.Text = "Play Tracking";
            playTrackingC.UseVisualStyleBackColor = true;
            playTrackingC.CheckedChanged += playTrackingC_CheckedChanged;
            // 
            // stepTrackingC
            // 
            stepTrackingC.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            stepTrackingC.Location = new Point(1065, 20);
            stepTrackingC.Name = "stepTrackingC";
            stepTrackingC.Size = new Size(111, 33);
            stepTrackingC.TabIndex = 9;
            stepTrackingC.Text = "Step";
            stepTrackingC.UseVisualStyleBackColor = true;
            stepTrackingC.Click += stepTrackingC_Click;
            // 
            // debugs
            // 
            debugs.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            debugs.AutoScroll = true;
            debugs.FlowDirection = FlowDirection.TopDown;
            debugs.Location = new Point(1091, 138);
            debugs.Name = "debugs";
            debugs.Size = new Size(248, 253);
            debugs.TabIndex = 10;
            // 
            // trackerInfoC
            // 
            trackerInfoC.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            trackerInfoC.Location = new Point(1091, 394);
            trackerInfoC.Name = "trackerInfoC";
            trackerInfoC.Size = new Size(248, 225);
            trackerInfoC.TabIndex = 11;
            trackerInfoC.Text = "label1";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1351, 1004);
            Controls.Add(trackerInfoC);
            Controls.Add(debugs);
            Controls.Add(stepTrackingC);
            Controls.Add(playTrackingC);
            Controls.Add(hoverCoordsC);
            Controls.Add(consoleC);
            Controls.Add(playedC);
            Controls.Add(seekbarC);
            Controls.Add(frameC);
            Controls.Add(camSourceC);
            Controls.Add(seqSourceC);
            Controls.Add(startC);
            Controls.Add(browseC);
            Controls.Add(imageSourceC);
            Name = "Form1";
            Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)frameC).EndInit();
            ((System.ComponentModel.ISupportInitialize)seekbarC).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox imageSourceC;
        private Button browseC;
        private RadioButton seqSourceC;
        private RadioButton camSourceC;
        private Button startC;
        private PictureBox frameC;
        private TrackBar seekbarC;
        private CheckBox playedC;
        private TextBox consoleC;
        private Label hoverCoordsC;
        private CheckBox playTrackingC;
        private Button stepTrackingC;
        private FlowLayoutPanel debugs;
        private Label trackerInfoC;
    }
}
