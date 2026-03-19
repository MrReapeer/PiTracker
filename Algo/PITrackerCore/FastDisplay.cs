using System;
using System.Threading;
using System.Runtime.InteropServices;
using OpenCvSharp;
using System.Diagnostics; // Add this at the top

public abstract class FrameRenderer : IDisposable
{
    public static FrameRenderer Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("[System] Windows detected. Using Windowed Renderer.");
            return new WinFrameRenderer();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Console.WriteLine("[System] Linux detected. Using Direct Framebuffer Renderer.");
            return new LinuxFrameRenderer();
        }
        else
        {
            throw new PlatformNotSupportedException("OS not supported for rendering.");
        }
    }

    // This is required in the base class so your main app can call it polymorphically
    public abstract void Display(Mat newFrame);

    public abstract void Dispose();
}
public class LinuxFrameRenderer : FrameRenderer
{
    // --- Native Linux Interop for Memory Mapping ---
    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr addr, long length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(IntPtr addr, long length);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    private const int O_RDWR = 2;
    private const int PROT_READ = 1;
    private const int PROT_WRITE = 2;
    private const int MAP_SHARED = 1;

    // --- State Variables ---
    private readonly Thread _renderThread;
    private bool _isRunning = true;
    private readonly int _fd;
    private readonly IntPtr _fbPtr;
    private readonly long _fbSize;

    // Concurrency control
    private Mat _pendingFrame = null;
    private readonly object _frameLock = new object();

    // Pre-allocated OpenCV buffers
    private readonly Mat _resizedFrame = new Mat();
    private readonly Mat _fbFrame = new Mat();
    private readonly int _targetWidth = 720;
    private readonly int _targetHeight = 480;

    public LinuxFrameRenderer(string devicePath = "/dev/fb0")
    {
        // 1. Calculate exact memory size (720x480 @ 16-bit color = 2 bytes per pixel)
        _fbSize = _targetWidth * _targetHeight * 2;

        // 2. Open the framebuffer file at the native OS level
        _fd = open(devicePath, O_RDWR);
        if (_fd < 0) throw new Exception($"Failed to open {devicePath}. Run as root or add user to 'video' group.");

        // 3. Map the hardware video memory directly into our C# app!
        _fbPtr = mmap(IntPtr.Zero, _fbSize, PROT_READ | PROT_WRITE, MAP_SHARED, _fd, 0);
        if (_fbPtr == new IntPtr(-1)) throw new Exception("Failed to mmap framebuffer.");

        // 4. Start the dedicated background render thread
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "AnalogVTX_FastRenderer"
        };
        _renderThread.Start();
    }

    public override void Display(Mat newFrame)
    {
        if (newFrame == null || newFrame.Empty()) return;

        lock (_frameLock)
        {
            _pendingFrame?.Dispose(); 
            _pendingFrame = newFrame.Clone(); 
        }
    }
    // Inside your FramebufferRenderer class:
    private unsafe void RenderLoop()
    {
        // The exact milliseconds for one 60Hz NTSC frame
        double targetFrameTimeMs = 1000.0 / 60.0; 
        
        Stopwatch sw = new Stopwatch();

        while (_isRunning)
        {
            sw.Restart(); // Start timing the frame

            Mat frameToRender = null;

            lock (_frameLock)
            {
                if (_pendingFrame != null)
                {
                    frameToRender = _pendingFrame;
                    _pendingFrame = null;
                }
            }

            if (frameToRender != null)
            {
                try
                {
                    // 1. Process image
                    Cv2.Resize(frameToRender, _resizedFrame, new Size(_targetWidth, _targetHeight));
                    Cv2.CvtColor(_resizedFrame, _fbFrame, ColorConversionCodes.BGR2BGR565);

                    // 2. Blast memory
                    Buffer.MemoryCopy(
                        _fbFrame.DataPointer, 
                        _fbPtr.ToPointer(), 
                        _fbSize, 
                        _fbSize
                    );
                }
                finally
                {
                    frameToRender.Dispose();
                }
            }

            // 3. THE PHASE LOCK: Wait exactly until the 16.66ms mark
            sw.Stop();
            double elapsedMs = sw.Elapsed.TotalMilliseconds;
            
            if (elapsedMs < targetFrameTimeMs)
            {
                // Calculate exactly how much time is left in our 16.66ms window
                double timeToWait = targetFrameTimeMs - elapsedMs;
                
                // Thread.Sleep only accepts whole integers, so we cast it.
                // For hyper-precision, you could use a spin-wait here, but Sleep is usually fine.
                Thread.Sleep((int)timeToWait); 
            }
            
            // --- THE TEAR DIAL ---
            // If the tear is frozen right in the middle of your screen, 
            // uncomment the line below and adjust the number (1 to 16) 
            // to manually push the tear down until it falls off the bottom edge!
            // Thread.Sleep(5); 
        }
    }

    public override void Dispose()
    {
        _isRunning = false;
        _renderThread?.Join();
        
        // Clean up the native memory map
        if (_fbPtr != IntPtr.Zero && _fbPtr != new IntPtr(-1)) munmap(_fbPtr, _fbSize);
        if (_fd >= 0) close(_fd);

        _resizedFrame?.Dispose();
        _fbFrame?.Dispose();
        _pendingFrame?.Dispose();
    }
}
// ==========================================
// WINDOWS IMPLEMENTATION (OpenCV Window)
// ==========================================
public class WinFrameRenderer : FrameRenderer
{
    private readonly Thread _renderThread;
    private bool _isRunning = true;

    private Mat _pendingFrame = null;
    private readonly object _frameLock = new object();

    private readonly string _windowName = "Edge Telemetry HUD (Windows Dev)";

    public WinFrameRenderer()
    {
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "Win_FastRenderer"
        };
        _renderThread.Start();
    }

    public override void Display(Mat newFrame)
    {
        if (newFrame == null || newFrame.Empty()) return;

        lock (_frameLock)
        {
            _pendingFrame?.Dispose();
            _pendingFrame = newFrame.Clone();
        }
    }

    private void RenderLoop()
    {
        while (_isRunning)
        {
            Mat frameToRender = null;

            lock (_frameLock)
            {
                if (_pendingFrame != null)
                {
                    frameToRender = _pendingFrame;
                    _pendingFrame = null;
                }
            }

            if (frameToRender != null)
            {
                try
                {
                    Cv2.ImShow(_windowName, frameToRender);
                }
                finally
                {
                    frameToRender.Dispose();
                }
            }
            else
            {
                // Sleep to avoid thrashing the CPU if no new frames arrive
                Thread.Sleep(5);
            }

            // HighGUI requires pumping the message loop on the thread that created the window
            Cv2.WaitKey(1);
        }

        // Clean up the GUI window when the thread exits
        Cv2.DestroyAllWindows();
    }

    public override void Dispose()
    {
        _isRunning = false;
        _renderThread?.Join();
        _pendingFrame?.Dispose();
    }
}