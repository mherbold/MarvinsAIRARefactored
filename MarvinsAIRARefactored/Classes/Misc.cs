
using System.Collections;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

using IWshRuntimeLibrary;

using Point = System.Windows.Point;

namespace MarvinsAIRARefactored.Classes;

public class Misc
{
	public static string GetVersion()
	{
		var systemVersion = Assembly.GetExecutingAssembly().GetName().Version;

		// the version scheme is three-part (2.1.[buildStamp]) but assembly versions are always stored
		// four-part with a zero appended - report only the three real fields
		return systemVersion?.ToString( 3 ) ?? string.Empty;
	}

	// Asks Windows whether now is a good time to show the user something. Returns true only when the
	// notification state is QUNS_ACCEPTS_NOTIFICATIONS (normal desktop) - i.e. NOT while a full-screen
	// game / app or presentation is running, the user is away or locked, or during Windows quiet hours.
	// Fail-open: if the query fails (e.g. no active console session) we return true so an API quirk can
	// never permanently inhibit user-facing prompts such as the update check.
	public static bool WindowsAcceptsNotifications()
	{
		var hresult = PInvoke.SHQueryUserNotificationState( out var notificationState );

		if ( hresult.Failed )
		{
			App.Instance?.Logger.WriteLine( $"[Misc] WindowsAcceptsNotifications SHQueryUserNotificationState failed: 0x{hresult.Value:X8}" );

			return true;
		}

		return notificationState == QUERY_USER_NOTIFICATION_STATE.QUNS_ACCEPTS_NOTIFICATIONS;
	}

