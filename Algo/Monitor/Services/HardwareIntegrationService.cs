using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monitor.Services;
using PITrackerCore;
using System;
using System.Device.Gpio;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Monitor.Services
{
    public class HardwareIntegrationService : BackgroundService
    {
        private readonly VisionStateService _state;
        private readonly ILogger<HardwareIntegrationService> _logger;
        
        private GpioController? _gpio;
        private SerialPort? _serial;
        private string _currentPortName = "";
        private int _currentPin = -1;
        private string _serialBuffer = "";

        public HardwareIntegrationService(VisionStateService state, ILogger<HardwareIntegrationService> logger)
        {
            _state = state;
            _logger = logger;
        }

        private void SetupGpio(int pin)
        {
            if (_currentPin == pin) return;
            
            // Clean up old
            if (_gpio != null)
            {
                try { _gpio.Dispose(); } catch { }
                _gpio = null;
            }

            _currentPin = pin;
            if (pin > 0)
            {
                try 
                {
                    _gpio = new GpioController();
                    _gpio.OpenPin(pin, PinMode.InputPullUp);
                    _gpio.RegisterCallbackForPinValueChangedEvent(pin, PinEventTypes.Falling, OnGpioTriggered);
                    _logger.LogInformation("GPIO trigger registered on pin {Pin}", pin);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to initialize GPIO trigger on pin {Pin}: {Msg}", pin, ex.Message);
                    _gpio = null;
                }
            }
        }

        private void SetupSerialPort(string portName)
        {
            if (_currentPortName == portName && _serial != null && _serial.IsOpen) return;

            // Cleanup old
            if (_serial != null)
            {
                try { 
                    if (_serial.IsOpen) _serial.Close();
                    _serial.Dispose(); 
                } catch { }
                _serial = null;
            }

            _currentPortName = portName;
            _serialBuffer = "";

            if (!string.IsNullOrWhiteSpace(portName))
            {
                try
                {
                    _serial = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
                    _serial.Open();
                    _state.SerialConnected = true;
                    _state.SerialStatus = $"Connected: {portName}";
                    _logger.LogInformation("Opened Serial Port {PortName}", portName);
                }
                catch (Exception ex)
                {
                    _state.SerialConnected = false;
                    _state.SerialStatus = $"Error: {ex.Message}";
                    _logger.LogWarning("Failed to open Serial Port {PortName}: {Msg}", portName, ex.Message);
                    _serial = null;
                }
            }
            else
            {
                _state.SerialConnected = false;
                _state.SerialStatus = "Disconnected";
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("HardwareIntegrationService starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Check if config changed
                    SetupGpio(_state.HardwareConfig.HardwareTriggerPin);
                    SetupSerialPort(_state.HardwareConfig.SerialPortName);

                    if (_serial != null && _serial.IsOpen)
                    {
                        // Write current lock state based on a throttle or just continuously
                        // To not overwhelm, we send it since last loop which is ~20ms
                        var lockData = _state.CurrentLock;
                        if (lockData != null)
                        {
                            var payload = new
                            {
                                lockData.IsLocked,
                                lockData.X,
                                lockData.Y,
                                lockData.W,
                                lockData.H,
                                lockData.dX,
                                lockData.dY,
                                lockData.Confidence
                            };
                            string json = JsonSerializer.Serialize(payload) + "\n";
                            byte[] bytes = Encoding.ASCII.GetBytes(json);
                            await _serial.BaseStream.WriteAsync(bytes, 0, bytes.Length, stoppingToken);
                        }

                        // Read from serial port non-blocking
                        if (_serial.BytesToRead > 0)
                        {
                            string data = _serial.ReadExisting();
                            _serialBuffer += data;
                            int newline = _serialBuffer.IndexOf('\n');
                            while (newline != -1)
                            {
                                string line = _serialBuffer.Substring(0, newline).Trim();
                                _serialBuffer = _serialBuffer.Substring(newline + 1);
                                
                                if (line.StartsWith("lock", StringComparison.OrdinalIgnoreCase) && line.Contains("["))
                                {
                                    try 
                                    {
                                        int start = line.IndexOf('[') + 1;
                                        int end = line.IndexOf(']');
                                        string[] parts = line.Substring(start, end - start).Split(',');
                                        if (parts.Length == 2)
                                        {
                                            double fx = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                                            double fy = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                                            
                                            // -1 is left edge, 1 is right edge
                                            double rx = (fx + 1.0) / 2.0;
                                            // fy -1 is bottom edge, fy 1 is top edge
                                            double ry = (-fy + 1.0) / 2.0;
                                            
                                            _state.ProcessLockRequest(rx, ry);
                                            _logger.LogInformation("Received serial lock command: {Fx}, {Fy}", fx, fy);
                                        }
                                    }
                                    catch (Exception pex)
                                    {
                                        _logger.LogWarning("Failed to parse serial lock command: {Line} - {Msg}", line, pex.Message);
                                    }
                                }
                                
                                newline = _serialBuffer.IndexOf('\n');
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!(ex is OperationCanceledException))
                    {
                        _state.SerialConnected = false;
                        _state.SerialStatus = $"Error: {ex.Message}";
                        // We will try opening again next loop if settings match, maybe clear current to force reopen
                        _currentPortName = "";
                    }
                }

                await Task.Delay(20, stoppingToken);
            }

            // Cleanup
            if (_gpio != null)
            {
                try { _gpio.Dispose(); } catch { }
            }
            if (_serial != null)
            {
                try {
                    if (_serial.IsOpen) _serial.Close();
                    _serial.Dispose();
                } catch { }
            }
        }

        private void OnGpioTriggered(object sender, PinValueChangedEventArgs pinValueChangedEventArgs)
        {
            _logger.LogInformation("GPIO Trigger pulled! Requesting lock at center.");
            _state.ProcessLockRequest(0.5, 0.5);
        }
    }
}
