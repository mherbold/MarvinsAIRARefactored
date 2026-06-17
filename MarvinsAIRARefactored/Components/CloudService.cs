
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

	private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours( 1 );

	private DateTime _nextUpdateCheckUtc = DateTime.MaxValue;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[CloudService] Initialize >>>" );

		// Schedule the first recurring update check one interval from now. The initial
		// (startup) check is fired separately from App_Startup; this keeps users who never
		// restart the app current by re-checking periodically while iRacing is not running.
		_nextUpdateCheckUtc = DateTime.UtcNow + UpdateCheckInterval;

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

		// Never check for updates while the iRacing simulator is running.
		if ( app.Simulator.IsConnected )
		{
			return;
		}

		if ( CheckingForUpdate || DownloadingUpdate )
		{
			return;
		}

		if ( DateTime.UtcNow < _nextUpdateCheckUtc )
		{
			return;
		}

		_nextUpdateCheckUtc = DateTime.UtcNow + UpdateCheckInterval;

		_ = CheckForUpdates( false );
	}

	class GetCurrentVersionResponse
	{
		public string currentVersion = string.Empty;
		public string downloadUrl = string.Empty;
		public string changeLog = string.Empty;
	}

	class GitHubReleaseResponse
	{
		public string body = string.Empty;
	}

	// The herboldracing.com endpoint flattens the changelog (strips the markdown
	// bullets and indentation), so for display we prefer the raw release body
	// straight from the GitHub releases API, which preserves the formatting. If
	// that fetch fails for any reason we fall back to the flattened changelog.
	private async Task<string> GetReleaseNotes( HttpClient httpClient, string version, string fallbackChangeLog )
	{
		var app = App.Instance!;

		try
		{
			var releaseApiUrl = $"https://api.github.com/repos/mherbold/MarvinsAIRARefactored/releases/tags/{version}";

			using var requestMessage = new HttpRequestMessage( HttpMethod.Get, releaseApiUrl );

			requestMessage.Headers.UserAgent.ParseAdd( "MarvinsAIRARefactored" );
			requestMessage.Headers.Accept.ParseAdd( "application/vnd.github+json" );

			using var responseMessage = await httpClient.SendAsync( requestMessage );

			responseMessage.EnsureSuccessStatusCode();

			var jsonString = await responseMessage.Content.ReadAsStringAsync();

			var gitHubReleaseResponse = JsonConvert.DeserializeObject<GitHubReleaseResponse>( jsonString );

			if ( !string.IsNullOrWhiteSpace( gitHubReleaseResponse?.body ) )
			{
				return gitHubReleaseResponse.body;
			}
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[CloudService] Could not fetch release notes from GitHub; using fallback changelog: {exception.Message.Trim()}" );
		}

		return fallbackChangeLog;
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

							var releaseNotes = await GetReleaseNotes( httpClient, getCurrentVersionResponse.currentVersion, getCurrentVersionResponse.changeLog );

							var window = new NewVersionAvailableWindow( getCurrentVersionResponse.currentVersion, releaseNotes )
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
