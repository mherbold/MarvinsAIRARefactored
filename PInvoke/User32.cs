
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.PInvoke;

public static partial class User32
{
	[Flags]
	public enum DeviceNotificationFlags : uint
	{
		DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000,
		DEVICE_NOTIFY_SERVICE_HANDLE = 0x00000001,
		DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 0x00000004
	}

	public enum DeviceType : uint
	{
		DBT_DEVTYP_OEM = 0x00000000,
		DBT_DEVTYP_DEVNODE = 0x00000001,
		DBT_DEVTYP_VOLUME = 0x00000002,
		DBT_DEVTYP_PORT = 0x00000003,
		DBT_DEVTYP_NET = 0x00000004,
		DBT_DEVTYP_DEVICEINTERFACE = 0x00000005,
		DBT_DEVTYP_HANDLE = 0x00000006
	}

	[StructLayout( LayoutKind.Sequential )]
	public struct DEV_BROADCAST_HDR
	{
		public uint dbch_size;
		public DeviceType dbch_devicetype;
		public uint dbch_reserved;
	}

	[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
	public struct DEV_BROADCAST_DEVICEINTERFACE
	{
		public uint dbcc_size;
		public DeviceType dbcc_devicetype;
		public uint dbcc_reserved;
		public Guid dbcc_classguid;

		[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 255 )]
		public string dbcc_name;
	}

	[DllImport( "user32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
	public static extern IntPtr RegisterDeviceNotification( IntPtr hRecipient, ref DEV_BROADCAST_DEVICEINTERFACE notificationFilter, DeviceNotificationFlags flags );

	[DllImport( "user32.dll", SetLastError = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	public static extern bool UnregisterDeviceNotification( IntPtr handle );
}
