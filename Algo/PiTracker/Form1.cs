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

        void formLog(string text)
        {
            consoleC.AppendText(text);
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
            piTracker.OnMicroDebug += PiTracker_OnMicroDebug;
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

                // Update Info and Extract Object Image
                if (piTracker != null && piTracker.currentTarget != null && piTracker.currentTarget.IsLocked)
                {
                    var t = piTracker.currentTarget;
                    trackerInfoC.Text = $"Position: {t.X:F1}, {t.Y:F1}\nSize: {t.W:F1} x {t.H:F1}\nVelocity: {t.dX:F2}, {t.dY:F2}\nConfidence: {t.Confidence:F3}\nThreshold: {t.BinaryThreshold}";

                    // Safely extract the tracked area
                    int rx = (int)Math.Max(0, t.X);
                    int ry = (int)Math.Max(0, t.Y);
                    int rw = (int)Math.Min(frame.Frame.Width - rx, t.W);
                    int rh = (int)Math.Min(frame.Frame.Height - ry, t.H);

                    if (rw > 0 && rh > 0)
                    {
                        using OpenCvSharp.Mat objMat = frame.Frame[new OpenCvSharp.Rect(rx, ry, rw, rh)];
                        InstantDebugObject(objMat, "TrackedObject");
                    }
                }
                else
                {
                    trackerInfoC.Text = "Not Tracking";
                }

                // Dispose the old image to prevent massive GDI+ memory leaks
                oldImage?.Dispose();
                frame.Frame.Dispose();
                frameC.Invalidate();
            }));
        }

        private void InstantDebugObject(OpenCvSharp.Mat mat, string label)
        {
            var ctrl = new MatMicroDebug();
            debugs.Invoke(() =>
            {
                debugs.Controls.Add(ctrl);
                debugs.ScrollControlIntoView(ctrl);
                if (debugs.Controls.Count > 50)
                {
                    var old = debugs.Controls[0];
                    debugs.Controls.RemoveAt(0);
                    old.Dispose();
                }
            });
            ctrl.Begin(new Tracker.DebugFrame { Frame = mat, Label = $"{DateTime.Now:HH:mm:ss.fff} - {label}" });
        }

        private void PiTracker_OnMicroDebug(Tracker.DebugFrame frame)
        {
            //InstantDebugObject(frame.Frame, frame.Label);
            //frame.Frame.Dispose();
        }
        private void PiTracker_OnTrackOutput(Tracker.TrackData output)
        {
        }

        LockParameters lockParams = new LockParameters() { W = 100, H = 100 };
        private void frameC_MouseMove(object sender, MouseEventArgs e)
        {
            // get the coordinates on the displayed Mat.
            if (frameC.BackgroundImage == null) return;
            // since the image is set to Zoom, the coordinates of cursor won't match the coordinates of the Mat, we need to convert them.
            var gToM_W = (float)frameC.BackgroundImage.Width / frameC.Width;
            var gToM_H = (float)frameC.BackgroundImage.Height / frameC.Height;
            if (gToM_H < gToM_W) // would be touching the top
            {
                // the image is limited by width, so we need to calculate the vertical offset.
                var offset = (frameC.Height - frameC.BackgroundImage.Height / gToM_W) / 2;
                var x = (int)(e.X * gToM_W);
                var y = (int)((e.Y - offset) * gToM_W);
                lockParams.X = x;
                lockParams.Y = y;
                hoverCoordsC.Text = $"{x} x {y}";
            }
            else
            {
                // the image is limited by height, so we need to calculate the horizontal offset.
                var offset = (frameC.Width - frameC.BackgroundImage.Width / gToM_H) / 2;
                var x = (int)((e.X - offset) * gToM_H);
                var y = (int)(e.Y * gToM_H);
                lockParams.X = x;
                lockParams.Y = y;
                hoverCoordsC.Text = $"{x} x {y}";
            }
        }

        private void frameC_Click(object sender, EventArgs e)
        {
            if (e is MouseEventArgs me)
            {
                if (me.Button == MouseButtons.Right)
                {
                    piTracker?.ClearTarget();
                }
                else if (me.Button == MouseButtons.Left && piTracker != null)
                {
                    // Center the lock around the cursor
                    var newTarget = new LockParameters
                    {
                        X = lockParams.X - lockParams.W / 2,
                        Y = lockParams.Y - lockParams.H / 2,
                        W = lockParams.W,
                        H = lockParams.H,
                        IsLocked = true,
                        Confidence = 1.0,
                        IsManual = true
                    };
                    piTracker.SetTarget(newTarget);
                }
            }
        }
        private void stepTrackingC_Click(object sender, EventArgs e)
        {
            if (piTracker != null)
            {
                piTracker.StepNextFrameTracker = true;
            }
        }

        private void playTrackingC_CheckedChanged(object sender, EventArgs e)
        {
            if (piTracker != null)
                piTracker.IsPlayingTracker = playTrackingC.Checked;
        }
    }
}
