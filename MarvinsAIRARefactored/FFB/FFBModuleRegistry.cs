
using MarvinsAIRARefactored.FFB.Modules;

using F = MarvinsAIRARefactored.FFB.FFBValueFormats;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Static catalog mapping a stable module type key to its descriptor (settings, arity, family flags) and a
/// factory for a fresh runtime instance. This is the single place that knows every module type; the engine,
/// migration, and (later) the editor UI all resolve modules through here. Adding a module = adding one entry.
/// </summary>
public static class FFBModuleRegistry
{
	// ---- stable type keys (serialized in FFBModuleData.ModuleType) --------------------------------------

	public const string Source60HzType = "Source60Hz";
	public const string Source360HzType = "Source360Hz";
	public const string SourceLFEType = "SourceLFE";
	public const string SourceWheelVelocityType = "SourceWheelVelocity";
	public const string SourceSoftLockType = "SourceSoftLock";
	public const string SourceWheelCenteringType = "SourceWheelCentering";
	public const string OutputType = "Output";

	public const string LowPassFilterType = "LowPassFilter";
	public const string HighPassFilterType = "HighPassFilter";
	public const string GainType = "Gain";
	public const string AddType = "Add";
	public const string SubtractType = "Subtract";
	public const string BlendType = "Blend";
	public const string SlewLimiterType = "SlewLimiter";
	public const string SlewCompressorType = "SlewCompressor";
	public const string CompressorType = "Compressor";
	public const string TransientEnhancerType = "TransientEnhancer";
	public const string AdaptiveSmootherType = "AdaptiveSmoother";
	public const string AdaptiveBlendType = "AdaptiveBlend";

	public const string CrashProtectionType = "CrashProtection";
	public const string CurbProtectionType = "CurbProtection";
	public const string UndersteerForceType = "UndersteerForce";
	public const string OversteerForceType = "OversteerForce";
	public const string SeatOfPantsForceType = "SeatOfPantsForce";

	public const string UndersteerVibrationType = "UndersteerVibration";
	public const string OversteerVibrationType = "OversteerVibration";
	public const string SeatOfPantsVibrationType = "SeatOfPantsVibration";
	public const string ShiftRPMVibrationType = "ShiftRPMVibration";
	public const string GearChangeVibrationType = "GearChangeVibration";
	public const string ABSVibrationType = "ABSVibration";

	public const string SpeedGainType = "SpeedGain";
	public const string RoadTextureType = "RoadTexture";
	public const string SlipTextureType = "SlipTexture";
	public const string TorqueDitherType = "TorqueDither";

	// Choice option key tables. Declared BEFORE _descriptors so their static initializers run first — the
	// _descriptors initializer calls BuildDescriptors(), which reads these arrays.
	// Choice option localization keys. Reuse the existing (already-translated) keys where they exist so choices
	// are localized for free; new option sets use short words that read fine via the humanized fallback.
	private static readonly string[] PredictionModeChoices = [ "Disabled", "PredictK1", "PredictK2" ];
	private static readonly string[] FilterSlopeChoices = [ "OnePole", "TwoPole" ];
	private static readonly string[] ConstantForceDirectionChoices = [ "None", "DecreaseForce", "IncreaseForce" ];
	private static readonly string[] VibrationPatternChoices = [ "None", "SineWave", "SquareWave", "TriangleWave", "SawtoothWaveIn", "SawtoothWaveOut" ];

	// declaration order below is meaningful: it is the display order of the editor's add-module dropdown
	// (within each category) — hand-curated, not alphabetical
	private static readonly List<FFBModuleDescriptor> _orderedDescriptors = BuildDescriptors();

	private static readonly Dictionary<string, FFBModuleDescriptor> _descriptors = _orderedDescriptors.ToDictionary( descriptor => descriptor.TypeKey );

	public static FFBModuleDescriptor? TryGet( string typeKey )
	{
		return _descriptors.TryGetValue( typeKey, out var descriptor ) ? descriptor : null;
	}

	public static FFBModuleDescriptor Get( string typeKey )
	{
		return _descriptors[ typeKey ];
	}

