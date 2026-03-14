using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Point = OpenCvSharp.Point;

namespace PITrackerCore
{
    public class Tracker
    {
        public ICameraSource Camera { get; private set; }
        public Tracker(ICameraSource camera) { Camera = camera; }
        public delegate void DebugFrameCallback(DebugFrame frame);
        public delegate void TrackOutputCallback(TrackData output);
        public event DebugFrameCallback OnDebugFrame;
        public event DebugFrameCallback OnMicroDebug;
        public event TrackOutputCallback OnTrackOutput;
        bool isRunning = false;

        public LockParameters currentTarget { get; private set; }
        public TrackerSettings currentSettings { get; set; } = new TrackerSettings();

        public bool IsPlayingTracker { get; set; } = false;
        public bool StepNextFrameTracker { get; set; } = false;
        public bool ReprocessCurrentFrame { get; set; } = false;
        private Mat _lastFrame = null;

        public void SetTarget(LockParameters target)
        {
            currentTarget = target;
            ReprocessCurrentFrame = true;
        }

        public void ClearTarget()
        {
            currentTarget = null;
        }

        public void InstantDebug(Mat frame, string label = "Debug")
        {
            OnMicroDebug?.Invoke(new DebugFrame { Frame = frame.Clone(), Label = label });
        }

        private Mat DrawDebugFrame(Mat frame, LockParameters prev, LockParameters current)
        {
            // Helper to get center of a rect
            Point GetCenter(LockParameters p) => new Point((int)(p.X + p.W / 2), (int)(p.Y + p.H / 2));

            // DRAW PREVIOUS STATE (Gray)
            if (prev != null && prev.IsLocked)
            {
                // Last Object
                //Cv2.Rectangle(frame, new Rect((int)prev.X, (int)prev.Y, (int)prev.W, (int)prev.H), Scalar.Gray, 1);
                
                // Last ROI
                if (prev.LastRoi.Width > 0)
                    Cv2.Rectangle(frame, prev.LastRoi, Scalar.Gray, 1);

                // Last Velocity Vector
                //var center = GetCenter(prev);
                //Cv2.Line(frame, center, new Point(center.X + (int)prev.dX, center.Y + (int)prev.dY), Scalar.Gray, 1);
            }

            // DRAW CURRENT STATE
            if (current != null && current.IsLocked)
            {
                // Current ROI (Green)
                if (current.LastRoi.Width > 0)
                    Cv2.Rectangle(frame, current.LastRoi, Scalar.Green, 1);

                // Current Object (Blue, thick)
                Cv2.Rectangle(frame, new Rect((int)current.X, (int)current.Y, (int)current.W, (int)current.H), Scalar.Blue, 2);
                
                // Current Velocity Vector (Green)
                var center = GetCenter(current);
                Cv2.Line(frame, center, new Point(center.X - (int)current.dX, center.Y - (int)current.dY), Scalar.Green, 1);

                Cv2.PutText(frame, $"Conf: {current.Confidence:F2}", new OpenCvSharp.Point((int)current.X, (int)current.Y - 5), HersheyFonts.HersheySimplex, 0.5, Scalar.Green, 1);
            }
            return frame;
        }

