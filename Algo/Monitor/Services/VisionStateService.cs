using OpenCvSharp;
using PITrackerCore;

namespace Monitor.Services
{
    public enum OperationMode { Demo, Live }

    /// <summary>
    /// Singleton bridge between the CV worker loop and the Blazor UI.
    /// All frame encoding happens on the caller (worker) thread — never blocks SignalR.
    /// </summary>
    public class VisionStateService
    {
        // ─── Mode & Configuration ─────────────────────────────────────────────
        public OperationMode Mode { get; set; } = OperationMode.Demo;
        public TrackerSettings Settings { get; } = new TrackerSettings();
        public StreamSettings StreamConfig { get; } = new StreamSettings();

        // ─── Shared State ─────────────────────────────────────────────────────
        public LockParameters? CurrentLock { get; private set; }
        public string LatestFrameBase64 { get; private set; } = string.Empty;
        public int NativeWidth { get; private set; } = 640;
        public int NativeHeight { get; private set; } = 480;
        public bool IsCameraAvailable { get; private set; } = true;
        public string CameraError { get; private set; } = string.Empty;

        // ─── Events ───────────────────────────────────────────────────────────
        /// <summary>Fired on the worker thread after every frame. Subscribers must dispatch to UI thread.</summary>
        public event Action? OnFrameUpdate;

        // ─── Internal pending-lock handshake ──────────────────────────────────
        private LockParameters? _pendingLock;
        private readonly object _lockLock = new();

        // ─── Frame Stats ──────────────────────────────────────────────────────
        public double FpsActual { get; private set; }
        private DateTime _lastFrameTime = DateTime.MinValue;

        // ─── Camera error reporting ───────────────────────────────────────────
        public void ReportCameraError(string message)
        {
            IsCameraAvailable = false;
            CameraError = message;
            OnFrameUpdate?.Invoke();
        }

        public void ReportCameraOk()
        {
            IsCameraAvailable = true;
            CameraError = string.Empty;
        }

        // ─── Called by TrackerWorker on every processed frame ─────────────────
        public void PushFrame(Mat frame, LockParameters? lockResult)
        {
            // Measure actual FPS
            var now = DateTime.UtcNow;
            if (_lastFrameTime != DateTime.MinValue)
                FpsActual = 1.0 / (now - _lastFrameTime).TotalSeconds;
            _lastFrameTime = now;

            // Update state
            CurrentLock = lockResult;
            NativeWidth = frame.Width;
            NativeHeight = frame.Height;

            // Encode frame to JPEG Base64 at configured quality/resolution
            try
            {
                using var resized = ResizeForStream(frame);
                Cv2.ImEncode(".jpg", resized, out var jpegBytes,
                    new ImageEncodingParam(ImwriteFlags.JpegQuality, StreamConfig.JpegQuality));
                LatestFrameBase64 = "data:image/jpeg;base64," + Convert.ToBase64String(jpegBytes);
            }
            catch
            {
                // If encoding fails, leave previous frame in place
            }

            // Fire event — non-blocking. Subscribers must call InvokeAsync.
            OnFrameUpdate?.Invoke();
        }

        private Mat ResizeForStream(Mat src)
        {
            if (StreamConfig.StreamWidth <= 0 || StreamConfig.StreamHeight <= 0)
                return src.Clone();

            var dst = new Mat();
            Cv2.Resize(src, dst, new OpenCvSharp.Size(StreamConfig.StreamWidth, StreamConfig.StreamHeight));
            return dst;
        }

        // ─── Lock Management ──────────────────────────────────────────────────

        /// <summary>
        /// Called by the UI with click ratios (0.0–1.0). Converts to camera pixel
        /// coordinates and queues a lock request for the worker to pick up.
        /// </summary>
        public void ProcessLockRequest(double ratioX, double ratioY)
        {
            var pixelX = ratioX * NativeWidth;
            var pixelY = ratioY * NativeHeight;

            var pending = new LockParameters
            {
                X = pixelX - Settings.MinROI,
                Y = pixelY - Settings.MinROI,
                W = Settings.MinROI * 2,
                H = Settings.MinROI * 2,
                RoiOffsetX = Settings.MinROI,
                RoiOffsetY = Settings.MinROI,
                Confidence = 1.0,
                IsLocked = true,
                IsManual = true,
                LockTime = DateTime.Now
            };

            lock (_lockLock)
                _pendingLock = pending;
        }

        public void ClearLock()
        {
            CurrentLock = null;
            lock (_lockLock)
                _pendingLock = null;
        }

        /// <summary>Atomically retrieves and clears the pending lock (called by worker).</summary>
        public LockParameters? ConsumePendingLock()
        {
            lock (_lockLock)
            {
                var p = _pendingLock;
                _pendingLock = null;
                return p;
            }
        }
    }

    /// <summary>
    /// Settings for the outgoing video stream (resolution + quality).
    /// Mutable so the Settings page can bind directly.
    /// </summary>
    public class StreamSettings
    {
        /// <summary>Output width in pixels. 0 = native camera resolution.</summary>
        public int StreamWidth { get; set; } = 640;
        /// <summary>Output height in pixels. 0 = native camera resolution.</summary>
        public int StreamHeight { get; set; } = 480;
        /// <summary>JPEG quality 0–100.</summary>
        public int JpegQuality { get; set; } = 75;
    }
}
