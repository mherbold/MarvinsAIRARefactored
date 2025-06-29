using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using MarvinsAIRARefactored.Classes;
namespace MarvinsAIRARefactored.Components
{
    public class SteeringEffects
    {
        Vector2 _cChangeInAngle;

        float _carDirection;

        Vector2 _lastDirectionalVector = Vector2.Zero, _currentDirectionalVector = Vector2.Zero;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProccessUndersteer(ref float torque)
        {

        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProcessEffects(ref float torque)
        { 
            //iv jused torque as a ref to avoid garbarge accumalating
            App app = App.Instance!;

            _lastDirectionalVector = _currentDirectionalVector;

            _currentDirectionalVector.X = app.Simulator.VelocityX;
            _currentDirectionalVector.Y = app.Simulator.VelocityY;

            if (_lastDirectionalVector == Vector2.Zero)
            {
                _lastDirectionalVector = _currentDirectionalVector;
            }

            _lastDirectionalVector = _currentDirectionalVector - _lastDirectionalVector;
            _carDirection = (float)_lastDirectionalVector.Angle();

        }
    }
}

   

