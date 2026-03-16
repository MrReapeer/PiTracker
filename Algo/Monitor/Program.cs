using Monitor.Components;
using Monitor.Services;
using MudBlazor.Services;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using PITrackerCore;

namespace Monitor
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Contains("--find-camera"))
            {
                FindCamera();
                return;
            }

            var builder = WebApplication.CreateBuilder(args);

            // Configure listening URLs (listen on all interfaces for external access)
            builder.WebHost.UseUrls("http://0.0.0.0:5062");

            // Detect Local IP for display
            string externalUrl = "http://[YOUR_IP]:5062";
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ip != null) externalUrl = $"http://{ip}:5062";
            }
            catch { /* Ignore */ }

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
            builder.Services.AddMudServices();

            // PiTracker services
            builder.Services.AddSingleton<VisionStateService>();
            // Register TrackerWorker as a singleton so pages can @inject it by type,
            // then wire it up as the hosted service using the same instance.
            builder.Services.AddSingleton<TrackerWorker>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<TrackerWorker>());

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            Console.WriteLine("=================================================");
            Console.WriteLine(" PiTracker Monitor Link Established ");
            Console.WriteLine("=================================================");
            Console.WriteLine($" Local:    http://localhost:5062");
            Console.WriteLine($" Network:  {externalUrl}");
            Console.WriteLine("=================================================");

            app.Run();
        }

        private static void FindCamera()
        {
            Console.WriteLine();
            Console.WriteLine("=================================================");
            Console.WriteLine("  PiTracker Camera Finder");
            Console.WriteLine("=================================================");
            Console.WriteLine("Scanning /dev/video* devices — this may take a");
            Console.WriteLine("few seconds per device due to V4L2 timeouts.");
            Console.WriteLine("=================================================");
            Console.WriteLine();

            if (!System.Runtime.InteropServices.RuntimeInformation
                    .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                Console.WriteLine("Not running on Linux — nothing to scan.");
                return;
            }

            var videoDevices = System.IO.Directory.GetFiles("/dev", "video*")
                                                  .OrderBy(f => f)
                                                  .ToArray();

            if (videoDevices.Length == 0)
            {
                Console.WriteLine("ERROR: No /dev/video* devices found. Is a camera connected?");
                return;
            }

            Console.WriteLine($"Found {videoDevices.Length} device(s). Testing each...");
            Console.WriteLine();

            using var testFrame = new OpenCvSharp.Mat();
            int recommendedId = -1;

            foreach (var dev in videoDevices)
            {
                if (!int.TryParse(dev.Replace("/dev/video", ""), out int idx)) continue;

                Console.Write($"  /dev/video{idx,-3}  ");
                try
                {
                    using var cap = new OpenCvSharp.VideoCapture(idx, OpenCvSharp.VideoCaptureAPIs.V4L2);
                    if (!cap.IsOpened())
                    {
                        Console.WriteLine("cannot open              [skip]");
                        continue;
                    }

                    int w = (int)cap.Get(OpenCvSharp.VideoCaptureProperties.FrameWidth);
                    int h = (int)cap.Get(OpenCvSharp.VideoCaptureProperties.FrameHeight);

                    bool gotFrame = false;
                    for (int i = 0; i < 3; i++)
                    {
                        cap.Read(testFrame);
                        if (!testFrame.Empty()) { gotFrame = true; break; }
                        System.Threading.Thread.Sleep(100);
                    }

                    if (gotFrame)
                    {
                        Console.WriteLine($"opened  {w}x{h,-6}  hasFrame=YES  *** USE THIS: deviceId={idx} ***");
                        if (recommendedId < 0) recommendedId = idx;
                    }
                    else
                    {
                        Console.WriteLine($"opened  {w}x{h,-6}  hasFrame=NO   (metadata/ISP pipe, skip)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                }
            }

            Console.WriteLine();
            if (recommendedId >= 0)
            {
                Console.WriteLine($"=================================================");
                Console.WriteLine($"  Recommended device ID: {recommendedId}");
                Console.WriteLine($"  In TrackerWorker.cs, change:");
                Console.WriteLine($"    var live = new LiveCameraSource(0, _logger);");
                Console.WriteLine($"  to:");
                Console.WriteLine($"    var live = new LiveCameraSource({recommendedId}, _logger);");
                Console.WriteLine($"=================================================");
            }
            else
            {
                Console.WriteLine("No device returned a frame. Check camera connection.");
            }
            Console.WriteLine();
        }
    }
}
