
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
	public const string OutputType = "Output";

	public const string LowPassFilterType = "LowPassFilter";
	public const string HighPassFilterType = "HighPassFilter";
	public const string GainType = "Gain";
	public const string AddType = "Add";
	public const string BlendType = "Blend";
	public const string SlewLimiterType = "SlewLimiter";
	public const string SlewCompressorType = "SlewCompressor";
	public const string CompressorType = "Compressor";
	public const string TransientEnhancerType = "TransientEnhancer";
	public const string AdaptiveSmootherType = "AdaptiveSmoother";
	public const string AdaptiveBlendType = "AdaptiveBlend";
	public const string CurveType = "Curve";
	public const string SoftLimiterType = "SoftLimiter";
	public const string MaximumType = "Maximum";
	public const string MinimumType = "Minimum";

	public const string CrashProtectionType = "CrashProtection";
	public const string CurbProtectionType = "CurbProtection";
	public const string ParkedStrengthType = "ParkedStrength";
	public const string SoftLockType = "SoftLock";
	public const string FrictionType = "Friction";
	public const string WheelCenteringType = "WheelCentering";
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

	private static readonly Dictionary<string, FFBModuleDescriptor> _descriptors = BuildDescriptors();

	public static FFBModuleDescriptor? TryGet( string typeKey )
	{
		return _descriptors.TryGetValue( typeKey, out var descriptor ) ? descriptor : null;
	}

	public static FFBModuleDescriptor Get( string typeKey )
	{
		return _descriptors[ typeKey ];
	}

	public static IReadOnlyCollection<FFBModuleDescriptor> All => _descriptors.Values;

	// ---- descriptor construction helpers ----------------------------------------------------------------

	private static FFBSettingDescriptor Knob( string key, float min, float max, float defaultValue, float clickStepSize, Func<FFBFormatContext, string>? format = null, string? localizationKey = null, bool showCurve = false )
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
			ShowCurve = showCurve
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
		bool isGenerator = false, bool isSource = false, bool isOutput = false, string? inputA = null, string? inputB = null )
	{
		return new FFBModuleDescriptor
		{
			TypeKey = typeKey,
			LocalizationKey = "FFBModule" + typeKey,
			SignalInputCount = signalInputCount,
			InputALocalizationKey = inputA,
			InputBLocalizationKey = inputB,
			IsGenerator = isGenerator,
			IsSource = isSource,
			IsOutput = isOutput,
			Settings = settings,
			CreateRuntime = createRuntime
		};
	}

	private static Dictionary<string, FFBModuleDescriptor> BuildDescriptors()
	{
		var list = new List<FFBModuleDescriptor>
		{
			// ---- sources ----
			Descriptor( Source60HzType, 0, () => new Source60HzModule(), isSource: true, settings:
			[
				Choice( "PredictionMode", 0f, PredictionModeChoices ),
				Knob( "PredictionBlend", 0f, 1f, 0f, 0.05f, F.Percent() )
			] ),

			Descriptor( Source360HzType, 0, () => new Source360HzModule(), isSource: true, settings: [] ),

			Descriptor( SourceLFEType, 0, () => new SourceLFEModule(), isSource: true, settings: [] ),

			// ---- generic DSP ----
			Descriptor( LowPassFilterType, 1, () => new LowPassFilterModule(), settings:
			[
				Choice( "Slope", 0f, FilterSlopeChoices ),
				Knob( "Cutoff", 0f, 180f, 0f, 1f, F.Number( 1, "HertzUnits" ) )
			] ),

			Descriptor( HighPassFilterType, 1, () => new HighPassFilterModule(), settings:
			[
				Choice( "Slope", 0f, FilterSlopeChoices ),
				Knob( "Cutoff", 0f, 180f, 0f, 1f, F.Number( 1, "HertzUnits" ) )
			] ),

			Descriptor( GainType, 1, () => new GainModule(), settings:
			[
				Knob( "Gain", -5f, 5f, 1f, 0.05f, F.Number( 2 ) )
			] ),

			Descriptor( AddType, 2, () => new AddModule(), settings: [] ),

			Descriptor( BlendType, 2, () => new BlendModule(), settings:
			[
				Knob( "Mix", 0f, 1f, 0.5f, 0.01f, F.Percent() )
			] ),

			Descriptor( SlewLimiterType, 1, () => new SlewLimiterModule(), settings:
			[
				Knob( "Limit", 1f, 1500f, 360f, 10f, F.Number( 0, "DeltaLimitUnits" ) )
			] ),

			Descriptor( SlewCompressorType, 1, () => new SlewCompressorModule(), settings:
			[
				Knob( "Threshold", 0f, 1000f, 75f, 5f, F.Number( 0, "DeltaLimitUnits" ) ),
				Knob( "Knee", 0f, 200f, 30f, 5f, F.Number( 0, "DeltaLimitUnits" ) ),
				Knob( "Ratio", 1f, 20f, 3f, 0.5f, F.Ratio ),
				Switch( "PeakMode", false )
			] ),

			Descriptor( CompressorType, 1, () => new CompressorModule(), settings:
			[
				Knob( "Threshold", 0f, 50f, 30f, 1f, F.Number( 1, "TorqueUnits" ) ),
				Knob( "Knee", 0f, 20f, 5f, 0.5f, F.Number( 1, "TorqueUnits" ) ),
				Knob( "Ratio", 1f, 20f, 4f, 0.5f, F.Ratio )
			] ),

			Descriptor( TransientEnhancerType, 1, () => new TransientEnhancerModule(), settings:
			[
				Knob( "Cutoff", 0f, 180f, 7.2f, 1f, F.Number( 1, "HertzUnits" ) ),
				Knob( "Gain", 0f, 5f, 1f, 0.05f, F.Number( 2 ) )
			] ),

			Descriptor( AdaptiveSmootherType, 1, () => new AdaptiveSmootherModule(), settings:
			[
				Knob( "Amount", 0f, 1f, 0f, 0.01f, F.SmootherAmount )
			] ),

			Descriptor( AdaptiveBlendType, 2, () => new AdaptiveBlendModule(), settings:
			[
				Knob( "Cutoff", 0f, 180f, 20f, 1f, F.Number( 1, "HertzUnits" ) ),
				Knob( "PeakCutoff", 0f, 180f, 6f, 1f, F.Number( 1, "HertzUnits" ) ),
				Knob( "Hold", 0f, 100f, 28f, 1f, F.Number( 0, "MillisecondsUnits" ) )
			] ),

			Descriptor( CurveType, 1, () => new CurveModule(), settings:
			[
				Knob( "Curve", -1f, 1f, 0f, 0.05f, F.Percent(), showCurve: true )
			] ),

			Descriptor( SoftLimiterType, 1, () => new SoftLimiterModule(), settings: [] ),

			Descriptor( MaximumType, 1, () => new MaximumModule(), settings:
			[
				Knob( "Maximum", 0f, 50f, 50f, 1f, F.Number( 1, "TorqueUnits" ) )
			] ),

			Descriptor( MinimumType, 1, () => new MinimumModule(), settings:
			[
				Knob( "Minimum", 0f, 25f, 0f, 0.5f, F.Number( 1, "TorqueUnits" ) )
			] ),

			// ---- effects ----
			Descriptor( CrashProtectionType, 1, () => new CrashProtectionModule(), settings:
			[
				Knob( "LongGForce", 2f, 20f, 8f, 0.5f, F.Number( 1, "GForceUnits" ) ),
				Knob( "LatGForce", 2f, 20f, 6f, 0.5f, F.Number( 1, "GForceUnits" ) ),
				Knob( "Duration", 0f, 10f, 1f, 0.5f, F.Number( 1, "SecondsUnits" ) ),
				Knob( "ForceReduction", 0f, 1f, 0.95f, 0.01f, F.Percent() )
			] ),

			Descriptor( CurbProtectionType, 1, () => new CurbProtectionModule(), settings:
			[
				Knob( "ShockVelocity", 0f, 2f, 0.5f, 0.05f, F.Number( 2, "MPSUnits" ) ),
				Knob( "Duration", 0f, 2f, 0.1f, 0.05f, F.Number( 2, "SecondsUnits" ) ),
				Knob( "ForceReduction", 0f, 1f, 0.75f, 0.01f, F.Percent() )
			] ),

			Descriptor( ParkedStrengthType, 1, () => new ParkedStrengthModule(), settings:
			[
				Knob( "Strength", 0f, 1f, 0.1f, 0.01f, F.Percent() )
			] ),

			Descriptor( SoftLockType, 1, () => new SoftLockModule(), settings:
			[
				Knob( "Strength", 0f, 1f, 0.25f, 0.01f, F.Percent() )
			] ),

			Descriptor( FrictionType, 1, () => new FrictionModule(), settings:
			[
				Knob( "RacingFriction", 0f, 1f, 0f, 0.01f, F.Percent() ),
				Knob( "ParkedFriction", 0f, 1f, 0f, 0.01f, F.Percent() )
			] ),

			Descriptor( WheelCenteringType, 1, () => new WheelCenteringModule(), settings:
			[
				Knob( "Strength", 0f, 1f, 0.75f, 0.01f, F.Percent() ),
				Switch( "WhileRacing", false ),
				Switch( "WhileParked", true )
			] ),

			Descriptor( UndersteerForceType, 1, () => new UndersteerForceModule(), settings:
			[
				Choice( "Direction", 0f, ConstantForceDirectionChoices ),
				Knob( "Strength", 0f, 1f, 0.1f, 0.01f, F.StrengthWithTorque() ),
				Knob( "Curve", -1f, 1f, 0f, 0.05f, F.Percent() )
			] ),

			Descriptor( OversteerForceType, 1, () => new OversteerForceModule(), settings:
			[
				Choice( "Direction", 0f, ConstantForceDirectionChoices ),
				Knob( "Strength", 0f, 1f, 0.1f, 0.01f, F.StrengthWithTorque() ),
				Knob( "Curve", -1f, 1f, 0f, 0.05f, F.Percent() )
			] ),

			Descriptor( SeatOfPantsForceType, 1, () => new SeatOfPantsForceModule(), settings:
			[
				Choice( "Direction", 0f, ConstantForceDirectionChoices ),
				Knob( "Strength", 0f, 1f, 0.1f, 0.01f, F.StrengthWithTorque() ),
				Knob( "Curve", -1f, 1f, 0f, 0.05f, F.Percent() )
			] ),

			// ---- vibration generators ----
			Descriptor( UndersteerVibrationType, 0, () => new UndersteerVibrationModule(), isGenerator: true, settings: VibrationSettings() ),
			Descriptor( OversteerVibrationType, 0, () => new OversteerVibrationModule(), isGenerator: true, settings: VibrationSettings() ),
			Descriptor( SeatOfPantsVibrationType, 0, () => new SeatOfPantsVibrationModule(), isGenerator: true, settings: VibrationSettings() ),

			Descriptor( ShiftRPMVibrationType, 0, () => new ShiftRPMVibrationModule(), isGenerator: true, settings:
			[
				Knob( "Strength", 0f, 1f, 0f, 0.01f, F.StrengthWithTorque() )
			] ),

			Descriptor( GearChangeVibrationType, 0, () => new GearChangeVibrationModule(), isGenerator: true, settings:
			[
				Knob( "Strength", 0f, 1f, 0f, 0.01f, F.StrengthWithTorque() )
			] ),

			Descriptor( ABSVibrationType, 0, () => new ABSVibrationModule(), isGenerator: true, settings:
			[
				Knob( "Strength", 0f, 1f, 0f, 0.01f, F.StrengthWithTorque() )
			] ),

			// ---- creative (milestone 6) ----
			Descriptor( SpeedGainType, 1, () => new SpeedGainModule(), settings:
			[
				Knob( "MinSpeed", 0f, 50f, 0f, 1f, F.Number( 0, "MPSUnits" ) ),
				Knob( "MaxSpeed", 0f, 100f, 30f, 1f, F.Number( 0, "MPSUnits" ) ),
				Knob( "GainAtMin", 0f, 2f, 1f, 0.05f, F.Number( 2 ) ),
				Knob( "GainAtMax", 0f, 2f, 1f, 0.05f, F.Number( 2 ) )
			] ),

			Descriptor( RoadTextureType, 0, () => new RoadTextureModule(), isGenerator: true, settings:
			[
				Knob( "Strength", 0f, 1f, 0.05f, 0.01f, F.Percent() ),
				Knob( "Frequency", 5f, 120f, 35f, 1f, F.Number( 0, "HertzUnits" ) )
			] ),

			Descriptor( SlipTextureType, 0, () => new SlipTextureModule(), isGenerator: true, settings:
			[
				Knob( "Strength", 0f, 1f, 0.05f, 0.01f, F.Percent() ),
				Knob( "Frequency", 5f, 120f, 35f, 1f, F.Number( 0, "HertzUnits" ) )
			] ),

			Descriptor( TorqueDitherType, 1, () => new TorqueDitherModule(), settings:
			[
				Knob( "Strength", 0f, 0.05f, 0.01f, 0.001f, F.Percent() ),
				Knob( "Threshold", 0f, 1f, 0.1f, 0.01f, F.Percent() )
			] ),

			// ---- output (pure normalizer: Nm -> normalized; no user settings) ----
			Descriptor( OutputType, 1, () => new OutputModule(), isOutput: true, settings: [] )
		};

		var map = new Dictionary<string, FFBModuleDescriptor>( list.Count );

		foreach ( var descriptor in list )
		{
			map[ descriptor.TypeKey ] = descriptor;
		}

		return map;
	}

	private static FFBSettingDescriptor[] VibrationSettings()
	{
		return
		[
			Choice( "Pattern", 1f, VibrationPatternChoices ),
			Knob( "Strength", 0f, 1f, 0f, 0.01f, F.StrengthWithTorque() ),
			Knob( "MinimumFrequency", 1f, 100f, 5f, 1f, F.Number( 0, "HertzUnits" ) ),
			Knob( "MaximumFrequency", 1f, 100f, 20f, 1f, F.Number( 0, "HertzUnits" ) ),
			Knob( "Curve", -1f, 1f, 0f, 0.05f, F.Percent() )
		];
	}
}
