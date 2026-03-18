using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using PITrackerCore;
using PiTrackerAlgo = PITrackerCore.Tracker;
using System.Diagnostics;

namespace Monitor.Services
{
    /// <summary>
    /// Background worker that owns the CV loop.
    /// Runs at full camera speed; never blocks on the UI/SignalR thread.
    /// </summary>
    public class TrackerWorker : BackgroundService
    {
        private readonly VisionStateService _state;
        private readonly ILogger<TrackerWorker> _logger;

        private ICameraSource? _camera;
        private PiTrackerAlgo? _tracker;
        private OperationMode _currentMode;
        private bool _isPlaying = true;

        // ─── Video Capture Buffering ──────────────────────────────
        public bool IsCapturing { get; set; } = false;
        private List<Mat> _capturedFrames = new List<Mat>();
        private string _capturePath = "/home/ai-interceptor/captured_frames";

        // ─── VCR Controls (exposed to Testing page) ──────────────────────────
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                if (_camera is ImageSequenceSource iss)
                    iss.AutoIncrement = value;
            }
        }

        public int CurrentFrameIndex
        {
            get => _camera is ImageSequenceSource iss ? iss.CurrentIndex : 0;
        }

        public int TotalFrameCount
        {
            get => _camera is ImageSequenceSource iss ? iss.Files.Length : 0;
        }

        public void Seek(int index)
        {
            if (_camera is ImageSequenceSource iss)
            {
                iss.SetIndex(index);
                // Force re-render of the seeked frame
                _seekRequested = true;
            }
        }

        // ─── Video Capture ────────────────────────────────────────────────────
        public void StartCapture()
        {
            IsCapturing = true;
            _capturedFrames.Clear();
            _logger.LogInformation("Started video capture buffering.");
        }

        public void StopCapture()
        {
            IsCapturing = false;
            SaveCapturedFrames();
            _logger.LogInformation("Stopped video capture and saved frames.");
        }

        private void SaveCapturedFrames()
        {
            if (_capturedFrames.Count == 0) return;

            try
            {
                Directory.CreateDirectory(_capturePath);
                for (int i = 0; i < _capturedFrames.Count; i++)
                {
                    string fileName = Path.Combine(_capturePath, $"frame_{i:0000}.bmp");
                    Cv2.ImWrite(fileName, _capturedFrames[i]);
                    _capturedFrames[i].Dispose();
                }
                _capturedFrames.Clear();
                _logger.LogInformation($"Saved {_capturedFrames.Count} frames to {_capturePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save captured frames.");
            }
        }

        private volatile bool _seekRequested = false;
        private DateTime _lastCaptureTime = DateTime.MinValue;

        public TrackerWorker(VisionStateService state, ILogger<TrackerWorker> logger)
        {
            _state = state;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("TrackerWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // ── Recreate source if mode changed ──────────────────────
                    if (_camera == null || _currentMode != _state.Mode)
                        await RecreateSourceAsync(stoppingToken);

                    if (_camera == null)
                    {
                        await Task.Delay(500, stoppingToken);
                        continue;
                    }

                    // ── Paused in demo mode: idle ─────────────────────────────
                    //if (!IsPlaying && !_seekRequested && _currentMode == OperationMode.Demo)
                    //{
                    //    await Task.Delay(30, stoppingToken);
                    //    continue;
                    //}
                    _seekRequested = false;

                    // ── Calculate capture FPS ──────────────────────────────────
                    var now = DateTime.UtcNow;
                    if (_lastCaptureTime != DateTime.MinValue)
                    {
                        var delta = now - _lastCaptureTime;
                        _state.SetCaptureFps(1.0 / delta.TotalSeconds);
                    }
                    _lastCaptureTime = now;

                    // ── Grab frame ────────────────────────────────────────────
                    Stopwatch sw = Stopwatch.StartNew();
                    var frame = await _camera.GetNextFrame();
                    long captureTime = sw.ElapsedTicks;
                    if (frame == null || frame.Empty())
                    {
                        await Task.Delay(10, stoppingToken);
                        continue;
                    }

                    string debugInfo = $"Capture:{captureTime * 1000000 / Stopwatch.Frequency}us ";

                    // Buffer frame if capturing
                    if (IsCapturing)
                    {
                        sw.Restart();
                        _capturedFrames.Add(frame.Clone());
                        long bufferTime = sw.ElapsedTicks;
                        debugInfo += $"Buffer:{bufferTime * 1000000 / Stopwatch.Frequency}us ";
                    }

                    using (frame)
                    {
                        // ── Apply pending lock request from UI ────────────────
                        sw.Restart();
                        var pending = _state.ConsumePendingLock();
                        long pendingTime = sw.ElapsedTicks;
                        debugInfo += $"Pending:{pendingTime * 1000000 / Stopwatch.Frequency}us ";
                        if (pending != null)
                        {
                            sw.Restart();
                            _tracker!.SetTarget(pending);
                            long setTime = sw.ElapsedTicks;
                            debugInfo += $"Set:{setTime * 1000000 / Stopwatch.Frequency}us ";
                        }

                        // ── Run tracker ──────────────────────────────────────
                        LockParameters? result = null;
                        if (_tracker!.currentTarget != null)
                        {
                            sw.Restart();
                            //result = _tracker.TryLock(_tracker.currentTarget, _state.Settings, frame);
                            long trackTime = sw.ElapsedTicks;
                            debugInfo += $"Track:{trackTime * 1000000 / Stopwatch.Frequency}us " + (result?.DebugInfo ?? "");
                        }

                        // ── Push to UI (non-blocking) ─────────────────────────
                        sw.Restart();
                        _state.PushFrame(frame.Clone(), result);
                        long pushTime = sw.ElapsedTicks;
                        string fullDebugInfo = debugInfo + $" Push:{pushTime * 1000000 / Stopwatch.Frequency}us";
                        _state.SetLatestDebugInfo(fullDebugInfo);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TrackerWorker loop error.");
                    await Task.Delay(200, stoppingToken);
                }
            }

            CleanupSource();
            _logger.LogInformation("TrackerWorker stopped.");
        }

        // ─── Source Lifecycle ─────────────────────────────────────────────────

        private Task RecreateSourceAsync(CancellationToken ct)
        {
            CleanupSource();
            _currentMode = _state.Mode;

            if (_currentMode == OperationMode.Demo)
            {
                var path = _state.Settings.DemoFramesPath;
                if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
                {
                    _logger.LogWarning("Demo path not found: {Path}", path);
                    _state.ReportCameraError($"Demo frames path not found: {path}");
                    _camera = null;
                    return Task.CompletedTask;
                }

                var seq = new ImageSequenceSource(path);
                if (seq.Files == null || seq.Files.Length == 0)
                {
                    _logger.LogWarning("No image frames found in: {Path}", path);
                    _state.ReportCameraError($"No .jpg, .jpeg, .png, or .bmp frames found in: {path}");
                    _camera = null;
                    return Task.CompletedTask;
                }

                seq.AutoIncrement = IsPlaying;
                _camera = seq;
                _state.ReportCameraOk();
                _logger.LogInformation("Demo source started: {Path} ({Count} frames)", path, seq.Files.Length);
            }
            else // Live
            {
                var live = new LiveCameraSource(0, _logger);
                live.Start();

                if (!live.IsRunning)
                {
                    _logger.LogWarning("Camera device 0 is not available.");
                    _state.ReportCameraError("Camera device 0 could not be opened. Check that a camera is connected and not in use by another application.");
                    _camera = null;
                    live.Dispose();
                    return Task.CompletedTask;
                }

                _camera = live;
                _state.ReportCameraOk();
                _logger.LogInformation("Live camera started.");
            }

            _tracker = new PiTrackerAlgo(_camera);
            return Task.CompletedTask;
        }

        private void CleanupSource()
        {
            try { _camera?.Stop(); } catch { }
            try { _camera?.Dispose(); } catch { }
            _camera = null;
            _tracker = null;
        }
    }
}
