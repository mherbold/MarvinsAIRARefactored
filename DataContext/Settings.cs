
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using System.Xml.Serialization;
using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Windows;

using static MarvinsAIRARefactored.Windows.MainWindow;

namespace MarvinsAIRARefactored.DataContext;

public class Settings : INotifyPropertyChanged
{
	public static bool SuppressUpdatingOfContextSettings { private get; set; } = false;

	private bool _updatingRacingWheelRelatedSettings = false;
	private bool _updatingPedalsRelatedSettings = false;
	private bool _updatingRacingWheelMultiSettings = false;

	#region INotifyProperty stuff

	public event PropertyChangedEventHandler? PropertyChanged;

	public void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
	{
		var app = App.Instance!;

		if ( ( propertyName != null ) && !propertyName.EndsWith( "String" ) )
		{
			var property = GetType().GetProperty( propertyName );

			if ( property != null )
			{
				var value = property.GetValue( this );

				var valueType = value?.GetType().Name ?? "null";

				if ( propertyName != "AppWindowPositionAndSize" )
				{
					app.Logger.WriteLine( $"[Settings] Updating base setting {propertyName} to ({valueType}) {value}" );
				}

				if ( !SuppressUpdatingOfContextSettings )
				{
					UpdateSettings( true );
				}
			}
		}

		app.SettingsFile.QueueForSerialization = true;

		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );

