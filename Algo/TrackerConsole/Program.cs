using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using PITrackerCore;

internal class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("[System] Booting Edge Telemetry & Vision Pipeline...");
        Console.WriteLine("[System] Controls: ARROWS (Move Crosshair), SPACE (Lock), ESC (Clear/Center), Q (Quit).");

        using var cts = new CancellationTokenSource();
        using var camera = new LiveCameraSource(0); // Replace with your actual implementation
        camera.Start();

        // 1. Initialize Components
        var renderer = FrameRenderer.Create();
        var tracker = new PITrackerCore.Tracker(camera);
        var settings = new TrackerSettings();
        
        var inputState = new InputState();
        
        // 2. Spin up dedicated threads
        var inputTask = Task.Run(() => InputController.RunGameLoop(inputState, cts), cts.Token);
        var visionTask = Task.Run(() => VisionPipeline.RunLoop(camera, tracker, settings, renderer, inputState, cts.Token), cts.Token);

        // Wait for user to press 'Q' to trigger cancellation
        await inputTask; 

        // 3. Clean Shutdown
        Console.WriteLine("[System] Shutting down cleanly...");
        await visionTask;
        
        camera.Stop();
        renderer.Dispose();
    }
}

// ==========================================
// 1. STATE MANAGEMENT
// ==========================================
public class InputState
{
    private readonly object _lock = new object();
    
    private double _nx = 0.0; // Normalized X [-1.0 to 1.0]
    private double _ny = 0.0; // Normalized Y [-1.0 to 1.0]
    private bool _triggerLock = false;
    private bool _triggerClear = false;

    public double CursorX => _nx;
    public double CursorY => _ny;

    public void MoveCursor(double deltaX, double deltaY)
    {
        lock (_lock)
        {
            _nx = Math.Clamp(_nx + deltaX, -1.0, 1.0);
            _ny = Math.Clamp(_ny + deltaY, -1.0, 1.0);
        }
    }

    public void ResetCursor()
    {
        lock (_lock)
        {
            _nx = 0.0;
            _ny = 0.0;
        }
    }

    public void RequestLock() { lock (_lock) _triggerLock = true; }
    public void RequestClear() { lock (_lock) _triggerClear = true; }

    // Consumes the triggers (returns true if requested, then resets it)
    public bool ConsumeLockRequest()
    {
        lock (_lock)
        {
            if (!_triggerLock) return false;
            _triggerLock = false;
            return true;
        }
    }

    public bool ConsumeClearRequest()
    {
        lock (_lock)
        {
            if (!_triggerClear) return false;
            _triggerClear = false;
            return true;
        }
    }
}

// ==========================================
// 2. INPUT CONTROLLER (Game Loop)
// ==========================================
public static class InputController
{
    public static void RunGameLoop(InputState state, CancellationTokenSource cts)
    {
        const double stepSize = 0.05; // 5% of the screen per keystroke

        while (!cts.Token.IsCancellationRequested)
        {
            // Intercept=true hides the key from being printed to the console
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true).Key;

                switch (key)
                {
                    case ConsoleKey.UpArrow:    state.MoveCursor(0, -stepSize); break;
                    case ConsoleKey.DownArrow:  state.MoveCursor(0, stepSize); break;
                    case ConsoleKey.LeftArrow:  state.MoveCursor(-stepSize, 0); break;
                    case ConsoleKey.RightArrow: state.MoveCursor(stepSize, 0); break;
                    
                    case ConsoleKey.Spacebar:   state.RequestLock(); break;
                    
                    case ConsoleKey.Escape:     
                        state.RequestClear(); 
                        state.ResetCursor();
                        break;
                        
                    case ConsoleKey.Q:          
                        cts.Cancel(); 
                        break;
                }
            }
            else
            {
                Thread.Sleep(10); // Prevent 100% CPU core usage on the input thread
            }
        }
    }
}

