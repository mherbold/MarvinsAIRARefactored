
using IRSDKSharper;

namespace MarvinsAIRARefactored.Components;

public class Drivers
{
	public class DriverInfo
	{
		public int CarIdx { get; set; }
		public string UserName { get; set; } = string.Empty;
		public string CarNumber { get; set; } = string.Empty;
		public bool CarIsPaceCar { get; set; }
		public int IRating { get; set; }
		public bool IsSpectator { get; set; }
	}

	private List<DriverInfo> _drivers = [];
	private Dictionary<int, DriverInfo> _driversByCarIdx = [];

	public IReadOnlyList<DriverInfo> DriversList => _drivers;

	public bool TryGetDriverByCarIdx( int carIdx, out DriverInfo? driver )
	{
		return _driversByCarIdx.TryGetValue( carIdx, out driver );
	}

	public void Update( IRacingSdkSessionInfo sessionInfo )
	{
		var newList = new List<DriverInfo>();
		var newLookup = new Dictionary<int, DriverInfo>();

		if ( sessionInfo?.DriverInfo?.Drivers != null )
		{
			foreach ( var d in sessionInfo.DriverInfo.Drivers )
			{
				if ( d == null )
				{
					continue;
				}

				var info = new DriverInfo
				{
					CarIdx = d.CarIdx,
					UserName = d.UserName ?? string.Empty,
					CarNumber = d.CarNumber ?? string.Empty,
					CarIsPaceCar = d.CarIsPaceCar != 0,
					IRating = d.IRating,
					IsSpectator = d.IsSpectator != 0,
				};

				newList.Add( info );

				// If duplicate CarIdx entries exist, last one wins
				newLookup[ info.CarIdx ] = info;
			}
		}

		// Swap in the new collections (atomic-ish)
		_drivers = newList;
		_driversByCarIdx = newLookup;
	}
}
