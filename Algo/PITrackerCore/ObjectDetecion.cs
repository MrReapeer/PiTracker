using System;
using OpenCvSharp;
using OpenCvSharp.Dnn;

public class BackgroundRemover
{
    private Net _net;

    public BackgroundRemover(string modelPath)
    {
        // Load the ONNX model using OpenCV's DNN module
        _net = CvDnn.ReadNetFromOnnx(modelPath);
    }

    public Mat RemoveBackground(Mat originalImage)
    {
        // 1. Preprocess: Create a 4D blob from the image
        // We force it to 128x128 here.
        // scale: 1.0/255.0 normalizes pixel values to [0, 1]
        // mean: ImageNet means (approximate) to match the model's training data
        // swapRB: true converts BGR (OpenCV default) to RGB (Model default)
        Mat blob = CvDnn.BlobFromImage(
            originalImage,
            1.0 / 255.0,
            new Size(320, 320),
            new Scalar(123.675, 116.28, 103.53),
            swapRB: true,
            crop: false
        );

        // 2. Set the blob as input and run the forward pass
        _net.SetInput(blob);
        Mat outputBlob = _net.Forward(); // Returns a 4D tensor: 1 x 1 x 128 x 128

        // 3. Extract the 2D mask from the 4D blob
        // outputBlob.Data is a pointer to the raw float array of the mask
        int maskHeight = outputBlob.Size(2);
        int maskWidth = outputBlob.Size(3);
        Mat mask128 = new Mat(maskHeight, maskWidth, MatType.CV_32FC1, outputBlob.Data);

        // 4. Resize the 128x128 mask back to the ORIGINAL image size
        Mat maskOriginalSize = new Mat();
        Cv2.Resize(mask128, maskOriginalSize, originalImage.Size());

        // 5. Convert the float mask [0.0 to 1.0] to an 8-bit mask [0 to 255]
        maskOriginalSize.ConvertTo(maskOriginalSize, MatType.CV_8UC1, 255.0);

        // Optional: Apply a binary threshold to make edges sharp instead of soft/semi-transparent
        Cv2.Threshold(maskOriginalSize, maskOriginalSize, 127, 255, ThresholdTypes.Binary);

        // 6. Apply the mask to the original image to create a transparent background
        Mat result = new Mat();
        Mat[] originalChannels = Cv2.Split(originalImage);

        if (originalChannels.Length >= 3)
        {
            // Merge Blue, Green, Red, and our new Alpha (Mask) channel
            Mat[] bgraChannels = { originalChannels[0], originalChannels[1], originalChannels[2], maskOriginalSize };
            Cv2.Merge(bgraChannels, result);
        }
        else
        {
            // Fallback just in case the input is grayscale
            result = originalImage.Clone();
        }

        // Clean up memory
        blob.Dispose();
        outputBlob.Dispose();
        mask128.Dispose();
        maskOriginalSize.Dispose();
        foreach (var c in originalChannels) c.Dispose();

        return result; // This Mat is now BGRA with a transparent background!
    }
}