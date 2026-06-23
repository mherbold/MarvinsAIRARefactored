
using System.Reflection;

using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.DataContext;

namespace MarvinsAIRARefactored.Classes;

public enum MappableActionDirection
{
	None,
	Increase,
	Decrease
}

// One entry per mappable input action. The catalog is the single source of truth for "all mappable
// actions" - it drives both App.OnInput (button dispatch) and the Controller Profiles page (grouped
// list + per-action mapping editor). The 264 action bodies that used to live inline in App.OnInput
// were lifted into the OnFire delegates here.
//
// For display, each action carries localization keys (not English text) so the Controller Profiles
// page translates: CategoryKey (module / expander header), GroupLabelKey (the section the control
// sits in on its own page), LabelKey (the control's own label), and Direction (shown as +/-). Index
// disambiguates repeated controls (e.g. Clutch Strength 1/2/3).
public sealed class MappableAction
{
	private static readonly Dictionary<string, PropertyInfo> _propertyCache = [];

	public required string SettingsPropertyName { get; init; }      // e.g. "RacingWheelStrengthPlusButtonMappings"
	public required string CategoryKey { get; init; }               // localization key for the top-level group (module)
	public required string GroupLabelKey { get; init; }             // localization key for the section the control sits in
	public required string LabelKey { get; init; }                  // localization key for the control's own label
	public int Index { get; init; } = 0;                            // appended to the label when > 0 (e.g. Clutch Strength "1")
	public MappableActionDirection Direction { get; init; } = MappableActionDirection.None;
	public required Action<App> OnFire { get; init; }               // the action body

	// Knob actions only (null for triggers). StepSizeKey is the settings property base name shared by
	// the +/- pair (e.g. "RacingWheelStrength") and is the key into the universal Settings.KnobStepSizes
	// store; DefaultStepSize is the fallback step magnitude when the user has not overridden it.
	public string? StepSizeKey { get; init; }
	public float DefaultStepSize { get; init; }

	public ButtonMappings GetButtonMappings( Settings settings )
	{
		if ( !_propertyCache.TryGetValue( SettingsPropertyName, out var propertyInfo ) )
		{
			propertyInfo = typeof( Settings ).GetProperty( SettingsPropertyName ) ?? throw new InvalidOperationException( $"[MappableActionCatalog] Settings has no property named '{SettingsPropertyName}'" );

			_propertyCache[ SettingsPropertyName ] = propertyInfo;
		}

		return (ButtonMappings) propertyInfo.GetValue( settings )!;
	}
}

public static class MappableActionCatalog
{
	#region shortcuts

	private static Settings S => DataContext.DataContext.Instance.Settings;
	private static Localization Loc => DataContext.DataContext.Instance.Localization;

	#endregion

	public static IReadOnlyList<MappableAction> Actions { get; } = BuildActions();

	// propBase -> default step magnitude, built from the knob actions above. Lets MairaKnob (which only
	// knows the propBase derived from its binding) recover the catalog default without a settings lookup.
	private static readonly Dictionary<string, float> _defaultStepSizes = BuildDefaultStepSizes();

	private static Dictionary<string, float> BuildDefaultStepSizes()
	{
		var defaultStepSizes = new Dictionary<string, float>();

		foreach ( var action in Actions )
		{
			if ( action.StepSizeKey != null )
			{
				defaultStepSizes[ action.StepSizeKey ] = action.DefaultStepSize;
			}
		}

		return defaultStepSizes;
	}

	// True when propBase names a knob in the catalog; stepSize receives its default step magnitude.
	public static bool TryGetDefaultStepSize( string propertyBaseName, out float stepSize )
	{
		return _defaultStepSizes.TryGetValue( propertyBaseName, out stepSize );
	}

