
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
			OnFire = app => AdjustFloat( S, propBase, delta )
		} );

		list.Add( new MappableAction
		{
			SettingsPropertyName = propBase + "MinusButtonMappings",
			CategoryKey = category,
			GroupLabelKey = groupKey,
			LabelKey = labelKey,
			Index = index,
			Direction = MappableActionDirection.Decrease,
			OnFire = app => AdjustFloat( S, propBase, -delta )
		} );
	}

	// Racing wheel knob - chats via RacingWheel.SendChatMessage( labelKey, valueString ), guarded by
	// RacingWheelInputMappedSettingUpdateEnabled. When inverted, the plus button decreases the value
	// and vice versa (matches the original auto-target behavior).
	private static void RwKnob( List<MappableAction> list, string groupKey, string labelKey, string propBase, float delta, bool inverted = false )
	{
		var plusDelta = inverted ? -delta : delta;

		list.Add( new MappableAction
		{
			SettingsPropertyName = propBase + "PlusButtonMappings",
			CategoryKey = "RacingWheel",
			GroupLabelKey = groupKey,
			LabelKey = labelKey,
			Direction = MappableActionDirection.Increase,
			OnFire = app =>
			{
				var settings = S;

				AdjustFloat( settings, propBase, plusDelta );

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( labelKey, GetValueString( settings, propBase + "String" ) );
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
			OnFire = app =>
			{
				var settings = S;

				AdjustFloat( settings, propBase, -plusDelta );

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( labelKey, GetValueString( settings, propBase + "String" ) );
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
			OnFire = app =>
			{
				var settings = S;

				AdjustFloat( settings, propBase, delta );

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
			OnFire = app =>
			{
				var settings = S;

				AdjustFloat( settings, propBase, -delta );

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

		Trigger( list, "RacingWheelEnableForceFeedbackButtonMappings", "RacingWheel", "Device_UC", "Power", app =>
		{
			var settings = S;

			settings.RacingWheelEnableForceFeedback = !settings.RacingWheelEnableForceFeedback;

			if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
			{
				RacingWheel.SendChatMessage( "Power", settings.RacingWheelEnableForceFeedback ? Loc[ "ON" ] : Loc[ "OFF" ] );
			}
		} );

		Trigger( list, "RacingWheelTestButtonMappings", "RacingWheel", "Device_UC", "Test", app =>
		{
			var settings = S;

			app.RacingWheel.PlayTestSignal = true;

			if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
			{
				RacingWheel.SendChatMessage( "Test" );
			}
		} );

		Trigger( list, "RacingWheelResetButtonMappings", "RacingWheel", "Device_UC", "Reset", app =>
		{
			var settings = S;

			app.RacingWheel.ResetForceFeedback = true;

			if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
			{
				RacingWheel.SendChatMessage( "Reset" );
			}
		} );

		Trigger( list, "RacingWheelSetButtonMappings", "RacingWheel", "OverallStrength_UC", "Set", app =>
		{
			var settings = S;

			app.RacingWheel.AutoSetMaxForce = true;

			if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
			{
				var autoTorqueString = $"{app.RacingWheel.GetCurrentAutoTorque():F1} {Loc[ "TorqueUnits" ]}";

				RacingWheel.SendChatMessage( "Set", autoTorqueString );
			}
		} );

		Trigger( list, "RacingWheelClearButtonMappings", "RacingWheel", "OverallStrength_UC", "Clear", app =>
		{
			var settings = S;

			app.RacingWheel.ClearPeakTorque = true;

			if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
			{
				RacingWheel.SendChatMessage( "Clear" );
			}
		} );

		Trigger( list, "RacingWheelStartRecordingMappings", "RacingWheel", "Preview_UC", "Record", app =>
		{
			app.RecordingManager.StartRecording();
		} );

		// ----- Racing wheel - knobs -----

		RwKnob( list, "OverallStrength_UC", "Strength", "RacingWheelStrength", 0.01f );
		RwKnob( list, "OverallStrength_UC", "MaxForce", "RacingWheelMaxForce", 1f );
		RwKnob( list, "OverallStrength_UC", "AutoTarget", "RacingWheelAutoMargin", 0.01f, inverted: true );
		RwKnob( list, "Algorithm_UC", "PredictionBlend", "RacingWheelPredictionBlend", 0.05f );
		RwKnob( list, "Algorithm_UC", "DetailBoost", "RacingWheelDetailBoost", 0.1f );
		RwKnob( list, "Algorithm_UC", "DetailBoostBias", "RacingWheelDetailBoostBias", 0.01f );
		RwKnob( list, "Algorithm_UC", "DeltaLimit", "RacingWheelDeltaLimit", 10f );
		RwKnob( list, "Algorithm_UC", "DeltaLimiterBias", "RacingWheelDeltaLimiterBias", 0.01f );
		RwKnob( list, "Algorithm_UC", "SlewCompressionThreshold", "RacingWheelSlewCompressionThreshold", 1f );
		RwKnob( list, "Algorithm_UC", "SlewCompressionRate", "RacingWheelSlewCompressionRate", 0.01f );
		RwKnob( list, "Algorithm_UC", "TotalCompressionThreshold", "RacingWheelTotalCompressionThreshold", 0.01f );
		RwKnob( list, "Algorithm_UC", "TotalCompressionRate", "RacingWheelTotalCompressionRate", 0.01f );
		RwKnob( list, "Algorithm_UC", "Multi360HzDetail", "RacingWheelMulti360HzDetail", 0.01f );
		RwKnob( list, "Algorithm_UC", "TorqueCompression", "RacingWheelMultiTorqueCompression", 0.01f );
		RwKnob( list, "Algorithm_UC", "SlewRateReduction", "RacingWheelMultiSlewRateReduction", 0.01f );
		RwKnob( list, "Algorithm_UC", "DetailGain", "RacingWheelMultiDetailGain", 0.01f );
		RwKnob( list, "Algorithm_UC", "OutputSmoothing", "RacingWheelMultiOutputSmoothing", 0.01f );
		RwKnob( list, "Output_UC", "Minimum", "RacingWheelOutputMinimum", 0.01f );
		RwKnob( list, "Output_UC", "Maximum", "RacingWheelOutputMaximum", 0.01f );
		RwKnob( list, "Output_UC", "Curve", "RacingWheelOutputCurve", 0.01f );
		RwKnob( list, "WheelLFE_UC", "Strength", "RacingWheelLFEStrength", 0.01f );
		RwKnob( list, "CrashProtection_UC", "LongitudalGForce", "RacingWheelCrashProtectionLongitudalGForce", 0.5f );
		RwKnob( list, "CrashProtection_UC", "LateralGForce", "RacingWheelCrashProtectionLateralGForce", 0.5f );
		RwKnob( list, "CrashProtection_UC", "Duration", "RacingWheelCrashProtectionDuration", 0.5f );
		RwKnob( list, "CrashProtection_UC", "ForceReduction", "RacingWheelCrashProtectionForceReduction", 0.05f );
		RwKnob( list, "CurbProtection_UC", "ShockVelocity", "RacingWheelCurbProtectionShockVelocity", 0.1f );
		RwKnob( list, "CurbProtection_UC", "Duration", "RacingWheelCurbProtectionDuration", 0.1f );
		RwKnob( list, "CurbProtection_UC", "ForceReduction", "RacingWheelCurbProtectionForceReduction", 0.05f );
		RwKnob( list, "ParkedEffects_UC", "ForceFeedbackStrength", "RacingWheelParkedStrength", 0.05f );
		RwKnob( list, "ParkedEffects_UC", "ParkedFriction", "RacingWheelParkedFriction", 0.05f );
		RwKnob( list, "OtherFeatures_UC", "SoftLockStrength", "RacingWheelSoftLockStrength", 0.05f );
		RwKnob( list, "OtherFeatures_UC", "RacingFriction", "RacingWheelFriction", 0.05f );
		RwKnob( list, "OtherFeatures_UC", "WheelCenteringStrength", "RacingWheelWheelCenteringStrength", 0.05f );
		RwKnob( list, "Effects_UC", "GearChangeVibrateStrength", "RacingWheelGearChangeVibrateStrength", 0.05f );
		RwKnob( list, "Effects_UC", "ABSVibrateStrength", "RacingWheelABSVibrateStrength", 0.05f );

		// ----- Steering effects - understeer -----

		SeKnob( list, "Understeer_UC", "MinimumThreshold", "SteeringEffectsUndersteerMinimumThreshold", 0.01f );
		SeKnob( list, "Understeer_UC", "MaximumThreshold", "SteeringEffectsUndersteerMaximumThreshold", 0.01f );
		SeKnob( list, "UndersteerWheelVibrationEffect_UC", "Strength", "SteeringEffectsUndersteerWheelVibrationStrength", 0.01f );
		SeKnob( list, "UndersteerWheelVibrationEffect_UC", "MinimumFrequency", "SteeringEffectsUndersteerWheelVibrationMinimumFrequency", 1f );
		SeKnob( list, "UndersteerWheelVibrationEffect_UC", "MaximumFrequency", "SteeringEffectsUndersteerWheelVibrationMaximumFrequency", 1f );
		SeKnob( list, "UndersteerWheelVibrationEffect_UC", "Curve", "SteeringEffectsUndersteerWheelVibrationCurve", 0.05f );
		SeKnob( list, "UndersteerWheelConstantForceEffect_UC", "Strength", "SteeringEffectsUndersteerWheelConstantForceStrength", 0.01f );
		SeKnob( list, "UndersteerWheelConstantForceEffect_UC", "Curve", "SteeringEffectsUndersteerWheelConstantForceCurve", 0.05f );
		SeKnob( list, "UndersteerPedalVibrationEffect_UC", "MinimumFrequency", "SteeringEffectsUndersteerPedalVibrationMinimumFrequency", 0.05f );
		SeKnob( list, "UndersteerPedalVibrationEffect_UC", "MaximumFrequency", "SteeringEffectsUndersteerPedalVibrationMaximumFrequency", 0.05f );
		SeKnob( list, "UndersteerPedalVibrationEffect_UC", "Curve", "SteeringEffectsUndersteerPedalVibrationCurve", 0.05f );

		// ----- Steering effects - oversteer -----

		SeKnob( list, "Oversteer_UC", "MinimumThreshold", "SteeringEffectsOversteerMinimumThreshold", 0.01f );
		SeKnob( list, "Oversteer_UC", "MaximumThreshold", "SteeringEffectsOversteerMaximumThreshold", 0.01f );
		SeKnob( list, "OversteerWheelVibrationEffect_UC", "Strength", "SteeringEffectsOversteerWheelVibrationStrength", 0.01f );
		SeKnob( list, "OversteerWheelVibrationEffect_UC", "MinimumFrequency", "SteeringEffectsOversteerWheelVibrationMinimumFrequency", 1f );
		SeKnob( list, "OversteerWheelVibrationEffect_UC", "MaximumFrequency", "SteeringEffectsOversteerWheelVibrationMaximumFrequency", 1f );
		SeKnob( list, "OversteerWheelVibrationEffect_UC", "Curve", "SteeringEffectsOversteerWheelVibrationCurve", 0.05f );
		SeKnob( list, "OversteerWheelConstantForceEffect_UC", "Strength", "SteeringEffectsOversteerWheelConstantForceStrength", 0.01f );
		SeKnob( list, "OversteerWheelConstantForceEffect_UC", "Curve", "SteeringEffectsOversteerWheelConstantForceCurve", 0.05f );
		SeKnob( list, "OversteerPedalVibrationEffect_UC", "MinimumFrequency", "SteeringEffectsOversteerPedalVibrationMinimumFrequency", 0.05f );
		SeKnob( list, "OversteerPedalVibrationEffect_UC", "MaximumFrequency", "SteeringEffectsOversteerPedalVibrationMaximumFrequency", 0.05f );
		SeKnob( list, "OversteerPedalVibrationEffect_UC", "Curve", "SteeringEffectsOversteerPedalVibrationCurve", 0.05f );

		// ----- Steering effects - seat of pants -----

		SeKnob( list, "SeatOfPants_UC", "MinimumThreshold", "SteeringEffectsSeatOfPantsMinimumThreshold", 0.5f );
		SeKnob( list, "SeatOfPants_UC", "MaximumThreshold", "SteeringEffectsSeatOfPantsMaximumThreshold", 0.5f );
		SeKnob( list, "SeatOfPantsWheelVibrationEffect_UC", "Strength", "SteeringEffectsSeatOfPantsWheelVibrationStrength", 0.01f );
		SeKnob( list, "SeatOfPantsWheelVibrationEffect_UC", "MinimumFrequency", "SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency", 1f );
		SeKnob( list, "SeatOfPantsWheelVibrationEffect_UC", "MaximumFrequency", "SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency", 1f );
		SeKnob( list, "SeatOfPantsWheelVibrationEffect_UC", "Curve", "SteeringEffectsSeatOfPantsWheelVibrationCurve", 0.05f );
		SeKnob( list, "SeatOfPantsWheelConstantForceEffect_UC", "Strength", "SteeringEffectsSeatOfPantsWheelConstantForceStrength", 0.01f );
		SeKnob( list, "SeatOfPantsWheelConstantForceEffect_UC", "Curve", "SteeringEffectsSeatOfPantsWheelConstantForceCurve", 0.05f );
		SeKnob( list, "SeatOfPantsPedalVibrationEffect_UC", "MinimumFrequency", "SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency", 0.05f );
		SeKnob( list, "SeatOfPantsPedalVibrationEffect_UC", "MaximumFrequency", "SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency", 0.05f );
		SeKnob( list, "SeatOfPantsPedalVibrationEffect_UC", "Curve", "SteeringEffectsSeatOfPantsPedalVibrationCurve", 0.05f );

		// ----- Pedals - frequency / amplitude -----

		PlainKnob( list, "Pedals", "Frequency_UC", "Minimum", "PedalsMinimumFrequency", 1f );
		PlainKnob( list, "Pedals", "Frequency_UC", "Maximum", "PedalsMaximumFrequency", 1f );
		PlainKnob( list, "Pedals", "Frequency_UC", "Curve", "PedalsFrequencyCurve", 0.01f );
		PlainKnob( list, "Pedals", "Amplitude_UC", "Minimum", "PedalsMinimumAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "Amplitude_UC", "Maximum", "PedalsMaximumAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "Amplitude_UC", "Curve", "PedalsAmplitudeCurve", 0.01f );

		// ----- Pedals - clutch / brake / throttle strength + test -----

		PlainKnob( list, "Pedals", "Clutch_UC", "Strength", "PedalsClutchStrength1", 0.05f, index: 1 );
		Trigger( list, "PedalsClutchTest1ButtonMappings", "Pedals", "Clutch_UC", "Test", app => app.Pedals.StartTest( 0, 0 ), index: 1 );
		PlainKnob( list, "Pedals", "Clutch_UC", "Strength", "PedalsClutchStrength2", 0.05f, index: 2 );
		Trigger( list, "PedalsClutchTest2ButtonMappings", "Pedals", "Clutch_UC", "Test", app => app.Pedals.StartTest( 0, 1 ), index: 2 );
		PlainKnob( list, "Pedals", "Clutch_UC", "Strength", "PedalsClutchStrength3", 0.05f, index: 3 );
		Trigger( list, "PedalsClutchTest3ButtonMappings", "Pedals", "Clutch_UC", "Test", app => app.Pedals.StartTest( 0, 2 ), index: 3 );

		PlainKnob( list, "Pedals", "Brake_UC", "Strength", "PedalsBrakeStrength1", 0.05f, index: 1 );
		Trigger( list, "PedalsBrakeTest1ButtonMappings", "Pedals", "Brake_UC", "Test", app => app.Pedals.StartTest( 1, 0 ), index: 1 );
		PlainKnob( list, "Pedals", "Brake_UC", "Strength", "PedalsBrakeStrength2", 0.05f, index: 2 );
		Trigger( list, "PedalsBrakeTest2ButtonMappings", "Pedals", "Brake_UC", "Test", app => app.Pedals.StartTest( 1, 1 ), index: 2 );
		PlainKnob( list, "Pedals", "Brake_UC", "Strength", "PedalsBrakeStrength3", 0.05f, index: 3 );
		Trigger( list, "PedalsBrakeTest3ButtonMappings", "Pedals", "Brake_UC", "Test", app => app.Pedals.StartTest( 2, 1 ), index: 3 );

		PlainKnob( list, "Pedals", "Throttle_UC", "Strength", "PedalsThrottleStrength1", 0.05f, index: 1 );
		Trigger( list, "PedalsThrottleTest1ButtonMappings", "Pedals", "Throttle_UC", "Test", app => app.Pedals.StartTest( 2, 0 ), index: 1 );
		PlainKnob( list, "Pedals", "Throttle_UC", "Strength", "PedalsThrottleStrength2", 0.05f, index: 2 );
		Trigger( list, "PedalsThrottleTest2ButtonMappings", "Pedals", "Throttle_UC", "Test", app => app.Pedals.StartTest( 2, 1 ), index: 2 );
		PlainKnob( list, "Pedals", "Throttle_UC", "Strength", "PedalsThrottleStrength3", 0.05f, index: 3 );
		Trigger( list, "PedalsThrottleTest3ButtonMappings", "Pedals", "Throttle_UC", "Test", app => app.Pedals.StartTest( 2, 2 ), index: 3 );

		// ----- Pedals - effects -----

		PlainKnob( list, "Pedals", "GearChangeShiftIntoGear_UC", "Frequency", "PedalsShiftIntoGearFrequency", 0.01f );
		PlainKnob( list, "Pedals", "GearChangeShiftIntoGear_UC", "Amplitude", "PedalsShiftIntoGearAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "GearChangeShiftIntoGear_UC", "Duration", "PedalsShiftIntoGearDuration", 0.05f );
		PlainKnob( list, "Pedals", "GearChangeShiftIntoNeutral_UC", "Frequency", "PedalsShiftIntoNeutralFrequency", 0.01f );
		PlainKnob( list, "Pedals", "GearChangeShiftIntoNeutral_UC", "Amplitude", "PedalsShiftIntoNeutralAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "GearChangeShiftIntoNeutral_UC", "Duration", "PedalsShiftIntoNeutralDuration", 0.05f );
		PlainKnob( list, "Pedals", "ABSEngaged_UC", "Frequency", "PedalsABSEngagedFrequency", 0.01f );
		PlainKnob( list, "Pedals", "ABSEngaged_UC", "Amplitude", "PedalsABSEngagedAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "RPM_UC", "StartingRPM", "PedalsStartingRPM", 0.01f );
		PlainKnob( list, "Pedals", "ShiftRPM_UC", "Frequency", "PedalsShiftRPMFrequency", 0.01f );
		PlainKnob( list, "Pedals", "ShiftRPM_UC", "Amplitude", "PedalsShiftRPMAmplitude", 0.01f );
		PlainKnob( list, "Pedals", "WheelLock_UC", "Frequency", "PedalsWheelLockFrequency", 0.01f );
		PlainKnob( list, "Pedals", "WheelLock_UC", "Sensitivity", "PedalsWheelLockSensitivity", 0.01f );
		PlainKnob( list, "Pedals", "WheelSpin_UC", "Frequency", "PedalsWheelSpinFrequency", 0.01f );
		PlainKnob( list, "Pedals", "WheelSpin_UC", "Sensitivity", "PedalsWheelSpinSensitivity", 0.01f );
		PlainKnob( list, "Pedals", "ClutchSlip_UC", "StartingPoint", "PedalsClutchSlipStart", 0.01f );
		PlainKnob( list, "Pedals", "ClutchSlip_UC", "EndingPoint", "PedalsClutchSlipEnd", 0.01f );
		PlainKnob( list, "Pedals", "ClutchSlip_UC", "Frequency", "PedalsClutchSlipFrequency", 0.01f );
		PlainKnob( list, "Pedals", "OtherFeatures_UC", "NoiseDamper", "PedalsNoiseDamper", 0.01f );

		// ----- Wind -----

		PlainKnob( list, "Wind", "Settings_UC", "MasterWindPower", "WindMasterWindPower", 0.01f );
		PlainKnob( list, "Wind", "Settings_UC", "MinimumSpeed", "WindMinimumSpeed", 0.5f );
		PlainKnob( list, "Wind", "Settings_UC", "WindCurving", "WindCurving", 0.01f );

		// ----- Sounds -----

		PlainKnob( list, "Sounds", "Master_UC", "Volume", "SoundsMasterVolume", 0.01f );
		PlainKnob( list, "Sounds", "Click_UC", "Volume", "SoundsClickVolume", 0.01f );
		PlainKnob( list, "Sounds", "Click_UC", "FrequencyRatio", "SoundsClickFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "ABSEngaged_UC", "Volume", "SoundsABSEngagedVolume", 0.01f );
		PlainKnob( list, "Sounds", "ABSEngaged_UC", "FrequencyRatio", "SoundsABSEngagedFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "WheelLock_UC", "Volume", "SoundsWheelLockVolume", 0.01f );
		PlainKnob( list, "Sounds", "WheelLock_UC", "FrequencyRatio", "SoundsWheelLockFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "WheelLock_UC", "Sensitivity", "SoundsWheelLockSensitivity", 0.01f );
		PlainKnob( list, "Sounds", "WheelSpin_UC", "Volume", "SoundsWheelSpinVolume", 0.01f );
		PlainKnob( list, "Sounds", "WheelSpin_UC", "FrequencyRatio", "SoundsWheelSpinFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "WheelSpin_UC", "Sensitivity", "SoundsWheelSpinSensitivity", 0.01f );
		PlainKnob( list, "Sounds", "Understeer_UC", "Volume", "SoundsUndersteerVolume", 0.01f );
		PlainKnob( list, "Sounds", "Understeer_UC", "FrequencyRatio", "SoundsUndersteerFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "Oversteer_UC", "Volume", "SoundsOversteerVolume", 0.01f );
		PlainKnob( list, "Sounds", "Oversteer_UC", "FrequencyRatio", "SoundsOversteerFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "SeatOfPants_UC", "Volume", "SoundsSeatOfPantsVolume", 0.01f );
		PlainKnob( list, "Sounds", "SeatOfPants_UC", "FrequencyRatio", "SoundsSeatOfPantsFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "BrakeThrottleWarning_UC", "Volume", "SoundsBrakeThrottleWarningVolume", 0.01f );
		PlainKnob( list, "Sounds", "BrakeThrottleWarning_UC", "FrequencyRatio", "SoundsBrakeThrottleWarningFrequencyRatio", 0.01f );
		PlainKnob( list, "Sounds", "FfbClipping_UC", "Volume", "SoundsFfbClippingVolume", 0.01f );
		PlainKnob( list, "Sounds", "FfbClipping_UC", "FrequencyRatio", "SoundsFfbClippingFrequencyRatio", 0.01f );

		// ----- AdminBoxx -----

		PlainKnob( list, "AdminBoxx", "Display_UC", "Brightness", "AdminBoxxBrightness", 0.01f );
		PlainKnob( list, "AdminBoxx", "Audio_UC", "Volume", "AdminBoxxVolume", 0.01f );

		// ----- Trading paints -----

		Trigger( list, "TradingPaintsRedownloadButtonMappings", "TradingPaints", "Settings_UC", "RedownloadPaintFiles", app => app.TradingPaints.Reset() );

		return list;
	}

	#endregion
}
