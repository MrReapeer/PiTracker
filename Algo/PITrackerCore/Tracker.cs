using OpenCvSharp;
using OpenCvSharp.XPhoto;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Point = OpenCvSharp.Point;

namespace PITrackerCore
{
    public class Tracker
    {
        public ICameraSource Camera { get; private set; }
        public static Tracker Create()
        {
            var c = new LiveCameraSource();
            var t = new Tracker(c);
            return t;
        }
        private Tracker(ICameraSource camera) { Camera = camera; }
        public delegate void DebugFrameCallback(DebugFrame frame);
        public delegate void TrackOutputCallback(TrackData output);
        public event DebugFrameCallback OnDebugFrame;
        public event DebugFrameCallback OnMicroDebug;
        public event TrackOutputCallback OnTrackOutput;

        public LockParameters currentTarget { get; private set; }
        public LockParameters InterestZone { get; private set; }
        public LockParameters PotentialTarget { get; private set; }
        private OpenCvSharp.Size _lastFrameSize;

        void SetTarget(LockParameters target)
        {
            currentTarget = target;
        }

        public void SetInterestZone(double nx, double ny, int seedW, int seedH)
        {
            var frameWidth = _lastFrameSize.Width;
            var frameHeight = _lastFrameSize.Height;
            // Convert normalized [-1..1] to absolute pixels
            var px = ((nx + 1.0) / 2.0) * frameWidth;
            var py = ((ny + 1.0) / 2.0) * frameHeight;

            InterestZone = new LockParameters
            {
                IsManual = true,
                IsLocked = true,
                IsSeed = true,
                X = px - (seedW / 2.0), // Center the box on the coordinates
                Y = py - (seedH / 2.0),
                W = seedW,
                H = seedH,
                RoiOffsetX = 64,
                RoiOffsetY = 64,
                LockTime = DateTime.UtcNow,
                Confidence = 1.0,
                LastRoi = new Rect((int)(px - seedW), (int)(py - seedH), seedW * 2, seedH * 2),
                DebugInfo = "Interest Zone"
            };
        }
        // Add this inside PITrackerCore.Tracker
        public void SetTarget(double nx, double ny)
        {
            var frameWidth = _lastFrameSize.Width;
            var frameHeight = _lastFrameSize.Height;
            // Convert normalized [-1..1] to absolute pixels
            var px = ((nx + 1.0) / 2.0) * frameWidth;
            var py = ((ny + 1.0) / 2.0) * frameHeight;
            
            // Create a sensible initial seed box (e.g., 1/8th of the screen)
            var seedW = Math.Max(40, frameWidth / 8);
            var seedH = Math.Max(40, frameHeight / 8);

            var manualSeed = new LockParameters
            {
                IsManual = true,
                IsLocked = true,
                IsSeed = true,
                X = px - (seedW / 2.0), // Center the box on the coordinates
                Y = py - (seedH / 2.0),
                W = seedW,
                H = seedH,
                RoiOffsetX = 64,
                RoiOffsetY = 64,
                LockTime = DateTime.UtcNow,
                Confidence = 1.0,
                LastRoi = new Rect((int)(px - seedW), (int)(py - seedH), seedW * 2, seedH * 2),
                DebugInfo = "Manual Keyboard Command"
            };

            lockingFree.WaitOne();
            SetTarget(manualSeed); // Call your existing method
        }
        public void ClearTarget()
        {
            lockingFree.WaitOne();
            currentTarget = null;
            lastProcessed = null;
        }


