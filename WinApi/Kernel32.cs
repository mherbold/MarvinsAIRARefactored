
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.WinApi;

partial class Kernel32
{
	[StructLayout( LayoutKind.Sequential )]
	public struct PROCESS_POWER_THROTTLING_STATE
	{
		public uint Version;
		public uint ControlMask;
		public uint StateMask;
	}

	public enum ProcessInformationClass : uint
	{
		ProcessPowerThrottling = 4
	}

	public enum ControlMask : uint
	{ 
		PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 4
	}

	// BOOL SetProcessInformation( HANDLE hProcess, PROCESS_INFORMATION_CLASS ProcessInformationClass, LPVOID ProcessInformation, DWORD ProcessInformationSize );
	[LibraryImport( "kernel32.dll", SetLastError = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	public static partial bool SetProcessInformation( nint hProcess, uint ProcessInformationClass, ref nint ProcessInformation, uint ProcessInformationSize );
}
