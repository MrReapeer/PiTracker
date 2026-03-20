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

        void MoveCursor(double deltaX, double deltaY)
        {
            CursorX = (float)Math.Clamp(CursorX + deltaX, -1.0, 1.0);
            CursorY = (float)Math.Clamp(CursorY + deltaY, -1.0, 1.0);
            OnTargetChange?.Invoke(CursorX, CursorY);
        }
        void ResetCursor()
        {
            CursorX = 0; CursorY = 0;
            OnTargetChange?.Invoke(CursorX, CursorY);
        }

        public void RunBlocking(Tracker tracker)
        {
            this.Tracker = tracker;
            const double stepSize = 0.05; // 5% of the screen per keystroke

            while (true)
            {
                // Intercept=true hides the key from being printed to the console
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true).Key;

                    switch (key)
                    {
                        case ConsoleKey.UpArrow: MoveCursor(0, -stepSize); break;
                        case ConsoleKey.DownArrow: MoveCursor(0, stepSize); break;
                        case ConsoleKey.LeftArrow: MoveCursor(-stepSize, 0); break;
                        case ConsoleKey.RightArrow: MoveCursor(stepSize, 0); break;
                        case ConsoleKey.Spacebar: tracker.SetTarget(CursorX, CursorY); break;
                        case ConsoleKey.C: ResetCursor(); tracker.ClearTarget(); break;
                        case ConsoleKey.Escape: break;
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
