
using System.Runtime.InteropServices;
using System.Text;

namespace MarvinsAIRARefactored.Classes;

public static partial class LogitechGSDK
{
	public const int MaxControllers = 4;

	[LibraryImport( "LogitechSteeringWheelEnginesWrapper" )]
	[UnmanagedCallConv( CallConvs = [ typeof( System.Runtime.CompilerServices.CallConvCdecl ) ] )]
	[return: MarshalAs( UnmanagedType.I1 )]
	public static partial bool LogiSteeringInitializeWithWindow( [MarshalAs( UnmanagedType.I1 )] bool ignoreXInputControllers, nint hwnd );

	[LibraryImport( "LogitechSteeringWheelEnginesWrapper" )]
	[UnmanagedCallConv( CallConvs = [ typeof( System.Runtime.CompilerServices.CallConvCdecl ) ] )]
	public static partial void LogiSteeringShutdown();

	[LibraryImport( "LogitechSteeringWheelEnginesWrapper" )]
	[UnmanagedCallConv( CallConvs = [ typeof( System.Runtime.CompilerServices.CallConvCdecl ) ] )]
	[return: MarshalAs( UnmanagedType.I1 )]
	public static partial bool LogiIsConnected( int index );

	[DllImport( "LogitechSteeringWheelEnginesWrapper", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode )]
	[return: MarshalAs( UnmanagedType.I1 )]
	public static extern bool LogiGetFriendlyProductName( int index, StringBuilder str, int size );

	[LibraryImport( "LogitechSteeringWheelEnginesWrapper" )]
	[UnmanagedCallConv( CallConvs = [ typeof( System.Runtime.CompilerServices.CallConvCdecl ) ] )]
	[return: MarshalAs( UnmanagedType.I1 )]
	public static partial bool LogiUpdate();

	[LibraryImport( "LogitechSteeringWheelEnginesWrapper" )]
	[UnmanagedCallConv( CallConvs = [ typeof( System.Runtime.CompilerServices.CallConvCdecl ) ] )]
	[return: MarshalAs( UnmanagedType.I1 )]
	public static partial bool LogiPlayLeds( int index, float currentRPM, float rpmFirstLedTurnsOn, float rpmRedLine );

	[LibraryImport( "LogitechSteeringWheelEnginesWrapper" )]
	[UnmanagedCallConv( CallConvs = [ typeof( System.Runtime.CompilerServices.CallConvCdecl ) ] )]
	[return: MarshalAs( UnmanagedType.Bool )]
	public static partial bool LogiPlayLedsDInput( nint deviceHandle, float currentRPM, float rpmFirstLedTurnsOn, float rpmRedLine );
}
