using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using PITrackerCore;
using PiTrackerAlgo = PITrackerCore.Tracker;

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

        private volatile bool _seekRequested = false;

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

                    // ── Grab frame ────────────────────────────────────────────
                    var frame = await _camera.GetNextFrame();
                    if (frame == null || frame.Empty())
                    {
                        await Task.Delay(10, stoppingToken);
                        continue;
                    }

                    using (frame)
                    {
                        // ── Apply pending lock request from UI ────────────────
                        var pending = _state.ConsumePendingLock();
                        if (pending != null)
                            _tracker!.SetTarget(pending);

                        // ── Run tracker ──────────────────────────────────────
                        LockParameters? result = null;
                        if (_tracker!.currentTarget != null)
                        {
                            result = _tracker.TryLock(_tracker.currentTarget, _state.Settings, frame);
                            if (result.IsLocked)
                                _tracker.SetTarget(result);
                            else
                                _tracker.ClearTarget();
                        }

                        // ── Draw debug overlay ───────────────────────────────
                        using var debugFrame = DrawOverlay(frame, result);

                        // ── Push to UI (non-blocking) ─────────────────────────
                        _state.PushFrame(debugFrame, result);
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
                var live = new LiveCameraSource(0);
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

        // ─── Debug Overlay ────────────────────────────────────────────────────

        private static Mat DrawOverlay(Mat frame, LockParameters? lp)
        {
            var dbg = frame.Clone();
            if (lp == null || !lp.IsLocked) return dbg;

            // ROI box (green)
            if (lp.LastRoi.Width > 0)
                Cv2.Rectangle(dbg, lp.LastRoi, Scalar.Green, 1);

            // Object box (blue, thick)
            Cv2.Rectangle(dbg, new Rect((int)lp.X, (int)lp.Y, (int)lp.W, (int)lp.H), Scalar.Blue, 2);

            // Velocity arrow
            var cx = (int)(lp.X + lp.W / 2);
            var cy = (int)(lp.Y + lp.H / 2);
            Cv2.Line(dbg, new Point(cx, cy), new Point(cx - (int)lp.dX, cy - (int)lp.dY), Scalar.Yellow, 1);

            // Confidence text
            Cv2.PutText(dbg, $"Conf:{lp.Confidence:F2}",
                new Point((int)lp.X, Math.Max(0, (int)lp.Y - 5)),
                HersheyFonts.HersheySimplex, 0.45, Scalar.LimeGreen, 1);

            return dbg;
        }
    }
}