	/// <summary>Every module descriptor, in declaration order (= the add-dropdown's curated display order).</summary>
	public static IReadOnlyList<FFBModuleDescriptor> All => _orderedDescriptors;

	// ---- descriptor construction helpers ----------------------------------------------------------------

	private static FFBSettingDescriptor Knob( string key, float min, float max, float defaultValue, float clickStepSize, Func<FFBFormatContext, string>? format = null, string? localizationKey = null, bool showCurve = false, bool breakRow = false )
	{
		return new FFBSettingDescriptor
		{
			Key = key,
			LocalizationKey = localizationKey ?? ( "FFBSetting" + key ),
			Type = FFBSettingType.Knob,
			Min = min,
			Max = max,
			DefaultValue = defaultValue,
			ClickStepSize = clickStepSize,
			DragStepSize = ( max - min ) / 5760f,
			FormatValue = format,
			ShowCurve = showCurve,
			BreakRow = breakRow
		};
	}

	private static FFBSettingDescriptor Switch( string key, bool defaultOn, string? localizationKey = null )
	{
		return new FFBSettingDescriptor
		{
			Key = key,
			LocalizationKey = localizationKey ?? ( "FFBSetting" + key ),
			Type = FFBSettingType.Switch,
			Min = 0f,
			Max = 1f,
			DefaultValue = defaultOn ? 1f : 0f,
			ClickStepSize = 1f,
			DragStepSize = 1f
		};
	}

	private static FFBSettingDescriptor Choice( string key, float defaultIndex, string[] choiceLocalizationKeys, string? localizationKey = null )
	{
		return new FFBSettingDescriptor
		{
			Key = key,
			LocalizationKey = localizationKey ?? ( "FFBSetting" + key ),
			Type = FFBSettingType.Choice,
			Min = 0f,
			Max = choiceLocalizationKeys.Length - 1,
			DefaultValue = defaultIndex,
			ClickStepSize = 1f,
			DragStepSize = 1f,
			ChoiceLocalizationKeys = choiceLocalizationKeys
		};
	}

	private static FFBModuleDescriptor Descriptor( string typeKey, int signalInputCount, Func<FFBModule> createRuntime, FFBSettingDescriptor[] settings,
		bool isGenerator = false, bool isSource = false, bool isOutput = false, bool canTest = false, string? inputA = null, string? inputB = null,
		FFBModuleCategory category = FFBModuleCategory.GenericDSP )
	{
		// sources and the Output module derive their category from their family flags; the main-chain modules
		// split into generic DSP / mixers / effects and the generators into steering / other vibrations via the
		// category argument (a generator that forgets to declare one lands in the "other" group)
		if ( isSource )
		{
			category = FFBModuleCategory.Source;
		}
		else if ( isGenerator && ( category == FFBModuleCategory.GenericDSP ) )
		{
			category = FFBModuleCategory.OtherVibration;
		}
		else if ( isOutput )
		{
			category = FFBModuleCategory.Output;
		}

		return new FFBModuleDescriptor
		{
			TypeKey = typeKey,
			LocalizationKey = "FFBModule" + typeKey,
			SignalInputCount = signalInputCount,
			InputALocalizationKey = inputA,
			InputBLocalizationKey = inputB,
			Category = category,
			IsGenerator = isGenerator,
			IsSource = isSource,
			IsOutput = isOutput,
			CanTest = canTest,
			Settings = settings,
			CreateRuntime = createRuntime
		};
	}

