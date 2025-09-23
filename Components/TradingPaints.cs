
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;

using ICSharpCode.SharpZipLib.BZip2;
using Newtonsoft.Json;

using MarvinsAIRARefactored.Classes;

using static IRSDKSharper.IRacingSdkSessionInfo.DriverInfoModel;

namespace MarvinsAIRARefactored.Components;

public class TradingPaints
{
	private static readonly HttpClient _http = CreateHttpClient();

	private readonly HashSet<int> _seenUserIds = [];
	private readonly HashSet<int> _enqueuedUserIds = [ new() ];
	private readonly Lock _lock = new();

	private Channel<DriverModel> _queue = Channel.CreateUnbounded<DriverModel>( new UnboundedChannelOptions { SingleReader = true, SingleWriter = false } );

	private Task? _processQueueLoopTask;
	private CancellationTokenSource _cts = new();

	private string _cachePath = string.Empty;
	private readonly ConcurrentDictionary<string, string> _fileUrlToCarId = new( StringComparer.OrdinalIgnoreCase );

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TradingPaints] Initialize >>>" );

		var settings = DataContext.DataContext.Instance.Settings;

		_cachePath = Path.Combine( settings.TradingPaintsFolder, "MarvinsAIRARefactored.cache" );

		Directory.CreateDirectory( settings.TradingPaintsFolder );

		LoadCache();

		app.Logger.WriteLine( "[TradingPaints] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TradingPaints] Shutdown >>>" );

		_cts.Cancel();

		try
		{
			_processQueueLoopTask?.Wait( 1000 );
		}
		catch
		{
		}

		app.Logger.WriteLine( "[TradingPaints] <<< Shutdown" );
	}

	public void Reset()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TradingPaints] Reset >>>" );

		Channel<DriverModel> oldQueue;
		CancellationTokenSource oldCts;

		using ( _lock.EnterScope() )
		{
			_seenUserIds.Clear();
			_enqueuedUserIds.Clear();

			oldQueue = _queue;
			oldCts = _cts;

			_queue = Channel.CreateUnbounded<DriverModel>( new UnboundedChannelOptions { SingleReader = true, SingleWriter = false } );

			_cts = new CancellationTokenSource();

			_processQueueLoopTask = Task.Run( () => ProcessQueueLoopAsync( _queue, _cts.Token ) );
		}

		try
		{
			oldCts.Cancel();
		}
		catch
		{
		}

		try
		{
			oldQueue.Writer.TryComplete();
		}
		catch
		{
		}

		app.Logger.WriteLine( "[TradingPaints] <<< Reset" );
	}

	public void UpdateDrivers( List<DriverModel> drivers )
	{
		ArgumentNullException.ThrowIfNull( drivers );

		var settings = DataContext.DataContext.Instance.Settings;

		if ( settings.TradingPaintsEnabled )
		{
			using ( _lock.EnterScope() )
			{
				foreach ( var driver in drivers )
				{
					var userId = driver.UserID;

					if ( ( userId != -1 ) && !_seenUserIds.Contains( userId ) )
					{
						_seenUserIds.Add( userId );

						if ( _enqueuedUserIds.Add( userId ) )
						{
							_queue.Writer.TryWrite( driver );
						}
					}
				}
			}

			_processQueueLoopTask ??= Task.Run( () => ProcessQueueLoopAsync( _queue, _cts.Token ) );
		}
	}

	private Task ProcessQueueLoopAsync( Channel<DriverModel> queue, CancellationToken token ) => ProcessQueueLoopCoreAsync( queue, token );

	private async Task ProcessQueueLoopCoreAsync( Channel<DriverModel> queue, CancellationToken ct )
	{
		var app = App.Instance!;

		try
		{
			while ( await queue.Reader.WaitToReadAsync( ct ) )
			{
				while ( queue.Reader.TryRead( out var driver ) )
				{
					try
					{
						await ProcessDriverAsync( driver, ct );
					}
					catch ( Exception exception )
					{
						app.Logger.WriteLine( $"[TradingPaints] Exception caught: {exception.Message.Trim()}" );
					}
					finally
					{
						using ( _lock.EnterScope() )
						{
							_enqueuedUserIds.Remove( driver.UserID );
						}
					}
				}
			}
		}
		catch ( OperationCanceledException )
		{
			// expected on reset/shutdown
		}
	}

	private async Task ProcessDriverAsync( DriverModel driver, CancellationToken ct )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[TradingPaints] Processing UserID={driver.UserID}, CarPath='{driver.CarPath}'" );

		var settings = DataContext.DataContext.Instance.Settings;

		// fetch the driver's XML from Trading Paints

		var fetchUri = new Uri( $"https://fetch.tradingpaints.gg/fetch_user.php?user={driver.UserID}" );

		using var req = new HttpRequestMessage( HttpMethod.Get, fetchUri );
		using var resp = await _http.SendAsync( req, HttpCompletionOption.ResponseHeadersRead, ct );

		resp.EnsureSuccessStatusCode();

		await using var xmlStream = await resp.Content.ReadAsStreamAsync( ct );

		// filter to the CarPath we're interested in (directoryFilter)

		var assets = TradingPaintsXml.ParseUserAssets( xmlStream, driver.CarPath );

		if ( assets.Count == 0 )
		{
			return;
		}

		// prepare download folder for this car

		var carFolderFullPath = Path.Combine( settings.TradingPaintsFolder, driver.CarPath );

		Directory.CreateDirectory( carFolderFullPath );

		// download assets sequentially (await one by one)

		var somethingGotUpdated = false;

		foreach ( var asset in assets )
		{
			ct.ThrowIfCancellationRequested();

			if ( !ShouldDownload( asset.FileUrl, asset.FileId ) )
			{
				app.Logger.WriteLine( $"[TradingPaints] Skipping already downloaded paint file ({asset.FileUrl}, {asset.FileId})" );

				continue;
			}

			var typeToken = asset.Type switch
			{
				TradingPaintsXml.Type.Car => "car",
				TradingPaintsXml.Type.CarNum => "car_num",
				TradingPaintsXml.Type.CarSpec => "car_spec",
				TradingPaintsXml.Type.CarDecal => "car_decal",
				TradingPaintsXml.Type.Suit => "suit",
				TradingPaintsXml.Type.Helmet => "helmet",
				_ => "file"
			};

			// figure out extension from the URL path (handles .mip, .tga, .tga.bz2)

			var (isBz2, fileExtension) = GetFileExtension( asset.FileUrl );

			// figure out if we need to add some text before the file extension

			if ( !string.IsNullOrWhiteSpace( asset.Ext ) )
			{
				fileExtension = $"_{asset.Ext}{fileExtension}";
			}

			// build final filename: {Type}_{UserID}_{Ext}.{mip/tga}

			var finalFileName = $"{typeToken}_{asset.UserId}{fileExtension}";
			var finalPath = Path.Combine( settings.TradingPaintsFolder, asset.Directory, finalFileName );

			// download to a temp file first

			var tmpPath = finalPath + ".part";

			try
			{
				app.Logger.WriteLine( $"[TradingPaints] Downloading paint file ({asset.FileUrl}, {asset.FileId})" );

				await DownloadAsync( asset.FileUrl, tmpPath, ct );

				if ( isBz2 )
				{
					app.Logger.WriteLine( $"[TradingPaints] Decompressing paint file ({asset.FileUrl}, {asset.FileId})" );

					var ok = TryDecompressBZip2ToTga( tmpPath, finalPath );

					if ( ok )
					{
						File.Delete( tmpPath );

						somethingGotUpdated = true;
					}
					else
					{
						File.Delete( tmpPath );
						continue;
					}
				}
				else
				{
					if ( File.Exists( finalPath ) )
					{
						File.Delete( finalPath );
					}

					File.Move( tmpPath, finalPath );

					somethingGotUpdated = true;
				}

				_fileUrlToCarId[ asset.FileUrl ] = asset.FileId;

				SaveCache();
			}
			catch
			{
				if ( File.Exists( tmpPath ) )
				{
					File.Delete( tmpPath );
				}
			}
		}

		// tell iracing to reload textures for this driver if anything got updated

		if ( somethingGotUpdated )
		{
			app.Logger.WriteLine( $"[TradingPaints] Telling iRacing to reload paint for {driver.UserName}" );

			App.Instance!.Simulator.IRSDK.ReloadTextures( IRSDKSharper.IRacingSdkEnum.ReloadTexturesMode.CarIdx, driver.CarIdx );
		}
	}

	private static (bool isBz2, string finalExt) GetFileExtension( string fileUrl )
	{
		// we'll inspect the URL path (ignore querystring)

		var extPath = new Uri( fileUrl, UriKind.Absolute ).AbsolutePath;

		// normalize to lower for checks

		extPath = extPath.ToLowerInvariant();

		// .tga.bz2 => final .tga (but special handling needed)

		if ( extPath.EndsWith( ".tga.bz2", StringComparison.Ordinal ) )
		{
			return (true, ".tga");
		}

		// plain .tga

		if ( extPath.EndsWith( ".tga", StringComparison.Ordinal ) )
		{
			return (false, ".tga");
		}

		// .mip

		if ( extPath.EndsWith( ".mip", StringComparison.Ordinal ) )
		{
			return (false, ".mip");
		}

		// fallback: attempt to extract last extension; default to .tga

		var lastDot = extPath.LastIndexOf( '.', extPath.Length - 1 );

		var ext = lastDot >= 0 ? extPath[ lastDot.. ] : ".tga";

		return (false, ext);
	}

	private static async Task DownloadAsync( string url, string destPath, CancellationToken ct )
	{
		// simple retry with exponential backoff + 429 respect

		var delayMs = 400;

		for ( var attempt = 1; attempt <= 5; attempt++ )
		{
			ct.ThrowIfCancellationRequested();

			using var req = new HttpRequestMessage( HttpMethod.Get, url );
			using var resp = await _http.SendAsync( req, HttpCompletionOption.ResponseHeadersRead, ct );

			// handle 429 with Retry-After header (or exponential backoff if missing)

			if ( (int) resp.StatusCode == 429 )
			{
				var retryAfter = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds( delayMs );

				await Task.Delay( retryAfter, ct );

				delayMs = Math.Min( delayMs * 2, 8000 );

				continue;
			}

			// handle other non-success status codes

			if ( !resp.IsSuccessStatusCode )
			{
				if ( (int) resp.StatusCode >= 500 && attempt < 5 )
				{
					await Task.Delay( delayMs, ct );

					delayMs = Math.Min( delayMs * 2, 8000 );

					continue;
				}

				resp.EnsureSuccessStatusCode();
			}

			// success

			await using var fs = new FileStream( destPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true );

			await resp.Content.CopyToAsync( fs, ct );

			return;
		}

		throw new HttpRequestException( $"Failed to download after retries: {url}" );
	}

	private static HttpClient CreateHttpClient()
	{
		var handler = new HttpClientHandler
		{
			AllowAutoRedirect = true,
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
			MaxAutomaticRedirections = 10,
			UseCookies = true
		};

		var http = new HttpClient( handler, disposeHandler: true )
		{
			Timeout = TimeSpan.FromSeconds( 30 )
		};

		http.DefaultRequestHeaders.UserAgent.ParseAdd( "MAIRA/2.0 (+https://github.com/mherbold/MarvinsAIRARefactored)" );
		http.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "*/*" ) );
		http.DefaultRequestHeaders.AcceptEncoding.Add( new StringWithQualityHeaderValue( "gzip" ) );
		http.DefaultRequestHeaders.AcceptEncoding.Add( new StringWithQualityHeaderValue( "deflate" ) );

		return http;
	}

	private bool ShouldDownload( string fileUrl, string carIdFromXml )
	{
		if ( !_fileUrlToCarId.TryGetValue( fileUrl, out var cachedCarId ) )
		{
			return true;
		}

		return !string.Equals( cachedCarId, carIdFromXml, StringComparison.Ordinal );
	}

	private void LoadCache()
	{
		try
		{
			if ( !File.Exists( _cachePath ) ) return;

			var json = File.ReadAllText( _cachePath, Encoding.UTF8 );
			var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>( json ) ?? new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

			foreach ( var kvp in dict )
			{
				_fileUrlToCarId.TryAdd( kvp.Key, kvp.Value );
			}
		}
		catch
		{
			// ignore cache load errors; start fresh
		}
	}

	private void SaveCache()
	{
		try
		{
			var snapshot = _fileUrlToCarId.ToDictionary( p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase );
			var json = JsonConvert.SerializeObject( snapshot, Formatting.Indented );

			var tmp = _cachePath + ".tmp";

			File.WriteAllText( tmp, json, Encoding.UTF8 );

			if ( File.Exists( _cachePath ) )
			{
				File.Delete( _cachePath );
			}

			File.Move( tmp, _cachePath );
		}
		catch
		{
			// ignore cache save errors (best effort)
		}
	}

	private static bool TryDecompressBZip2ToTga( string bz2Path, string tgaOutPath )
	{
		try
		{
			// write to a temp file first then atomically move into place

			var tmpOutPath = tgaOutPath + ".part2";

			using ( var inputStream = File.OpenRead( bz2Path ) )
			using ( var outputStream = new FileStream( tmpOutPath, FileMode.Create, FileAccess.Write, FileShare.None ) )
			{
				BZip2.Decompress( inputStream, outputStream, true );
			}

			if ( File.Exists( tgaOutPath ) )
			{
				File.Delete( tgaOutPath );
			}

			File.Move( tmpOutPath, tgaOutPath );

			return true;
		}
		catch
		{
			try
			{
				File.Delete( tgaOutPath + ".part2" );
			}
			catch
			{
			}

			return false;
		}
	}
}
