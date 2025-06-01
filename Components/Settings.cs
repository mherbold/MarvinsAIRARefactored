
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace MarvinsAIRARefactored.Components;

public class Settings : INotifyPropertyChanged
{
	#region Button Mappings class

	public class ButtonMappings
	{
		public class MappedButton
		{
			public class Button
			{
				public string DeviceProductName { get; set; } = string.Empty;
				public Guid DeviceInstanceGuid { get; set; } = Guid.Empty;
				public int ButtonNumber { get; set; } = 0;
			}

			public Button HoldButton { get; set; } = new();
			public Button ClickButton { get; set; } = new();
		}

		public List<MappedButton> MappedButtons { get; } = [];
	}

	#endregion

	#region INotifyProperty stuff

	public event PropertyChangedEventHandler? PropertyChanged;

	public void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
	{
		var app = App.Instance;

		if ( app != null )
		{
			if ( ( propertyName != null ) && !propertyName.EndsWith( "String" ) )
			{
				var property = this.GetType().GetProperty( propertyName );

				if ( property != null )
				{
					app.Logger.WriteLine( $"[Settings] {propertyName} = {property.GetValue( this )}" );
				}
			}

			app.SettingsFile.QueueForSerialization = true;
		}

		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
	}

	#endregion

	#region Racing wheel - Device

	private Guid _racingWheelDeviceGuid = Guid.Empty;

	public Guid RacingWheelDeviceGuid
	{
		get => _racingWheelDeviceGuid;

		set
		{
			if ( value != _racingWheelDeviceGuid )
			{
				_racingWheelDeviceGuid = value;

				OnPropertyChanged();

				var app = App.Instance;

				if ( app != null )
				{
					app.RacingWheel.NextRacingWheelGuid = _racingWheelDeviceGuid;
				}
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

			var app = App.Instance;

			app?.MainWindow.UpdateRacingWheelPowerButton();
		}
	}

	public ButtonMappings RacingWheelPowerButtonMappings { get; set; } = new();

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

			RacingWheelMaxForceString = $"{_racingWheelMaxForce:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]}";
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

	public ButtonMappings RacingWheelMaxForcePlusButtonMappings { get; set; } = new();

	public ButtonMappings RacingWheelMaxForceMinusButtonMappings { get; set; } = new();

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

	public ButtonMappings RacingWheelSoftLockStrengthPlusButtonMappings { get; set; } = new();

	public ButtonMappings RacingWheelSoftLockStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Algorithm

	public enum RacingWheelAlgorithmEnum
	{
		Native60Hz,
		Native360Hz,
		DetailBooster,
		DeltaLimiter,
		DetailBoosterOn60Hz,
		DeltaLimiterOn60Hz,
		ZeAlanLeTwist,
	};

	private RacingWheelAlgorithmEnum _racingWheelAlgorithm = RacingWheelAlgorithmEnum.Native360Hz;

	public RacingWheelAlgorithmEnum RacingWheelAlgorithm
	{
		get => _racingWheelAlgorithm;

		set
		{
			if ( value != _racingWheelAlgorithm )
			{
				_racingWheelAlgorithm = value;

				OnPropertyChanged();
			}

			var app = App.Instance;

			app?.MainWindow.UpdateRacingWheelAlgorithmControls();
		}
	}

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

	public ButtonMappings RacingWheelDetailBoostPlusButtonMappings { get; set; } = new();

	public ButtonMappings RacingWheelDetailBoostMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Delta limit

	private float _racingWheelDeltaLimit = 1f;

	public float RacingWheelDeltaLimit
	{
		get => _racingWheelDeltaLimit;

		set
		{
			value = Math.Clamp( value, 0f, 99.99f );

			if ( value != _racingWheelDeltaLimit )
			{
				_racingWheelDeltaLimit = value;

				OnPropertyChanged();
			}

			RacingWheelDeltaLimitString = $"{_racingWheelDeltaLimit:F2}{DataContext.Instance.Localization[ "DeltaLimitUnits" ]}";
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

	public ButtonMappings RacingWheelDeltaLimitPlusButtonMappings { get; set; } = new();

	public ButtonMappings RacingWheelDeltaLimitMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Bias

	private float _racingWheelBias = 0.1f;

	public float RacingWheelBias
	{
		get => _racingWheelBias;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _racingWheelBias )
			{
				_racingWheelBias = value;

				OnPropertyChanged();
			}

			RacingWheelBiasString = $"{_racingWheelBias * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	private string _racingWheelBiasString = string.Empty;

	[XmlIgnore]
	public string RacingWheelBiasString
	{
		get => _racingWheelBiasString;

		set
		{
			if ( value != _racingWheelBiasString )
			{
				_racingWheelBiasString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings RacingWheelBiasPlusButtonMappings { get; set; } = new();

	public ButtonMappings RacingWheelBiasMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Compression rate

	private float _racingWheelCompressionRate = 0.1f;

	public float RacingWheelCompressionRate
	{
		get => _racingWheelCompressionRate;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _racingWheelCompressionRate )
			{
				_racingWheelCompressionRate = value;

				OnPropertyChanged();
			}

			RacingWheelCompressionRateString = $"{_racingWheelCompressionRate * 100f:F0}{DataContext.Instance.Localization[ "CompressionRateUnits" ]}";
		}
	}

	private string _racingWheelCompressionRateString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCompressionRateString
	{
		get => _racingWheelCompressionRateString;

		set
		{
			if ( value != _racingWheelCompressionRateString )
			{
				_racingWheelCompressionRateString = value;

				OnPropertyChanged();
			}
		}
	}

	public ButtonMappings RacingWheelCompressionRatePlusButtonMappings { get; set; } = new();

	public ButtonMappings RacingWheelCompressionRateMinusButtonMappings { get; set; } = new();

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

				var app = App.Instance;

				app?.MainWindow.RefreshWindow();

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

			var app = App.Instance;

			if ( app != null )
			{
				app.MainWindow.Topmost = _appTopmostWindowEnabled;
			}
		}
	}

	#endregion
}