	// Validate that the catalog covers every ButtonMappings-typed property on Settings exactly once.
	// App calls this at startup and logs the result so a future mapping cannot be silently dropped
	// from input handling.
	public static (List<string> missing, List<string> duplicated) Validate()
	{
		var settingsProperties = typeof( Settings )
			.GetProperties( BindingFlags.Public | BindingFlags.Instance )
			.Where( p => p.PropertyType == typeof( ButtonMappings ) )
			.Select( p => p.Name )
			.ToHashSet();

		var seen = new HashSet<string>();
		var duplicated = new List<string>();

		foreach ( var action in Actions )
		{
			if ( !seen.Add( action.SettingsPropertyName ) )
			{
				duplicated.Add( action.SettingsPropertyName );
			}
		}

		var missing = settingsProperties.Where( name => !seen.Contains( name ) ).ToList();

		return ( missing, duplicated );
	}

	#region reflection helpers used by the action bodies

	private static readonly Dictionary<string, PropertyInfo> _propertyCache = [];

	private static PropertyInfo ResolveProperty( string propertyName )
	{
		if ( !_propertyCache.TryGetValue( propertyName, out var propertyInfo ) )
		{
			propertyInfo = typeof( Settings ).GetProperty( propertyName ) ?? throw new InvalidOperationException( $"[MappableActionCatalog] Settings has no property named '{propertyName}'" );

			_propertyCache[ propertyName ] = propertyInfo;
		}

		return propertyInfo;
	}

	private static void AdjustFloat( Settings settings, string valueProperty, float delta )
	{
		var propertyInfo = ResolveProperty( valueProperty );

		var current = (float) propertyInfo.GetValue( settings )!;

		propertyInfo.SetValue( settings, current + delta );
	}

	private static string GetValueString( Settings settings, string stringProperty )
	{
		var propertyInfo = ResolveProperty( stringProperty );

		return (string) ( propertyInfo.GetValue( settings ) ?? string.Empty );
	}

	#endregion

	#region factories

	private static void Trigger( List<MappableAction> list, string property, string category, string groupKey, string labelKey, Action<App> onFire, int index = 0 )
	{
		list.Add( new MappableAction
		{
			SettingsPropertyName = property,
			CategoryKey = category,
			GroupLabelKey = groupKey,
			LabelKey = labelKey,
			Index = index,
			Direction = MappableActionDirection.None,
			OnFire = onFire
		} );
	}

	// Plain knob (no chat message) - Pedals, Wind, Sounds, AdminBoxx
	private static void PlainKnob( List<MappableAction> list, string category, string groupKey, string labelKey, string propBase, float delta, int index = 0 )
	{
		list.Add( new MappableAction
		{
			SettingsPropertyName = propBase + "PlusButtonMappings",
			CategoryKey = category,
			GroupLabelKey = groupKey,
			LabelKey = labelKey,
			Index = index,
			Direction = MappableActionDirection.Increase,
			StepSizeKey = propBase,
			DefaultStepSize = delta,
			OnFire = app => AdjustFloat( S, propBase, S.GetKnobStepSize( propBase, delta ) )
		} );

		list.Add( new MappableAction
		{
			SettingsPropertyName = propBase + "MinusButtonMappings",
			CategoryKey = category,
			GroupLabelKey = groupKey,
			LabelKey = labelKey,
			Index = index,
			Direction = MappableActionDirection.Decrease,
			StepSizeKey = propBase,
			DefaultStepSize = delta,
			OnFire = app => AdjustFloat( S, propBase, -S.GetKnobStepSize( propBase, delta ) )
		} );
	}

	// Racing wheel knob - chats via RacingWheel.SendChatMessage( groupKey, labelKey, valueString ), guarded by
	// RacingWheelInputMappedSettingUpdateEnabled. When inverted, the plus button decreases the value
	// and vice versa (matches the original auto-target behavior).
	private static void RwKnob( List<MappableAction> list, string groupKey, string labelKey, string propBase, float delta, bool inverted = false )
	{
		list.Add( new MappableAction
		{
			SettingsPropertyName = propBase + "PlusButtonMappings",
			CategoryKey = "RacingWheel",
			GroupLabelKey = groupKey,
			LabelKey = labelKey,
			Direction = MappableActionDirection.Increase,
			StepSizeKey = propBase,
			DefaultStepSize = delta,
			OnFire = app =>
			{
				var settings = S;

				var step = settings.GetKnobStepSize( propBase, delta );

				AdjustFloat( settings, propBase, inverted ? -step : step );

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( groupKey, labelKey, GetValueString( settings, propBase + "String" ) );
				}
			}
		} );