        CancellationTokenSource loopsCTS;
        public void BeginAsync()
        {
            Camera.Start();
            loopsCTS = new CancellationTokenSource();
            new Thread(async () =>
            {
                var camera = this.Camera;
                var ct = loopsCTS.Token;
                var settings = new TrackerSettings();
                while (!ct.IsCancellationRequested)
                {
                    if (!camera.IsRunning)
                    {
                        await Task.Delay(100, ct);
                        continue;
                    }

                    using Mat frame = await camera.GetNextFrame();
                    _lastFrameSize = new OpenCvSharp.Size(frame.Width, frame.Height);
                    if (frame == null || frame.Empty())
                    {
                        await Task.Delay(5, ct);
                        continue;
                    }


                    // 2. Execute Tracking Algorithm
                    var trackState = TryLock(settings, frame);
                    if (lastProcessed != null) // in tracking
                    {
                        lastProcessed = trackState;
                    }
                    else
                    {
                        if (InterestZone != null && trackState != null)
                            if (trackState.IsLocked)
                                PotentialTarget = trackState;
                    }
                    OnTrackOutput?.Invoke(new TrackData(lastProcessed, frame));
                    if (!frame.IsDisposed)
                        frame.Dispose();
                }
            }).Start(); ;

        } 
        public void RequestStop()
        {
            Camera.Stop();
            loopsCTS.Cancel();
        }
        public static Mat VisualizeHistogramWithThresholds(Mat gray, int safeLow, int safeHigh, int centerEst, TrackerSettings cfg)
        {
            // 1. Calculate the raw histogram using your exact method
            using Mat hist = new Mat();
            Cv2.CalcHist(new Mat[] { gray }, new int[] { 0 }, null, hist, 1, new int[] { 256 }, new Rangef[] { new Rangef(0, 256) });

            float[] h = new float[256];
            for (int i = 0; i < 256; i++) h[i] = hist.At<float>(i);

            // 2. Apply YOUR user-controlled smoothing
            float[] smoothH = SmoothHistogram(h, cfg.HistogramSmoothingWindow);

            // 3. Find the maximum value in the smoothed array so we can scale it to the window
            float maxVal = 0;
            for (int i = 0; i < 256; i++)
            {
                if (smoothH[i] > maxVal) maxVal = smoothH[i];
            }

            // Prevent division by zero if the ROI is completely black
            if (maxVal == 0) maxVal = 1;

            // 4. Setup the visualization image dimensions
            int histW = 512;
            int histH = 300;
            int binW = histW / 256;

            // Create a dark gray background
            Mat histImage = new Mat(histH, histW, MatType.CV_8UC3, new Scalar(30, 30, 30));

            // 5. Highlight the Active Band (The pixels that will turn White)
            int xLow = safeLow * binW;
            int xHigh = safeHigh * binW;
            int xCenterEst = centerEst * binW;

            // Draw a dark blue rectangle behind the kept region
            Cv2.Rectangle(histImage, new Point(xLow, 0), new Point(xHigh, histH), new Scalar(70, 50, 50), -1);

            // 6. Draw the SMOOTHED histogram curve
            for (int i = 1; i < 256; i++)
            {
                // Calculate Y coordinates by normalizing against maxVal. 
                // (histH - 20) leaves a 20-pixel padding at the top of the window.
                int y1 = histH - (int)Math.Round((smoothH[i - 1] / maxVal) * (histH - 20));
                int y2 = histH - (int)Math.Round((smoothH[i] / maxVal) * (histH - 20));

                Point p1 = new Point(binW * (i - 1), y1);
                Point p2 = new Point(binW * i, y2);

                // Draw the line in white
                Cv2.Line(histImage, p1, p2, new Scalar(255, 255, 255), 2, LineTypes.AntiAlias);
            }

            // 7. Draw the Threshold Lines
            // Low Threshold (Green)
            Cv2.Line(histImage, new Point(xLow, 0), new Point(xLow, histH), new Scalar(0, 255, 0), 2);
            Cv2.PutText(histImage, $"Low: {safeLow}", new Point(Math.Max(0, xLow - 60), 20), HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);

            // High Threshold (Red)
            Cv2.Line(histImage, new Point(xHigh, 0), new Point(xHigh, histH), new Scalar(0, 0, 255), 2);
            Cv2.PutText(histImage, $"High: {safeHigh}", new Point(Math.Min(histW - 70, xHigh + 5), 40), HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 0, 255), 1);

