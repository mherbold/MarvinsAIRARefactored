
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MarvinsAIRARefactored.DataContext;

public class ContextSwitches : INotifyPropertyChanged
{
	#region Point to settings

	public static DataContext DataContext { get => DataContext.Instance; }

	#endregion

	#region Initializing

	private bool _initializing = true;

	#endregion

	#region INotifyProperty stuff

	public event PropertyChangedEventHandler? PropertyChanged;

	public void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
	{
		if ( !_initializing )
		{
			var app = App.Instance!;

			if ( propertyName != null )
			{
				var property = GetType().GetProperty( propertyName );

				if ( property != null )
				{
					app.Logger.WriteLine( $"[ContextSwitches] {propertyName} = {property.GetValue( this )}" );
				}
			}

			app.SettingsFile.QueueForSerialization = true;
		}

		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
	}

	#endregion

	public ContextSwitches()
	{
		PerWheelbase = false;
		PerCar = false;
		PerTrack = false;
		PerTrackConfiguration = false;
		PerWetDry = false;

		_initializing = false;
	}

	public ContextSwitches( bool perWheelbase, bool perCar, bool perTrack, bool perTrackConfiguration, bool perWetDry )
	{
		PerWheelbase = perWheelbase;
		PerCar = perCar;
		PerTrack = perTrack;
		PerTrackConfiguration = perTrackConfiguration;
		PerWetDry = perWetDry;

		_initializing = false;
	}

	#region Per wheelbase

	private bool _perWheelbase;

	public bool PerWheelbase
	{
		get => _perWheelbase;

		set
		{
			if ( value != _perWheelbase )
			{
				_perWheelbase = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Per car

	private bool _perCar;

	public bool PerCar
	{
		get => _perCar;

		set
		{
			if ( value != _perCar )
			{
				_perCar = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Per track

	private bool _perTrack;

	public bool PerTrack
	{
		get => _perTrack;

		set
		{
			if ( value != _perTrack )
			{
				_perTrack = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Per track configuration

	private bool _perTrackConfiguration;

	public bool PerTrackConfiguration
	{
		get => _perTrackConfiguration;

		set
		{
			if ( value != _perTrackConfiguration )
			{
				_perTrackConfiguration = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Per wet/dry condition

	private bool _perWetDry;

	public bool PerWetDry
	{
		get => _perWetDry;

		set
		{
			if ( value != _perWetDry )
			{
				_perWetDry = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion
}
