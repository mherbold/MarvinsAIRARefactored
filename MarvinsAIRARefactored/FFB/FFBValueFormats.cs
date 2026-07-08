
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

	/// <summary>SlewCompressor.Threshold: Linear mode reproduces the old slew threshold (MaxForce/1000, Nm/ms); Soft mode (the Multi pipeline) shows the raw knee coefficient, whose scale is ~100x smaller.</summary>
	public static readonly Func<FFBFormatContext, string> SlewThreshold = ctx =>
		(int) ctx.Sibling( "Mode", Modules.SlewCompressorModule.ModeLinear ) == Modules.SlewCompressorModule.ModeSoft
			? Fixed( ctx.Value, 4 )
			: Fixed( ctx.Value * FFBFormatContext.MaxForce / 1000f, 2 ) + FFBFormatContext.Localized( "SlewUnits" );

	private static string Fixed( float value, int decimals ) => value.ToString( "F" + decimals, CultureInfo.CurrentCulture );
}
