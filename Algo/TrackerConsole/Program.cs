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
        var cs = new CancellationTokenSource();

        var userInput = new InputController();
        var tracker = PITrackerCore.Tracker.Create();
        var hud = DroneHUD.Create(tracker, userInput);
        var droneController = DroneController.Open(cs.Token);
        var pilot = VisualPilot.Create(tracker, droneController, cs.Token);

        // Start tracking
        tracker.BeginAsync();

        // console debug Loop
        userInput.RunBlocking(tracker);

        //Cleanup
        tracker.RequestStop();
        hud.Close();
        cs.Cancel();
    }
}