        public void BeginAsync()
        {
            if (isRunning)
                return;
            isRunning = true;
            new System.Threading.Thread(async () =>
            {
                while (isRunning)
                {
                    if (!IsPlayingTracker && !StepNextFrameTracker && !ReprocessCurrentFrame)
                    {
                        Thread.Sleep(10); continue;
                    }
                    StepNextFrameTracker = false;
                    
                    Mat frame = null;
                    if (ReprocessCurrentFrame)
                    {
                        ReprocessCurrentFrame = false;
                        if (_lastFrame != null && !_lastFrame.IsDisposed)
                        {
                            frame = _lastFrame.Clone();
                        }
                    }
                    else
                    {
                        frame = await Camera.GetNextFrame();
                        if (frame != null)
                        {
                            _lastFrame?.Dispose();
                            _lastFrame = frame.Clone();
                        }
                    }

                    if (frame != null)
                    {
                        Mat debugFrame = frame.Clone();
                        LockParameters prevTarget = currentTarget;

                        if (currentTarget != null)
                        {
                            currentTarget = TryLock(currentTarget, currentSettings, frame);
                            
                            debugFrame = DrawDebugFrame(debugFrame, prevTarget, currentTarget);
                            if (!currentTarget.IsLocked)
                            {
                                currentTarget = null;
                            }
                        }

                        OnDebugFrame?.Invoke(new DebugFrame { Frame = debugFrame, Label = currentTarget != null ? "Tracking" : "Idle" });
                        frame.Dispose();
                    }
                }
            }).Start();
        }
        public void RequestStop()
        {
            isRunning = true;
        }
        public LockParameters TryLock(LockParameters last, TrackerSettings cfg, Mat frame)
        {
            if (frame == null || frame.Empty()) return new LockParameters { IsLocked = false };

            // --- STEP 1: PREDICT & IDENTIFY ROI ---
            // Estimate 1: Use exact ROI offset of previous
            int est1OffsetX = last.RoiOffsetX;
            int est1OffsetY = last.RoiOffsetY;

            // Estimate 2: Extrapolate based on velocity (dX/dY * MarginFactor)
            int est2OffsetX = (int)(Math.Abs(last.dX) * cfg.MarginFactor) + cfg.MinROI;
            int est2OffsetY = (int)(Math.Abs(last.dY) * cfg.MarginFactor) + cfg.MinROI;

            // Average the two roi offsets
            int newOffsetX = (est1OffsetX + est2OffsetX) / 2;
            int newOffsetY = (est1OffsetY + est2OffsetY) / 2;
            
            // Clamp to min/max allowed
            newOffsetX = Math.Clamp(newOffsetX, cfg.MinROI, cfg.MaxROI);
            newOffsetY = Math.Clamp(newOffsetY, cfg.MinROI, cfg.MaxROI);

            // Construct new ROI rect centered on last known position
            int startX = (int)(last.X - newOffsetX);
            int startY = (int)(last.Y - newOffsetY);
            int endX = (int)(last.X + last.W + newOffsetX);
            int endY = (int)(last.Y + last.H + newOffsetY);

            int rx = Math.Max(0, startX);
            int ry = Math.Max(0, startY);
            int rw = Math.Min(frame.Width, endX) - rx;
            int rh = Math.Min(frame.Height, endY) - ry;

            Rect roiRect = new Rect(rx, ry, rw, rh);
            using Mat roiGray = new Mat();
            using Mat roiView = frame[roiRect];
            Cv2.CvtColor(roiView, roiGray, ColorConversionCodes.BGR2GRAY);

            // --- STEP 2: THRESHOLDING ---
            // Get fresh threshold from Histogram
            int frameThreshold = GetHistogramThreshold(roiGray, cfg);
            int activeThreshold = last.IsManual ? frameThreshold : (int)((last.BinaryThreshold * cfg.ThresholdWeight) + (frameThreshold * (1 - cfg.ThresholdWeight)));

            using Mat binary = new Mat();
            Cv2.Threshold(roiGray, binary, activeThreshold, 255, ThresholdTypes.BinaryInv);
            InstantDebug(binary, "BinaryROI");

            // --- STEP 3: MEASUREMENT (Median and Bounding Rect) ---
            int whiteCount = Cv2.CountNonZero(binary);
            if (whiteCount < cfg.MinObjSize) return new LockParameters { IsLocked = false };

            using Mat locations = new Mat();
            Cv2.FindNonZero(binary, locations);
            var indexer = locations.GetGenericIndexer<System.Drawing.Point>();

            // Compute Median Position (More robust than average for small dots)
            var points = Enumerable.Range(0, whiteCount).Select(i => indexer[i]).ToList();
            int medianX = (int)points.OrderBy(p => p.X).ElementAt(whiteCount / 2).X;
            int medianY = (int)points.OrderBy(p => p.Y).ElementAt(whiteCount / 2).Y;

            // Compute Bounding Rect for Size
            var dimensions = GetObjectDimensionsManual(binary);

            // --- STEP 4: KINEMATICS & CONFIDENCE ---
            double curX = rx + medianX - (dimensions.Width / 2.0);
            double curY = ry + medianY - (dimensions.Height / 2.0);
            
            double curDX = last.IsManual ? 0 : curX - last.X;
            double curDY = last.IsManual ? 0 : curY - last.Y;
            double curDW = last.IsManual ? 0 : dimensions.Width - last.W;
            double curDH = last.IsManual ? 0 : dimensions.Height - last.H;

            // Confidence Calculation
            double velConf = last.dX == 0 ? 1.0 : 1.0 - Math.Min(1.0, Math.Abs((curDX - last.dX) / (last.dX + 0.1)));
            double sizeConf = last.W == 0 ? 1.0 : 1.0 - Math.Min(1.0, Math.Abs((dimensions.Width - last.W) / last.W));
            double currentFrameConf = (velConf + sizeConf) / 2.0;
            double finalConf = last.IsManual ? currentFrameConf : (last.Confidence * cfg.ConfidenceWeight) + (currentFrameConf * (1 - cfg.ConfidenceWeight));

            // --- STEP 5: INTEGRATE PREDICTION ---
            // integration of prediction with measurements
            double smoothedX = last.IsManual ? curX : (curX * (1 - cfg.VelocityWeight)) + ((last.X + last.dX) * cfg.VelocityWeight);
            double smoothedY = last.IsManual ? curY : (curY * (1 - cfg.VelocityWeight)) + ((last.Y + last.dY) * cfg.VelocityWeight);

            return new LockParameters
            {
                X = smoothedX,
                Y = smoothedY,
                W = dimensions.Width,
                H = dimensions.Height,
                dX = curDX,
                dY = curDY,
                dW = curDW,
                dH = curDH,
                RoiOffsetX = newOffsetX,
                RoiOffsetY = newOffsetY,
                BinaryThreshold = activeThreshold,
                Confidence = finalConf,
                LockTime = DateTime.Now,
                IsLocked = finalConf > 0.2, // Failsafe threshold
                IsManual = false,
                LastRoi = roiRect
            };
        }
        public (double Width, double Height) GetObjectDimensionsManual(Mat binary)
        {
            if (binary == null || binary.Empty()) return (0, 0);

            var indexer = binary.GetGenericIndexer<byte>();

            // Fix 1: Correct the array sizing
            float[] rowDensity = new float[binary.Rows]; // Vertical distribution
            float[] colDensity = new float[binary.Cols]; // Horizontal distribution

            for (int y = 0; y < binary.Rows; y++)
            {
                for (int x = 0; x < binary.Cols; x++)
                {
                    if (indexer[y, x] > 0)
                    {
                        // Fix 2: Increment the correct axis
                        rowDensity[y]++;
                        colDensity[x]++;
                    }
                }
            }

            // Fix 3: Logic Check
            // rowDensity.Average() = The average number of white pixels found per row (Object Width)
            // colDensity.Average() = The average number of white pixels found per column (Object Height)
            return (rowDensity.Sum() / rowDensity.Count(r => r > 0), colDensity.Sum() / colDensity.Count(c => c > 0));
        }
        public Mat VisualizeHistogram(float[] histData, int thresholdValue)
        {
            int width = 256;
            int height = 150;
            Mat vis = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);

