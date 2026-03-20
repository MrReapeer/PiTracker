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
}