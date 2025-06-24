
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.PInvoke;

partial class WinMM
{
	[Flags]
	public enum fuEvent : uint
	{
		TIME_ONESHOT = 0x00,
		TIME_PERIODIC = 0x01,
		TIME_CALLBACK_EVENT_SET = 0x10
	}

	// MMRESULT timeBeginPeriod( UINT uPeriod );
	[LibraryImport( "winmm.dll", EntryPoint = "timeBeginPeriod" )]
	public static partial uint TimeBeginPeriod( uint uPeriod );

	// MMRESULT timeEndPeriod( UINT uPeriod );
	[LibraryImport( "winmm.dll", EntryPoint = "timeEndPeriod" )]
	public static partial uint TimeEndPeriod( uint uPeriod );

	// MMRESULT timeSetEvent( UINT uDelay, UINT uResolution, LPTIMECALLBACK lpTimeProc, DWORD_PTR dwUser, UINT fuEvent );
	[LibraryImport( "winmm.dll", EntryPoint = "timeSetEvent", SetLastError = true )]
	public static partial uint TimeSetEvent( uint uDelay, uint uResolution, nint lpTimeProc, ref uint dwUser, uint fuEvent );

	// MMRESULT timeKillEvent( UINT uTimerID );
	[LibraryImport( "winmm.dll", EntryPoint = "timeKillEvent" )]
	public static partial uint TimeKillEvent( uint uTimerID );
}
