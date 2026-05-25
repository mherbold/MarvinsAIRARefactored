
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.Classes;

public static class CpuAffinityHelper
{
	public static void SetCpuAffinity( ulong affinityMaskBits )
	{
		var process = Process.GetCurrentProcess();

		process.ProcessorAffinity = (IntPtr) affinityMaskBits;
	}

	public sealed record PhysicalCoreOption( int Index, string Label, ulong AffinityMaskBits, IReadOnlyList<int> LogicalProcessorNumbers, bool? IsPerformanceCore );

	public static IReadOnlyList<PhysicalCoreOption> GetPhysicalCoreOptions()
	{
		var coreDescriptors = GetPhysicalCoreDescriptors();

		var isHybridCpu = coreDescriptors.Select( coreDescriptor => coreDescriptor.EfficiencyClass ).Distinct().Count() > 1;

		var maxEfficiencyClass = coreDescriptors.Max( coreDescriptor => coreDescriptor.EfficiencyClass );

		var orderedPhysicalCoreOptions = coreDescriptors
			.Select( coreDescriptor =>
			{
				var logicalProcessorNumbers = GetLogicalProcessorNumbers( coreDescriptor.AffinityMaskBits );
				var baseLabel = string.Join( "/", logicalProcessorNumbers.Select( logicalProcessorNumber => logicalProcessorNumber + 1 ) );

				bool? isPerformanceCore = null;
				var label = baseLabel;

				if ( isHybridCpu )
				{
					isPerformanceCore = coreDescriptor.EfficiencyClass == maxEfficiencyClass;
					label += isPerformanceCore.Value ? " (P)" : " (E)";
				}

				return new
				{
					coreDescriptor.AffinityMaskBits,
					coreDescriptor.EfficiencyClass,
					LogicalProcessorNumbers = logicalProcessorNumbers,
					Label = label,
					IsPerformanceCore = isPerformanceCore
				};
			} )
			.OrderBy( coreOption => coreOption.LogicalProcessorNumbers.Min() )
			.ToList();

		var physicalCoreOptions = orderedPhysicalCoreOptions
			.Select( ( coreOption, index ) => new PhysicalCoreOption(
				Index: index,
				Label: coreOption.Label,
				AffinityMaskBits: coreOption.AffinityMaskBits,
				LogicalProcessorNumbers: coreOption.LogicalProcessorNumbers,
				IsPerformanceCore: coreOption.IsPerformanceCore ) )
			.ToList();

		return physicalCoreOptions;
	}

	public static ulong BuildAffinityMask( IEnumerable<int> selectedPhysicalCoreIndexes, IReadOnlyList<PhysicalCoreOption> allPhysicalCoreOptions )
	{
		var selectedIndexSet = new HashSet<int>( selectedPhysicalCoreIndexes );
		var affinityMaskBits = 0UL;

		foreach ( var physicalCoreOption in allPhysicalCoreOptions )
		{
			if ( selectedIndexSet.Contains( physicalCoreOption.Index ) )
			{
				affinityMaskBits |= physicalCoreOption.AffinityMaskBits;
			}
		}

		return affinityMaskBits;
	}

	public static ulong GetProcessAffinityMask( Process process )
	{
		if ( !GetProcessAffinityMask( process.Handle, out var processAffinityMask, out _ ) )
		{
			throw new Win32Exception( Marshal.GetLastWin32Error() );
		}

		return unchecked((ulong) processAffinityMask.ToInt64());
	}

	private static List<PhysicalCoreDescriptor> GetPhysicalCoreDescriptors()
	{
		var requiredBufferLength = 0;

		_ = GetLogicalProcessorInformationEx( LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, IntPtr.Zero, ref requiredBufferLength );

		if ( requiredBufferLength == 0 )
		{
			throw new InvalidOperationException( "Unable to determine the required buffer length for CPU topology." );
		}

		var bufferPointer = Marshal.AllocHGlobal( requiredBufferLength );

		try
		{
			if ( !GetLogicalProcessorInformationEx( LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, bufferPointer, ref requiredBufferLength ) )
			{
				throw new Win32Exception( Marshal.GetLastWin32Error() );
			}

			var physicalCoreDescriptors = new List<PhysicalCoreDescriptor>();
			var currentPointer = bufferPointer;
			var endPointer = IntPtr.Add( bufferPointer, requiredBufferLength );

			while ( currentPointer.ToInt64() < endPointer.ToInt64() )
			{
				var relationship = Marshal.ReadInt32( currentPointer );
				var size = Marshal.ReadInt32( IntPtr.Add( currentPointer, 4 ) );

				if ( relationship == (int) LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore )
				{
					var flags = Marshal.ReadByte( IntPtr.Add( currentPointer, 8 ) );
					var efficiencyClass = Marshal.ReadByte( IntPtr.Add( currentPointer, 9 ) );
					// GroupCount is at offset 30: 8 (base) + 1 (Flags) + 1 (EfficiencyClass) + 20 (Reserved)
					var groupCount = (ushort) Marshal.ReadInt16( IntPtr.Add( currentPointer, 30 ) );

					if ( groupCount < 1 )
					{
						throw new InvalidOperationException( "Invalid processor topology returned by Windows." );
					}

					// GROUP_AFFINITY array starts at offset 32
					var firstGroupAffinityPointer = IntPtr.Add( currentPointer, 32 );
					var firstGroupAffinity = Marshal.PtrToStructure<GROUP_AFFINITY>( firstGroupAffinityPointer );

					if ( firstGroupAffinity.Group != 0 )
					{
						throw new NotSupportedException( "This helper currently supports only processor group 0 systems." );
					}

					physicalCoreDescriptors.Add( new PhysicalCoreDescriptor( AffinityMaskBits: firstGroupAffinity.Mask, EfficiencyClass: efficiencyClass, HasSmtSibling: ( flags & 0x1 ) != 0 ) );
				}

				currentPointer = IntPtr.Add( currentPointer, size );
			}

			return physicalCoreDescriptors;
		}
		finally
		{
			Marshal.FreeHGlobal( bufferPointer );
		}
	}

	private static IReadOnlyList<int> GetLogicalProcessorNumbers( ulong affinityMaskBits )
	{
		var logicalProcessorNumbers = new List<int>();

		for ( var bitIndex = 0; bitIndex < 64; bitIndex++ )
		{
			var bitMask = 1UL << bitIndex;

			if ( ( affinityMaskBits & bitMask ) != 0 )
			{
				logicalProcessorNumbers.Add( bitIndex );
			}
		}

		return logicalProcessorNumbers;
	}

	private sealed record PhysicalCoreDescriptor( ulong AffinityMaskBits, byte EfficiencyClass, bool HasSmtSibling );

	[DllImport( "kernel32.dll", SetLastError = true )]
	private static extern bool GetLogicalProcessorInformationEx( LOGICAL_PROCESSOR_RELATIONSHIP relationshipType, IntPtr buffer, ref int returnedLength );

	[DllImport( "kernel32.dll", SetLastError = true )]
	private static extern bool GetProcessAffinityMask( IntPtr processHandle, out IntPtr processAffinityMask, out IntPtr systemAffinityMask );

	private enum LOGICAL_PROCESSOR_RELATIONSHIP
	{
		RelationProcessorCore = 0,
		RelationNumaNode = 1,
		RelationCache = 2,
		RelationProcessorPackage = 3,
		RelationGroup = 4,
		RelationAll = 0xffff
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct GROUP_AFFINITY
	{
		public ulong Mask;
		public ushort Group;
		public ushort Reserved1;
		public ushort Reserved2;
		public ushort Reserved3;
	}
}