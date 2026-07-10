
using System.Reflection;

using MarvinsAIRARefactored.Components;

using Settings = MarvinsAIRARefactored.DataContext.Settings;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Builds the built-in graphs by COMPOSING the DSP primitives so each one reproduces an old algorithm (per the
/// decomposition identities documented in DspModules), and converts the old flat settings into the equivalent
/// module setting values.
/// <para>Values are read through a reflection <see cref="SettingsSource"/> so the exact same builder maps from
/// either the live <c>Settings</c> or a per-context <c>ContextSettings</c> (they share property names); anything
/// present only on <c>Settings</c> — e.g. the global prediction mode/blend — falls back to the live settings.
/// Structure (which modules exist, and their deterministic ids) depends only on the algorithm and the collapsed
/// Multi source mode, so building from any source yields identical module ids and therefore stable composite
/// value keys.</para>
/// </summary>
public static class FFBGraphMigration
{
	// ---- built-in graph display names -------------------------------------------------------------------

	public static string BuiltInGraphNameFor( RacingWheel.Algorithm algorithm )
	{
		return algorithm switch
		{
			RacingWheel.Algorithm.Native60Hz => "Native 60 Hz",
			RacingWheel.Algorithm.Native360Hz => "Native 360 Hz",
			RacingWheel.Algorithm.DetailBooster => "Detail booster",
			RacingWheel.Algorithm.DeltaLimiter => "Delta limiter",
			RacingWheel.Algorithm.DetailBoosterOn60Hz => "Detail booster on 60 Hz",
			RacingWheel.Algorithm.DeltaLimiterOn60Hz => "Delta limiter on 60 Hz",
			RacingWheel.Algorithm.SlewAndTotalCompression => "Slew and total compression",
			RacingWheel.Algorithm.MultiAdjustmentToolkit => "Multi adjustment toolkit",
			_ => "Native 360 Hz"
		};
	}

	/// <summary>Collapse the old Multi source selection (incl. Defaults*/Preset* values) to a base source mode.</summary>
	public static RacingWheel.MultiFFBSourceOptions CollapseMultiSource( RacingWheel.MultiFFBSourceOptions source )
	{
		return source switch
		{
			RacingWheel.MultiFFBSourceOptions.Native60Hz or RacingWheel.MultiFFBSourceOptions.DefaultsNative60Hz => RacingWheel.MultiFFBSourceOptions.Native60Hz,
			RacingWheel.MultiFFBSourceOptions.Native360Hz or RacingWheel.MultiFFBSourceOptions.DefaultsNative360Hz => RacingWheel.MultiFFBSourceOptions.Native360Hz,
			RacingWheel.MultiFFBSourceOptions.Hybrid10 or RacingWheel.MultiFFBSourceOptions.DefaultsHybrid10 => RacingWheel.MultiFFBSourceOptions.Hybrid10,
			RacingWheel.MultiFFBSourceOptions.HybridVariable30 or RacingWheel.MultiFFBSourceOptions.DefaultsHybridVariable30
				or RacingWheel.MultiFFBSourceOptions.PresetBasicFFB or RacingWheel.MultiFFBSourceOptions.PresetBalancedFFB => RacingWheel.MultiFFBSourceOptions.HybridVariable30,
			_ => RacingWheel.MultiFFBSourceOptions.Native360Hz
		};
	}

	/// <summary>
	/// The old wheel-side setting base names whose per-context switches are OR-unioned into the single FFB graph
	/// values scope at migration time (granularity loss — noted in the release notes). Reflection reads each
	/// <c>{name}ContextSwitches</c> tolerantly, so names without a context-switch property are simply skipped.
	/// </summary>
	public static readonly string[] MigratedWheelSettingBaseNames =
	[
		"RacingWheelAlgorithm", "RacingWheelEnableSoftLimiter",
		"RacingWheelDetailBoost", "RacingWheelDetailBoostBias", "RacingWheelDeltaLimit", "RacingWheelDeltaLimiterBias",
		"RacingWheelSlewCompressionThreshold", "RacingWheelSlewCompressionRate", "RacingWheelTotalCompressionThreshold", "RacingWheelTotalCompressionRate",
		"RacingWheelMulti360HzDetail", "RacingWheelMultiTorqueCompression", "RacingWheelMultiEnableSlewPeakMode", "RacingWheelMultiSlewRateReduction", "RacingWheelMultiDetailGain", "RacingWheelMultiOutputSmoothing",
		"RacingWheelOutputMinimum", "RacingWheelOutputMaximum", "RacingWheelOutputCurve",
		"RacingWheelLFEStrength",
		"RacingWheelCrashProtectionLongitudalGForce", "RacingWheelCrashProtectionLateralGForce", "RacingWheelCrashProtectionDuration", "RacingWheelCrashProtectionForceReduction",
		"RacingWheelCurbProtectionShockVelocity", "RacingWheelCurbProtectionDuration", "RacingWheelCurbProtectionForceReduction",
		"RacingWheelParkedStrength", "RacingWheelParkedFriction", "RacingWheelSoftLockStrength", "RacingWheelFriction",
		"RacingWheelWheelCenteringStrength", "RacingWheelCenterWheelWhileRacing", "RacingWheelCenterWheelWhileParked",
		"RacingWheelShiftRPMVibrateStrength", "RacingWheelGearChangeVibrateStrength", "RacingWheelABSVibrateStrength",
		"SteeringEffectsUndersteerEnabled", "SteeringEffectsUndersteerWheelVibrationPattern", "SteeringEffectsUndersteerWheelVibrationStrength", "SteeringEffectsUndersteerWheelVibrationMinimumFrequency", "SteeringEffectsUndersteerWheelVibrationMaximumFrequency", "SteeringEffectsUndersteerWheelVibrationCurve",
		"SteeringEffectsUndersteerWheelConstantForceDirection", "SteeringEffectsUndersteerWheelConstantForceStrength", "SteeringEffectsUndersteerWheelConstantForceCurve",
		"SteeringEffectsOversteerEnabled", "SteeringEffectsOversteerWheelVibrationPattern", "SteeringEffectsOversteerWheelVibrationStrength", "SteeringEffectsOversteerWheelVibrationMinimumFrequency", "SteeringEffectsOversteerWheelVibrationMaximumFrequency", "SteeringEffectsOversteerWheelVibrationCurve",
		"SteeringEffectsOversteerWheelConstantForceDirection", "SteeringEffectsOversteerWheelConstantForceStrength", "SteeringEffectsOversteerWheelConstantForceCurve",
		"SteeringEffectsSeatOfPantsEnabled", "SteeringEffectsSeatOfPantsWheelVibrationPattern", "SteeringEffectsSeatOfPantsWheelVibrationStrength", "SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency", "SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency", "SteeringEffectsSeatOfPantsWheelVibrationCurve",
		"SteeringEffectsSeatOfPantsWheelConstantForceDirection", "SteeringEffectsSeatOfPantsWheelConstantForceStrength", "SteeringEffectsSeatOfPantsWheelConstantForceCurve"
	];

	// ---- reflection value source ------------------------------------------------------------------------

	/// <summary>Reads named settings from either a live <c>Settings</c> or a <c>ContextSettings</c>, falling back to the live settings for globals not present on the source.</summary>
	private sealed class SettingsSource
	{
		private readonly object _source;
		private readonly Settings _fallback;

		public SettingsSource( object source, Settings fallback )
		{
			_source = source;
			_fallback = fallback;
		}

		public float F( string name ) => Convert.ToSingle( Get( name ) );
		public int E( string name ) => Convert.ToInt32( Get( name ) );      // enums stored as their index
		public float B( string name ) => (bool) ( Get( name ) ?? false ) ? 1f : 0f;

		private object? Get( string name )
		{
			var property = _source.GetType().GetProperty( name );

			if ( property != null )
			{
				return property.GetValue( _source );
			}

			return typeof( Settings ).GetProperty( name )?.GetValue( _fallback );
		}
	}

	// ---- module-list assembly helper --------------------------------------------------------------------

	private sealed class Builder
	{
		public readonly FFBGraph Graph;
		private readonly string _keyPrefix;
		private int _seq;

		public Builder( string name, string keyPrefix )
		{
			Graph = new FFBGraph { Name = name, IsBuiltIn = true };
			_keyPrefix = keyPrefix;
		}

		public string Add( string moduleType, string inputA, params (string key, float value)[] values ) => AddInternal( moduleType, inputA, inputA, values );

		public string Add2( string moduleType, string inputA, string inputB, params (string key, float value)[] values ) => AddInternal( moduleType, inputA, inputB, values );

		private string AddInternal( string moduleType, string inputA, string inputB, (string key, float value)[] values )
		{
			var id = $"{_keyPrefix}.{_seq:D2}.{moduleType}";

			_seq++;

			var data = new FFBModuleData( id, moduleType )
			{
				InputAModuleId = inputA,
				InputBModuleId = inputB
			};

			foreach ( var (key, value) in values )
			{
				data.SettingValues[ key ] = value;
			}

			Graph.Modules.Add( data );

			return id;
		}

		public void AddSources( SettingsSource src )
		{
			var source60 = new FFBModuleData( FFBGraph.Source60ModuleId, FFBModuleRegistry.Source60HzType );

			source60.SettingValues[ "PredictionMode" ] = src.E( "RacingWheelPredictionMode" );
			source60.SettingValues[ "PredictionBlend" ] = src.F( "RacingWheelPredictionBlend" );

			Graph.Modules.Add( source60 );

			Graph.Modules.Add( new FFBModuleData( FFBGraph.Source360ModuleId, FFBModuleRegistry.Source360HzType ) );
			Graph.Modules.Add( new FFBModuleData( FFBGraph.SourceLFEModuleId, FFBModuleRegistry.SourceLFEType ) );
		}

		public void AddOutput( string inputA, SettingsSource src )
		{
			// Old settings expressed output minimum/maximum as a fraction of full scale; the new modules take Nm, so
			// convert with this source's MaxForce (= WheelForce / Strength; MaxForce itself is not a per-context
			// property, but WheelForce and Strength are, so this yields the right per-context full-scale reference).
			var wheelForce = src.F( "RacingWheelWheelForce" );
			var strength = src.F( "RacingWheelStrength" );
			var maxForce = strength != 0f ? wheelForce / strength : wheelForce;

			var oldMinimum = src.F( "RacingWheelOutputMinimum" );   // fraction of full scale (0..0.5)
			var oldMaximum = src.F( "RacingWheelOutputMaximum" );   // fraction of full scale (0.2..1)

			// The old Output applied, in order, curve -> soft limiter -> maximum -> minimum on the normalized signal.
			// Reproduce that with standalone modules ahead of a bare Output. Each stage's Enabled mirrors the old
			// guard (curve always ran and is an identity at 0; soft clip = its toggle; maximum active iff < full
			// scale; minimum active iff > 0) so a disabled stage passes the signal through exactly as before.
			var curve = Add( FFBModuleRegistry.CurveType, inputA, ( "Curve", src.F( "RacingWheelOutputCurve" ) ) );

			var softLimiter = Add( FFBModuleRegistry.SoftLimiterType, curve, ( "Enabled", src.B( "RacingWheelEnableSoftLimiter" ) ) );

			var maximum = Add( FFBModuleRegistry.MaximumType, softLimiter,
				( "Enabled", oldMaximum < 1f ? 1f : 0f ),
				( "Maximum", oldMaximum * maxForce ) );

			var minimum = Add( FFBModuleRegistry.MinimumType, maximum,
				( "Enabled", oldMinimum > 0f ? 1f : 0f ),
				( "Minimum", oldMinimum * maxForce ) );

			Graph.Modules.Add( new FFBModuleData( FFBGraph.OutputModuleId, FFBModuleRegistry.OutputType )
			{
				InputAModuleId = minimum
			} );
		}
	}

	// ---- DSP-core assembly ------------------------------------------------------------------------------

	// The filter modules' Cutoff is stored in Hz; the old bias settings were the one-pole coefficient α of
	// y += α(x − y) at the 360 Hz tick rate. Invert α = 1 − e^(−2π·fc/360): fc = −360·ln(1−α)/2π, clamped to
	// Nyquist (α = 1, which killed the detail entirely, maps to +∞).
	private static float CoefficientToCutoffHz( float coefficient )
	{
		coefficient = Math.Clamp( coefficient, 0f, 1f );

		return MathF.Min( -360f * MathF.Log( 1f - coefficient ) / MathF.Tau, 180f );
	}

	// Old compression rates were the fraction removed from the over-threshold excess (rate = 1 − 1/ratio).
	// Invert to the knob's N:1 ratio, capped at the knob max (rate → 1 means an ∞:1 hard ceiling).
	private static float RateToRatio( float rate )
	{
		return rate >= 0.95f ? 20f : MathF.Min( 1f / ( 1f - rate ), 20f );
	}

	private static string AppendAlgorithmModules( Builder builder, RacingWheel.Algorithm algorithm, RacingWheel.MultiFFBSourceOptions structureMultiSource, SettingsSource src )
	{
		var s60 = FFBGraph.Source60ModuleId;
		var s360 = FFBGraph.Source360ModuleId;

		switch ( algorithm )
		{
			case RacingWheel.Algorithm.Native60Hz:
				return s60;

			case RacingWheel.Algorithm.Native360Hz:
				return s360;

			case RacingWheel.Algorithm.DetailBooster:
			case RacingWheel.Algorithm.DetailBoosterOn60Hz:
			{
				var anchor = ( algorithm == RacingWheel.Algorithm.DetailBoosterOn60Hz ) ? s60 : s360;
				var bias = src.F( "RacingWheelDetailBoostBias" );

				var lpf = builder.Add( FFBModuleRegistry.LowPassFilterType, anchor, ( "Cutoff", CoefficientToCutoffHz( bias ) ) );
				var detail = builder.Add( FFBModuleRegistry.HighPassFilterType, s360, ( "Cutoff", CoefficientToCutoffHz( bias ) ) );

				// the extractor no longer has a built-in gain — boost via a dedicated Gain module (clamped to its range)
				var boostGain = MathF.Min( 1f + src.F( "RacingWheelDetailBoost" ), 5f );

				if ( boostGain != 1f )
				{
					detail = builder.Add( FFBModuleRegistry.GainType, detail, ( "Gain", boostGain ) );
				}

				return builder.Add2( FFBModuleRegistry.AddType, lpf, detail );
			}

			case RacingWheel.Algorithm.DeltaLimiter:
			case RacingWheel.Algorithm.DeltaLimiterOn60Hz:
			{
				var anchor = ( algorithm == RacingWheel.Algorithm.DeltaLimiterOn60Hz ) ? s60 : s360;
				var bias = src.F( "RacingWheelDeltaLimiterBias" );

				// old Limit units were (knob/500) Nm per tick; the slew limiter takes honest Nm/s → ×(360/500)
				var rate = builder.Add( FFBModuleRegistry.SlewLimiterType, s360, ( "Limit", src.F( "RacingWheelDeltaLimit" ) * 0.72f ) );
				var detail = builder.Add( FFBModuleRegistry.HighPassFilterType, rate, ( "Cutoff", CoefficientToCutoffHz( bias ) ) );
				var lpf = builder.Add( FFBModuleRegistry.LowPassFilterType, anchor, ( "Cutoff", CoefficientToCutoffHz( bias ) ) );

				return builder.Add2( FFBModuleRegistry.AddType, lpf, detail );
			}

			case RacingWheel.Algorithm.SlewAndTotalCompression:
			{
				// old threshold was (knob/500) of full scale per tick → Nm/s = knob × 0.72 × maxForce; the old
				// embedded total compression becomes a separate forward Compressor (the old feedback coupling —
				// compressed output feeding the slew integrator — is gone; slight feel change), with the old
				// width = threshold convention carried into its Knee
				var wheelForce = src.F( "RacingWheelWheelForce" );
				var strength = src.F( "RacingWheelStrength" );
				var maxForce = strength != 0f ? wheelForce / strength : wheelForce;

				var slew = builder.Add( FFBModuleRegistry.SlewCompressorType, s360,
					( "Threshold", src.F( "RacingWheelSlewCompressionThreshold" ) * 0.72f * maxForce ),
					( "Knee", 0f ),
					( "Ratio", RateToRatio( src.F( "RacingWheelSlewCompressionRate" ) ) ) );

				var totalCompressionRate = src.F( "RacingWheelTotalCompressionRate" );

				if ( totalCompressionRate <= 0f )
				{
					return slew;
				}

				var totalCompressionThresholdNm = src.F( "RacingWheelTotalCompressionThreshold" ) * maxForce;

				return builder.Add( FFBModuleRegistry.CompressorType, slew,
					( "Threshold", totalCompressionThresholdNm ),
					( "Knee", totalCompressionThresholdNm ),
					( "Ratio", RateToRatio( totalCompressionRate ) ) );
			}

			case RacingWheel.Algorithm.MultiAdjustmentToolkit:
				return AppendMultiModules( builder, structureMultiSource, src );
		}

		return s360;
	}

	private static string AppendMultiModules( Builder builder, RacingWheel.MultiFFBSourceOptions structureMultiSource, SettingsSource src )
	{
		var s60 = FFBGraph.Source60ModuleId;
		var s360 = FFBGraph.Source360ModuleId;

		var detail = src.F( "RacingWheelMulti360HzDetail" );

		string sourceId;

		switch ( structureMultiSource )
		{
			case RacingWheel.MultiFFBSourceOptions.Native60Hz:
				sourceId = s60;
				break;

			case RacingWheel.MultiFFBSourceOptions.Hybrid10:
			{
				var lpf = builder.Add( FFBModuleRegistry.LowPassFilterType, s60, ( "Cutoff", CoefficientToCutoffHz( 0.1f ) ) );
				var detailId = builder.Add( FFBModuleRegistry.HighPassFilterType, s360, ( "Cutoff", CoefficientToCutoffHz( 0.1f ) ) );

				if ( detail != 1f )
				{
					detailId = builder.Add( FFBModuleRegistry.GainType, detailId, ( "Gain", detail ) );
				}

				sourceId = builder.Add2( FFBModuleRegistry.AddType, lpf, detailId );
				break;
			}

			case RacingWheel.MultiFFBSourceOptions.HybridVariable30:
			{
				// old Mix/PeakMix were one-pole coefficients → Hz corners; the old 10-tick hold → ms; the old
				// Detail knob is now a Gain on the A input (input A enters only via its delta, so it is exact)
				var detailId = s360;

				if ( detail != 1f )
				{
					detailId = builder.Add( FFBModuleRegistry.GainType, s360, ( "Gain", detail ) );
				}

				sourceId = builder.Add2( FFBModuleRegistry.AdaptiveBlendType, detailId, s60,
					( "Cutoff", CoefficientToCutoffHz( 0.3f ) ),
					( "PeakCutoff", CoefficientToCutoffHz( 0.1f ) ),
					( "Hold", 10f * 1000f / 360f ) );
				break;
			}

			case RacingWheel.MultiFFBSourceOptions.Native360Hz:
			default:
				sourceId = s360;
				break;
		}

		var torqueCompression = src.F( "RacingWheelMultiTorqueCompression" );

		// the compressor now takes Nm (threshold/width) and an N:1 ratio — convert the old fractions with this
		// source's full-scale reference (WheelForce / Strength, same as AddOutput) and the old rate via 1/(1−rate)
		var wheelForce = src.F( "RacingWheelWheelForce" );
		var strength = src.F( "RacingWheelStrength" );
		var maxForce = strength != 0f ? wheelForce / strength : wheelForce;

		var oldCompressionRate = MathF.Min( 2f * torqueCompression, 0.75f );

		var compressorId = builder.Add( FFBModuleRegistry.CompressorType, sourceId,
			( "Threshold", ( 1f - 0.75f * torqueCompression ) * maxForce ),
			( "Ratio", RateToRatio( oldCompressionRate ) ),
			( "Knee", MathF.Min( torqueCompression, 0.5f ) * maxForce ) );

		var slewAmount = src.F( "RacingWheelMultiSlewRateReduction" );

		// old soft-mode threshold/width were normalized-per-tick values → Nm/s = value × maxForce × 360; the old
		// stage was inert at slewAmount 0 (its derived rate hit 0), so it is omitted entirely there
		var slewId = compressorId;

		if ( slewAmount > 0f )
		{
			slewId = builder.Add( FFBModuleRegistry.SlewCompressorType, compressorId,
				( "Threshold", ( 0.01f - 0.0095f * slewAmount ) * maxForce * 360f ),
				( "Knee", MathF.Min( MathF.Pow( slewAmount, 0.005f ), 0.0025f ) * maxForce * 360f ),
				( "Ratio", RateToRatio( MathF.Min( MathF.Pow( slewAmount, 0.55f ), 0.9f ) ) ),
				( "PeakMode", src.B( "RacingWheelMultiEnableSlewPeakMode" ) ) );
		}

		// the transient enhancer outputs detail only — the old DetailGain stage (body + enhanced detail) is
		// recomposed as LowPassFilter + TransientEnhancer summed by a Mixer, with the old 1+gain multiplier
		// baked into the enhancer's Gain; gain 0 meant pass-through, so the whole stage is omitted there
		var multiDetailGain = src.F( "RacingWheelMultiDetailGain" );

		var detailGainId = slewId;

		if ( multiDetailGain != 0f )
		{
			var bodyId = builder.Add( FFBModuleRegistry.LowPassFilterType, slewId, ( "Cutoff", CoefficientToCutoffHz( 0.11809f ) ) );
			var transientId = builder.Add( FFBModuleRegistry.TransientEnhancerType, slewId,
				( "Cutoff", CoefficientToCutoffHz( 0.11809f ) ), ( "Gain", Math.Clamp( 1f + multiDetailGain, 0f, 5f ) ) );

			detailGainId = builder.Add2( FFBModuleRegistry.AddType, bodyId, transientId );
		}

		return builder.Add( FFBModuleRegistry.AdaptiveSmootherType, detailGainId,
			( "Amount", src.F( "RacingWheelMultiOutputSmoothing" ) ) );
	}

	// ---- effect tail + generators (baked from source) ---------------------------------------------------

	private static string AppendEffectTail( Builder builder, string algoId, SettingsSource src )
	{
		var understeer = builder.Add( FFBModuleRegistry.UndersteerForceType, algoId,
			( "Enabled", src.B( "SteeringEffectsUndersteerEnabled" ) ),
			( "Direction", src.E( "SteeringEffectsUndersteerWheelConstantForceDirection" ) ),
			( "Strength", src.F( "SteeringEffectsUndersteerWheelConstantForceStrength" ) ),
			( "Curve", src.F( "SteeringEffectsUndersteerWheelConstantForceCurve" ) ) );

		var oversteer = builder.Add( FFBModuleRegistry.OversteerForceType, understeer,
			( "Enabled", src.B( "SteeringEffectsOversteerEnabled" ) ),
			( "Direction", src.E( "SteeringEffectsOversteerWheelConstantForceDirection" ) ),
			( "Strength", src.F( "SteeringEffectsOversteerWheelConstantForceStrength" ) ),
			( "Curve", src.F( "SteeringEffectsOversteerWheelConstantForceCurve" ) ) );

		var seatOfPants = builder.Add( FFBModuleRegistry.SeatOfPantsForceType, oversteer,
			( "Enabled", src.B( "SteeringEffectsSeatOfPantsEnabled" ) ),
			( "Direction", src.E( "SteeringEffectsSeatOfPantsWheelConstantForceDirection" ) ),
			( "Strength", src.F( "SteeringEffectsSeatOfPantsWheelConstantForceStrength" ) ),
			( "Curve", src.F( "SteeringEffectsSeatOfPantsWheelConstantForceCurve" ) ) );

		var crash = builder.Add( FFBModuleRegistry.CrashProtectionType, seatOfPants,
			( "LongGForce", src.F( "RacingWheelCrashProtectionLongitudalGForce" ) ),
			( "LatGForce", src.F( "RacingWheelCrashProtectionLateralGForce" ) ),
			( "Duration", src.F( "RacingWheelCrashProtectionDuration" ) ),
			( "ForceReduction", src.F( "RacingWheelCrashProtectionForceReduction" ) ) );

		var parked = builder.Add( FFBModuleRegistry.ParkedStrengthType, crash, ( "Strength", src.F( "RacingWheelParkedStrength" ) ) );

		// LFE is a source now — the old LFE mix stage becomes LFE source -> Gain (old strength) summed in by
		// a Mixer; strength 0 meant no effect, so the whole stage is omitted there
		var lfeStrength = src.F( "RacingWheelLFEStrength" );

		var lfe = parked;

		if ( lfeStrength > 0f )
		{
			var lfeGain = builder.Add( FFBModuleRegistry.GainType, FFBGraph.SourceLFEModuleId, ( "Gain", lfeStrength ) );

			lfe = builder.Add2( FFBModuleRegistry.AddType, parked, lfeGain );
		}

		var softLock = builder.Add( FFBModuleRegistry.SoftLockType, lfe, ( "Strength", src.F( "RacingWheelSoftLockStrength" ) ) );

		var friction = builder.Add( FFBModuleRegistry.FrictionType, softLock,
			( "RacingFriction", src.F( "RacingWheelFriction" ) ),
			( "ParkedFriction", src.F( "RacingWheelParkedFriction" ) ) );

		return builder.Add( FFBModuleRegistry.WheelCenteringType, friction,
			( "Strength", src.F( "RacingWheelWheelCenteringStrength" ) ),
			( "WhileRacing", src.B( "RacingWheelCenterWheelWhileRacing" ) ),
			( "WhileParked", src.B( "RacingWheelCenterWheelWhileParked" ) ) );
	}

	private static void AppendVibrationGenerators( Builder builder, SettingsSource src )
	{
		var s360 = FFBGraph.Source360ModuleId;

		builder.Add( FFBModuleRegistry.UndersteerVibrationType, s360,
			( "Enabled", src.B( "SteeringEffectsUndersteerEnabled" ) ),
			( "Pattern", src.E( "SteeringEffectsUndersteerWheelVibrationPattern" ) ),
			( "Strength", src.F( "SteeringEffectsUndersteerWheelVibrationStrength" ) ),
			( "MinimumFrequency", src.F( "SteeringEffectsUndersteerWheelVibrationMinimumFrequency" ) ),
			( "MaximumFrequency", src.F( "SteeringEffectsUndersteerWheelVibrationMaximumFrequency" ) ),
			( "Curve", src.F( "SteeringEffectsUndersteerWheelVibrationCurve" ) ) );

		builder.Add( FFBModuleRegistry.OversteerVibrationType, s360,
			( "Enabled", src.B( "SteeringEffectsOversteerEnabled" ) ),
			( "Pattern", src.E( "SteeringEffectsOversteerWheelVibrationPattern" ) ),
			( "Strength", src.F( "SteeringEffectsOversteerWheelVibrationStrength" ) ),
			( "MinimumFrequency", src.F( "SteeringEffectsOversteerWheelVibrationMinimumFrequency" ) ),
			( "MaximumFrequency", src.F( "SteeringEffectsOversteerWheelVibrationMaximumFrequency" ) ),
			( "Curve", src.F( "SteeringEffectsOversteerWheelVibrationCurve" ) ) );

		builder.Add( FFBModuleRegistry.SeatOfPantsVibrationType, s360,
			( "Enabled", src.B( "SteeringEffectsSeatOfPantsEnabled" ) ),
			( "Pattern", src.E( "SteeringEffectsSeatOfPantsWheelVibrationPattern" ) ),
			( "Strength", src.F( "SteeringEffectsSeatOfPantsWheelVibrationStrength" ) ),
			( "MinimumFrequency", src.F( "SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency" ) ),
			( "MaximumFrequency", src.F( "SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency" ) ),
			( "Curve", src.F( "SteeringEffectsSeatOfPantsWheelVibrationCurve" ) ) );

		builder.Add( FFBModuleRegistry.ShiftRPMVibrationType, s360, ( "Strength", src.F( "RacingWheelShiftRPMVibrateStrength" ) ) );
		builder.Add( FFBModuleRegistry.GearChangeVibrationType, s360, ( "Strength", src.F( "RacingWheelGearChangeVibrateStrength" ) ) );
		builder.Add( FFBModuleRegistry.ABSVibrationType, s360, ( "Strength", src.F( "RacingWheelABSVibrateStrength" ) ) );
	}

	/// <summary>Build one full built-in graph (sources + curb tap + algorithm + effect tail + generators + Output) with values from <paramref name="src"/>. Deterministic module ids come from the algorithm-based key prefix.</summary>
	private static FFBGraph BuildFullGraph( RacingWheel.Algorithm algorithm, RacingWheel.MultiFFBSourceOptions structureMultiSource, SettingsSource src )
	{
		var builder = new Builder( BuiltInGraphNameFor( algorithm ), $"BuiltIn.{algorithm}" );

		builder.AddSources( src );

		// curb protection sits early (pass-through) so its PrePass publishes the curb factor before the chain
		builder.Add( FFBModuleRegistry.CurbProtectionType, FFBGraph.Source360ModuleId,
			( "ShockVelocity", src.F( "RacingWheelCurbProtectionShockVelocity" ) ),
			( "Duration", src.F( "RacingWheelCurbProtectionDuration" ) ),
			( "ForceReduction", src.F( "RacingWheelCurbProtectionForceReduction" ) ) );

		var algoId = AppendAlgorithmModules( builder, algorithm, structureMultiSource, src );

		var centering = AppendEffectTail( builder, algoId, src );

		AppendVibrationGenerators( builder, src );

		builder.AddOutput( centering, src );

		return builder.Graph;
	}

	// ---- public builders --------------------------------------------------------------------------------

	/// <summary>
	/// Build a minimal parity graph (Source60, Source360, the algorithm's DSP core, Output) with all values
	/// baked from <paramref name="settings"/>. No effect tail or generators — under the neutral preview context
	/// those are inert anyway, so this isolates the DSP + Output math the milestone-1 parity harness verifies.
	/// </summary>
	public static FFBGraph BuildParityGraph( RacingWheel.Algorithm algorithm, RacingWheel.MultiFFBSourceOptions multiSource, Settings settings )
	{
		var src = new SettingsSource( settings, settings );

		var builder = new Builder( BuiltInGraphNameFor( algorithm ), $"Parity.{algorithm}.{multiSource}" );

		builder.AddSources( src );

		var lastId = AppendAlgorithmModules( builder, algorithm, CollapseMultiSource( multiSource ), src );

		builder.AddOutput( lastId, src );

		return builder.Graph;
	}

	/// <summary>
	/// Build the full set of named built-in graphs: one per old algorithm, each with the standard effect tail
	/// (in old pipeline order) and the six vibration generators. All values are baked from the live
	/// <paramref name="settings"/>; the Multi graph's source-stage structure uses the (collapsed) live Multi
	/// source mode.
	/// </summary>
	public static List<FFBGraph> CreateBuiltInGraphs( Settings settings )
	{
		var src = new SettingsSource( settings, settings );
		var structureMultiSource = CollapseMultiSource( settings.RacingWheelMultiFFBSourceSelection );

		var graphs = new List<FFBGraph>();

		foreach ( var algorithm in Enum.GetValues<RacingWheel.Algorithm>() )
		{
			graphs.Add( BuildFullGraph( algorithm, structureMultiSource, src ) );
		}

		return graphs;
	}

	/// <summary>
	/// Map an old settings source (the live <c>Settings</c> or one <c>ContextSettings</c>) into the per-context
	/// composite value dictionary for ALL built-in graphs. Structure uses the (collapsed) live Multi source mode
	/// so the module ids — and therefore the composite keys — match the baseline built-in graphs exactly.
	/// </summary>
	public static FFBGraphValues MapOldSettingsIntoGraphValues( object source, Settings live, RacingWheel.MultiFFBSourceOptions structureMultiSource )
	{
		var src = new SettingsSource( source, live );

		var values = new FFBGraphValues();

		foreach ( var algorithm in Enum.GetValues<RacingWheel.Algorithm>() )
		{
			var graph = BuildFullGraph( algorithm, structureMultiSource, src );

			foreach ( var module in graph.Modules )
			{
				foreach ( var pair in module.SettingValues )
				{
					values[ FFBGraphValues.ComposeKey( module.ModuleId, pair.Key ) ] = pair.Value;
				}
			}
		}

		return values;
	}
}
