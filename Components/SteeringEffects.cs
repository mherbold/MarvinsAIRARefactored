using MarvinsAIRARefactored.Classes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using System.IO;
namespace MarvinsAIRARefactored.Components
{

    [DebuggerDisplay("Speed = {Speed}, Yaw = {Yaw}")]
    public class SpeedSteeringLink
    {
        public int Speed;
        public float Yaw;

        public SpeedSteeringLink(int speed, float yaw)
        {
            this.Speed = speed;
            this.Yaw = yaw;
        }

    }
    [DebuggerDisplay("Recoding = {_recordProfile}, Links = {Links.Count}")]
    public class SteertingProfile
    {
        private readonly float DistanceAllowedWheelAngle = MathHelper.ToRadians(2);
        private readonly float RecordingWheelAngle = MathHelper.ToRadians(90);

        /// <summary>
        /// maybe we could use a dropoff rate to detect when the car is starting to understeer
        /// </summary>
        private const float YawRateDropOffEnd = .007f;
        public List<SpeedSteeringLink> Links { get; private set; } = new List<SpeedSteeringLink>();

        public bool UnderSteerEffectEnabled = true;
        public bool OverSteerEffectEnabled = true;
        private bool _recordProfile = false;

        public Action<string> WriteLineToLog;

        Simulator _simulator;

        public SteertingProfile(Simulator sim)
        {
            _simulator = sim;
        }
        public void StartProfileSetup()
        {
            if (_simulator.Velocity < 1)
                return; //cannot setup as car is moving

            StartLogingData(); //this needs moving over to a button

        }

        private void StartLogingData()
        {
            _recordProfile = true;
            WriteLineToLog($"Loging Data For {_simulator.CarScreenName}");
        }

        private void RecordProfile()
        {
            float YawRate = _simulator.IRSDK.Data.GetFloat("YawRate");

            float speed = MathHelper.ToMPH(_simulator.Velocity);


            if (MathHelper.Distance(Math.Abs(_simulator.SteeringWheelAngle), RecordingWheelAngle) < DistanceAllowedWheelAngle)
            {
                int nearestSpeed = ((int)(speed / 2f)) * 2;

                if (Links.FirstOrDefault(x => x.Speed == nearestSpeed) == null)
                    if (MathHelper.Distance(nearestSpeed, speed) < .6f)
                    {
                        Links.Add(new SpeedSteeringLink(nearestSpeed, YawRate));
                       
                    }


                if (speed > 80)
                {
                    FinalizeProfile();
                }
            }

        }


        private void DumpDataToFile()
        {

            string path = Path.Combine(App.DocumentsFolder, $"{_simulator.CarScreenName}.csv");

            
            using (TextWriter writer = new StreamWriter(path))
            {
                //write header
                writer.WriteLine($"{_simulator.CarScreenName}");
                writer.WriteLine($"Speed, Yaw");
                foreach(var item in Links)  
                    writer.WriteLine($"{item.Speed}, {item.Yaw}");
            }
        }

        private void FinalizeProfile()
        {
            DumpDataToFile();
            _recordProfile = false;
            
        }

        public void Reset()
        {
            _recordProfile = true;
            Links.Clear();
        }

        private void ProcessUnderSteer()
        {
            if (Links.Count == 0)
                return;
        }

        private void ProcessOverSteer()
        {
            if (Links.Count == 0)
                return;
        }

        public void Update()
        {
            if (_recordProfile)
            {
                RecordProfile();
                return;
            }

        }

        public void Process()
        {
            if (UnderSteerEffectEnabled)
                ProcessUnderSteer();

            if (OverSteerEffectEnabled)
                ProcessOverSteer();
        }
    }

    public class SteeringEffects
    {

        private Simulator _simulator;

        SteertingProfile _profile;

        private Logger _logger;

        private App _app;
        
        public SteeringEffects()
        {
            _app = App.Instance!;
            _app.Simulator.IRSDK.OnConnected += IRSDK_OnConnected;
            _app.Simulator.IRSDK.OnTelemetryData += IRSDK_OnTelemetryData;

            _simulator = _app.Simulator;

            _profile = new SteertingProfile(_simulator);
            _profile.WriteLineToLog += WriteLineToLog;
        }
        
        private void WriteLineToLog(string text)
        {
            _logger.WriteLine(text);
        }

        private void IRSDK_OnTelemetryData()
        {
            _profile.Update();
            //just for testing
      
            _app.Debug.Label_10 = $"{MathHelper.ToDegress(_simulator.SteeringWheelAngle)}";
           
        }

        private void IRSDK_OnConnected()
        {
            _profile.Reset();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProcessEffects(ref float torque)
        { 
            //iv jused torque as a ref to avoid garbarge accumalating
            App app = App.Instance!;

            _profile.Process();

   
        }
    }
}

   

