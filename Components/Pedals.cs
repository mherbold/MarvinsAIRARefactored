
using Simagic;

using ComboBox = System.Windows.Controls.ComboBox;

namespace MarvinsAIRARefactored.Components;

public class Pedals
{
	private const float deltaSeconds = 1f / 20f;

	private const float _gearChangeTimeNeutral = 0.025f;
	private const float _gearChangeTimeOther = 0.1f;

	private const float _gearChangeFrequencyNeutral = 35f;
	private const float _gearChangeFrequencyOther = 15f;

	private const float _gearChangeAmplitude = 1f;

	private const float _absEngagedFrequency = 25f;

	private const float _clutchSlipAmplitude = 0.5f;

	private readonly HPR _hpr = new();

	private int _gearLastFrame = 0;

	private float _gearChangeFrequency = 0f;
	private float _gearChangeTimer = 0f;

	public void Initialize()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[Pedals] Initialize >>>" );

			var pedals = _hpr.Initialize();

			app.Logger.WriteLine( $"[Pedals] Simagic HPR API reports: {pedals}" );

			app.Dispatcher.BeginInvoke( () =>
			{
				switch ( pedals )
				{
					case HPR.Pedals.None:
						app.MainWindow.Pedals_Device_Label.Content = DataContext.Instance.Localization[ "PedalsNone" ];
						break;

					case HPR.Pedals.P1000:
						app.MainWindow.Pedals_Device_Label.Content = DataContext.Instance.Localization[ "PedalsP1000" ];
						break;

					case HPR.Pedals.P2000:
						app.MainWindow.Pedals_Device_Label.Content = DataContext.Instance.Localization[ "PedalsP2000" ];
						break;
				}
			} );

