using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using static PITrackerCore.Tracker;

namespace PiTracker
{
    public partial class MatMicroDebug : UserControl
    {
        public MatMicroDebug()
        {
            InitializeComponent();
        }
        public void Begin(DebugFrame frame)
        {
            Bitmap newBitmap = frame.Frame != null ? BitmapConverter.ToBitmap(frame.Frame) : null;
            this.Invoke(() =>
            {
                var old = pictureBox1.Image;
                pictureBox1.Image = newBitmap;
                label1.Text = frame.Label;
                old?.Dispose();
            });
        }
    }
}
