using Monitor.Components;
using Monitor.Services;
using MudBlazor.Services;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using PITrackerCore;
using System.Threading.Tasks;

namespace Monitor
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Contains("--find-camera"))
            {
                PITrackerCore.LiveCameraSource.FindCamera();
                return;
            }

            if (args.Contains("--test-profiles"))
            {
                PITrackerCore.LiveCameraSource.TestProfiles();
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

            // Hardware Integration service for GPIO limits and Serial
            builder.Services.AddHostedService<HardwareIntegrationService>();

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
    }
}
