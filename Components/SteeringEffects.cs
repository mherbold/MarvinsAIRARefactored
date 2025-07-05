using MarvinsAIRARefactored.Classes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace MarvinsAIRARefactored.Components
{

    [DebuggerDisplay("Speed = {Speed}, Yaw = {Yaw}")]
    public class SpeedSteeringLink
    {
        public int Speed;
        public float Yaw;

        public SpeedSteeringLink()
        {

        }

        public SpeedSteeringLink(int speed, float yaw)
        {
            this.Speed = speed;
            this.Yaw = yaw;
        }

    }

    public class AngleSteeringLink
    {
        public int Angle;
        public float Yaw;

        public AngleSteeringLink()
        {

        }

        public AngleSteeringLink(int angle, float yaw)
        {
            this.Angle = angle;
            this.Yaw = yaw;
        }
    }
    [DebuggerDisplay("Recoding = {_recordProfile}, Links = {Links.Count}")]
    public class SteertingProfile
    {
        private readonly float DistanceAllowedWheelAngle = MathHelper.ToRadians(2);
        private readonly float RecordingWheelAngle90 = MathHelper.ToRadians(90);
        private readonly float RecordingWheelAngle45 = MathHelper.ToRadians(45);
        private readonly float RecordingWheelAngle135 = MathHelper.ToRadians(135);

        /// <summary>
        /// maybe we could use a dropoff rate to detect when the car is starting to understeer
        /// </summary>
        private const float YawRateDropOffEnd = .007f;
        public List<SpeedSteeringLink> Links90 { get; private set; } = new List<SpeedSteeringLink>();
        public List<SpeedSteeringLink> Links135 { get; private set; } = new List<SpeedSteeringLink>();
        public List<SpeedSteeringLink> Links45 { get; private set; } = new List<SpeedSteeringLink>();

        //expermental
        public List<AngleSteeringLink> AngleLinks { get; private set; } = new List<AngleSteeringLink>();

        public bool UnderSteerEffectEnabled = true;
        public bool OverSteerEffectEnabled = true;
        private bool _recordProfile = true;

        /// <summary>
        /// the point of the yaw data where the tire scrub starts to kick in
        /// </summary>
        private int YawCurveStartPoint = -1;

        private float YawCurveFactor = 1;


        private float PredictionStartYawValue, PredictionEndYawValue;
        private float PredictionStartSpeed;

        private float PredictionYawPerWheelAngle;

        public float PredictedYawRate { get; private set; }

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
            /* expermimental code, work in progress proberbly be dumped

                        if (speed >= 9.5 && speed <= 10.5f) //16kph
                        {
                            int wheelAngle = ((int)(_simulator.SteeringWheelAngle / 5f)) * 5;

                            if (AngleLinks.FirstOrDefault(x => x.Angle == wheelAngle) == null)
                            {
                                AngleLinks.Add(new AngleSteeringLink(wheelAngle, YawRate));
                            }
                        }
            */
            //we will record 90, 45 and 135 angles


            if (MathHelper.Distance(Math.Abs(_simulator.SteeringWheelAngle), RecordingWheelAngle45) < DistanceAllowedWheelAngle)
            {
                int nearestSpeed = ((int)(speed / 2f)) * 2;

                if (Links45.FirstOrDefault(x => x.Speed == nearestSpeed) == null)
                    if (MathHelper.Distance(nearestSpeed, speed) < .6f)
                    {
                        Links45.Add(new SpeedSteeringLink(nearestSpeed, YawRate));

                    }
            }


            if (MathHelper.Distance(Math.Abs(_simulator.SteeringWheelAngle), RecordingWheelAngle135) < DistanceAllowedWheelAngle)
            {
                int nearestSpeed = ((int)(speed / 2f)) * 2;

                if (Links135.FirstOrDefault(x => x.Speed == nearestSpeed) == null)
                    if (MathHelper.Distance(nearestSpeed, speed) < .6f)
                    {
                        Links135.Add(new SpeedSteeringLink(nearestSpeed, YawRate));

                    }
            }

            if (MathHelper.Distance(Math.Abs(_simulator.SteeringWheelAngle), RecordingWheelAngle90) < DistanceAllowedWheelAngle)
            {
                int nearestSpeed = ((int)(speed / 2f)) * 2;

                if (Links90.FirstOrDefault(x => x.Speed == nearestSpeed) == null)
                    if (MathHelper.Distance(nearestSpeed, speed) < .6f)
                    {
                        Links90.Add(new SpeedSteeringLink(nearestSpeed, YawRate));

                    }


                if (speed > 80)
                {
                    DumpDataToFile();
                    _recordProfile = false;
                    FinalizeProfile();
                }
            }

        }

        private void SaveXml(string path, List<SpeedSteeringLink> ldata)
        {
            System.Xml.Serialization.XmlSerializer data = new System.Xml.Serialization.XmlSerializer(typeof(List<SpeedSteeringLink>));
            //now lets write some tmp code to save the data so we can load it up for some testing
            using (StreamWriter writer = new StreamWriter(path))
            {
                data.Serialize(writer, ldata);
            }
        }


        private void DumpDataToFile()
        {
            string path = Path.Combine(App.DocumentsFolder, $"{_simulator.CarScreenName}-90.csv");
            
            using (TextWriter writer = new StreamWriter(path))
            {
                //write header
                writer.WriteLine($"{_simulator.CarScreenName} - 90");
                writer.WriteLine($"Speed, Yaw");
                foreach(var item in Links90)  
                    writer.WriteLine($"{item.Speed}, {item.Yaw}");
            }

            if (Links45.Count != 0)
            {
                path = Path.Combine(App.DocumentsFolder, $"{_simulator.CarScreenName}-45.csv");
                using (TextWriter writer = new StreamWriter(path))
                {
                    //write header
                    writer.WriteLine($"{_simulator.CarScreenName} - 45");
                    writer.WriteLine($"Speed, Yaw");
                    foreach (var item in Links45)
                        writer.WriteLine($"{item.Speed}, {item.Yaw}");
                }
            }

            if (Links135.Count != 0)
            {
                path = Path.Combine(App.DocumentsFolder, $"{_simulator.CarScreenName}-135.csv");
                using (TextWriter writer = new StreamWriter(path))
                {
                    //write header
                    writer.WriteLine($"{_simulator.CarScreenName} - 135");
                    writer.WriteLine($"Speed, Yaw");
                    foreach (var item in Links135)
                        writer.WriteLine($"{item.Speed}, {item.Yaw}");
                }
            }


            try
            {
                SaveXml(Path.Combine(App.DocumentsFolder, $"{_simulator.CarScreenName}.xml"), Links90);
            }
            catch (Exception ex) 
            {

            }
        }

        /// <summary>
        /// the last thing to do is to analize the data collected
        /// </summary>
        private void FinalizeProfile()
        {
            Links90 = Links90.OrderBy(x => x.Speed).ToList();

            byte yawDropCount = 0;
            int index = -1;
            for (int i = 0; i < Links90.Count - 1; i++)
            {
                if (Links90[i].Yaw > Links90[i + 1].Yaw) //look for when the yaw drops
                {
                    yawDropCount++;
                    if (index == -1)
                        index = i;
                    if (yawDropCount > 2) //if 3 or more drops then we found the correct place
                        break;
                }
                else
                {
                    index = -1;
                    yawDropCount = 0; //reset drop count
                }
            }

            YawCurveStartPoint = index; //set the point where the tires start to slip and the yaw rate starts to drop off

            //now we need to calculate the curve factor
            //we need to do this to calculate how the tire slip will work           
            float start, end;
            start = Links90[YawCurveStartPoint].Yaw;
            end = Links90[Links90.Count-1].Yaw;

            float curveFactor = 0f; //curve factor is how much of a curve we want
            float marginOfError = 0; 
            float bestMargineOfError = float.PositiveInfinity;
            float predicion = 0;

            //we will loop through 10 times changing the curve factor
            //we will mesure the margin of error between the real result and predicted result
            //the smallest margin of error will be the best curvFactor to use
            for (int c = 0; c < 10; c++)
            {
                marginOfError = 0;
                
                for (int i = YawCurveStartPoint; i < Links90.Count; i++)
                {
                    float f = (i - YawCurveStartPoint) / (float)(Links90.Count - YawCurveStartPoint - 1);
                    predicion = MathHelper.QuadraticBezier(start, (start * curveFactor) + (end * (1 - curveFactor)), end, f);
                    
                    marginOfError += Math.Abs(1 - (predicion / Links90[i].Yaw));
                }

                if (marginOfError < bestMargineOfError)
                {
                    bestMargineOfError = marginOfError;
                    YawCurveFactor = curveFactor; //get the current best curve factor and save it
                }
                curveFactor += .05f;
                curveFactor = Math.Clamp(curveFactor, 0, 1);
            }

           //write the predictated data to file so we can plot it in excel
            using (TextWriter writer = new StreamWriter(Path.Combine(App.DocumentsFolder, $"{_simulator.CarScreenName}PRE.csv")))
            {
                //write header
                writer.WriteLine($"{_simulator.CarScreenName}");
                writer.WriteLine($"Speed, Yaw");
                for (int i = YawCurveStartPoint; i < Links90.Count; i++)
                {
                    float f = (i - YawCurveStartPoint) / (float)(Links90.Count - YawCurveStartPoint - 1);
                    writer.WriteLine($"{Links90[i].Speed}, {MathHelper.QuadraticBezier(start, (start * YawCurveFactor) + (end * (1 - YawCurveFactor)), end, f)}");
                    // foreach (var item in predicted)
                    // writer.WriteLine($"{item.Speed}, {item.Yaw}");
                }
            }

            PredictionStartSpeed = Links90[YawCurveStartPoint].Speed;
            PredictionStartYawValue = Links90[YawCurveStartPoint].Yaw;
            PredictionEndYawValue = Links90[Links90.Count-1].Yaw;
            
        }
        bool _tryLoad = false;
        public void Reset()
        {
            _recordProfile = true;
            Links90.Clear();

            _tryLoad = true;
            
        }

        private void ProcessUnderSteer()
        {
            if (Links90.Count == 0)
                return;
        }

        private void ProcessOverSteer()
        {
            if (Links90.Count == 0)
                return;
        }

        public void Update()
        {
            if (_tryLoad)
            {
                if (_simulator.CarScreenName == string.Empty)
                    return;
                //mess around and try to load the data for testing
                string pathxml = Path.Combine(App.DocumentsFolder, $"{_simulator.CarScreenName}.xml");

                if (File.Exists(pathxml))
                {
                    System.Xml.Serialization.XmlSerializer data = new System.Xml.Serialization.XmlSerializer(typeof(List<SpeedSteeringLink>));
                    using (StreamReader reader = new StreamReader(pathxml))
                    {
                        Links90 = (List<SpeedSteeringLink>)data.Deserialize(reader);
                    }
                    FinalizeProfile(); //we stil need to finalize the profile
                  //  _recordProfile = false;
                }
                _tryLoad = false;
            }


            if (_recordProfile)
            {
                RecordProfile();
                return;
            }

            //run predictions

            int speed = ((int)(MathHelper.ToMPH(_simulator.Velocity) / 2)) * 2;
            var pdata = Links90?.FirstOrDefault(x => x.Speed == speed);
            if (pdata == null)
            {
                return;
            }
            if (speed >= PredictionStartSpeed)
            {
                float t = (MathHelper.ToMPH(_simulator.Velocity) - PredictionStartSpeed) / (80f - PredictionStartSpeed);
                t = Math.Clamp(t, 0, 1);
                PredictedYawRate = MathHelper.QuadraticBezier(PredictionStartYawValue, (PredictionStartYawValue * YawCurveFactor) + (PredictionEndYawValue * (1 - YawCurveFactor)), PredictionEndYawValue, t);

                PredictedYawRate = (PredictedYawRate / MathHelper.PIOVER2) * _simulator.SteeringWheelAngle;
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

            _app.Debug.Label_8 = $"Pre: {_profile.PredictedYawRate:0.00}";
            _app.Debug.Label_9 = $"Act: {_simulator.IRSDK.Data.GetFloat("YawRate"):0.00}";

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

   

