using System;
using System.Threading;
using PITrackerCore;

namespace TrackerConsole
{
    public class VisualPilot
    {
        private Tracker _tracker;
        private DroneController _drone;
        
        // PIDs for X (Roll - Left/Right) and Y (Pitch - Forward/Backward)
        private PidController _rollPid;
        private PidController _pitchPid;

        private Tracker.TrackData _latestTrackData;
        private DateTime _lastLoopTime;

        // Configuration
        public float BaseAscendThrust { get; set; } = 0.3f; // 0.0 to 1.0 (hover to climb)
        public float TargetSizePercentage { get; set; } = 0.4f; // Stop climbing if target fills 40% of frame
        public PilotStatus Status { get; private set; } = PilotStatus.Idle;
        VisualPilot(Tracker tracker, DroneController drone)
        {
            _tracker = tracker;
            _drone = drone;

            // Initialize PIDs. 
            // Roll and Pitch usually share similar gains in symmetrical drones.
            _rollPid = new PidController(kp: 0.6f, ki: 0.05f, kd: 0.15f);
            _pitchPid = new PidController(kp: 0.6f, ki: 0.05f, kd: 0.15f);

            _tracker.OnTrackOutput += HandleTrackOutput;
        }

        private void HandleTrackOutput(Tracker.TrackData data)
        {
            Interlocked.Exchange(ref _latestTrackData, data); // we don't want memory leakages in frame access
        }

        public static VisualPilot Create(Tracker tracker, DroneController drone, CancellationToken token)
        {
            var pilot = new VisualPilot(tracker, drone);
            pilot.BeginAsync(token);
            return pilot;
        }
        void BeginAsync(CancellationToken token)
        {
            new Thread(() =>
            {
                Console.WriteLine("Visual Pilot Control Loop Started.");
                _lastLoopTime = DateTime.UtcNow;
while (!token.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;
                    float dt = (float)(now - _lastLoopTime).TotalSeconds;
                    _lastLoopTime = now;

                    if (dt <= 0) dt = 0.01f; 

                    var track = _latestTrackData;
                    
                    // --- 1. DETERMINE ACTIVE TARGET ---
                    PITrackerCore.LockParameters targetToPursue = null;

                    if (_drone.Mode != DroneController.PursuitMode.Controller && track != null)
                    {
                        // A. Do we have a solid, locked target?
                        if (track.Lock != null && track.Lock.IsLocked)
                        {
                            Status = PilotStatus.Pursuing;
                            targetToPursue = track.Lock;
                        }
                        // B. If no hard lock, are we allowed to search for one?
                        else if (_drone.Mode == DroneController.PursuitMode.LocateAndPursue)
                        {
                            Status = PilotStatus.FindingTarget;
                            // Did the Interest Zone find something promising?
                            if (_tracker.PotentialTarget != null && _tracker.PotentialTarget.IsLocked)
                            {
                                Status = PilotStatus.AutoPursuing;
                                targetToPursue = _tracker.PotentialTarget;
                            }
                            // No potential target either. Ensure the Interest Zone is active in the center!
                            // We check for null so we don't spam the Tracker thread and reset its history every 50ms.
                            else if (_tracker.InterestZone == null)
                            {
                                Status = PilotStatus.Idle;
                                // nx = 0, ny = 0 is the exact center. 
                                // Seed size of 100x100 pixels (adjust based on your camera resolution)
                                _tracker.SetInterestZone(0.0, 0.0, 100, 100);
                            }
                        }
                    }

                    // --- 2. EXECUTE FLIGHT COMMANDS ---
                    if (targetToPursue != null)
                    {
                        Status = PilotStatus.Pursuing;
                        // 1. Calculate Visual Error (-1.0 to 1.0)
                        // pX ranges from 0.0 (Left) to 1.0 (Right). Center is 0.5.
                        float errorX = ((float)targetToPursue.pX - 0.5f) * 2.0f;

                        // Image Y 0 is Top. If object is at the top (pY < 0.5), we want a positive error to pitch Forward (+).
                        float errorY = (0.5f - (float)targetToPursue.pY) * 2.0f;

                        // 2. Compute PID Outputs
                        float rollCommand = _rollPid.Compute(errorX, dt);
                        float pitchCommand = _pitchPid.Compute(errorY, dt);

                        // 3. Calculate Ascend Thrust
                        // The proportion of the frame filled by the object is simply pW * pH
                        float sizeRatio = (float)(targetToPursue.pW * targetToPursue.pH);

                        float ascendCommand = BaseAscendThrust;
                        if (sizeRatio > TargetSizePercentage)
                        {
                            ascendCommand = 0f; // Target reached, just hover (1500 PWM)
                        }
                        
                        // 4. Send Commands
                        _drone.TakeOver(); 
                        _drone.SetRoll(rollCommand);
                        _drone.SetPitch(pitchCommand);
                        _drone.SetAscend(ascendCommand);

                        // 5. Debugging Output
                        Console.WriteLine($"[VisualPilot] ERR -> R: {errorX,5:F2} | P: {errorY,5:F2} || CMD -> R: {rollCommand,5:F2} | P: {pitchCommand,5:F2} | Asc: {ascendCommand,5:F2}");
                    }
                    else
                    {
                        Status = PilotStatus.Idle;
                        // Target lost, or we are in manual Controller mode
                        _rollPid.Reset();
                        _pitchPid.Reset();
                        _drone.ReleaseControl();
                    }

                    // Run loop at ~50Hz
                    Thread.Sleep(20); 
                }
            }).Start();
        }
        public enum PilotStatus
        {
            Idle,
            Controller,
            Pursuing,
            FindingTarget
        }
    }

    public class PidController
    {
        public float Kp { get; set; }
        public float Ki { get; set; }
        public float Kd { get; set; }

        public float OutputMin { get; set; } = -1.0f;
        public float OutputMax { get; set; } = 1.0f;  
        public float Deadband { get; set; } = 0.05f; 

        private float _integral = 0;
        private float _previousError = 0;

        public PidController(float kp, float ki, float kd)
        {
            Kp = kp; Ki = ki; Kd = kd;
        }

        public float Compute(float error, float deltaTime)
        {
            if (Math.Abs(error) < Deadband) error = 0;

            float pOut = Kp * error;

            _integral += error * deltaTime;
            float iOut = Ki * _integral;

            if (iOut > OutputMax) _integral = OutputMax / Ki;
            else if (iOut < OutputMin) _integral = OutputMin / Ki;

            float derivative = (error - _previousError) / deltaTime;
            float dOut = Kd * derivative;
            _previousError = error;

            float totalOutput = pOut + iOut + dOut;
            return Math.Clamp(totalOutput, OutputMin, OutputMax);
        }

        public void Reset()
        {
            _integral = 0;
            _previousError = 0;
        }
    }
}