            // Mid Estimate (blue)
            Cv2.Line(histImage, new Point(xCenterEst, 0), new Point(xCenterEst, histH), new Scalar(255, 0, 0), 2);
            Cv2.PutText(histImage, $"Center: {centerEst}", new Point(Math.Min(histW - 70, xCenterEst + 5), 40), HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 0, 0), 1);

            return histImage;
        }
        LockParameters lastProcessed = null;
        ManualResetEvent lockingFree = new ManualResetEvent(false);
        public LockParameters TryLock(TrackerSettings cfg, Mat frame)
        {
            lockingFree.Reset();
            var l = _TryLock_(cfg, frame);
            lockingFree.Set();
            return l;
        }
        void UpdateTargetThresholdEstimate(LockParameters currentTarget, Mat frame, TrackerSettings cfg)
        {
            //// 1. Get the center coordinates
            //int cx = (int)(currentTarget.X + currentTarget.W / 2);
            //int cy = (int)(currentTarget.Y + currentTarget.H / 2);

            //// 2. Define a 4x4 bounding box centered around cx, cy (offset by -2)
            //Rect sampleRect = new Rect(cx - 2, cy - 2, 4, 4);

            //// 3. Intersect with frame bounds to prevent OutOfBounds crashes if clicked near the edge
            //Rect frameBounds = new Rect(0, 0, frame.Width, frame.Height);
            //Rect safeRect = sampleRect.Intersect(frameBounds);

            //byte grayValue = 128; // Fallback value just in case the rect is totally invalid

            //if (safeRect.Width > 0 && safeRect.Height > 0)
            //{
            //    // 4. Extract the tiny ROI
            //    using Mat roi = new Mat(frame, safeRect);

            //    // 5. Calculate the average of all pixels in the ROI instantly
            //    Scalar meanColor = Cv2.Mean(roi);

            //    // Scalar stores values as doubles. Val0 = B, Val1 = G, Val2 = R
            //    double b = meanColor.Val0;
            //    double g = meanColor.Val1;
            //    double r = meanColor.Val2;

            //    // Standard CCIR 601 luminance formula
            //    grayValue = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            //}

            //currentTarget.BinaryThresholdLow = grayValue;
            //currentTarget.BinaryThresholdHigh = grayValue;

            // Construct new ROI rect centered on last known position
            int startX = (int)(currentTarget.X);
            int startY = (int)(currentTarget.Y);
            int endX = (int)(currentTarget.X + currentTarget.W);
            int endY = (int)(currentTarget.Y + currentTarget.H);
            int rx = Math.Max(0, startX);
            int ry = Math.Max(0, startY);
            int rw = Math.Min(frame.Width, endX) - rx;
            int rh = Math.Min(frame.Height, endY) - ry;
            Rect roiRect = new Rect(rx, ry, rw, rh);
            using Mat roiGray = new Mat();
            using Mat roiView = frame[roiRect];
            Cv2.CvtColor(roiView, roiGray, ColorConversionCodes.BGR2GRAY);
            //v2.ImShow("temp", roiGray);
            //Cv2.WaitKey(1);
            (currentTarget.BinaryThresholdLow, currentTarget.BinaryThresholdHigh, _) = GetHistogramThresholdRange(roiGray, cfg, 0);

            lastProcessed = currentTarget; // remove previous history
        }
        LockParameters _TryLock_(TrackerSettings cfg, Mat frame)
        {
            bool canPursuePotentialTarget = false;
            if (frame == null || frame.Empty()) // camera failure
            {
                lastProcessed = null;
                return null;
            }
            if (currentTarget != null)  // new manual target
            {
                UpdateTargetThresholdEstimate(currentTarget, frame, cfg);
                // We now wil;l have a "lastProcessed" with IsManual set
                currentTarget = null; // Clear current target to force using the new seed
                InterestZone = null;
            }
            else // continue a track
                if (lastProcessed == null) // no existing tracks
                {
                    // no target, no history.
                    if (InterestZone == null) // not even a potential interest zone
                    {
                        lastProcessed = null;
                        return null;
                    }
                    else
                    {
                        canPursuePotentialTarget = true;
                    }
                }

            // find the last locked state in the history chain

            LockParameters last = null;
            if (lastProcessed != null) {
                var train = new LockTrain(LockParameters.GetLastLocked(LockParameters.GetLastLocked(lastProcessed)), 5);
                last = train.GetSmoothened();
            }
            else if (canPursuePotentialTarget)
            {
                last = InterestZone; // cannot be null in case canPursuePotentialTarget is true
            }
            else
            { 
                // its a mistake, return;
                return null;
            }
            if (last == null)
            { 
                // its a mistake, return;
                return null;
            }
            
            Stopwatch sw = Stopwatch.StartNew();
            StringBuilder sb = new StringBuilder();

            // --- STEP 1: PREDICT & IDENTIFY ROI ---
            long startT = sw.ElapsedTicks;
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
            sb.Append($"ROI:{(sw.ElapsedTicks - startT) * 1000000 / Stopwatch.Frequency}us ");
            startT = sw.ElapsedTicks;

            // --- STEP 2: THRESHOLDING ---
            // Get fresh threshold from Histogram
            var centerEst = (last.BinaryThresholdLow + last.BinaryThresholdHigh) / 2;
            (int frameThresholdLow, int frameThresholdHigh, var type) = GetHistogramThresholdRange(roiGray, cfg, (last.BinaryThresholdLow + last.BinaryThresholdHigh) / 2);
            int activeThresholdHigh = last.IsManual ? frameThresholdHigh : (int)((last.BinaryThresholdHigh * cfg.ThresholdWeight) + (frameThresholdHigh * (1 - cfg.ThresholdWeight)));
            int activeThresholdLow = last.IsManual ? frameThresholdLow : (int)((last.BinaryThresholdLow * cfg.ThresholdWeight) + (frameThresholdLow * (1 - cfg.ThresholdWeight)));
            // Safety check: InRange will fail if Low > High. Clamp them just in case your weights cause a flip.
            int safeLow = Math.Min(activeThresholdLow, activeThresholdHigh);
            int safeHigh = Math.Max(activeThresholdLow, activeThresholdHigh);

            using Mat binary = new Mat();

            Cv2.InRange(roiGray, new Scalar(safeLow), new Scalar(safeHigh), binary);
            //Cv2.ImShow("roi", roiView);
            //Cv2.ImShow("binary", binary);
            //var histVis = VisualizeHistogramWithThresholds(roiGray, safeLow, safeHigh, centerEst, cfg);
            //Cv2.ImShow("hist", histVis);
            //Cv2.MoveWindow("roi", 10, 10);
            //Cv2.MoveWindow("binary", 140, 10);
            //Cv2.MoveWindow("hist", 10, 140);
            //Cv2.WaitKey(1);
            sb.Append($"Thresh:{(sw.ElapsedTicks - startT) * 1000000 / Stopwatch.Frequency}us ");
            startT = sw.ElapsedTicks;

            // --- STEP 3: MEASUREMENT (Median and Bounding Rect) ---
            int whiteCount = Cv2.CountNonZero(binary);
            if (whiteCount < cfg.MinObjSize) 
                return new LockParameters { IsLocked = false };

            using Mat locations = new Mat();
            Cv2.FindNonZero(binary, locations);
            var indexer = locations.GetGenericIndexer<System.Drawing.Point>();

            // Compute Median Position (More robust than average for small dots)
            var points = Enumerable.Range(0, whiteCount).Select(i => indexer[i]).ToList();
            int medianX = (int)points.OrderBy(p => p.X).ElementAt(whiteCount / 2).X;
            int medianY = (int)points.OrderBy(p => p.Y).ElementAt(whiteCount / 2).Y;

            // Compute Bounding Rect for Size
            var dimensions = GetObjectDimensionsManual(binary);
            sb.Append($"Meas:{(sw.ElapsedTicks - startT) * 1000000 / Stopwatch.Frequency}us ");
            startT = sw.ElapsedTicks;

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
            double roiConfX = Math.Min(Math.Max(1.0 - ((double)(newOffsetX - cfg.MaxROI) / cfg.MaxROI), 0), 1);
            double roiConfY = Math.Min(Math.Max(1.0 - ((double)(newOffsetY - cfg.MaxROI) / cfg.MaxROI), 0), 1);
            double thresholdingConf = type == ThresholdDetectionType.TargetedHump ? 1 : (type == ThresholdDetectionType.SmallestPeak ? 0.6 : (type == ThresholdDetectionType.SingleBackground ? 0.1 : 0));
            double roiConf = Math.Min(roiConfX, roiConfY);
            double lostConf = LockParameters.GetLockedUnloackedRatio(lastProcessed, 30);
            double currentFrameConf = (velConf + sizeConf + roiConf + lostConf) / 4.0;

            double finalConf = last.IsManual ? currentFrameConf : (last.Confidence * cfg.ConfidenceWeight) + (currentFrameConf * (1 - cfg.ConfidenceWeight));

            // --- STEP 5: INTEGRATE PREDICTION ---
            // integration of prediction with measurements
            double smoothedX = last.IsManual ? curX : (curX * (1 - cfg.VelocityWeight)) + ((last.X + last.dX) * cfg.VelocityWeight);
            double smoothedY = last.IsManual ? curY : (curY * (1 - cfg.VelocityWeight)) + ((last.Y + last.dY) * cfg.VelocityWeight);
            sb.Append($"Predict:{(sw.ElapsedTicks - startT) * 1000000 / Stopwatch.Frequency}us");

            // Display the frame on screen
            //Cv2.ImShow("Tracker", frame);
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
                BinaryThresholdHigh = activeThresholdHigh,
                BinaryThresholdLow = activeThresholdLow,
                Confidence = finalConf,
                LockTime = DateTime.Now,
                Seed = last,
                IsLocked = finalConf > 0.2, // Failsafe threshold
                IsSeed = false,
                IsManual = false,
                LastRoi = roiRect,
                DebugInfo = sb.ToString()
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
        
        public enum ThresholdDetectionType
        {
            TargetedHump,       // Found and bounded the local hump around the estimate
            SmallestPeak,    // Failed targeted; found global background and secondary peak
            NoRegions,     // No object peak; background is dark, capturing bright tail
            SingleBackground    // No object peak; background is bright, capturing dark tail
        }

        public static (int Low, int High, ThresholdDetectionType DetectionType) GetHistogramThresholdRange(Mat gray, TrackerSettings cfg, int targetEstimate = -1)
        {
            using Mat hist = new Mat();
            Cv2.CalcHist(new Mat[] { gray }, new int[] { 0 }, null, hist, 1, new int[] { 256 }, new Rangef[] { new Rangef(0, 256) });

            float[] h = new float[256];
            for (int i = 0; i < 256; i++)
                h[i] = hist.At<float>(i);

            // Apply user-controlled smoothing
            float[] smoothH = SmoothHistogram(h, cfg.HistogramSmoothingWindow);

            // 1. Find the Absolute Primary Peak (Background) - Needed for fallbacks
            int primaryPeak = 0;
            float maxVal = 0;
            for (int i = 0; i < 256; i++)
            {
                if (smoothH[i] > maxVal)
                {
                    maxVal = smoothH[i];
                    primaryPeak = i;
                }
            }
            // PHASE 1: TARGETED HUMP SEARCH (Refined)
            if (targetEstimate > 0 && targetEstimate < 255)
            {
                // 1. "Climb" to the actual peak starting from the estimate
                // Check left and right to see which way is "up"
                int posOnPeak = 0; // 0 is top, -1 is left of peak, +1 is right of peak
                if (targetEstimate == 0) // Already on fast left
                    posOnPeak = -1;
                else if (targetEstimate == 255)
                    posOnPeak = 1;
                else
                {
                    bool foundOnLeft = false;
                    bool foundOnRight = false;
                    float firstDiffValue = 0;
                    for (int i = targetEstimate; i >= 0; i--)
                    {
                        if (Math.Abs(smoothH[i] - smoothH[targetEstimate]) > smoothH[targetEstimate] * cfg.MinPeakRatio)
                        {
                            firstDiffValue = smoothH[i];
                            foundOnLeft = true;
                            break;
                        }
                    }
                    if (!foundOnLeft)
                    {
                        for (int i = targetEstimate; i < 255; i++)
                        {
                            if (Math.Abs(smoothH[i] - smoothH[targetEstimate]) > smoothH[targetEstimate] * cfg.MinPeakRatio)
                            {
                                firstDiffValue = smoothH[i];
                                foundOnLeft = true;
                                break;
                            }
                        }
                    }
                    if (foundOnLeft || foundOnRight)
                    {
                        if (foundOnLeft)
                        {
                            if (firstDiffValue > smoothH[targetEstimate]) // 
                            {
                                posOnPeak = +1;
                            }
                            else
                                posOnPeak = -1;
                        }
                        else
                        {
                            if (firstDiffValue > smoothH[targetEstimate]) // 
                            {
                                posOnPeak = -1;
                            }
                            else
                                posOnPeak = +1;
                        }
                        int indexOfPeak = -1;
                        int indexOfMaxValue = targetEstimate;
                        // Move towards a peak now
                        if (posOnPeak == -1) // On left, go right
                        {
                            for (int i = targetEstimate + 1; i <= 255; i++)
                            {
                                if (smoothH[i] >= smoothH[indexOfMaxValue])
                                    indexOfMaxValue = i;
                                if (smoothH[i] - smoothH[i - 1] < -smoothH[i - 1] * cfg.MinPeakRatio)
                                {
                                    indexOfPeak = indexOfMaxValue;
                                    break;
                                }
                            }
                        }
                        else if (posOnPeak == +1) // On right, go left
                        {
                            for (int i = targetEstimate - 1; i >= 0; i--)
                            {
                                if (smoothH[i] >= smoothH[indexOfMaxValue])
                                    indexOfMaxValue = i;
                                if (smoothH[i] - smoothH[i + 1] < -smoothH[i + 1] * cfg.MinPeakRatio)
                                {
                                    indexOfPeak = indexOfMaxValue;
                                    break;
                                }
                            }
                        }
                        if (indexOfPeak > 0 && indexOfPeak < 255) // we found a peak. Need to walk toward minima on both directions now
                        {
                            int highAt = indexOfPeak;
                            int lowAt = indexOfPeak;
                            int minRefAt = indexOfPeak;
                            for (int i = indexOfPeak + 1; i <= 255; i++)
                            {
                                if (smoothH[i - 1] < smoothH[minRefAt])
                                    minRefAt = i - 1;
                                if (i == 255 || smoothH[i] - smoothH[minRefAt] > smoothH[minRefAt] * cfg.MinPeakRatio)
                                {
                                    highAt = minRefAt;
                                    break;
                                }
                            }
                            minRefAt = indexOfPeak;
                            for (int i = indexOfPeak - 1; i >= 0; i--)
                            {
                                if (smoothH[i] < smoothH[minRefAt])
                                    minRefAt = i;
                                if (i == 0 || smoothH[i] - smoothH[minRefAt] > smoothH[minRefAt] * cfg.MinPeakRatio)
                                {
                                    lowAt = minRefAt;
                                    break;
                                }
                            }
                            return ((int)Math.Max(0, lowAt), (int)Math.Min(255, highAt), ThresholdDetectionType.TargetedHump);
                        }
                    }
                    else // the histogram is flat
                    { 

                    }
                }
            }

            // PHASE 2: Auto pin
            // Collect Maximas.
            Dictionary<int, int> extremas = new ();
            List<int> peaks = new();
            List<int> valleys = new();
            List<int> regionSizes = new();
            {
                int i = 0;
                bool findingUpEdge = true;
                bool hasDownEdge() => smoothH[i] - smoothH[i - 1] < -smoothH[i] * cfg.MinPeakRatio;
                bool hasUpEdge() => (smoothH[i + 1] - smoothH[i] > smoothH[i] * cfg.MinPeakRatio);
                bool hasRequiredEdge() => findingUpEdge ? hasUpEdge() : hasDownEdge();
                while (i < 255)
                {
                    if (hasRequiredEdge())
                    {
                        if (i == 0) { i++; continue; }
                        extremas[i] = findingUpEdge ? 1 : -1;
                        if (!findingUpEdge)
                            peaks.Add(i);
                        else
                            valleys.Add(i);
                        findingUpEdge = !findingUpEdge;
                    }
                    i++;
                }
                if (valleys.Count >= 3) // at least 2 peaks
                    return (valleys[0], valleys[1], ThresholdDetectionType.SmallestPeak);
                else if (valleys.Count == 2)
                    return (valleys[0], valleys[0], ThresholdDetectionType.SingleBackground);
                else
                    return (0, 0, ThresholdDetectionType.NoRegions);
            }
        }

        private static float[] SmoothHistogram(float[] data, int window) {
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
            public TrackData(LockParameters lock_, Mat mat)
            {
                this.Lock = lock_;
                this.Frame = mat;
            }
            public LockParameters Lock { get; private set; }
            public Mat Frame { get; private set; }
        }
    }
    public class TrackerSettings
    {
        // Process Controls
        public int MinROI { get; set; } = 30;
        public int MaxROI { get; set; } = 75;
        public int MinObjSize { get; set; } = 2;
        public int MaxObjSize { get; set; } = 300;
        public int HistogramSmoothingWindow { get; set; } = 5;
        public float MinPeakRatio { get; set; } = 0.2F;

        // Mixing Weights (0.0 to 1.0)
        public float ThresholdWeight { get; set; } = 0.7f; // How much to keep the old threshold
        public float VelocityWeight { get; set; } = 0.6f;  // How much to trust the prediction vs current measurement
        public float ConfidenceWeight { get; set; } = 0.8f; // Smoothing for confidence score
        public float MarginFactor { get; set; } = 1.8f;    // Extra padding for velocity-based ROI

        // Demo Source
        /// <summary>Path to a directory of .jpg frames used in Demo mode.</summary>
        public string DemoFramesPath { get; set; } = System.IO.Path.Combine(System.AppContext.BaseDirectory, "demo_frames");
    }

    public class LockTrain
    {
        List<LockParameters> history = new List<LockParameters>();
        public LockTrain(LockParameters initial, int maxDepth)
        {
            // use GetLastLocked to get all the history chain from the initial seed
            var current = initial;
            while (current != null)
            {
                if (current.IsLocked)
                    history.Add(current);
                if (history.Count >= maxDepth) break;
                current = current.Seed;
            }
        }
        float decayingAverage(List<float> values)
        {
            // Example of a decaying average giving more weight to recent entries
            float totalWeight = 0;
            float weightedSum = 0;
            float decayFactor = 0.1f; // Recent entries get more weight
            float weight = 1;
            for (int i = values.Count - 1; i >= 0; i--, weight += (int)(weight * decayFactor))
            {
                totalWeight += weight;
                weightedSum += weight * (float)values[i]; // Example using Confidence as the value
            }

            return totalWeight > 0 ? weightedSum / totalWeight : 0;
        }
        public LockParameters GetSmoothened()
        {
            if (history.Count == 0) return null;

            // Simple average of all locked states in the history
            double avgX = decayingAverage(history.Select(p => (float)p.X).ToList());
            double avgY = decayingAverage(history.Select(p => (float)p.Y).ToList());
            double avgW = decayingAverage(history.Select(p => (float)p.W).ToList());
            double avgH = decayingAverage(history.Select(p => (float)p.H).ToList());
            double avfRoiXOffset = decayingAverage(history.Select(p => (float)p.RoiOffsetX).ToList());
            double avfRoiYOffset = decayingAverage(history.Select(p => (float)p.RoiOffsetY).ToList());
            double avgDx = decayingAverage(history.Select(p => (float)p.dX).ToList());
            double avgDy = decayingAverage(history.Select(p => (float)p.dY).ToList());
            double avgDw = decayingAverage(history.Select(p => (float)p.dW).ToList());
            double avgDh = decayingAverage(history.Select(p => (float)p.dH).ToList());
            double binaryThresholdHigh = decayingAverage(history.Select(p => (float)p.BinaryThresholdHigh).ToList());
            double binaryThresholdLow = decayingAverage(history.Select(p => (float)p.BinaryThresholdLow).ToList());


            return new LockParameters
            {            
                X = avgX,
                Y = avgY,
                W = avgW,
                H = avgH,
                RoiOffsetX = (int)avfRoiXOffset,
                RoiOffsetY = (int)avfRoiYOffset,
                dX = avgDx,
                dY = avgDy,
                dW = avgDw,
                dH = avgDh,
                BinaryThresholdHigh = (int)binaryThresholdHigh,
                BinaryThresholdLow = (int)binaryThresholdLow,
                IsManual = false,
                IsLocked = true
            };
        }
    }
    public class LockParameters
    {
        public static float GetLockedUnloackedRatio(LockParameters current, int depth = 10)
        {
            if (current == null) // in case trying a target with no history.
                return 0;
            // ratio of locked and unlocked
            int lockedCount = 0;
            int totalCount = 0;
            var currentNode = current;
            while (currentNode != null && totalCount < depth)            
            {
                if (currentNode.IsLocked) lockedCount++;
                totalCount++;
                currentNode = currentNode.Seed;
            }
            return totalCount > 0 ? (float)lockedCount / totalCount : 0;
        }
        public static LockParameters GetLastLocked(LockParameters current)
        {
            if (current == null)
                return null;
            if (current.IsLocked)
                return current;
            else
                return GetLastLocked(current.Seed);
        }
        
        // LockParameters
        public LockParameters Seed{ get; set; }
        // Object Properties (Absolute Coordinates)
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public Rect ObjRectangle { get => new Rect((int)(X), (int)(Y), (int)W, (int)H); }
        public Rect RoIRectangle { get => new Rect((int)(X - RoiOffsetX), (int)(Y - RoiOffsetY), (int)(W + RoiOffsetX * 2), (int)(H + RoiOffsetY * 2)); }

        // Kinematics (Change per frame)
        public double dX { get; set; }
        public double dY { get; set; }
        public double dW { get; set; }
        public double dH { get; set; }

        // ROI Logic
        public int RoiOffsetX { get; set; } = 50;
        public int RoiOffsetY { get; set; } = 50;

        // State Metadata
        public int BinaryThresholdHigh { get; set; } = 10;
        public int BinaryThresholdLow { get; set; }
        public double Confidence { get; set; } = 1.0;
        public DateTime LockTime { get; set; }
        public bool IsLocked { get; set; }
        public bool IsSeed { get; set; } = true;
        public bool IsManual { get; set; }
        public Rect LastRoi { get; set; }
        public string DebugInfo { get; set; }
    }
}
