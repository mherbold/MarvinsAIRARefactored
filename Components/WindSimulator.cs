using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.DataContext;
using System.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

using IRSDKSharper;

namespace MarvinsAIRARefactored.Components
{
    public class WindSimulator
    {
        const int MaxWindSpeed = 320; // this is the maximum speed of the fan
        const float MPS_TO_MPH = 2.23694f;
        const float MPS_TO_KPH = 3.6f;

        static readonly byte[] handshake = { (byte)'w', (byte)'i', (byte)'n', (byte)'d' };

        bool arduinoIsConnected = false;

        int testBand = -1;
        int leftSpinState = 0;
        int rightSpinState = 0;

        readonly DispatcherTimer dispatcherTimer = new();

        readonly ArduinoConnection arduinoConnection = new(handshake);

        private readonly IRacingSdk _irsdk;

    
        // private Settings _settings;
        private bool _arduinoEnabled = false;
        public WindSimulator()
        {
            _irsdk = App.Instance?.Simulator.IRSDK ?? throw new ArgumentNullException("IRSDK is null");

            _irsdk.OnSessionInfo += Irsdk_OnSessionInfo;
            _irsdk.OnTelemetryData += Irsdk_OnTelemetryData;

            App.Instance.Exit += Instance_Exit;

            dispatcherTimer.Tick += OnTick;
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 250);

            dispatcherTimer.Start();

            arduinoConnection.ArduinoConnected += ArduinoConnection_ArduinoConnected;
            arduinoConnection.ArduinoDisconnected += ArduinoConnection_ArduinoDisconnected;

            App.Instance?.MainWindow.Arduino_Connected_Label.Content = "Wind Simulator Disabled";
            App.Instance?.MainWindow.Arduino_Connected_Label.Background = System.Windows.Media.Brushes.Gray;

            if (DataContext.DataContext.Instance.Settings.WindSimulationEnabled)
            {
                arduinoConnection.Start();
                _arduinoEnabled = true;
            }


        }

        private void Instance_Exit(object sender, ExitEventArgs e)
        {
            arduinoConnection.Stop();
        }

        private App _app;
        private void UpdateFanPowers(int leftFanPower, int rightFanPower)
        {
            if (arduinoIsConnected)
            {
                if (_arduinoEnabled)
                {
                    if (leftFanPower < DataContext.DataContext.Instance.Settings.WindSimulationMinimumSpeed)
                        leftFanPower = DataContext.DataContext.Instance.Settings.WindSimulationMinimumSpeed;

                    if (rightFanPower < DataContext.DataContext.Instance.Settings.WindSimulationMinimumSpeed)
                        rightFanPower = DataContext.DataContext.Instance.Settings.WindSimulationMinimumSpeed;

                    if (DataContext.DataContext.Instance.Settings.WindSimulatorPreviewEnabled && Controls.MairaWindSlider.WindPreviewItemIndex != -1)
                    {
                        leftFanPower = DataContext.DataContext.Instance.Settings.WindSimulationFanPower[Controls.MairaWindSlider.WindPreviewItemIndex];
                        rightFanPower = DataContext.DataContext.Instance.Settings.WindSimulationFanPower[Controls.MairaWindSlider.WindPreviewItemIndex];
                    }

                    leftFanPower = (int)((float)leftFanPower * DataContext.DataContext.Instance.Settings.WindSimulationScaleFactor);
                    rightFanPower = (int)((float)rightFanPower * DataContext.DataContext.Instance.Settings.WindSimulationScaleFactor);
                }
                    var arduinoPort = arduinoConnection.ArduinoPort;

                if (arduinoPort != null)
                {
                    try
                    {
                        if (leftFanPower < 10)
                        {
                            leftSpinState = 0;
                        }
                        else if (leftSpinState < 2)
                        {
                            leftSpinState++;

                            leftFanPower = 160;
                        }

                        arduinoPort.Write($"L{leftFanPower:000}");
                     //   App.Instance.Logger.WriteLine($"Left Fan Power: {leftFanPower}#");
                        if (rightFanPower < 10)
                        {
                            rightSpinState = 0;
                        }
                        else if (rightSpinState < 2)
                        {
                            rightSpinState++;

                            rightFanPower = 160;
                        }

                        arduinoPort.Write($"R{rightFanPower:000}");
                    }
                    catch
                    {
                    }
                }
            }
        }
        float speed = 0;
        private (int leftFan, int rightFan) CalculateFanPower(float speed)
        {
           

            int index = Array.FindIndex(DataContext.DataContext.Instance.Settings.WindSimulationSpeedKPH, x=> x > speed);

            if (index == -1)
                return (0, 0); // No valid speed found

            if (index == 0)
                return (0, 0);
            index--;
            float delta = speed - DataContext.DataContext.Instance.Settings.WindSimulationSpeedKPH[index];
            float max = DataContext.DataContext.Instance.Settings.WindSimulationSpeedKPH[index + 1] - DataContext.DataContext.Instance.Settings.WindSimulationSpeedKPH[index];  
            float factor = delta / max;

            float power = DataContext.DataContext.Instance.Settings.WindSimulationFanPower[index] + (DataContext.DataContext.Instance.Settings.WindSimulationFanPower[index + 1] - DataContext.DataContext.Instance.Settings.WindSimulationFanPower[index]) * factor;
            return ((int)power, (int)power); // Return the same power for both fans for now
        }

