#!/bin/bash
# Stop script on any error
set -e

echo "==========================================================="
echo " PiTracker Bootstrap: OpenCV + LibCamera + OpenCvSharp API "
echo "==========================================================="

echo ""
echo "1. Installing system dependencies (OpenCV + GStreamer + Build Tools)..."
sudo apt update
sudo apt install -y cmake git build-essential \
                    libopencv-dev libopencv-videoio-dev libv4l-dev \
                    gstreamer1.0-libcamera gstreamer1.0-tools gstreamer1.0-plugins-good gstreamer1.0-plugins-bad

echo ""
echo "2. Downloading OpenCvSharp source (Tag: 4.6.0.20220608)..."
# We make it in a temporary build directory in the user's home folder
cd ~
if [ -d "opencvsharp_build" ]; then
    rm -rf opencvsharp_build
fi
mkdir opencvsharp_build
cd opencvsharp_build

git clone --branch 4.6.0.20220608 https://github.com/shimat/opencvsharp.git
cd opencvsharp/src/OpenCvSharpExtern

echo ""
echo "3. Patching OpenCvSharp..."
echo "  -> Excluding opencv_contrib modules (xfeatures2d, bgsegm) to ensure build success against base OpenCV."
mv xfeatures2d.cpp xfeatures2d.cpp.bak || true
mv bgsegm.cpp bgsegm.cpp.bak || true

echo ""
echo "4. Building libOpenCvSharpExtern.so natively..."
echo "  -> Note: The 'make -j4' step will take 5-15 minutes on a Raspberry Pi 4."
mkdir build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release
make -j4

echo ""
echo "5. Installing native wrapper to system libraries..."
sudo cp libOpenCvSharpExtern.so /usr/local/lib/
sudo ldconfig

echo ""
echo "6. Cleaning up..."
cd ~
rm -rf opencvsharp_build

echo "==========================================================="
echo " Setup Complete! The Pi is now ready to run PiTracker."
echo " Just deploy your code and run:"
echo "   dotnet run --no-build -r linux-arm64"
echo "==========================================================="
