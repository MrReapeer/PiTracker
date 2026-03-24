using System;
using System.IO.Ports;
using System.Threading;

namespace TrackerConsole
{
    public class DroneController
    {
        SerialPort sp;
        public event EventHandler OnModeChange;
        public PursuitMode Mode { get; private set; } = PursuitMode.Controller;// --- Inside DroneController ---

        // State variables for RC overrides
        private ushort _overrideRoll = 0;      // Channel 1
        private ushort _overridePitch = 0;     // Channel 2
        private ushort _overrideThrottle = 0;  // Channel 3
        private ushort _overrideYaw = 0;       // Channel 4
        private ushort _overrideMode = 0;      // Channel 6 (Mode Selector)
        private MAVLink.MavlinkParse _parser = new MAVLink.MavlinkParse();

        // --- Channel Mapping Configuration (Default ArduPilot AETR) ---
        public int RollChannelIndex { get; set; } = 1;
        public int PitchChannelIndex { get; set; } = 2;
        public int ThrottleChannelIndex { get; set; } = 3;
        public int YawChannelIndex { get; set; } = 4;
        
        // Internal state for up to 18 possible MAVLink channels
        // Array index 0 = Channel 1, Index 1 = Channel 2, etc.
        private ushort[] _channelOverrides = new ushort[18];

        // Configuration
        public ushort StabilizeModePwm { get; set; } = 1000; // The PWM value that triggers STABILIZE

        public void TakeOver()
        {
            // Center the primary flight sticks if we are grabbing control from 0
            if (_channelOverrides[RollChannelIndex - 1] == 0) _channelOverrides[RollChannelIndex - 1] = 1500;
            if (_channelOverrides[PitchChannelIndex - 1] == 0) _channelOverrides[PitchChannelIndex - 1] = 1500;
            if (_channelOverrides[YawChannelIndex - 1] == 0) _channelOverrides[YawChannelIndex - 1] = 1500;
            if (_channelOverrides[ThrottleChannelIndex - 1] == 0) _channelOverrides[ThrottleChannelIndex - 1] = 1500;
            
            // Force the mode channel to STABILIZE (using ModeSelector's dynamic channel)
            if (ModeSelector != null)
            {
                _channelOverrides[ModeSelector.ChannelNumber - 1] = StabilizeModePwm; 
            }
            
            SendRcOverride();
        }

        public void ReleaseControl()
        {
            // Rapidly wipe the array to 0 to release all MAVLink control back to the RC radio
            Array.Clear(_channelOverrides, 0, _channelOverrides.Length);
            SendRcOverride();
        }

        public void SetRoll(float amount)
        {
            amount = Math.Clamp(amount, -1f, 1f);
            _channelOverrides[RollChannelIndex - 1] = (ushort)(1500 + (amount * 500));
        }

        public void SetPitch(float amount)
        {
            amount = Math.Clamp(amount, -1f, 1f);
            _channelOverrides[PitchChannelIndex - 1] = (ushort)(1500 + (amount * 500));
        }

        public void SetAscend(float amount)
        {
            amount = Math.Clamp(amount, -1f, 1f);
            _channelOverrides[ThrottleChannelIndex - 1] = (ushort)(1500 + (amount * 500));
        }
        protected void SetOverrideValue(ref MAVLink.mavlink_rc_channels_override_t payload, int channelNumber, ushort value)
        {
            switch (channelNumber)
            {
                case 1: payload.chan1_raw = value; break;
                case 2: payload.chan2_raw = value; break;
                case 3: payload.chan3_raw = value; break;
                case 4: payload.chan4_raw = value; break;
                case 5: payload.chan5_raw = value; break;
                case 6: payload.chan6_raw = value; break;
                case 7: payload.chan7_raw = value; break;
                case 8: payload.chan8_raw = value; break;
                case 9: payload.chan9_raw = value; break;
                case 10: payload.chan10_raw = value; break;
                case 11: payload.chan11_raw = value; break;
                case 12: payload.chan12_raw = value; break;
                case 13: payload.chan13_raw = value; break;
                case 14: payload.chan14_raw = value; break;
                case 15: payload.chan15_raw = value; break;
                case 16: payload.chan16_raw = value; break;
                // MAVLink 1.0 RC Override typically handles 8 channels. 
                // If using MAVLink 2 with an 18-channel struct, you can add cases 9-18 here.
                default: break; 
            }
        }

