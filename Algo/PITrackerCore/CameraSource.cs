using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PITrackerCore
{
    public interface ICameraSource : IDisposable
    {
        void Start();
        void Stop();
        Task<Mat> GetNextFrame();
        bool IsRunning { get; }
    }

    public abstract class CameraSource : ICameraSource
    {
        protected VideoCapture capture;
        protected int deviceId;

        public bool IsRunning { get; protected set; }

        public CameraSource(int deviceId = 0)
        {
            this.deviceId = deviceId;
        }

        public abstract void Start();

        public virtual void Stop()
        {
            IsRunning = false;
            capture?.Release();
        }
        
        public virtual async Task<Mat> GetNextFrame()
        {
            // Return null if stream is dead or not opened
            if (!IsRunning || capture == null || !capture.IsOpened()) return null;

            Mat frame = new Mat();
            if (capture.Read(frame) && !frame.Empty())
            {
                return frame; // Caller is responsible for disposing this Mat!
            }

            // If we get an empty frame, clean up the empty Mat and return null
            frame.Dispose();
            return null;
        }

        public virtual void Dispose()
        {
            Stop();
            capture?.Dispose();
        }
    }
    public class ImageSequenceSource : ICameraSource
    {
        public bool IsRunning { get; } = true;
        public Action<int, int, int> OnIndexChanged { get; set; }
        public bool AutoIncrement { get; set; } = true;

        public class FileFrame
        {
            public FileFrame(string filename)
            {
                Filename = filename;
            }
            public string Filename { get; private set; }
            private Mat _frame;
            public Mat Frame
            {
                get
                {
                    if (_frame == null)
                        _frame = Cv2.ImRead(Filename);
                    if (_frame.IsDisposed)
                        _frame = Cv2.ImRead(Filename);
                    return _frame;
                }
                private set => _frame = value;
            }

            public void ClearCache()
            {
                if (Frame == null)
                    return;
                Frame.Dispose();
                Frame = null;
            }
        }
        public FileFrame[] Files;
        private int currentIndex = 0;
        public int CurrentIndex => currentIndex;
        public void SetIndex(int index)
        {
            currentIndex = index;
            if(currentIndex >= Files.Length)
                currentIndex = Files.Length - 1;
            else if (currentIndex < 0)
                currentIndex = 0;
        }

        public ImageSequenceSource(string dirOrFile)
        {
            if (File.Exists(dirOrFile))
            {
                Files = new FileFrame[] {  new FileFrame(dirOrFile) };
            }
            else if (Directory.Exists(dirOrFile))
            {
                Files = Directory.GetFiles(dirOrFile, "*.jpg").Select((f) => new FileFrame(f)).ToArray();
            }
            else
            {
                Files = new FileFrame[0];
            }
        }

        public void Start()
        {
            currentIndex = 0;
        }

        public void Stop()
        {
        }

        DateTime lastFrame = DateTime.MinValue;

        public virtual async Task<Mat> GetNextFrame()
        {
            while ((DateTime.Now - lastFrame).TotalMilliseconds < 30) // Limit to ~30 FPS
                await Task.Delay(5);
            lastFrame = DateTime.Now;
            var f = Files[currentIndex].Frame;
            if (AutoIncrement)
            {
                // dispose the last cache
                currentIndex++;
                if (currentIndex >= Files.Length)
                    currentIndex = 0; // Loop back to the beginning
                OnIndexChanged?.Invoke(currentIndex, 0, Files.Length - 1);
            }
            return f;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public class LiveCameraSource : CameraSource
    {
        public LiveCameraSource(int deviceId = 0) : base(deviceId) { }

        public override void Start()
        {
            VideoCaptureAPIs backend = VideoCaptureAPIs.ANY; // Fallback

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                backend = VideoCaptureAPIs.DSHOW; // Windows optimal
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                backend = VideoCaptureAPIs.V4L2; // Linux/Pi optimal
            }

            capture = new VideoCapture(deviceId, backend);
            IsRunning = capture.IsOpened();
        }
    }
}