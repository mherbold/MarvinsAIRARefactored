
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

	// The single built-in vibration graph (the six old generator effects). Graph names are stored keys, not
	// localized — same convention as the built-in FFB graph names below.
	public const string BuiltInVibrationGraphName = "Default";

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

		// Sources are optional per-graph now — only the two torque sources are always present (every algorithm
		// uses them and the prediction settings live on the 60 Hz source); the other sources are added by the
		// stage that needs them (see AddSource).
		public void AddSources( SettingsSource src )
		{
			var source60 = new FFBModuleData( FFBGraph.Source60ModuleId, FFBModuleRegistry.Source60HzType );

			source60.SettingValues[ "PredictionMode" ] = src.E( "RacingWheelPredictionMode" );
			source60.SettingValues[ "PredictionBlend" ] = src.F( "RacingWheelPredictionBlend" );

			Graph.Modules.Add( source60 );

			Graph.Modules.Add( new FFBModuleData( FFBGraph.Source360ModuleId, FFBModuleRegistry.Source360HzType ) );
		}

		/// <summary>Add a source module under its canonical id (skipped when already present — one per type).</summary>
		public string AddSource( string moduleId, string moduleType, params (string key, float value)[] values )
		{
			if ( !Graph.Modules.Any( module => module.ModuleId == moduleId ) )
			{
				var data = new FFBModuleData( moduleId, moduleType );

				foreach ( var (key, value) in values )
				{
					data.SettingValues[ key ] = value;
				}

				Graph.Modules.Add( data );
			}

			return moduleId;
		}

		public void AddOutput( string inputA, SettingsSource src )
		{
			var oldMinimum = src.F( "RacingWheelOutputMinimum" );   // fraction of full scale (0..0.5)
			var oldMaximum = src.F( "RacingWheelOutputMaximum" );   // fraction of full scale (0.2..1)

			// The old output minimum forced small signals up to a hard floor; its modern replacement is the
			// torque dither module — instead of a floor, an alternating dither below the old floor level keeps
			// the wheel's mechanism live (the dither strength is capped at its knob range; the old floor could
			// legally be far larger than any sane dither).
			var dither = inputA;

			if ( oldMinimum > 0f )
			{
				dither = Add( FFBModuleRegistry.TorqueDitherType, inputA,
					( "Strength", MathF.Min( oldMinimum, 0.05f ) ),
					( "Threshold", oldMinimum ) );
			}

			var output = new FFBModuleData( FFBGraph.OutputModuleId, FFBModuleRegistry.OutputType )
			{
				InputAModuleId = dither
			};

			output.SettingValues[ "Curve" ] = src.F( "RacingWheelOutputCurve" );

			// The old hard output maximum becomes the Output's soft limiter — deliberately soft, no attempt to
			// emulate the hard ceiling: threshold at the old maximum with the default knee/ratio. When no old
			// maximum was active the soft limiter simply carries the old enable flag (default curve approximation).
			var oldMaximumActive = oldMaximum < 1f;

			output.SettingValues[ "SoftLimiter" ] = ( oldMaximumActive || ( src.B( "RacingWheelEnableSoftLimiter" ) != 0f ) ) ? 1f : 0f;

			if ( oldMaximumActive )
			{
				output.SettingValues[ "Threshold" ] = Math.Clamp( oldMaximum, 0f, 1f );
			}

			Graph.Modules.Add( output );
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

		// the old parked strength is a SpeedGain configuration: its ramp was Lerp(1, Strength, saturate(1 − v/2.2352)),
		// which is exactly GainAtMin = Strength at 0 m/s rising to GainAtMax = 1 at 2.2352 m/s (5 mph)
		var parked = builder.Add( FFBModuleRegistry.SpeedGainType, crash,
			( "MinSpeed", 0f ),
			( "MaxSpeed", ParkedRampTopSpeedMS ),
			( "GainAtMin", src.F( "RacingWheelParkedStrength" ) ),
			( "GainAtMax", 1f ) );

		// LFE is a source now — the old LFE mix stage becomes LFE source -> Gain (old strength) summed in by
		// a Mixer; strength 0 meant no effect, so the whole stage (source included) is omitted there
		var lfeStrength = src.F( "RacingWheelLFEStrength" );

		var lfe = parked;

		if ( lfeStrength > 0f )
		{
			var lfeSource = builder.AddSource( FFBGraph.SourceLFEModuleId, FFBModuleRegistry.SourceLFEType );

			var lfeGain = builder.Add( FFBModuleRegistry.GainType, lfeSource, ( "Gain", lfeStrength ) );

			lfe = builder.Add2( FFBModuleRegistry.AddType, parked, lfeGain );
		}

		// Soft lock emits its force on its own branch now: source (strength knob) summed in by an Add.
		// Strength 0 meant no effect, so the stage is omitted there.
		var softLockStrength = src.F( "RacingWheelSoftLockStrength" );

		var softLock = lfe;

		if ( softLockStrength > 0f )
		{
			var softLockSource = builder.AddSource( FFBGraph.SourceSoftLockModuleId, FFBModuleRegistry.SourceSoftLockType,
				( "Strength", softLockStrength ) );

			softLock = builder.Add2( FFBModuleRegistry.AddType, lfe, softLockSource );
		}

		// The old fixed-function friction becomes a composition: wheel-velocity source scaled by a SpeedGain
		// whose two gains reproduce the racing/parked coefficient crossfade (damping is linear, so blending
		// the OUTPUTS of two dampers equals blending their coefficients), summed into the chain. Both
		// strengths 0 (the defaults) meant no effect, so the whole stage is omitted there — matching the LFE
		// stage's pattern above.
		var racingFriction = src.F( "RacingWheelFriction" );
		var parkedFriction = src.F( "RacingWheelParkedFriction" );

		var friction = softLock;

		if ( ( racingFriction > 0f ) || ( parkedFriction > 0f ) )
		{
			var wheelVelocitySource = builder.AddSource( FFBGraph.SourceWheelVelocityModuleId, FFBModuleRegistry.SourceWheelVelocityType );

			var frictionGain = builder.Add( FFBModuleRegistry.SpeedGainType, wheelVelocitySource,
				( "MinSpeed", 0f ),
				( "MaxSpeed", ParkedRampTopSpeedMS ),
				( "GainAtMin", parkedFriction * FrictionUnitConversion ),
				( "GainAtMax", racingFriction * FrictionUnitConversion ) );

			friction = builder.Add2( FFBModuleRegistry.AddType, softLock, frictionGain );
		}

		// Wheel centering emits its force on its own branch now (the while-racing/while-parked toggles are gone) —
		// the old toggle gating was Lerp(racing, parked, parkedFactor), which is exactly a SpeedGain over the same
		// 0–5 mph ramp with the toggles as its two gains (omitted when both toggles agree). Strength 0 or both
		// toggles off meant no effect, so the whole stage is omitted there — same pattern as the LFE stage above.
		var centeringStrength = src.F( "RacingWheelWheelCenteringStrength" );
		var centerWhileRacing = src.B( "RacingWheelCenterWheelWhileRacing" );
		var centerWhileParked = src.B( "RacingWheelCenterWheelWhileParked" );

		var centering = friction;

		if ( ( centeringStrength > 0f ) && ( ( centerWhileRacing != 0f ) || ( centerWhileParked != 0f ) ) )
		{
			var centeringForce = builder.AddSource( FFBGraph.SourceWheelCenteringModuleId, FFBModuleRegistry.SourceWheelCenteringType,
				( "Strength", centeringStrength ) );

			if ( centerWhileRacing != centerWhileParked )
			{
				centeringForce = builder.Add( FFBModuleRegistry.SpeedGainType, centeringForce,
					( "MinSpeed", 0f ),
					( "MaxSpeed", ParkedRampTopSpeedMS ),
					( "GainAtMin", centerWhileParked ),
					( "GainAtMax", centerWhileRacing ) );
			}

			centering = builder.Add2( FFBModuleRegistry.AddType, friction, centeringForce );
		}

		return centering;
	}

	// Generators have no signal inputs and live in the standalone vibration graphs (schema v23) — no sources
	// exist there, so their input references are left empty (the engine's fallback resolution tolerates that).
	private static void AppendVibrationGenerators( Builder builder, SettingsSource src )
	{
		var noInput = string.Empty;

		builder.Add( FFBModuleRegistry.UndersteerVibrationType, noInput,
			( "Enabled", src.B( "SteeringEffectsUndersteerEnabled" ) ),
			( "Pattern", src.E( "SteeringEffectsUndersteerWheelVibrationPattern" ) ),
			( "Strength", src.F( "SteeringEffectsUndersteerWheelVibrationStrength" ) ),
			( "MinimumFrequency", src.F( "SteeringEffectsUndersteerWheelVibrationMinimumFrequency" ) ),
			( "MaximumFrequency", src.F( "SteeringEffectsUndersteerWheelVibrationMaximumFrequency" ) ),
			( "Curve", src.F( "SteeringEffectsUndersteerWheelVibrationCurve" ) ) );

		builder.Add( FFBModuleRegistry.OversteerVibrationType, noInput,
			( "Enabled", src.B( "SteeringEffectsOversteerEnabled" ) ),
			( "Pattern", src.E( "SteeringEffectsOversteerWheelVibrationPattern" ) ),
			( "Strength", src.F( "SteeringEffectsOversteerWheelVibrationStrength" ) ),
			( "MinimumFrequency", src.F( "SteeringEffectsOversteerWheelVibrationMinimumFrequency" ) ),
			( "MaximumFrequency", src.F( "SteeringEffectsOversteerWheelVibrationMaximumFrequency" ) ),
			( "Curve", src.F( "SteeringEffectsOversteerWheelVibrationCurve" ) ) );

		builder.Add( FFBModuleRegistry.SeatOfPantsVibrationType, noInput,
			( "Enabled", src.B( "SteeringEffectsSeatOfPantsEnabled" ) ),
			( "Pattern", src.E( "SteeringEffectsSeatOfPantsWheelVibrationPattern" ) ),
			( "Strength", src.F( "SteeringEffectsSeatOfPantsWheelVibrationStrength" ) ),
			( "MinimumFrequency", src.F( "SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency" ) ),
			( "MaximumFrequency", src.F( "SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency" ) ),
			( "Curve", src.F( "SteeringEffectsSeatOfPantsWheelVibrationCurve" ) ) );

		builder.Add( FFBModuleRegistry.ShiftRPMVibrationType, noInput, ( "Strength", src.F( "RacingWheelShiftRPMVibrateStrength" ) ) );
		builder.Add( FFBModuleRegistry.GearChangeVibrationType, noInput, ( "Strength", src.F( "RacingWheelGearChangeVibrateStrength" ) ) );
		builder.Add( FFBModuleRegistry.ABSVibrationType, noInput, ( "Strength", src.F( "RacingWheelABSVibrateStrength" ) ) );
	}

	/// <summary>Build one full built-in graph (sources + curb tap + algorithm + effect tail + Output) with values from <paramref name="src"/>. Deterministic module ids come from the algorithm-based key prefix. The vibration generators live in the standalone built-in vibration graph (schema v23) — see <see cref="BuildVibrationGraph"/>.</summary>
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

		builder.AddOutput( centering, src );

		return builder.Graph;
	}

	/// <summary>Build the built-in vibration graph (the six generator modules, no sources or Output) with values from <paramref name="src"/>. Deterministic module ids come from the fixed key prefix.</summary>
	private static FFBGraph BuildVibrationGraph( SettingsSource src )
	{
		var builder = new Builder( BuiltInVibrationGraphName, "BuiltIn.Vibration" );

		AppendVibrationGenerators( builder, src );

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
	/// (in old pipeline order). All values are baked from the live <paramref name="settings"/>; the Multi graph's
	/// source-stage structure uses the (collapsed) live Multi source mode.
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

	/// <summary>Build the set of built-in vibration graphs (currently the single default) with values baked from the live <paramref name="settings"/>.</summary>
	public static List<FFBGraph> CreateBuiltInVibrationGraphs( Settings settings )
	{
		var src = new SettingsSource( settings, settings );

		return [ BuildVibrationGraph( src ) ];
	}

	/// <summary>
	/// Remove every generator module from <paramref name="graph"/> and return them (the schema v23 split moves
	/// them into a standalone vibration graph, ids preserved so per-context values keep resolving). Generators
	/// can never feed a signal input, so no consumer repointing is needed.
	/// </summary>
	public static List<FFBModuleData> ExtractGeneratorModules( FFBGraph graph )
	{
		var generatorModules = graph.Modules.Where( module => FFBModuleRegistry.TryGet( module.ModuleType )?.IsGenerator == true ).ToList();

		foreach ( var module in generatorModules )
		{
			graph.Modules.Remove( module );
		}

		return generatorModules;
	}

	/// <summary>
	/// Map an old settings source (the live <c>Settings</c> or one <c>ContextSettings</c>) into the per-context
	/// composite value dictionary for ALL built-in graphs. Structure uses the (collapsed) live Multi source mode
	/// so the module ids — and therefore the composite keys — match the baseline built-in graphs exactly.
	/// </summary>
	// Top of the old parked-strength velocity ramp (5 mph — see RacingWheel's parkedFactor). Used to convert
	// parked strength into its equivalent SpeedGain configuration.
	private const float ParkedRampTopSpeedMS = 2.2352f;

	// Old friction measured wheel velocity in full-axis-range fractions/s (DirectInput); the wheel-velocity
	// source emits revolutions/s. Assuming the common 900° rotation range (2.5 revolutions across the 2-unit
	// axis), 1 rev/s = 0.8 axis units/s — so an old friction strength converts by this factor.
	private const float FrictionUnitConversion = 0.8f;

	/// <summary>
	/// Repair retired module types in a stored (user-created) graph. The curve and soft limiter moved back into
	/// the Output module as settings (schema v17) — those are spliced out, with consumers repointed to the removed
	/// module's own input A so the signal path stays intact (falling back to the 360 Hz source on a dangling
	/// reference). The parked strength module was superseded by SpeedGain (schema v18) — those convert in place,
	/// keeping their wiring and node position. The friction module was superseded by the wheel-velocity source
	/// composition (schema v19) — those are spliced out (recompose as source → SpeedGain → Add). Wheel centering
	/// became an inputless force emitter (schema v20) and then a proper source, alongside soft lock (schema v21) —
	/// old in-chain instances are re-wired onto their own branch and renamed to the source type keys. The Nm-domain
	/// output shapers retired at schema v22: Minimum converts in place to TorqueDither and Maximum is spliced out
	/// in favor of the Output module's soft limiter.
	/// Built-in graphs are regenerated wholesale by the schema upgrade and never pass through here.
	/// </summary>
	public static void RemoveRetiredModuleTypes( FFBGraph graph )
	{
		// SoftLock -> SourceSoftLock (schema v21): the module emits ONLY its opposing force now (no signal input).
		// An old in-chain instance is re-wired as a branch: consumers repoint to a new Add that sums the instance's
		// old input path with the soft lock force. A disabled instance stays disabled — the engine mutes disabled
		// sources to 0, so the Add contributes nothing, exactly like the old disabled passthrough. New modules are
		// inserted directly after the instance so the list stays in dependency order.
		foreach ( var softLockModule in graph.Modules.Where( module => module.ModuleType == "SoftLock" ).ToList() )
		{
			softLockModule.ModuleType = FFBModuleRegistry.SourceSoftLockType;

			var oldInputAModuleId = softLockModule.InputAModuleId;

			if ( string.IsNullOrEmpty( oldInputAModuleId ) || !graph.Modules.Any( module => module.ModuleId == oldInputAModuleId ) )
			{
				oldInputAModuleId = FFBGraph.Source360ModuleId;
			}

			var add = new FFBModuleData( Guid.NewGuid().ToString( "N" ), FFBModuleRegistry.AddType )
			{
				InputAModuleId = oldInputAModuleId,
				InputBModuleId = softLockModule.ModuleId,
				NodeX = softLockModule.NodeX + FFBGraphTopology.NodeWidth + FFBGraphTopology.HorizontalGap,
				NodeY = softLockModule.NodeY
			};

			graph.Modules.Insert( graph.Modules.IndexOf( softLockModule ) + 1, add );

			foreach ( var module in graph.Modules )
			{
				if ( module == add )
				{
					continue;
				}

				if ( module.InputAModuleId == softLockModule.ModuleId )
				{
					module.InputAModuleId = add.ModuleId;
				}

				if ( module.InputBModuleId == softLockModule.ModuleId )
				{
					module.InputBModuleId = add.ModuleId;
				}
			}

			softLockModule.InputAModuleId = string.Empty;
			softLockModule.InputBModuleId = string.Empty;
		}

		// WheelCentering -> SourceWheelCentering (schema v20/v21): the module emits ONLY the centering force (no
		// signal input) and the while-racing/while-parked toggles are gone. An old in-chain instance is re-wired
		// as a branch: consumers repoint to a new Add that sums the instance's old input path with the centering
		// force, routed through a SpeedGain when the old toggles gated it by speed (Lerp(racing, parked,
		// parkedFactor) == SpeedGain over the same 0–5 mph ramp). Both toggles off meant the instance contributed
		// nothing — it is spliced out. New modules are inserted directly after the instance so the list stays in
		// dependency order. An instance already converted by the v20 upgrade (no input wiring) only gets the
		// type rename.
		foreach ( var centeringModule in graph.Modules.Where( module => module.ModuleType == "WheelCentering" ).ToList() )
		{
			centeringModule.ModuleType = FFBModuleRegistry.SourceWheelCenteringType;

			if ( string.IsNullOrEmpty( centeringModule.InputAModuleId ) )
			{
				continue;   // already branch-wired by the v20 upgrade
			}

			// old toggle defaults were racing = off, parked = on (missing key ⇒ descriptor default)
			var whileRacing = centeringModule.SettingValues.TryGetValue( "WhileRacing", out var whileRacingValue ) && ( whileRacingValue != 0f );
			var whileParked = !centeringModule.SettingValues.TryGetValue( "WhileParked", out var whileParkedValue ) || ( whileParkedValue != 0f );

			centeringModule.SettingValues.Remove( "WhileRacing" );
			centeringModule.SettingValues.Remove( "WhileParked" );

			var oldInputAModuleId = centeringModule.InputAModuleId;

			if ( string.IsNullOrEmpty( oldInputAModuleId ) || !graph.Modules.Any( module => module.ModuleId == oldInputAModuleId ) )
			{
				oldInputAModuleId = FFBGraph.Source360ModuleId;
			}

			if ( !whileRacing && !whileParked )
			{
				graph.Modules.Remove( centeringModule );

				foreach ( var module in graph.Modules )
				{
					if ( module.InputAModuleId == centeringModule.ModuleId )
					{
						module.InputAModuleId = oldInputAModuleId;
					}

					if ( module.InputBModuleId == centeringModule.ModuleId )
					{
						module.InputBModuleId = oldInputAModuleId;
					}
				}

				continue;
			}

			var insertIndex = graph.Modules.IndexOf( centeringModule ) + 1;

			var branchTailModuleId = centeringModule.ModuleId;

			if ( whileRacing != whileParked )
			{
				var speedGain = new FFBModuleData( Guid.NewGuid().ToString( "N" ), FFBModuleRegistry.SpeedGainType )
				{
					InputAModuleId = centeringModule.ModuleId,
					InputBModuleId = FFBGraph.Source360ModuleId,
					NodeX = centeringModule.NodeX,
					NodeY = centeringModule.NodeY + FFBGraphTopology.NodeHeight + FFBGraphTopology.VerticalGap
				};

				speedGain.SettingValues[ "MinSpeed" ] = 0f;
				speedGain.SettingValues[ "MaxSpeed" ] = ParkedRampTopSpeedMS;
				speedGain.SettingValues[ "GainAtMin" ] = whileParked ? 1f : 0f;
				speedGain.SettingValues[ "GainAtMax" ] = whileRacing ? 1f : 0f;

				graph.Modules.Insert( insertIndex++, speedGain );

				branchTailModuleId = speedGain.ModuleId;
			}

			var add = new FFBModuleData( Guid.NewGuid().ToString( "N" ), FFBModuleRegistry.AddType )
			{
				InputAModuleId = oldInputAModuleId,
				InputBModuleId = branchTailModuleId,
				NodeX = centeringModule.NodeX + FFBGraphTopology.NodeWidth + FFBGraphTopology.HorizontalGap,
				NodeY = centeringModule.NodeY
			};

			graph.Modules.Insert( insertIndex, add );

			foreach ( var module in graph.Modules )
			{
				if ( ( module == add ) || ( module.ModuleId == branchTailModuleId ) )
				{
					continue;
				}

				if ( module.InputAModuleId == centeringModule.ModuleId )
				{
					module.InputAModuleId = add.ModuleId;
				}

				if ( module.InputBModuleId == centeringModule.ModuleId )
				{
					module.InputBModuleId = add.ModuleId;
				}
			}

			// the emitter keeps no wiring of its own (it has no signal inputs anymore)
			centeringModule.InputAModuleId = string.Empty;
			centeringModule.InputBModuleId = string.Empty;
		}

		// ParkedStrength -> SpeedGain conversion (exact equivalent, see ParkedRampTopSpeedMS)
		foreach ( var module in graph.Modules )
		{
			if ( module.ModuleType == "ParkedStrength" )
			{
				var strength = module.SettingValues.TryGetValue( "Strength", out var value ) ? value : 0.1f;

				module.ModuleType = FFBModuleRegistry.SpeedGainType;

				module.SettingValues.Remove( "Strength" );

				module.SettingValues[ "MinSpeed" ] = 0f;
				module.SettingValues[ "MaxSpeed" ] = ParkedRampTopSpeedMS;
				module.SettingValues[ "GainAtMin" ] = strength;
				module.SettingValues[ "GainAtMax" ] = 1f;
			}
		}

		// Minimum -> TorqueDither conversion (schema v22): same wiring and position, dither defaults — the old
		// knob was in Nm and cannot be converted back to a fraction without the recording-time max force.
		foreach ( var module in graph.Modules )
		{
			if ( module.ModuleType == "Minimum" )
			{
				module.ModuleType = FFBModuleRegistry.TorqueDitherType;

				module.SettingValues.Remove( "Minimum" );
			}
		}

		// Maximum retired (schema v22): its job moves to the Output module's soft limiter (deliberately soft —
		// no hard-ceiling emulation), so an enabled Maximum turns the soft limiter on before the splice below.
		if ( graph.Modules.Any( module => ( module.ModuleType == "Maximum" )
			&& ( !module.SettingValues.TryGetValue( "Enabled", out var enabledValue ) || ( enabledValue != 0f ) ) ) )
		{
			var output = graph.Modules.FirstOrDefault( module => module.ModuleType == FFBModuleRegistry.OutputType );

			if ( output != null )
			{
				output.SettingValues[ "SoftLimiter" ] = 1f;
			}
		}

		string[] retiredModuleTypes = [ "Curve", "SoftLimiter", "Friction", "Maximum" ];

		foreach ( var retiredModule in graph.Modules.Where( module => retiredModuleTypes.Contains( module.ModuleType ) ).ToList() )
		{
			var replacementModuleId = retiredModule.InputAModuleId;

			if ( string.IsNullOrEmpty( replacementModuleId ) || !graph.Modules.Any( module => module.ModuleId == replacementModuleId ) )
			{
				replacementModuleId = FFBGraph.Source360ModuleId;
			}

			graph.Modules.Remove( retiredModule );

			foreach ( var module in graph.Modules )
			{
				if ( module.InputAModuleId == retiredModule.ModuleId )
				{
					module.InputAModuleId = replacementModuleId;
				}

				if ( module.InputBModuleId == retiredModule.ModuleId )
				{
					module.InputBModuleId = replacementModuleId;
				}
			}
		}
	}

	public static FFBGraphValues MapOldSettingsIntoGraphValues( object source, Settings live, RacingWheel.MultiFFBSourceOptions structureMultiSource )
	{
		var src = new SettingsSource( source, live );

		var values = new FFBGraphValues();

		var graphs = new List<FFBGraph>();

		foreach ( var algorithm in Enum.GetValues<RacingWheel.Algorithm>() )
		{
			graphs.Add( BuildFullGraph( algorithm, structureMultiSource, src ) );
		}

		graphs.Add( BuildVibrationGraph( src ) );

		foreach ( var graph in graphs )
		{
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
