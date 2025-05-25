
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MarvinsAIRARefactored.Components;

public class Settings : INotifyPropertyChanged
{
	public enum RacingWheelAlgorithmEnum
	{
		Native60Hz,
		Native360Hz
	};

	public event PropertyChangedEventHandler? PropertyChanged;

	public void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
	{
		if ( propertyName != null )
		{
			var app = App.Instance;

			if ( app != null )
			{
				var property = this.GetType().GetProperty( propertyName );

				if ( property != null )
				{
					app.Logger.WriteLine( $"[Settings] {propertyName} = {property.GetValue( this )}" );
				}

				app.SettingsFile.QueueForSerialization = true;
			}
		}

		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
	}

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

				var app = App.Instance;

				app?.MainWindow.UpdateRacingWheelPowerIcon();
			}
		}
	}

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

			RacingWheelMaxForceString = $"{_racingWheelMaxForce:F1} {DataContext.Instance.Localization[ "TorqueUnits" ]}";
		}
	}

	private string _racingWheelMaxForceString = $"{50f:F1} {DataContext.Instance.Localization[ "TorqueUnits" ]}";

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

	private float _racingWheelDetailStrength = 1f;

	public float RacingWheelDetailStrength
	{
		get => _racingWheelDetailStrength;

		set
		{
			value = Math.Clamp( value, 0f, 9.99f );

			if ( value != _racingWheelDetailStrength )
			{
				_racingWheelDetailStrength = value;

				OnPropertyChanged();
			}

			RacingWheelDetailStrengthString = $"{_racingWheelDetailStrength * 100f:F0}%";
		}
	}

	private string _racingWheelDetailStrengthString = "100%";

	public string RacingWheelDetailStrengthString
	{
		get => _racingWheelDetailStrengthString;

		set
		{
			if ( value != _racingWheelDetailStrengthString )
			{
				_racingWheelDetailStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

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
		}
	}

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
				RacingWheelParkedStrengthString = $"{_racingWheelParkedStrength * 100f:F0}%";
			}
		}
	}

	private string _racingWheelParkedStrengthString = "25%";

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
				RacingWheelSoftLockStrengthString = $"{_racingWheelSoftLockStrength * 100f:F0}%";
			}
		}
	}

	private string _racingWheelSoftLockStrengthString = "25%";

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
				RacingWheelFrictionString = $"{_racingWheelFriction * 100f:F0}%";
			}
		}
	}

	private string _racingWheelFrictionString = DataContext.Instance.Localization[ "OFF" ];

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

				var app = App.Instance;

				if ( app != null )
				{
					app.MainWindow.Topmost = _racingWheelFadeEnabled;

					app.MainWindow.UpdateRacingWheelFadeEnabledIcon();
				}
			}
		}
	}

	private string _currentLanguageCode = "default";

	public string CurrentLanguageCode
	{
		get => _currentLanguageCode;

		set
		{
			if ( value != _currentLanguageCode )
			{
				_currentLanguageCode = value;

				DataContext.Instance.Localization.LoadLanguage( value );

				OnPropertyChanged( null );
			}
		}
	}

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

				var app = App.Instance;

				if ( app != null )
				{
					app.MainWindow.Topmost = _appTopmostWindowEnabled;

					app.MainWindow.UpdateAppTopmostWindowEnabledIcon();
				}
			}
		}
	}
}
