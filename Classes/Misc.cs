
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

using Point = System.Windows.Point;

using IWshRuntimeLibrary;
using PInvoke;

namespace MarvinsAIRARefactored.Classes;

public class Misc
{
	public static string GetVersion()
	{
		var systemVersion = Assembly.GetExecutingAssembly().GetName().Version;

		return systemVersion?.ToString() ?? string.Empty;
	}

	public static void DisableThrottling()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Misc] DisableThrottling >>>" );

		var processInformationSize = Marshal.SizeOf<Kernel32.PROCESS_POWER_THROTTLING_STATE>();

		var processInformation = new Kernel32.PROCESS_POWER_THROTTLING_STATE()
		{
			Version = 1,
			ControlMask = (Kernel32.ProcessorPowerThrottlingFlags) 4, // PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION
			StateMask = 0
		};

		var processInformationPtr = Marshal.AllocHGlobal( processInformationSize );

		Marshal.StructureToPtr( processInformation, processInformationPtr, false );

		var processHandle = Process.GetCurrentProcess().Handle;

		Kernel32.SafeObjectHandle safeHandle = new Kernel32.SafeObjectHandle( processHandle, ownsHandle: false );

		_ = Kernel32.SetProcessInformation( safeHandle, Kernel32.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, processInformationPtr, (uint) processInformationSize );

		Marshal.FreeHGlobal( processInformationPtr );

		app.Logger.WriteLine( "[Misc] <<< DisableThrottling" );
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

	public static bool IsWindowBoundsVisible( Rectangle bounds )
	{
		foreach ( var screen in Screen.AllScreens )
		{
			if ( screen.WorkingArea.IntersectsWith( bounds ) )
			{
				return true;
			}
		}

		return false;
	}

	public static void BringExistingInstanceToFront()
	{
		var current = Process.GetCurrentProcess();

		foreach ( var process in Process.GetProcessesByName( current.ProcessName ) )
		{
			if ( process.Id != current.Id )
			{
				var handle = process.MainWindowHandle;

				if ( handle != IntPtr.Zero )
				{
					User32.ShowWindow( handle, User32.WindowShowStyle.SW_RESTORE );
					User32.SetForegroundWindow( handle );
				}

				break;
			}
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

		User32.SetCursorPos( (int) Math.Round( p.X ), (int) Math.Round( p.Y ) );
	}
}
