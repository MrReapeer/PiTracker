using OpenCvSharp;
using PITrackerCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace TrackerConsole
{

    // ==========================================
    // 3. GRAPHICS & OVERLAY (Drone HUD)
    // ==========================================
    public class DroneHUD
    {
        FrameRenderer renderer;
        private DroneHUD()
        {
            renderer = FrameRenderer.Create();
        }
        public static DroneHUD Create(PITrackerCore.Tracker tracker, InputController userinput, VisualPilot pilot, DroneController droneController)
        {
            var hud = new DroneHUD();
            userinput.OnTargetChange += (x, y) => { };
            tracker.OnTrackOutput += (output) =>
            {
                LockParameters roi = output.Lock;
                LockParameters target = output.Lock;
                if (output.Lock == null) // no taget, see if there is interest zone
                {
                    roi = tracker.InterestZone;
                    target = tracker.PotentialTarget;
                }

                if (roi != null)
                {
                    Cv2.Rectangle(output.Frame, roi.RoIRectangle, Scalar.Green, 4);
                    if (output.Lock != null)
                    // Cross hair means we have a lock
                        DrawCrosshair(output.Frame, userinput.CursorX, userinput.CursorY);
                }
                if (target != null)
                {
                    Cv2.Rectangle(output.Frame, target.ObjRectangle, Scalar.Red, 2);
                }
                DrawTelemetry(output.Frame, output.Lock, pilot.Status.ToString());
                hud.renderer.Display(output.Frame);
            };
            return hud;
        }
        static void DrawCrosshair(Mat frame, double nx, double ny)
        {
            int cx = (int)(((nx + 1.0) / 2.0) * frame.Width);
            int cy = (int)(((ny + 1.0) / 2.0) * frame.Height);
            int size = 20;

            // Draw a distinct targeting reticle (White with black shadow for analog visibility)
            Cv2.Line(frame, new Point(cx - size, cy), new Point(cx + size, cy), Scalar.Black, 4);
            Cv2.Line(frame, new Point(cx, cy - size), new Point(cx, cy + size), Scalar.Black, 4);

            Cv2.Line(frame, new Point(cx - size, cy), new Point(cx + size, cy), Scalar.White, 2);
            Cv2.Line(frame, new Point(cx, cy - size), new Point(cx, cy + size), Scalar.White, 2);

            Cv2.Circle(frame, new Point(cx, cy), 4, Scalar.Red, -1); // Solid red center dot
        }

        static void DrawTelemetry(Mat frame, LockParameters state, string statusMsg)
        {
            Cv2.PutText(frame, statusMsg, new Point(10, 25), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 3);
            Cv2.PutText(frame, statusMsg, new Point(10, 25), HersheyFonts.HersheySimplex, 0.7, Scalar.Yellow, 1);

            if (state != null && state.IsLocked)
            {
                // Draw Velocity Vector
                var box = new Rect((int)state.X, (int)state.Y, (int)state.W, (int)state.H);
                var center = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
                var dxEnd = new Point((int)(center.X + state.dX), (int)(center.Y + state.dY));
                Cv2.ArrowedLine(frame, center, dxEnd, Scalar.Red, 2, LineTypes.AntiAlias, 0, 0.3);

                // Draw Confidence
                string lockData = $"LOCKED [Conf: {state.Confidence:F2}]";
                Cv2.PutText(frame, lockData, new Point(10, 50), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 3);
                Cv2.PutText(frame, lockData, new Point(10, 50), HersheyFonts.HersheySimplex, 0.6, Scalar.Green, 1);
            }
            else
            {
                Cv2.PutText(frame, "SEARCHING...", new Point(10, 50), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 3);
                Cv2.PutText(frame, "SEARCHING...", new Point(10, 50), HersheyFonts.HersheySimplex, 0.6, Scalar.Red, 1);
            }
        }

        public void Close()
        {
            renderer.Dispose();
        }
    }
}
