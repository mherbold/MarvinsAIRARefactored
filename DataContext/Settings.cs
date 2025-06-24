
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.DataContext;

public class Settings : INotifyPropertyChanged
{
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
				app.Logger.WriteLine( $"[Settings] {propertyName} = {property.GetValue( this )}" );

				var contextSwitchesPropertyName = $"{propertyName}ContextSwitches";

				var contextSwitchesProperty = GetType().GetProperty( contextSwitchesPropertyName );

				if ( contextSwitchesProperty != null )
				{
					var contextSwitches = (ContextSwitches?) contextSwitchesProperty.GetValue( this );

					if ( contextSwitches != null )
					{
						var context = new Context( contextSwitches );

						var contextSettings = FindContextSettings( context );

						UpdateToContextSettings( contextSettings );
					}
				}
			}
		}

		app.SettingsFile.QueueForSerialization = true;

		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
	}

	#endregion

	#region Context settings

	public SerializableDictionary<Context, ContextSettings> ContextSettingsDictionary { get; set; } = [];

	public void UpdateFromContextSettings()
	{
		var app = App.Instance!;

		var destinationProperties = typeof( Settings ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

		foreach ( var destinationProperty in destinationProperties )
		{
			if ( destinationProperty.CanWrite && !destinationProperty.Name.EndsWith( "String" ) )
			{
				var contextSwitchesPropertyName = $"{destinationProperty.Name}ContextSwitches";

				var contextSwitchesProperty = GetType().GetProperty( contextSwitchesPropertyName );

				if ( contextSwitchesProperty != null )
				{
					var contextSwitches = (ContextSwitches?) contextSwitchesProperty.GetValue( this );

					if ( contextSwitches != null )
					{
						var context = new Context( contextSwitches );

						var contextSettings = FindContextSettings( context );

						var sourceProperty = typeof( ContextSettings ).GetProperty( destinationProperty.Name );

						if ( sourceProperty != null )
						{
							var value = sourceProperty.GetValue( contextSettings );

							app.Logger.WriteLine( $"[Settings] Setting {destinationProperty.Name} = {value} ({context.WheelbaseGuid}|{context.CarName}|{context.TrackName}|{context.TrackConfigurationName}|{context.WetDryName})" );

							destinationProperty.SetValue( this, value );
						}
					}
				}
			}
		}
	}

	private ContextSettings FindContextSettings( Context context )
	{
		if ( !ContextSettingsDictionary.TryGetValue( context, out var contextSettings ) )
		{
			contextSettings = new ContextSettings();

			UpdateToContextSettings( contextSettings );

			ContextSettingsDictionary.Add( context, contextSettings );
		}

		return contextSettings;
	}

	private void UpdateToContextSettings( ContextSettings contextSettings )
	{
		var sourceProperties = typeof( Settings ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

		var destinationProperties = typeof( ContextSettings ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

		foreach ( var sourceProperty in sourceProperties )
		{
			if ( sourceProperty.CanRead )
			{
				var destinationProperty = Array.Find( destinationProperties, p => p.Name == sourceProperty.Name && p.CanWrite && p.PropertyType.IsAssignableFrom( sourceProperty.PropertyType ) );

				if ( destinationProperty != null )
				{
					var value = sourceProperty.GetValue( this );

					destinationProperty.SetValue( contextSettings, value );
				}
			}
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

			var app = App.Instance!;

			app.MainWindow.UpdateRacingWheelPowerButton();
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

	#region Racing wheel - Max force

	private float _racingWheelMaxForce = 50f;

	public float RacingWheelMaxForce
	{
		get => _racingWheelMaxForce;

		set
		{
			value = Math.Clamp( value, 5f, 99.9f );

			if ( value != _racingWheelMaxForce )
			{
				_racingWheelMaxForce = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			RacingWheelMaxForceString = $"{_racingWheelMaxForce:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]}";

			UpdateRacingWheelSlewCompressionThresholdString();
			UpdateRacingWheelTotalCompressionThresholdString();
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

	public ContextSwitches RacingWheelMaxForceContextSwitches { get; set; } = new( true, true, true, false, false );
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
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _racingWheelAutoMargin )
			{
				_racingWheelAutoMargin = value;

				OnPropertyChanged();
			}

			RacingWheelAutoMarginString = $"{_racingWheelAutoMargin * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
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

	public ContextSwitches RacingWheelAutoMarginContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelAutoMarginPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelAutoMarginMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Auto

	public ButtonMappings RacingWheelAutoButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Clear

	public ButtonMappings RacingWheelClearButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Algorithm

	private RacingWheel.Algorithm _racingWheelAlgorithm = RacingWheel.Algorithm.DetailBooster;

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

	#region Racing wheel - Detail boost

	private float _racingWheelDetailBoost = 0f;

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

			RacingWheelDeltaLimitString = $"{_racingWheelDeltaLimit:F0}{DataContext.Instance.Localization[ "DeltaLimitUnits" ]}";
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

	#region Racing wheel - Detail boost bias

	private float _racingWheelDetailBoostBias = 0.1f;

	public float RacingWheelDetailBoostBias
	{
		get => _racingWheelDetailBoostBias;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

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

	#region Racing wheel - Delta limiter bias

	private float _racingWheelDeltaLimiterBias = 0.2f;

	public float RacingWheelDeltaLimiterBias
	{
		get => _racingWheelDeltaLimiterBias;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

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

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelSlewCompressionThresholdString();
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

	public ContextSwitches RacingWheelSlewCompressionThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelSlewCompressionThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelSlewCompressionThresholdMinusButtonMappings { get; set; } = new();

	private void UpdateRacingWheelSlewCompressionThresholdString()
	{
		RacingWheelSlewCompressionThresholdString = $"{_racingWheelSlewCompressionThreshold * DataContext.Instance.Settings.RacingWheelMaxForce / 1000f:F2}{DataContext.Instance.Localization[ "SlewUnits" ]}";
	}

	#endregion

	#region Racing wheel - Slew compression rate

	private float _racingWheelSlewCompressionRate = 0.65f;

	public float RacingWheelSlewCompressionRate
	{
		get => _racingWheelSlewCompressionRate;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

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
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _racingWheelTotalCompressionThreshold )
			{
				_racingWheelTotalCompressionThreshold = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelTotalCompressionThresholdString();
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

	public ContextSwitches RacingWheelTotalCompressionThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelTotalCompressionThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelTotalCompressionThresholdMinusButtonMappings { get; set; } = new();

	private void UpdateRacingWheelTotalCompressionThresholdString()
	{
		RacingWheelTotalCompressionThresholdString = $"{_racingWheelTotalCompressionThreshold * DataContext.Instance.Settings.RacingWheelMaxForce:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]}";
	}

	#endregion

	#region Racing wheel - Total compression rate

	private float _racingWheelTotalCompressionRate = 0.75f;

	public float RacingWheelTotalCompressionRate
	{
		get => _racingWheelTotalCompressionRate;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

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

			if ( _racingWheelOutputMinimum == 0f )
			{
				RacingWheelOutputMinimumString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelOutputMinimumString = $"{_racingWheelOutputMinimum * 100f:F1}{DataContext.Instance.Localization[ "Percent" ]}";
			}
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

			if ( _racingWheelOutputMaximum == 1f )
			{
				RacingWheelOutputMaximumString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelOutputMaximumString = $"{_racingWheelOutputMaximum * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
			}
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

				app.LFE.NextRecordingDeviceGuid = _racingWheelLFERecordingDeviceGuid;
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
			value = Math.Clamp( value, 0f, 1f );

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

	#region Racing wheel - Crash protection g-force

	private float _racingWheelCrashProtectionGForce = 8f;

	public float RacingWheelCrashProtectionGForce
	{
		get => _racingWheelCrashProtectionGForce;

		set
		{
			value = Math.Clamp( value, 2f, 20f );

			if ( value != _racingWheelCrashProtectionGForce )
			{
				_racingWheelCrashProtectionGForce = value;

				OnPropertyChanged();
			}

			if ( _racingWheelCrashProtectionGForce == 2f )
			{
				RacingWheelCrashProtectionGForceString = DataContext.Instance.Localization[ "OFF" ];
			}
			else
			{
				RacingWheelCrashProtectionGForceString = $"{_racingWheelCrashProtectionGForce:F1}{DataContext.Instance.Localization[ "GForceUnits" ]}";
			}
		}
	}

	private string _racingWheelCrashProtectionGForceString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCrashProtectionGForceString
	{
		get => _racingWheelCrashProtectionGForceString;

		set
		{
			if ( value != _racingWheelCrashProtectionGForceString )
			{
				_racingWheelCrashProtectionGForceString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCrashProtectionGForceContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCrashProtectionGForcePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCrashProtectionGForceMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Crash protection duration

	private float _racingWheelCrashProtectionDuration = 5f;

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
				RacingWheelCrashProtectionDurationString = $"{_racingWheelCrashProtectionDuration:F1}{DataContext.Instance.Localization[ "SecondsUnits" ]}";
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
			value = Math.Clamp( value, 0f, 1f );

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
			value = Math.Clamp( value, 0f, 1f );

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
				RacingWheelCurbProtectionShockVelocityString = $"{_racingWheelCurbProtectionShockVelocity:F2}{DataContext.Instance.Localization[ "ShockVelocityUnits" ]}";
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
			value = Math.Clamp( value, 0f, 1f );

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
				RacingWheelCurbProtectionDurationString = $"{_racingWheelCurbProtectionDuration:F2}{DataContext.Instance.Localization[ "SecondsUnits" ]}";
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
			value = Math.Clamp( value, 0f, 1f );

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

	private float _racingWheelParkedStrength = 0.25f;

	public float RacingWheelParkedStrength
	{
		get => _racingWheelParkedStrength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

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

	public ContextSwitches RacingWheelParkedStrengthContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelParkedStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelParkedStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Soft lock strength

	private float _racingWheelSoftLockStrength = 0.25f;

	public float RacingWheelSoftLockStrength
	{
		get => _racingWheelSoftLockStrength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

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
			value = Math.Clamp( value, 0f, 1f );

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

			var app = App.Instance!;

			app.MainWindow.UpdateRacingWheelPowerButton();
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

	#region Pedals - Clutch effect 1 strength

	private float _pedalsClutchEffect1Strength = 1f;

	public float PedalsClutchEffect1Strength
	{
		get => _pedalsClutchEffect1Strength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsClutchEffect1Strength )
			{
				_pedalsClutchEffect1Strength = value;

				OnPropertyChanged();
			}

			PedalsClutchEffect1StrengthString = $"{_pedalsClutchEffect1Strength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsClutchEffect1StrengthString = string.Empty;

	[XmlIgnore]
	public string PedalsClutchEffect1StrengthString
	{
		get => _pedalsClutchEffect1StrengthString;

		set
		{
			if ( value != _pedalsClutchEffect1StrengthString )
			{
				_pedalsClutchEffect1StrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchEffect1StrengthContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsClutchEffect1StrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchEffect1StrengthMinusButtonMappings { get; set; } = new();

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

	#region Pedals - Clutch effect 2 strength

	private float _pedalsClutchEffect2Strength = 1f;

	public float PedalsClutchEffect2Strength
	{
		get => _pedalsClutchEffect2Strength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsClutchEffect2Strength )
			{
				_pedalsClutchEffect2Strength = value;

				OnPropertyChanged();
			}

			PedalsClutchEffect2StrengthString = $"{_pedalsClutchEffect2Strength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsClutchEffect2StrengthString = string.Empty;

	[XmlIgnore]
	public string PedalsClutchEffect2StrengthString
	{
		get => _pedalsClutchEffect2StrengthString;

		set
		{
			if ( value != _pedalsClutchEffect2StrengthString )
			{
				_pedalsClutchEffect2StrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchEffect2StrengthContextSwitches { get => PedalsClutchEffect1StrengthContextSwitches; set => PedalsClutchEffect1StrengthContextSwitches = value; }
	public ButtonMappings PedalsClutchEffect2StrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchEffect2StrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch effect 3

	private Pedals.Effect _pedalsClutchEffect3 = Pedals.Effect.SteeringEffects;

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

	private float _pedalsClutchEffect3Strength = 1f;

	public float PedalsClutchEffect3Strength
	{
		get => _pedalsClutchEffect3Strength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsClutchEffect3Strength )
			{
				_pedalsClutchEffect3Strength = value;

				OnPropertyChanged();
			}

			PedalsClutchEffect3StrengthString = $"{_pedalsClutchEffect3Strength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsClutchEffect3StrengthString = string.Empty;

	[XmlIgnore]
	public string PedalsClutchEffect3StrengthString
	{
		get => _pedalsClutchEffect3StrengthString;

		set
		{
			if ( value != _pedalsClutchEffect3StrengthString )
			{
				_pedalsClutchEffect3StrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchEffect3StrengthContextSwitches { get => PedalsClutchEffect1StrengthContextSwitches; set => PedalsClutchEffect1StrengthContextSwitches = value; }
	public ButtonMappings PedalsClutchEffect3StrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchEffect3StrengthMinusButtonMappings { get; set; } = new();

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

	#region Pedals - Brake effect 1 strength

	private float _pedalsBrakeEffect1Strength = 1f;

	public float PedalsBrakeEffect1Strength
	{
		get => _pedalsBrakeEffect1Strength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsBrakeEffect1Strength )
			{
				_pedalsBrakeEffect1Strength = value;

				OnPropertyChanged();
			}

			PedalsBrakeEffect1StrengthString = $"{_pedalsBrakeEffect1Strength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsBrakeEffect1StrengthString = string.Empty;

	[XmlIgnore]
	public string PedalsBrakeEffect1StrengthString
	{
		get => _pedalsBrakeEffect1StrengthString;

		set
		{
			if ( value != _pedalsBrakeEffect1StrengthString )
			{
				_pedalsBrakeEffect1StrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeEffect1StrengthContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsBrakeEffect1StrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsBrakeEffect1StrengthMinusButtonMappings { get; set; } = new();

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

	#region Pedals - Brake effect 2 strength

	private float _pedalsBrakeEffect2Strength = 1f;

	public float PedalsBrakeEffect2Strength
	{
		get => _pedalsBrakeEffect2Strength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsBrakeEffect2Strength )
			{
				_pedalsBrakeEffect2Strength = value;

				OnPropertyChanged();
			}

			PedalsBrakeEffect2StrengthString = $"{_pedalsBrakeEffect2Strength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsBrakeEffect2StrengthString = string.Empty;

	[XmlIgnore]
	public string PedalsBrakeEffect2StrengthString
	{
		get => _pedalsBrakeEffect2StrengthString;

		set
		{
			if ( value != _pedalsBrakeEffect2StrengthString )
			{
				_pedalsBrakeEffect2StrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeEffect2StrengthContextSwitches { get => PedalsBrakeEffect1StrengthContextSwitches; set => PedalsBrakeEffect1StrengthContextSwitches = value; }
	public ButtonMappings PedalsBrakeEffect2StrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsBrakeEffect2StrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake effect 3

	private Pedals.Effect _pedalsBrakeEffect3 = Pedals.Effect.SteeringEffects;

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

	private float _pedalsBrakeEffect3Strength = 1f;

	public float PedalsBrakeEffect3Strength
	{
		get => _pedalsBrakeEffect3Strength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsBrakeEffect3Strength )
			{
				_pedalsBrakeEffect3Strength = value;

				OnPropertyChanged();
			}

			PedalsBrakeEffect3StrengthString = $"{_pedalsBrakeEffect3Strength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsBrakeEffect3StrengthString = string.Empty;

	[XmlIgnore]
	public string PedalsBrakeEffect3StrengthString
	{
		get => _pedalsBrakeEffect3StrengthString;

		set
		{
			if ( value != _pedalsBrakeEffect3StrengthString )
			{
				_pedalsBrakeEffect3StrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeEffect3StrengthContextSwitches { get => PedalsBrakeEffect1StrengthContextSwitches; set => PedalsBrakeEffect1StrengthContextSwitches = value; }
	public ButtonMappings PedalsBrakeEffect3StrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsBrakeEffect3StrengthMinusButtonMappings { get; set; } = new();

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

	#region Pedals - Throttle effect 1 strength

	private float _pedalsThrottleEffect1Strength = 1f;

	public float PedalsThrottleEffect1Strength
	{
		get => _pedalsThrottleEffect1Strength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsThrottleEffect1Strength )
			{
				_pedalsThrottleEffect1Strength = value;

				OnPropertyChanged();
			}

			PedalsThrottleEffect1StrengthString = $"{_pedalsThrottleEffect1Strength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsThrottleEffect1StrengthString = string.Empty;

	[XmlIgnore]
	public string PedalsThrottleEffect1StrengthString
	{
		get => _pedalsThrottleEffect1StrengthString;

		set
		{
			if ( value != _pedalsThrottleEffect1StrengthString )
			{
				_pedalsThrottleEffect1StrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleEffect1StrengthContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsThrottleEffect1StrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsThrottleEffect1StrengthMinusButtonMappings { get; set; } = new();

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

	#region Pedals - Throttle effect 2 strength

	private float _pedalsThrottleEffect2Strength = 1f;

	public float PedalsThrottleEffect2Strength
	{
		get => _pedalsThrottleEffect2Strength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsThrottleEffect2Strength )
			{
				_pedalsThrottleEffect2Strength = value;

				OnPropertyChanged();
			}

			PedalsThrottleEffect2StrengthString = $"{_pedalsThrottleEffect2Strength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsThrottleEffect2StrengthString = string.Empty;

	[XmlIgnore]
	public string PedalsThrottleEffect2StrengthString
	{
		get => _pedalsThrottleEffect2StrengthString;

		set
		{
			if ( value != _pedalsThrottleEffect2StrengthString )
			{
				_pedalsThrottleEffect2StrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleEffect2StrengthContextSwitches { get => PedalsThrottleEffect1StrengthContextSwitches; set => PedalsThrottleEffect1StrengthContextSwitches = value; }
	public ButtonMappings PedalsThrottleEffect2StrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsThrottleEffect2StrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle effect 3

	private Pedals.Effect _pedalsThrottleEffect3 = Pedals.Effect.None;

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

	private float _pedalsThrottleEffect3Strength = 1f;

	public float PedalsThrottleEffect3Strength
	{
		get => _pedalsThrottleEffect3Strength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsThrottleEffect3Strength )
			{
				_pedalsThrottleEffect3Strength = value;

				OnPropertyChanged();
			}

			PedalsThrottleEffect3StrengthString = $"{_pedalsThrottleEffect3Strength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _pedalsThrottleEffect3StrengthString = string.Empty;

	[XmlIgnore]
	public string PedalsThrottleEffect3StrengthString
	{
		get => _pedalsThrottleEffect3StrengthString;

		set
		{
			if ( value != _pedalsThrottleEffect3StrengthString )
			{
				_pedalsThrottleEffect3StrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleEffect3StrengthContextSwitches { get => PedalsThrottleEffect1StrengthContextSwitches; set => PedalsThrottleEffect1StrengthContextSwitches = value; }
	public ButtonMappings PedalsThrottleEffect3StrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsThrottleEffect3StrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into gear frequency

	private float _pedalsShiftIntoGearFrequency = 0.3f;

	public float PedalsShiftIntoGearFrequency
	{
		get => _pedalsShiftIntoGearFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsShiftIntoGearFrequency )
			{
				_pedalsShiftIntoGearFrequency = value;

				OnPropertyChanged();
			}

			PedalsShiftIntoGearFrequencyString = $"{_pedalsShiftIntoGearFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
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
			value = Math.Clamp( value, 0f, 1f );

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

			PedalsShiftIntoGearDurationString = $"{_pedalsShiftIntoGearDuration:F2}{DataContext.Instance.Localization[ "SecondsUnits" ]}";
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
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsShiftIntoNeutralFrequency )
			{
				_pedalsShiftIntoNeutralFrequency = value;

				OnPropertyChanged();
			}

			PedalsShiftIntoNeutralFrequencyString = $"{_pedalsShiftIntoNeutralFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
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
			value = Math.Clamp( value, 0f, 1f );

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

			PedalsShiftIntoNeutralDurationString = $"{_pedalsShiftIntoNeutralDuration:F2}{DataContext.Instance.Localization[ "SecondsUnits" ]}";
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
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsABSEngagedFrequency )
			{
				_pedalsABSEngagedFrequency = value;

				OnPropertyChanged();
			}

			PedalsABSEngagedFrequencyString = $"{_pedalsABSEngagedFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
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
			value = Math.Clamp( value, 0f, 1f );

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

	private float _pedalsStartingRPM = 0f;

	public float PedalsStartingRPM
	{
		get => _pedalsStartingRPM;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

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

	#region Pedals - Clutch slip start

	private float _pedalsClutchSlipStart = 0.25f;

	public float PedalsClutchSlipStart
	{
		get => _pedalsClutchSlipStart;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

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
			value = Math.Clamp( value, 0f, 1f );

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

			PedalsClutchSlipFrequencyString = $"{_pedalsClutchSlipFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
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

	public ContextSwitches PedalsClutchSlipFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsClutchSlipFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchSlipFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Minimum frequency

	private float _pedalsMinimumFrequency = 15f;

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
			}

			PedalsMinimumFrequencyString = $"{_pedalsMinimumFrequency:F0}{DataContext.Instance.Localization[ "HertzUnits" ]}";
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
			}

			PedalsMaximumFrequencyString = $"{_pedalsMaximumFrequency:F0}{DataContext.Instance.Localization[ "HertzUnits" ]}";
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
			value = Math.Clamp( value, 0f, 1f );

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
			value = Math.Clamp( value, 0f, 1f );

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

	#region Pedals - Noise damper

	private float _pedalsNoiseDamper = 0.1f;

	public float PedalsNoiseDamper
	{
		get => _pedalsNoiseDamper;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

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
			value = Math.Clamp( value, 0f, 1f );

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

	#region AdminBoxx - Black flag R

	private float _adminBoxxBlackFlagR = 0.25f;

	public float AdminBoxxBlackFlagR
	{
		get => _adminBoxxBlackFlagR;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _adminBoxxBlackFlagR )
			{
				_adminBoxxBlackFlagR = value;

				OnPropertyChanged();
			}

			AdminBoxxBlackFlagRString = $"{_adminBoxxBlackFlagR * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _adminBoxxBlackFlagRString = string.Empty;

	[XmlIgnore]
	public string AdminBoxxBlackFlagRString
	{
		get => _adminBoxxBlackFlagRString;

		set
		{
			if ( value != _adminBoxxBlackFlagRString )
			{
				_adminBoxxBlackFlagRString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings AdminBoxxBlackFlagRPlusButtonMappings { get; set; } = new();
	public ButtonMappings AdminBoxxBlackFlagRMinusButtonMappings { get; set; } = new();

	#endregion

	#region AdminBoxx - Black flag G

	private float _adminBoxxBlackFlagG = 0f;

	public float AdminBoxxBlackFlagG
	{
		get => _adminBoxxBlackFlagG;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _adminBoxxBlackFlagG )
			{
				_adminBoxxBlackFlagG = value;

				OnPropertyChanged();
			}

			AdminBoxxBlackFlagGString = $"{_adminBoxxBlackFlagG * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _adminBoxxBlackFlagGString = string.Empty;

	[XmlIgnore]
	public string AdminBoxxBlackFlagGString
	{
		get => _adminBoxxBlackFlagGString;

		set
		{
			if ( value != _adminBoxxBlackFlagGString )
			{
				_adminBoxxBlackFlagGString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings AdminBoxxBlackFlagGPlusButtonMappings { get; set; } = new();
	public ButtonMappings AdminBoxxBlackFlagGMinusButtonMappings { get; set; } = new();

	#endregion

	#region AdminBoxx - Black flag B

	private float _adminBoxxBlackFlagB = 0.25f;

	public float AdminBoxxBlackFlagB
	{
		get => _adminBoxxBlackFlagB;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _adminBoxxBlackFlagB )
			{
				_adminBoxxBlackFlagB = value;

				OnPropertyChanged();
			}

			AdminBoxxBlackFlagBString = $"{_adminBoxxBlackFlagB * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _adminBoxxBlackFlagBString = string.Empty;

	[XmlIgnore]
	public string AdminBoxxBlackFlagBString
	{
		get => _adminBoxxBlackFlagBString;

		set
		{
			if ( value != _adminBoxxBlackFlagBString )
			{
				_adminBoxxBlackFlagBString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings AdminBoxxBlackFlagBPlusButtonMappings { get; set; } = new();
	public ButtonMappings AdminBoxxBlackFlagBMinusButtonMappings { get; set; } = new();

	#endregion

	#region AdminBoxx - Volume

	private float _adminBoxxVolume = 0.75f;

	public float AdminBoxxVolume
	{
		get => _adminBoxxVolume;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

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
			value = Math.Clamp( value, 0.25f, 4f );

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

	#region Debug (temporary)

	public ButtonMappings DebugAlanLeResetMappings { get; set; } = new();
	public ButtonMappings DebugAlanLeDumpMappings { get; set; } = new();

	#endregion
}
