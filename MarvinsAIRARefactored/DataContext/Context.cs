
namespace MarvinsAIRARefactored.DataContext;

public class Context : IComparable<Context?>
{
	public static string DefaultContextName { get; } = "Default";
	public static string WetContextName { get; } = "Wet";
	public static string DryContextName { get; } = "Dry";
	public static string IRacingGameName { get; } = "iRacing";

	// The game the car / track names belong to - the game bridge feeds other sims through the same pipeline, and
	// their cars and tracks can share a name with an iRacing one (the Ferrari 499P, Spa, ...). It qualifies those
	// names, so it is only set when at least one of them is; the default and weather-only buckets stay shared by
	// every game. Settings files from before the game dimension carry no GameName element, so their buckets load as
	// iRacing - and an iRacing context is never written out with one, so it serializes exactly as it always did.
	public string GameName { get; set; } = IRacingGameName;

	public bool ShouldSerializeGameName() => GameName != IRacingGameName;

	public string CarName { get; set; } = DefaultContextName;
	public string TrackName { get; set; } = DefaultContextName;
	public string TrackConfigurationName { get; set; } = DefaultContextName;
	public string WetDryName { get; set; } = DryContextName;

	// Legacy - the per-wheelbase context dimension was retired. The property remains so older settings
	// files still deserialize their wheelbase guids and stay distinct through the dictionary load;
	// Settings.ConsolidateLegacyWheelbaseContexts then merges the buckets (the currently selected
	// steering device's bucket wins) and re-keys them without the guid. Never written back out.
	public Guid WheelbaseGuid { get; set; } = Guid.Empty;

	public bool ShouldSerializeWheelbaseGuid() => false;

	public Context()
	{
	}

	public Context( ContextSwitches contextSwitches )
	{
		var app = App.Instance!;

		if ( contextSwitches.PerCar )
		{
			if ( ( app.Simulator.IsConnected ) && ( app.Simulator.CarScreenName != string.Empty ) )
			{
				CarName = app.Simulator.CarScreenName;
			}
		}

		if ( contextSwitches.PerTrack )
		{
			if ( ( app.Simulator.IsConnected ) && ( app.Simulator.TrackDisplayName != string.Empty ) )
			{
				TrackName = app.Simulator.TrackDisplayName;
			}
		}

		if ( contextSwitches.PerTrackConfiguration )
		{
			if ( ( app.Simulator.IsConnected ) && ( app.Simulator.TrackConfigName != string.Empty ) )
			{
				TrackConfigurationName = app.Simulator.TrackConfigName;
			}
		}

		if ( contextSwitches.PerWetDry )
		{
			if ( app.Simulator.IsConnected )
			{
				WetDryName = app.Simulator.WeatherDeclaredWet ? Context.WetContextName : Context.DryContextName;
			}
		}

		if ( HasNamedCarOrTrack )
		{
			GameName = app.Simulator.ContextGameName;
		}
	}

	// Whether any of the dimensions the game qualifies holds a real name.
	public bool HasNamedCarOrTrack { get => ( CarName != DefaultContextName ) || ( TrackName != DefaultContextName ) || ( TrackConfigurationName != DefaultContextName ); }

	public bool IsIRacing { get => GameName == IRacingGameName; }

	// The same car / track / configuration / weather in another game.
	public Context WithGameName( string gameName )
	{
		return new Context
		{
			GameName = gameName,
			CarName = CarName,
			TrackName = TrackName,
			TrackConfigurationName = TrackConfigurationName,
			WetDryName = WetDryName
		};
	}

	public int CompareTo( Context? other )
	{
		if ( other == null ) return 1;

		if ( GameName != other.GameName ) return GameName.CompareTo( other.GameName );
		if ( CarName != other.CarName ) return CarName.CompareTo( other.CarName );
		if ( TrackName != other.TrackName ) return TrackName.CompareTo( other.TrackName );
		if ( TrackConfigurationName != other.TrackConfigurationName ) return TrackConfigurationName.CompareTo( other.TrackConfigurationName );
		if ( WetDryName != other.WetDryName ) return WetDryName.CompareTo( other.WetDryName );

		// inert after consolidation (every live context carries Guid.Empty) - only keeps legacy buckets
		// distinct between deserialization and ConsolidateLegacyWheelbaseContexts
		return WheelbaseGuid.CompareTo( other.WheelbaseGuid );
	}
}