		list.Add( new MappableAction
		{
			SettingsPropertyName = propBase + "MinusButtonMappings",
			CategoryKey = "RacingWheel",
			GroupLabelKey = groupKey,
			LabelKey = labelKey,
			Direction = MappableActionDirection.Decrease,
			StepSizeKey = propBase,
			DefaultStepSize = delta,
			OnFire = app =>
			{
				var settings = S;

				var step = settings.GetKnobStepSize( propBase, delta );

				AdjustFloat( settings, propBase, inverted ? step : -step );

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( groupKey, labelKey, GetValueString( settings, propBase + "String" ) );
				}
			}
		} );
	}

	// Steering effects knob - chats via SteeringEffects.SendChatMessage( groupKey, labelKey, valueString ),
	// guarded by SteeringEffectsInputMappedSettingUpdateEnabled. The chat group/label keys double as
	// the display group/label keys.
	private static void SeKnob( List<MappableAction> list, string groupKey, string labelKey, string propBase, float delta )
	{
		list.Add( new MappableAction
		{
			SettingsPropertyName = propBase + "PlusButtonMappings",
			CategoryKey = "SteeringEffects",
			GroupLabelKey = groupKey,
			LabelKey = labelKey,
			Direction = MappableActionDirection.Increase,
			StepSizeKey = propBase,
			DefaultStepSize = delta,
			OnFire = app =>
			{
				var settings = S;

				AdjustFloat( settings, propBase, settings.GetKnobStepSize( propBase, delta ) );

				if ( settings.SteeringEffectsInputMappedSettingUpdateEnabled )
				{
					SteeringEffects.SendChatMessage( groupKey, labelKey, GetValueString( settings, propBase + "String" ) );
				}
			}
		} );

		list.Add( new MappableAction
		{
			SettingsPropertyName = propBase + "MinusButtonMappings",
			CategoryKey = "SteeringEffects",
			GroupLabelKey = groupKey,
			LabelKey = labelKey,
			Direction = MappableActionDirection.Decrease,
			StepSizeKey = propBase,
			DefaultStepSize = delta,
			OnFire = app =>
			{
				var settings = S;

				AdjustFloat( settings, propBase, -settings.GetKnobStepSize( propBase, delta ) );

				if ( settings.SteeringEffectsInputMappedSettingUpdateEnabled )
				{
					SteeringEffects.SendChatMessage( groupKey, labelKey, GetValueString( settings, propBase + "String" ) );
				}
			}
		} );
	}

	#endregion

	#region catalog

	private static List<MappableAction> BuildActions()
	{
		var list = new List<MappableAction>();

		// ----- Racing wheel - buttons -----

		Trigger( list, "RacingWheelEnableForceFeedbackButtonMappings", "RacingWheel", "Device", "Power", app =>
		{
			var settings = S;

			settings.RacingWheelEnableForceFeedback = !settings.RacingWheelEnableForceFeedback;

			if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
			{
				RacingWheel.SendChatMessage( "Device", "Power", settings.RacingWheelEnableForceFeedback ? Loc[ "ON" ] : Loc[ "OFF" ] );
			}
		} );

		Trigger( list, "RacingWheelTestButtonMappings", "RacingWheel", "Device", "Test", app =>
		{
			var settings = S;

			app.RacingWheel.PlayTestSignal = true;

			if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
			{
				RacingWheel.SendChatMessage( "Device", "Test" );
			}
		} );

		Trigger( list, "RacingWheelResetButtonMappings", "RacingWheel", "Device", "Reset", app =>
		{
			var settings = S;

			app.RacingWheel.ResetForceFeedback = true;

			if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
			{
				RacingWheel.SendChatMessage( "Device", "Reset" );
			}
		} );

		Trigger( list, "RacingWheelSetButtonMappings", "RacingWheel", "OverallStrength", "Set", app =>
		{
			var settings = S;

			app.RacingWheel.AutoSetMaxForce = true;

			if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
			{
				var autoTorqueString = $"{app.RacingWheel.GetCurrentAutoTorque():F1} {Loc[ "TorqueUnits" ]}";

				RacingWheel.SendChatMessage( "OverallStrength", "Set", autoTorqueString );
			}
		} );

		Trigger( list, "RacingWheelClearButtonMappings", "RacingWheel", "OverallStrength", "Clear", app =>
		{
			var settings = S;

			app.RacingWheel.ClearPeakTorque = true;

			if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
			{
				RacingWheel.SendChatMessage( "OverallStrength", "Clear" );
			}
		} );

		Trigger( list, "RacingWheelStartRecordingMappings", "RacingWheel", "Preview", "Record", app =>
		{
			app.RecordingManager.StartRecording();
		} );

		// ----- Racing wheel - knobs -----

		RwKnob( list, "OverallStrength", "Strength", "RacingWheelStrength", 0.01f );
		RwKnob( list, "OverallStrength", "MaxForce", "RacingWheelMaxForce", 1f );
		RwKnob( list, "OverallStrength", "AutoTarget", "RacingWheelAutoTarget", 0.5f );
		RwKnob( list, "Algorithm", "PredictionBlend", "RacingWheelPredictionBlend", 0.05f );
		RwKnob( list, "Algorithm", "DetailBoost", "RacingWheelDetailBoost", 0.1f );
		RwKnob( list, "Algorithm", "DetailBoostBias", "RacingWheelDetailBoostBias", 0.01f );
		RwKnob( list, "Algorithm", "DeltaLimit", "RacingWheelDeltaLimit", 10f );
		RwKnob( list, "Algorithm", "DeltaLimiterBias", "RacingWheelDeltaLimiterBias", 0.01f );
		RwKnob( list, "Algorithm", "SlewCompressionThreshold", "RacingWheelSlewCompressionThreshold", 1f );
		RwKnob( list, "Algorithm", "SlewCompressionRate", "RacingWheelSlewCompressionRate", 0.01f );
		RwKnob( list, "Algorithm", "TotalCompressionThreshold", "RacingWheelTotalCompressionThreshold", 0.01f );
		RwKnob( list, "Algorithm", "TotalCompressionRate", "RacingWheelTotalCompressionRate", 0.01f );
		RwKnob( list, "Algorithm", "Multi360HzDetail", "RacingWheelMulti360HzDetail", 0.01f );
		RwKnob( list, "Algorithm", "TorqueCompression", "RacingWheelMultiTorqueCompression", 0.01f );
		RwKnob( list, "Algorithm", "SlewRateReduction", "RacingWheelMultiSlewRateReduction", 0.01f );
		RwKnob( list, "Algorithm", "DetailGain", "RacingWheelMultiDetailGain", 0.01f );
		RwKnob( list, "Algorithm", "OutputSmoothing", "RacingWheelMultiOutputSmoothing", 0.01f );
		RwKnob( list, "Output", "Minimum", "RacingWheelOutputMinimum", 0.01f );
		RwKnob( list, "Output", "Maximum", "RacingWheelOutputMaximum", 0.01f );
		RwKnob( list, "Output", "Curve", "RacingWheelOutputCurve", 0.01f );
		RwKnob( list, "WheelLFE", "Strength", "RacingWheelLFEStrength", 0.01f );
		RwKnob( list, "CrashProtection", "LongitudalGForce", "RacingWheelCrashProtectionLongitudalGForce", 0.5f );
		RwKnob( list, "CrashProtection", "LateralGForce", "RacingWheelCrashProtectionLateralGForce", 0.5f );
		RwKnob( list, "CrashProtection", "Duration", "RacingWheelCrashProtectionDuration", 0.5f );
		RwKnob( list, "CrashProtection", "ForceReduction", "RacingWheelCrashProtectionForceReduction", 0.05f );
		RwKnob( list, "CurbProtection", "ShockVelocity", "RacingWheelCurbProtectionShockVelocity", 0.1f );
		RwKnob( list, "CurbProtection", "Duration", "RacingWheelCurbProtectionDuration", 0.1f );
		RwKnob( list, "CurbProtection", "ForceReduction", "RacingWheelCurbProtectionForceReduction", 0.05f );
		RwKnob( list, "ParkedEffects", "ForceFeedbackStrength", "RacingWheelParkedStrength", 0.05f );
		RwKnob( list, "ParkedEffects", "ParkedFriction", "RacingWheelParkedFriction", 0.05f );
		RwKnob( list, "OtherFeatures", "SoftLockStrength", "RacingWheelSoftLockStrength", 0.05f );
		RwKnob( list, "OtherFeatures", "RacingFriction", "RacingWheelFriction", 0.05f );
		RwKnob( list, "OtherFeatures", "WheelCenteringStrength", "RacingWheelWheelCenteringStrength", 0.05f );
		RwKnob( list, "Effects", "GearChangeVibrateStrength", "RacingWheelGearChangeVibrateStrength", 0.05f );
		RwKnob( list, "Effects", "ABSVibrateStrength", "RacingWheelABSVibrateStrength", 0.05f );

		// ----- Steering effects - understeer -----

		SeKnob( list, "Understeer", "MinimumThreshold", "SteeringEffectsUndersteerMinimumThreshold", 0.01f );
		SeKnob( list, "Understeer", "MaximumThreshold", "SteeringEffectsUndersteerMaximumThreshold", 0.01f );
		SeKnob( list, "UndersteerWheelVibrationEffect", "Strength", "SteeringEffectsUndersteerWheelVibrationStrength", 0.01f );
		SeKnob( list, "UndersteerWheelVibrationEffect", "MinimumFrequency", "SteeringEffectsUndersteerWheelVibrationMinimumFrequency", 1f );
		SeKnob( list, "UndersteerWheelVibrationEffect", "MaximumFrequency", "SteeringEffectsUndersteerWheelVibrationMaximumFrequency", 1f );
		SeKnob( list, "UndersteerWheelVibrationEffect", "Curve", "SteeringEffectsUndersteerWheelVibrationCurve", 0.05f );
		SeKnob( list, "UndersteerWheelConstantForceEffect", "Strength", "SteeringEffectsUndersteerWheelConstantForceStrength", 0.01f );
		SeKnob( list, "UndersteerWheelConstantForceEffect", "Curve", "SteeringEffectsUndersteerWheelConstantForceCurve", 0.05f );
		SeKnob( list, "UndersteerPedalVibrationEffect", "MinimumFrequency", "SteeringEffectsUndersteerPedalVibrationMinimumFrequency", 0.05f );
		SeKnob( list, "UndersteerPedalVibrationEffect", "MaximumFrequency", "SteeringEffectsUndersteerPedalVibrationMaximumFrequency", 0.05f );
		SeKnob( list, "UndersteerPedalVibrationEffect", "Curve", "SteeringEffectsUndersteerPedalVibrationCurve", 0.05f );

		// ----- Steering effects - oversteer -----

		SeKnob( list, "Oversteer", "MinimumThreshold", "SteeringEffectsOversteerMinimumThreshold", 0.01f );
		SeKnob( list, "Oversteer", "MaximumThreshold", "SteeringEffectsOversteerMaximumThreshold", 0.01f );
		SeKnob( list, "OversteerWheelVibrationEffect", "Strength", "SteeringEffectsOversteerWheelVibrationStrength", 0.01f );
		SeKnob( list, "OversteerWheelVibrationEffect", "MinimumFrequency", "SteeringEffectsOversteerWheelVibrationMinimumFrequency", 1f );
		SeKnob( list, "OversteerWheelVibrationEffect", "MaximumFrequency", "SteeringEffectsOversteerWheelVibrationMaximumFrequency", 1f );
		SeKnob( list, "OversteerWheelVibrationEffect", "Curve", "SteeringEffectsOversteerWheelVibrationCurve", 0.05f );
		SeKnob( list, "OversteerWheelConstantForceEffect", "Strength", "SteeringEffectsOversteerWheelConstantForceStrength", 0.01f );
		SeKnob( list, "OversteerWheelConstantForceEffect", "Curve", "SteeringEffectsOversteerWheelConstantForceCurve", 0.05f );
		SeKnob( list, "OversteerPedalVibrationEffect", "MinimumFrequency", "SteeringEffectsOversteerPedalVibrationMinimumFrequency", 0.05f );
		SeKnob( list, "OversteerPedalVibrationEffect", "MaximumFrequency", "SteeringEffectsOversteerPedalVibrationMaximumFrequency", 0.05f );
		SeKnob( list, "OversteerPedalVibrationEffect", "Curve", "SteeringEffectsOversteerPedalVibrationCurve", 0.05f );

		// ----- Steering effects - seat of pants -----

		SeKnob( list, "SeatOfPants", "MinimumThreshold", "SteeringEffectsSeatOfPantsMinimumThreshold", 0.5f );
		SeKnob( list, "SeatOfPants", "MaximumThreshold", "SteeringEffectsSeatOfPantsMaximumThreshold", 0.5f );
		SeKnob( list, "SeatOfPantsWheelVibrationEffect", "Strength", "SteeringEffectsSeatOfPantsWheelVibrationStrength", 0.01f );
		SeKnob( list, "SeatOfPantsWheelVibrationEffect", "MinimumFrequency", "SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency", 1f );
		SeKnob( list, "SeatOfPantsWheelVibrationEffect", "MaximumFrequency", "SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency", 1f );
		SeKnob( list, "SeatOfPantsWheelVibrationEffect", "Curve", "SteeringEffectsSeatOfPantsWheelVibrationCurve", 0.05f );
		SeKnob( list, "SeatOfPantsWheelConstantForceEffect", "Strength", "SteeringEffectsSeatOfPantsWheelConstantForceStrength", 0.01f );
		SeKnob( list, "SeatOfPantsWheelConstantForceEffect", "Curve", "SteeringEffectsSeatOfPantsWheelConstantForceCurve", 0.05f );
		SeKnob( list, "SeatOfPantsPedalVibrationEffect", "MinimumFrequency", "SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency", 0.05f );
		SeKnob( list, "SeatOfPantsPedalVibrationEffect", "MaximumFrequency", "SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency", 0.05f );
		SeKnob( list, "SeatOfPantsPedalVibrationEffect", "Curve", "SteeringEffectsSeatOfPantsPedalVibrationCurve", 0.05f );

		// ----- Pedals - frequency / amplitude -----

		PlainKnob( list, "Pedals", "GlobalFrequency", "Minimum", "PedalsMinimumFrequency", 1f );
		PlainKnob( list, "Pedals", "GlobalFrequency", "Maximum", "PedalsMaximumFrequency", 1f );
		PlainKnob( list, "Pedals", "GlobalFrequency", "Curve", "PedalsFrequencyCurve", 0.01f );
		PlainKnob( list, "Pedals", "GlobalAmplitude", "Minimum", "PedalsMinimumAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "GlobalAmplitude", "Maximum", "PedalsMaximumAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "GlobalAmplitude", "Curve", "PedalsAmplitudeCurve", 0.01f );

		// ----- Pedals - clutch / brake / throttle strength + test -----

		PlainKnob( list, "Pedals", "Clutch", "Strength", "PedalsClutchStrength1", 0.05f, index: 1 );
		Trigger( list, "PedalsClutchTest1ButtonMappings", "Pedals", "Clutch", "Test", app => app.Pedals.StartTest( 0, 0 ), index: 1 );
		PlainKnob( list, "Pedals", "Clutch", "Strength", "PedalsClutchStrength2", 0.05f, index: 2 );
		Trigger( list, "PedalsClutchTest2ButtonMappings", "Pedals", "Clutch", "Test", app => app.Pedals.StartTest( 0, 1 ), index: 2 );
		PlainKnob( list, "Pedals", "Clutch", "Strength", "PedalsClutchStrength3", 0.05f, index: 3 );
		Trigger( list, "PedalsClutchTest3ButtonMappings", "Pedals", "Clutch", "Test", app => app.Pedals.StartTest( 0, 2 ), index: 3 );

		PlainKnob( list, "Pedals", "Brake", "Strength", "PedalsBrakeStrength1", 0.05f, index: 1 );
		Trigger( list, "PedalsBrakeTest1ButtonMappings", "Pedals", "Brake", "Test", app => app.Pedals.StartTest( 1, 0 ), index: 1 );
		PlainKnob( list, "Pedals", "Brake", "Strength", "PedalsBrakeStrength2", 0.05f, index: 2 );
		Trigger( list, "PedalsBrakeTest2ButtonMappings", "Pedals", "Brake", "Test", app => app.Pedals.StartTest( 1, 1 ), index: 2 );
		PlainKnob( list, "Pedals", "Brake", "Strength", "PedalsBrakeStrength3", 0.05f, index: 3 );
		Trigger( list, "PedalsBrakeTest3ButtonMappings", "Pedals", "Brake", "Test", app => app.Pedals.StartTest( 2, 1 ), index: 3 );

		PlainKnob( list, "Pedals", "Throttle", "Strength", "PedalsThrottleStrength1", 0.05f, index: 1 );
		Trigger( list, "PedalsThrottleTest1ButtonMappings", "Pedals", "Throttle", "Test", app => app.Pedals.StartTest( 2, 0 ), index: 1 );
		PlainKnob( list, "Pedals", "Throttle", "Strength", "PedalsThrottleStrength2", 0.05f, index: 2 );
		Trigger( list, "PedalsThrottleTest2ButtonMappings", "Pedals", "Throttle", "Test", app => app.Pedals.StartTest( 2, 1 ), index: 2 );
		PlainKnob( list, "Pedals", "Throttle", "Strength", "PedalsThrottleStrength3", 0.05f, index: 3 );
		Trigger( list, "PedalsThrottleTest3ButtonMappings", "Pedals", "Throttle", "Test", app => app.Pedals.StartTest( 2, 2 ), index: 3 );

		// ----- Pedals - effects -----

		PlainKnob( list, "Pedals", "GearChangeShiftIntoGear", "Frequency", "PedalsShiftIntoGearFrequency", 0.01f );
		PlainKnob( list, "Pedals", "GearChangeShiftIntoGear", "Amplitude", "PedalsShiftIntoGearAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "GearChangeShiftIntoGear", "Duration", "PedalsShiftIntoGearDuration", 0.05f );
		PlainKnob( list, "Pedals", "GearChangeShiftIntoNeutral", "Frequency", "PedalsShiftIntoNeutralFrequency", 0.01f );
		PlainKnob( list, "Pedals", "GearChangeShiftIntoNeutral", "Amplitude", "PedalsShiftIntoNeutralAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "GearChangeShiftIntoNeutral", "Duration", "PedalsShiftIntoNeutralDuration", 0.05f );
		PlainKnob( list, "Pedals", "ABSEngaged", "Frequency", "PedalsABSEngagedFrequency", 0.01f );
		PlainKnob( list, "Pedals", "ABSEngaged", "Amplitude", "PedalsABSEngagedAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "RPM", "StartingRPM", "PedalsStartingRPM", 0.01f );
		PlainKnob( list, "Pedals", "ShiftRPM", "Frequency", "PedalsShiftRPMFrequency", 0.01f );
		PlainKnob( list, "Pedals", "ShiftRPM", "Amplitude", "PedalsShiftRPMAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "WheelLock", "Frequency", "PedalsWheelLockFrequency", 0.01f );
		PlainKnob( list, "Pedals", "WheelLock", "Sensitivity", "PedalsWheelLockSensitivity", 0.01f );
		PlainKnob( list, "Pedals", "WheelSpin", "Frequency", "PedalsWheelSpinFrequency", 0.01f );
		PlainKnob( list, "Pedals", "WheelSpin", "Sensitivity", "PedalsWheelSpinSensitivity", 0.01f );
		PlainKnob( list, "Pedals", "ClutchSlip", "StartingPoint", "PedalsClutchSlipStart", 0.01f );
		PlainKnob( list, "Pedals", "ClutchSlip", "EndingPoint", "PedalsClutchSlipEnd", 0.01f );
		PlainKnob( list, "Pedals", "ClutchSlip", "Frequency", "PedalsClutchSlipFrequency", 0.01f );
		PlainKnob( list, "Pedals", "OtherFeatures", "NoiseDamper", "PedalsNoiseDamper", 0.01f );

		// ----- Wind -----

		PlainKnob( list, "Wind", "Settings", "MasterWindPower", "WindMasterWindPower", 0.01f );
		PlainKnob( list, "Wind", "Settings", "MinimumSpeed", "WindMinimumSpeed", 0.5f );
		PlainKnob( list, "Wind", "Settings", "WindCurving", "WindCurving", 0.01f );

		// ----- Sounds -----

		PlainKnob( list, "Sounds", "Master", "Volume", "SoundsMasterVolume", 0.01f );
		PlainKnob( list, "Sounds", "Click", "Volume", "SoundsClickVolume", 0.01f );
		PlainKnob( list, "Sounds", "Click", "FrequencyRatio", "SoundsClickFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "ABSEngaged", "Volume", "SoundsABSEngagedVolume", 0.01f );
		PlainKnob( list, "Sounds", "ABSEngaged", "FrequencyRatio", "SoundsABSEngagedFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "WheelLock", "Volume", "SoundsWheelLockVolume", 0.01f );
		PlainKnob( list, "Sounds", "WheelLock", "FrequencyRatio", "SoundsWheelLockFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "WheelLock", "Sensitivity", "SoundsWheelLockSensitivity", 0.01f );
		PlainKnob( list, "Sounds", "WheelSpin", "Volume", "SoundsWheelSpinVolume", 0.01f );
		PlainKnob( list, "Sounds", "WheelSpin", "FrequencyRatio", "SoundsWheelSpinFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "WheelSpin", "Sensitivity", "SoundsWheelSpinSensitivity", 0.01f );
		PlainKnob( list, "Sounds", "Understeer", "Volume", "SoundsUndersteerVolume", 0.01f );
		PlainKnob( list, "Sounds", "Understeer", "FrequencyRatio", "SoundsUndersteerFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "Oversteer", "Volume", "SoundsOversteerVolume", 0.01f );
		PlainKnob( list, "Sounds", "Oversteer", "FrequencyRatio", "SoundsOversteerFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "SeatOfPants", "Volume", "SoundsSeatOfPantsVolume", 0.01f );
		PlainKnob( list, "Sounds", "SeatOfPants", "FrequencyRatio", "SoundsSeatOfPantsFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "BrakeThrottleWarning", "Volume", "SoundsBrakeThrottleWarningVolume", 0.01f );
		PlainKnob( list, "Sounds", "BrakeThrottleWarning", "FrequencyRatio", "SoundsBrakeThrottleWarningFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "FfbClipping", "Volume", "SoundsFfbClippingVolume", 0.01f );
		PlainKnob( list, "Sounds", "FfbClipping", "FrequencyRatio", "SoundsFfbClippingFrequencyRatio", 0.01f );

		// ----- AdminBoxx -----

		PlainKnob( list, "AdminBoxx", "Display", "Brightness", "AdminBoxxBrightness", 0.01f );
		PlainKnob( list, "AdminBoxx", "Audio", "Volume", "AdminBoxxVolume", 0.01f );

		// ----- Trading paints -----

		Trigger( list, "TradingPaintsRedownloadButtonMappings", "TradingPaints", "Settings", "RedownloadPaintFiles", app => app.TradingPaints.Reset() );

		// ----- Commentary -----

		Trigger( list, "CommentaryEnabledButtonMappings", "Commentary", "Master", "Enabled", app =>
		{
			var settings = S;

			settings.CommentaryEnabled = !settings.CommentaryEnabled;
		} );

		// ----- App settings -----

		Trigger( list, "AppToggleMainWindowButtonMappings", "AppSettings", "WindowVisibility", "ShowHideWindow", app => app.MainWindow.ToggleWindowVisibility() );

		return list;
	}

	#endregion
}