        (float left, float right) _fanPower = new(0, 0);
        private void OnTick(object? sender, EventArgs e)
        {
            if (!_arduinoEnabled)
            {
                if (DataContext.DataContext.Instance.Settings.WindSimulationEnabled)
                {
                    arduinoConnection.Start();
                    _arduinoEnabled = true;

                    App.Instance?.MainWindow.Arduino_Connected_Label.Content = "Searching Wind Simulator Device...";
                    App.Instance?.MainWindow.Arduino_Connected_Label.Background = System.Windows.Media.Brushes.Red;
                }
            }
            else
            if (!DataContext.DataContext.Instance.Settings.WindSimulationEnabled)
            {

                arduinoConnection.Stop();
                _arduinoEnabled = false;
                App.Instance?.MainWindow.Arduino_Connected_Label.Content = "Wind Simulator Disabled";
                App.Instance?.MainWindow.Arduino_Connected_Label.Background = System.Windows.Media.Brushes.Gray;
                UpdateFanPowers(0, 0);
            }
            if (arduinoIsConnected)
            {
                UpdateFanPowers((int)_fanPower.left, (int)_fanPower.right); // Set initial fan power to 500 for both fans
            }
            else
                UpdateFanPowers(0, 0);
        }

        private void Irsdk_OnTelemetryData()
        {
            if (_isReplay)
                return; // don't do anything in replay mode

            var velocityY = _irsdk.Data.GetFloat("VelocityY", 0); // positive = turning right, negative = turning left
            var velocityX = _irsdk.Data.GetFloat("VelocityX", 0); // positive = forward, negative = backwards

            var z = Math.Max(0, velocityX);
            var lx = Math.Max(0, -velocityY);
            var rx = Math.Max(0, velocityY);

            //   if (!settings.curve)
            {
                z += lx;
                z += rx;

                lx = 0;
                rx = 0;
            }

//            _settings
            //   if (settings.units == Settings.Units.Imperial)
            {
                z *= MPS_TO_MPH;
                lx *= MPS_TO_MPH;
                rx *= MPS_TO_MPH;
            }
            // else
            {
  //              z *= MPS_TO_KPH;
    ///            lx *= MPS_TO_KPH;
       //         rx *= MPS_TO_KPH;
            }

            _fanPower = CalculateFanPower((float)Math.Sqrt(Math.Max(0, (lx * lx) - (rx * rx) + (z * z))));
        }

        private bool _isReplay = false;
        private void Irsdk_OnSessionInfo()
        {
            _isReplay = (_irsdk.Data.SessionInfo.WeekendInfo.SimMode != "full");
        }

      

        private void ArduinoConnection_ArduinoDisconnected(object connection, ArduinoConnection.ConnectionEventArgs connectionInformation)
        {
            arduinoIsConnected = true;

            App.Instance?.Dispatcher.BeginInvoke(() =>
            {
                //Thread.Sleep(1000); // give the UI a chance to update
                App.Instance?.MainWindow.Arduino_Connected_Label.Content = "Searching Simulator Device Not Found...";
                App.Instance?.MainWindow.Arduino_Connected_Label.Background = System.Windows.Media.Brushes.Red;
            });

        }

        private void ArduinoConnection_ArduinoConnected(object connection, ArduinoConnection.ConnectionEventArgs connectionInformation)
        {
            arduinoIsConnected = true;
            
            App.Instance?.Dispatcher.BeginInvoke(() =>
            {
                //Thread.Sleep(1000); // give the UI a chance to update
                App.Instance?.MainWindow.Arduino_Connected_Label.Content = "Wind Simulator Device Connected";
                App.Instance?.MainWindow.Arduino_Connected_Label.Background = System.Windows.Media.Brushes.Green;
            });
            
        }

        public void AppClosing()
        {

            arduinoConnection.Stop();
            dispatcherTimer.Stop();
        }
    }
}
