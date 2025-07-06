
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.Classes;

public partial class LogitechGSDK
{
	[LibraryImport( "LogitechSteeringWheelEnginesWrapper" )]
	[UnmanagedCallConv( CallConvs = [ typeof( System.Runtime.CompilerServices.CallConvCdecl ) ] )]
	[return: MarshalAs( UnmanagedType.Bool )]
	public static partial bool LogiPlayLedsDInput( nint deviceHandle, float currentRPM, float rpmFirstLedTurnsOn, float rpmRedLine );
}
