using Monitor.Components;
using Monitor.Services;
using MudBlazor.Services;

namespace Monitor
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

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

            app.Run();
        }
    }
}