	private static List<FFBModuleDescriptor> BuildDescriptors()
	{
		return
		[
			// ---- sources (6) ----
			Descriptor( Source60HzType, 0, () => new Source60HzModule(), isSource: true, settings:
			[
				Choice( "PredictionMode", 1f, PredictionModeChoices ),
				Knob( "PredictionBlend", 0f, 1f, 0.3f, 0.05f, F.Percent() )
			] ),

			Descriptor( Source360HzType, 0, () => new Source360HzModule(), isSource: true, settings: [] ),

			Descriptor( SourceLFEType, 0, () => new SourceLFEModule(), isSource: true, settings: [] ),

			Descriptor( SourceSoftLockType, 0, () => new SourceSoftLockModule(), isSource: true, settings:
			[
				Knob( "Strength", 0f, 1f, 0.25f, 0.01f, F.WithOff( F.Percent() ) )
			] ),

			Descriptor( SourceWheelVelocityType, 0, () => new SourceWheelVelocityModule(), isSource: true, settings: [] ),

			Descriptor( SourceWheelCenteringType, 0, () => new SourceWheelCenteringModule(), isSource: true, settings:
			[
				Knob( "Strength", 0f, 1f, 0.75f, 0.01f, F.WithOff( F.Percent() ) )
			] ),

			// ---- generic DSP (8) ----
			Descriptor( GainType, 1, () => new GainModule(), settings:
			[
				Knob( "Gain", -5f, 5f, 1f, 0.05f, F.Multiplier( 2 ) )
			] ),

			Descriptor( CompressorType, 1, () => new CompressorModule(), settings:
			[
				Knob( "Threshold", 0f, 50f, 30f, 1f, F.Number( 1, "TorqueUnits" ) ),
				Knob( "Knee", 0f, 20f, 5f, 0.5f, F.Number( 1, "TorqueUnits" ) ),
				Knob( "Ratio", 1f, 20f, 4f, 0.5f, F.Ratio )
			] ),

			Descriptor( HighPassFilterType, 1, () => new HighPassFilterModule(), settings:
			[
				Choice( "Slope", 0f, FilterSlopeChoices ),
				Knob( "Cutoff", 0f, 180f, 0f, 1f, F.WithOff( F.Number( 1, "HertzUnits" ) ) )
			] ),

			Descriptor( LowPassFilterType, 1, () => new LowPassFilterModule(), settings:
			[
				Choice( "Slope", 0f, FilterSlopeChoices ),
				Knob( "Cutoff", 0f, 180f, 0f, 1f, F.Number( 1, "HertzUnits" ) )
			] ),

			Descriptor( SlewCompressorType, 1, () => new SlewCompressorModule(), settings:
			[
				Knob( "Threshold", 0f, 1000f, 75f, 5f, F.Number( 0, "DeltaLimitUnits" ) ),
				Knob( "Knee", 0f, 200f, 30f, 5f, F.Number( 0, "DeltaLimitUnits" ) ),
				Knob( "Ratio", 1f, 20f, 3f, 0.5f, F.Ratio ),
				Switch( "PeakMode", false )
			] ),

			Descriptor( SlewLimiterType, 1, () => new SlewLimiterModule(), settings:
			[
				Knob( "Limit", 1f, 1500f, 360f, 10f, F.Number( 0, "DeltaLimitUnits" ) )
			] ),

			Descriptor( AdaptiveSmootherType, 1, () => new AdaptiveSmootherModule(), settings:
			[
				Knob( "Amount", 0f, 1f, 0f, 0.01f, F.SmootherAmount )
			] ),

			Descriptor( TransientEnhancerType, 1, () => new TransientEnhancerModule(), settings:
			[
				Knob( "Cutoff", 0f, 180f, 7.2f, 1f, F.Number( 1, "HertzUnits" ) ),
				Knob( "Gain", 0f, 5f, 1f, 0.05f, F.Multiplier( 2 ) )
			] ),

			// ---- mixers (4) ----
			Descriptor( AddType, 2, () => new AddModule(), category: FFBModuleCategory.Mixer, settings: [] ),

			Descriptor( SubtractType, 2, () => new SubtractModule(), category: FFBModuleCategory.Mixer, settings: [] ),

			Descriptor( BlendType, 2, () => new BlendModule(), category: FFBModuleCategory.Mixer, settings:
			[
				Knob( "Mix", 0f, 1f, 0.5f, 0.01f, F.Percent() )
			] ),

			Descriptor( AdaptiveBlendType, 2, () => new AdaptiveBlendModule(), category: FFBModuleCategory.Mixer, settings:
			[
				Knob( "Cutoff", 0f, 180f, 20f, 1f, F.Number( 1, "HertzUnits" ) ),
				Knob( "PeakCutoff", 0f, 180f, 6f, 1f, F.Number( 1, "HertzUnits" ) ),
				Knob( "Hold", 0f, 100f, 28f, 1f, F.Number( 0, "MillisecondsUnits" ) )
			] ),

			// ---- effects (7) ----
			Descriptor( SpeedGainType, 1, () => new SpeedGainModule(), category: FFBModuleCategory.Effect, canTest: true, settings:
			[
				Knob( "MinSpeed", 0f, 50f, 0f, 1f, F.Number( 0, "MPSUnits" ) ),
				Knob( "MaxSpeed", 0f, 100f, 30f, 1f, F.Number( 0, "MPSUnits" ) ),
				Knob( "GainAtMin", 0f, 2f, 1f, 0.05f, F.Multiplier( 2 ), breakRow: true ),
				Knob( "GainAtMax", 0f, 2f, 1f, 0.05f, F.Multiplier( 2 ) )
			] ),

			Descriptor( TorqueDitherType, 1, () => new TorqueDitherModule(), category: FFBModuleCategory.Effect, settings:
			[
				Knob( "Strength", 0f, 0.05f, 0.01f, 0.001f, F.WithOff( F.Percent( 1 ) ) ),
				Knob( "Threshold", 0f, 1f, 0.1f, 0.01f, F.Percent() )
			] ),

			Descriptor( CrashProtectionType, 1, () => new CrashProtectionModule(), category: FFBModuleCategory.Effect, canTest: true, settings:
			[
				Knob( "LongGForce", 2f, 20f, 8f, 0.5f, F.Number( 1, "GForceUnits" ) ),
				Knob( "LatGForce", 2f, 20f, 6f, 0.5f, F.Number( 1, "GForceUnits" ) ),
				Knob( "Duration", 0f, 10f, 1f, 0.5f, F.WithOff( F.Number( 1, "SecondsUnits" ) ), breakRow: true ),
				Knob( "ForceReduction", 0f, 1f, 0.95f, 0.01f, F.WithOff( F.Percent() ) ),
				Knob( "RecoveryTime", 0f, 5f, 1f, 0.25f, F.WithOff( F.Number( 2, "SecondsUnits" ) ) )
			] ),

			Descriptor( CurbProtectionType, 1, () => new CurbProtectionModule(), category: FFBModuleCategory.Effect, canTest: true, settings:
			[
				Knob( "ShockVelocity", 0f, 2f, 0.5f, 0.05f, F.Number( 2, "MPSUnits" ) ),
				Knob( "Duration", 0f, 2f, 0.1f, 0.05f, F.WithOff( F.Number( 2, "SecondsUnits" ) ) ),
				Knob( "ForceReduction", 0f, 1f, 0.75f, 0.01f, F.WithOff( F.Percent() ), breakRow: true ),
				Knob( "RecoveryTime", 0f, 2f, 0.1f, 0.05f, F.WithOff( F.Number( 2, "SecondsUnits" ) ) )
			] ),

			Descriptor( UndersteerForceType, 1, () => new UndersteerForceModule(), category: FFBModuleCategory.Effect, settings:
			[
				Choice( "Direction", 0f, ConstantForceDirectionChoices ),
				Knob( "Strength", 0f, 1f, 0.1f, 0.01f, F.StrengthWithTorque() ),
				Knob( "Curve", -1f, 1f, 0f, 0.05f, F.WithOff( F.Percent() ) )
			] ),

			Descriptor( OversteerForceType, 1, () => new OversteerForceModule(), category: FFBModuleCategory.Effect, settings:
			[
				Choice( "Direction", 0f, ConstantForceDirectionChoices ),
				Knob( "Strength", 0f, 1f, 0.1f, 0.01f, F.StrengthWithTorque() ),
				Knob( "Curve", -1f, 1f, 0f, 0.05f, F.WithOff( F.Percent() ) )
			] ),

			Descriptor( SeatOfPantsForceType, 1, () => new SeatOfPantsForceModule(), category: FFBModuleCategory.Effect, settings:
			[
				Choice( "Direction", 0f, ConstantForceDirectionChoices ),
				Knob( "Strength", 0f, 1f, 0.1f, 0.01f, F.StrengthWithTorque() ),
				Knob( "Curve", -1f, 1f, 0f, 0.05f, F.WithOff( F.Percent() ) )
			] ),

			// ---- vibration generators: steering effects (3) ----
			Descriptor( UndersteerVibrationType, 0, () => new UndersteerVibrationModule(), isGenerator: true, category: FFBModuleCategory.SteeringVibration, settings: VibrationSettings() ),
			Descriptor( OversteerVibrationType, 0, () => new OversteerVibrationModule(), isGenerator: true, category: FFBModuleCategory.SteeringVibration, settings: VibrationSettings() ),
			Descriptor( SeatOfPantsVibrationType, 0, () => new SeatOfPantsVibrationModule(), isGenerator: true, category: FFBModuleCategory.SteeringVibration, settings: VibrationSettings() ),

			// ---- vibration generators: other effects (5) ----
			Descriptor( ABSVibrationType, 0, () => new ABSVibrationModule(), isGenerator: true, category: FFBModuleCategory.OtherVibration, settings:
			[
				Knob( "Strength", 0f, 1f, 0f, 0.01f, F.StrengthWithTorque() )
			] ),

			Descriptor( GearChangeVibrationType, 0, () => new GearChangeVibrationModule(), isGenerator: true, category: FFBModuleCategory.OtherVibration, settings:
			[
				Knob( "Strength", 0f, 1f, 0f, 0.01f, F.StrengthWithTorque() )
			] ),

			Descriptor( ShiftRPMVibrationType, 0, () => new ShiftRPMVibrationModule(), isGenerator: true, category: FFBModuleCategory.OtherVibration, settings:
			[
				Knob( "Strength", 0f, 1f, 0f, 0.01f, F.StrengthWithTorque() )
			] ),

			Descriptor( RoadTextureType, 0, () => new RoadTextureModule(), isGenerator: true, category: FFBModuleCategory.OtherVibration, settings:
			[
				Knob( "Strength", 0f, 1f, 0.05f, 0.01f, F.WithOff( F.Percent() ) ),
				Knob( "Frequency", 5f, 120f, 35f, 1f, F.Number( 0, "HertzUnits" ) )
			] ),

			Descriptor( SlipTextureType, 0, () => new SlipTextureModule(), isGenerator: true, category: FFBModuleCategory.OtherVibration, settings:
			[
				Knob( "Strength", 0f, 1f, 0.05f, 0.01f, F.WithOff( F.Percent() ) ),
				Knob( "Frequency", 5f, 120f, 35f, 1f, F.Number( 0, "HertzUnits" ) )
			] ),

			// ---- output (1) (normalizer: Nm -> normalized, plus the two shapers that only make sense in
			// normalized space — keeping them here decouples the rest of the graph from the max force setting) ----
			Descriptor( OutputType, 1, () => new OutputModule(), isOutput: true, settings:
			[
				Knob( "Curve", -1f, 1f, 0f, 0.05f, F.WithOff( F.Percent() ), showCurve: true ),
				Switch( "SoftLimiter", true, "FFBModuleSoftLimiter" ),
				Knob( "Threshold", 0f, 1f, 0.9f, 0.01f, F.Percent() ),
				Knob( "Knee", 0f, 1f, 0.3f, 0.01f, F.Percent() ),
				Knob( "Ratio", 1f, 20f, 6f, 0.5f, F.Ratio )
			] )
		];
	}

	private static FFBSettingDescriptor[] VibrationSettings()
	{
		return
		[
			Choice( "Pattern", 1f, VibrationPatternChoices ),
			Knob( "Strength", 0f, 1f, 0f, 0.01f, F.StrengthWithTorque() ),
			Knob( "MinimumFrequency", 1f, 100f, 5f, 1f, F.Number( 0, "HertzUnits" ) ),
			Knob( "MaximumFrequency", 1f, 100f, 20f, 1f, F.Number( 0, "HertzUnits" ) ),
			Knob( "Curve", -1f, 1f, 0f, 0.05f, F.WithOff( F.Percent() ) )
		];
	}
}
