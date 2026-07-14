
using System.Globalization;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Context handed to a setting's value formatter: the value being formatted, a resolver for sibling settings in
/// the same module (so a format can depend on e.g. the module's Mode), and the live wheel force scalars plus the
/// localized unit table. Everything is read at call time so displays track a language switch or a force change.
/// </summary>
public readonly struct FFBFormatContext
{
	public readonly float Value;

	private readonly Func<string, float, float>? _sibling;

	public FFBFormatContext( float value, Func<string, float, float>? sibling )
	{
		Value = value;
		_sibling = sibling;
	}

	/// <summary>The stored value of a sibling setting in the same module, or <paramref name="fallback"/> when unset.</summary>
	public float Sibling( string key, float fallback = 0f ) => _sibling != null ? _sibling( key, fallback ) : fallback;

	/// <summary>Live effective wheel MaxForce (Nm). The two compression thresholds scale their display by this, as the old code did.</summary>
	public static float MaxForce
	{
		get
		{
			var settings = DataContext.DataContext.Instance?.Settings;

			return settings != null ? settings.RacingWheelMaxForce : 1f;
		}
	}

	/// <summary>Live effective wheel force (Nm) at 100% strength. Strength/output torque conversions scale by this, as the old code did.</summary>
	public static float WheelForce
	{
		get
		{
			var settings = DataContext.DataContext.Instance?.Settings;

			return settings != null ? settings.RacingWheelWheelForce : 1f;
		}
	}

	/// <summary>A localized string (unit suffix, "OFF", ...). Unit values already carry their own leading space where needed. Empty if unavailable.</summary>
	public static string Localized( string key )
	{
		var localization = DataContext.DataContext.Instance?.Localization;

		return localization != null ? localization[ key ] : string.Empty;
	}
}

/// <summary>
/// Value-string formatters for FFB module knobs, reproducing the unit/percent presentation the old per-setting
/// <c>Settings.Update*String()</c> methods produced (30% not 0.3, 5.0 Nm not 5, etc.). Each factory returns a
/// <see cref="Func{FFBFormatContext, String}"/> assigned to a setting descriptor's FormatValue.
/// </summary>
public static class FFBValueFormats
{
	/// <summary>0..1 fraction shown as a percentage: 0.30 -> "30%".</summary>
	public static Func<FFBFormatContext, string> Percent( int decimals = 0 )
		=> ctx => Fixed( ctx.Value * 100f, decimals ) + FFBFormatContext.Localized( "Percent" );

	/// <summary>Fixed-precision number with an optional localized unit (which already includes its leading space): "5.0 Nm", "20 Hz", "1.00".</summary>
	public static Func<FFBFormatContext, string> Number( int decimals, string? unitKey = null )
		=> ctx => Fixed( ctx.Value, decimals ) + ( unitKey != null ? FFBFormatContext.Localized( unitKey ) : string.Empty );

	/// <summary>Value scaled by the live MaxForce (times <paramref name="extraScale"/>) with a unit: total-compression torque (Nm), slew rate (Nm/ms).</summary>
	public static Func<FFBFormatContext, string> MaxForceScaled( float extraScale, int decimals, string unitKey )
		=> ctx => Fixed( ctx.Value * FFBFormatContext.MaxForce * extraScale, decimals ) + FFBFormatContext.Localized( unitKey );

	/// <summary>A 0..1 strength as a percentage plus its WheelForce-relative torque: "50% (12.0 Nm)". Renders localized "OFF" at <paramref name="offValue"/> (0 for strengths / output minimum, 1 for output maximum).</summary>
	public static Func<FFBFormatContext, string> StrengthWithTorque( int percentDecimals = 0, float offValue = 0f )
		=> ctx =>
		{
			if ( ctx.Value == offValue )
			{
				return FFBFormatContext.Localized( "OFF" );
			}

			var percent = Fixed( ctx.Value * 100f, percentDecimals ) + FFBFormatContext.Localized( "Percent" );
			var torque = Fixed( ctx.Value * FFBFormatContext.WheelForce, 1 ) + FFBFormatContext.Localized( "TorqueUnits" );

			return $"{percent} ({torque})";
		};

	/// <summary>Gain-style multiplier: "1.50x" — the x marks the value as a scale factor, not an absolute amount.</summary>
	public static Func<FFBFormatContext, string> Multiplier( int decimals )
		=> ctx => Fixed( ctx.Value, decimals ) + "x";

	/// <summary>Wrap a formatter to render localized "OFF" at the value that makes the stage a pass-through
	/// (0 for most strengths/curves, a range end for things like Maximum or ParkedStrength).</summary>
	public static Func<FFBFormatContext, string> WithOff( Func<FFBFormatContext, string> inner, float offValue = 0f )
		=> ctx => ctx.Value == offValue ? FFBFormatContext.Localized( "OFF" ) : inner( ctx );

	/// <summary>Compressor.Ratio: "4.0:1" — N Nm over the threshold in, 1 Nm out. The stored value is the ratio
	/// number itself, so the knob's click-to-type edit (first-float parse) round-trips naturally.</summary>
	public static readonly Func<FFBFormatContext, string> Ratio = ctx => Fixed( ctx.Value, 1 ) + ":1";

	/// <summary>AdaptiveSmoother.Amount: percent plus the derived cutoff floor it maps to ("50% (13.4 Hz)") — the
	/// knob is logarithmic in the floor, FloorMaxHz^(1−amount). Localized "OFF" at 0 (the module bypasses exactly).</summary>
	public static readonly Func<FFBFormatContext, string> SmootherAmount = ctx =>
	{
		if ( ctx.Value == 0f )
		{
			return FFBFormatContext.Localized( "OFF" );
		}

		var percent = Fixed( ctx.Value * 100f, 0 ) + FFBFormatContext.Localized( "Percent" );
		var floorHz = Fixed( MathF.Pow( Modules.AdaptiveSmootherModule.FloorMaxHz, 1f - ctx.Value ), 1 ) + FFBFormatContext.Localized( "HertzUnits" );

		return $"{percent} ({floorHz})";
	};

	/// <summary>Prediction horizon in 360 Hz sub-ticks: localized "OFF" at 0, otherwise "K{n}" (K6 = one iRacing
	/// 60 Hz tick ahead, K12 = two). The stored value is the sub-tick count; the module rounds it to an integer.</summary>
	public static readonly Func<FFBFormatContext, string> PredictionHorizon = ctx =>
	{
		var k = (int) MathF.Round( ctx.Value );

		return ( k <= 0 ) ? FFBFormatContext.Localized( "OFF" ) : $"K{k}";
	};

	private static string Fixed( float value, int decimals ) => value.ToString( "F" + decimals, CultureInfo.CurrentCulture );
}