			app.Logger.WriteLine( "[Pedals] <<< Initialize" );
		}
	}

	public static void SetComboBoxItemsSource( ComboBox comboBox )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[Pedals] SetComboBoxItemsSource >>>" );

			var selectedItem = comboBox.SelectedItem as KeyValuePair<Settings.PedalEffectEnum, string>?;

			var dictionary = new Dictionary<Settings.PedalEffectEnum, string>
			{
				{ Settings.PedalEffectEnum.None, DataContext.Instance.Localization[ "None" ] },
				{ Settings.PedalEffectEnum.GearChange, DataContext.Instance.Localization[ "GearChange" ] },
				{ Settings.PedalEffectEnum.ABSEngaged, DataContext.Instance.Localization[ "ABSEngaged" ] },
				{ Settings.PedalEffectEnum.WideRPM, DataContext.Instance.Localization[ "WideRPM" ] },
				{ Settings.PedalEffectEnum.NarrowRPM, DataContext.Instance.Localization[ "NarrowRPM" ] },
				{ Settings.PedalEffectEnum.SteeringEffects, DataContext.Instance.Localization[ "SteeringEffects" ] },
				{ Settings.PedalEffectEnum.WheelLock, DataContext.Instance.Localization[ "WheelLock" ] },
				{ Settings.PedalEffectEnum.WheelSpin, DataContext.Instance.Localization[ "WheelSpin" ] },
				{ Settings.PedalEffectEnum.ClutchSlip, DataContext.Instance.Localization[ "ClutchSlip" ] },
			};

			comboBox.ItemsSource = dictionary;

			if ( selectedItem.HasValue )
			{
				comboBox.SelectedItem = dictionary.FirstOrDefault( keyValuePair => keyValuePair.Key.Equals( selectedItem.Value.Key ) ); ;
			}

			app.Logger.WriteLine( "[Pedals] <<< SetComboBoxItemsSource" );
		}
	}

	public void Update( App app )
	{
		float[] frequency = [ 0f, 0f, 0f ];
		float[] amplitude = [ 0f, 0f, 0f ];

		// if not on track or we're not live then just turn off pedal vibrations

		if ( !app.Simulator.IsOnTrack || ( app.Simulator.SimMode != "full" ) )
		{
			_hpr.VibratePedal( HPR.Channel.Clutch, HPR.State.Off, 0, 0 );
			_hpr.VibratePedal( HPR.Channel.Brake, HPR.State.Off, 0, 0 );
			_hpr.VibratePedal( HPR.Channel.Throttle, HPR.State.Off, 0, 0 );

			return;
		}

		// initialize effects

		bool[] effectEngaged = [ false, false, false, false, false, false, false, false, false ];

		float[] effectFrequency = [ 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f ];
		float[] effectAmplitude = [ 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f ];

		#region gear change

		if ( !app.Simulator.WasOnTrack )
		{
			_gearLastFrame = app.Simulator.Gear;
		}

		if ( app.Simulator.Gear != _gearLastFrame )
		{
			if ( app.Simulator.Gear == 0 )
			{
				_gearChangeFrequency = _gearChangeFrequencyNeutral;
				_gearChangeTimer = _gearChangeTimeNeutral;
			}
			else
			{
				_gearChangeFrequency = _gearChangeFrequencyOther;
				_gearChangeTimer = _gearChangeTimeOther;
			}
		}

		_gearLastFrame = app.Simulator.Gear;

		if ( _gearChangeTimer > 0f )
		{
			_gearChangeTimer -= deltaSeconds;

			effectEngaged[ 1 ] = true;

			effectFrequency[ 1 ] = MathF.Min( DataContext.Instance.Settings.PedalsMaximumFrequency, MathF.Max( DataContext.Instance.Settings.PedalsMinimumFrequency, _gearChangeFrequency ) );
			effectAmplitude[ 1 ] = MathF.Min( DataContext.Instance.Settings.PedalsMaximumAmplitude, MathF.Max( DataContext.Instance.Settings.PedalsMinimumAmplitude, _gearChangeAmplitude ) );
		}

		#endregion

		#region abs engaged

		if ( app.Simulator.BrakeABSactive )
		{
			effectEngaged[ 2 ] = true;

			effectFrequency[ 2 ] = MathF.Min( DataContext.Instance.Settings.PedalsMaximumFrequency, MathF.Max( DataContext.Instance.Settings.PedalsMinimumFrequency, _absEngagedFrequency ) );
			effectAmplitude[ 2 ] = Misc.Lerp( DataContext.Instance.Settings.PedalsMinimumAmplitude, DataContext.Instance.Settings.PedalsMaximumAmplitude, MathF.Pow( app.Simulator.Brake, 1f + DataContext.Instance.Settings.PedalsAmplitudeCurve ) );
		}

		#endregion

		#region RPM (wide)

		var rpm = app.Simulator.RPM;

		var rpmRange = app.Simulator.ShiftLightsShiftRPM * 0.5f;
		var thresholdRPM = app.Simulator.ShiftLightsShiftRPM - rpmRange;

		if ( rpm > thresholdRPM )
		{
			rpm = Math.Clamp( ( rpm - thresholdRPM ) / rpmRange, 0f, 1f );

			effectEngaged[ 3 ] = true;

			effectFrequency[ 3 ] = Misc.Lerp( DataContext.Instance.Settings.PedalsMinimumFrequency, DataContext.Instance.Settings.PedalsMaximumFrequency, MathF.Pow( rpm, 1f + DataContext.Instance.Settings.PedalsFrequencyCurve ) );
			effectAmplitude[ 3 ] = Misc.Lerp( DataContext.Instance.Settings.PedalsMinimumAmplitude, DataContext.Instance.Settings.PedalsMaximumAmplitude, MathF.Pow( rpm * app.Simulator.Throttle, 1f + DataContext.Instance.Settings.PedalsAmplitudeCurve ) );
		}

		#endregion

		#region RPM (narrow)

		if ( ( app.Simulator.Gear >= 1 ) && ( app.Simulator.Gear < app.Simulator.NumForwardGears ) )
		{
			rpm = app.Simulator.RPM;

			rpmRange = app.Simulator.ShiftLightsShiftRPM * 0.05f;
			thresholdRPM = app.Simulator.ShiftLightsShiftRPM - rpmRange;

			if ( rpm > thresholdRPM )
			{
				rpm = Math.Clamp( ( rpm - thresholdRPM ) / rpmRange, 0f, 1f );

				effectEngaged[ 4 ] = true;

				effectFrequency[ 4 ] = Misc.Lerp( DataContext.Instance.Settings.PedalsMinimumFrequency, DataContext.Instance.Settings.PedalsMaximumFrequency, MathF.Pow( rpm, 1f + DataContext.Instance.Settings.PedalsFrequencyCurve ) );
				effectAmplitude[ 4 ] = Misc.Lerp( DataContext.Instance.Settings.PedalsMinimumAmplitude, DataContext.Instance.Settings.PedalsMaximumAmplitude, MathF.Pow( rpm * app.Simulator.Throttle, 1f + DataContext.Instance.Settings.PedalsAmplitudeCurve ) );
			}
		}

		#endregion

		#region Steering effects
		/*
				if ( Settings.SteeringEffectsEnabled && ( ( Settings.USEffectStyle == 4 ) || ( Settings.OSEffectStyle == 4 ) ) )
				{
					var effectAmount = 0f;

					if ( Settings.USEffectStyle == 4 )
					{
						var absUndersteerAmount = MathF.Abs( _ffb_understeerAmount );

						effectAmount = absUndersteerAmount * Settings.USEffectStrength / 100f;
						effectFrequency[ 5 ] = HPR_MAX_FREQUENCY;
					}

					if ( Settings.OSEffectStyle == 4 )
					{
						var absOversteerAmount = MathF.Abs( _ffb_oversteerAmount );

						if ( absOversteerAmount > effectAmount )
						{
							effectAmount = absOversteerAmount * Settings.OSEffectStrength / 100f;
							effectFrequency[ 5 ] = ( HPR_MAX_FREQUENCY - HPR_MIN_FREQUENCY ) / 2f + HPR_MIN_FREQUENCY;
						}
					}

					if ( effectAmount > 0f )
					{
						effectEngaged[ 5 ] = true;

						effectAmplitude[ 5 ] = ( HPR_MAX_AMPLITUDE - DataContext.Instance.Settings.PedalsMinimumAmplitude ) * effectAmount + DataContext.Instance.Settings.PedalsMinimumAmplitude;
					}
				}
		*/
		#endregion

		#region Wheel lock and wheel spin

		// update rpm vs speed ratios for wheel lock and spin effects
		/*
		if ( ( app.Simulator.Gear > 0 ) && ( app.Simulator.RPM > 100f ) && ( _irsdk_velocityX > 5f ) )
		{
			_hpr_currentRpmSpeedRatio = _irsdk_velocityX / app.Simulator.RPM;

			if ( ( _irsdk_brake == 0f ) && ( app.Simulator.Clutch == 1f ) )
			{
				if ( _hpr_averageRpmSpeedRatioPerGear[ app.Simulator.Gear ] == 0.0f )
				{
					_hpr_averageRpmSpeedRatioPerGear[ app.Simulator.Gear ] = _hpr_currentRpmSpeedRatio;
				}
				else
				{
					_hpr_averageRpmSpeedRatioPerGear[ app.Simulator.Gear ] = _hpr_averageRpmSpeedRatioPerGear[ app.Simulator.Gear ] * 0.95f + _hpr_currentRpmSpeedRatio * 0.05f;
				}
			}

			// wheel lock (6)

			if ( _hpr_averageRpmSpeedRatioPerGear[ app.Simulator.Gear ] != 0f )
			{
				if ( app.Simulator.Clutch == 1f )
				{
					if ( _hpr_currentRpmSpeedRatio > _hpr_averageRpmSpeedRatioPerGear[ app.Simulator.Gear ] * 1.05f )
					{
						effectEngaged[ 6 ] = true;

						effectFrequency[ 6 ] = HPR_MAX_FREQUENCY;
						effectAmplitude[ 6 ] = HPR_MAX_AMPLITUDE;
					}
				}
			}

			// wheel spin (7)

			if ( _hpr_averageRpmSpeedRatioPerGear[ app.Simulator.Gear ] != 0f )
			{
				if ( app.Simulator.Clutch == 1f )
				{
					if ( _hpr_currentRpmSpeedRatio < _hpr_averageRpmSpeedRatioPerGear[ app.Simulator.Gear ] * 0.95f )
					{
						effectEngaged[ 7 ] = true;

						effectFrequency[ 7 ] = HPR_MAX_FREQUENCY;
						effectAmplitude[ 7 ] = HPR_MAX_AMPLITUDE;
					}
				}
			}
		}
		else
		{
			_hpr_currentRpmSpeedRatio = 0f;
		}
		*/
		#endregion

		#region Clutch slip

		if ( ( app.Simulator.Clutch > 0.25f ) && ( app.Simulator.Clutch < 0.75f ) )
		{
			rpm = app.Simulator.RPM;

			rpmRange = app.Simulator.ShiftLightsShiftRPM * 0.5f;
			thresholdRPM = app.Simulator.ShiftLightsShiftRPM - rpmRange;

			if ( rpm > thresholdRPM )
			{
				rpm = Math.Clamp( ( rpm - thresholdRPM ) / rpmRange, 0f, 1f );

				effectEngaged[ 8 ] = true;

				effectFrequency[ 8 ] = Misc.Lerp( DataContext.Instance.Settings.PedalsMinimumFrequency, DataContext.Instance.Settings.PedalsMaximumFrequency, MathF.Pow( rpm, 1f + DataContext.Instance.Settings.PedalsFrequencyCurve ) );
				effectAmplitude[ 8 ] = MathF.Min( DataContext.Instance.Settings.PedalsMaximumAmplitude, MathF.Max( DataContext.Instance.Settings.PedalsMinimumAmplitude, _clutchSlipAmplitude ) );
			}
		}

		#endregion

		#region Apply effects

		for ( var i = 0; i < 3; i++ )
		{
			var effect1 = ( i == 0 ) ? (int) DataContext.Instance.Settings.PedalsClutchEffect1 : ( i == 1 ) ? (int) DataContext.Instance.Settings.PedalsBrakeEffect1 : (int) DataContext.Instance.Settings.PedalsThrottleEffect1;
			var effect2 = ( i == 0 ) ? (int) DataContext.Instance.Settings.PedalsClutchEffect2 : ( i == 1 ) ? (int) DataContext.Instance.Settings.PedalsBrakeEffect2 : (int) DataContext.Instance.Settings.PedalsThrottleEffect2;
			var effect3 = ( i == 0 ) ? (int) DataContext.Instance.Settings.PedalsClutchEffect3 : ( i == 1 ) ? (int) DataContext.Instance.Settings.PedalsBrakeEffect3 : (int) DataContext.Instance.Settings.PedalsThrottleEffect3;

			var scale1 = ( i == 0 ) ? DataContext.Instance.Settings.PedalsClutchEffect1Strength : ( i == 1 ) ? DataContext.Instance.Settings.PedalsBrakeEffect1Strength : DataContext.Instance.Settings.PedalsThrottleEffect1Strength;
			var scale2 = ( i == 0 ) ? DataContext.Instance.Settings.PedalsClutchEffect2Strength : ( i == 1 ) ? DataContext.Instance.Settings.PedalsBrakeEffect2Strength : DataContext.Instance.Settings.PedalsThrottleEffect2Strength;
			var scale3 = ( i == 0 ) ? DataContext.Instance.Settings.PedalsClutchEffect3Strength : ( i == 1 ) ? DataContext.Instance.Settings.PedalsBrakeEffect3Strength : DataContext.Instance.Settings.PedalsThrottleEffect3Strength;

			if ( effectEngaged[ effect1 ] )
			{
				frequency[ i ] = effectFrequency[ effect1 ];
				amplitude[ i ] = ( effectAmplitude[ effect1 ] - DataContext.Instance.Settings.PedalsMinimumAmplitude ) * scale1 + DataContext.Instance.Settings.PedalsMinimumAmplitude;
			}
			else if ( effectEngaged[ effect2 ] )
			{
				frequency[ i ] = effectFrequency[ effect2 ];
				amplitude[ i ] = ( effectAmplitude[ effect2 ] - DataContext.Instance.Settings.PedalsMinimumAmplitude ) * scale2 + DataContext.Instance.Settings.PedalsMinimumAmplitude;
			}
			else if ( effectEngaged[ effect3 ] )
			{
				frequency[ i ] = effectFrequency[ effect3 ];
				amplitude[ i ] = ( effectAmplitude[ effect3 ] - DataContext.Instance.Settings.PedalsMinimumAmplitude ) * scale3 + DataContext.Instance.Settings.PedalsMinimumAmplitude;
			}

			if ( ( frequency[ i ] == 0f ) || ( amplitude[ i ] == 0f ) )
			{
				_hpr.VibratePedal( (HPR.Channel) i, HPR.State.Off, 0f, 0f );
			}
			else
			{
				_hpr.VibratePedal( (HPR.Channel) i, HPR.State.On, frequency[ i ], amplitude[ i ] * 100f );
			}
		}

		#endregion
	}

	public void Tick( App app )
	{
	}
}
