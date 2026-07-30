
using System.IO;

using Microsoft.Win32.SafeHandles;

using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace MarvinsAIRARefactored.Classes;

// Minimal raw-HID access, used by the Logitech rev lights component.
//
// Windows splits a multi-interface USB device into one HID "collection" per interface and per report
// size, each with its own device interface path, so talking to a wheel means picking the right
// collection rather than the right device. This helper enumerates every HID collection for a given
// vendor, reports the identifying bits needed to choose one (product id, usage page/usage, report
// lengths), and opens it as a FileStream for report I/O.
//
// Deliberately not a general HID library - it does only what the rev lights need, and it never
// touches a device outside the vendor id it is asked for.
//
// All of the HID and SetupAPI calls come from the CsWin32 bindings in MarvinsAIRARefactored.Win32
// (the solution is x64-only, which is what lets CsWin32 generate the SetupDi* functions - under
// AnyCPU it refuses with PInvoke005 because SP_DEVICE_INTERFACE_DETAIL_DATA_W is packed differently
// on 32- and 64-bit).

public sealed class HidCollectionInfo
{
	public required string DevicePath { get; init; }
	public required ushort VendorId { get; init; }
	public required ushort ProductId { get; init; }
	public required ushort UsagePage { get; init; }
	public required ushort Usage { get; init; }
	public required int InputReportByteLength { get; init; }
	public required int OutputReportByteLength { get; init; }

	// The sibling collections of one USB interface share a device path up to the "&col" suffix
	// Windows appends per top-level collection. Grouping on that stem keeps, say, the HID++ short,
	// long, and very-long collections of interface 0 together and apart from interface 1's.
	public string GroupStem
	{
		get
		{
			var index = DevicePath.IndexOf( "&col", StringComparison.OrdinalIgnoreCase );

			return ( index > 0 ) ? DevicePath[ ..index ] : DevicePath;
		}
	}

	public bool PathContains( string fragment ) => DevicePath.Contains( fragment, StringComparison.OrdinalIgnoreCase );
}

public static class HidDeviceHelper
{
	private const uint GenericRead = 0x80000000;
	private const uint GenericWrite = 0x40000000;

	private const int HidpStatusSuccess = 0x00110000;

	// Enumerate every present HID collection belonging to the given vendor. Best effort throughout:
	// a collection that cannot be opened for query or does not answer HidP_GetCaps is skipped rather
	// than failing the whole scan, because unrelated vendor devices should never break wheel discovery.
	public static List<HidCollectionInfo> Enumerate( ushort vendorId )
	{
		var collections = new List<HidCollectionInfo>();

		PInvoke.HidD_GetHidGuid( out var hidInterfaceGuid );

		using var deviceInfoSet = PInvoke.SetupDiGetClassDevs( hidInterfaceGuid, null, HWND.Null, SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT | SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_DEVICEINTERFACE );

		if ( deviceInfoSet.IsInvalid )
		{
			return collections;
		}

		for ( var memberIndex = 0u; ; memberIndex++ )
		{
			var deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();

			unsafe
			{
				deviceInterfaceData.cbSize = (uint) sizeof( SP_DEVICE_INTERFACE_DATA );
			}

			if ( !PInvoke.SetupDiEnumDeviceInterfaces( deviceInfoSet, null, hidInterfaceGuid, memberIndex, ref deviceInterfaceData ) )
			{
				break;
			}

			var devicePath = GetDeviceInterfacePath( deviceInfoSet, deviceInterfaceData );

			if ( devicePath == null )
			{
				continue;
			}

			var collectionInfo = TryDescribeCollection( devicePath, vendorId );

			if ( collectionInfo != null )
			{
				collections.Add( collectionInfo );
			}
		}

		return collections;
	}