        private void SendRcOverride()
        {
            if (sp == null || !sp.IsOpen) return;

            // 1. Initialize an empty payload (Defaults to 0)
            var overridePayload = new MAVLink.mavlink_rc_channels_override_t
            {
                target_system = 1,
                target_component = 1
            };

            // 2. Loop through our state array and inject the values using the helper.
            // (Standard MAVLink override payload supports up to 8 channels)
            for (int i = 0; i < 8; i++) 
            {
                // i + 1 translates array index 0 back to Channel 1
                SetOverrideValue(ref overridePayload, i + 1, _channelOverrides[i]);
            }

            // 3. Serialize and Send
            byte[] packetBytes = _parser.GenerateMAVLinkPacket10(
                MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_OVERRIDE, 
                overridePayload, 
                255, 
                0
            );

            sp.Write(packetBytes, 0, packetBytes.Length);
        }
        // User input
        public ToggleSwitchChannel ModeSelector { get;  set; }
        public ToggleSwitchChannel PursuitTrigger { get; set; }
        public ToggleSwitchChannel TargetWindowUpDown { get; set; }
        public ToggleSwitchChannel TargetWindowLeftRight { get; set; }
        public ToggleSwitchChannel EmergencyOverride { get; set; }

        DroneController()
        {
        }
        public static DroneController Open(CancellationToken token)
        {
            var controller = new DroneController();
            controller.BeginAsync(token);
            return controller;
        }
        void BeginAsync(CancellationToken token){
            new Thread(() =>
            {
                Console.WriteLine("Initializing ArduPilot Connection...");

                // 1. Setup the standard Serial Port for the RPi USB connection
                // Note: ArduPilot USB generally defaults to 115200 baud
                string[] portsToTry = { "/dev/ttyUSB0", "/dev/ttyACM0" };
                foreach (var portName in portsToTry)
                {
                    try
                    {
                        using var testPort = new SerialPort(portName, 115200);
                        testPort.Open();
                        Console.WriteLine($"Serial port {portName} opened successfully.");
                        sp = testPort; // Store the successful port for later use
                        break; // Exit the loop if successful
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to open serial port {portName}: {ex.Message}");
                        Console.WriteLine("Did you run: sudo usermod -a -G dialout $USER ?");
                    }
                }
                if (sp == null)
                {
                    Console.WriteLine("Could not open any serial port. Exiting ArduPilot Connection.");
                    return;
                }
                // 2. Initialize the official MAVLink parser
                var mavlinkParser = new MAVLink.MavlinkParse();
                RequestDataStreams(sp, mavlinkParser);
                
                Console.WriteLine("Listening for incoming telemetry...");

                // 3. Read loop
                while (true)
                {
                    if (token.IsCancellationRequested)
                    {
                        sp?.Close();
                        sp?.Dispose();
                        Console.WriteLine("Serial port closed safely.");
                        return;
                    }
                    // Pass the underlying stream directly to the parser. 
                    // It will automatically read the bytes, frame the packet, and return it.
                    var packet = mavlinkParser.ReadPacket(sp.BaseStream);

                    // If 'packet' isn't null, a full MAVLink message was successfully decoded!
                    if (packet != null && packet.data != null)
                    {
                        // Check if it's a Heartbeat message
                        if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
                        {
                            var heartbeat = (MAVLink.mavlink_heartbeat_t)packet.data;
                            // Console.WriteLine($"[Rx Heartbeat] System ID: {packet.sysid}, " +
                            //                   $"Autopilot: {(MAVLink.MAV_AUTOPILOT)heartbeat.autopilot}, " +
                            //                   $"Flight Mode: {heartbeat.custom_mode}");
                        }
                        
                        else if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.RC_CHANNELS)
                        {
                            var rc = (MAVLink.mavlink_rc_channels_t)packet.data;
                            
                            // Channel 6: Mode. [750,1250] , 1750
                            // Channel 7: Treigger over 1500, pursue target if there is one
                            // Channel 8: Stop pursuit, release ovveride on Channel 6
                            // Channel 9: Target window Up/Down
                            // Channel 10: Tarrget window Left/Right
                            // Channel 11: Emergency override, release mode override
                            var channels = new RCChannel[]
                            {
                                ModeSelector,
                                PursuitTrigger,
                                TargetWindowUpDown,
                                TargetWindowLeftRight,
                                EmergencyOverride
                            };
                            foreach (var channel in channels)
                            {
                                channel?.Feed(rc);
                            }
                        }
                    }
                }
            }).Start();
        }
        static void RequestDataStreams(SerialPort port, MAVLink.MavlinkParse parser)
        {
            Console.WriteLine("Requesting data streams from ArduPilot...");

            // 1. Create the request payload
            var request = new MAVLink.mavlink_request_data_stream_t
            {
                target_system = 1,      // The System ID we saw in your heartbeat
                target_component = 1,   // Component 1 is usually the flight controller
                req_stream_id = (byte)MAVLink.MAV_DATA_STREAM.RC_CHANNELS,
                req_message_rate = 5,   // Send it 5 times a second (5Hz)
                start_stop = 1          // 1 = Start sending, 0 = Stop sending
            };

            // 2. Wrap the payload into a full MAVLink packet. 
            // We identify ourselves as System 255 (Standard for a Ground Control Station)
            byte[] packetBytes = parser.GenerateMAVLinkPacket10(
                MAVLink.MAVLINK_MSG_ID.REQUEST_DATA_STREAM, 
                request, 
                255, // Our GCS System ID
                0    // Our GCS Component ID
            );

            // 3. Send it down the USB cable
            port.Write(packetBytes, 0, packetBytes.Length);
            Console.WriteLine("Data stream request sent!");
        }
        public enum PursuitMode
        {
            Controller,
            Pursue,
            LocateAndPursue
        }
        public class RCChannel
        {
            public int ChannelNumber { get; protected set; }
            protected int GetRawValue(MAVLink.mavlink_rc_channels_t rc)
            {
                return ChannelNumber switch
                {
                    1 => rc.chan1_raw,
                    2 => rc.chan2_raw,
                    3 => rc.chan3_raw,
                    4 => rc.chan4_raw,
                    5 => rc.chan5_raw,
                    6 => rc.chan6_raw,
                    7 => rc.chan7_raw,
                    8 => rc.chan8_raw,
                    9 => rc.chan9_raw,
                    10 => rc.chan10_raw,
                    11 => rc.chan11_raw,
                    12 => rc.chan12_raw,
                    13 => rc.chan13_raw,
                    14 => rc.chan14_raw,
                    15 => rc.chan15_raw,
                    16 => rc.chan16_raw,
                    _ => throw new ArgumentException("Invalid channel number")
                };
            }
            protected static float Map(float x, float in_min, float in_max, float out_min, float out_max)
            {
                float v = (x - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;
                if (v < out_min) return out_min;
                if (v > out_max) return out_max;
                return v;
            }
            public virtual void Feed(MAVLink.mavlink_rc_channels_t rc)
            {
                // Base class does nothing. Derived classes will override this to react to RC input.
            }
        }
        public class AnalogChannel: RCChannel
        {

