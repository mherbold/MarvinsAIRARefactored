
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
					// app.Logger.WriteLine( $"[ContextSwitches] {propertyName} = {property.GetValue( this )}" );
				}
			}

			app.SettingsFile.QueueForSerialization = true;
		}

		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
	}

	#endregion

	public ContextSwitches()
	{
		PerCar = false;
		PerTrack = false;
		PerTrackConfiguration = false;
		PerWetDry = false;

		_initializing = false;
	}

	public ContextSwitches( bool perCar, bool perTrack, bool perTrackConfiguration, bool perWetDry )
	{
		PerCar = perCar;
		PerTrack = perTrack;
		PerTrackConfiguration = perTrackConfiguration;
		PerWetDry = perWetDry;

		_initializing = false;
	}

	#region Hierarchy

	// The context dimensions form a hierarchy: a per track scope only means something inside a per car scope, and a
	// per track configuration scope only means something inside a per track scope (per wet/dry is independent of all
	// of them). Turns any orphaned child dimension off and returns true when something had to be turned off. This is
	// deliberately NOT enforced inside the property setters - deserialization has to be able to load an illegal
	// combination so the load-time migration can spot it, fix it, and re-serialize the settings file.
	public bool Normalize()
	{
		var changed = false;

		if ( !PerCar && ( PerTrack || PerTrackConfiguration ) )
		{
			PerTrack = false;
			PerTrackConfiguration = false;

			changed = true;
		}

		if ( !PerTrack && PerTrackConfiguration )
		{
			PerTrackConfiguration = false;

			changed = true;
		}

		return changed;
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
