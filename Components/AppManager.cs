
using System.Diagnostics;
using System.IO;

namespace MarvinsAIRARefactored.Components;

public class AppManager
{
	private CancellationTokenSource? _cancellationTokenSource;

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AppManager] Shutdown >>>" );

		CancelStartupSequence();

		app.Logger.WriteLine( "[AppManager] <<< Shutdown" );
	}

	public void SimulatorConnected()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AppManager] SimulatorConnected >>>" );

		var settings = DataContext.DataContext.Instance.Settings;

		if ( !settings.AppManagerEnabled )
		{
			app.Logger.WriteLine( "[AppManager] App Manager is disabled - skipping" );
			app.Logger.WriteLine( "[AppManager] <<< SimulatorConnected" );

			return;
		}

		CancelStartupSequence();

		TerminateApps();

		_cancellationTokenSource = new CancellationTokenSource();

		_ = Task.Run( () => StartAppsAsync( _cancellationTokenSource.Token ) );

		app.Logger.WriteLine( "[AppManager] <<< SimulatorConnected" );
	}

	public void SimulatorDisconnected()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AppManager] SimulatorDisconnected >>>" );

		CancelStartupSequence();

		var settings = DataContext.DataContext.Instance.Settings;

		if ( settings.AppManagerEnabled )
		{
			TerminateStartedApps();
		}

		app.Logger.WriteLine( "[AppManager] <<< SimulatorDisconnected" );
	}

	private void CancelStartupSequence()
	{
		if ( _cancellationTokenSource != null )
		{
			_cancellationTokenSource.Cancel();
			_cancellationTokenSource.Dispose();
			_cancellationTokenSource = null;
		}
	}

	private void TerminateApps()
	{
		var app = App.Instance!;

		var settings = DataContext.DataContext.Instance.Settings;

		foreach ( var entry in settings.AppManagerTerminateList )
		{
			if ( string.IsNullOrWhiteSpace( entry.ExecutablePath ) )
			{
				continue;
			}

			var processName = Path.GetFileNameWithoutExtension( entry.ExecutablePath );

			app.Logger.WriteLine( $"[AppManager] Terminating processes named '{processName}'" );

			var processes = Process.GetProcessesByName( processName );

			foreach ( var process in processes )
			{
				try
				{
					app.Logger.WriteLine( $"[AppManager] Killing process {process.Id} ({processName})" );

					process.Kill( entireProcessTree: true );
				}
				catch ( Exception ex )
				{
					app.Logger.WriteLine( $"[AppManager] ERROR killing process {process.Id}: {ex.Message}" );
				}
				finally
				{
					process.Dispose();
				}
			}
		}
	}

	private void TerminateStartedApps()
	{
		var app = App.Instance!;

		var settings = DataContext.DataContext.Instance.Settings;

		foreach ( var entry in settings.AppManagerStartList )
		{
			if ( !entry.TerminateOnSimulatorDisconnect )
			{
				continue;
			}

			if ( string.IsNullOrWhiteSpace( entry.ExecutablePath ) )
			{
				continue;
			}

			var processName = Path.GetFileNameWithoutExtension( entry.ExecutablePath );

			app.Logger.WriteLine( $"[AppManager] Terminating started app '{processName}' on simulator disconnect" );

			var processes = Process.GetProcessesByName( processName );

			foreach ( var process in processes )
			{
				try
				{
					app.Logger.WriteLine( $"[AppManager] Killing process {process.Id} ({processName})" );

					process.Kill( entireProcessTree: true );
				}
				catch ( Exception ex )
				{
					app.Logger.WriteLine( $"[AppManager] ERROR killing process {process.Id}: {ex.Message}" );
				}
				finally
				{
					process.Dispose();
				}
			}
		}
	}

	private async Task StartAppsAsync( CancellationToken cancellationToken )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AppManager] StartAppsAsync >>>" );

		var settings = DataContext.DataContext.Instance.Settings;

		foreach ( var entry in settings.AppManagerStartList )
		{
			if ( cancellationToken.IsCancellationRequested )
			{
				app.Logger.WriteLine( "[AppManager] Startup sequence cancelled" );

				break;
			}

			if ( string.IsNullOrWhiteSpace( entry.ExecutablePath ) )
			{
				continue;
			}

			var processName = Path.GetFileNameWithoutExtension( entry.ExecutablePath );

			var existingProcesses = Process.GetProcessesByName( processName );

			if ( existingProcesses.Length > 0 )
			{
				app.Logger.WriteLine( $"[AppManager] '{processName}' is already running - skipping" );

				foreach ( var p in existingProcesses )
				{
					p.Dispose();
				}
			}
			else
			{
				app.Logger.WriteLine( $"[AppManager] Starting '{entry.ExecutablePath}'" );

				try
				{
					var startInfo = new ProcessStartInfo
					{
						FileName = entry.ExecutablePath,
						Arguments = entry.Arguments,
						WindowStyle = entry.WindowStyle,
						UseShellExecute = true
					};

					var process = Process.Start( startInfo );

					if ( process != null )
					{
						try
						{
							process.WaitForInputIdle( 5000 );

							process.PriorityClass = entry.CpuPriority;

							app.Logger.WriteLine( $"[AppManager] Started '{processName}' (PID {process.Id}), priority={entry.CpuPriority}, windowStyle={entry.WindowStyle}" );
						}
						catch ( Exception ex )
						{
							app.Logger.WriteLine( $"[AppManager] WARNING: Could not set priority for '{processName}': {ex.Message}" );
						}
						finally
						{
							process.Dispose();
						}
					}
					else
					{
						app.Logger.WriteLine( $"[AppManager] WARNING: Process.Start returned null for '{entry.ExecutablePath}'" );
					}
				}
				catch ( Exception ex )
				{
					app.Logger.WriteLine( $"[AppManager] ERROR starting '{entry.ExecutablePath}': {ex.Message}" );
				}
			}

			if ( cancellationToken.IsCancellationRequested )
			{
				app.Logger.WriteLine( "[AppManager] Startup sequence cancelled" );

				break;
			}

			if ( entry.DelayAfterStartSeconds > 0f )
			{
				app.Logger.WriteLine( $"[AppManager] Waiting {entry.DelayAfterStartSeconds:F1}s before next app" );

				try
				{
					await Task.Delay( TimeSpan.FromSeconds( entry.DelayAfterStartSeconds ), cancellationToken );
				}
				catch ( TaskCanceledException )
				{
					app.Logger.WriteLine( "[AppManager] Startup sequence cancelled during delay" );

					break;
				}
			}
		}

		app.Logger.WriteLine( "[AppManager] <<< StartAppsAsync" );
	}
}