            public float Value { get; private set; } = 0;
            public float InMin { get; set; } = 1000;
            public float InMax { get; set; } = 2000;
            public float OutMin { get; set; } = -1;
            public float OutMax { get; set; } = 1;
            public AnalogChannel(int channelNumber)
            {
                ChannelNumber = channelNumber;
            }
            public override void Feed(MAVLink.mavlink_rc_channels_t rc)
            {
                int rawValue = GetRawValue(rc);
                float v = Map(rawValue, InMin, InMax, OutMin, OutMax);   
                if (v != Value)
                {
                    Value = v;
                    OnValueChanged?.Invoke(this, new ValueChangedEventArgs(v));
                }
            }
            public delegate void ValueChangedHandler(object sender, ValueChangedEventArgs e);
            public event ValueChangedHandler OnValueChanged;
            public class ValueChangedEventArgs : EventArgs
            {
                public float Value { get; private set; }
                public ValueChangedEventArgs(float value)
                {
                    Value = value;
                }
             }
        }
        public class ToggleSwitchChannel: RCChannel
        {

            public int Stop1 { get; set; } = 1250;
            public int Stop2 { get; set; } = 1750;
            public bool Inverted {get; set; } = false;
            public ToggleSwitchState CurrentState { get; private set; } = ToggleSwitchState.Low;
            public ToggleSwitchChannel(int channelNumber)
            {
                ChannelNumber = channelNumber;
            }
            public override void Feed(MAVLink.mavlink_rc_channels_t rc)
            {
                ToggleSwitchState newState;
                int rawValue = GetRawValue(rc);
                if (rawValue < Stop1) newState = ToggleSwitchState.Low;
                else if (rawValue < Stop2) newState = ToggleSwitchState.Mid;
                else newState = ToggleSwitchState.High;

                if (Inverted)
                {
                    if (newState == ToggleSwitchState.Low) newState = ToggleSwitchState.High;
                    else if (newState == ToggleSwitchState.High) newState = ToggleSwitchState.Low;
                }
                if (newState != CurrentState)
                {
                    CurrentState = newState;
                    OnToggleSwitchStateChanged?.Invoke(this, new ToggleSwitchStateEventArgs(newState));
                }
            }
            
            public event ToggleSwitchStateChangedHandler OnToggleSwitchStateChanged;
            public delegate void ToggleSwitchStateChangedHandler(object sender, ToggleSwitchStateEventArgs e);
            public class ToggleSwitchStateEventArgs : EventArgs
            {
                public ToggleSwitchState State { get; private set; }
                public ToggleSwitchStateEventArgs(ToggleSwitchState state)
                {
                    State = state;
                }
            }
        }
        public enum ToggleSwitchState
        {
            Low,
            Mid,
            High
        }
    }
}