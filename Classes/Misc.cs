
using System.Collections;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using MarvinsAIRARefactored.WinApi;

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
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[Misc] DisableThrottling >>>" );

			var processInformationSize = Marshal.SizeOf<Kernel32.PROCESS_POWER_THROTTLING_STATE>();

			var processInformation = new Kernel32.PROCESS_POWER_THROTTLING_STATE()
			{
				Version = 1,
				ControlMask = (uint) Kernel32.ControlMask.PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
				StateMask = 0
			};

			var processInformationPtr = Marshal.AllocHGlobal( processInformationSize );

			Marshal.StructureToPtr( processInformation, processInformationPtr, false );

			var processHandle = Process.GetCurrentProcess().Handle;

			_ = Kernel32.SetProcessInformation( processHandle, (uint) Kernel32.ProcessInformationClass.ProcessPowerThrottling, ref processInformationPtr, (uint) processInformationSize );

			Marshal.FreeHGlobal( processInformationPtr );

			app.Logger.WriteLine( "[Misc] <<< DisableThrottling" );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float Lerp( float start, float end, float t )
	{
		return start + ( end - start ) * Math.Clamp( t, 0f, 1f );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float InterpolateHermite( float v0, float v1, float v2, float v3, float t )
	{
		var a = 2.0f * v1;
		var b = v2 - v0;
		var c = 2.0f * v0 - 5.0f * v1 + 4.0f * v2 - v3;
		var d = -v0 + 3.0f * v1 - 3.0f * v2 + v3;

		return 0.5f * ( a +  b * t  +  c * t * t  +  d * t * t * t  );
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
					System.Diagnostics.Debug.WriteLine( $"Error processing property '{prop.Name}': {ex.Message}" );
				}
			}
		}
	}

	public static Dictionary<string, string> LoadResx( string filePath )
	{
		var dictionary = new Dictionary<string, string>();

		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( $"[Misc] LoadResx >>> ({filePath})" );

			if ( File.Exists( filePath ) )
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

			app.Logger.WriteLine( "[Misc] <<< LoadResx" );
		}

		return dictionary;
	}
}
