
using System.Diagnostics;

using IRSDKSharper;

namespace MarvinsAIRARefactored.GameBridges;

public abstract class GameBridgeAdapter
{
	public abstract string GameName { get; }
	public abstract string LocalizationKey { get; }
	public abstract string[] ProcessNames { get; }
	public abstract GameBridgeCapabilities Capabilities { get; }

	public virtual bool IsImplemented => false;

	public IRacingSdkInMemoryDataSource? DataSource { get; protected set; } = null;

	// stamped with the pump clock every time the game's own physics output actually advances (each adapter's
	// packet id / elapsed time / simulation time gate) - the shared memory maps stay readable while the game
	// is paused or sitting in a menu, and the bridge keeps committing frames with the last held values, so a
	// frozen stamp is the only reliable signal that the game has stopped producing telemetry
	public double LastDataActivitySeconds { get; protected set; } = double.MinValue;

	public bool IsGameRunning
	{
		get
		{
			foreach ( var processName in ProcessNames )
			{
				var processes = Process.GetProcessesByName( processName );

				var found = processes.Length > 0;

				foreach ( var process in processes )
				{
					process.Dispose();
				}

				if ( found )
				{
					return true;
				}
			}

			return false;
		}
	}

	public virtual void Start()
	{
	}

	public virtual void Stop()
	{
	}

	// called from the multimedia timer worker thread (~500 Hz, kernel-scheduled) immediately before the
	// racing wheel update - implementations take their 360 Hz sub-samples here on a precise clock instead
	// of pacing their own thread with Sleep(1)
	public virtual void Pump( double totalSeconds )
	{
	}

	public virtual void Tick( App app )
	{
	}
}
