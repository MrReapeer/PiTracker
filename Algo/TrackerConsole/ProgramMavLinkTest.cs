using System;
using System.IO.Ports;
using System.Threading;

namespace TrackerConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Initializing ArduPilot Connection...");

            // 1. Setup the standard Serial Port for the RPi USB connection
            // Note: ArduPilot USB generally defaults to 115200 baud
            using var serialPort = new SerialPort("/dev/ttyACM0", 115200);
            
            try
            {
                serialPort.Open();
                Console.WriteLine("Serial port /dev/ttyACM0 opened successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open serial port: {ex.Message}");
                Console.WriteLine("Did you run: sudo usermod -a -G dialout $USER ?");
                return;
            }

            // 2. Initialize the official MAVLink parser
            var mavlinkParser = new MAVLink.MavlinkParse();
            RequestDataStreams(serialPort, mavlinkParser);
            
            Console.WriteLine("Listening for incoming telemetry...");

          // 3. Read loop
            while (true)
            {
                // Pass the underlying stream directly to the parser. 
                // It will automatically read the bytes, frame the packet, and return it.
                var packet = mavlinkParser.ReadPacket(serialPort.BaseStream);

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
                    
                    // Example: Check if it's an Attitude (Pitch/Roll/Yaw) message
                    else if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.ATTITUDE)
                    {
                        var attitude = (MAVLink.mavlink_attitude_t)packet.data;
                        double roll = attitude.roll * (180.0 / Math.PI);
                        double pitch = attitude.pitch * (180.0 / Math.PI);
                        //Console.WriteLine($"[Rx Attitude] Roll: {roll:F2}°, Pitch: {pitch:F2}°");
                    }
                    // Check if it's the raw RC Channels message
                    else if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_RAW)
                    {
                        var rc = (MAVLink.mavlink_rc_channels_raw_t)packet.data;

                        // ArduPilot reports RC inputs in PWM (Pulse Width Modulation).
                        // Standard values range from ~1000 to ~2000. 
                        // If your receiver is disconnected or the radio is off, these often drop to 0.
                        
                        bool isRcConnected = rc.chan3_raw > 0; // Checking throttle is a quick connection test
                        
                        Console.WriteLine($"[RC Data] Connected: {isRcConnected} | RSSI: {rc.rssi}");
                        Console.WriteLine($"  -> CH1 (Roll)    : {rc.chan1_raw}");
                        Console.WriteLine($"  -> CH2 (Pitch)   : {rc.chan2_raw}");
                        Console.WriteLine($"  -> CH3 (Throttle): {rc.chan3_raw}");
                        Console.WriteLine($"  -> CH4 (Yaw)     : {rc.chan4_raw}");
                        Console.WriteLine($"  -> CH5 (Switch)  : {rc.chan5_raw}");
                    }
                    else if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.RC_CHANNELS)
                    {
                        var rc = (MAVLink.mavlink_rc_channels_t)packet.data;
                        
                        // Grouping by 4 keeps the console output clean and easy to read
                        Console.WriteLine("[Rx RC_CHANNELS]");
                        Console.WriteLine($"  CH1-4  : {rc.chan1_raw}, {rc.chan2_raw}, {rc.chan3_raw}, {rc.chan4_raw}");
                        Console.WriteLine($"  CH5-8  : {rc.chan5_raw}, {rc.chan6_raw}, {rc.chan7_raw}, {rc.chan8_raw}");
                        Console.WriteLine($"  CH9-12 : {rc.chan9_raw}, {rc.chan10_raw}, {rc.chan11_raw}, {rc.chan12_raw}");
                        Console.WriteLine($"  CH13-16: {rc.chan13_raw}, {rc.chan14_raw}, {rc.chan15_raw}, {rc.chan16_raw}");
                        Console.WriteLine($"  RSSI   : {rc.rssi}"); 
                    }
                }
            }
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
    }
}