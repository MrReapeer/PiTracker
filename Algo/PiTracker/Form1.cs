using OpenCvSharp.Extensions;
using PITrackerCore;

namespace PiTracker
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        Tracker piTracker;
        ImageSequenceSource imageSequenceSource;
        private void startC_Click(object sender, EventArgs e)
        {
            if (piTracker != null)
                return;
            // Create the interface and trigger the algorithm
            
            if (seqSourceC.Checked)
            {
                if (imageSequenceSource == null)
                {
                    imageSequenceSource = new ImageSequenceSource(imageSourceC.Text);
                    playedC.CheckedChanged += (s, e) =>
                    {
                        imageSequenceSource.AutoIncrement = playedC.Checked;
                    };
                    seekbarC.ValueChanged += (s, e) =>
                    {
                        if (playedC.Checked) return;
                        imageSequenceSource.SetIndex(seekbarC.Value);
                    };
                    imageSequenceSource.OnIndexChanged += (index, min, max) =>
                    {
                        if (!playedC.Checked)
                            return;
                        seekbarC.Invoke(() =>
                        {
                            if (seekbarC.Minimum > min)
                                seekbarC.Minimum = min;
                            if (seekbarC.Maximum < max)
                                seekbarC.Maximum = max;
                            seekbarC.Value = index;
                        });
                    };
                }
                piTracker = new Tracker(imageSequenceSource);
            }
            else
            {
                piTracker = new Tracker(new LiveCameraSource());
            }
            piTracker.OnTrackOutput += PiTracker_OnTrackOutput;
            piTracker.OnDebugFrame += PiTracker_OnDebugFrame;
            piTracker.BeginAsync();
        }

        private void PiTracker_OnDebugFrame(Tracker.DebugFrame frame)
        {// Convert Mat to standard Windows Bitmap
            Bitmap newBitmap = BitmapConverter.ToBitmap(frame.Frame);

            // Marshal to the UI thread
            frameC.Invoke(new Action(() =>
            {
                // CRITICAL: Capture the old image so we can dispose of it
                var oldImage = frameC.BackgroundImage;
                // Assign the new frame
                frameC.BackgroundImage = newBitmap;
                // Dispose the old image to prevent massive GDI+ memory leaks
                oldImage?.Dispose();
                frameC.Invalidate();
            }));
        }

        private void PiTracker_OnTrackOutput(Tracker.TrackData output)
        {
        }
    }
}