	public static unsafe void DisableThrottling()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Misc] DisableThrottling >>>" );

		const uint processPowerThrottlingCurrentVersion = 1;
		const uint processPowerThrottlingIgnoreTimerResolution = 0x4;

		var processPowerThrottlingState = new PROCESS_POWER_THROTTLING_STATE
		{
			Version = processPowerThrottlingCurrentVersion,
			ControlMask = processPowerThrottlingIgnoreTimerResolution,
			StateMask = 0
		};

		var processHandle = (HANDLE) Process.GetCurrentProcess().Handle;

		var result = PInvoke.SetProcessInformation( processHandle, PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, &processPowerThrottlingState, (uint) sizeof( PROCESS_POWER_THROTTLING_STATE ) );

		if ( !result )
		{
			app.Logger.WriteLine( $"[Misc] DisableThrottling SetProcessInformation failed: {Marshal.GetLastWin32Error()}" );
		}

		app.Logger.WriteLine( "[Misc] <<< DisableThrottling" );
	}

	public static Process? GetProcessFromWindowHandle( IntPtr windowHandle )
	{
		if ( windowHandle == IntPtr.Zero )
		{
			return null;
		}

		_ = PInvoke.GetWindowThreadProcessId( (HWND) windowHandle, out var processId );

		if ( processId == 0 )
		{
			return null;
		}

		try
		{
			return Process.GetProcessById( (int) processId );
		}
		catch
		{
			return null;
		}
	}

	public static void ForcePropertySetters( object obj )
	{
		if ( obj == null ) return;

		var type = obj.GetType();

		var properties = type.GetProperties( BindingFlags.Public | BindingFlags.Instance );

		foreach ( var prop in properties )
		{
			if ( prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0 )
			{
				try
				{
					var currentValue = prop.GetValue( obj );

					prop.SetValue( obj, currentValue );
				}
				catch ( Exception ex )
				{
					Debug.WriteLine( $"Error processing property '{prop.Name}': {ex.Message}" );
				}
			}
		}
	}

	public static Dictionary<string, string> LoadResx( string filePath )
	{
		var dictionary = new Dictionary<string, string>();

		var app = App.Instance;

		app?.Logger.WriteLine( $"[Misc] LoadResx >>> ({filePath})" );

		if ( System.IO.File.Exists( filePath ) )
		{
			using var reader = new ResXResourceReader( filePath );

			reader.UseResXDataNodes = true;

			foreach ( DictionaryEntry entry in reader )
			{
				var key = entry.Key.ToString();

				if ( key != null )
				{
					if ( entry.Value is ResXDataNode node )
					{
						var valueAsString = node.GetValue( (ITypeResolutionService?) null )?.ToString();

						if ( valueAsString != null )
						{
							dictionary[ key ] = valueAsString;
						}
					}
					else
					{
						var valueAsString = entry.Value?.ToString();

						if ( valueAsString != null )
						{
							dictionary[ key ] = valueAsString;
						}
					}
				}
			}
		}

		app?.Logger.WriteLine( "[Misc] <<< LoadResx" );

		return dictionary;
	}

	public static Dictionary<string, string> LoadResxFromStream( Stream stream )
	{
		var dictionary = new Dictionary<string, string>();

		using var reader = new ResXResourceReader( stream );

		reader.UseResXDataNodes = true;

		foreach ( DictionaryEntry entry in reader )
		{
			var key = entry.Key.ToString();

			if ( key != null )
			{
				if ( entry.Value is ResXDataNode node )
				{
					var valueAsString = node.GetValue( (ITypeResolutionService?) null )?.ToString();

					if ( valueAsString != null )
					{
						dictionary[ key ] = valueAsString;
					}
				}
				else
				{
					var valueAsString = entry.Value?.ToString();

					if ( valueAsString != null )
					{
						dictionary[ key ] = valueAsString;
					}
				}
			}
		}

		return dictionary;
	}

	private const double WindowTitleBarHeightInDIPs = 32.0;

	// Tests whether enough of the window's title bar is on a screen for the user to grab it with the
	// mouse. Both the bounds and the screen working areas are in physical pixels - convert WPF DIPs
	// using the window's DPI scale before calling (they diverge whenever any monitor is not at 100%
	// scaling). Screens are tested individually rather than against the virtual desktop bounding box,
	// so the dead zones of non-rectangular multi-monitor layouts correctly count as off-screen.
	public static bool IsWindowTitleBarVisible( Rectangle boundsInPixels, double dpiScale )
	{
		if ( ( boundsInPixels.Width <= 0 ) || ( boundsInPixels.Height <= 0 ) )
		{
			return false;
		}

		var titleBarHeightInPixels = Math.Max( 1, (int) Math.Round( WindowTitleBarHeightInDIPs * dpiScale ) );

		var titleBarBounds = new Rectangle( boundsInPixels.X, boundsInPixels.Y, boundsInPixels.Width, titleBarHeightInPixels );

		var totalTitleBarArea = titleBarBounds.Width * titleBarBounds.Height;

		var totalVisibleArea = 0;

		foreach ( var screen in Screen.AllScreens )
		{
			var intersection = Rectangle.Intersect( screen.WorkingArea, titleBarBounds );

			totalVisibleArea += intersection.Width * intersection.Height;
		}

		// Require at least 25% of the title bar to be visible so there is enough of it to grab
		return totalVisibleArea >= totalTitleBarArea * 0.25;
	}

	public static void BringExistingInstanceToFront()
	{
		var current = Process.GetCurrentProcess();

		foreach ( var process in Process.GetProcessesByName( current.ProcessName ) )
		{
			if ( process.Id == current.Id )
			{
				continue;
			}

			var hwnd = (HWND) process.MainWindowHandle;

			if ( hwnd != HWND.Null )
			{
				_ = PInvoke.ShowWindow( hwnd, SHOW_WINDOW_CMD.SW_RESTORE );
				_ = PInvoke.SetForegroundWindow( hwnd );
			}

			break;
		}
	}

	public static void SetStartWithWindows( bool enable )
	{
		var startupPath = Environment.GetFolderPath( Environment.SpecialFolder.Startup );
		var shortcutPath = Path.Combine( startupPath, App.AppName + ".lnk" );

		if ( enable )
		{
			if ( !System.IO.File.Exists( shortcutPath ) )
			{
				var shell = new WshShell();

				var shortcut = (IWshShortcut) shell.CreateShortcut( shortcutPath );

				shortcut.TargetPath = Environment.ProcessPath;
				shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
				shortcut.Save();
			}
		}
		else
		{
			if ( System.IO.File.Exists( shortcutPath ) )
			{
				System.IO.File.Delete( shortcutPath );
			}
		}
	}

	public static void ApplyToTaggedElements( DependencyObject root, string tagName, Action<FrameworkElement> action )
	{
		if ( ( root == null ) || ( action == null ) || ( tagName == null ) )
		{
			return;
		}

		for ( var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount( root ); childIndex++ )
		{
			var child = VisualTreeHelper.GetChild( root, childIndex );

			if ( ( child is FrameworkElement frameworkElement ) && ( frameworkElement.Tag?.ToString() == tagName ) )
			{
				action( frameworkElement );
			}

			ApplyToTaggedElements( child, tagName, action );
		}
	}

	public static TabPanel? FindTabPanel( DependencyObject parent )
	{
		for ( var i = 0; i < VisualTreeHelper.GetChildrenCount( parent ); i++ )
		{
			var child = VisualTreeHelper.GetChild( parent, i );

			if ( child is TabPanel panel )
			{
				return panel;
			}

			var result = FindTabPanel( child );

			if ( result != null )
			{
				return result;
			}
		}

		return null;
	}

	public static T? FindAncestor<T>( DependencyObject start ) where T : DependencyObject
	{
		for ( var parent = VisualTreeHelper.GetParent( start ); parent is not null; parent = VisualTreeHelper.GetParent( parent ) )
		{
			if ( parent is T typed ) return typed;
		}

		return null;
	}

	public static void MoveCursorToElement( FrameworkElement element )
	{
		if ( !element.IsLoaded ) return;

		var p = element.PointToScreen( new Point( element.ActualWidth / 2, element.ActualHeight / 2 ) );

		PInvoke.SetCursorPos( (int) Math.Round( p.X ), (int) Math.Round( p.Y ) );
	}

	// Mirrors a window's entire layout for right-to-left languages (Hebrew, Arabic, ...).
	// FlowDirection cascades to every child, so this single assignment mirrors the whole window.
	public static void ApplyFlowDirection( Window window )
	{
		if ( window == null )
		{
			return;
		}

		window.FlowDirection = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization.IsRightToLeft
			? System.Windows.FlowDirection.RightToLeft
			: System.Windows.FlowDirection.LeftToRight;
	}

	// Registers a single class handler so every Window in the app (current and future, dialogs and
	// overlays) gets its FlowDirection set automatically when it loads. MainWindow additionally
	// re-applies this in RefreshWindow() so a live language change mirrors it without a reopen.
	public static void RegisterGlobalFlowDirectionHandler()
	{
		EventManager.RegisterClassHandler( typeof( Window ), FrameworkElement.LoadedEvent, new RoutedEventHandler( ( sender, _ ) =>
		{
			if ( sender is Window window )
			{
				ApplyFlowDirection( window );
			}
		} ) );
	}
}