		app.Telemetry.UpdateSettings();
	}

	#endregion

	#region Context settings

	public SerializableDictionary<Context, ContextSettings> ContextSettingsDictionary { get; set; } = [];

	private ContextSettings FindContextSettings( Context context )
	{
		if ( !ContextSettingsDictionary.TryGetValue( context, out var contextSettings ) )
		{
			contextSettings = new ContextSettings();

			var contextSettingsProperties = typeof( ContextSettings ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

			foreach ( var contextSettingsProperty in contextSettingsProperties )
			{
				if ( contextSettingsProperty.CanRead && contextSettingsProperty.CanWrite )
				{
					var settingsProperty = typeof( Settings ).GetProperty( contextSettingsProperty.Name );

					if ( settingsProperty != null )
					{
						var settingsPropertyValue = settingsProperty.GetValue( this );

						contextSettingsProperty.SetValue( contextSettings, settingsPropertyValue );
					}
				}
			}

			ContextSettingsDictionary.Add( context, contextSettings );
		}

		return contextSettings;
	}

	public void UpdateSettings( bool updateContextSettings )
	{
		var app = App.Instance!;

		SuppressUpdatingOfContextSettings = !updateContextSettings;

		var settingsProperties = typeof( Settings ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

		foreach ( var settingsProperty in settingsProperties )
		{
			if ( settingsProperty.CanRead && settingsProperty.CanWrite && !settingsProperty.Name.EndsWith( "String" ) )
			{
				var contextSwitchesPropertyName = $"{settingsProperty.Name}ContextSwitches";

				var contextSwitchesProperty = GetType().GetProperty( contextSwitchesPropertyName );

				if ( contextSwitchesProperty != null )
				{
					var contextSwitches = (ContextSwitches?) contextSwitchesProperty.GetValue( this );

					if ( contextSwitches != null )
					{
						var context = new Context( contextSwitches );

						var contextSettings = FindContextSettings( context );

						var contextSettingsProperty = typeof( ContextSettings ).GetProperty( settingsProperty.Name );

						if ( contextSettingsProperty != null )
						{
							var contextSettingsPropertyValue = contextSettingsProperty.GetValue( contextSettings );
							var settingsPropertyValue = settingsProperty.GetValue( this );

							if ( !Equals( contextSettingsPropertyValue, settingsPropertyValue ) )
							{
								if ( updateContextSettings )
								{
									var valueType = settingsPropertyValue?.GetType().Name ?? "null";

									app.Logger.WriteLine( $"[Settings] Updating context setting {contextSettingsProperty.Name} to ({valueType}) {settingsPropertyValue} from setting ({context.WheelbaseGuid}|{context.CarName}|{context.TrackName}|{context.TrackConfigurationName}|{context.WetDryName})" );

									contextSettingsProperty.SetValue( contextSettings, settingsPropertyValue );
								}
								else
								{
									var valueType = contextSettingsPropertyValue?.GetType().Name ?? "null";

									app.Logger.WriteLine( $"[Settings] Updating setting {settingsProperty.Name} to ({valueType}) {contextSettingsPropertyValue} from context setting ({context.WheelbaseGuid}|{context.CarName}|{context.TrackName}|{context.TrackConfigurationName}|{context.WetDryName})" );

									settingsProperty.SetValue( this, contextSettingsPropertyValue );
								}
							}
						}
					}
				}
			}
		}

		SuppressUpdatingOfContextSettings = false;
	}

	#endregion

	#region Related settings

	private void UpdateRelatedRacingWheelSettings( [CallerMemberName] string? propertyName = null )
	{
		if ( !_updatingRacingWheelRelatedSettings )
		{
			_updatingRacingWheelRelatedSettings = true;

			if ( propertyName == "RacingWheelWheelForce" )
			{
				RacingWheelMaxForce = RacingWheelWheelForce / RacingWheelStrength;
			}
			else if ( propertyName == "RacingWheelStrength" )
			{
				RacingWheelMaxForce = RacingWheelWheelForce / RacingWheelStrength;
			}
			else if ( propertyName == "RacingWheelMaxForce" )
			{
				RacingWheelStrength = RacingWheelWheelForce / RacingWheelMaxForce;
			}

			UpdateRacingWheelWheelForceString();
			UpdateRacingWheelStrengthString();
			UpdateRacingWheelMaxForceString();
			UpdateRacingWheelAutoMarginString();
			UpdateRacingWheelSlewCompressionThresholdString();
			UpdateRacingWheelTotalCompressionThresholdString();
			UpdateRacingWheelOutputMinimumString();
			UpdateRacingWheelOutputMaximumString();
			UpdateRacingWheelGearChangeVibrateStrengthString();
			UpdateRacingWheelABSVibrateStrengthString();

			UpdateSteeringEffectsUndersteerWheelVibrationStrengthString();
			UpdateSteeringEffectsUndersteerWheelConstantForceStrengthString();
			UpdateSteeringEffectsOversteerWheelVibrationStrengthString();
			UpdateSteeringEffectsOversteerWheelConstantForceStrengthString();
			UpdateSteeringEffectsSeatOfPantsWheelVibrationStrengthString();
			UpdateSteeringEffectsSeatOfPantsWheelConstantForceStrengthString();

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			_updatingRacingWheelRelatedSettings = false;
		}
	}

	private void UpdateRelatedPedalSettings( [CallerMemberName] string? propertyName = null )
	{
		if ( !_updatingPedalsRelatedSettings )
		{
			_updatingPedalsRelatedSettings = true;

			UpdateSteeringEffectsUndersteerPedalVibrationMinimumFrequencyString();
			UpdateSteeringEffectsUndersteerPedalVibrationMaximumFrequencyString();
			UpdateSteeringEffectsOversteerPedalVibrationMinimumFrequencyString();
			UpdateSteeringEffectsOversteerPedalVibrationMaximumFrequencyString();
			UpdateSteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString();
			UpdateSteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString();

			UpdatePedalsShiftIntoGearFrequencyString();
			UpdatePedalsShiftIntoNeutralFrequencyString();
			UpdatePedalsABSEngagedFrequencyString();
			UpdatePedalsWheelLockFrequencyString();
			UpdatePedalsWheelSpinFrequencyString();
			UpdatePedalsClutchSlipFrequencyString();

			_updatingPedalsRelatedSettings = false;
		}
	}

	#endregion

	#region Racing wheel - Device

	private Guid _racingWheelSteeringDeviceGuid = Guid.Empty;

	public Guid RacingWheelSteeringDeviceGuid
	{
		get => _racingWheelSteeringDeviceGuid;

		set
		{
			if ( value != _racingWheelSteeringDeviceGuid )
			{
				_racingWheelSteeringDeviceGuid = value;

				OnPropertyChanged();

				var app = App.Instance!;

				app.RacingWheel.NextRacingWheelGuid = _racingWheelSteeringDeviceGuid;
			}
		}
	}

	#endregion

	#region Racing wheel - Enable force feedback

	private bool _racingWheelEnableForceFeedback = true;

	public bool RacingWheelEnableForceFeedback
	{
		get => _racingWheelEnableForceFeedback;

		set
		{
			if ( value != _racingWheelEnableForceFeedback )
			{
				_racingWheelEnableForceFeedback = value;

				OnPropertyChanged();
			}

			_racingWheelPage.UpdateSteeringDeviceSection();
		}
	}

	public ContextSwitches RacingWheelEnableForceFeedbackContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelEnableForceFeedbackButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Test

	public ButtonMappings RacingWheelTestButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Reset

	public ButtonMappings RacingWheelResetButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Wheel force

	private float _racingWheelWheelForce = 5f;

	public float RacingWheelWheelForce
	{
		get => _racingWheelWheelForce;

		set
		{
			value = float.IsNaN( value ) ? 5f : value;

			value = Math.Clamp( value, 2f, 50f );

			if ( value != _racingWheelWheelForce )
			{
				_racingWheelWheelForce = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings();
		}
	}

	private string _racingWheelWheelForceString = string.Empty;

	[XmlIgnore]
	public string RacingWheelWheelForceString
	{
		get => _racingWheelWheelForceString;

		set
		{
			if ( value != _racingWheelWheelForceString )
			{
				_racingWheelWheelForceString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelWheelForceString()
	{
		RacingWheelWheelForceString = $"{_racingWheelWheelForce:F1} {DataContext.Instance.Localization[ "TorqueUnits" ]}";
	}

	public ContextSwitches RacingWheelWheelForceContextSwitches { get; set; } = new( true, false, false, false, false );

	#endregion

	#region Racing wheel - Strength

	private float _racingWheelStrength = 0.1f;

	public float RacingWheelStrength
	{
		get => _racingWheelStrength;

		set
		{
			value = float.IsNaN( value ) ? 0.1f : value;

			value = Math.Clamp( value, 0f, RacingWheelAllowSuperStrength ? 2f : 1f );

			if ( value != _racingWheelStrength )
			{
				_racingWheelStrength = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings();
		}
	}

	private string _racingWheelStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelStrengthString
	{
		get => _racingWheelStrengthString;

		set
		{
			if ( value != _racingWheelStrengthString )
			{
				_racingWheelStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelStrengthString()
	{
		RacingWheelStrengthString = $"{_racingWheelStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelStrengthContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Max force

	private float _racingWheelMaxForce = 50f;

	public float RacingWheelMaxForce
	{
		get => _racingWheelMaxForce;

		set
		{
			value = float.IsNaN( value ) ? 50f : value;

			value = Math.Clamp( value, RacingWheelWheelForce * ( RacingWheelAllowSuperStrength ? 0.5f : 1f ), 300.0f );

			if ( value != _racingWheelMaxForce )
			{
				_racingWheelMaxForce = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings();
		}
	}

	private string _racingWheelMaxForceString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMaxForceString
	{
		get => _racingWheelMaxForceString;

		set
		{
			if ( value != _racingWheelMaxForceString )
			{
				_racingWheelMaxForceString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelMaxForceString()
	{
		RacingWheelMaxForceString = $"{_racingWheelMaxForce:F1} {DataContext.Instance.Localization[ "TorqueUnits" ]}";
	}

	public ButtonMappings RacingWheelMaxForcePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMaxForceMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Auto margin

	private float _racingWheelAutoMargin = 0f;

	public float RacingWheelAutoMargin
	{
		get => _racingWheelAutoMargin;

		set
		{
			value = Math.Clamp( value, -0.5f, 1.5f );

			if ( value != _racingWheelAutoMargin )
			{
				_racingWheelAutoMargin = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelAutoMarginString();
		}
	}

	private string _racingWheelAutoMarginString = string.Empty;

	[XmlIgnore]
	public string RacingWheelAutoMarginString
	{
		get => _racingWheelAutoMarginString;

		set
		{
			if ( value != _racingWheelAutoMarginString )
			{
				_racingWheelAutoMarginString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelAutoMarginString()
	{
		var convertedToTarget = 1 / ( 1 + _racingWheelAutoMargin );
		var convertedToTorque = RacingWheelWheelForce * convertedToTarget;

		RacingWheelAutoMarginString = $"{convertedToTarget * 100f:F1}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
	}

	public ContextSwitches RacingWheelAutoMarginContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelAutoMarginPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelAutoMarginMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Set

	public ButtonMappings RacingWheelSetButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Clear

	public ButtonMappings RacingWheelClearButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Algorithm

	private RacingWheel.Algorithm _racingWheelAlgorithm = RacingWheel.Algorithm.DetailBoosterOn60Hz;

	public RacingWheel.Algorithm RacingWheelAlgorithm
	{
		get => _racingWheelAlgorithm;

		set
		{
			if ( value != _racingWheelAlgorithm )
			{
				_racingWheelAlgorithm = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.MainWindow.UpdateRacingWheelAlgorithmControls();

			app.RacingWheel.UpdateAlgorithmPreview = true;
		}
	}

	public ContextSwitches RacingWheelAlgorithmContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Enable soft limiter

	private bool _racingWheelEnableSoftLimiter = true;

	public bool RacingWheelEnableSoftLimiter
	{
		get => _racingWheelEnableSoftLimiter;

		set
		{
			if ( value != _racingWheelEnableSoftLimiter )
			{
				_racingWheelEnableSoftLimiter = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;
		}
	}

	public ContextSwitches RacingWheelEnableSoftLimiterContextSwitches { get; set; } = new( true, true, false, false, false );

	#endregion

	#region Racing wheel - Prediction mode

	private RacingWheel.PredictionMode _racingWheelPredictionMode = RacingWheel.PredictionMode.PredictK1;

	public RacingWheel.PredictionMode RacingWheelPredictionMode
	{
		get => _racingWheelPredictionMode;

		set
		{
			if ( value != _racingWheelPredictionMode )
			{
				_racingWheelPredictionMode = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelPredictionModeContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Prediction blend

	private float _racingWheelPredictionBlend = 0.35f;

	public float RacingWheelPredictionBlend
	{
		get => _racingWheelPredictionBlend;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _racingWheelPredictionBlend )
			{
				_racingWheelPredictionBlend = value;

				OnPropertyChanged();
			}

			RacingWheelPredictionBlendString = $"{_racingWheelPredictionBlend * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelPredictionBlendString = string.Empty;

	[XmlIgnore]
	public string RacingWheelPredictionBlendString
	{
		get => _racingWheelPredictionBlendString;

		set
		{
			if ( value != _racingWheelPredictionBlendString )
			{
				_racingWheelPredictionBlendString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelPredictionBlendContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelPredictionBlendPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelPredictionBlendMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Detail boost

	private float _racingWheelDetailBoost = 0.5f;

	public float RacingWheelDetailBoost
	{
		get => _racingWheelDetailBoost;

		set
		{
			value = Math.Clamp( value, 0f, 9.99f );

			if ( value != _racingWheelDetailBoost )
			{
				_racingWheelDetailBoost = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelDetailBoostString = $"{_racingWheelDetailBoost * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelDetailBoostString = string.Empty;

	[XmlIgnore]
	public string RacingWheelDetailBoostString
	{
		get => _racingWheelDetailBoostString;

		set
		{
			if ( value != _racingWheelDetailBoostString )
			{
				_racingWheelDetailBoostString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelDetailBoostContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelDetailBoostPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelDetailBoostMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Detail boost bias

	private float _racingWheelDetailBoostBias = 0.1f;

	public float RacingWheelDetailBoostBias
	{
		get => _racingWheelDetailBoostBias;

		set
		{
			value = Math.Clamp( value, 0.05f, 1f );

			if ( value != _racingWheelDetailBoostBias )
			{
				_racingWheelDetailBoostBias = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelDetailBoostBiasString = $"{_racingWheelDetailBoostBias * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelDetailBoostBiasString = string.Empty;

	[XmlIgnore]
	public string RacingWheelDetailBoostBiasString
	{
		get => _racingWheelDetailBoostBiasString;

		set
		{
			if ( value != _racingWheelDetailBoostBiasString )
			{
				_racingWheelDetailBoostBiasString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelDetailBoostBiasContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelDetailBoostBiasPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelDetailBoostBiasMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Delta limit

	private float _racingWheelDeltaLimit = 500f;

	public float RacingWheelDeltaLimit
	{
		get => _racingWheelDeltaLimit;

		set
		{
			value = Math.Clamp( value, 0f, 3000f );

			if ( value != _racingWheelDeltaLimit )
			{
				_racingWheelDeltaLimit = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelDeltaLimitString = $"{_racingWheelDeltaLimit:F0} {DataContext.Instance.Localization[ "DeltaLimitUnits" ]}";
		}
	}

	private string _racingWheelDeltaLimitString = string.Empty;

	[XmlIgnore]
	public string RacingWheelDeltaLimitString
	{
		get => _racingWheelDeltaLimitString;

		set
		{
			if ( value != _racingWheelDeltaLimitString )
			{
				_racingWheelDeltaLimitString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelDeltaLimitContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelDeltaLimitPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelDeltaLimitMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Delta limiter bias

	private float _racingWheelDeltaLimiterBias = 0.2f;

	public float RacingWheelDeltaLimiterBias
	{
		get => _racingWheelDeltaLimiterBias;

		set
		{
			value = Math.Clamp( value, 0.05f, 1f );

			if ( value != _racingWheelDeltaLimiterBias )
			{
				_racingWheelDeltaLimiterBias = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelDeltaLimiterBiasString = $"{_racingWheelDeltaLimiterBias * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelDeltaLimiterBiasString = string.Empty;

	[XmlIgnore]
	public string RacingWheelDeltaLimiterBiasString
	{
		get => _racingWheelDeltaLimiterBiasString;

		set
		{
			if ( value != _racingWheelDeltaLimiterBiasString )
			{
				_racingWheelDeltaLimiterBiasString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelDeltaLimiterBiasContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelDeltaLimiterBiasPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelDeltaLimiterBiasMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Slew compression threshold

	private float _racingWheelSlewCompressionThreshold = 2f;

	public float RacingWheelSlewCompressionThreshold
	{
		get => _racingWheelSlewCompressionThreshold;

		set
		{
			value = Math.Clamp( value, 0f, 350f );

			if ( value != _racingWheelSlewCompressionThreshold )
			{
				_racingWheelSlewCompressionThreshold = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings();
		}
	}

	private string _racingWheelSlewCompressionThresholdString = string.Empty;

	[XmlIgnore]
	public string RacingWheelSlewCompressionThresholdString
	{
		get => _racingWheelSlewCompressionThresholdString;

		set
		{
			if ( value != _racingWheelSlewCompressionThresholdString )
			{
				_racingWheelSlewCompressionThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelSlewCompressionThresholdString()
	{
		RacingWheelSlewCompressionThresholdString = $"{_racingWheelSlewCompressionThreshold * DataContext.Instance.Settings.RacingWheelMaxForce / 1000f:F2} {DataContext.Instance.Localization[ "SlewUnits" ]}";
	}

	public ContextSwitches RacingWheelSlewCompressionThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelSlewCompressionThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelSlewCompressionThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Slew compression rate

	private float _racingWheelSlewCompressionRate = 0.65f;

	public float RacingWheelSlewCompressionRate
	{
		get => _racingWheelSlewCompressionRate;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelSlewCompressionRate )
			{
				_racingWheelSlewCompressionRate = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelSlewCompressionRateString = $"{_racingWheelSlewCompressionRate * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelSlewCompressionRateString = string.Empty;

	[XmlIgnore]
	public string RacingWheelSlewCompressionRateString
	{
		get => _racingWheelSlewCompressionRateString;

		set
		{
			if ( value != _racingWheelSlewCompressionRateString )
			{
				_racingWheelSlewCompressionRateString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelSlewCompressionRateContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelSlewCompressionRatePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelSlewCompressionRateMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Total compression threshold

	private float _racingWheelTotalCompressionThreshold = 0.65f;

	public float RacingWheelTotalCompressionThreshold
	{
		get => _racingWheelTotalCompressionThreshold;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelTotalCompressionThreshold )
			{
				_racingWheelTotalCompressionThreshold = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings();
		}
	}

	private string _racingWheelTotalCompressionThresholdString = string.Empty;

	[XmlIgnore]
	public string RacingWheelTotalCompressionThresholdString
	{
		get => _racingWheelTotalCompressionThresholdString;

		set
		{
			if ( value != _racingWheelTotalCompressionThresholdString )
			{
				_racingWheelTotalCompressionThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelTotalCompressionThresholdString()
	{
		RacingWheelTotalCompressionThresholdString = $"{_racingWheelTotalCompressionThreshold * DataContext.Instance.Settings.RacingWheelMaxForce:F1} {DataContext.Instance.Localization[ "TorqueUnits" ]}";
	}

	public ContextSwitches RacingWheelTotalCompressionThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelTotalCompressionThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelTotalCompressionThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Total compression rate

	private float _racingWheelTotalCompressionRate = 0.75f;

	public float RacingWheelTotalCompressionRate
	{
		get => _racingWheelTotalCompressionRate;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelTotalCompressionRate )
			{
				_racingWheelTotalCompressionRate = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelTotalCompressionRateString = $"{_racingWheelTotalCompressionRate * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelTotalCompressionRateString = string.Empty;

	[XmlIgnore]
	public string RacingWheelTotalCompressionRateString
	{
		get => _racingWheelTotalCompressionRateString;

		set
		{
			if ( value != _racingWheelTotalCompressionRateString )
			{
				_racingWheelTotalCompressionRateString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelTotalCompressionRateContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelTotalCompressionRatePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelTotalCompressionRateMinusButtonMappings { get; set; } = new();

    #endregion

    #region Racing wheel - Multi FFB source menu selection

    private RacingWheel.MultiFFBSourceOptions _racingWheelMultiFFBSourceSelection = RacingWheel.MultiFFBSourceOptions.Native360Hz;

    public RacingWheel.MultiFFBSourceOptions RacingWheelMultiFFBSourceSelection
    {
        get => _racingWheelMultiFFBSourceSelection;

        set
        {
            if (value != _racingWheelMultiFFBSourceSelection)
            {
                var oldValue = _racingWheelMultiFFBSourceSelection;

                _racingWheelMultiFFBSourceSelection = value;

                OnPropertyChanged();

                var app = App.Instance!;

                if (!_updatingRacingWheelMultiSettings)
                {
                    app.RacingWheel.SetCannedMultiAdjustAlgorithmValues();

                    _updatingRacingWheelMultiSettings = true;

                    switch (RacingWheelMultiFFBSourceSelection)
                    {
						case RacingWheel.MultiFFBSourceOptions.Native60Hz:
						case RacingWheel.MultiFFBSourceOptions.DefaultsNative60Hz:
                            RacingWheelMultiFFBSourceSelection = RacingWheel.MultiFFBSourceOptions.Native60Hz;
                            break;

						case RacingWheel.MultiFFBSourceOptions.Native360Hz:
                        case RacingWheel.MultiFFBSourceOptions.DefaultsNative360Hz:
                            RacingWheelMultiFFBSourceSelection = RacingWheel.MultiFFBSourceOptions.Native360Hz;
                            break;

						case RacingWheel.MultiFFBSourceOptions.Hybrid10:
                        case RacingWheel.MultiFFBSourceOptions.DefaultsHybrid10:
                            RacingWheelMultiFFBSourceSelection = RacingWheel.MultiFFBSourceOptions.Hybrid10;
                            break;

						case RacingWheel.MultiFFBSourceOptions.HybridVariable30:
                        case RacingWheel.MultiFFBSourceOptions.DefaultsHybridVariable30:
                        case RacingWheel.MultiFFBSourceOptions.PresetBasicFFB:
                        case RacingWheel.MultiFFBSourceOptions.PresetBalancedFFB:
                            RacingWheelMultiFFBSourceSelection = RacingWheel.MultiFFBSourceOptions.HybridVariable30;
                            break;

						default:
                            RacingWheelMultiFFBSourceSelection = oldValue;
                            break;
                    }

                    _updatingRacingWheelMultiSettings = false;

                }
                else
                {
					// Force the combo box's displayed selection to update when the user selected a preset

                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() =>
                    {
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RacingWheelMultiFFBSourceSelection)));
                    }));
                }
                app.RacingWheel.UpdateAlgorithmPreview = true;
            }
        }
    }

    public ContextSwitches RacingWheelMultiFFBSourceSelectionContextSwitches { get; set; } = new(false, false, false, false, false);

    #endregion

    #region Racing wheel - Multi 360Hz detail

    private float _racingWheelMulti360HzDetail = 1f;

	public float RacingWheelMulti360HzDetail
	{
		get => _racingWheelMulti360HzDetail;

		set
		{
			var app = App.Instance!;

			if ( RacingWheelMultiFFBSourceSelection == RacingWheel.MultiFFBSourceOptions.Native60Hz )
			{
				value = 0f;
			}
			else if ( RacingWheelMultiFFBSourceSelection == RacingWheel.MultiFFBSourceOptions.Native360Hz )
			{
				value = 1f;
			}
			else
			{
				value = Math.Clamp( value, 0f, 3f );
			}

			if ( value != _racingWheelMulti360HzDetail )
			{
				_racingWheelMulti360HzDetail = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelMulti360HzDetailString = $"{_racingWheelMulti360HzDetail * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelMulti360HzDetailString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMulti360HzDetailString
	{
		get => _racingWheelMulti360HzDetailString;

		set
		{
			if ( value != _racingWheelMulti360HzDetailString )
			{
				_racingWheelMulti360HzDetailString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelMulti360HzDetailContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelMulti360HzDetailPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMulti360HzDetailMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Multi torque compression

	private float _racingWheelMultiTorqueCompression = 0f;

	public float RacingWheelMultiTorqueCompression
	{
		get => _racingWheelMultiTorqueCompression;

		set
		{
			var app = App.Instance!;

			value = MathZ.Saturate( value );

			if ( value != _racingWheelMultiTorqueCompression )
			{
				_racingWheelMultiTorqueCompression = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelMultiTorqueCompressionString = $"{_racingWheelMultiTorqueCompression * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelMultiTorqueCompressionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMultiTorqueCompressionString
	{
		get => _racingWheelMultiTorqueCompressionString;

		set
		{
			if ( value != _racingWheelMultiTorqueCompressionString )
			{
				_racingWheelMultiTorqueCompressionString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelMultiTorqueCompressionContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelMultiTorqueCompressionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMultiTorqueCompressionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Multi enable slew peak mode

	private bool _racingWheelMultiEnableSlewPeakMode = false;

	public bool RacingWheelMultiEnableSlewPeakMode
	{
		get => _racingWheelMultiEnableSlewPeakMode;

		set
		{
			var app = App.Instance!;

			if ( value != _racingWheelMultiEnableSlewPeakMode )
			{
				_racingWheelMultiEnableSlewPeakMode = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;
		}
	}

	public ContextSwitches RacingWheelMultiEnableSlewPeakModeContextSwitches { get; set; } = new( true, true, false, false, false );

	#endregion

	#region Racing wheel - Multi slew rate reduction

	private float _racingWheelMultiSlewRateReduction = 0f;

	public float RacingWheelMultiSlewRateReduction
	{
		get => _racingWheelMultiSlewRateReduction;

		set
		{
			var app = App.Instance!;

			value = MathZ.Saturate( value );

			if ( value != _racingWheelMultiSlewRateReduction )
			{
				_racingWheelMultiSlewRateReduction = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelMultiSlewRateReductionString = $"{_racingWheelMultiSlewRateReduction * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelMultiSlewRateReductionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMultiSlewRateReductionString
	{
		get => _racingWheelMultiSlewRateReductionString;

		set
		{
			if ( value != _racingWheelMultiSlewRateReductionString )
			{
				_racingWheelMultiSlewRateReductionString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelMultiSlewRateReductionContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelMultiSlewRateReductionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMultiSlewRateReductionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Multi detail gain

	private float _racingWheelMultiDetailGain = 0f;

	public float RacingWheelMultiDetailGain
	{
		get => _racingWheelMultiDetailGain;

		set
		{
			var app = App.Instance!;

			value = Math.Clamp( value, -1f, 3f );

			if ( value != _racingWheelMultiDetailGain )
			{
				_racingWheelMultiDetailGain = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelMultiDetailGainString = $"{_racingWheelMultiDetailGain * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelMultiDetailGainString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMultiDetailGainString
	{
		get => _racingWheelMultiDetailGainString;

		set
		{
			if ( value != _racingWheelMultiDetailGainString )
			{
				_racingWheelMultiDetailGainString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelMultiDetailGainContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelMultiDetailGainPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMultiDetailGainMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Multi output smoothing

	private float _racingWheelMultiOutputSmoothing = 0f;

	public float RacingWheelMultiOutputSmoothing
	{
		get => _racingWheelMultiOutputSmoothing;

		set
		{
			var app = App.Instance!;

			value = MathZ.Saturate( value );

			if ( value != _racingWheelMultiOutputSmoothing )
			{
				_racingWheelMultiOutputSmoothing = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelMultiOutputSmoothingString = $"{_racingWheelMultiOutputSmoothing * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelMultiOutputSmoothingString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMultiOutputSmoothingString
	{
		get => _racingWheelMultiOutputSmoothingString;

		set
		{
			if ( value != _racingWheelMultiOutputSmoothingString )
			{
				_racingWheelMultiOutputSmoothingString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelMultiOutputSmoothingContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelMultiOutputSmoothingPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMultiOutputSmoothingMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Output minimum

	private float _racingWheelOutputMinimum = 0f;

	public float RacingWheelOutputMinimum
	{
		get => _racingWheelOutputMinimum;

		set
		{
			value = Math.Clamp( value, 0f, 0.1f );

			if ( value != _racingWheelOutputMinimum )
			{
				_racingWheelOutputMinimum = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelOutputMinimumString();
		}
	}

	private string _racingWheelOutputMinimumString = string.Empty;

	[XmlIgnore]
	public string RacingWheelOutputMinimumString
	{
		get => _racingWheelOutputMinimumString;

		set
		{
			if ( value != _racingWheelOutputMinimumString )
			{
				_racingWheelOutputMinimumString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelOutputMinimumString()
	{
		if ( _racingWheelOutputMinimum == 0f )
		{
			RacingWheelOutputMinimumString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _racingWheelOutputMinimum;

			RacingWheelOutputMinimumString = $"{_racingWheelOutputMinimum * 100f:F1}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches RacingWheelOutputMinimumContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelOutputMinimumPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelOutputMinimumMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Output maximum

	private float _racingWheelOutputMaximum = 1f;

	public float RacingWheelOutputMaximum
	{
		get => _racingWheelOutputMaximum;

		set
		{
			value = Math.Clamp( value, 0.2f, 1f );

			if ( value != _racingWheelOutputMaximum )
			{
				_racingWheelOutputMaximum = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelOutputMaximumString();
		}
	}

	private string _racingWheelOutputMaximumString = string.Empty;

	[XmlIgnore]
	public string RacingWheelOutputMaximumString
	{
		get => _racingWheelOutputMaximumString;

		set
		{
			if ( value != _racingWheelOutputMaximumString )
			{
				_racingWheelOutputMaximumString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelOutputMaximumString()
	{
		if ( _racingWheelOutputMaximum == 1f )
		{
			RacingWheelOutputMaximumString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _racingWheelOutputMaximum;

			RacingWheelOutputMaximumString = $"{_racingWheelOutputMaximum * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches RacingWheelOutputMaximumContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelOutputMaximumPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelOutputMaximumMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Output curve

	private float _racingWheelOutputCurve = 0f;

	public float RacingWheelOutputCurve
	{
		get => _racingWheelOutputCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _racingWheelOutputCurve )
			{
				_racingWheelOutputCurve = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			if ( _racingWheelOutputCurve == 0f )
			{
				RacingWheelOutputCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelOutputCurveString = $"{_racingWheelOutputCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _racingWheelOutputCurveString = string.Empty;

	[XmlIgnore]
	public string RacingWheelOutputCurveString
	{
		get => _racingWheelOutputCurveString;

		set
		{
			if ( value != _racingWheelOutputCurveString )
			{
				_racingWheelOutputCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelOutputCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelOutputCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelOutputCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Selected recording

	private string _racingWheelSelectedRecording = string.Empty;

	public string RacingWheelSelectedRecording
	{
		get => _racingWheelSelectedRecording;

		set
		{
			if ( value != _racingWheelSelectedRecording )
			{
				_racingWheelSelectedRecording = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;
		}
	}

	#endregion

	#region Racing wheel - Save recording

	public ButtonMappings RacingWheelStartRecordingMappings { get; set; } = new();

	#endregion

	#region Racing wheel - LFE Recording Device

	private Guid _racingWheelLFERecordingDeviceGuid = Guid.Empty;

	public Guid RacingWheelLFERecordingDeviceGuid
	{
		get => _racingWheelLFERecordingDeviceGuid;

		set
		{
			if ( value != _racingWheelLFERecordingDeviceGuid )
			{
				_racingWheelLFERecordingDeviceGuid = value;

				OnPropertyChanged();

				var app = App.Instance!;

				app.LFE.NextCaptureDeviceGuid = _racingWheelLFERecordingDeviceGuid;
			}
		}
	}

	#endregion

	#region Racing wheel - LFE strength

	private float _racingWheelLFEStrength = 0.05f;

	public float RacingWheelLFEStrength
	{
		get => _racingWheelLFEStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelLFEStrength )
			{
				_racingWheelLFEStrength = value;

				OnPropertyChanged();
			}

			if ( _racingWheelLFEStrength == 0f )
			{
				RacingWheelLFEStrengthString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelLFEStrengthString = $"{_racingWheelLFEStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _racingWheelLFEStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelLFEStrengthString
	{
		get => _racingWheelLFEStrengthString;

		set
		{
			if ( value != _racingWheelLFEStrengthString )
			{
				_racingWheelLFEStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelLFEStrengthContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelLFEStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelLFEStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Crash protection longitudinal g-force

	private float _racingWheelCrashProtectionLongitudalGForce = 8f;

	public float RacingWheelCrashProtectionLongitudalGForce
	{
		get => _racingWheelCrashProtectionLongitudalGForce;

		set
		{
			value = Math.Clamp( value, 2f, 20f );

			if ( value != _racingWheelCrashProtectionLongitudalGForce )
			{
				_racingWheelCrashProtectionLongitudalGForce = value;

				OnPropertyChanged();
			}

			if ( _racingWheelCrashProtectionLongitudalGForce == 20f )
			{
				RacingWheelCrashProtectionLongitudalGForceString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelCrashProtectionLongitudalGForceString = $"{_racingWheelCrashProtectionLongitudalGForce:F1} {DataContext.Instance.Localization[ "GForceUnits" ]}";
			}
		}
	}

	private string _racingWheelCrashProtectionLongitudalGForceString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCrashProtectionLongitudalGForceString
	{
		get => _racingWheelCrashProtectionLongitudalGForceString;

		set
		{
			if ( value != _racingWheelCrashProtectionLongitudalGForceString )
			{
				_racingWheelCrashProtectionLongitudalGForceString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCrashProtectionLongitudalGForceContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCrashProtectionLongitudalGForcePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCrashProtectionLongitudalGForceMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Crash protection lateral g-force

	private float _racingWheelCrashProtectionLateralGForce = 6f;

	public float RacingWheelCrashProtectionLateralGForce
	{
		get => _racingWheelCrashProtectionLateralGForce;

		set
		{
			value = Math.Clamp( value, 2f, 20f );

			if ( value != _racingWheelCrashProtectionLateralGForce )
			{
				_racingWheelCrashProtectionLateralGForce = value;

				OnPropertyChanged();
			}

			if ( _racingWheelCrashProtectionLateralGForce == 20f )
			{
				RacingWheelCrashProtectionLateralGForceString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelCrashProtectionLateralGForceString = $"{_racingWheelCrashProtectionLateralGForce:F1} {DataContext.Instance.Localization[ "GForceUnits" ]}";
			}
		}
	}

	private string _racingWheelCrashProtectionLateralGForceString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCrashProtectionLateralGForceString
	{
		get => _racingWheelCrashProtectionLateralGForceString;

		set
		{
			if ( value != _racingWheelCrashProtectionLateralGForceString )
			{
				_racingWheelCrashProtectionLateralGForceString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCrashProtectionLateralGForceContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCrashProtectionLateralGForcePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCrashProtectionLateralGForceMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Crash protection duration

	private float _racingWheelCrashProtectionDuration = 1f;

	public float RacingWheelCrashProtectionDuration
	{
		get => _racingWheelCrashProtectionDuration;

		set
		{
			value = Math.Clamp( value, 0f, 10f );

			if ( value != _racingWheelCrashProtectionDuration )
			{
				_racingWheelCrashProtectionDuration = value;

				OnPropertyChanged();
			}

			if ( _racingWheelCrashProtectionDuration == 0f )
			{
				RacingWheelCrashProtectionDurationString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelCrashProtectionDurationString = $"{_racingWheelCrashProtectionDuration:F1} {DataContext.Instance.Localization[ "SecondsUnits" ]}";
			}
		}
	}

	private string _racingWheelCrashProtectionDurationString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCrashProtectionDurationString
	{
		get => _racingWheelCrashProtectionDurationString;

		set
		{
			if ( value != _racingWheelCrashProtectionDurationString )
			{
				_racingWheelCrashProtectionDurationString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCrashProtectionDurationContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCrashProtectionDurationPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCrashProtectionDurationMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Crash protection force reduction

	private float _racingWheelCrashProtectionForceReduction = 0.95f;

	public float RacingWheelCrashProtectionForceReduction
	{
		get => _racingWheelCrashProtectionForceReduction;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelCrashProtectionForceReduction )
			{
				_racingWheelCrashProtectionForceReduction = value;

				OnPropertyChanged();
			}

			if ( _racingWheelCrashProtectionForceReduction == 0f )
			{
				RacingWheelCrashProtectionForceReductionString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelCrashProtectionForceReductionString = $"{_racingWheelCrashProtectionForceReduction * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _racingWheelCrashProtectionForceReductionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCrashProtectionForceReductionString
	{
		get => _racingWheelCrashProtectionForceReductionString;

		set
		{
			if ( value != _racingWheelCrashProtectionForceReductionString )
			{
				_racingWheelCrashProtectionForceReductionString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCrashProtectionForceReductionContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCrashProtectionForceReductionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCrashProtectionForceReductionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Curb protection shock velocity

	private float _racingWheelCurbProtectionShockVelocity = 0.5f;

	public float RacingWheelCurbProtectionShockVelocity
	{
		get => _racingWheelCurbProtectionShockVelocity;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelCurbProtectionShockVelocity )
			{
				_racingWheelCurbProtectionShockVelocity = value;

				OnPropertyChanged();
			}

			if ( _racingWheelCurbProtectionShockVelocity == 0f )
			{
				RacingWheelCurbProtectionShockVelocityString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelCurbProtectionShockVelocityString = $"{_racingWheelCurbProtectionShockVelocity:F2} {DataContext.Instance.Localization[ "MPSUnits" ]}";
			}
		}
	}

	private string _racingWheelCurbProtectionShockVelocityString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCurbProtectionShockVelocityString
	{
		get => _racingWheelCurbProtectionShockVelocityString;

		set
		{
			if ( value != _racingWheelCurbProtectionShockVelocityString )
			{
				_racingWheelCurbProtectionShockVelocityString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCurbProtectionShockVelocityContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCurbProtectionShockVelocityPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCurbProtectionShockVelocityMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Curb protection duration

	private float _racingWheelCurbProtectionDuration = 0.1f;

	public float RacingWheelCurbProtectionDuration
	{
		get => _racingWheelCurbProtectionDuration;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelCurbProtectionDuration )
			{
				_racingWheelCurbProtectionDuration = value;

				OnPropertyChanged();
			}

			if ( _racingWheelCurbProtectionDuration == 0f )
			{
				RacingWheelCurbProtectionDurationString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelCurbProtectionDurationString = $"{_racingWheelCurbProtectionDuration:F2} {DataContext.Instance.Localization[ "SecondsUnits" ]}";
			}
		}
	}

	private string _racingWheelCurbProtectionDurationString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCurbProtectionDurationString
	{
		get => _racingWheelCurbProtectionDurationString;

		set
		{
			if ( value != _racingWheelCurbProtectionDurationString )
			{
				_racingWheelCurbProtectionDurationString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCurbProtectionDurationContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCurbProtectionDurationPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCurbProtectionDurationMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Curb protection force reduction

	private float _racingWheelCurbProtectionForceReduction = 0.75f;

	public float RacingWheelCurbProtectionForceReduction
	{
		get => _racingWheelCurbProtectionForceReduction;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelCurbProtectionForceReduction )
			{
				_racingWheelCurbProtectionForceReduction = value;

				OnPropertyChanged();
			}

			if ( _racingWheelCurbProtectionForceReduction == 0f )
			{
				RacingWheelCurbProtectionForceReductionString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelCurbProtectionForceReductionString = $"{_racingWheelCurbProtectionForceReduction * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _racingWheelCurbProtectionForceReductionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCurbProtectionForceReductionString
	{
		get => _racingWheelCurbProtectionForceReductionString;

		set
		{
			if ( value != _racingWheelCurbProtectionForceReductionString )
			{
				_racingWheelCurbProtectionForceReductionString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCurbProtectionForceReductionContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCurbProtectionForceReductionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCurbProtectionForceReductionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Parked strength

	private float _racingWheelParkedStrength = 0.1f;

	public float RacingWheelParkedStrength
	{
		get => _racingWheelParkedStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelParkedStrength )
			{
				_racingWheelParkedStrength = value;

				OnPropertyChanged();
			}

			if ( _racingWheelParkedStrength == 1f )
			{
				RacingWheelParkedStrengthString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelParkedStrengthString = $"{_racingWheelParkedStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _racingWheelParkedStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelParkedStrengthString
	{
		get => _racingWheelParkedStrengthString;

		set
		{
			if ( value != _racingWheelParkedStrengthString )
			{
				_racingWheelParkedStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelParkedStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelParkedStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelParkedStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Parked friction

	private float _racingWheelParkedFriction = 0f;

	public float RacingWheelParkedFriction
	{
		get => _racingWheelParkedFriction;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelParkedFriction )
			{
				_racingWheelParkedFriction = value;

				OnPropertyChanged();
			}

			if ( _racingWheelParkedFriction == 0f )
			{
				RacingWheelParkedFrictionString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelParkedFrictionString = $"{_racingWheelParkedFriction * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _racingWheelParkedFrictionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelParkedFrictionString
	{
		get => _racingWheelParkedFrictionString;

		set
		{
			if ( value != _racingWheelParkedFrictionString )
			{
				_racingWheelParkedFrictionString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelParkedFrictionContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelParkedFrictionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelParkedFrictionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Soft lock strength

	private float _racingWheelSoftLockStrength = 0.25f;

	public float RacingWheelSoftLockStrength
	{
		get => _racingWheelSoftLockStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelSoftLockStrength )
			{
				_racingWheelSoftLockStrength = value;

				OnPropertyChanged();
			}

			if ( _racingWheelSoftLockStrength == 0f )
			{
				RacingWheelSoftLockStrengthString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelSoftLockStrengthString = $"{_racingWheelSoftLockStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _racingWheelSoftLockStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelSoftLockStrengthString
	{
		get => _racingWheelSoftLockStrengthString;

		set
		{
			if ( value != _racingWheelSoftLockStrengthString )
			{
				_racingWheelSoftLockStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelSoftLockStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelSoftLockStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelSoftLockStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Friction

	private float _racingWheelFriction = 0f;

	public float RacingWheelFriction
	{
		get => _racingWheelFriction;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelFriction )
			{
				_racingWheelFriction = value;

				OnPropertyChanged();
			}

			if ( _racingWheelFriction == 0f )
			{
				RacingWheelFrictionString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelFrictionString = $"{_racingWheelFriction * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _racingWheelFrictionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelFrictionString
	{
		get => _racingWheelFrictionString;

		set
		{
			if ( value != _racingWheelFrictionString )
			{
				_racingWheelFrictionString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelFrictionContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelFrictionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelFrictionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Wheel centering strength

	private float _racingWheelWheelCenteringStrength = 0.75f;

	public float RacingWheelWheelCenteringStrength
	{
		get => _racingWheelWheelCenteringStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelWheelCenteringStrength )
			{
				_racingWheelWheelCenteringStrength = value;

				OnPropertyChanged();
			}

			if ( _racingWheelWheelCenteringStrength == 0f )
			{
				RacingWheelWheelCenteringStrengthString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelWheelCenteringStrengthString = $"{_racingWheelWheelCenteringStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _racingWheelWheelCenteringStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelWheelCenteringStrengthString
	{
		get => _racingWheelWheelCenteringStrengthString;

		set
		{
			if ( value != _racingWheelWheelCenteringStrengthString )
			{
				_racingWheelWheelCenteringStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelWheelCenteringStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelWheelCenteringStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelWheelCenteringStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Gear change vibrate strength

	private float _racingWheelGearChangeVibrateStrength = 0.0f;

	public float RacingWheelGearChangeVibrateStrength
	{
		get => _racingWheelGearChangeVibrateStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelGearChangeVibrateStrength )
			{
				_racingWheelGearChangeVibrateStrength = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelGearChangeVibrateStrengthString();
		}
	}

	private string _racingWheelGearChangeVibrateStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelGearChangeVibrateStrengthString
	{
		get => _racingWheelGearChangeVibrateStrengthString;

		set
		{
			if ( value != _racingWheelGearChangeVibrateStrengthString )
			{
				_racingWheelGearChangeVibrateStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelGearChangeVibrateStrengthString()
	{
		if ( _racingWheelGearChangeVibrateStrength == 0f )
		{
			RacingWheelGearChangeVibrateStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _racingWheelGearChangeVibrateStrength;

			RacingWheelGearChangeVibrateStrengthString = $"{_racingWheelGearChangeVibrateStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches RacingWheelGearChangeVibrateStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelGearChangeVibrateStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelGearChangeVibrateStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - ABS vibrate strength

	private float _racingWheelABSVibrateStrength = 0.0f;

	public float RacingWheelABSVibrateStrength
	{
		get => _racingWheelABSVibrateStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelABSVibrateStrength )
			{
				_racingWheelABSVibrateStrength = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelABSVibrateStrengthString();
		}
	}

	private string _racingWheelABSVibrateStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelABSVibrateStrengthString
	{
		get => _racingWheelABSVibrateStrengthString;

		set
		{
			if ( value != _racingWheelABSVibrateStrengthString )
			{
				_racingWheelABSVibrateStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelABSVibrateStrengthString()
	{
		if ( _racingWheelABSVibrateStrength == 0f )
		{
			RacingWheelABSVibrateStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _racingWheelABSVibrateStrength;

			RacingWheelABSVibrateStrengthString = $"{_racingWheelABSVibrateStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches RacingWheelABSVibrateStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelABSVibrateStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelABSVibrateStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Send chat messages

	private bool _racingWheelSendChatMessages = true;

	public bool RacingWheelSendChatMessages
	{
		get => _racingWheelSendChatMessages;

		set
		{
			if ( value != _racingWheelSendChatMessages )
			{
				_racingWheelSendChatMessages = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Racing wheel - Input mapped setting update enabled

	private bool _racingWheelInputMappedSettingUpdateEnabled = true;

	public bool RacingWheelInputMappedSettingUpdateEnabled
	{
		get => _racingWheelInputMappedSettingUpdateEnabled;

		set
		{
			if ( value != _racingWheelInputMappedSettingUpdateEnabled )
			{
				_racingWheelInputMappedSettingUpdateEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Racing wheel - Crash protection messages enabled

	private bool _racingWheelCrashProtectionMessagesEnabled = false;

	public bool RacingWheelCrashProtectionMessagesEnabled
	{
		get => _racingWheelCrashProtectionMessagesEnabled;

		set
		{
			if ( value != _racingWheelCrashProtectionMessagesEnabled )
			{
				_racingWheelCrashProtectionMessagesEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Racing wheel - Curb protection messages enabled

	private bool _racingWheelCurbProtectionMessagesEnabled = false;

	public bool RacingWheelCurbProtectionMessagesEnabled
	{
		get => _racingWheelCurbProtectionMessagesEnabled;

		set
		{
			if ( value != _racingWheelCurbProtectionMessagesEnabled )
			{
				_racingWheelCurbProtectionMessagesEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Racing wheel - Center wheel while racing

	private bool _racingWheelCenterWheelWhileRacing = false;

	public bool RacingWheelCenterWheelWhileRacing
	{
		get => _racingWheelCenterWheelWhileRacing;

		set
		{
			if ( value != _racingWheelCenterWheelWhileRacing )
			{
				_racingWheelCenterWheelWhileRacing = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCenterWheelWhileRacingContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Center wheel while parked

	private bool _racingWheelCenterWheelWhileParked = true;

	public bool RacingWheelCenterWheelWhileParked
	{
		get => _racingWheelCenterWheelWhileParked;

		set
		{
			if ( value != _racingWheelCenterWheelWhileParked )
			{
				_racingWheelCenterWheelWhileParked = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCenterWheelWhileParkedContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Fade enabled

	private bool _racingWheelFadeEnabled = true;

	public bool RacingWheelFadeEnabled
	{
		get => _racingWheelFadeEnabled;

		set
		{
			if ( value != _racingWheelFadeEnabled )
			{
				_racingWheelFadeEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelFadeEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Enable Logitech RPM lights

	private bool _racingWheelEnableLogitechRPMLights = true;

	public bool RacingWheelEnableLogitechRPMLights
	{
		get => _racingWheelEnableLogitechRPMLights;

		set
		{
			if ( value != _racingWheelEnableLogitechRPMLights )
			{
				_racingWheelEnableLogitechRPMLights = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Racing wheel - Allow super strength

	private bool _racingWheelAllowSuperStrength = false;

	public bool RacingWheelAllowSuperStrength
	{
		get => _racingWheelAllowSuperStrength;

		set
		{
			if ( value != _racingWheelAllowSuperStrength )
			{
				_racingWheelAllowSuperStrength = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings( nameof( RacingWheelMaxForce ) );
			UpdateRelatedRacingWheelSettings( nameof( RacingWheelStrength ) );
		}
	}

	#endregion

	#region Racing wheel - Always enable FFB

	private bool _racingWheelAlwaysEnableFFB = false;

	public bool RacingWheelAlwaysEnableFFB
	{
		get => _racingWheelAlwaysEnableFFB;

		set
		{
			if ( value != _racingWheelAlwaysEnableFFB )
			{
				_racingWheelAlwaysEnableFFB = value;

				OnPropertyChanged();
			}

			_racingWheelPage.UpdateSteeringDeviceSection();
		}
	}

	#endregion

	#region Racing wheel - Simple mode

	private bool _racingWheelSimpleModeEnabled = false;

	public bool RacingWheelSimpleModeEnabled
	{
		get => _racingWheelSimpleModeEnabled;

		set
		{
			if ( value != _racingWheelSimpleModeEnabled )
			{
				_racingWheelSimpleModeEnabled = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.MainWindow.UpdateRacingWheelSimpleMode();
		}
	}

	#endregion

	#region Steering effects - Calibration file name

	private string _steeringEffectsCalibrationFileName = string.Empty;

	public string SteeringEffectsCalibrationFileName
	{
		get => _steeringEffectsCalibrationFileName;

		set
		{
			var app = App.Instance!;

			if ( !app.SettingsFile.PauseSerialization )
			{
				if ( value == null )
				{
					value = string.Empty;
				}

				if ( value != _steeringEffectsCalibrationFileName )
				{
					_steeringEffectsCalibrationFileName = value;

					OnPropertyChanged();

					app.SteeringEffects.LoadCalibration();

					MainWindow._steeringEffectsPage.CalibrationFileNameChanged( _steeringEffectsCalibrationFileName != string.Empty );
				}
			}
		}
	}

	public ContextSwitches SteeringEffectsCalibrationFileNameContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Steering effects - Understeer enabled

	private bool _steeringEffectsUndersteerEnabled = false;

	public bool SteeringEffectsUndersteerEnabled
	{
		get => _steeringEffectsUndersteerEnabled;

		set
		{
			if ( value != _steeringEffectsUndersteerEnabled )
			{
				_steeringEffectsUndersteerEnabled = value;

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerEnabledContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Steering effects - Understeer minimum threshold

	private float _steeringEffectsUndersteerMinimumThreshold = 0.05f;

	public float SteeringEffectsUndersteerMinimumThreshold
	{
		get => _steeringEffectsUndersteerMinimumThreshold;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsUndersteerMinimumThreshold )
			{
				_steeringEffectsUndersteerMinimumThreshold = value;

				SteeringEffectsUndersteerMaximumThreshold = MathF.Max( SteeringEffectsUndersteerMaximumThreshold, _steeringEffectsUndersteerMinimumThreshold );

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}

			SteeringEffectsUndersteerMinimumThresholdString = $"{_steeringEffectsUndersteerMinimumThreshold:F2}{DataContext.Instance.Localization[ "DegreesPerSecond" ]}";
		}
	}

	private string _steeringEffectsUndersteerMinimumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerMinimumThresholdString
	{
		get => _steeringEffectsUndersteerMinimumThresholdString;

		set
		{
			if ( value != _steeringEffectsUndersteerMinimumThresholdString )
			{
				_steeringEffectsUndersteerMinimumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerMinimumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerMinimumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerMinimumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer maximum threshold

	private float _steeringEffectsUndersteerMaximumThreshold = 0.15f;

	public float SteeringEffectsUndersteerMaximumThreshold
	{
		get => _steeringEffectsUndersteerMaximumThreshold;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsUndersteerMaximumThreshold )
			{
				_steeringEffectsUndersteerMaximumThreshold = value;

				SteeringEffectsUndersteerMinimumThreshold = MathF.Min( SteeringEffectsUndersteerMinimumThreshold, _steeringEffectsUndersteerMaximumThreshold );

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}

			SteeringEffectsUndersteerMaximumThresholdString = $"{_steeringEffectsUndersteerMaximumThreshold:F2}{DataContext.Instance.Localization[ "DegreesPerSecond" ]}";
		}
	}

	private string _steeringEffectsUndersteerMaximumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerMaximumThresholdString
	{
		get => _steeringEffectsUndersteerMaximumThresholdString;

		set
		{
			if ( value != _steeringEffectsUndersteerMaximumThresholdString )
			{
				_steeringEffectsUndersteerMaximumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerMaximumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerMaximumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerMaximumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel vibration pattern

	private RacingWheel.VibrationPattern _steeringEffectsUndersteerWheelVibrationPattern = RacingWheel.VibrationPattern.SineWave;

	public RacingWheel.VibrationPattern SteeringEffectsUndersteerWheelVibrationPattern
	{
		get => _steeringEffectsUndersteerWheelVibrationPattern;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelVibrationPattern )
			{
				_steeringEffectsUndersteerWheelVibrationPattern = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelVibrationPatternContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Understeer wheel vibration strength

	private float _steeringEffectsUndersteerWheelVibrationStrength = 0.1f;

	public float SteeringEffectsUndersteerWheelVibrationStrength
	{
		get => _steeringEffectsUndersteerWheelVibrationStrength;

		set
		{
			value = Math.Clamp( value, 0f, 0.3f );

			if ( value != _steeringEffectsUndersteerWheelVibrationStrength )
			{
				_steeringEffectsUndersteerWheelVibrationStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerWheelVibrationStrengthString();
		}
	}

	private string _steeringEffectsUndersteerWheelVibrationStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelVibrationStrengthString
	{
		get => _steeringEffectsUndersteerWheelVibrationStrengthString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelVibrationStrengthString )
			{
				_steeringEffectsUndersteerWheelVibrationStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerWheelVibrationStrengthString()
	{
		if ( _steeringEffectsUndersteerWheelVibrationStrength == 0f )
		{
			SteeringEffectsUndersteerWheelVibrationStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsUndersteerWheelVibrationStrength;

			SteeringEffectsUndersteerWheelVibrationStrengthString = $"{_steeringEffectsUndersteerWheelVibrationStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelVibrationStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel vibration minimum frequency

	private float _steeringEffectsUndersteerWheelVibrationMinimumFrequency = 15f;

	public float SteeringEffectsUndersteerWheelVibrationMinimumFrequency
	{
		get => _steeringEffectsUndersteerWheelVibrationMinimumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsUndersteerWheelVibrationMinimumFrequency )
			{
				_steeringEffectsUndersteerWheelVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			SteeringEffectsUndersteerWheelVibrationMinimumFrequencyString = $"{_steeringEffectsUndersteerWheelVibrationMinimumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
		}
	}

	private string _steeringEffectsUndersteerWheelVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelVibrationMinimumFrequencyString
	{
		get => _steeringEffectsUndersteerWheelVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelVibrationMinimumFrequencyString )
			{
				_steeringEffectsUndersteerWheelVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel vibration maximum frequency

	private float _steeringEffectsUndersteerWheelVibrationMaximumFrequency = 50f;

	public float SteeringEffectsUndersteerWheelVibrationMaximumFrequency
	{
		get => _steeringEffectsUndersteerWheelVibrationMaximumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsUndersteerWheelVibrationMaximumFrequency )
			{
				_steeringEffectsUndersteerWheelVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			SteeringEffectsUndersteerWheelVibrationMaximumFrequencyString = $"{_steeringEffectsUndersteerWheelVibrationMaximumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
		}
	}

	private string _steeringEffectsUndersteerWheelVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelVibrationMaximumFrequencyString
	{
		get => _steeringEffectsUndersteerWheelVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelVibrationMaximumFrequencyString )
			{
				_steeringEffectsUndersteerWheelVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel vibration curve

	private float _steeringEffectsUndersteerWheelVibrationCurve = 0.25f;

	public float SteeringEffectsUndersteerWheelVibrationCurve
	{
		get => _steeringEffectsUndersteerWheelVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsUndersteerWheelVibrationCurve )
			{
				_steeringEffectsUndersteerWheelVibrationCurve = value;

				OnPropertyChanged();
			}

			if ( _steeringEffectsUndersteerWheelVibrationCurve == 0f )
			{
				SteeringEffectsUndersteerWheelVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				SteeringEffectsUndersteerWheelVibrationCurveString = $"{_steeringEffectsUndersteerWheelVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _steeringEffectsUndersteerWheelVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelVibrationCurveString
	{
		get => _steeringEffectsUndersteerWheelVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelVibrationCurveString )
			{
				_steeringEffectsUndersteerWheelVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelVibrationCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel constant force direction

	private RacingWheel.ConstantForceDirection _steeringEffectsUndersteerWheelConstantForceDirection = RacingWheel.ConstantForceDirection.None;

	public RacingWheel.ConstantForceDirection SteeringEffectsUndersteerWheelConstantForceDirection
	{
		get => _steeringEffectsUndersteerWheelConstantForceDirection;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelConstantForceDirection )
			{
				_steeringEffectsUndersteerWheelConstantForceDirection = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelConstantForceDirectionContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Understeer wheel constant force strength

	private float _steeringEffectsUndersteerWheelConstantForceStrength = 0.1f;

	public float SteeringEffectsUndersteerWheelConstantForceStrength
	{
		get => _steeringEffectsUndersteerWheelConstantForceStrength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _steeringEffectsUndersteerWheelConstantForceStrength )
			{
				_steeringEffectsUndersteerWheelConstantForceStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerWheelConstantForceStrengthString();
		}
	}

	private string _steeringEffectsUndersteerWheelConstantForceStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelConstantForceStrengthString
	{
		get => _steeringEffectsUndersteerWheelConstantForceStrengthString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelConstantForceStrengthString )
			{
				_steeringEffectsUndersteerWheelConstantForceStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerWheelConstantForceStrengthString()
	{
		if ( _steeringEffectsUndersteerWheelConstantForceStrength == 0f )
		{
			SteeringEffectsUndersteerWheelConstantForceStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsUndersteerWheelConstantForceStrength;

			SteeringEffectsUndersteerWheelConstantForceStrengthString = $"{_steeringEffectsUndersteerWheelConstantForceStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}


	public ContextSwitches SteeringEffectsUndersteerWheelConstantForceStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelConstantForceStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelConstantForceStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel constant force curve

	private float _steeringEffectsUndersteerWheelConstantForceCurve = 0f;

	public float SteeringEffectsUndersteerWheelConstantForceCurve
	{
		get => _steeringEffectsUndersteerWheelConstantForceCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsUndersteerWheelConstantForceCurve )
			{
				_steeringEffectsUndersteerWheelConstantForceCurve = value;

				OnPropertyChanged();
			}

			if ( _steeringEffectsUndersteerWheelConstantForceCurve == 0f )
			{
				SteeringEffectsUndersteerWheelConstantForceCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				SteeringEffectsUndersteerWheelConstantForceCurveString = $"{_steeringEffectsUndersteerWheelConstantForceCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _steeringEffectsUndersteerWheelConstantForceCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelConstantForceCurveString
	{
		get => _steeringEffectsUndersteerWheelConstantForceCurveString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelConstantForceCurveString )
			{
				_steeringEffectsUndersteerWheelConstantForceCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelConstantForceCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelConstantForceCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelConstantForceCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer pedal vibration minimum frequency

	private float _steeringEffectsUndersteerPedalVibrationMinimumFrequency = 0.1f;

	public float SteeringEffectsUndersteerPedalVibrationMinimumFrequency
	{
		get => _steeringEffectsUndersteerPedalVibrationMinimumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsUndersteerPedalVibrationMinimumFrequency )
			{
				_steeringEffectsUndersteerPedalVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerPedalVibrationMinimumFrequencyString();
		}
	}

	private string _steeringEffectsUndersteerPedalVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerPedalVibrationMinimumFrequencyString
	{
		get => _steeringEffectsUndersteerPedalVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsUndersteerPedalVibrationMinimumFrequencyString )
			{
				_steeringEffectsUndersteerPedalVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerPedalVibrationMinimumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsUndersteerPedalVibrationMinimumFrequency ) );

		SteeringEffectsUndersteerPedalVibrationMinimumFrequencyString = $"{_steeringEffectsUndersteerPedalVibrationMinimumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsUndersteerPedalVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer pedal vibration maximum frequency

	private float _steeringEffectsUndersteerPedalVibrationMaximumFrequency = 1f;

	public float SteeringEffectsUndersteerPedalVibrationMaximumFrequency
	{
		get => _steeringEffectsUndersteerPedalVibrationMaximumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsUndersteerPedalVibrationMaximumFrequency )
			{
				_steeringEffectsUndersteerPedalVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerPedalVibrationMaximumFrequencyString();
		}
	}

	private string _steeringEffectsUndersteerPedalVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerPedalVibrationMaximumFrequencyString
	{
		get => _steeringEffectsUndersteerPedalVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsUndersteerPedalVibrationMaximumFrequencyString )
			{
				_steeringEffectsUndersteerPedalVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerPedalVibrationMaximumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsUndersteerPedalVibrationMaximumFrequency ) );

		SteeringEffectsUndersteerPedalVibrationMaximumFrequencyString = $"{_steeringEffectsUndersteerPedalVibrationMaximumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsUndersteerPedalVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer pedal vibration curve

	private float _steeringEffectsUndersteerPedalVibrationCurve = 0f;

	public float SteeringEffectsUndersteerPedalVibrationCurve
	{
		get => _steeringEffectsUndersteerPedalVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsUndersteerPedalVibrationCurve )
			{
				_steeringEffectsUndersteerPedalVibrationCurve = value;

				OnPropertyChanged();
			}

			if ( _steeringEffectsUndersteerPedalVibrationCurve == 0f )
			{
				SteeringEffectsUndersteerPedalVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				SteeringEffectsUndersteerPedalVibrationCurveString = $"{_steeringEffectsUndersteerPedalVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _steeringEffectsUndersteerPedalVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerPedalVibrationCurveString
	{
		get => _steeringEffectsUndersteerPedalVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsUndersteerPedalVibrationCurveString )
			{
				_steeringEffectsUndersteerPedalVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerPedalVibrationCurveContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer enabled

	private bool _steeringEffectsOversteerEnabled = false;

	public bool SteeringEffectsOversteerEnabled
	{
		get => _steeringEffectsOversteerEnabled;

		set
		{
			if ( value != _steeringEffectsOversteerEnabled )
			{
				_steeringEffectsOversteerEnabled = value;

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerEnabledContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Steering effects - Oversteer minimum threshold

	private float _steeringEffectsOversteerMinimumThreshold = 0f;

	public float SteeringEffectsOversteerMinimumThreshold
	{
		get => _steeringEffectsOversteerMinimumThreshold;

		set
		{
			value = Math.Clamp( value, -1f, 2f );

			if ( value != _steeringEffectsOversteerMinimumThreshold )
			{
				_steeringEffectsOversteerMinimumThreshold = value;

				SteeringEffectsOversteerMaximumThreshold = MathF.Max( SteeringEffectsOversteerMaximumThreshold, _steeringEffectsOversteerMinimumThreshold );

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}

			SteeringEffectsOversteerMinimumThresholdString = $"{_steeringEffectsOversteerMinimumThreshold:F2}{DataContext.Instance.Localization[ "DegreesPerSecond" ]}";
		}
	}

	private string _steeringEffectsOversteerMinimumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerMinimumThresholdString
	{
		get => _steeringEffectsOversteerMinimumThresholdString;

		set
		{
			if ( value != _steeringEffectsOversteerMinimumThresholdString )
			{
				_steeringEffectsOversteerMinimumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerMinimumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsOversteerMinimumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerMinimumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer maximum threshold

	private float _steeringEffectsOversteerMaximumThreshold = 0.5f;

	public float SteeringEffectsOversteerMaximumThreshold
	{
		get => _steeringEffectsOversteerMaximumThreshold;

		set
		{
			value = Math.Clamp( value, -1f, 2f );

			if ( value != _steeringEffectsOversteerMaximumThreshold )
			{
				_steeringEffectsOversteerMaximumThreshold = value;

				SteeringEffectsOversteerMinimumThreshold = MathF.Min( SteeringEffectsOversteerMinimumThreshold, _steeringEffectsOversteerMaximumThreshold );

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}

			SteeringEffectsOversteerMaximumThresholdString = $"{_steeringEffectsOversteerMaximumThreshold:F2}{DataContext.Instance.Localization[ "DegreesPerSecond" ]}";
		}
	}

	private string _steeringEffectsOversteerMaximumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerMaximumThresholdString
	{
		get => _steeringEffectsOversteerMaximumThresholdString;

		set
		{
			if ( value != _steeringEffectsOversteerMaximumThresholdString )
			{
				_steeringEffectsOversteerMaximumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerMaximumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsOversteerMaximumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerMaximumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel vibration pattern

	private RacingWheel.VibrationPattern _steeringEffectsOversteerWheelVibrationPattern = RacingWheel.VibrationPattern.None;

	public RacingWheel.VibrationPattern SteeringEffectsOversteerWheelVibrationPattern
	{
		get => _steeringEffectsOversteerWheelVibrationPattern;

		set
		{
			if ( value != _steeringEffectsOversteerWheelVibrationPattern )
			{
				_steeringEffectsOversteerWheelVibrationPattern = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelVibrationPatternContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Oversteer wheel vibration strength

	private float _steeringEffectsOversteerWheelVibrationStrength = 0.1f;

	public float SteeringEffectsOversteerWheelVibrationStrength
	{
		get => _steeringEffectsOversteerWheelVibrationStrength;

		set
		{
			value = Math.Clamp( value, 0f, 0.3f );

			if ( value != _steeringEffectsOversteerWheelVibrationStrength )
			{
				_steeringEffectsOversteerWheelVibrationStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerWheelVibrationStrengthString();
		}
	}

	private string _steeringEffectsOversteerWheelVibrationStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelVibrationStrengthString
	{
		get => _steeringEffectsOversteerWheelVibrationStrengthString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelVibrationStrengthString )
			{
				_steeringEffectsOversteerWheelVibrationStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerWheelVibrationStrengthString()
	{
		if ( _steeringEffectsOversteerWheelVibrationStrength == 0f )
		{
			SteeringEffectsOversteerWheelVibrationStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsOversteerWheelVibrationStrength;

			SteeringEffectsOversteerWheelVibrationStrengthString = $"{_steeringEffectsOversteerWheelVibrationStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelVibrationStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelVibrationStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelVibrationStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel vibration minimum frequency

	private float _steeringEffectsOversteerWheelVibrationMinimumFrequency = 15f;

	public float SteeringEffectsOversteerWheelVibrationMinimumFrequency
	{
		get => _steeringEffectsOversteerWheelVibrationMinimumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsOversteerWheelVibrationMinimumFrequency )
			{
				_steeringEffectsOversteerWheelVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			SteeringEffectsOversteerWheelVibrationMinimumFrequencyString = $"{_steeringEffectsOversteerWheelVibrationMinimumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
		}
	}

	private string _steeringEffectsOversteerWheelVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelVibrationMinimumFrequencyString
	{
		get => _steeringEffectsOversteerWheelVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelVibrationMinimumFrequencyString )
			{
				_steeringEffectsOversteerWheelVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel vibration maximum frequency

	private float _steeringEffectsOversteerWheelVibrationMaximumFrequency = 50f;

	public float SteeringEffectsOversteerWheelVibrationMaximumFrequency
	{
		get => _steeringEffectsOversteerWheelVibrationMaximumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsOversteerWheelVibrationMaximumFrequency )
			{
				_steeringEffectsOversteerWheelVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			SteeringEffectsOversteerWheelVibrationMaximumFrequencyString = $"{_steeringEffectsOversteerWheelVibrationMaximumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
		}
	}

	private string _steeringEffectsOversteerWheelVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelVibrationMaximumFrequencyString
	{
		get => _steeringEffectsOversteerWheelVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelVibrationMaximumFrequencyString )
			{
				_steeringEffectsOversteerWheelVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel vibration curve

	private float _steeringEffectsOversteerWheelVibrationCurve = 0.25f;

	public float SteeringEffectsOversteerWheelVibrationCurve
	{
		get => _steeringEffectsOversteerWheelVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsOversteerWheelVibrationCurve )
			{
				_steeringEffectsOversteerWheelVibrationCurve = value;

				OnPropertyChanged();
			}

			if ( _steeringEffectsOversteerWheelVibrationCurve == 0f )
			{
				SteeringEffectsOversteerWheelVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				SteeringEffectsOversteerWheelVibrationCurveString = $"{_steeringEffectsOversteerWheelVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _steeringEffectsOversteerWheelVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelVibrationCurveString
	{
		get => _steeringEffectsOversteerWheelVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelVibrationCurveString )
			{
				_steeringEffectsOversteerWheelVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelVibrationCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel constant force direction

	private RacingWheel.ConstantForceDirection _steeringEffectsOversteerWheelConstantForceDirection = RacingWheel.ConstantForceDirection.IncreaseForce;

	public RacingWheel.ConstantForceDirection SteeringEffectsOversteerWheelConstantForceDirection
	{
		get => _steeringEffectsOversteerWheelConstantForceDirection;

		set
		{
			if ( value != _steeringEffectsOversteerWheelConstantForceDirection )
			{
				_steeringEffectsOversteerWheelConstantForceDirection = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelConstantForceDirectionContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Oversteer wheel constant force strength

	private float _steeringEffectsOversteerWheelConstantForceStrength = 0.1f;

	public float SteeringEffectsOversteerWheelConstantForceStrength
	{
		get => _steeringEffectsOversteerWheelConstantForceStrength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _steeringEffectsOversteerWheelConstantForceStrength )
			{
				_steeringEffectsOversteerWheelConstantForceStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerWheelConstantForceStrengthString();
		}
	}

	private string _steeringEffectsOversteerWheelConstantForceStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelConstantForceStrengthString
	{
		get => _steeringEffectsOversteerWheelConstantForceStrengthString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelConstantForceStrengthString )
			{
				_steeringEffectsOversteerWheelConstantForceStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerWheelConstantForceStrengthString()
	{
		if ( _steeringEffectsOversteerWheelConstantForceStrength == 0f )
		{
			SteeringEffectsOversteerWheelConstantForceStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsOversteerWheelConstantForceStrength;

			SteeringEffectsOversteerWheelConstantForceStrengthString = $"{_steeringEffectsOversteerWheelConstantForceStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelConstantForceStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelConstantForceStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelConstantForceStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel constant force curve

	private float _steeringEffectsOversteerWheelConstantForceCurve = 0f;

	public float SteeringEffectsOversteerWheelConstantForceCurve
	{
		get => _steeringEffectsOversteerWheelConstantForceCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsOversteerWheelConstantForceCurve )
			{
				_steeringEffectsOversteerWheelConstantForceCurve = value;

				OnPropertyChanged();
			}

			if ( _steeringEffectsOversteerWheelConstantForceCurve == 0f )
			{
				SteeringEffectsOversteerWheelConstantForceCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				SteeringEffectsOversteerWheelConstantForceCurveString = $"{_steeringEffectsOversteerWheelConstantForceCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _steeringEffectsOversteerWheelConstantForceCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelConstantForceCurveString
	{
		get => _steeringEffectsOversteerWheelConstantForceCurveString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelConstantForceCurveString )
			{
				_steeringEffectsOversteerWheelConstantForceCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelConstantForceCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelConstantForceCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelConstantForceCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer pedal vibration minimum frequency

	private float _steeringEffectsOversteerPedalVibrationMinimumFrequency = 0.1f;

	public float SteeringEffectsOversteerPedalVibrationMinimumFrequency
	{
		get => _steeringEffectsOversteerPedalVibrationMinimumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsOversteerPedalVibrationMinimumFrequency )
			{
				_steeringEffectsOversteerPedalVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerPedalVibrationMinimumFrequencyString();
		}
	}

	private string _steeringEffectsOversteerPedalVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerPedalVibrationMinimumFrequencyString
	{
		get => _steeringEffectsOversteerPedalVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsOversteerPedalVibrationMinimumFrequencyString )
			{
				_steeringEffectsOversteerPedalVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerPedalVibrationMinimumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsOversteerPedalVibrationMinimumFrequency ) );

		SteeringEffectsOversteerPedalVibrationMinimumFrequencyString = $"{_steeringEffectsOversteerPedalVibrationMinimumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsOversteerPedalVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsOversteerPedalVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerPedalVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer pedal vibration maximum frequency

	private float _steeringEffectsOversteerPedalVibrationMaximumFrequency = 1f;

	public float SteeringEffectsOversteerPedalVibrationMaximumFrequency
	{
		get => _steeringEffectsOversteerPedalVibrationMaximumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsOversteerPedalVibrationMaximumFrequency )
			{
				_steeringEffectsOversteerPedalVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerPedalVibrationMaximumFrequencyString();
		}
	}

	private string _steeringEffectsOversteerPedalVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerPedalVibrationMaximumFrequencyString
	{
		get => _steeringEffectsOversteerPedalVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsOversteerPedalVibrationMaximumFrequencyString )
			{
				_steeringEffectsOversteerPedalVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerPedalVibrationMaximumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsOversteerPedalVibrationMaximumFrequency ) );

		SteeringEffectsOversteerPedalVibrationMaximumFrequencyString = $"{_steeringEffectsOversteerPedalVibrationMaximumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsOversteerPedalVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsOversteerPedalVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerPedalVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer pedal vibration curve

	private float _steeringEffectsOversteerPedalVibrationCurve = 0f;

	public float SteeringEffectsOversteerPedalVibrationCurve
	{
		get => _steeringEffectsOversteerPedalVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsOversteerPedalVibrationCurve )
			{
				_steeringEffectsOversteerPedalVibrationCurve = value;

				OnPropertyChanged();
			}

			if ( _steeringEffectsOversteerPedalVibrationCurve == 0f )
			{
				SteeringEffectsOversteerPedalVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				SteeringEffectsOversteerPedalVibrationCurveString = $"{_steeringEffectsOversteerPedalVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _steeringEffectsOversteerPedalVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerPedalVibrationCurveString
	{
		get => _steeringEffectsOversteerPedalVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsOversteerPedalVibrationCurveString )
			{
				_steeringEffectsOversteerPedalVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerPedalVibrationCurveContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsOversteerPedalVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerPedalVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - SeatOfPants enabled

	private bool _steeringEffectsSeatOfPantsEnabled = false;

	public bool SteeringEffectsSeatOfPantsEnabled
	{
		get => _steeringEffectsSeatOfPantsEnabled;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsEnabled )
			{
				_steeringEffectsSeatOfPantsEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsEnabledContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Steering effects - Seat-of-pants minimum threshold

	private float _steeringEffectsSeatOfPantsMinimumThreshold = 0f;

	public float SteeringEffectsSeatOfPantsMinimumThreshold
	{
		get => _steeringEffectsSeatOfPantsMinimumThreshold;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsSeatOfPantsMinimumThreshold )
			{
				_steeringEffectsSeatOfPantsMinimumThreshold = value;

				SteeringEffectsSeatOfPantsMaximumThreshold = MathF.Max( SteeringEffectsSeatOfPantsMaximumThreshold, _steeringEffectsSeatOfPantsMinimumThreshold );

				OnPropertyChanged();
			}

			var units = SteeringEffectsSeatOfPantsAlgorithm switch
			{
				SteeringEffects.SeatOfPantsAlgorithm.YAcceleration => DataContext.Instance.Localization[ "GForceUnits" ],
				SteeringEffects.SeatOfPantsAlgorithm.YVelocity => DataContext.Instance.Localization[ "MPSUnits" ],
				_ => ""
			};

			SteeringEffectsSeatOfPantsMinimumThresholdString = $"{_steeringEffectsSeatOfPantsMinimumThreshold:F2} {units}";
		}
	}

	private string _steeringEffectsSeatOfPantsMinimumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsMinimumThresholdString
	{
		get => _steeringEffectsSeatOfPantsMinimumThresholdString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsMinimumThresholdString )
			{
				_steeringEffectsSeatOfPantsMinimumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsMinimumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsMinimumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsMinimumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants maximum threshold

	private float _steeringEffectsSeatOfPantsMaximumThreshold = 10f;

	public float SteeringEffectsSeatOfPantsMaximumThreshold
	{
		get => _steeringEffectsSeatOfPantsMaximumThreshold;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsSeatOfPantsMaximumThreshold )
			{
				_steeringEffectsSeatOfPantsMaximumThreshold = value;

				SteeringEffectsSeatOfPantsMinimumThreshold = MathF.Min( SteeringEffectsSeatOfPantsMinimumThreshold, _steeringEffectsSeatOfPantsMaximumThreshold );

				OnPropertyChanged();
			}

			var units = SteeringEffectsSeatOfPantsAlgorithm switch
			{
				SteeringEffects.SeatOfPantsAlgorithm.YAcceleration => DataContext.Instance.Localization[ "GForceUnits" ],
				SteeringEffects.SeatOfPantsAlgorithm.YVelocity => DataContext.Instance.Localization[ "MPSUnits" ],
				_ => ""
			};

			SteeringEffectsSeatOfPantsMaximumThresholdString = $"{_steeringEffectsSeatOfPantsMaximumThreshold:F2} {units}";
		}
	}

	private string _steeringEffectsSeatOfPantsMaximumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsMaximumThresholdString
	{
		get => _steeringEffectsSeatOfPantsMaximumThresholdString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsMaximumThresholdString )
			{
				_steeringEffectsSeatOfPantsMaximumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsMaximumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsMaximumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsMaximumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants algorithm

	private SteeringEffects.SeatOfPantsAlgorithm _steeringEffectsSeatOfPantsAlgorithm = SteeringEffects.SeatOfPantsAlgorithm.YVelocityOverXVelocity;

	public SteeringEffects.SeatOfPantsAlgorithm SteeringEffectsSeatOfPantsAlgorithm
	{
		get => _steeringEffectsSeatOfPantsAlgorithm;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsAlgorithm )
			{
				_steeringEffectsSeatOfPantsAlgorithm = value;

				OnPropertyChanged();

				SteeringEffectsSeatOfPantsMinimumThreshold = _steeringEffectsSeatOfPantsMinimumThreshold;
				SteeringEffectsSeatOfPantsMaximumThreshold = _steeringEffectsSeatOfPantsMaximumThreshold;
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsAlgorithmContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Seat-of-pants wheel vibration pattern

	private RacingWheel.VibrationPattern _steeringEffectsSeatOfPantsWheelVibrationPattern = RacingWheel.VibrationPattern.None;

	public RacingWheel.VibrationPattern SteeringEffectsSeatOfPantsWheelVibrationPattern
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationPattern;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelVibrationPattern )
			{
				_steeringEffectsSeatOfPantsWheelVibrationPattern = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelVibrationPatternContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Seat-of-pants wheel vibration strength

	private float _steeringEffectsSeatOfPantsWheelVibrationStrength = 0.1f;

	public float SteeringEffectsSeatOfPantsWheelVibrationStrength
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationStrength;

		set
		{
			value = Math.Clamp( value, 0f, 0.3f );

			if ( value != _steeringEffectsSeatOfPantsWheelVibrationStrength )
			{
				_steeringEffectsSeatOfPantsWheelVibrationStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsWheelVibrationStrengthString();
		}
	}

	private string _steeringEffectsSeatOfPantsWheelVibrationStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelVibrationStrengthString
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationStrengthString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelVibrationStrengthString )
			{
				_steeringEffectsSeatOfPantsWheelVibrationStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsWheelVibrationStrengthString()
	{
		if ( _steeringEffectsSeatOfPantsWheelVibrationStrength == 0f )
		{
			SteeringEffectsSeatOfPantsWheelVibrationStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsSeatOfPantsWheelVibrationStrength;

			SteeringEffectsSeatOfPantsWheelVibrationStrengthString = $"{_steeringEffectsSeatOfPantsWheelVibrationStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelVibrationStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants wheel vibration minimum frequency

	private float _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequency = 15f;

	public float SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequency )
			{
				_steeringEffectsSeatOfPantsWheelVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString = $"{_steeringEffectsSeatOfPantsWheelVibrationMinimumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
		}
	}

	private string _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString )
			{
				_steeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants wheel vibration maximum frequency

	private float _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequency = 50f;

	public float SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequency )
			{
				_steeringEffectsSeatOfPantsWheelVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString = $"{_steeringEffectsSeatOfPantsWheelVibrationMaximumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
		}
	}

	private string _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString )
			{
				_steeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants wheel vibration curve

	private float _steeringEffectsSeatOfPantsWheelVibrationCurve = 0.25f;

	public float SteeringEffectsSeatOfPantsWheelVibrationCurve
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsSeatOfPantsWheelVibrationCurve )
			{
				_steeringEffectsSeatOfPantsWheelVibrationCurve = value;

				OnPropertyChanged();
			}

			if ( _steeringEffectsSeatOfPantsWheelVibrationCurve == 0f )
			{
				SteeringEffectsSeatOfPantsWheelVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				SteeringEffectsSeatOfPantsWheelVibrationCurveString = $"{_steeringEffectsSeatOfPantsWheelVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _steeringEffectsSeatOfPantsWheelVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelVibrationCurveString
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelVibrationCurveString )
			{
				_steeringEffectsSeatOfPantsWheelVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelVibrationCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants wheel constant force direction

	private RacingWheel.ConstantForceDirection _steeringEffectsSeatOfPantsWheelConstantForceDirection = RacingWheel.ConstantForceDirection.IncreaseForce;

	public RacingWheel.ConstantForceDirection SteeringEffectsSeatOfPantsWheelConstantForceDirection
	{
		get => _steeringEffectsSeatOfPantsWheelConstantForceDirection;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelConstantForceDirection )
			{
				_steeringEffectsSeatOfPantsWheelConstantForceDirection = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelConstantForceDirectionContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Seat-of-pants wheel constant force strength

	private float _steeringEffectsSeatOfPantsWheelConstantForceStrength = 0.1f;

	public float SteeringEffectsSeatOfPantsWheelConstantForceStrength
	{
		get => _steeringEffectsSeatOfPantsWheelConstantForceStrength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _steeringEffectsSeatOfPantsWheelConstantForceStrength )
			{
				_steeringEffectsSeatOfPantsWheelConstantForceStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsWheelConstantForceStrengthString();
		}
	}

	private string _steeringEffectsSeatOfPantsWheelConstantForceStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelConstantForceStrengthString
	{
		get => _steeringEffectsSeatOfPantsWheelConstantForceStrengthString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelConstantForceStrengthString )
			{
				_steeringEffectsSeatOfPantsWheelConstantForceStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsWheelConstantForceStrengthString()
	{
		if ( _steeringEffectsSeatOfPantsWheelConstantForceStrength == 0f )
		{
			SteeringEffectsSeatOfPantsWheelConstantForceStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsSeatOfPantsWheelConstantForceStrength;

			SteeringEffectsSeatOfPantsWheelConstantForceStrengthString = $"{_steeringEffectsSeatOfPantsWheelConstantForceStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelConstantForceStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelConstantForceStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelConstantForceStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - SeatOfPants wheel constant force curve

	private float _steeringEffectsSeatOfPantsWheelConstantForceCurve = 0.25f;

	public float SteeringEffectsSeatOfPantsWheelConstantForceCurve
	{
		get => _steeringEffectsSeatOfPantsWheelConstantForceCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsSeatOfPantsWheelConstantForceCurve )
			{
				_steeringEffectsSeatOfPantsWheelConstantForceCurve = value;

				OnPropertyChanged();
			}

			if ( _steeringEffectsSeatOfPantsWheelConstantForceCurve == 0f )
			{
				SteeringEffectsSeatOfPantsWheelConstantForceCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				SteeringEffectsSeatOfPantsWheelConstantForceCurveString = $"{_steeringEffectsSeatOfPantsWheelConstantForceCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _steeringEffectsSeatOfPantsWheelConstantForceCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelConstantForceCurveString
	{
		get => _steeringEffectsSeatOfPantsWheelConstantForceCurveString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelConstantForceCurveString )
			{
				_steeringEffectsSeatOfPantsWheelConstantForceCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelConstantForceCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelConstantForceCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelConstantForceCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants pedal vibration minimum frequency

	private float _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency = 0.1f;

	public float SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency )
			{
				_steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString();
		}
	}

	private string _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString )
			{
				_steeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency ) );

		SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString = $"{_steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants pedal vibration maximum frequency

	private float _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency = 1f;

	public float SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency )
			{
				_steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString();
		}
	}

	private string _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString )
			{
				_steeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency ) );

		SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString = $"{_steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants pedal vibration curve

	private float _steeringEffectsSeatOfPantsPedalVibrationCurve = 0f;

	public float SteeringEffectsSeatOfPantsPedalVibrationCurve
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsSeatOfPantsPedalVibrationCurve )
			{
				_steeringEffectsSeatOfPantsPedalVibrationCurve = value;

				OnPropertyChanged();
			}

			if ( _steeringEffectsSeatOfPantsPedalVibrationCurve == 0f )
			{
				SteeringEffectsSeatOfPantsPedalVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				SteeringEffectsSeatOfPantsPedalVibrationCurveString = $"{_steeringEffectsSeatOfPantsPedalVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _steeringEffectsSeatOfPantsPedalVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsPedalVibrationCurveString
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsPedalVibrationCurveString )
			{
				_steeringEffectsSeatOfPantsPedalVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsPedalVibrationCurveContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Show Grip-O-Meter window

	private bool _steeringEffectsShowGripOMeterWindow = false;

	public bool SteeringEffectsShowGripOMeterWindow
	{
		get => _steeringEffectsShowGripOMeterWindow;

		set
		{
			if ( value != _steeringEffectsShowGripOMeterWindow )
			{
				_steeringEffectsShowGripOMeterWindow = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGripOMeterWindowVisibility();
		}
	}

	#endregion

	#region Steering effects - Show Grip-O-Meter title

	private bool _steeringEffectsShowGripOMeterTitle = true;

	public bool SteeringEffectsShowGripOMeterTitle
	{
		get => _steeringEffectsShowGripOMeterTitle;

		set
		{
			if ( value != _steeringEffectsShowGripOMeterTitle )
			{
				_steeringEffectsShowGripOMeterTitle = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGripOMeterWindowVisibility();
		}
	}

	#endregion

	#region Steering effects - Make Grip-O-Meter draggable

	private bool _steeringEffectsMakeGripOMeterDraggable = false;

	public bool SteeringEffectsMakeGripOMeterDraggable
	{
		get => _steeringEffectsMakeGripOMeterDraggable;

		set
		{
			if ( value != _steeringEffectsMakeGripOMeterDraggable )
			{
				_steeringEffectsMakeGripOMeterDraggable = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGripOMeterWindowVisibility();
		}
	}

	#endregion

	#region Steering effects - Grip-O-Meter window position

	private Rectangle _steeringEffectsGripOMeterWindowPosition = Rectangle.Empty;

	public Rectangle SteeringEffectsGripOMeterWindowPosition
	{
		get => _steeringEffectsGripOMeterWindowPosition;

		set
		{
			if ( value != _steeringEffectsGripOMeterWindowPosition )
			{
				_steeringEffectsGripOMeterWindowPosition = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Steering effects - Grip-O-Meter window scale

	private float _steeringEffectsGripOMeterWindowScale = 1f;

	public float SteeringEffectsGripOMeterWindowScale
	{
		get => _steeringEffectsGripOMeterWindowScale;

		set
		{
			value = Math.Clamp( value, 0.5f, 2f );

			if ( value != _steeringEffectsGripOMeterWindowScale )
			{
				_steeringEffectsGripOMeterWindowScale = value;

				OnPropertyChanged();
			}

			SteeringEffectsGripOMeterWindowScaleString = $"{_steeringEffectsGripOMeterWindowScale * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _steeringEffectsGripOMeterWindowScaleString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsGripOMeterWindowScaleString
	{
		get => _steeringEffectsGripOMeterWindowScaleString;

		set
		{
			if ( value != _steeringEffectsGripOMeterWindowScaleString )
			{
				_steeringEffectsGripOMeterWindowScaleString = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Pedals - Enabled

	private bool _pedalsEnabled = false;

	public bool PedalsEnabled
	{
		get => _pedalsEnabled;

		set
		{
			if ( value != _pedalsEnabled )
			{
				_pedalsEnabled = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			if ( !app.SettingsFile.PauseSerialization )
			{
				app.Pedals.Refresh();
			}
		}
	}

	#endregion

	#region Pedals - Minimum frequency

	private float _pedalsMinimumFrequency = 0f;

	public float PedalsMinimumFrequency
	{
		get => _pedalsMinimumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _pedalsMinimumFrequency )
			{
				_pedalsMinimumFrequency = value;

				OnPropertyChanged();

				PedalsMaximumFrequency = MathF.Max( PedalsMaximumFrequency, _pedalsMinimumFrequency );

				UpdateRelatedPedalSettings();
			}

			PedalsMinimumFrequencyString = $"{_pedalsMinimumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
		}
	}

	private string _pedalsMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsMinimumFrequencyString
	{
		get => _pedalsMinimumFrequencyString;

		set
		{
			if ( value != _pedalsMinimumFrequencyString )
			{
				_pedalsMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsMinimumFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Maximum frequency

	private float _pedalsMaximumFrequency = 50f;

	public float PedalsMaximumFrequency
	{
		get => _pedalsMaximumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _pedalsMaximumFrequency )
			{
				_pedalsMaximumFrequency = value;

				OnPropertyChanged();

				PedalsMinimumFrequency = MathF.Min( PedalsMinimumFrequency, _pedalsMaximumFrequency );

				UpdateRelatedPedalSettings();
			}

			PedalsMaximumFrequencyString = $"{_pedalsMaximumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
		}
	}

	private string _pedalsMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsMaximumFrequencyString
	{
		get => _pedalsMaximumFrequencyString;

		set
		{
			if ( value != _pedalsMaximumFrequencyString )
			{
				_pedalsMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsMaximumFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Frequency curve

	private float _pedalsFrequencyCurve = 0.25f;

	public float PedalsFrequencyCurve
	{
		get => _pedalsFrequencyCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _pedalsFrequencyCurve )
			{
				_pedalsFrequencyCurve = value;

				OnPropertyChanged();
			}

			if ( _pedalsFrequencyCurve == 0f )
			{
				PedalsFrequencyCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				PedalsFrequencyCurveString = $"{_pedalsFrequencyCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _pedalsFrequencyCurveString = string.Empty;

	[XmlIgnore]
	public string PedalsFrequencyCurveString
	{
		get => _pedalsFrequencyCurveString;

		set
		{
			if ( value != _pedalsFrequencyCurveString )
			{
				_pedalsFrequencyCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsFrequencyCurveContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsFrequencyCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsFrequencyCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Minimum Amplitude

	private float _pedalsMinimumAmplitude = 0f;

	public float PedalsMinimumAmplitude
	{
		get => _pedalsMinimumAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsMinimumAmplitude )
			{
				_pedalsMinimumAmplitude = value;

				OnPropertyChanged();

				PedalsMaximumAmplitude = MathF.Max( PedalsMaximumAmplitude, _pedalsMinimumAmplitude );
			}

			PedalsMinimumAmplitudeString = $"{_pedalsMinimumAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsMinimumAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsMinimumAmplitudeString
	{
		get => _pedalsMinimumAmplitudeString;

		set
		{
			if ( value != _pedalsMinimumAmplitudeString )
			{
				_pedalsMinimumAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsMinimumAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsMinimumAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsMinimumAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Maximum Amplitude

	private float _pedalsMaximumAmplitude = 1f;

	public float PedalsMaximumAmplitude
	{
		get => _pedalsMaximumAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsMaximumAmplitude )
			{
				_pedalsMaximumAmplitude = value;

				OnPropertyChanged();

				PedalsMinimumAmplitude = MathF.Min( PedalsMinimumAmplitude, _pedalsMaximumAmplitude );
			}

			PedalsMaximumAmplitudeString = $"{_pedalsMaximumAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsMaximumAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsMaximumAmplitudeString
	{
		get => _pedalsMaximumAmplitudeString;

		set
		{
			if ( value != _pedalsMaximumAmplitudeString )
			{
				_pedalsMaximumAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsMaximumAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsMaximumAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsMaximumAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Amplitude curve

	private float _pedalsAmplitudeCurve = 0f;

	public float PedalsAmplitudeCurve
	{
		get => _pedalsAmplitudeCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _pedalsAmplitudeCurve )
			{
				_pedalsAmplitudeCurve = value;

				OnPropertyChanged();
			}

			if ( _pedalsAmplitudeCurve == 0f )
			{
				PedalsAmplitudeCurveString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				PedalsAmplitudeCurveString = $"{_pedalsAmplitudeCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _pedalsAmplitudeCurveString = string.Empty;

	[XmlIgnore]
	public string PedalsAmplitudeCurveString
	{
		get => _pedalsAmplitudeCurveString;

		set
		{
			if ( value != _pedalsAmplitudeCurveString )
			{
				_pedalsAmplitudeCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsAmplitudeCurveContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsAmplitudeCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsAmplitudeCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch effect 1

	private Pedals.Effect _pedalsClutchEffect1 = Pedals.Effect.GearChange;

	public Pedals.Effect PedalsClutchEffect1
	{
		get => _pedalsClutchEffect1;

		set
		{
			if ( value != _pedalsClutchEffect1 )
			{
				_pedalsClutchEffect1 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchEffect1ContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Clutch strength 1

	private float _pedalsClutchStrength1 = 1f;

	public float PedalsClutchStrength1
	{
		get => _pedalsClutchStrength1;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsClutchStrength1 )
			{
				_pedalsClutchStrength1 = value;

				OnPropertyChanged();
			}

			PedalsClutchStrength1String = $"{_pedalsClutchStrength1 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsClutchStrength1String = string.Empty;

	[XmlIgnore]
	public string PedalsClutchStrength1String
	{
		get => _pedalsClutchStrength1String;

		set
		{
			if ( value != _pedalsClutchStrength1String )
			{
				_pedalsClutchStrength1String = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchStrength1ContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsClutchStrength1PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchStrength1MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch test 1

	public ButtonMappings PedalsClutchTest1ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch effect 2

	private Pedals.Effect _pedalsClutchEffect2 = Pedals.Effect.ClutchSlip;

	public Pedals.Effect PedalsClutchEffect2
	{
		get => _pedalsClutchEffect2;

		set
		{
			if ( value != _pedalsClutchEffect2 )
			{
				_pedalsClutchEffect2 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchEffect2ContextSwitches { get => PedalsClutchEffect1ContextSwitches; set => PedalsClutchEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Clutch strength 2

	private float _pedalsClutchStrength2 = 1f;

	public float PedalsClutchStrength2
	{
		get => _pedalsClutchStrength2;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsClutchStrength2 )
			{
				_pedalsClutchStrength2 = value;

				OnPropertyChanged();
			}

			PedalsClutchStrength2String = $"{_pedalsClutchStrength2 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsClutchStrength2String = string.Empty;

	[XmlIgnore]
	public string PedalsClutchStrength2String
	{
		get => _pedalsClutchStrength2String;

		set
		{
			if ( value != _pedalsClutchStrength2String )
			{
				_pedalsClutchStrength2String = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchStrength2ContextSwitches { get => PedalsClutchStrength1ContextSwitches; set => PedalsClutchStrength1ContextSwitches = value; }
	public ButtonMappings PedalsClutchStrength2PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchStrength2MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch test 2

	public ButtonMappings PedalsClutchTest2ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch effect 3

	private Pedals.Effect _pedalsClutchEffect3 = Pedals.Effect.None;

	public Pedals.Effect PedalsClutchEffect3
	{
		get => _pedalsClutchEffect3;

		set
		{
			if ( value != _pedalsClutchEffect3 )
			{
				_pedalsClutchEffect3 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchEffect3ContextSwitches { get => PedalsClutchEffect1ContextSwitches; set => PedalsClutchEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Clutch effect 3 strength

	private float _pedalsClutchStrength3 = 1f;

	public float PedalsClutchStrength3
	{
		get => _pedalsClutchStrength3;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsClutchStrength3 )
			{
				_pedalsClutchStrength3 = value;

				OnPropertyChanged();
			}

			PedalsClutchStrength3String = $"{_pedalsClutchStrength3 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsClutchStrength3String = string.Empty;

	[XmlIgnore]
	public string PedalsClutchStrength3String
	{
		get => _pedalsClutchStrength3String;

		set
		{
			if ( value != _pedalsClutchStrength3String )
			{
				_pedalsClutchStrength3String = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchStrength3ContextSwitches { get => PedalsClutchStrength1ContextSwitches; set => PedalsClutchStrength1ContextSwitches = value; }
	public ButtonMappings PedalsClutchStrength3PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchStrength3MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch test 3

	public ButtonMappings PedalsClutchTest3ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake effect 1

	private Pedals.Effect _pedalsBrakeEffect1 = Pedals.Effect.ABSEngaged;

	public Pedals.Effect PedalsBrakeEffect1
	{
		get => _pedalsBrakeEffect1;

		set
		{
			if ( value != _pedalsBrakeEffect1 )
			{
				_pedalsBrakeEffect1 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeEffect1ContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Brake strength 1

	private float _pedalsBrakeStrength1 = 1f;

	public float PedalsBrakeStrength1
	{
		get => _pedalsBrakeStrength1;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsBrakeStrength1 )
			{
				_pedalsBrakeStrength1 = value;

				OnPropertyChanged();
			}

			PedalsBrakeStrength1String = $"{_pedalsBrakeStrength1 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsBrakeStrength1String = string.Empty;

	[XmlIgnore]
	public string PedalsBrakeStrength1String
	{
		get => _pedalsBrakeStrength1String;

		set
		{
			if ( value != _pedalsBrakeStrength1String )
			{
				_pedalsBrakeStrength1String = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeStrength1ContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsBrakeStrength1PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsBrakeStrength1MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake test 1

	public ButtonMappings PedalsBrakeTest1ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake effect 2

	private Pedals.Effect _pedalsBrakeEffect2 = Pedals.Effect.WheelLock;

	public Pedals.Effect PedalsBrakeEffect2
	{
		get => _pedalsBrakeEffect2;

		set
		{
			if ( value != _pedalsBrakeEffect2 )
			{
				_pedalsBrakeEffect2 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeEffect2ContextSwitches { get => PedalsBrakeEffect1ContextSwitches; set => PedalsBrakeEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Brake strength 2

	private float _pedalsBrakeStrength2 = 1f;

	public float PedalsBrakeStrength2
	{
		get => _pedalsBrakeStrength2;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsBrakeStrength2 )
			{
				_pedalsBrakeStrength2 = value;

				OnPropertyChanged();
			}

			PedalsBrakeStrength2String = $"{_pedalsBrakeStrength2 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsBrakeStrength2String = string.Empty;

	[XmlIgnore]
	public string PedalsBrakeStrength2String
	{
		get => _pedalsBrakeStrength2String;

		set
		{
			if ( value != _pedalsBrakeStrength2String )
			{
				_pedalsBrakeStrength2String = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeStrength2ContextSwitches { get => PedalsBrakeStrength1ContextSwitches; set => PedalsBrakeStrength1ContextSwitches = value; }
	public ButtonMappings PedalsBrakeStrength2PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsBrakeStrength2MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake test 2

	public ButtonMappings PedalsBrakeTest2ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake effect 3

	private Pedals.Effect _pedalsBrakeEffect3 = Pedals.Effect.UndersteerEffect;

	public Pedals.Effect PedalsBrakeEffect3
	{
		get => _pedalsBrakeEffect3;

		set
		{
			if ( value != _pedalsBrakeEffect3 )
			{
				_pedalsBrakeEffect3 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeEffect3ContextSwitches { get => PedalsBrakeEffect1ContextSwitches; set => PedalsBrakeEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Brake effect 3 strength

	private float _pedalsBrakeStrength3 = 1f;

	public float PedalsBrakeStrength3
	{
		get => _pedalsBrakeStrength3;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsBrakeStrength3 )
			{
				_pedalsBrakeStrength3 = value;

				OnPropertyChanged();
			}

			PedalsBrakeStrength3String = $"{_pedalsBrakeStrength3 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsBrakeStrength3String = string.Empty;

	[XmlIgnore]
	public string PedalsBrakeStrength3String
	{
		get => _pedalsBrakeStrength3String;

		set
		{
			if ( value != _pedalsBrakeStrength3String )
			{
				_pedalsBrakeStrength3String = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeStrength3ContextSwitches { get => PedalsBrakeStrength1ContextSwitches; set => PedalsBrakeStrength1ContextSwitches = value; }
	public ButtonMappings PedalsBrakeStrength3PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsBrakeStrength3MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake test 3

	public ButtonMappings PedalsBrakeTest3ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle effect 1

	private Pedals.Effect _pedalsThrottleEffect1 = Pedals.Effect.WheelSpin;

	public Pedals.Effect PedalsThrottleEffect1
	{
		get => _pedalsThrottleEffect1;

		set
		{
			if ( value != _pedalsThrottleEffect1 )
			{
				_pedalsThrottleEffect1 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleEffect1ContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Throttle strength 1

	private float _pedalsThrottleStrength1 = 1f;

	public float PedalsThrottleStrength1
	{
		get => _pedalsThrottleStrength1;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsThrottleStrength1 )
			{
				_pedalsThrottleStrength1 = value;

				OnPropertyChanged();
			}

			PedalsThrottleStrength1String = $"{_pedalsThrottleStrength1 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsThrottleStrength1String = string.Empty;

	[XmlIgnore]
	public string PedalsThrottleStrength1String
	{
		get => _pedalsThrottleStrength1String;

		set
		{
			if ( value != _pedalsThrottleStrength1String )
			{
				_pedalsThrottleStrength1String = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleStrength1ContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsThrottleStrength1PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsThrottleStrength1MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle test 1

	public ButtonMappings PedalsThrottleTest1ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle effect 2

	private Pedals.Effect _pedalsThrottleEffect2 = Pedals.Effect.RPM;

	public Pedals.Effect PedalsThrottleEffect2
	{
		get => _pedalsThrottleEffect2;

		set
		{
			if ( value != _pedalsThrottleEffect2 )
			{
				_pedalsThrottleEffect2 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleEffect2ContextSwitches { get => PedalsThrottleEffect1ContextSwitches; set => PedalsThrottleEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Throttle strength 2

	private float _pedalsThrottleStrength2 = 1f;

	public float PedalsThrottleStrength2
	{
		get => _pedalsThrottleStrength2;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsThrottleStrength2 )
			{
				_pedalsThrottleStrength2 = value;

				OnPropertyChanged();
			}

			PedalsThrottleStrength2String = $"{_pedalsThrottleStrength2 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsThrottleStrength2String = string.Empty;

	[XmlIgnore]
	public string PedalsThrottleStrength2String
	{
		get => _pedalsThrottleStrength2String;

		set
		{
			if ( value != _pedalsThrottleStrength2String )
			{
				_pedalsThrottleStrength2String = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleStrength2ContextSwitches { get => PedalsThrottleStrength1ContextSwitches; set => PedalsThrottleStrength1ContextSwitches = value; }
	public ButtonMappings PedalsThrottleStrength2PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsThrottleStrength2MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle test 2

	public ButtonMappings PedalsThrottleTest2ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle effect 3

	private Pedals.Effect _pedalsThrottleEffect3 = Pedals.Effect.OversteerEffect;

	public Pedals.Effect PedalsThrottleEffect3
	{
		get => _pedalsThrottleEffect3;

		set
		{
			if ( value != _pedalsThrottleEffect3 )
			{
				_pedalsThrottleEffect3 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleEffect3ContextSwitches { get => PedalsThrottleEffect1ContextSwitches; set => PedalsThrottleEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Throttle effect 3 strength

	private float _pedalsThrottleStrength3 = 1f;

	public float PedalsThrottleStrength3
	{
		get => _pedalsThrottleStrength3;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsThrottleStrength3 )
			{
				_pedalsThrottleStrength3 = value;

				OnPropertyChanged();
			}

			PedalsThrottleStrength3String = $"{_pedalsThrottleStrength3 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsThrottleStrength3String = string.Empty;

	[XmlIgnore]
	public string PedalsThrottleStrength3String
	{
		get => _pedalsThrottleStrength3String;

		set
		{
			if ( value != _pedalsThrottleStrength3String )
			{
				_pedalsThrottleStrength3String = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleStrength3ContextSwitches { get => PedalsThrottleStrength1ContextSwitches; set => PedalsThrottleStrength1ContextSwitches = value; }
	public ButtonMappings PedalsThrottleStrength3PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsThrottleStrength3MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle test 3

	public ButtonMappings PedalsThrottleTest3ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into gear frequency

	private float _pedalsShiftIntoGearFrequency = 0.3f;

	public float PedalsShiftIntoGearFrequency
	{
		get => _pedalsShiftIntoGearFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsShiftIntoGearFrequency )
			{
				_pedalsShiftIntoGearFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsShiftIntoGearFrequencyString();
		}
	}

	private string _pedalsShiftIntoGearFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoGearFrequencyString
	{
		get => _pedalsShiftIntoGearFrequencyString;

		set
		{
			if ( value != _pedalsShiftIntoGearFrequencyString )
			{
				_pedalsShiftIntoGearFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsShiftIntoGearFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsShiftIntoGearFrequency ) );

		PedalsShiftIntoGearFrequencyString = $"{_pedalsShiftIntoGearFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsShiftIntoGearFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoGearFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoGearFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into gear amplitude

	private float _pedalsShiftIntoGearAmplitude = 1f;

	public float PedalsShiftIntoGearAmplitude
	{
		get => _pedalsShiftIntoGearAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsShiftIntoGearAmplitude )
			{
				_pedalsShiftIntoGearAmplitude = value;

				OnPropertyChanged();
			}

			PedalsShiftIntoGearAmplitudeString = $"{_pedalsShiftIntoGearAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsShiftIntoGearAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoGearAmplitudeString
	{
		get => _pedalsShiftIntoGearAmplitudeString;

		set
		{
			if ( value != _pedalsShiftIntoGearAmplitudeString )
			{
				_pedalsShiftIntoGearAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsShiftIntoGearAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoGearAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoGearAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into gear duration

	private float _pedalsShiftIntoGearDuration = 0.1f;

	public float PedalsShiftIntoGearDuration
	{
		get => _pedalsShiftIntoGearDuration;

		set
		{
			value = Math.Clamp( value, 0.05f, 1f );

			if ( value != _pedalsShiftIntoGearDuration )
			{
				_pedalsShiftIntoGearDuration = value;

				OnPropertyChanged();
			}

			PedalsShiftIntoGearDurationString = $"{_pedalsShiftIntoGearDuration:F2} {DataContext.Instance.Localization[ "SecondsUnits" ]}";
		}
	}

	private string _pedalsShiftIntoGearDurationString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoGearDurationString
	{
		get => _pedalsShiftIntoGearDurationString;

		set
		{
			if ( value != _pedalsShiftIntoGearDurationString )
			{
				_pedalsShiftIntoGearDurationString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsShiftIntoGearDurationContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoGearDurationPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoGearDurationMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into neutral frequency

	private float _pedalsShiftIntoNeutralFrequency = 0.7f;

	public float PedalsShiftIntoNeutralFrequency
	{
		get => _pedalsShiftIntoNeutralFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsShiftIntoNeutralFrequency )
			{
				_pedalsShiftIntoNeutralFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsShiftIntoNeutralFrequencyString();
		}
	}

	private string _pedalsShiftIntoNeutralFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoNeutralFrequencyString
	{
		get => _pedalsShiftIntoNeutralFrequencyString;

		set
		{
			if ( value != _pedalsShiftIntoNeutralFrequencyString )
			{
				_pedalsShiftIntoNeutralFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsShiftIntoNeutralFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsShiftIntoNeutralFrequency ) );

		PedalsShiftIntoNeutralFrequencyString = $"{_pedalsShiftIntoNeutralFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsShiftIntoNeutralFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoNeutralFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoNeutralFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into neutral amplitude

	private float _pedalsShiftIntoNeutralAmplitude = 0.75f;

	public float PedalsShiftIntoNeutralAmplitude
	{
		get => _pedalsShiftIntoNeutralAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsShiftIntoNeutralAmplitude )
			{
				_pedalsShiftIntoNeutralAmplitude = value;

				OnPropertyChanged();
			}

			PedalsShiftIntoNeutralAmplitudeString = $"{_pedalsShiftIntoNeutralAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsShiftIntoNeutralAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoNeutralAmplitudeString
	{
		get => _pedalsShiftIntoNeutralAmplitudeString;

		set
		{
			if ( value != _pedalsShiftIntoNeutralAmplitudeString )
			{
				_pedalsShiftIntoNeutralAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsShiftIntoNeutralAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoNeutralAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoNeutralAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into neutral duration

	private float _pedalsShiftIntoNeutralDuration = 0.05f;

	public float PedalsShiftIntoNeutralDuration
	{
		get => _pedalsShiftIntoNeutralDuration;

		set
		{
			value = Math.Clamp( value, 0.05f, 1f );

			if ( value != _pedalsShiftIntoNeutralDuration )
			{
				_pedalsShiftIntoNeutralDuration = value;

				OnPropertyChanged();
			}

			PedalsShiftIntoNeutralDurationString = $"{_pedalsShiftIntoNeutralDuration:F2} {DataContext.Instance.Localization[ "SecondsUnits" ]}";
		}
	}

	private string _pedalsShiftIntoNeutralDurationString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoNeutralDurationString
	{
		get => _pedalsShiftIntoNeutralDurationString;

		set
		{
			if ( value != _pedalsShiftIntoNeutralDurationString )
			{
				_pedalsShiftIntoNeutralDurationString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsShiftIntoNeutralDurationContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoNeutralDurationPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoNeutralDurationMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - ABS engaged frequency

	private float _pedalsABSEngagedFrequency = 0.5f;

	public float PedalsABSEngagedFrequency
	{
		get => _pedalsABSEngagedFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsABSEngagedFrequency )
			{
				_pedalsABSEngagedFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsABSEngagedFrequencyString();
		}
	}

	private string _pedalsABSEngagedFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsABSEngagedFrequencyString
	{
		get => _pedalsABSEngagedFrequencyString;

		set
		{
			if ( value != _pedalsABSEngagedFrequencyString )
			{
				_pedalsABSEngagedFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsABSEngagedFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsABSEngagedFrequency ) );

		PedalsABSEngagedFrequencyString = $"{_pedalsABSEngagedFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsABSEngagedFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsABSEngagedFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsABSEngagedFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - ABS engaged amplitude

	private float _pedalsABSEngagedAmplitude = 1f;

	public float PedalsABSEngagedAmplitude
	{
		get => _pedalsABSEngagedAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsABSEngagedAmplitude )
			{
				_pedalsABSEngagedAmplitude = value;

				OnPropertyChanged();
			}

			PedalsABSEngagedAmplitudeString = $"{_pedalsABSEngagedAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsABSEngagedAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsABSEngagedAmplitudeString
	{
		get => _pedalsABSEngagedAmplitudeString;

		set
		{
			if ( value != _pedalsABSEngagedAmplitudeString )
			{
				_pedalsABSEngagedAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsABSEngagedAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsABSEngagedAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsABSEngagedAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - ABS engaged fade with brake enabled

	private bool _pedalsABSEngagedFadeWithBrakeEnabled = true;

	public bool PedalsABSEngagedFadeWithBrakeEnabled
	{
		get => _pedalsABSEngagedFadeWithBrakeEnabled;

		set
		{
			if ( value != _pedalsABSEngagedFadeWithBrakeEnabled )
			{
				_pedalsABSEngagedFadeWithBrakeEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsABSEngagedFadeWithBrakeEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Starting RPM

	private float _pedalsStartingRPM = 1f;

	public float PedalsStartingRPM
	{
		get => _pedalsStartingRPM;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsStartingRPM )
			{
				_pedalsStartingRPM = value;

				OnPropertyChanged();
			}

			PedalsStartingRPMString = $"{_pedalsStartingRPM * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsStartingRPMString = string.Empty;

	[XmlIgnore]
	public string PedalsStartingRPMString
	{
		get => _pedalsStartingRPMString;

		set
		{
			if ( value != _pedalsStartingRPMString )
			{
				_pedalsStartingRPMString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsStartingRPMContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsStartingRPMPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsStartingRPMMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - RPM vibrate in top gear enabled

	private bool _pedalsRPMVibrateInTopGearEnabled = false;

	public bool PedalsRPMVibrateInTopGearEnabled
	{
		get => _pedalsRPMVibrateInTopGearEnabled;

		set
		{
			if ( value != _pedalsRPMVibrateInTopGearEnabled )
			{
				_pedalsRPMVibrateInTopGearEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsRPMVibrateInTopGearEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - RPM fade with throttle enabled

	private bool _pedalsRPMFadeWithThrottleEnabled = true;

	public bool PedalsRPMFadeWithThrottleEnabled
	{
		get => _pedalsRPMFadeWithThrottleEnabled;

		set
		{
			if ( value != _pedalsRPMFadeWithThrottleEnabled )
			{
				_pedalsRPMFadeWithThrottleEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsRPMFadeWithThrottleEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Wheel lock frequency

	private float _pedalsWheelLockFrequency = 0.2f;

	public float PedalsWheelLockFrequency
	{
		get => _pedalsWheelLockFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsWheelLockFrequency )
			{
				_pedalsWheelLockFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsWheelLockFrequencyString();
		}
	}

	private string _pedalsWheelLockFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsWheelLockFrequencyString
	{
		get => _pedalsWheelLockFrequencyString;

		set
		{
			if ( value != _pedalsWheelLockFrequencyString )
			{
				_pedalsWheelLockFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsWheelLockFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsWheelLockFrequency ) );

		PedalsWheelLockFrequencyString = $"{_pedalsWheelLockFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsWheelLockFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsWheelLockFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsWheelLockFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Wheel lock sensitivity

	private float _pedalsWheelLockSensitivity = 0.95f;

	public float PedalsWheelLockSensitivity
	{
		get => _pedalsWheelLockSensitivity;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsWheelLockSensitivity )
			{
				_pedalsWheelLockSensitivity = value;

				OnPropertyChanged();
			}

			PedalsWheelLockSensitivityString = $"{_pedalsWheelLockSensitivity * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsWheelLockSensitivityString = string.Empty;

	[XmlIgnore]
	public string PedalsWheelLockSensitivityString
	{
		get => _pedalsWheelLockSensitivityString;

		set
		{
			if ( value != _pedalsWheelLockSensitivityString )
			{
				_pedalsWheelLockSensitivityString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsWheelLockSensitivityContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsWheelLockSensitivityPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsWheelLockSensitivityMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Wheel lock fade with brake enabled

	private bool _pedalsWheelLockFadeWithBrakeEnabled = true;

	public bool PedalsWheelLockFadeWithBrakeEnabled
	{
		get => _pedalsWheelLockFadeWithBrakeEnabled;

		set
		{
			if ( value != _pedalsWheelLockFadeWithBrakeEnabled )
			{
				_pedalsWheelLockFadeWithBrakeEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsWheelLockFadeWithBrakeEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Wheel spin frequency

	private float _pedalsWheelSpinFrequency = 1f;

	public float PedalsWheelSpinFrequency
	{
		get => _pedalsWheelSpinFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsWheelSpinFrequency )
			{
				_pedalsWheelSpinFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsWheelSpinFrequencyString();
		}
	}

	private string _pedalsWheelSpinFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsWheelSpinFrequencyString
	{
		get => _pedalsWheelSpinFrequencyString;

		set
		{
			if ( value != _pedalsWheelSpinFrequencyString )
			{
				_pedalsWheelSpinFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsWheelSpinFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsWheelSpinFrequency ) );

		PedalsWheelSpinFrequencyString = $"{_pedalsWheelSpinFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsWheelSpinFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsWheelSpinFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsWheelSpinFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Wheel spin sensitivity

	private float _pedalsWheelSpinSensitivity = 0.95f;

	public float PedalsWheelSpinSensitivity
	{
		get => _pedalsWheelSpinSensitivity;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsWheelSpinSensitivity )
			{
				_pedalsWheelSpinSensitivity = value;

				OnPropertyChanged();
			}

			PedalsWheelSpinSensitivityString = $"{_pedalsWheelSpinSensitivity * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsWheelSpinSensitivityString = string.Empty;

	[XmlIgnore]
	public string PedalsWheelSpinSensitivityString
	{
		get => _pedalsWheelSpinSensitivityString;

		set
		{
			if ( value != _pedalsWheelSpinSensitivityString )
			{
				_pedalsWheelSpinSensitivityString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsWheelSpinSensitivityContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsWheelSpinSensitivityPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsWheelSpinSensitivityMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Wheel spin fade with throttle enabled

	private bool _pedalsWheelSpinFadeWithThrottleEnabled = true;

	public bool PedalsWheelSpinFadeWithThrottleEnabled
	{
		get => _pedalsWheelSpinFadeWithThrottleEnabled;

		set
		{
			if ( value != _pedalsWheelSpinFadeWithThrottleEnabled )
			{
				_pedalsWheelSpinFadeWithThrottleEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsWheelSpinFadeWithThrottleEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Clutch slip start

	private float _pedalsClutchSlipStart = 0.25f;

	public float PedalsClutchSlipStart
	{
		get => _pedalsClutchSlipStart;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsClutchSlipStart )
			{
				_pedalsClutchSlipStart = value;

				OnPropertyChanged();

				PedalsClutchSlipEnd = MathF.Max( PedalsClutchSlipEnd, _pedalsClutchSlipStart );
			}

			PedalsClutchSlipStartString = $"{_pedalsClutchSlipStart * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsClutchSlipStartString = string.Empty;

	[XmlIgnore]
	public string PedalsClutchSlipStartString
	{
		get => _pedalsClutchSlipStartString;

		set
		{
			if ( value != _pedalsClutchSlipStartString )
			{
				_pedalsClutchSlipStartString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchSlipStartContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsClutchSlipStartPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchSlipStartMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch slip end

	private float _pedalsClutchSlipEnd = 0.75f;

	public float PedalsClutchSlipEnd
	{
		get => _pedalsClutchSlipEnd;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsClutchSlipEnd )
			{
				_pedalsClutchSlipEnd = value;

				OnPropertyChanged();

				PedalsClutchSlipStart = MathF.Min( PedalsClutchSlipStart, _pedalsClutchSlipEnd );
			}

			PedalsClutchSlipEndString = $"{_pedalsClutchSlipEnd * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsClutchSlipEndString = string.Empty;

	[XmlIgnore]
	public string PedalsClutchSlipEndString
	{
		get => _pedalsClutchSlipEndString;

		set
		{
			if ( value != _pedalsClutchSlipEndString )
			{
				_pedalsClutchSlipEndString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchSlipEndContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsClutchSlipEndPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchSlipEndMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch slip frequency

	private float _pedalsClutchSlipFrequency = 1f;

	public float PedalsClutchSlipFrequency
	{
		get => _pedalsClutchSlipFrequency;

		set
		{
			value = Math.Clamp( value, 0.05f, 1f );

			if ( value != _pedalsClutchSlipFrequency )
			{
				_pedalsClutchSlipFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsClutchSlipFrequencyString();
		}
	}

	private string _pedalsClutchSlipFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsClutchSlipFrequencyString
	{
		get => _pedalsClutchSlipFrequencyString;

		set
		{
			if ( value != _pedalsClutchSlipFrequencyString )
			{
				_pedalsClutchSlipFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsClutchSlipFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsClutchSlipFrequency ) );

		PedalsClutchSlipFrequencyString = $"{_pedalsClutchSlipFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsClutchSlipFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsClutchSlipFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchSlipFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Noise damper

	private float _pedalsNoiseDamper = 0.1f;

	public float PedalsNoiseDamper
	{
		get => _pedalsNoiseDamper;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsNoiseDamper )
			{
				_pedalsNoiseDamper = value;

				OnPropertyChanged();
			}

			if ( _pedalsNoiseDamper == 0f )
			{
				PedalsNoiseDamperString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				PedalsNoiseDamperString = $"{_pedalsNoiseDamper * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _pedalsNoiseDamperString = string.Empty;

	[XmlIgnore]
	public string PedalsNoiseDamperString
	{
		get => _pedalsNoiseDamperString;

		set
		{
			if ( value != _pedalsNoiseDamperString )
			{
				_pedalsNoiseDamperString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsNoiseDamperContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsNoiseDamperPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsNoiseDamperMinusButtonMappings { get; set; } = new();

	#endregion

	#region Wind - Connect on startup

	private bool _windConnectOnStartup = false;

	public bool WindConnectOnStartup
	{
		get => _windConnectOnStartup;

		set
		{
			if ( value != _windConnectOnStartup )
			{
				_windConnectOnStartup = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Master wind power

	private float _windMasterWindPower = 1f;

	public float WindMasterWindPower
	{
		get => _windMasterWindPower;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windMasterWindPower )
			{
				_windMasterWindPower = value;

				OnPropertyChanged();
			}

			if ( _windMasterWindPower == 0f )
			{
				WindMasterWindPowerString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				WindMasterWindPowerString = $"{_windMasterWindPower * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _windMasterWindPowerString = string.Empty;

	[XmlIgnore]
	public string WindMasterWindPowerString
	{
		get => _windMasterWindPowerString;

		set
		{
			if ( value != _windMasterWindPowerString )
			{
				_windMasterWindPowerString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches WindMasterWindPowerContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings WindMasterWindPowerPlusButtonMappings { get; set; } = new();
	public ButtonMappings WindMasterWindPowerMinusButtonMappings { get; set; } = new();

	#endregion

	#region Wind - Minimum speed

	private float _windMinimumSpeed = 0f;

	public float WindMinimumSpeed
	{
		get => _windMinimumSpeed;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windMinimumSpeed )
			{
				_windMinimumSpeed = value;

				OnPropertyChanged();
			}

			if ( _windMinimumSpeed == 0f )
			{
				WindMinimumSpeedString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				var app = App.Instance!;

				if ( app.Simulator.DisplayUnits == 0 )
				{
					WindMinimumSpeedString = $"{_windMinimumSpeed * 100f * MathZ.MPSToMPH:F0} {DataContext.Instance.Localization[ "MPHUnits" ]}";
				}
				else
				{
					WindMinimumSpeedString = $"{_windMinimumSpeed * 100f * MathZ.MPSToKPH:F0} {DataContext.Instance.Localization[ "KPHUnits" ]}";
				}
			}
		}
	}

	private string _windMinimumSpeedString = string.Empty;

	[XmlIgnore]
	public string WindMinimumSpeedString
	{
		get => _windMinimumSpeedString;

		set
		{
			if ( value != _windMinimumSpeedString )
			{
				_windMinimumSpeedString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches WindMinimumSpeedContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings WindMinimumSpeedPlusButtonMappings { get; set; } = new();
	public ButtonMappings WindMinimumSpeedMinusButtonMappings { get; set; } = new();

	#endregion

	#region Wind - Curving

	private float _windCurving = 1f;

	public float WindCurving
	{
		get => _windCurving;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windCurving )
			{
				_windCurving = value;

				OnPropertyChanged();
			}

			if ( _windCurving == 0f )
			{
				WindCurvingString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				WindCurvingString = $"{_windCurving * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
		}
	}

	private string _windCurvingString = string.Empty;

	[XmlIgnore]
	public string WindCurvingString
	{
		get => _windCurvingString;

		set
		{
			if ( value != _windCurvingString )
			{
				_windCurvingString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches WindCurvingContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings WindCurvingPlusButtonMappings { get; set; } = new();
	public ButtonMappings WindCurvingMinusButtonMappings { get; set; } = new();

	#endregion

	#region Wind - Speed 1

	private float _windSpeed1 = 0f;

	public float WindSpeed1
	{
		get => _windSpeed1;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windSpeed1 )
			{
				_windSpeed1 = value;

				OnPropertyChanged();

				WindSpeed2 = MathF.Max( WindSpeed2, _windSpeed1 );
			}

			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindSpeed1String = $"{_windSpeed1 * 100f * MathZ.MPSToMPH:F0}";
			}
			else
			{
				WindSpeed1String = $"{_windSpeed1 * 100f * MathZ.MPSToKPH:F0}";
			}
		}
	}

	private string _windSpeed1String = string.Empty;

	[XmlIgnore]
	public string WindSpeed1String
	{
		get => _windSpeed1String;

		set
		{
			if ( value != _windSpeed1String )
			{
				_windSpeed1String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Fan power 1

	private float _windFanPower1 = 0f;

	public float WindFanPower1
	{
		get => _windFanPower1;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower1 )
			{
				_windFanPower1 = value;

				OnPropertyChanged();
			}

			WindFanPower1String = $"{_windFanPower1 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _windFanPower1String = string.Empty;

	[XmlIgnore]
	public string WindFanPower1String
	{
		get => _windFanPower1String;

		set
		{
			if ( value != _windFanPower1String )
			{
				_windFanPower1String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Speed 2

	private float _windSpeed2 = 0.0675f;

	public float WindSpeed2
	{
		get => _windSpeed2;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windSpeed2 )
			{
				_windSpeed2 = value;

				OnPropertyChanged();

				WindSpeed1 = MathF.Min( WindSpeed1, _windSpeed2 );
				WindSpeed3 = MathF.Max( WindSpeed3, _windSpeed2 );
			}

			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindSpeed2String = $"{_windSpeed2 * 100f * MathZ.MPSToMPH:F0}";
			}
			else
			{
				WindSpeed2String = $"{_windSpeed2 * 100f * MathZ.MPSToKPH:F0}";
			}
		}
	}

	private string _windSpeed2String = string.Empty;

	[XmlIgnore]
	public string WindSpeed2String
	{
		get => _windSpeed2String;

		set
		{
			if ( value != _windSpeed2String )
			{
				_windSpeed2String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Fan power 2

	private float _windFanPower2 = 0.02f;

	public float WindFanPower2
	{
		get => _windFanPower2;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower2 )
			{
				_windFanPower2 = value;

				OnPropertyChanged();
			}

			WindFanPower2String = $"{_windFanPower2 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _windFanPower2String = string.Empty;

	[XmlIgnore]
	public string WindFanPower2String
	{
		get => _windFanPower2String;

		set
		{
			if ( value != _windFanPower2String )
			{
				_windFanPower2String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Speed 3

	private float _windSpeed3 = 0.135f;

	public float WindSpeed3
	{
		get => _windSpeed3;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windSpeed3 )
			{
				_windSpeed3 = value;

				OnPropertyChanged();

				WindSpeed2 = MathF.Min( WindSpeed2, _windSpeed3 );
				WindSpeed4 = MathF.Max( WindSpeed4, _windSpeed3 );
			}

			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindSpeed3String = $"{_windSpeed3 * 100f * MathZ.MPSToMPH:F0}";
			}
			else
			{
				WindSpeed3String = $"{_windSpeed3 * 100f * MathZ.MPSToKPH:F0}";
			}
		}
	}

	private string _windSpeed3String = string.Empty;

	[XmlIgnore]
	public string WindSpeed3String
	{
		get => _windSpeed3String;

		set
		{
			if ( value != _windSpeed3String )
			{
				_windSpeed3String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Fan power 3

	private float _windFanPower3 = 0.04f;

	public float WindFanPower3
	{
		get => _windFanPower3;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower3 )
			{
				_windFanPower3 = value;

				OnPropertyChanged();
			}

			WindFanPower3String = $"{_windFanPower3 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _windFanPower3String = string.Empty;

	[XmlIgnore]
	public string WindFanPower3String
	{
		get => _windFanPower3String;

		set
		{
			if ( value != _windFanPower3String )
			{
				_windFanPower3String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Speed 4

	private float _windSpeed4 = 0.2f;

	public float WindSpeed4
	{
		get => _windSpeed4;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windSpeed4 )
			{
				_windSpeed4 = value;

				OnPropertyChanged();

				WindSpeed3 = MathF.Min( WindSpeed3, _windSpeed4 );
				WindSpeed5 = MathF.Max( WindSpeed5, _windSpeed4 );
			}

			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindSpeed4String = $"{_windSpeed4 * 100f * MathZ.MPSToMPH:F0}";
			}
			else
			{
				WindSpeed4String = $"{_windSpeed4 * 100f * MathZ.MPSToKPH:F0}";
			}
		}
	}

	private string _windSpeed4String = string.Empty;

	[XmlIgnore]
	public string WindSpeed4String
	{
		get => _windSpeed4String;

		set
		{
			if ( value != _windSpeed4String )
			{
				_windSpeed4String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Fan power 4

	private float _windFanPower4 = 0.06f;

	public float WindFanPower4
	{
		get => _windFanPower4;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower4 )
			{
				_windFanPower4 = value;

				OnPropertyChanged();
			}

			WindFanPower4String = $"{_windFanPower4 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _windFanPower4String = string.Empty;

	[XmlIgnore]
	public string WindFanPower4String
	{
		get => _windFanPower4String;

		set
		{
			if ( value != _windFanPower4String )
			{
				_windFanPower4String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Speed 5

	private float _windSpeed5 = 0.268f;

	public float WindSpeed5
	{
		get => _windSpeed5;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windSpeed5 )
			{
				_windSpeed5 = value;

				OnPropertyChanged();

				WindSpeed4 = MathF.Min( WindSpeed4, _windSpeed5 );
				WindSpeed6 = MathF.Max( WindSpeed6, _windSpeed5 );
			}

			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindSpeed5String = $"{_windSpeed5 * 100f * MathZ.MPSToMPH:F0}";
			}
			else
			{
				WindSpeed5String = $"{_windSpeed5 * 100f * MathZ.MPSToKPH:F0}";
			}
		}
	}

	private string _windSpeed5String = string.Empty;

	[XmlIgnore]
	public string WindSpeed5String
	{
		get => _windSpeed5String;

		set
		{
			if ( value != _windSpeed5String )
			{
				_windSpeed5String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Fan power 5

	private float _windFanPower5 = 0.10f;

	public float WindFanPower5
	{
		get => _windFanPower5;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower5 )
			{
				_windFanPower5 = value;

				OnPropertyChanged();
			}

			WindFanPower5String = $"{_windFanPower5 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _windFanPower5String = string.Empty;

	[XmlIgnore]
	public string WindFanPower5String
	{
		get => _windFanPower5String;

		set
		{
			if ( value != _windFanPower5String )
			{
				_windFanPower5String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Speed 6

	private float _windSpeed6 = 0.4025f;

	public float WindSpeed6
	{
		get => _windSpeed6;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windSpeed6 )
			{
				_windSpeed6 = value;

				OnPropertyChanged();

				WindSpeed5 = MathF.Min( WindSpeed5, _windSpeed6 );
				WindSpeed7 = MathF.Max( WindSpeed7, _windSpeed6 );
			}

			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindSpeed6String = $"{_windSpeed6 * 100f * MathZ.MPSToMPH:F0}";
			}
			else
			{
				WindSpeed6String = $"{_windSpeed6 * 100f * MathZ.MPSToKPH:F0}";
			}
		}
	}

	private string _windSpeed6String = string.Empty;

	[XmlIgnore]
	public string WindSpeed6String
	{
		get => _windSpeed6String;

		set
		{
			if ( value != _windSpeed6String )
			{
				_windSpeed6String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Fan power 6

	private float _windFanPower6 = 0.15f;

	public float WindFanPower6
	{
		get => _windFanPower6;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower6 )
			{
				_windFanPower6 = value;

				OnPropertyChanged();
			}

			WindFanPower6String = $"{_windFanPower6 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _windFanPower6String = string.Empty;

	[XmlIgnore]
	public string WindFanPower6String
	{
		get => _windFanPower6String;

		set
		{
			if ( value != _windFanPower6String )
			{
				_windFanPower6String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Speed 7

	private float _windSpeed7 = 0.535f;

	public float WindSpeed7
	{
		get => _windSpeed7;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windSpeed7 )
			{
				_windSpeed7 = value;

				OnPropertyChanged();

				WindSpeed6 = MathF.Min( WindSpeed6, _windSpeed7 );
				WindSpeed8 = MathF.Max( WindSpeed8, _windSpeed7 );
			}

			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindSpeed7String = $"{_windSpeed7 * 100f * MathZ.MPSToMPH:F0}";
			}
			else
			{
				WindSpeed7String = $"{_windSpeed7 * 100f * MathZ.MPSToKPH:F0}";
			}
		}
	}

	private string _windSpeed7String = string.Empty;

	[XmlIgnore]
	public string WindSpeed7String
	{
		get => _windSpeed7String;

		set
		{
			if ( value != _windSpeed7String )
			{
				_windSpeed7String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Fan power 7

	private float _windFanPower7 = 0.25f;

	public float WindFanPower7
	{
		get => _windFanPower7;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower7 )
			{
				_windFanPower7 = value;

				OnPropertyChanged();
			}

			WindFanPower7String = $"{_windFanPower7 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _windFanPower7String = string.Empty;

	[XmlIgnore]
	public string WindFanPower7String
	{
		get => _windFanPower7String;

		set
		{
			if ( value != _windFanPower7String )
			{
				_windFanPower7String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Speed 8

	private float _windSpeed8 = 0.6725f;

	public float WindSpeed8
	{
		get => _windSpeed8;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windSpeed8 )
			{
				_windSpeed8 = value;

				OnPropertyChanged();

				WindSpeed7 = MathF.Min( WindSpeed7, _windSpeed8 );
				WindSpeed9 = MathF.Max( WindSpeed9, _windSpeed8 );
			}

			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindSpeed8String = $"{_windSpeed8 * 100f * MathZ.MPSToMPH:F0}";
			}
			else
			{
				WindSpeed8String = $"{_windSpeed8 * 100f * MathZ.MPSToKPH:F0}";
			}
		}
	}

	private string _windSpeed8String = string.Empty;

	[XmlIgnore]
	public string WindSpeed8String
	{
		get => _windSpeed8String;

		set
		{
			if ( value != _windSpeed8String )
			{
				_windSpeed8String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Fan power 8

	private float _windFanPower8 = 0.40f;

	public float WindFanPower8
	{
		get => _windFanPower8;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower8 )
			{
				_windFanPower8 = value;

				OnPropertyChanged();
			}

			WindFanPower8String = $"{_windFanPower8 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _windFanPower8String = string.Empty;

	[XmlIgnore]
	public string WindFanPower8String
	{
		get => _windFanPower8String;

		set
		{
			if ( value != _windFanPower8String )
			{
				_windFanPower8String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Speed 9

	private float _windSpeed9 = 0.805f;

	public float WindSpeed9
	{
		get => _windSpeed9;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windSpeed9 )
			{
				_windSpeed9 = value;

				OnPropertyChanged();

				WindSpeed8 = MathF.Min( WindSpeed8, _windSpeed9 );
				WindSpeed10 = MathF.Max( WindSpeed10, _windSpeed9 );
			}

			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindSpeed9String = $"{_windSpeed9 * 100f * MathZ.MPSToMPH:F0}";
			}
			else
			{
				WindSpeed9String = $"{_windSpeed9 * 100f * MathZ.MPSToKPH:F0}";
			}
		}
	}

	private string _windSpeed9String = string.Empty;

	[XmlIgnore]
	public string WindSpeed9String
	{
		get => _windSpeed9String;

		set
		{
			if ( value != _windSpeed9String )
			{
				_windSpeed9String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Fan power 9

	private float _windFanPower9 = 0.65f;

	public float WindFanPower9
	{
		get => _windFanPower9;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower9 )
			{
				_windFanPower9 = value;

				OnPropertyChanged();
			}

			WindFanPower9String = $"{_windFanPower9 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _windFanPower9String = string.Empty;

	[XmlIgnore]
	public string WindFanPower9String
	{
		get => _windFanPower9String;

		set
		{
			if ( value != _windFanPower9String )
			{
				_windFanPower9String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Speed 10

	private float _windSpeed10 = 0.94f;

	public float WindSpeed10
	{
		get => _windSpeed10;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windSpeed10 )
			{
				_windSpeed10 = value;

				OnPropertyChanged();

				WindSpeed9 = MathF.Min( WindSpeed9, _windSpeed10 );
			}

			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindSpeed10String = $"{_windSpeed10 * 100f * MathZ.MPSToMPH:F0}";
			}
			else
			{
				WindSpeed10String = $"{_windSpeed10 * 100f * MathZ.MPSToKPH:F0}";
			}
		}
	}

	private string _windSpeed10String = string.Empty;

	[XmlIgnore]
	public string WindSpeed10String
	{
		get => _windSpeed10String;

		set
		{
			if ( value != _windSpeed10String )
			{
				_windSpeed10String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Fan power 10

	private float _windFanPower10 = 1f;

	public float WindFanPower10
	{
		get => _windFanPower10;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower10 )
			{
				_windFanPower10 = value;

				OnPropertyChanged();
			}

			WindFanPower10String = $"{_windFanPower10 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _windFanPower10String = string.Empty;

	[XmlIgnore]
	public string WindFanPower10String
	{
		get => _windFanPower10String;

		set
		{
			if ( value != _windFanPower10String )
			{
				_windFanPower10String = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Show Gap monitor window

	private bool _overlaysShowGapMonitorWindow = false;

	public bool OverlaysShowGapMonitorWindow
	{
		get => _overlaysShowGapMonitorWindow;

		set
		{
			if ( value != _overlaysShowGapMonitorWindow )
			{
				_overlaysShowGapMonitorWindow = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGapMonitorWindowVisibility();
		}
	}

	#endregion

	#region Overlays - Make Gap monitor draggable

	private bool _overlaysMakeGapMonitorDraggable = false;

	public bool OverlaysMakeGapMonitorDraggable
	{
		get => _overlaysMakeGapMonitorDraggable;

		set
		{
			if ( value != _overlaysMakeGapMonitorDraggable )
			{
				_overlaysMakeGapMonitorDraggable = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGapMonitorWindowVisibility();
		}
	}

	#endregion

	#region Overlays - Gap monitor window position

	private Rectangle _overlaysGapMonitorWindowPosition = Rectangle.Empty;

	public Rectangle OverlaysGapMonitorWindowPosition
	{
		get => _overlaysGapMonitorWindowPosition;

		set
		{
			if ( value != _overlaysGapMonitorWindowPosition )
			{
				_overlaysGapMonitorWindowPosition = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Gap monitor window scale

	private float _overlaysGapMonitorWindowScale = 1f;

	public float OverlaysGapMonitorWindowScale
	{
		get => _overlaysGapMonitorWindowScale;

		set
		{
			value = Math.Clamp( value, 0.5f, 2f );

			if ( value != _overlaysGapMonitorWindowScale )
			{
				_overlaysGapMonitorWindowScale = value;

				OnPropertyChanged();
			}

			OverlaysGapMonitorWindowScaleString = $"{_overlaysGapMonitorWindowScale * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _overlaysGapMonitorWindowScaleString = string.Empty;

	[XmlIgnore]
	public string OverlaysGapMonitorWindowScaleString
	{
		get => _overlaysGapMonitorWindowScaleString;

		set
		{
			if ( value != _overlaysGapMonitorWindowScaleString )
			{
				_overlaysGapMonitorWindowScaleString = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Show Gap monitor title

	private bool _overlaysShowGapMonitorTitle = true;

	public bool OverlaysShowGapMonitorTitle
	{
		get => _overlaysShowGapMonitorTitle;

		set
		{
			if ( value != _overlaysShowGapMonitorTitle )
			{
				_overlaysShowGapMonitorTitle = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGapMonitorWindowVisibility();
		}
	}

	#endregion

	#region Sounds - Master enabled

	private bool _soundsMasterEnabled = true;

	public bool SoundsMasterEnabled
	{
		get => _soundsMasterEnabled;

		set
		{
			if ( value != _soundsMasterEnabled )
			{
				_soundsMasterEnabled = value;

				OnPropertyChanged();

				var app = App.Instance!;

				if ( _soundsMasterEnabled )
				{
					app.AudioManager.OpenDevice();
				}
				else
				{
					app.AudioManager.CloseDevice();
				}
			}
		}
	}

	#endregion

	#region Sounds - Master volume

	private float _soundsMasterVolume = 0.75f;

	public float SoundsMasterVolume
	{
		get => _soundsMasterVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsMasterVolume )
			{
				_soundsMasterVolume = value;

				OnPropertyChanged();
			}

			SoundsMasterVolumeString = $"{_soundsMasterVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsMasterVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsMasterVolumeString
	{
		get => _soundsMasterVolumeString;

		set
		{
			if ( value != _soundsMasterVolumeString )
			{
				_soundsMasterVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsMasterVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsMasterVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Click enabled

	private bool _soundsClickEnabled = true;

	public bool SoundsClickEnabled
	{
		get => _soundsClickEnabled;

		set
		{
			if ( value != _soundsClickEnabled )
			{
				_soundsClickEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Click volume

	private float _soundsClickVolume = 0.75f;

	public float SoundsClickVolume
	{
		get => _soundsClickVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsClickVolume )
			{
				_soundsClickVolume = value;

				OnPropertyChanged();
			}

			SoundsClickVolumeString = $"{_soundsClickVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsClickVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsClickVolumeString
	{
		get => _soundsClickVolumeString;

		set
		{
			if ( value != _soundsClickVolumeString )
			{
				_soundsClickVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsClickVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsClickVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Click frequency ratio

	private float _soundsClickFrequencyRatio = 1f;

	public float SoundsClickFrequencyRatio
	{
		get => _soundsClickFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 4f );

			if ( value != _soundsClickFrequencyRatio )
			{
				_soundsClickFrequencyRatio = value;

				OnPropertyChanged();
			}

			SoundsClickFrequencyRatioString = $"{_soundsClickFrequencyRatio * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsClickFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsClickFrequencyRatioString
	{
		get => _soundsClickFrequencyRatioString;

		set
		{
			if ( value != _soundsClickFrequencyRatioString )
			{
				_soundsClickFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsClickFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsClickFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - ABS engaged enabled

	private bool _soundsABSEngagedEnabled = false;

	public bool SoundsABSEngagedEnabled
	{
		get => _soundsABSEngagedEnabled;

		set
		{
			if ( value != _soundsABSEngagedEnabled )
			{
				_soundsABSEngagedEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - ABS engaged volume

	private float _soundsABSEngagedVolume = 0.75f;

	public float SoundsABSEngagedVolume
	{
		get => _soundsABSEngagedVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsABSEngagedVolume )
			{
				_soundsABSEngagedVolume = value;

				OnPropertyChanged();
			}

			SoundsABSEngagedVolumeString = $"{_soundsABSEngagedVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsABSEngagedVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsABSEngagedVolumeString
	{
		get => _soundsABSEngagedVolumeString;

		set
		{
			if ( value != _soundsABSEngagedVolumeString )
			{
				_soundsABSEngagedVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsABSEngagedVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsABSEngagedVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - ABS engaged frequency ratio

	private float _soundsABSEngagedFrequencyRatio = 1f;

	public float SoundsABSEngagedFrequencyRatio
	{
		get => _soundsABSEngagedFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 4f );

			if ( value != _soundsABSEngagedFrequencyRatio )
			{
				_soundsABSEngagedFrequencyRatio = value;

				OnPropertyChanged();
			}

			SoundsABSEngagedFrequencyRatioString = $"{_soundsABSEngagedFrequencyRatio * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsABSEngagedFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsABSEngagedFrequencyRatioString
	{
		get => _soundsABSEngagedFrequencyRatioString;

		set
		{
			if ( value != _soundsABSEngagedFrequencyRatioString )
			{
				_soundsABSEngagedFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsABSEngagedFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsABSEngagedFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - ABS engaged fade with brake

	private bool _soundsABSEngagedFadeWithBrake = false;

	public bool SoundsABSEngagedFadeWithBrake
	{
		get => _soundsABSEngagedFadeWithBrake;

		set
		{
			if ( value != _soundsABSEngagedFadeWithBrake )
			{
				_soundsABSEngagedFadeWithBrake = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Wheel lock enabled

	private bool _soundsWheelLockEnabled = false;

	public bool SoundsWheelLockEnabled
	{
		get => _soundsWheelLockEnabled;

		set
		{
			if ( value != _soundsWheelLockEnabled )
			{
				_soundsWheelLockEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Wheel lock volume

	private float _soundsWheelLockVolume = 0.75f;

	public float SoundsWheelLockVolume
	{
		get => _soundsWheelLockVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsWheelLockVolume )
			{
				_soundsWheelLockVolume = value;

				OnPropertyChanged();
			}

			SoundsWheelLockVolumeString = $"{_soundsWheelLockVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsWheelLockVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelLockVolumeString
	{
		get => _soundsWheelLockVolumeString;

		set
		{
			if ( value != _soundsWheelLockVolumeString )
			{
				_soundsWheelLockVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsWheelLockVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelLockVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel lock frequency ratio

	private float _soundsWheelLockFrequencyRatio = 1f;

	public float SoundsWheelLockFrequencyRatio
	{
		get => _soundsWheelLockFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 4f );

			if ( value != _soundsWheelLockFrequencyRatio )
			{
				_soundsWheelLockFrequencyRatio = value;

				OnPropertyChanged();
			}

			SoundsWheelLockFrequencyRatioString = $"{_soundsWheelLockFrequencyRatio * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsWheelLockFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelLockFrequencyRatioString
	{
		get => _soundsWheelLockFrequencyRatioString;

		set
		{
			if ( value != _soundsWheelLockFrequencyRatioString )
			{
				_soundsWheelLockFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsWheelLockFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelLockFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel lock sensitivity

	private float _soundsWheelLockSensitivity = 0.85f;

	public float SoundsWheelLockSensitivity
	{
		get => _soundsWheelLockSensitivity;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsWheelLockSensitivity )
			{
				_soundsWheelLockSensitivity = value;

				OnPropertyChanged();
			}

			SoundsWheelLockSensitivityString = $"{_soundsWheelLockSensitivity * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsWheelLockSensitivityString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelLockSensitivityString
	{
		get => _soundsWheelLockSensitivityString;

		set
		{
			if ( value != _soundsWheelLockSensitivityString )
			{
				_soundsWheelLockSensitivityString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsWheelLockSensitivityPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelLockSensitivityMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel lock fade with brake

	private bool _soundsWheelLockFadeWithBrake = true;

	public bool SoundsWheelLockFadeWithBrake
	{
		get => _soundsWheelLockFadeWithBrake;

		set
		{
			if ( value != _soundsWheelLockFadeWithBrake )
			{
				_soundsWheelLockFadeWithBrake = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Wheel spin enabled

	private bool _soundsWheelSpinEnabled = false;

	public bool SoundsWheelSpinEnabled
	{
		get => _soundsWheelSpinEnabled;

		set
		{
			if ( value != _soundsWheelSpinEnabled )
			{
				_soundsWheelSpinEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Wheel spin volume

	private float _soundsWheelSpinVolume = 0.75f;

	public float SoundsWheelSpinVolume
	{
		get => _soundsWheelSpinVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsWheelSpinVolume )
			{
				_soundsWheelSpinVolume = value;

				OnPropertyChanged();
			}

			SoundsWheelSpinVolumeString = $"{_soundsWheelSpinVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsWheelSpinVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelSpinVolumeString
	{
		get => _soundsWheelSpinVolumeString;

		set
		{
			if ( value != _soundsWheelSpinVolumeString )
			{
				_soundsWheelSpinVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsWheelSpinVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelSpinVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel spin frequency ratio

	private float _soundsWheelSpinFrequencyRatio = 1f;

	public float SoundsWheelSpinFrequencyRatio
	{
		get => _soundsWheelSpinFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 4f );

			if ( value != _soundsWheelSpinFrequencyRatio )
			{
				_soundsWheelSpinFrequencyRatio = value;

				OnPropertyChanged();
			}

			SoundsWheelSpinFrequencyRatioString = $"{_soundsWheelSpinFrequencyRatio * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsWheelSpinFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelSpinFrequencyRatioString
	{
		get => _soundsWheelSpinFrequencyRatioString;

		set
		{
			if ( value != _soundsWheelSpinFrequencyRatioString )
			{
				_soundsWheelSpinFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsWheelSpinFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelSpinFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel spin sensitivity

	private float _soundsWheelSpinSensitivity = 0.85f;

	public float SoundsWheelSpinSensitivity
	{
		get => _soundsWheelSpinSensitivity;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsWheelSpinSensitivity )
			{
				_soundsWheelSpinSensitivity = value;

				OnPropertyChanged();
			}

			SoundsWheelSpinSensitivityString = $"{_soundsWheelSpinSensitivity * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsWheelSpinSensitivityString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelSpinSensitivityString
	{
		get => _soundsWheelSpinSensitivityString;

		set
		{
			if ( value != _soundsWheelSpinSensitivityString )
			{
				_soundsWheelSpinSensitivityString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsWheelSpinSensitivityPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelSpinSensitivityMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel spin fade with throttle

	private bool _soundsWheelSpinFadeWithThrottle = true;

	public bool SoundsWheelSpinFadeWithThrottle
	{
		get => _soundsWheelSpinFadeWithThrottle;

		set
		{
			if ( value != _soundsWheelSpinFadeWithThrottle )
			{
				_soundsWheelSpinFadeWithThrottle = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Understeer enabled

	private bool _soundsUndersteerEnabled = false;

	public bool SoundsUndersteerEnabled
	{
		get => _soundsUndersteerEnabled;

		set
		{
			if ( value != _soundsUndersteerEnabled )
			{
				_soundsUndersteerEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Understeer volume

	private float _soundsUndersteerVolume = 0.75f;

	public float SoundsUndersteerVolume
	{
		get => _soundsUndersteerVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsUndersteerVolume )
			{
				_soundsUndersteerVolume = value;

				OnPropertyChanged();
			}

			SoundsUndersteerVolumeString = $"{_soundsUndersteerVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsUndersteerVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsUndersteerVolumeString
	{
		get => _soundsUndersteerVolumeString;

		set
		{
			if ( value != _soundsUndersteerVolumeString )
			{
				_soundsUndersteerVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsUndersteerVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsUndersteerVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Understeer frequency ratio

	private float _soundsUndersteerFrequencyRatio = 1f;

	public float SoundsUndersteerFrequencyRatio
	{
		get => _soundsUndersteerFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 4f );

			if ( value != _soundsUndersteerFrequencyRatio )
			{
				_soundsUndersteerFrequencyRatio = value;

				OnPropertyChanged();
			}

			SoundsUndersteerFrequencyRatioString = $"{_soundsUndersteerFrequencyRatio * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsUndersteerFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsUndersteerFrequencyRatioString
	{
		get => _soundsUndersteerFrequencyRatioString;

		set
		{
			if ( value != _soundsUndersteerFrequencyRatioString )
			{
				_soundsUndersteerFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsUndersteerFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsUndersteerFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Oversteer enabled

	private bool _soundsOversteerEnabled = false;

	public bool SoundsOversteerEnabled
	{
		get => _soundsOversteerEnabled;

		set
		{
			if ( value != _soundsOversteerEnabled )
			{
				_soundsOversteerEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Oversteer volume

	private float _soundsOversteerVolume = 0.75f;

	public float SoundsOversteerVolume
	{
		get => _soundsOversteerVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsOversteerVolume )
			{
				_soundsOversteerVolume = value;

				OnPropertyChanged();
			}

			SoundsOversteerVolumeString = $"{_soundsOversteerVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsOversteerVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsOversteerVolumeString
	{
		get => _soundsOversteerVolumeString;

		set
		{
			if ( value != _soundsOversteerVolumeString )
			{
				_soundsOversteerVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsOversteerVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsOversteerVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Oversteer frequency ratio

	private float _soundsOversteerFrequencyRatio = 1f;

	public float SoundsOversteerFrequencyRatio
	{
		get => _soundsOversteerFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 4f );

			if ( value != _soundsOversteerFrequencyRatio )
			{
				_soundsOversteerFrequencyRatio = value;

				OnPropertyChanged();
			}

			SoundsOversteerFrequencyRatioString = $"{_soundsOversteerFrequencyRatio * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsOversteerFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsOversteerFrequencyRatioString
	{
		get => _soundsOversteerFrequencyRatioString;

		set
		{
			if ( value != _soundsOversteerFrequencyRatioString )
			{
				_soundsOversteerFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsOversteerFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsOversteerFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Seat-of-pants enabled

	private bool _soundsSeatOfPantsEnabled = false;

	public bool SoundsSeatOfPantsEnabled
	{
		get => _soundsSeatOfPantsEnabled;

		set
		{
			if ( value != _soundsSeatOfPantsEnabled )
			{
				_soundsSeatOfPantsEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Seat-of-pants volume

	private float _soundsSeatOfPantsVolume = 0.75f;

	public float SoundsSeatOfPantsVolume
	{
		get => _soundsSeatOfPantsVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsSeatOfPantsVolume )
			{
				_soundsSeatOfPantsVolume = value;

				OnPropertyChanged();
			}

			SoundsSeatOfPantsVolumeString = $"{_soundsSeatOfPantsVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsSeatOfPantsVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsSeatOfPantsVolumeString
	{
		get => _soundsSeatOfPantsVolumeString;

		set
		{
			if ( value != _soundsSeatOfPantsVolumeString )
			{
				_soundsSeatOfPantsVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsSeatOfPantsVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsSeatOfPantsVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Seat-of-pants frequency ratio

	private float _soundsSeatOfPantsFrequencyRatio = 1f;

	public float SoundsSeatOfPantsFrequencyRatio
	{
		get => _soundsSeatOfPantsFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 4f );

			if ( value != _soundsSeatOfPantsFrequencyRatio )
			{
				_soundsSeatOfPantsFrequencyRatio = value;

				OnPropertyChanged();
			}

			SoundsSeatOfPantsFrequencyRatioString = $"{_soundsSeatOfPantsFrequencyRatio * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _soundsSeatOfPantsFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsSeatOfPantsFrequencyRatioString
	{
		get => _soundsSeatOfPantsFrequencyRatioString;

		set
		{
			if ( value != _soundsSeatOfPantsFrequencyRatioString )
			{
				_soundsSeatOfPantsFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings SoundsSeatOfPantsFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsSeatOfPantsFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Speech to text - Enabled

	private bool _speechToTextEnabled = false;

	public bool SpeechToTextEnabled
	{
		get => _speechToTextEnabled;

		set
		{
			if ( value != _speechToTextEnabled )
			{
				_speechToTextEnabled = value;

				OnPropertyChanged();

				var app = App.Instance!;

				if ( _speechToTextEnabled )
				{
					if ( app.Simulator.IsConnected )
					{
						_ = app.SpeechToText.EnableAsync();
					}
				}
				else
				{
					_ = app.SpeechToText.DisableAsync();
				}
			}
		}
	}

	#endregion

	#region Speech to text - Language code

	private string _speechToTextLanguageCode = "en-US";

	public string SpeechToTextLanguageCode
	{
		get => _speechToTextLanguageCode;

		set
		{
			if ( value != _speechToTextLanguageCode )
			{
				_speechToTextLanguageCode = value;

				OnPropertyChanged();

				App.Instance!.SpeechToText.Language = _speechToTextLanguageCode;
			}
		}
	}

	#endregion

	#region Speech to text - Show overlay window title

	private bool _speechToTextShowOverlayWindowTitle = true;

	public bool SpeechToTextShowOverlayWindowTitle
	{
		get => _speechToTextShowOverlayWindowTitle;

		set
		{
			if ( value != _speechToTextShowOverlayWindowTitle )
			{
				_speechToTextShowOverlayWindowTitle = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;
		}
	}

	#endregion

	#region Speech to text - Make overlay window draggable

	private bool _speechToTextMakeOverlayWindowDraggable = false;

	public bool SpeechToTextMakeOverlayWindowDraggable
	{
		get => _speechToTextMakeOverlayWindowDraggable;

		set
		{
			if ( value != _speechToTextMakeOverlayWindowDraggable )
			{
				_speechToTextMakeOverlayWindowDraggable = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateSpeechToTextWindowVisibility();
		}
	}

	#endregion

	#region Speech to text - Overlay window position

	private Rectangle _speechToTextOverlayWindowPosition = Rectangle.Empty;

	public Rectangle SpeechToTextOverlayWindowPosition
	{
		get => _speechToTextOverlayWindowPosition;

		set
		{
			if ( value != _speechToTextOverlayWindowPosition )
			{
				_speechToTextOverlayWindowPosition = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Speech to text - Overlay window scale

	private float _speechToTextOverlayWindowScale = 1f;

	public float SpeechToTextOverlayWindowScale
	{
		get => _speechToTextOverlayWindowScale;

		set
		{
			value = Math.Clamp( value, 0.5f, 2f );

			if ( value != _speechToTextOverlayWindowScale )
			{
				_speechToTextOverlayWindowScale = value;

				OnPropertyChanged();
			}

			SpeechToTextOverlayWindowScaleString = $"{_speechToTextOverlayWindowScale * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _speechToTextOverlayWindowScaleString = string.Empty;

	[XmlIgnore]
	public string SpeechToTextOverlayWindowScaleString
	{
		get => _speechToTextOverlayWindowScaleString;

		set
		{
			if ( value != _speechToTextOverlayWindowScaleString )
			{
				_speechToTextOverlayWindowScaleString = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Statistics

	private Graph.LayerIndex _graphStatisticsLayerIndex = Graph.LayerIndex.TimerJitter;

	public Graph.LayerIndex GraphStatisticsLayerIndex
	{
		get => _graphStatisticsLayerIndex;

		set
		{
			if ( value != _graphStatisticsLayerIndex )
			{
				_graphStatisticsLayerIndex = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Input torque

	private bool _graphInputTorque = true;

	public bool GraphInputTorque
	{
		get => _graphInputTorque;

		set
		{
			if ( value != _graphInputTorque )
			{
				_graphInputTorque = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Output torque

	private bool _graphOutputTorque = true;

	public bool GraphOutputTorque
	{
		get => _graphOutputTorque;

		set
		{
			if ( value != _graphOutputTorque )
			{
				_graphOutputTorque = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Input torque (60 Hz)

	private bool _graphInputTorque60Hz = false;

	public bool GraphInputTorque60Hz
	{
		get => _graphInputTorque60Hz;

		set
		{
			if ( value != _graphInputTorque60Hz )
			{
				_graphInputTorque60Hz = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Input LFE

	private bool _graphInputLFE = false;

	public bool GraphInputLFE
	{
		get => _graphInputLFE;

		set
		{
			if ( value != _graphInputLFE )
			{
				_graphInputLFE = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Clutch pedal haptics

	private bool _graphClutchPedalHaptics = false;

	public bool GraphClutchPedalHaptics
	{
		get => _graphClutchPedalHaptics;

		set
		{
			if ( value != _graphClutchPedalHaptics )
			{
				_graphClutchPedalHaptics = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Brake pedal haptics

	private bool _graphBrakePedalHaptics = false;

	public bool GraphBrakePedalHaptics
	{
		get => _graphBrakePedalHaptics;

		set
		{
			if ( value != _graphBrakePedalHaptics )
			{
				_graphBrakePedalHaptics = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Throttle pedal haptics

	private bool _graphThrottlePedalHaptics = false;

	public bool GraphThrottlePedalHaptics
	{
		get => _graphThrottlePedalHaptics;

		set
		{
			if ( value != _graphThrottlePedalHaptics )
			{
				_graphThrottlePedalHaptics = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Timer jitter

	private bool _graphTimerJitter = false;

	public bool GraphTimerJitter
	{
		get => _graphTimerJitter;

		set
		{
			if ( value != _graphTimerJitter )
			{
				_graphTimerJitter = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region AdminBoxx - Connect on startup

	private bool _adminBoxxConnectOnStartup = false;

	public bool AdminBoxxConnectOnStartup
	{
		get => _adminBoxxConnectOnStartup;

		set
		{
			if ( value != _adminBoxxConnectOnStartup )
			{
				_adminBoxxConnectOnStartup = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region AdminBoxx - Brightness

	private float _adminBoxxBrightness = 0.15f;

	public float AdminBoxxBrightness
	{
		get => _adminBoxxBrightness;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _adminBoxxBrightness )
			{
				_adminBoxxBrightness = value;

				OnPropertyChanged();
			}

			AdminBoxxBrightnessString = $"{_adminBoxxBrightness * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _adminBoxxBrightnessString = string.Empty;

	[XmlIgnore]
	public string AdminBoxxBrightnessString
	{
		get => _adminBoxxBrightnessString;

		set
		{
			if ( value != _adminBoxxBrightnessString )
			{
				_adminBoxxBrightnessString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings AdminBoxxBrightnessPlusButtonMappings { get; set; } = new();
	public ButtonMappings AdminBoxxBrightnessMinusButtonMappings { get; set; } = new();

	#endregion

	#region AdminBoxx - Volume

	private float _adminBoxxVolume = 0.75f;

	public float AdminBoxxVolume
	{
		get => _adminBoxxVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _adminBoxxVolume )
			{
				_adminBoxxVolume = value;

				OnPropertyChanged();
			}

			AdminBoxxVolumeString = $"{_adminBoxxVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _adminBoxxVolumeString = string.Empty;

	[XmlIgnore]
	public string AdminBoxxVolumeString
	{
		get => _adminBoxxVolumeString;

		set
		{
			if ( value != _adminBoxxBrightnessString )
			{
				_adminBoxxVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings AdminBoxxVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings AdminBoxxVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region App - Current language code

	private string _appCurrentLanguageCode = "default";

	public string AppCurrentLanguageCode
	{
		get => _appCurrentLanguageCode;

		set
		{
			if ( value != _appCurrentLanguageCode )
			{
				_appCurrentLanguageCode = value;

				DataContext.Instance.Localization.LoadLanguage( value );

				OnPropertyChanged();

				var app = App.Instance!;

				app.MainWindow.RefreshWindow();

				Misc.ForcePropertySetters( this );
			}
		}
	}

	#endregion

	#region App - Default page

#if !ADMINBOXX

	private MainWindow.AppPage _appDefaultPage = MainWindow.AppPage.RacingWheel;

#else

	private MainWindow.AppPage _appDefaultPage = MainWindow.AppPage.AdminBoxx;

#endif

	public MainWindow.AppPage AppDefaultPage
	{
		get => _appDefaultPage;

		set
		{
			if ( value != _appDefaultPage )
			{
				_appDefaultPage = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Topmost window enabled

	private bool _appTopmostWindowEnabled = false;

	public bool AppTopmostWindowEnabled
	{
		get => _appTopmostWindowEnabled;

		set
		{
			if ( value != _appTopmostWindowEnabled )
			{
				_appTopmostWindowEnabled = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.MainWindow.Topmost = _appTopmostWindowEnabled;
		}
	}

	#endregion

	#region App - Remember window position and size

	private bool _appRememberWindowPositionAndSize = false;

	public bool AppRememberWindowPositionAndSize
	{
		get => _appRememberWindowPositionAndSize;

		set
		{
			if ( value != _appRememberWindowPositionAndSize )
			{
				_appRememberWindowPositionAndSize = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Window position and size

	private Rectangle _appWindowPositionAndSize = Rectangle.Empty;

	public Rectangle AppWindowPositionAndSize
	{
		get => _appWindowPositionAndSize;

		set
		{
			if ( value != _appWindowPositionAndSize )
			{
				_appWindowPositionAndSize = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Start with Windows

	private bool _appStartWithWindows = false;

	public bool AppStartWithWindows
	{
		get => _appStartWithWindows;

		set
		{
			if ( value != _appStartWithWindows )
			{
				_appStartWithWindows = value;

				OnPropertyChanged();
			}

			Misc.SetStartWithWindows( _appStartWithWindows );
		}
	}

	#endregion

	#region App - Start minimized

	private bool _appStartMinimized = false;

	public bool AppStartMinimized
	{
		get => _appStartMinimized;

		set
		{
			if ( value != _appStartMinimized )
			{
				_appStartMinimized = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Minimize to system tray

	private bool _appMinimizeToSystemTray = false;

	public bool AppMinimizeToSystemTray
	{
		get => _appMinimizeToSystemTray;

		set
		{
			if ( value != _appMinimizeToSystemTray )
			{
				_appMinimizeToSystemTray = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.MainWindow.UpdateNotifyIcon();
		}
	}

	#endregion

	#region App - UI scale

	private float _appUIScale = 1f;

	public float AppUIScale
	{
		get => _appUIScale;

		set
		{
			value = Math.Clamp( value, 0.5f, 2f );

			if ( value != _appUIScale )
			{
				_appUIScale = value;

				OnPropertyChanged();
			}

			AppUIScaleString = $"{_appUIScale * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _appUIScaleString = string.Empty;

	[XmlIgnore]
	public string AppUIScaleString
	{
		get => _appUIScaleString;

		set
		{
			if ( value != _appUIScaleString )
			{
				_appUIScaleString = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Check for updates

	private bool _appCheckForUpdates = true;

	public bool AppCheckForUpdates
	{
		get => _appCheckForUpdates;

		set
		{
			if ( value != _appCheckForUpdates )
			{
				_appCheckForUpdates = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - CPU affinity

	private ulong _appAffinityMaskBits = 0xFFFFFFFFFFFFFFFF;

	public ulong AppAffinityMaskBits
	{
		get => _appAffinityMaskBits;

		set
		{
			if ( value != _appAffinityMaskBits )
			{
				_appAffinityMaskBits = value;

				OnPropertyChanged();

				try
				{
					CpuAffinityHelper.SetCpuAffinity( _appAffinityMaskBits );
				}
				catch ( Exception ex )
				{
					var app = App.Instance!;

					app.Logger.WriteLine( $"[Settings] Failed to set CPU affinity: {ex.Message}" );
				}
			}
		}
	}

	#endregion

	#region Trading paints - Enabled

	private bool _tradingPaintsEnabled = false;

	public bool TradingPaintsEnabled
	{
		get => _tradingPaintsEnabled;

		set
		{
			if ( value != _tradingPaintsEnabled )
			{
				_tradingPaintsEnabled = value;

				OnPropertyChanged();

				App.Instance?.TradingPaints.Reset();
			}
		}
	}

	#endregion

	#region Trading paints - Folder

	private string _tradingPaintsFolder = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), "iRacing", "paint" );

	public string TradingPaintsFolder
	{
		get => _tradingPaintsFolder;

		set
		{
			if ( value != _tradingPaintsFolder )
			{
				_tradingPaintsFolder = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion
}
