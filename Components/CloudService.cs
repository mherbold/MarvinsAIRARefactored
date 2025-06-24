
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

		try
		{
			CheckingForUpdate = true;

			app.MainWindow.UpdateStatus();

			var getCurrentVersionUrl = $"https://herboldracing.com/wp-json/maira/v2/get-current-version?id={NetworkIdGuid}";

			using var httpClient = new HttpClient();

			var jsonString = await httpClient.GetStringAsync( getCurrentVersionUrl );

			app.Logger.WriteLine( jsonString );

			var getCurrentVersionResponse = JsonConvert.DeserializeObject<GetCurrentVersionResponse>( jsonString );

			if ( getCurrentVersionResponse != null )
			{
				var appVersion = Misc.GetVersion();

				if ( appVersion != getCurrentVersionResponse.currentVersion )
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