            // 1. Find the max value to normalize the height
            float maxVal = histData.Max();
            if (maxVal == 0) return vis; // Avoid division by zero

            // 2. Draw the histogram bars
            for (int i = 0; i < width; i++)
            {
                // Calculate bar height relative to the image height
                int barHeight = (int)((histData[i] / maxVal) * height);

                // Draw a vertical line for each bin
                // Point(x, y): (0,0) is top-left in OpenCV
                Cv2.Line(vis,
                    new Point(i, height),
                    new Point(i, height - barHeight),
                    Scalar.White);
            }

            // 3. Draw the Threshold marker (Red line)
            // This helps you see if the threshold is in the "valley" or on a "peak"
            Cv2.Line(vis,
                new Point(thresholdValue, 0),
                new Point(thresholdValue, height),
                new Scalar(0, 0, 255), // Red in BGR
                1);

            return vis;
        }
        private int GetHistogramThreshold_Traveler(Mat gray, TrackerSettings cfg)
        {
            using Mat hist = new Mat();
            Cv2.CalcHist(new Mat[] { gray }, new int[] { 0 }, null, hist, 1, new int[] { 256 }, new Rangef[] { new Rangef(0, 256) });

            float[] h = new float[256];
            for (int i = 0; i < 256; i++) h[i] = hist.At<float>(i);

            // Smooth slightly to avoid getting stuck on tiny "rocks" while climbing down
            float[] smoothH = SmoothHistogram(h, cfg.HistogramSmoothingWindow);

            // 1. Find the Highest Peak (The Mountain Summit)
            int summitIdx = 0;
            float summitHeight = 0;
            for (int i = 0; i < 256; i++)
            {
                if (smoothH[i] > summitHeight)
                {
                    summitHeight = smoothH[i];
                    summitIdx = i;
                }
            }

            // 2. Start Moving Backward (Down the Mountain)
            // We assume a light background, so we move from summitIdx towards 0.
            int currentPos = summitIdx;
            bool hasClimbedDown = false;
            float climbDownThreshold = summitHeight * 0.50f; // Must drop below 50% height

            int threshold = 0;

            for (int i = summitIdx; i >= 0; i--)
            {
                // Phase A: Climbing down the steep slope
                if (!hasClimbedDown)
                {
                    if (smoothH[i] < climbDownThreshold)
                    {
                        hasClimbedDown = true;
                    }
                    continue;
                }

                // Phase B: Looking for the Local Minima (The Valley)
                // We look for a "rise" in the next step (i-1) compared to current step (i)
                if (i > 0 && smoothH[i - 1] > smoothH[i])
                {
                    // We found the bottom! The next pixel is higher, meaning we hit the next hump.
                    threshold = i;
                    break;
                }

                // Phase C: Failsafe - if we reach the absolute bottom (0) without a rise
                if (i == 0)
                {
                    threshold = 0;
                }
            }

            // If we never found a rise, use the last point we reached as the "foot"
            if (threshold == 0 && hasClimbedDown)
            {
                // Find the absolute lowest point in the tail we traversed
                float minTailVal = float.MaxValue;
                for (int i = 0; i < summitIdx; i++)
                {
                    if (smoothH[i] < minTailVal)
                    {
                        minTailVal = smoothH[i];
                        threshold = i;
                    }
                }
            }

            using var histVis = VisualizeHistogram(smoothH, threshold);
            InstantDebug(histVis, "Traveler_Hist");

            return threshold;
        }
        private int GetHistogramThreshold(Mat gray, TrackerSettings cfg)
        {
            using Mat hist = new Mat();
            Cv2.CalcHist(new Mat[] { gray }, new int[] { 0 }, null, hist, 1, new int[] { 256 }, new Rangef[] { new Rangef(0, 256) });

            float[] h = new float[256];
            for (int i = 0; i < 256; i++) h[i] = hist.At<float>(i);
            
            // Apply user-controlled smoothing
            float[] smoothH = SmoothHistogram(h, cfg.HistogramSmoothingWindow);
            // 1. Find the Absolute Primary Peak (Background)
            int primaryPeak = 0;
            float maxVal = 0;
            for (int i = 0; i < 256; i++) {
                if (smoothH[i] > maxVal) {
                    maxVal = smoothH[i];
                    primaryPeak = i;
                }
            }

            // 2. Try to find a distinct secondary peak (Object)
            // We look for the highest point that is at least 40 units away from the primary peak
            int secondaryPeak = -1;
            float secondMaxVal = 0;
            for (int i = 0; i < 256; i++) {
                if (Math.Abs(i - primaryPeak) < 40) continue; 
                if (smoothH[i] > secondMaxVal) {
                    secondMaxVal = smoothH[i];
                    secondaryPeak = i;
                }
            }

            // 3. Logic Branch: Bimodal vs Unimodal
            int threshold = 0;
            if (secondaryPeak != -1 && secondMaxVal > (maxVal * 0.01)) // Found a real second hump
            {
                // Find the lowest point between the two peaks
                int start = Math.Min(primaryPeak, secondaryPeak);
                int end = Math.Max(primaryPeak, secondaryPeak);
                int valley = start;
                float minVal = float.MaxValue;

                for (int i = start; i < end; i++) {
                    if (smoothH[i] < minVal) {
                        minVal = smoothH[i];
                        valley = i;
                    }
                }
                threshold = valley;
            }
            else 
            {
                // UNIMODAL CASE: Only one peak detected
                // If peak is on the right (bright), walk left until the "mountain" ends
                if (primaryPeak > 128) 
                {
                    threshold = primaryPeak - 50; // Fallback
                    for (int i = primaryPeak; i > 0; i--) {
                        // Stop when the population drops to 10% of the peak height
                        if (smoothH[i] < (maxVal * 0.10f)) { threshold = i; break; }
                    }
                }
                else // Peak is on the left (dark background), walk right
                {
                    threshold = primaryPeak + 50; // Fallback
                    for (int i = primaryPeak; i < 256; i++) {
                        if (smoothH[i] < (maxVal * 0.10f)) { threshold = i; break; }
                    }
                }
            }

            using var histVis = VisualizeHistogram(smoothH, threshold);
            InstantDebug(histVis, "Hist");

            return threshold;
        }

