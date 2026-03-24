using PITrackerCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace TrackerConsole
{
    public class InputController
    {
        public Tracker Tracker { get; private set; }
        public delegate void TargetChangeHandler(float x, float y);
        public event TargetChangeHandler OnTargetChange;

        public float CursorX { get; private set; } = 0.0F; // Normalized X [-1.0 to 1.0]
        public float CursorY { get; private set; } = 0.0F; // Normalized Y [-1.0 to 1.0]
        void KeyUpdate(string info)
        {
            Console.Write("\r> " + info.PadRight(Console.WindowWidth - 3, ' '), "\r");
        }

        void MoveCursor(double deltaX, double deltaY)
        {
            Tracker.SetInterestZone(CursorX, CursorY, 40, 40);
            CursorX = (float)Math.Clamp(CursorX + deltaX, -1.0, 1.0);
            CursorY = (float)Math.Clamp(CursorY + deltaY, -1.0, 1.0);
            
            OnTargetChange?.Invoke(CursorX, CursorY);
        }
        void ResetCursor()
        {
            CursorX = 0; CursorY = 0;
            Tracker.SetInterestZone(CursorX, CursorY, 40, 40);
            OnTargetChange?.Invoke(CursorX, CursorY);
            KeyUpdate("Reset");
        }

        public void RunBlocking(Tracker tracker)
        {
            this.Tracker = tracker;
            bool firstOutput = false;
            Tracker.OnTrackOutput += (output) =>
            {
                if (firstOutput)
                    Tracker.SetInterestZone(CursorX, CursorY, 40, 40);
                firstOutput = true;
            };
            const double stepSize = 0.05; // 5% of the screen per keystroke

            Console.WriteLine();
            Console.WriteLine("Use cursor keys to control location, C to clear, Esc to exit.");
            Console.Write(">");
            while (true)
            {
                // Intercept=true hides the key from being printed to the console
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true).Key;

                    switch (key)
                    {
                        case ConsoleKey.UpArrow: MoveCursor(0, -stepSize); KeyUpdate("Y--"); break;
                        case ConsoleKey.DownArrow: MoveCursor(0, stepSize); KeyUpdate("Y++"); break;
                        case ConsoleKey.LeftArrow: MoveCursor(-stepSize, 0); KeyUpdate("X--"); break;
                        case ConsoleKey.RightArrow: MoveCursor(stepSize, 0); KeyUpdate("X++"); break;
                        case ConsoleKey.Spacebar: tracker.SetTarget(CursorX, CursorY); KeyUpdate("Lock!"); break;
                        case ConsoleKey.C: tracker.ClearTarget(); Tracker.SetInterestZone(CursorX, CursorY, 40, 40); break;
                        case ConsoleKey.Escape: return;
                    }
                }
                else
                {
                    Thread.Sleep(10); // Prevent 100% CPU core usage on the input thread
                }
            }
        }
    }
}
