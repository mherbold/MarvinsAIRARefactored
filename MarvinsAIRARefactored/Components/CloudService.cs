
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Windows;

using Newtonsoft.Json;

namespace MarvinsAIRARefactored.Components;

public class CloudService
{
	public Guid NetworkIdGuid { get; private set; } = Guid.Empty;

	public bool CheckingForUpdate { get; private set; } = false;
	public bool DownloadingUpdate { get; private set; } = false;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[CloudService] Initialize >>>" );

		var networkInterfaceList = NetworkInterface.GetAllNetworkInterfaces();

		var networkInterface = networkInterfaceList.FirstOrDefault();

		if ( networkInterface != null )
		{
			if ( Guid.TryParse( networkInterface.Id, out var networkIdGuid ) )
			{
				NetworkIdGuid = networkIdGuid;

				app.Logger.WriteLine( $"[CloudService] Network ID = {NetworkIdGuid}" );
			}
		}

		app.Logger.WriteLine( "[CloudService] <<< Initialize" );
	}

	public void Tick( App app )
	{
		if ( !DataContext.DataContext.Instance.Settings.AppCheckForUpdates )
		{
			return;
		}

		// The startup check (fired from App_Startup) is gated only on AppCheckForUpdates. Recurring interval
		// re-checks additionally require AppCheckForUpdatesOnInterval, so the user can keep the launch check
		// while disabling the periodic re-checks.
		if ( !DataContext.DataContext.Instance.Settings.AppCheckForUpdatesOnInterval )
		{
			return;
		}

		// Never check for updates while the iRacing simulator is running.
		if ( app.Simulator.IsConnected )
		{
			return;
		}

		if ( CheckingForUpdate || DownloadingUpdate )
		{
			return;
		}

		if ( !IsUpdateCheckDue() )
		{
			return;
		}

		_ = CheckForUpdates( false );
	}

	// Returns true when the persisted last-check time is at least one interval in the past (or has never
	// been set). The interval is read live so changes to the user's setting take effect immediately.
	public bool IsUpdateCheckDue()
	{
		var settings = DataContext.DataContext.Instance.Settings;

		var updateCheckInterval = TimeSpan.FromHours( settings.AppUpdateCheckIntervalHours );

		return DateTime.UtcNow >= settings.AppLastUpdateCheckUtc + updateCheckInterval;
	}

	class GetCurrentVersionResponse
	{
		public string currentVersion = string.Empty;
		public string downloadUrl = string.Empty;
		public string changeLog = string.Empty;
	}

	public async Task CheckForUpdates( bool manuallyLaunched )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[CloudService] CheckForUpdates >>>" );

		// Record (and persist) the check time up front so the recurring-interval clock advances even if the
		// network call below fails, and so it survives an app restart.
		DataContext.DataContext.Instance.Settings.AppLastUpdateCheckUtc = DateTime.UtcNow;

		try
		{
			CheckingForUpdate = true;

			app.MainWindow.UpdateStatus();

			var getCurrentVersionUrl = $"https://mairapp.com/get-current-version/?id={NetworkIdGuid}";

			using var httpClient = new HttpClient();

			var jsonString = await httpClient.GetStringAsync( getCurrentVersionUrl );

			app.Logger.WriteLine( jsonString );

			var getCurrentVersionResponse = JsonConvert.DeserializeObject<GetCurrentVersionResponse>( jsonString );

			if ( getCurrentVersionResponse != null )
			{
				var appVersion = Misc.GetVersion();

				var isNewerVersionAvailable = Version.TryParse( appVersion, out var localVersion ) && Version.TryParse( getCurrentVersionResponse.currentVersion, out var remoteVersion ) && remoteVersion > localVersion;

				if ( isNewerVersionAvailable )
				{
					app.Logger.WriteLine( "[CloudService] Newer version is available" );

					var localFilePath = Path.Combine( App.DocumentsFolder, $"MarvinsAIRARefactored-Setup-{getCurrentVersionResponse.currentVersion}.exe" );

					var updateDownloaded = File.Exists( localFilePath );

					if ( updateDownloaded && !manuallyLaunched )
					{
						app.Logger.WriteLine( "[CloudService] File is already downloaded; skipping update process" );
					}
					else
					{
						if ( !updateDownloaded )
						{
							var downloadUpdate = false;

							app.Logger.WriteLine( "[CloudService] Asking user if they want to download the update" );

							var window = new NewVersionAvailableWindow( getCurrentVersionResponse.currentVersion, getCurrentVersionResponse.changeLog )
							{
								Owner = app.MainWindow
							};

							window.ShowDialog();

							downloadUpdate = window.DownloadUpdate;

							if ( downloadUpdate )
							{
								CheckingForUpdate = false;
								DownloadingUpdate = true;

								app.MainWindow.UpdateStatus();

								app.Logger.WriteLine( $"[CloudService] Downloading update from {getCurrentVersionResponse.downloadUrl}" );

								var httpResponseMessage = await httpClient.GetAsync( getCurrentVersionResponse.downloadUrl, HttpCompletionOption.ResponseHeadersRead );

								httpResponseMessage.EnsureSuccessStatusCode();

								var contentLength = httpResponseMessage.Content.Headers.ContentLength;

								using var fileStream = new FileStream( localFilePath, FileMode.Create, FileAccess.Write, FileShare.None );

								using var stream = await httpResponseMessage.Content.ReadAsStreamAsync();

								var buffer = new byte[ 1024 * 1024 ];

								var totalBytesRead = 0;

								while ( true )
								{
									var bytesRead = await stream.ReadAsync( buffer );

									if ( bytesRead == 0 )
									{
										break;
									}

									await fileStream.WriteAsync( buffer.AsMemory( 0, bytesRead ) );

									totalBytesRead += bytesRead;

									if ( contentLength.HasValue && ( contentLength.Value > 0 ) )
									{
										var progressPct = 100f * (float) totalBytesRead / (float) contentLength.Value;
									}
								}

								app.Logger.WriteLine( $"[CloudService] Update downloaded" );

								updateDownloaded = true;
							}
						}

						if ( updateDownloaded )
						{
							app.Logger.WriteLine( "[CloudService] Asking user if they want to install the update" );

							var window = new RunInstallerWindow( localFilePath )
							{
								Owner = app.MainWindow
							};

							window.ShowDialog();

							if ( window.InstallUpdate )
							{
								app.MainWindow.CloseAndLaunchInstaller( localFilePath );
							}
						}
					}
				}
			}

			CheckingForUpdate = false;
			DownloadingUpdate = false;

			app.MainWindow.UpdateStatus();
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[CloudService] Failed trying to check for updates: {exception.Message.Trim()}" );

			CheckingForUpdate = false;
			DownloadingUpdate = false;

			app.MainWindow.UpdateStatus();
		}

		app.Logger.WriteLine( "[CloudService] <<< CheckForUpdates" );
	}
}
