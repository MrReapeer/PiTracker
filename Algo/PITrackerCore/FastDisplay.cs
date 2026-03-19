using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using OpenCvSharp;

public class FramebufferRenderer : IDisposable
{
    private readonly FileStream _fs;
    private readonly Thread _renderThread;
    private bool _isRunning = true;

    // Concurrency control
    private Mat _pendingFrame = null;
    private readonly object _frameLock = new object();

    // Pre-allocated buffers to prevent Garbage Collection stutters
    private readonly Mat _resizedFrame = new Mat();
    private readonly Mat _fbFrame = new Mat();
    private readonly byte[] _frameBytes;
    
    private readonly int _targetWidth = 720;
    private readonly int _targetHeight = 480;

    public FramebufferRenderer(string devicePath = "/dev/fb0")
    {
        // 1. Open the file stream once
        _fs = new FileStream(devicePath, FileMode.Open, FileAccess.Write);
        
        // 2. Pre-allocate the exact byte array size (720 x 480 x 2 bytes for 16-bit color)
        _frameBytes = new byte[_targetWidth * _targetHeight * 2]; 

        // 3. Start the dedicated background render thread
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "AnalogVTX_Renderer"
        };
        _renderThread.Start();
    }

    // Call this from your TrackerWorker! It returns almost instantly.
    public void Display(Mat newFrame)
    {
        if (newFrame == null || newFrame.Empty()) return;

        lock (_frameLock)
        {
            // Dispose of any unprocessed frame sitting in the queue
            _pendingFrame?.Dispose(); 
            
            // Clone the incoming frame so the Tracker thread can keep working 
            // on the original Mat without crashing the renderer
            _pendingFrame = newFrame.Clone(); 
        }
    }

    private void RenderLoop()
    {
        while (_isRunning)
        {
            Mat frameToRender = null;

            // Safely grab the latest frame from the Tracker
            lock (_frameLock)
            {
                if (_pendingFrame != null)
                {
                    frameToRender = _pendingFrame;
                    _pendingFrame = null; // Empty the queue
                }
            }

            if (frameToRender != null)
            {
                try
                {
                    // 1. Resize to fit the analog VTX resolution
                    Cv2.Resize(frameToRender, _resizedFrame, new Size(_targetWidth, _targetHeight));
                    
                    // 2. Convert to the 16-bit hardware color depth
                    Cv2.CvtColor(_resizedFrame, _fbFrame, ColorConversionCodes.BGR2BGR565);

                    // 3. Extract bytes to our pre-allocated array
                    Marshal.Copy(_fbFrame.Data, _frameBytes, 0, _frameBytes.Length);

                    // 4. Blast to hardware (rewinding the pointer first!)
                    _fs.Seek(0, SeekOrigin.Begin);
                    _fs.Write(_frameBytes, 0, _frameBytes.Length);
                }
                finally
                {
                    // Always clean up the temporary clone to prevent C++ memory leaks
                    frameToRender.Dispose();
                }
            }
            else
            {
                // If the Tracker hasn't produced a new frame yet, sleep for 5ms 
                // so we don't accidentally max out a CPU core doing nothing.
                Thread.Sleep(5);
            }
        }
    }

    public void Dispose()
    {
        _isRunning = false;
        _renderThread.Join(); // Wait for thread to finish
        _fs?.Dispose();
        _resizedFrame?.Dispose();
        _fbFrame?.Dispose();
        _pendingFrame?.Dispose();
    }
}