using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

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
                var extensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
                Files = extensions.SelectMany(ext => Directory.GetFiles(dirOrFile, ext))
                                  .OrderBy(f => f)
                                  .Select(f => new FileFrame(f))
                                  .ToArray();
            }
            else
            {
                Files = Array.Empty<FileFrame>();
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

            if (Files == null || Files.Length == 0)
                return null;

            if (currentIndex >= Files.Length)
                currentIndex = 0;

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
        private readonly ILogger? _logger;
        private readonly string? _pipeline;

        public LiveCameraSource(int deviceId = 0, ILogger? logger = null) : base(deviceId)
        {
            _logger = logger;
        }

        public LiveCameraSource(string pipeline, ILogger? logger = null) : base(-1)
        {
            _pipeline = pipeline;
            _logger = logger;
        }

        public override void Start()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                OpenDevice(deviceId >= 0 ? deviceId : 0, VideoCaptureAPIs.DSHOW);
            }
            else
            {
                // On modern Pi OS, the Pi Camera (ov5647) requires libcamera via GStreamer.
                // Requesting nearest HD resolution (1280x720).
                // appsink is configured to drop old frames and keep only 1 buffer to minimize latency.
                string pipeline = _pipeline ?? "libcamerasrc ! video/x-raw, width=1280, height=720, framerate=30/1 ! videoconvert ! appsink sync=false max-buffers=1 drop=true";
                OpenPipeline(pipeline, VideoCaptureAPIs.GSTREAMER);
            }
        }

        private void OpenPipeline(string pipeline, VideoCaptureAPIs backend)
        {
            _logger?.LogInformation("[CameraDiag] Opening pipeline: {Pipe} with {Backend}", pipeline, backend);
            capture = new VideoCapture(pipeline, backend);

            if (!capture.IsOpened())
            {
                _logger?.LogWarning("[CameraDiag] Failed to open pipeline.");
                IsRunning = false;
                return;
            }

            _logger?.LogInformation(
                "[CameraDiag] Opened Pipeline. Resolution={W}x{H}  FPS={FPS}",
                (int)capture.Get(VideoCaptureProperties.FrameWidth),
                (int)capture.Get(VideoCaptureProperties.FrameHeight),
                capture.Get(VideoCaptureProperties.Fps));

            IsRunning = true;
        }

        private void OpenDevice(int idx, VideoCaptureAPIs backend)
        {
            _logger?.LogInformation("[CameraDiag] Opening /dev/video{Id} with {Backend}", idx, backend);
            capture = new VideoCapture(idx, backend);

            if (!capture.IsOpened())
            {
                _logger?.LogWarning("[CameraDiag] Failed to open /dev/video{Id}.", idx);
                IsRunning = false;
                return;
            }

            _logger?.LogInformation(
                "[CameraDiag] Opened /dev/video{Id}. Resolution={W}x{H}  FPS={FPS}",
                idx,
                (int)capture.Get(VideoCaptureProperties.FrameWidth),
                (int)capture.Get(VideoCaptureProperties.FrameHeight),
                capture.Get(VideoCaptureProperties.Fps));

            // V4L2 warm-up flush
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var warmup = new Mat();
                for (int i = 0; i < 10; i++)
                {
                    capture.Read(warmup);
                    System.Threading.Thread.Sleep(30);
                }
                warmup.Dispose();
            }

            deviceId = idx;
            IsRunning = true;
        }



        private int _emptyFrameCount = 0;

        public override async Task<Mat> GetNextFrame()
        {
            if (!IsRunning || capture == null || !capture.IsOpened()) return null;

            Mat frame = new Mat();
            if (capture.Read(frame) && !frame.Empty())
            {
                _emptyFrameCount = 0;
                return frame;
            }

            frame.Dispose();
            _emptyFrameCount++;

            if (_emptyFrameCount == 1 || _emptyFrameCount % 100 == 0)
                _logger?.LogWarning("[CameraDiag] Empty/failed frame #{Count} from device {Id}. IsOpened={Open}",
                    _emptyFrameCount, deviceId, capture.IsOpened());

            return null;
        }
    }
}