	// SetupDiGetDeviceInterfaceDetail returns a variable-length structure, so it is called twice: once to
	// learn the byte count, once to fill a buffer of that size. cbSize describes the fixed header only
	// (8 bytes on x64: the DWORD plus one aligned WCHAR of path), NOT the size of the buffer passed in.
	// CsWin32 only generates the raw pointer form of this one (the variable-length struct defeats the
	// friendly overload), hence the DangerousGetHandle - the safe handle stays alive in Enumerate's scope.
	private static unsafe string? GetDeviceInterfacePath( SetupDiDestroyDeviceInfoListSafeHandle deviceInfoSet, SP_DEVICE_INTERFACE_DATA deviceInterfaceData )
	{
		var deviceInfoSetHandle = new HDEVINFO( deviceInfoSet.DangerousGetHandle() );

		var requiredSize = 0u;

		PInvoke.SetupDiGetDeviceInterfaceDetail( deviceInfoSetHandle, &deviceInterfaceData, null, 0, &requiredSize, null );

		if ( requiredSize <= sizeof( uint ) )
		{
			return null;
		}

		var buffer = new byte[ requiredSize ];

		fixed ( byte* bufferPointer = buffer )
		{
			var detailData = (SP_DEVICE_INTERFACE_DETAIL_DATA_W*) bufferPointer;

			detailData->cbSize = (uint) sizeof( SP_DEVICE_INTERFACE_DETAIL_DATA_W );

			if ( !PInvoke.SetupDiGetDeviceInterfaceDetail( deviceInfoSetHandle, &deviceInterfaceData, detailData, requiredSize, null, null ) )
			{
				return null;
			}

			return new string( (char*) &detailData->DevicePath );
		}
	}

	// Open with zero desired access - a query-only handle reads attributes and capabilities without
	// contending with whatever already owns the device for I/O (the game, the driver, G HUB).
	private static HidCollectionInfo? TryDescribeCollection( string devicePath, ushort vendorId )
	{
		using var deviceHandle = PInvoke.CreateFile( devicePath, 0, FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE, null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, 0, null );

		if ( deviceHandle.IsInvalid )
		{
			return null;
		}

		if ( !PInvoke.HidD_GetAttributes( deviceHandle, out var attributes ) )
		{
			return null;
		}

		if ( attributes.VendorID != vendorId )
		{
			return null;
		}

		if ( !PInvoke.HidD_GetPreparsedData( deviceHandle, out var preparsedData ) )
		{
			return null;
		}

		try
		{
			if ( PInvoke.HidP_GetCaps( preparsedData, out var capabilities ) != HidpStatusSuccess )
			{
				return null;
			}

			return new HidCollectionInfo
			{
				DevicePath = devicePath,
				VendorId = attributes.VendorID,
				ProductId = attributes.ProductID,
				UsagePage = capabilities.UsagePage,
				Usage = capabilities.Usage,
				InputReportByteLength = capabilities.InputReportByteLength,
				OutputReportByteLength = capabilities.OutputReportByteLength
			};
		}
		finally
		{
			PInvoke.HidD_FreePreparsedData( preparsedData );
		}
	}

	// Open a collection for report I/O. Overlapped so reads can be abandoned on a timeout - a HID++
	// request whose reply never arrives must not wedge the caller's thread forever.
	public static FileStream? Open( string devicePath )
	{
		SafeFileHandle deviceHandle;

		try
		{
			deviceHandle = PInvoke.CreateFile( devicePath, GenericRead | GenericWrite, FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE, null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED, null );
		}
		catch ( Exception )
		{
			return null;
		}

		if ( deviceHandle.IsInvalid )
		{
			deviceHandle.Dispose();

			return null;
		}

		try
		{
			return new FileStream( deviceHandle, FileAccess.ReadWrite, 0, true );
		}
		catch ( Exception )
		{
			deviceHandle.Dispose();

			return null;
		}
	}

	// Read one input report, giving up after timeoutMilliseconds. Returns the byte count, or 0 when the
	// read timed out or failed. The cancellation both unblocks us and cancels the pending overlapped read
	// at the driver, so a late reply cannot land in a later caller's buffer.
	public static int ReadWithTimeout( FileStream stream, byte[] buffer, int timeoutMilliseconds )
	{
		using var cancellationTokenSource = new CancellationTokenSource( timeoutMilliseconds );

		try
		{
			return stream.ReadAsync( buffer.AsMemory(), cancellationTokenSource.Token ).AsTask().GetAwaiter().GetResult();
		}
		catch ( Exception )
		{
			return 0;
		}
	}
}
