
namespace MarvinsAIRARefactored.DataContext;

public class Context : IComparable<Context?>
{
	public static string DefaultContextName { get; } = "Default";
	public static string WetContextName { get; } = "Wet";
	public static string DryContextName { get; } = "Dry";

	public Guid WheelbaseGuid { get; set; } = Guid.Empty;

	public string CarName { get; set; } = DefaultContextName;
	public string TrackName { get; set; } = DefaultContextName;
	public string TrackConfigurationName { get; set; } = DefaultContextName;
	public string WetDryName { get; set; } = DryContextName;

	public Context()
	{
	}

	public Context( ContextSwitches contextSwitches )
	{
		var app = App.Instance!;

		if ( contextSwitches.PerWheelbase )
		{
			WheelbaseGuid = DataContext.Instance.Settings.RacingWheelSteeringDeviceGuid;
		}

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
	}

	public int CompareTo( Context? other )
	{
		if ( other == null ) return 1;

		if ( WheelbaseGuid != other.WheelbaseGuid ) return WheelbaseGuid.CompareTo( other.WheelbaseGuid );
		if ( CarName != other.CarName ) return CarName.CompareTo( other.CarName );
		if ( TrackName != other.TrackName ) return TrackName.CompareTo( other.TrackName );
		if ( TrackConfigurationName != other.TrackConfigurationName ) return TrackConfigurationName.CompareTo( other.TrackConfigurationName );

		return WetDryName.CompareTo( other.WetDryName );
	}
}
