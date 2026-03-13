using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace PITrackerCore
{
    public class Tracker
    {
        public ICameraSource Camera { get; private set; }
        public Tracker(ICameraSource camera) { Camera = camera; }
        public delegate void DebugFrameCallback(DebugFrame frame);
        public delegate void TrackOutputCallback(TrackData output);
        public event DebugFrameCallback OnDebugFrame;
        public event TrackOutputCallback OnTrackOutput;
        bool isRunning = false;
        public void BeginAsync()
        {
            if (isRunning)
                return;
            isRunning = true;
            new System.Threading.Thread(async () =>
            {
                while (isRunning)
                {
                    var frame = await Camera.GetNextFrame();
                    if (frame != null)
                    {
                        // Process the frame and generate debug frames and track output
                        // For demonstration, we'll just create dummy data
                        OnDebugFrame?.Invoke(new DebugFrame { Frame = frame, Label = "got a frame" });
                    }
                }
            }).Start();
        }
        public void RequestStop()
        {
            isRunning = true;
        }

        public class DebugFrame
        {
            public Mat Frame { get; set; }
            public string Label { get; set; }
        }
        public class TrackData
        {
            public Point2f Position { get; set; }
            public float Confidence { get; set; }
        }
    }
}
