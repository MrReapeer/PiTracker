using System;
using System.Collections.Generic;
using System.Text;

namespace PITrackerCore
{
    public interface ICameraSource
    {
        
    }
    public class CameraSource:ICameraSource
    {
    }
    public class ImageSequenceSource : CameraSource
    {
        private string dir;
        public string[] Files;

        public ImageSequenceSource(string dirOrFile)
        {
            if (File.Exists(dirOrFile))
            {
                this.dir = Path.GetDirectoryName(dirOrFile);
                Files = new string[] { dirOrFile };
            }

            else
            {
                this.dir = dirOrFile;
                Files = Directory.GetFiles("*.jpg").ToArray();
            }
        }
    }
    public class WindowsCameraSource
    {

    }
    public class LinuxCameraSource
    {

    }
}
