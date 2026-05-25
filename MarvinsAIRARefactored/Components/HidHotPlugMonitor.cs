
using System.Runtime.InteropServices;
using System.Windows.Interop;

using MarvinsAIRARefactored.PInvoke;

namespace MarvinsAIRARefactored.Components;

public sealed class HidHotplugMonitor : IDisposable
{
	private HwndSource? _hwndSource;
	private IntPtr _deviceNotifyHandle = IntPtr.Zero;
	private readonly Guid _hidInterfaceGuid = new( "{4D1E55B2-F16F-11CF-88CB-001111000030}" ); // GUID_DEVINTERFACE_HID

	private System.Timers.Timer? _debounceTimer;

	public event EventHandler? DeviceListMightHaveChanged;

	public void Initialize()
	{
		var app = App.Instance!;

		app.MainWindow.SourceInitialized += ( _, __ ) =>
		{
			var hwnd = new WindowInteropHelper( app.MainWindow ).Handle;

			SetupForHwnd( hwnd );
		};

		// In some startup scenarios (start with Windows + start minimized) the
		// window handle may never be created via the normal show path, so
		// SourceInitialized may not fire. Ensure the HWND exists and register
		// for device notifications immediately on the UI thread.
		app.Dispatcher.BeginInvoke( () =>
		{
			var hwnd = new WindowInteropHelper( app.MainWindow ).EnsureHandle();

			SetupForHwnd( hwnd );
		} );

		app.MainWindow.Closed += ( _, __ ) => Dispose();
	}

	public void Dispose()
	{
		if ( _deviceNotifyHandle != IntPtr.Zero )
		{
			User32.UnregisterDeviceNotification( _deviceNotifyHandle );

			_deviceNotifyHandle = IntPtr.Zero;
		}

		_hwndSource?.RemoveHook( WndProc );

		_hwndSource = null;

		_debounceTimer?.Dispose();
	}

	private void RegisterForHidNotifications( IntPtr hwnd )
	{
		var dbi = new User32.DEV_BROADCAST_DEVICEINTERFACE_W
		{
			dbcc_size = (uint) Marshal.SizeOf<User32.DEV_BROADCAST_DEVICEINTERFACE_W>(),
			dbcc_classguid = _hidInterfaceGuid,
			dbcc_devicetype = User32.DeviceType.DBT_DEVTYP_DEVICEINTERFACE
		};

		_deviceNotifyHandle = User32.RegisterDeviceNotification( hwnd, ref dbi, User32.DeviceNotificationFlags.DEVICE_NOTIFY_WINDOW_HANDLE );
	}

	private void SetupForHwnd( IntPtr hwnd )
	{
		if ( ( hwnd == IntPtr.Zero ) || ( _hwndSource is not null ) )
		{
			return;
		}

		_hwndSource = HwndSource.FromHwnd( hwnd );
		_hwndSource?.AddHook( WndProc );

		RegisterForHidNotifications( hwnd );

		if ( _debounceTimer is null )
		{
			_debounceTimer = new System.Timers.Timer( 2000 ) { AutoReset = false };

			_debounceTimer.Elapsed += ( _, __ ) => DeviceListMightHaveChanged?.Invoke( this, EventArgs.Empty );
		}
	}

	private IntPtr WndProc( IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled )
	{
		const int WM_DEVICECHANGE = 0x0219;
		const int DBT_DEVICEARRIVAL = 0x8000;
		const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

		if ( msg == WM_DEVICECHANGE )
		{
			var eventType = wParam.ToInt32();

			if ( eventType == DBT_DEVICEARRIVAL || eventType == DBT_DEVICEREMOVECOMPLETE )
			{
				_debounceTimer?.Stop();
				_debounceTimer?.Start();

				var hdr = Marshal.PtrToStructure<User32.DEV_BROADCAST_HDR>( lParam );

				if ( hdr.dbch_devicetype == User32.DeviceType.DBT_DEVTYP_DEVICEINTERFACE )
				{
					var nameOffset = Marshal.OffsetOf<User32.DEV_BROADCAST_DEVICEINTERFACE_W>( nameof( User32.DEV_BROADCAST_DEVICEINTERFACE_W.dbcc_name ) ).ToInt32();
					var namePtr = IntPtr.Add( lParam, nameOffset );

					var devicePath = Marshal.PtrToStringUni( namePtr ) ?? string.Empty;

					var app = App.Instance!;

					if ( eventType == DBT_DEVICEREMOVECOMPLETE )
					{
						app.Logger.WriteLine( $"[HidHotPlugMonitor] Device {devicePath} was removed!" );
					}
					else
					{
						app.Logger.WriteLine( $"[HidHotPlugMonitor] Device {devicePath} was added!" );
					}
				}
			}
		}

		return IntPtr.Zero;
	}
}
