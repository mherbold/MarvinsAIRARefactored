
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

	public virtual void Tick( App app )
	{
	}
}