// ==========================================
// 3. GRAPHICS & OVERLAY (Drone HUD)
// ==========================================
public static class DroneHUD
{
    public static void DrawCrosshair(Mat frame, double nx, double ny)
    {
        int cx = (int)(((nx + 1.0) / 2.0) * frame.Width);
        int cy = (int)(((ny + 1.0) / 2.0) * frame.Height);
        int size = 20;

        // Draw a distinct targeting reticle (White with black shadow for analog visibility)
        Cv2.Line(frame, new Point(cx - size, cy), new Point(cx + size, cy), Scalar.Black, 4);
        Cv2.Line(frame, new Point(cx, cy - size), new Point(cx, cy + size), Scalar.Black, 4);
        
        Cv2.Line(frame, new Point(cx - size, cy), new Point(cx + size, cy), Scalar.White, 2);
        Cv2.Line(frame, new Point(cx, cy - size), new Point(cx, cy + size), Scalar.White, 2);
        
        Cv2.Circle(frame, new Point(cx, cy), 4, Scalar.Red, -1); // Solid red center dot
    }

    public static void DrawTelemetry(Mat frame, LockParameters state, string statusMsg)
    {
        Cv2.PutText(frame, statusMsg, new Point(10, 25), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 3);
        Cv2.PutText(frame, statusMsg, new Point(10, 25), HersheyFonts.HersheySimplex, 0.7, Scalar.Yellow, 1);

        if (state != null && state.IsLocked)
        {
            // Draw Tracking Box
            var box = new Rect((int)state.X, (int)state.Y, (int)state.W, (int)state.H);
            Cv2.Rectangle(frame, box, Scalar.FromRgb(0, 255, 0), 2);
            
            // Draw Velocity Vector
            var center = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
            var dxEnd = new Point((int)(center.X + state.dX), (int)(center.Y + state.dY));
            Cv2.ArrowedLine(frame, center, dxEnd, Scalar.Red, 2, LineTypes.AntiAlias, 0, 0.3);

            // Draw Confidence
            string lockData = $"LOCKED [Conf: {state.Confidence:F2}]";
            Cv2.PutText(frame, lockData, new Point(10, 50), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 3);
            Cv2.PutText(frame, lockData, new Point(10, 50), HersheyFonts.HersheySimplex, 0.6, Scalar.Green, 1);
        }
        else
        {
            Cv2.PutText(frame, "SEARCHING...", new Point(10, 50), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 3);
            Cv2.PutText(frame, "SEARCHING...", new Point(10, 50), HersheyFonts.HersheySimplex, 0.6, Scalar.Red, 1);
        }
    }
}

// ==========================================
// 4. CORE VISION PIPELINE
// ==========================================
public static class VisionPipeline
{
    public static async Task RunLoop(
        ICameraSource camera, 
        PITrackerCore.Tracker tracker, 
        TrackerSettings settings, 
        FrameRenderer renderer, 
        InputState input, 
        CancellationToken ct)
    {
        string currentStatus = "SYSTEM READY";

        while (!ct.IsCancellationRequested)
        {
            if (!camera.IsRunning)
            {
                await Task.Delay(100, ct);
                continue;
            }

            using Mat frame = await camera.GetNextFrame();
            if (frame == null || frame.Empty())
            {
                await Task.Delay(5, ct);
                continue;
            }

            // 1. Process Input Commands
            if (input.ConsumeClearRequest())
            {
                tracker.ClearTarget();
                currentStatus = "TARGET CLEARED";
            }

            if (input.ConsumeLockRequest())
            {
                tracker.SetTarget(input.CursorX, input.CursorY, frame.Width, frame.Height);
                currentStatus = $"TRACKING ENGAGED @ ({input.CursorX:F2}, {input.CursorY:F2})";
            }

            // 2. Execute Tracking Algorithm
            var trackState = tracker.TryLock(settings, frame);

            // 3. Render Graphics
            using Mat displayFrame = frame.Clone();
            
            // Draw the manual reticle (always visible and movable)
            DroneHUD.DrawCrosshair(displayFrame, input.CursorX, input.CursorY);
            
            // Draw tracking bounding boxes and text
            DroneHUD.DrawTelemetry(displayFrame, trackState, currentStatus);

            // 4. Push to Analog Framebuffer
            renderer.Display(displayFrame);
            
            // 5. Yield slightly to allow GC and other threads to breathe
            await Task.Delay(1, ct); 
        }
    }
}