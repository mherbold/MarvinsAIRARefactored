
using System.Runtime.CompilerServices;

namespace MarvinsAIRARefactored.GameBridges;

// Fixed-size inline value arrays used by the game bridge shared memory structs. [InlineArray] lays the
// elements out contiguously inside the struct itself (same byte layout as a C fixed-size array), so the
// structs stay blittable and can be read straight out of a byte buffer with MemoryMarshal.Read with ZERO
// heap allocations. The previous [MarshalAs(ByValArray)] representation forced Marshal.PtrToStructure to
// box the struct and allocate a new managed array for every array field on every read - at the bridges'
// 360 Hz sub-sample rate on the multimedia timer worker thread that was constant GC pressure on the FFB
// hot path. Inline arrays index like normal arrays and convert implicitly to ReadOnlySpan<T>.

[InlineArray( 2 )] public struct FloatArray2 { private float _element0; }
[InlineArray( 3 )] public struct FloatArray3 { private float _element0; }
[InlineArray( 4 )] public struct FloatArray4 { private float _element0; }
[InlineArray( 5 )] public struct FloatArray5 { private float _element0; }
[InlineArray( 12 )] public struct FloatArray12 { private float _element0; }

[InlineArray( 3 )] public struct DoubleArray3 { private double _element0; }
[InlineArray( 4 )] public struct DoubleArray4 { private double _element0; }

[InlineArray( 3 )] public struct IntArray3 { private int _element0; }
[InlineArray( 4 )] public struct IntArray4 { private int _element0; }
[InlineArray( 12 )] public struct IntArray12 { private int _element0; }

[InlineArray( 3 )] public struct SByteArray3 { private sbyte _element0; }

[InlineArray( 2 )] public struct ByteArray2 { private byte _element0; }
[InlineArray( 8 )] public struct ByteArray8 { private byte _element0; }
[InlineArray( 16 )] public struct ByteArray16 { private byte _element0; }
[InlineArray( 18 )] public struct ByteArray18 { private byte _element0; }
[InlineArray( 24 )] public struct ByteArray24 { private byte _element0; }
[InlineArray( 32 )] public struct ByteArray32 { private byte _element0; }
[InlineArray( 48 )] public struct ByteArray48 { private byte _element0; }
[InlineArray( 64 )] public struct ByteArray64 { private byte _element0; }
[InlineArray( 111 )] public struct ByteArray111 { private byte _element0; }
[InlineArray( 200 )] public struct ByteArray200 { private byte _element0; }

[InlineArray( 15 )] public struct CharArray15 { private char _element0; }
[InlineArray( 33 )] public struct CharArray33 { private char _element0; }