        private float[] SmoothHistogram(float[] data, int window) {
            if (window <= 0) return data;
            float[] result = new float[data.Length];
            for (int i = 0; i < data.Length; i++) {
                float sum = 0; int count = 0;
                for (int j = i - window; j <= i + window; j++) {
                    if (j >= 0 && j < data.Length) { sum += data[j]; count++; }
                }
                result[i] = sum / count;
            }
            return result;
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
    public class TrackerSettings
    {
        // Process Controls
        public int MinROI { get; set; } = 40;
        public int MaxROI { get; set; } = 150;
        public int MinObjSize { get; set; } = 2;
        public int MaxObjSize { get; set; } = 300;
        public int HistogramSmoothingWindow { get; set; } = 3;
        
        // Mixing Weights (0.0 to 1.0)
        public float ThresholdWeight { get; set; } = 0.7f; // How much to keep the old threshold
        public float VelocityWeight { get; set; } = 0.6f;  // How much to trust the prediction vs current measurement
        public float ConfidenceWeight { get; set; } = 0.8f; // Smoothing for confidence score
        public float MarginFactor { get; set; } = 1.8f;    // Extra padding for velocity-based ROI
    }

    public class LockParameters
    {
        // Object Properties (Absolute Coordinates)
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }

        // Kinematics (Change per frame)
        public double dX { get; set; }
        public double dY { get; set; }
        public double dW { get; set; }
        public double dH { get; set; }

        // ROI Logic
        public int RoiOffsetX { get; set; } = 50;
        public int RoiOffsetY { get; set; } = 50;

        // State Metadata
        public int BinaryThreshold { get; set; }
        public double Confidence { get; set; } = 1.0;
        public DateTime LockTime { get; set; }
        public bool IsLocked { get; set; }
        public bool IsManual { get; set; }
        public Rect LastRoi { get; set; }
    }
}
