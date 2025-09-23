
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

			_hwndSource = HwndSource.FromHwnd( hwnd );

			_hwndSource?.AddHook( WndProc );

			RegisterForHidNotifications( hwnd );

			_debounceTimer = new System.Timers.Timer( 2000 ) { AutoReset = false };

			_debounceTimer.Elapsed += ( _, __ ) => DeviceListMightHaveChanged?.Invoke( this, EventArgs.Empty );
		};

		app.MainWindow.Closed += ( _, __ ) => Dispose();
	}

	public void Dispose()
	{
		if ( _deviceNotifyHandle != IntPtr.Zero )
		{
			User32.UnregisterDeviceNotification( _deviceNotifyHandle );

			_deviceNotifyHandle = IntPtr.Zero;
		}

		if ( _hwndSource is not null )
		{
			_hwndSource.RemoveHook( WndProc );

			_hwndSource = null;
		}

		_debounceTimer?.Dispose();
	}

	private void RegisterForHidNotifications( IntPtr hwnd )
	{
		var dbi = new User32.DEV_BROADCAST_DEVICEINTERFACE
		{
			dbcc_size = (uint) Marshal.SizeOf<User32.DEV_BROADCAST_DEVICEINTERFACE>(),
			dbcc_classguid = _hidInterfaceGuid,
			dbcc_devicetype = User32.DeviceType.DBT_DEVTYP_DEVICEINTERFACE 
		};

		_deviceNotifyHandle = User32.RegisterDeviceNotification( hwnd, ref dbi, User32.DeviceNotificationFlags.DEVICE_NOTIFY_WINDOW_HANDLE );
	}

	private IntPtr WndProc( IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled )
	{
		const int WM_DEVICECHANGE = 0x0219;
		const int DBT_DEVICEARRIVAL = 0x8000;
		const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
		const int DBT_DEVNODES_CHANGED = 0x0007;

		if ( msg == WM_DEVICECHANGE )
		{
			var evt = wParam.ToInt32();

			if ( ( evt == DBT_DEVICEARRIVAL ) || ( evt == DBT_DEVICEREMOVECOMPLETE ) || ( evt == DBT_DEVNODES_CHANGED ) )
			{
				_debounceTimer?.Stop();
				_debounceTimer?.Start();
			}
		}

		return IntPtr.Zero;
	}
}
