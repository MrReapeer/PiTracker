using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using PITrackerCore;
using TrackerConsole;

internal class Program
{
    public static async Task Main(string[] args)
    {
        //var rembg = new BackgroundRemover("u2netp.onnx");
        //foreach (var file in Directory.GetFiles("rembg_samples/trimmed"))
        //{
        //    if (file.Contains("rembg."))
        //        continue;
        //    Console.WriteLine("Processing: " + Path.GetFileNameWithoutExtension(file));
        //    using var iMat = Cv2.ImRead(file);
        //    using var remBG = rembg.RemoveBackground(iMat);
        //    // Split the BGRA image to get access to our transparency mask
        //    Mat[] channels = Cv2.Split(remBG);

        //    Cv2.ImShow("src", iMat);
        //    if (channels.Length == 4)
        //    {
        //        // 1. Show the raw AI mask (White = Object, Black = Background)
        //        Cv2.ImShow("AI Alpha Mask", channels[3]);

        //        // 2. Force the background to actually render black in ImShow
        //        using Mat blackBgPreview = new Mat(iMat.Size(), MatType.CV_8UC3, Scalar.Black);
        //        iMat.CopyTo(blackBgPreview, channels[3]); // Copies only where mask is white

        //        Cv2.ImShow("Extracted Object", blackBgPreview);
        //    }
        //    else
        //    {
        //        Cv2.ImShow("Output", remBG);
        //    }
        //    // 2. Move them side-by-side (adjust the X coordinates based on your image widths)
        //    Cv2.MoveWindow("src", 0, 0);
        //    Cv2.MoveWindow("AI Alpha Mask", 128, 0);
        //    Cv2.MoveWindow("Extracted Object", 256, 0);
        //    Cv2.WaitKey();
        //}
        //return;
        // Setup
        var userInput = new InputController();
        var tracker = PITrackerCore.Tracker.Create();
        tracker.OnTrackOutput += (ouput) => { };
        var hud = DroneHUD.Create(tracker, userInput);
        tracker.BeginAsync();

        //Loop
        userInput.RunBlocking(tracker);

        //Cleanup
        tracker.RequestStop();
        hud.Close();
    }
    //public static void BatchCropAndProcess(string inputFolder, string outputFolder)
    //{
    //    // Define the initial search area
    //    Rect searchRect = new Rect(1100, 500, 300, 300);

    //    // Define the padding around the detected object center (100x100 box means +/- 50 from center)
    //    int cropOffsetX = 50;
    //    int cropOffsetY = 50;

    //    if (!Directory.Exists(outputFolder))
    //    {
    //        Directory.CreateDirectory(outputFolder);
    //    }

    //    string[] supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
    //    var files = Directory.GetFiles(inputFolder)
    //                         .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
    //                         .ToArray();

    //    Console.WriteLine($"Found {files.Length} images to process.");

    //    foreach (string file in files)
    //    {
    //        using (Mat originalMat = Cv2.ImRead(file, ImreadModes.Color))
    //        {
    //            if (originalMat.Empty())
    //            {
    //                Console.WriteLine($"Skipping (Failed to load): {file}");
    //                continue;
    //            }

    //            // 1. Validate the initial search Rect against image bounds
    //            Rect imageBounds = new Rect(0, 0, originalMat.Width, originalMat.Height);
    //            Rect safeSearchRect = searchRect.Intersect(imageBounds);

    //            if (safeSearchRect.Width <= 0 || safeSearchRect.Height <= 0)
    //            {
    //                Console.WriteLine($"Skipping (Search area outside image bounds): {Path.GetFileName(file)}");
    //                continue;
    //            }

    //            // 2. Extract ROI and convert to Grayscale
    //            using (Mat roiView = new Mat(originalMat, safeSearchRect))
    //            using (Mat roiGray = new Mat())
    //            {
    //                Cv2.CvtColor(roiView, roiGray, ColorConversionCodes.BGR2GRAY);

    //                // 3. Thresholding (Finding dark objects on plain background)
    //                int frameThreshold = PITrackerCore.Tracker.GetHistogramThreshold(roiGray, new TrackerSettings()); // Use your custom method here

    //                using (Mat binary = new Mat())
    //                {
    //                    // BinaryInv turns dark objects white, which is required for CountNonZero/FindNonZero
    //                    Cv2.Threshold(roiGray, binary, frameThreshold, 255, ThresholdTypes.BinaryInv);

    //                    // 4. Measurement (Find white pixels)
    //                    int whiteCount = Cv2.CountNonZero(binary);

    //                    // If no object found, skip or handle as needed
    //                    if (whiteCount < 10) // Adjust this minimum size based on your 'cfg.MinObjSize'
    //                    {
    //                        Console.WriteLine($"Skipping (No object detected in search area): {Path.GetFileName(file)}");
    //                        continue;
    //                    }

    //                    using (Mat locations = new Mat())
    //                    {
    //                        Cv2.FindNonZero(binary, locations);
    //                        var indexer = locations.GetGenericIndexer<System.Drawing.Point>();

    //                        // 5. Compute Median Position within the ROI
    //                        var points = Enumerable.Range(0, whiteCount).Select(i => indexer[i]).ToList();

    //                        // Ordering by X and Y separately to find the median
    //                        int medianRoiX = points.OrderBy(p => p.X).ElementAt(whiteCount / 2).X;
    //                        int medianRoiY = points.OrderBy(p => p.Y).ElementAt(whiteCount / 2).Y;

    //                        // Translate ROI coordinates back to Absolute Image Coordinates
    //                        int absoluteCenterX = safeSearchRect.X + medianRoiX;
    //                        int absoluteCenterY = safeSearchRect.Y + medianRoiY;

    //                        // 6. Define the final crop bounding box around the detected center
    //                        int cropStartX = absoluteCenterX - cropOffsetX;
    //                        int cropStartY = absoluteCenterY - cropOffsetY;
    //                        int cropWidth = cropOffsetX * 2;  // 100
    //                        int cropHeight = cropOffsetY * 2; // 100

    //                        Rect finalCropRect = new Rect(cropStartX, cropStartY, cropWidth, cropHeight);
    //                        searchRect = finalCropRect;
    //                        searchRect.Inflate(100, 100);
    //                        Rect safeCropRect = finalCropRect.Intersect(imageBounds);

    //                        if (safeCropRect.Width <= 0 || safeCropRect.Height <= 0)
    //                        {
    //                            Console.WriteLine($"Skipping (Final crop area invalid): {Path.GetFileName(file)}");
    //                            continue;
    //                        }

    //                        // 7. Crop and Save
    //                        using (Mat finalCroppedMat = new Mat(originalMat, safeCropRect).Clone())
    //                        {
    //                            string fileName = Path.GetFileName(file);
    //                            string outputPath = Path.Combine(outputFolder, fileName);

    //                            Cv2.ImWrite(outputPath, finalCroppedMat);
    //                            Console.WriteLine($"Successfully cropped and saved: {fileName}");
    //                        }
    //                    }
    //                }
    //            }
    //        }
    //    }

    //    Console.WriteLine("Batch processing complete.");
    //}
}