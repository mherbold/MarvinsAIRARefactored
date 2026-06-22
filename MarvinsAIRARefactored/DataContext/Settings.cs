
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using System.Xml.Serialization;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Windows;

using static MarvinsAIRARefactored.Windows.MainWindow;

namespace MarvinsAIRARefactored.DataContext;

public class Settings : INotifyPropertyChanged
{
	public static bool SuppressUpdatingOfContextSettings { private get; set; } = false;

	private bool _updatingRacingWheelRelatedSettings = false;
	private bool _updatingPedalsRelatedSettings = false;
	private bool _updatingRacingWheelMultiSettings = false;

	#region INotifyProperty stuff

	public event PropertyChangedEventHandler? PropertyChanged;

	public void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
	{
		var app = App.Instance!;

		if ( ( propertyName != null ) && !propertyName.EndsWith( "String" ) )
		{
			var propertyInfo = GetType().GetProperty( propertyName );

			if ( propertyInfo != null )
			{
				var value = propertyInfo.GetValue( this );

				var valueType = value?.GetType().Name ?? "null";

				bool isXmlIgnored = propertyInfo.GetCustomAttribute<XmlIgnoreAttribute>() != null;

				if ( ( propertyName != "AppWindowPositionAndSize" ) && !propertyName.EndsWith( "Scale" ) && !isXmlIgnored )
				{
					app.Logger.WriteLine( $"[Settings] Updating base setting {propertyName} to ({valueType}) {value}" );
				}

				if ( !SuppressUpdatingOfContextSettings )
				{
					UpdateSettings( true );
				}

				if ( !isXmlIgnored )
				{
					app.SettingsFile.QueueForSerialization = true;
				}
			}
		}

		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );

		app.Telemetry.UpdateSettings();
	}

	#endregion

	#region Context settings

	public SerializableDictionary<Context, ContextSettings> ContextSettingsDictionary { get; set; } = [];

	private ContextSettings FindContextSettings( Context context )
	{
		if ( !ContextSettingsDictionary.TryGetValue( context, out var contextSettings ) )
		{
			contextSettings = new ContextSettings();

			var contextSettingsProperties = typeof( ContextSettings ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

			foreach ( var contextSettingsProperty in contextSettingsProperties )
			{
				if ( contextSettingsProperty.CanRead && contextSettingsProperty.CanWrite )
				{
					var settingsProperty = typeof( Settings ).GetProperty( contextSettingsProperty.Name );

					if ( settingsProperty != null )
					{
						var settingsPropertyValue = settingsProperty.GetValue( this );

						contextSettingsProperty.SetValue( contextSettings, settingsPropertyValue );
					}
				}
			}

			ContextSettingsDictionary.Add( context, contextSettings );
		}

		return contextSettings;
	}

	#endregion

	#region Controller profiles

	// A controller profile is a complete, named set of button mappings. Users who swap wheels (rims)
	// on the same wheelbase switch profiles manually - the wheelbase device GUID never changes, so the
	// active rim cannot be auto-detected. The flat Settings.*ButtonMappings properties are the live
	// working copy of the currently selected profile (still bound in XAML and read by App.OnInput);
	// these helpers snapshot mappings in and out of the named profiles below, mutating the live
	// ButtonMappings objects in place so existing bindings stay valid.

	public SerializableDictionary<string, ControllerProfile> ControllerProfiles { get; set; } = [];

	public string CurrentControllerProfileName { get; set; } = ControllerProfile.DefaultProfileName;

	// Universal knob step sizes - the per-step amount applied when a knob's +/- button is pressed (by
	// mouse or by mapped input). Keyed by the knob's settings property base name (e.g. "RacingWheelStrength").
	// This is intentionally NOT part of any controller profile: it is shared across every profile, so the
	// step sizes survive profile switches. Missing entries fall back to the catalog default for that knob.
	public SerializableDictionary<string, float> KnobStepSizes { get; set; } = [];

	public float GetKnobStepSize( string propertyBaseName, float defaultStepSize )
	{
		return KnobStepSizes.TryGetValue( propertyBaseName, out var stepSize ) ? stepSize : defaultStepSize;
	}

	public void SetKnobStepSize( string propertyBaseName, float stepSize )
	{
		KnobStepSizes[ propertyBaseName ] = stepSize;
	}

	private static ButtonMappings.MappedButton.Button CloneButton( ButtonMappings.MappedButton.Button source )
	{
		return new ButtonMappings.MappedButton.Button
		{
			DeviceProductName = source.DeviceProductName,
			DeviceInstanceGuid = source.DeviceInstanceGuid,
			ButtonNumber = source.ButtonNumber
		};
	}

	private static ButtonMappings.MappedButton CloneMappedButton( ButtonMappings.MappedButton source )
	{
		return new ButtonMappings.MappedButton
		{
			HoldButton = CloneButton( source.HoldButton ),
			ClickButton = CloneButton( source.ClickButton )
		};
	}

	private static ButtonMappings CloneButtonMappings( ButtonMappings source )
	{
		var clone = new ButtonMappings();

		foreach ( var mappedButton in source.MappedButtons )
		{
			clone.MappedButtons.Add( CloneMappedButton( mappedButton ) );
		}

		return clone;
	}

	// Snapshots the live working-copy mappings into the currently selected profile (creating it if
	// needed). Called before switching away and before serialization so the store is authoritative.
	public void SaveCurrentControllerProfile()
	{
		if ( !ControllerProfiles.TryGetValue( CurrentControllerProfileName, out var profile ) )
		{
			profile = new ControllerProfile { Name = CurrentControllerProfileName };

			ControllerProfiles[ CurrentControllerProfileName ] = profile;
		}

		profile.ButtonMappings.Clear();

		foreach ( var action in MappableActionCatalog.Actions )
		{
			profile.ButtonMappings[ action.SettingsPropertyName ] = CloneButtonMappings( action.GetButtonMappings( this ) );
		}
	}

	// Copies the named profile's mappings into the live working copy, mutating the existing
	// ButtonMappings objects in place (so XAML bindings remain valid).
	public void ApplyControllerProfile( string name )
	{
		ControllerProfiles.TryGetValue( name, out var profile );

		foreach ( var action in MappableActionCatalog.Actions )
		{
			var liveButtonMappings = action.GetButtonMappings( this );

			liveButtonMappings.MappedButtons.Clear();

			if ( ( profile != null ) && profile.ButtonMappings.TryGetValue( action.SettingsPropertyName, out var storedButtonMappings ) )
			{
				foreach ( var mappedButton in storedButtonMappings.MappedButtons )
				{
					liveButtonMappings.MappedButtons.Add( CloneMappedButton( mappedButton ) );
				}
			}
		}
	}

	// Persists the current profile, then makes the named profile active. Data-only - the caller is
	// responsible for rebuilding the App button-mapping index, refreshing mappable button visuals,
	// and queuing serialization.
	public void SelectControllerProfile( string name )
	{
		SaveCurrentControllerProfile();

		CurrentControllerProfileName = name;

		ApplyControllerProfile( name );
	}

	// Creates a new profile (either empty or seeded with a copy of the current profile's mappings),
	// makes it active, and applies it. Data-only.
	public void CreateControllerProfile( string name, bool copyFromCurrent )
	{
		SaveCurrentControllerProfile();

		var profile = new ControllerProfile { Name = name };

		if ( copyFromCurrent )
		{
			foreach ( var action in MappableActionCatalog.Actions )
			{
				profile.ButtonMappings[ action.SettingsPropertyName ] = CloneButtonMappings( action.GetButtonMappings( this ) );
			}
		}

		ControllerProfiles[ name ] = profile;

		CurrentControllerProfileName = name;

		ApplyControllerProfile( name );
	}

	// Renames the named profile in place, preserving its mappings and keeping it active if it was the
	// current profile. Does nothing if the source is missing, the names match, or the target name is
	// already taken by another profile. Data-only.
	public void RenameControllerProfile( string oldName, string newName )
	{
		SaveCurrentControllerProfile();

		if ( oldName == newName )
		{
			return;
		}

		if ( !ControllerProfiles.TryGetValue( oldName, out var profile ) )
		{
			return;
		}

		if ( ControllerProfiles.ContainsKey( newName ) )
		{
			return;
		}

		profile.Name = newName;

		ControllerProfiles.Remove( oldName );

		ControllerProfiles[ newName ] = profile;

		if ( CurrentControllerProfileName == oldName )
		{
			CurrentControllerProfileName = newName;
		}
	}

	// Deletes the named profile, guaranteeing at least one profile always remains, then applies the
	// surviving active profile. Data-only.
	public void DeleteControllerProfile( string name )
	{
		ControllerProfiles.Remove( name );

		if ( ControllerProfiles.Count == 0 )
		{
			var defaultProfileName = ControllerProfile.GetLocalizedDefaultProfileName();

			ControllerProfiles[ defaultProfileName ] = new ControllerProfile { Name = defaultProfileName };
		}

		if ( !ControllerProfiles.ContainsKey( CurrentControllerProfileName ) )
		{
			CurrentControllerProfileName = ControllerProfiles.Keys.First();
		}

		ApplyControllerProfile( CurrentControllerProfileName );
	}

	// Called once after settings load. Migrates a pre-profiles settings file by snapshotting the
	// existing flat mappings into a "Default" profile, and guarantees the current profile is valid.
	public void EnsureControllerProfilesInitialized()
	{
		if ( ControllerProfiles.Count == 0 )
		{
			// First-ever creation of the default profile - name it using the localized "Default" for the
			// user's current language. A genuinely custom active name (should one somehow exist) is left alone.
			if ( string.IsNullOrEmpty( CurrentControllerProfileName ) || ( CurrentControllerProfileName == ControllerProfile.DefaultProfileName ) )
			{
				CurrentControllerProfileName = ControllerProfile.GetLocalizedDefaultProfileName();
			}

			SaveCurrentControllerProfile();
		}
		else
		{
			if ( !ControllerProfiles.ContainsKey( CurrentControllerProfileName ) )
			{
				CurrentControllerProfileName = ControllerProfiles.Keys.First();
			}

			ApplyControllerProfile( CurrentControllerProfileName );
		}
	}

	// One-time migration from the old percentage-based RacingWheelAutoMargin to the new absolute-Nm
	// RacingWheelAutoTarget. Converts the live and per-context values, carries the context scope and
	// the input mappings (both the live working copy and every saved controller profile) across, then
	// marks itself done. Must run BEFORE EnsureControllerProfilesInitialized so the renamed profile
	// keys are in place when the active profile is applied. The old setting is left untouched (dormant)
	// until it is eventually removed.
	public void MigrateAutoMarginToAutoTarget()
	{
		if ( RacingWheelAutoTargetMigrated )
		{
			return;
		}

		RacingWheelAutoTargetMigrated = true;

		RacingWheelAutoTarget = ConvertAutoMarginToAutoTarget( RacingWheelWheelForce, RacingWheelAutoMargin );

		foreach ( var contextSettings in ContextSettingsDictionary.Values )
		{
			contextSettings.RacingWheelAutoTarget = ConvertAutoMarginToAutoTarget( contextSettings.RacingWheelWheelForce, contextSettings.RacingWheelAutoMargin );
		}

		RacingWheelAutoTargetContextSwitches = new ContextSwitches(
			RacingWheelAutoMarginContextSwitches.PerWheelbase,
			RacingWheelAutoMarginContextSwitches.PerCar,
			RacingWheelAutoMarginContextSwitches.PerTrack,
			RacingWheelAutoMarginContextSwitches.PerTrackConfiguration,
			RacingWheelAutoMarginContextSwitches.PerWetDry );

		RacingWheelAutoTargetPlusButtonMappings = CloneButtonMappings( RacingWheelAutoMarginPlusButtonMappings );
		RacingWheelAutoTargetMinusButtonMappings = CloneButtonMappings( RacingWheelAutoMarginMinusButtonMappings );

		foreach ( var profile in ControllerProfiles.Values )
		{
			MigrateProfileMapping( profile, "RacingWheelAutoMarginPlusButtonMappings", "RacingWheelAutoTargetPlusButtonMappings" );
			MigrateProfileMapping( profile, "RacingWheelAutoMarginMinusButtonMappings", "RacingWheelAutoTargetMinusButtonMappings" );
		}
	}

	// The old margin mapped the peak torque to WheelForce / (1 + margin) Nm - that torque is the new
	// auto target. Clamped to [1, WheelForce] so it never exceeds the wheel force. If the wheel force is
	// below 1 Nm (e.g. an unconfigured context), there is no meaningful target, so just return the wheel
	// force itself - clamping with min > max would throw an ArgumentException.
	private static float ConvertAutoMarginToAutoTarget( float wheelForce, float autoMargin )
	{
		if ( wheelForce <= 1f )
		{
			return wheelForce;
		}

		var autoTarget = wheelForce / ( 1f + autoMargin );

		return Math.Clamp( autoTarget, 1f, wheelForce );
	}

	private static void MigrateProfileMapping( ControllerProfile profile, string oldKey, string newKey )
	{
		if ( !profile.ButtonMappings.ContainsKey( newKey ) && profile.ButtonMappings.TryGetValue( oldKey, out var mappings ) )
		{
			profile.ButtonMappings[ newKey ] = CloneButtonMappings( mappings );
		}
	}

	#endregion

	#region Context settings

	public void UpdateSettings( bool updateContextSettings )
	{
		var app = App.Instance!;

		SuppressUpdatingOfContextSettings = !updateContextSettings;

		var settingsProperties = typeof( Settings ).GetProperties( BindingFlags.Public | BindingFlags.Instance );

		foreach ( var settingsProperty in settingsProperties )
		{
			if ( settingsProperty.CanRead && settingsProperty.CanWrite && !settingsProperty.Name.EndsWith( "String" ) )
			{
				var contextSwitchesPropertyName = $"{settingsProperty.Name}ContextSwitches";

				var contextSwitchesProperty = GetType().GetProperty( contextSwitchesPropertyName );

				if ( contextSwitchesProperty != null )
				{
					var contextSwitches = (ContextSwitches?) contextSwitchesProperty.GetValue( this );

					if ( contextSwitches != null )
					{
						var context = new Context( contextSwitches );

						var contextSettings = FindContextSettings( context );

						var contextSettingsProperty = typeof( ContextSettings ).GetProperty( settingsProperty.Name );

						if ( contextSettingsProperty != null )
						{
							var contextSettingsPropertyValue = contextSettingsProperty.GetValue( contextSettings );
							var settingsPropertyValue = settingsProperty.GetValue( this );

							if ( !Equals( contextSettingsPropertyValue, settingsPropertyValue ) )
							{
								if ( updateContextSettings )
								{
									var valueType = settingsPropertyValue?.GetType().Name ?? "null";

									app.Logger.WriteLine( $"[Settings] Updating context setting {contextSettingsProperty.Name} to ({valueType}) {settingsPropertyValue} from setting ({context.WheelbaseGuid}|{context.CarName}|{context.TrackName}|{context.TrackConfigurationName}|{context.WetDryName})" );

									contextSettingsProperty.SetValue( contextSettings, settingsPropertyValue );
								}
								else
								{
									var valueType = contextSettingsPropertyValue?.GetType().Name ?? "null";

									app.Logger.WriteLine( $"[Settings] Updating setting {settingsProperty.Name} to ({valueType}) {contextSettingsPropertyValue} from context setting ({context.WheelbaseGuid}|{context.CarName}|{context.TrackName}|{context.TrackConfigurationName}|{context.WetDryName})" );

									settingsProperty.SetValue( this, contextSettingsPropertyValue );
								}
							}
						}
					}
				}
			}
		}

		SuppressUpdatingOfContextSettings = false;
	}

	/// <summary>Updates only the display strings that depend on the iRacing speed units (MPH vs KPH).</summary>
	public void UpdateSpeedUnitStrings()
	{
		var app = App.Instance!;

		var useMph = app.Simulator.DisplayUnits == 0;

		if ( _windMinimumSpeed == 0f )
		{
			WindMinimumSpeedString = DataContext.Instance.Localization[ "OFF" ];
		}
		else if ( useMph )
		{
			WindMinimumSpeedString = $"{_windMinimumSpeed * MathZ.MPSToMPH:F0} {DataContext.Instance.Localization[ "MPHUnits" ]}";
		}
		else
		{
			WindMinimumSpeedString = $"{_windMinimumSpeed * MathZ.MPSToKPH:F0} {DataContext.Instance.Localization[ "KPHUnits" ]}";
		}

		WindSpeed1String = useMph ? $"{_windSpeed1 * MathZ.MPSToMPH:F0}" : $"{_windSpeed1 * MathZ.MPSToKPH:F0}";
		WindSpeed2String = useMph ? $"{_windSpeed2 * MathZ.MPSToMPH:F0}" : $"{_windSpeed2 * MathZ.MPSToKPH:F0}";
		WindSpeed3String = useMph ? $"{_windSpeed3 * MathZ.MPSToMPH:F0}" : $"{_windSpeed3 * MathZ.MPSToKPH:F0}";
		WindSpeed4String = useMph ? $"{_windSpeed4 * MathZ.MPSToMPH:F0}" : $"{_windSpeed4 * MathZ.MPSToKPH:F0}";
		WindSpeed5String = useMph ? $"{_windSpeed5 * MathZ.MPSToMPH:F0}" : $"{_windSpeed5 * MathZ.MPSToKPH:F0}";
		WindSpeed6String = useMph ? $"{_windSpeed6 * MathZ.MPSToMPH:F0}" : $"{_windSpeed6 * MathZ.MPSToKPH:F0}";
		WindSpeed7String = useMph ? $"{_windSpeed7 * MathZ.MPSToMPH:F0}" : $"{_windSpeed7 * MathZ.MPSToKPH:F0}";
		WindSpeed8String = useMph ? $"{_windSpeed8 * MathZ.MPSToMPH:F0}" : $"{_windSpeed8 * MathZ.MPSToKPH:F0}";
		WindSpeed9String = useMph ? $"{_windSpeed9 * MathZ.MPSToMPH:F0}" : $"{_windSpeed9 * MathZ.MPSToKPH:F0}";
		WindSpeed10String = useMph ? $"{_windSpeed10 * MathZ.MPSToMPH:F0}" : $"{_windSpeed10 * MathZ.MPSToKPH:F0}";
	}

	// The various "...String" display properties cache a localized value (e.g. "100%") that is only
	// recomputed when the underlying setting changes. When the user switches languages at runtime they
	// would otherwise keep showing the previous language until the setting is touched, so this re-runs
	// every Update*String() builder to re-localize them. These builders only rebuild a display string
	// (no setter side effects), unlike the old Misc.ForcePropertySetters hack.
	private static readonly MethodInfo[] _displayStringUpdaters = [ .. typeof( Settings )
		.GetMethods( BindingFlags.NonPublic | BindingFlags.Instance )
		.Where( method => method.Name.StartsWith( "Update", StringComparison.Ordinal ) && method.Name.EndsWith( "String", StringComparison.Ordinal ) && method.GetParameters().Length == 0 ) ];

	/// <summary>Recomputes every localized display string. Call after a runtime language change.</summary>
	public void UpdateLocalizedStrings()
	{
		foreach ( var updater in _displayStringUpdaters )
		{
			updater.Invoke( this, null );
		}

		UpdateSpeedUnitStrings();
	}

	#endregion

	#region Related settings

	private void UpdateRelatedRacingWheelSettings( [CallerMemberName] string? propertyName = null )
	{
		if ( !_updatingRacingWheelRelatedSettings )
		{
			_updatingRacingWheelRelatedSettings = true;

			if ( propertyName == "RacingWheelWheelForce" )
			{
				RacingWheelMaxForce = RacingWheelWheelForce / RacingWheelStrength;
			}
			else if ( propertyName == "RacingWheelStrength" )
			{
				RacingWheelMaxForce = RacingWheelWheelForce / RacingWheelStrength;
			}
			else if ( propertyName == "RacingWheelMaxForce" )
			{
				RacingWheelStrength = RacingWheelWheelForce / RacingWheelMaxForce;
			}

			// Auto target can never exceed the wheel force - re-clamp it when the wheel force drops.
			if ( _racingWheelAutoTarget > RacingWheelWheelForce )
			{
				RacingWheelAutoTarget = RacingWheelWheelForce;
			}

			UpdateRacingWheelWheelForceString();
			UpdateRacingWheelStrengthString();
			UpdateRacingWheelMaxForceString();
			UpdateRacingWheelAutoTargetString();
			UpdateRacingWheelSlewCompressionThresholdString();
			UpdateRacingWheelTotalCompressionThresholdString();
			UpdateRacingWheelOutputMinimumString();
			UpdateRacingWheelOutputMaximumString();
			UpdateRacingWheelGearChangeVibrateStrengthString();
			UpdateRacingWheelABSVibrateStrengthString();

			UpdateSteeringEffectsUndersteerWheelVibrationStrengthString();
			UpdateSteeringEffectsUndersteerWheelConstantForceStrengthString();
			UpdateSteeringEffectsOversteerWheelVibrationStrengthString();
			UpdateSteeringEffectsOversteerWheelConstantForceStrengthString();
			UpdateSteeringEffectsSeatOfPantsWheelVibrationStrengthString();
			UpdateSteeringEffectsSeatOfPantsWheelConstantForceStrengthString();

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			_updatingRacingWheelRelatedSettings = false;
		}
	}

	private void UpdateRelatedPedalSettings( [CallerMemberName] string? propertyName = null )
	{
		if ( !_updatingPedalsRelatedSettings )
		{
			_updatingPedalsRelatedSettings = true;

			UpdateSteeringEffectsUndersteerPedalVibrationMinimumFrequencyString();
			UpdateSteeringEffectsUndersteerPedalVibrationMaximumFrequencyString();
			UpdateSteeringEffectsOversteerPedalVibrationMinimumFrequencyString();
			UpdateSteeringEffectsOversteerPedalVibrationMaximumFrequencyString();
			UpdateSteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString();
			UpdateSteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString();

			UpdatePedalsShiftIntoGearFrequencyString();
			UpdatePedalsShiftIntoNeutralFrequencyString();
			UpdatePedalsABSEngagedFrequencyString();
			UpdatePedalsShiftRPMFrequencyString();
			UpdatePedalsWheelLockFrequencyString();
			UpdatePedalsWheelSpinFrequencyString();
			UpdatePedalsClutchSlipFrequencyString();

			_updatingPedalsRelatedSettings = false;
		}
	}

	#endregion

	#region Racing wheel - Device

	private Guid _racingWheelSteeringDeviceGuid = Guid.Empty;

	public Guid RacingWheelSteeringDeviceGuid
	{
		get => _racingWheelSteeringDeviceGuid;

		set
		{
			if ( value != _racingWheelSteeringDeviceGuid )
			{
				_racingWheelSteeringDeviceGuid = value;

				OnPropertyChanged();

				var app = App.Instance!;

				app.RacingWheel.NextRacingWheelGuid = _racingWheelSteeringDeviceGuid;
			}
		}
	}

	#endregion

	#region Racing wheel - Enable force feedback

	private bool _racingWheelEnableForceFeedback = true;

	public bool RacingWheelEnableForceFeedback
	{
		get => _racingWheelEnableForceFeedback;

		set
		{
			if ( value != _racingWheelEnableForceFeedback )
			{
				_racingWheelEnableForceFeedback = value;

				OnPropertyChanged();
			}

			_racingWheelPage.UpdateSteeringDeviceSection();
		}
	}

	public ContextSwitches RacingWheelEnableForceFeedbackContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelEnableForceFeedbackButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Test

	public ButtonMappings RacingWheelTestButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Reset

	public ButtonMappings RacingWheelResetButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Wheel force

	private float _racingWheelWheelForce = 5f;

	public float RacingWheelWheelForce
	{
		get => _racingWheelWheelForce;

		set
		{
			value = float.IsNaN( value ) ? 5f : value;

			value = Math.Clamp( value, 2f, 50f );

			if ( value != _racingWheelWheelForce )
			{
				_racingWheelWheelForce = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings();
		}
	}

	private string _racingWheelWheelForceString = string.Empty;

	[XmlIgnore]
	public string RacingWheelWheelForceString
	{
		get => _racingWheelWheelForceString;

		set
		{
			if ( value != _racingWheelWheelForceString )
			{
				_racingWheelWheelForceString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelWheelForceString()
	{
		RacingWheelWheelForceString = $"{_racingWheelWheelForce:F1} {DataContext.Instance.Localization[ "TorqueUnits" ]}";
	}

	public ContextSwitches RacingWheelWheelForceContextSwitches { get; set; } = new( true, false, false, false, false );

	#endregion

	#region Racing wheel - Strength

	private float _racingWheelStrength = 0.1f;

	public float RacingWheelStrength
	{
		get => _racingWheelStrength;

		set
		{
			value = float.IsNaN( value ) ? 0.1f : value;

			value = Math.Clamp( value, 0f, RacingWheelAllowSuperStrength ? 2f : 1f );

			if ( value != _racingWheelStrength )
			{
				_racingWheelStrength = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings();
		}
	}

	private string _racingWheelStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelStrengthString
	{
		get => _racingWheelStrengthString;

		set
		{
			if ( value != _racingWheelStrengthString )
			{
				_racingWheelStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelStrengthString()
	{
		RacingWheelStrengthString = $"{_racingWheelStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelStrengthContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Max force

	private float _racingWheelMaxForce = 50f;

	public float RacingWheelMaxForce
	{
		get => _racingWheelMaxForce;

		set
		{
			value = float.IsNaN( value ) ? 50f : value;

			value = Math.Clamp( value, RacingWheelWheelForce * ( RacingWheelAllowSuperStrength ? 0.5f : 1f ), 300.0f );

			if ( value != _racingWheelMaxForce )
			{
				_racingWheelMaxForce = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings();
		}
	}

	private string _racingWheelMaxForceString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMaxForceString
	{
		get => _racingWheelMaxForceString;

		set
		{
			if ( value != _racingWheelMaxForceString )
			{
				_racingWheelMaxForceString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelMaxForceString()
	{
		RacingWheelMaxForceString = $"{_racingWheelMaxForce:F1} {DataContext.Instance.Localization[ "TorqueUnits" ]}";
	}

	public ButtonMappings RacingWheelMaxForcePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMaxForceMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Auto margin

	private float _racingWheelAutoMargin = 0f;

	public float RacingWheelAutoMargin
	{
		get => _racingWheelAutoMargin;

		set
		{
			value = Math.Clamp( value, -0.5f, 6f );

			if ( value != _racingWheelAutoMargin )
			{
				_racingWheelAutoMargin = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelAutoMarginContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelAutoMarginPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelAutoMarginMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Auto target

	// Auto target replaces the old percentage-based RacingWheelAutoMargin. It stores the desired peak
	// torque directly in Nm (like Max Force). One-time migration from the old setting happens on load
	// (see MigrateAutoMarginToAutoTarget); RacingWheelAutoMargin is kept dormant until enough users
	// have migrated. Auto target is never allowed to exceed the wheel force, so the auto-set max force
	// (peak * WheelForce / AutoTarget) always sits at or above the measured peak.
	private float _racingWheelAutoTarget = 10f;

	public float RacingWheelAutoTarget
	{
		get => _racingWheelAutoTarget;

		set
		{
			value = float.IsNaN( value ) ? RacingWheelWheelForce : value;

			value = Math.Clamp( value, 1f, RacingWheelWheelForce );

			if ( value != _racingWheelAutoTarget )
			{
				_racingWheelAutoTarget = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelAutoTargetString();
		}
	}

	private string _racingWheelAutoTargetString = string.Empty;

	[XmlIgnore]
	public string RacingWheelAutoTargetString
	{
		get => _racingWheelAutoTargetString;

		set
		{
			if ( value != _racingWheelAutoTargetString )
			{
				_racingWheelAutoTargetString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelAutoTargetString()
	{
		RacingWheelAutoTargetString = $"{_racingWheelAutoTarget:F1} {DataContext.Instance.Localization[ "TorqueUnits" ]}";
	}

	public ContextSwitches RacingWheelAutoTargetContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelAutoTargetPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelAutoTargetMinusButtonMappings { get; set; } = new();

	// Set true once the old RacingWheelAutoMargin value/mappings have been migrated into RacingWheelAutoTarget.
	public bool RacingWheelAutoTargetMigrated { get; set; } = false;

	#endregion

	#region Racing wheel - Set

	public ButtonMappings RacingWheelSetButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Clear

	public ButtonMappings RacingWheelClearButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Algorithm

	private RacingWheel.Algorithm _racingWheelAlgorithm = RacingWheel.Algorithm.DetailBoosterOn60Hz;

	public RacingWheel.Algorithm RacingWheelAlgorithm
	{
		get => _racingWheelAlgorithm;

		set
		{
			if ( value != _racingWheelAlgorithm )
			{
				_racingWheelAlgorithm = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.MainWindow.UpdateRacingWheelAlgorithmControls();

			app.RacingWheel.UpdateAlgorithmPreview = true;
		}
	}

	public ContextSwitches RacingWheelAlgorithmContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Enable soft limiter

	private bool _racingWheelEnableSoftLimiter = true;

	public bool RacingWheelEnableSoftLimiter
	{
		get => _racingWheelEnableSoftLimiter;

		set
		{
			if ( value != _racingWheelEnableSoftLimiter )
			{
				_racingWheelEnableSoftLimiter = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;
		}
	}

	public ContextSwitches RacingWheelEnableSoftLimiterContextSwitches { get; set; } = new( true, true, false, false, false );

	#endregion

	#region Racing wheel - Prediction mode

	private RacingWheel.PredictionMode _racingWheelPredictionMode = RacingWheel.PredictionMode.PredictK1;

	public RacingWheel.PredictionMode RacingWheelPredictionMode
	{
		get => _racingWheelPredictionMode;

		set
		{
			if ( value != _racingWheelPredictionMode )
			{
				_racingWheelPredictionMode = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelPredictionModeContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Prediction blend

	private float _racingWheelPredictionBlend = 0.35f;

	public float RacingWheelPredictionBlend
	{
		get => _racingWheelPredictionBlend;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _racingWheelPredictionBlend )
			{
				_racingWheelPredictionBlend = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelPredictionBlendString();
		}
	}

	private string _racingWheelPredictionBlendString = string.Empty;

	[XmlIgnore]
	public string RacingWheelPredictionBlendString
	{
		get => _racingWheelPredictionBlendString;

		set
		{
			if ( value != _racingWheelPredictionBlendString )
			{
				_racingWheelPredictionBlendString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelPredictionBlendString()
	{
		RacingWheelPredictionBlendString = $"{_racingWheelPredictionBlend * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelPredictionBlendContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelPredictionBlendPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelPredictionBlendMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Detail boost

	private float _racingWheelDetailBoost = 0.5f;

	public float RacingWheelDetailBoost
	{
		get => _racingWheelDetailBoost;

		set
		{
			value = Math.Clamp( value, 0f, 9.99f );

			if ( value != _racingWheelDetailBoost )
			{
				_racingWheelDetailBoost = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelDetailBoostString();
		}
	}

	private string _racingWheelDetailBoostString = string.Empty;

	[XmlIgnore]
	public string RacingWheelDetailBoostString
	{
		get => _racingWheelDetailBoostString;

		set
		{
			if ( value != _racingWheelDetailBoostString )
			{
				_racingWheelDetailBoostString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelDetailBoostString()
	{
		RacingWheelDetailBoostString = $"{_racingWheelDetailBoost * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelDetailBoostContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelDetailBoostPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelDetailBoostMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Detail boost bias

	private float _racingWheelDetailBoostBias = 0.1f;

	public float RacingWheelDetailBoostBias
	{
		get => _racingWheelDetailBoostBias;

		set
		{
			value = Math.Clamp( value, 0.05f, 1f );

			if ( value != _racingWheelDetailBoostBias )
			{
				_racingWheelDetailBoostBias = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelDetailBoostBiasString();
		}
	}

	private string _racingWheelDetailBoostBiasString = string.Empty;

	[XmlIgnore]
	public string RacingWheelDetailBoostBiasString
	{
		get => _racingWheelDetailBoostBiasString;

		set
		{
			if ( value != _racingWheelDetailBoostBiasString )
			{
				_racingWheelDetailBoostBiasString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelDetailBoostBiasString()
	{
		RacingWheelDetailBoostBiasString = $"{_racingWheelDetailBoostBias * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelDetailBoostBiasContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelDetailBoostBiasPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelDetailBoostBiasMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Delta limit

	private float _racingWheelDeltaLimit = 500f;

	public float RacingWheelDeltaLimit
	{
		get => _racingWheelDeltaLimit;

		set
		{
			value = Math.Clamp( value, 0f, 3000f );

			if ( value != _racingWheelDeltaLimit )
			{
				_racingWheelDeltaLimit = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelDeltaLimitString();
		}
	}

	private string _racingWheelDeltaLimitString = string.Empty;

	[XmlIgnore]
	public string RacingWheelDeltaLimitString
	{
		get => _racingWheelDeltaLimitString;

		set
		{
			if ( value != _racingWheelDeltaLimitString )
			{
				_racingWheelDeltaLimitString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelDeltaLimitString()
	{
		RacingWheelDeltaLimitString = $"{_racingWheelDeltaLimit:F0} {DataContext.Instance.Localization[ "DeltaLimitUnits" ]}";
	}

	public ContextSwitches RacingWheelDeltaLimitContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelDeltaLimitPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelDeltaLimitMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Delta limiter bias

	private float _racingWheelDeltaLimiterBias = 0.2f;

	public float RacingWheelDeltaLimiterBias
	{
		get => _racingWheelDeltaLimiterBias;

		set
		{
			value = Math.Clamp( value, 0.05f, 1f );

			if ( value != _racingWheelDeltaLimiterBias )
			{
				_racingWheelDeltaLimiterBias = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelDeltaLimiterBiasString();
		}
	}

	private string _racingWheelDeltaLimiterBiasString = string.Empty;

	[XmlIgnore]
	public string RacingWheelDeltaLimiterBiasString
	{
		get => _racingWheelDeltaLimiterBiasString;

		set
		{
			if ( value != _racingWheelDeltaLimiterBiasString )
			{
				_racingWheelDeltaLimiterBiasString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelDeltaLimiterBiasString()
	{
		RacingWheelDeltaLimiterBiasString = $"{_racingWheelDeltaLimiterBias * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelDeltaLimiterBiasContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelDeltaLimiterBiasPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelDeltaLimiterBiasMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Slew compression threshold

	private float _racingWheelSlewCompressionThreshold = 2f;

	public float RacingWheelSlewCompressionThreshold
	{
		get => _racingWheelSlewCompressionThreshold;

		set
		{
			value = Math.Clamp( value, 0f, 350f );

			if ( value != _racingWheelSlewCompressionThreshold )
			{
				_racingWheelSlewCompressionThreshold = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings();
		}
	}

	private string _racingWheelSlewCompressionThresholdString = string.Empty;

	[XmlIgnore]
	public string RacingWheelSlewCompressionThresholdString
	{
		get => _racingWheelSlewCompressionThresholdString;

		set
		{
			if ( value != _racingWheelSlewCompressionThresholdString )
			{
				_racingWheelSlewCompressionThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelSlewCompressionThresholdString()
	{
		RacingWheelSlewCompressionThresholdString = $"{_racingWheelSlewCompressionThreshold * DataContext.Instance.Settings.RacingWheelMaxForce / 1000f:F2} {DataContext.Instance.Localization[ "SlewUnits" ]}";
	}

	public ContextSwitches RacingWheelSlewCompressionThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelSlewCompressionThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelSlewCompressionThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Slew compression rate

	private float _racingWheelSlewCompressionRate = 0.65f;

	public float RacingWheelSlewCompressionRate
	{
		get => _racingWheelSlewCompressionRate;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelSlewCompressionRate )
			{
				_racingWheelSlewCompressionRate = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelSlewCompressionRateString();
		}
	}

	private string _racingWheelSlewCompressionRateString = string.Empty;

	[XmlIgnore]
	public string RacingWheelSlewCompressionRateString
	{
		get => _racingWheelSlewCompressionRateString;

		set
		{
			if ( value != _racingWheelSlewCompressionRateString )
			{
				_racingWheelSlewCompressionRateString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelSlewCompressionRateString()
	{
		RacingWheelSlewCompressionRateString = $"{_racingWheelSlewCompressionRate * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelSlewCompressionRateContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelSlewCompressionRatePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelSlewCompressionRateMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Total compression threshold

	private float _racingWheelTotalCompressionThreshold = 0.65f;

	public float RacingWheelTotalCompressionThreshold
	{
		get => _racingWheelTotalCompressionThreshold;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelTotalCompressionThreshold )
			{
				_racingWheelTotalCompressionThreshold = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings();
		}
	}

	private string _racingWheelTotalCompressionThresholdString = string.Empty;

	[XmlIgnore]
	public string RacingWheelTotalCompressionThresholdString
	{
		get => _racingWheelTotalCompressionThresholdString;

		set
		{
			if ( value != _racingWheelTotalCompressionThresholdString )
			{
				_racingWheelTotalCompressionThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelTotalCompressionThresholdString()
	{
		RacingWheelTotalCompressionThresholdString = $"{_racingWheelTotalCompressionThreshold * DataContext.Instance.Settings.RacingWheelMaxForce:F1} {DataContext.Instance.Localization[ "TorqueUnits" ]}";
	}

	public ContextSwitches RacingWheelTotalCompressionThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelTotalCompressionThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelTotalCompressionThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Total compression rate

	private float _racingWheelTotalCompressionRate = 0.75f;

	public float RacingWheelTotalCompressionRate
	{
		get => _racingWheelTotalCompressionRate;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelTotalCompressionRate )
			{
				_racingWheelTotalCompressionRate = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelTotalCompressionRateString();
		}
	}

	private string _racingWheelTotalCompressionRateString = string.Empty;

	[XmlIgnore]
	public string RacingWheelTotalCompressionRateString
	{
		get => _racingWheelTotalCompressionRateString;

		set
		{
			if ( value != _racingWheelTotalCompressionRateString )
			{
				_racingWheelTotalCompressionRateString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelTotalCompressionRateString()
	{
		RacingWheelTotalCompressionRateString = $"{_racingWheelTotalCompressionRate * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelTotalCompressionRateContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelTotalCompressionRatePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelTotalCompressionRateMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Multi FFB source menu selection

	private RacingWheel.MultiFFBSourceOptions _racingWheelMultiFFBSourceSelection = RacingWheel.MultiFFBSourceOptions.Native360Hz;

	public RacingWheel.MultiFFBSourceOptions RacingWheelMultiFFBSourceSelection
	{
		get => _racingWheelMultiFFBSourceSelection;

		set
		{
			if ( value != _racingWheelMultiFFBSourceSelection )
			{
				var oldValue = _racingWheelMultiFFBSourceSelection;

				_racingWheelMultiFFBSourceSelection = value;

				OnPropertyChanged();

				var app = App.Instance!;

				if ( !_updatingRacingWheelMultiSettings )
				{
					app.RacingWheel.SetCannedMultiAdjustAlgorithmValues();

					_updatingRacingWheelMultiSettings = true;

					switch ( RacingWheelMultiFFBSourceSelection )
					{
						case RacingWheel.MultiFFBSourceOptions.Native60Hz:
						case RacingWheel.MultiFFBSourceOptions.DefaultsNative60Hz:
							RacingWheelMultiFFBSourceSelection = RacingWheel.MultiFFBSourceOptions.Native60Hz;
							break;

						case RacingWheel.MultiFFBSourceOptions.Native360Hz:
						case RacingWheel.MultiFFBSourceOptions.DefaultsNative360Hz:
							RacingWheelMultiFFBSourceSelection = RacingWheel.MultiFFBSourceOptions.Native360Hz;
							break;

						case RacingWheel.MultiFFBSourceOptions.Hybrid10:
						case RacingWheel.MultiFFBSourceOptions.DefaultsHybrid10:
							RacingWheelMultiFFBSourceSelection = RacingWheel.MultiFFBSourceOptions.Hybrid10;
							break;

						case RacingWheel.MultiFFBSourceOptions.HybridVariable30:
						case RacingWheel.MultiFFBSourceOptions.DefaultsHybridVariable30:
						case RacingWheel.MultiFFBSourceOptions.PresetBasicFFB:
						case RacingWheel.MultiFFBSourceOptions.PresetBalancedFFB:
							RacingWheelMultiFFBSourceSelection = RacingWheel.MultiFFBSourceOptions.HybridVariable30;
							break;

						default:
							RacingWheelMultiFFBSourceSelection = oldValue;
							break;
					}

					_updatingRacingWheelMultiSettings = false;

				}
				else
				{
					// Force the combo box's displayed selection to update when the user selected a preset

					System.Windows.Application.Current.Dispatcher.BeginInvoke(
					DispatcherPriority.Loaded,
					new Action( () =>
					{
						PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( nameof( RacingWheelMultiFFBSourceSelection ) ) );
					} ) );
				}
				app.RacingWheel.UpdateAlgorithmPreview = true;
			}
		}
	}

	public ContextSwitches RacingWheelMultiFFBSourceSelectionContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Multi 360Hz detail

	private float _racingWheelMulti360HzDetail = 1f;

	public float RacingWheelMulti360HzDetail
	{
		get => _racingWheelMulti360HzDetail;

		set
		{
			var app = App.Instance!;

			if ( RacingWheelMultiFFBSourceSelection == RacingWheel.MultiFFBSourceOptions.Native60Hz )
			{
				value = 0f;
			}
			else if ( RacingWheelMultiFFBSourceSelection == RacingWheel.MultiFFBSourceOptions.Native360Hz )
			{
				value = 1f;
			}
			else
			{
				value = Math.Clamp( value, 0f, 3f );
			}

			if ( value != _racingWheelMulti360HzDetail )
			{
				_racingWheelMulti360HzDetail = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelMulti360HzDetailString();
		}
	}

	private string _racingWheelMulti360HzDetailString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMulti360HzDetailString
	{
		get => _racingWheelMulti360HzDetailString;

		set
		{
			if ( value != _racingWheelMulti360HzDetailString )
			{
				_racingWheelMulti360HzDetailString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelMulti360HzDetailString()
	{
		RacingWheelMulti360HzDetailString = $"{_racingWheelMulti360HzDetail * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelMulti360HzDetailContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelMulti360HzDetailPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMulti360HzDetailMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Multi torque compression

	private float _racingWheelMultiTorqueCompression = 0f;

	public float RacingWheelMultiTorqueCompression
	{
		get => _racingWheelMultiTorqueCompression;

		set
		{
			var app = App.Instance!;

			value = MathZ.Saturate( value );

			if ( value != _racingWheelMultiTorqueCompression )
			{
				_racingWheelMultiTorqueCompression = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelMultiTorqueCompressionString();
		}
	}

	private string _racingWheelMultiTorqueCompressionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMultiTorqueCompressionString
	{
		get => _racingWheelMultiTorqueCompressionString;

		set
		{
			if ( value != _racingWheelMultiTorqueCompressionString )
			{
				_racingWheelMultiTorqueCompressionString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelMultiTorqueCompressionString()
	{
		RacingWheelMultiTorqueCompressionString = $"{_racingWheelMultiTorqueCompression * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelMultiTorqueCompressionContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelMultiTorqueCompressionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMultiTorqueCompressionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Multi enable slew peak mode

	private bool _racingWheelMultiEnableSlewPeakMode = false;

	public bool RacingWheelMultiEnableSlewPeakMode
	{
		get => _racingWheelMultiEnableSlewPeakMode;

		set
		{
			var app = App.Instance!;

			if ( value != _racingWheelMultiEnableSlewPeakMode )
			{
				_racingWheelMultiEnableSlewPeakMode = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;
		}
	}

	public ContextSwitches RacingWheelMultiEnableSlewPeakModeContextSwitches { get; set; } = new( true, true, false, false, false );

	#endregion

	#region Racing wheel - Multi slew rate reduction

	private float _racingWheelMultiSlewRateReduction = 0f;

	public float RacingWheelMultiSlewRateReduction
	{
		get => _racingWheelMultiSlewRateReduction;

		set
		{
			var app = App.Instance!;

			value = MathZ.Saturate( value );

			if ( value != _racingWheelMultiSlewRateReduction )
			{
				_racingWheelMultiSlewRateReduction = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelMultiSlewRateReductionString();
		}
	}

	private string _racingWheelMultiSlewRateReductionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMultiSlewRateReductionString
	{
		get => _racingWheelMultiSlewRateReductionString;

		set
		{
			if ( value != _racingWheelMultiSlewRateReductionString )
			{
				_racingWheelMultiSlewRateReductionString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelMultiSlewRateReductionString()
	{
		RacingWheelMultiSlewRateReductionString = $"{_racingWheelMultiSlewRateReduction * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelMultiSlewRateReductionContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelMultiSlewRateReductionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMultiSlewRateReductionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Multi detail gain

	private float _racingWheelMultiDetailGain = 0f;

	public float RacingWheelMultiDetailGain
	{
		get => _racingWheelMultiDetailGain;

		set
		{
			var app = App.Instance!;

			value = Math.Clamp( value, -1f, 3f );

			if ( value != _racingWheelMultiDetailGain )
			{
				_racingWheelMultiDetailGain = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelMultiDetailGainString();
		}
	}

	private string _racingWheelMultiDetailGainString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMultiDetailGainString
	{
		get => _racingWheelMultiDetailGainString;

		set
		{
			if ( value != _racingWheelMultiDetailGainString )
			{
				_racingWheelMultiDetailGainString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelMultiDetailGainString()
	{
		RacingWheelMultiDetailGainString = $"{_racingWheelMultiDetailGain * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelMultiDetailGainContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelMultiDetailGainPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMultiDetailGainMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Multi output smoothing

	private float _racingWheelMultiOutputSmoothing = 0f;

	public float RacingWheelMultiOutputSmoothing
	{
		get => _racingWheelMultiOutputSmoothing;

		set
		{
			var app = App.Instance!;

			value = MathZ.Saturate( value );

			if ( value != _racingWheelMultiOutputSmoothing )
			{
				_racingWheelMultiOutputSmoothing = value;

				OnPropertyChanged();
			}

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelMultiOutputSmoothingString();
		}
	}

	private string _racingWheelMultiOutputSmoothingString = string.Empty;

	[XmlIgnore]
	public string RacingWheelMultiOutputSmoothingString
	{
		get => _racingWheelMultiOutputSmoothingString;

		set
		{
			if ( value != _racingWheelMultiOutputSmoothingString )
			{
				_racingWheelMultiOutputSmoothingString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelMultiOutputSmoothingString()
	{
		RacingWheelMultiOutputSmoothingString = $"{_racingWheelMultiOutputSmoothing * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches RacingWheelMultiOutputSmoothingContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelMultiOutputSmoothingPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelMultiOutputSmoothingMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Output minimum

	private float _racingWheelOutputMinimum = 0f;

	public float RacingWheelOutputMinimum
	{
		get => _racingWheelOutputMinimum;

		set
		{
			value = Math.Clamp( value, 0f, 0.1f );

			if ( value != _racingWheelOutputMinimum )
			{
				_racingWheelOutputMinimum = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelOutputMinimumString();
		}
	}

	private string _racingWheelOutputMinimumString = string.Empty;

	[XmlIgnore]
	public string RacingWheelOutputMinimumString
	{
		get => _racingWheelOutputMinimumString;

		set
		{
			if ( value != _racingWheelOutputMinimumString )
			{
				_racingWheelOutputMinimumString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelOutputMinimumString()
	{
		if ( _racingWheelOutputMinimum == 0f )
		{
			RacingWheelOutputMinimumString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _racingWheelOutputMinimum;

			RacingWheelOutputMinimumString = $"{_racingWheelOutputMinimum * 100f:F1}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches RacingWheelOutputMinimumContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelOutputMinimumPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelOutputMinimumMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Output maximum

	private float _racingWheelOutputMaximum = 1f;

	public float RacingWheelOutputMaximum
	{
		get => _racingWheelOutputMaximum;

		set
		{
			value = Math.Clamp( value, 0.2f, 1f );

			if ( value != _racingWheelOutputMaximum )
			{
				_racingWheelOutputMaximum = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelOutputMaximumString();
		}
	}

	private string _racingWheelOutputMaximumString = string.Empty;

	[XmlIgnore]
	public string RacingWheelOutputMaximumString
	{
		get => _racingWheelOutputMaximumString;

		set
		{
			if ( value != _racingWheelOutputMaximumString )
			{
				_racingWheelOutputMaximumString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelOutputMaximumString()
	{
		if ( _racingWheelOutputMaximum == 1f )
		{
			RacingWheelOutputMaximumString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _racingWheelOutputMaximum;

			RacingWheelOutputMaximumString = $"{_racingWheelOutputMaximum * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches RacingWheelOutputMaximumContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelOutputMaximumPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelOutputMaximumMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Output curve

	private float _racingWheelOutputCurve = 0f;

	public float RacingWheelOutputCurve
	{
		get => _racingWheelOutputCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _racingWheelOutputCurve )
			{
				_racingWheelOutputCurve = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			UpdateRacingWheelOutputCurveString();
		}
	}

	private string _racingWheelOutputCurveString = string.Empty;

	[XmlIgnore]
	public string RacingWheelOutputCurveString
	{
		get => _racingWheelOutputCurveString;

		set
		{
			if ( value != _racingWheelOutputCurveString )
			{
				_racingWheelOutputCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelOutputCurveString()
	{
		if ( _racingWheelOutputCurve == 0f )
		{
			RacingWheelOutputCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelOutputCurveString = $"{_racingWheelOutputCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches RacingWheelOutputCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelOutputCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelOutputCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Selected recording

	private string _racingWheelSelectedRecording = string.Empty;

	public string RacingWheelSelectedRecording
	{
		get => _racingWheelSelectedRecording;

		set
		{
			if ( value != _racingWheelSelectedRecording )
			{
				_racingWheelSelectedRecording = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.RacingWheel.UpdateAlgorithmPreview = true;
		}
	}

	#endregion

	#region Racing wheel - Save recording

	public ButtonMappings RacingWheelStartRecordingMappings { get; set; } = new();

	#endregion

	#region Racing wheel - LFE Recording Device

	private string _racingWheelLFERecordingDeviceName = Components.LFE.DisabledDeviceName;

	public string RacingWheelLFERecordingDeviceName
	{
		get => _racingWheelLFERecordingDeviceName;

		set
		{
			var normalizedValue = value ?? Components.LFE.DisabledDeviceName;

			if ( normalizedValue != _racingWheelLFERecordingDeviceName )
			{
				_racingWheelLFERecordingDeviceName = normalizedValue;

				OnPropertyChanged();

				var app = App.Instance!;

				app.LFE.NextCaptureDeviceName = _racingWheelLFERecordingDeviceName;
			}
		}
	}

	#endregion

	#region Racing wheel - LFE strength

	private float _racingWheelLFEStrength = 0.05f;

	public float RacingWheelLFEStrength
	{
		get => _racingWheelLFEStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelLFEStrength )
			{
				_racingWheelLFEStrength = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelLFEStrengthString();
		}
	}

	private string _racingWheelLFEStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelLFEStrengthString
	{
		get => _racingWheelLFEStrengthString;

		set
		{
			if ( value != _racingWheelLFEStrengthString )
			{
				_racingWheelLFEStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelLFEStrengthString()
	{
		if ( _racingWheelLFEStrength == 0f )
		{
			RacingWheelLFEStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelLFEStrengthString = $"{_racingWheelLFEStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches RacingWheelLFEStrengthContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings RacingWheelLFEStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelLFEStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Crash protection longitudinal g-force

	private float _racingWheelCrashProtectionLongitudalGForce = 8f;

	public float RacingWheelCrashProtectionLongitudalGForce
	{
		get => _racingWheelCrashProtectionLongitudalGForce;

		set
		{
			value = Math.Clamp( value, 2f, 20f );

			if ( value != _racingWheelCrashProtectionLongitudalGForce )
			{
				_racingWheelCrashProtectionLongitudalGForce = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelCrashProtectionLongitudalGForceString();
		}
	}

	private string _racingWheelCrashProtectionLongitudalGForceString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCrashProtectionLongitudalGForceString
	{
		get => _racingWheelCrashProtectionLongitudalGForceString;

		set
		{
			if ( value != _racingWheelCrashProtectionLongitudalGForceString )
			{
				_racingWheelCrashProtectionLongitudalGForceString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelCrashProtectionLongitudalGForceString()
	{
		if ( _racingWheelCrashProtectionLongitudalGForce == 20f )
		{
			RacingWheelCrashProtectionLongitudalGForceString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelCrashProtectionLongitudalGForceString = $"{_racingWheelCrashProtectionLongitudalGForce:F1} {DataContext.Instance.Localization[ "GForceUnits" ]}";
		}
	}

	public ContextSwitches RacingWheelCrashProtectionLongitudalGForceContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCrashProtectionLongitudalGForcePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCrashProtectionLongitudalGForceMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Crash protection lateral g-force

	private float _racingWheelCrashProtectionLateralGForce = 6f;

	public float RacingWheelCrashProtectionLateralGForce
	{
		get => _racingWheelCrashProtectionLateralGForce;

		set
		{
			value = Math.Clamp( value, 2f, 20f );

			if ( value != _racingWheelCrashProtectionLateralGForce )
			{
				_racingWheelCrashProtectionLateralGForce = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelCrashProtectionLateralGForceString();
		}
	}

	private string _racingWheelCrashProtectionLateralGForceString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCrashProtectionLateralGForceString
	{
		get => _racingWheelCrashProtectionLateralGForceString;

		set
		{
			if ( value != _racingWheelCrashProtectionLateralGForceString )
			{
				_racingWheelCrashProtectionLateralGForceString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelCrashProtectionLateralGForceString()
	{
		if ( _racingWheelCrashProtectionLateralGForce == 20f )
		{
			RacingWheelCrashProtectionLateralGForceString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelCrashProtectionLateralGForceString = $"{_racingWheelCrashProtectionLateralGForce:F1} {DataContext.Instance.Localization[ "GForceUnits" ]}";
		}
	}

	public ContextSwitches RacingWheelCrashProtectionLateralGForceContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCrashProtectionLateralGForcePlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCrashProtectionLateralGForceMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Crash protection duration

	private float _racingWheelCrashProtectionDuration = 1f;

	public float RacingWheelCrashProtectionDuration
	{
		get => _racingWheelCrashProtectionDuration;

		set
		{
			value = Math.Clamp( value, 0f, 10f );

			if ( value != _racingWheelCrashProtectionDuration )
			{
				_racingWheelCrashProtectionDuration = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelCrashProtectionDurationString();
		}
	}

	private string _racingWheelCrashProtectionDurationString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCrashProtectionDurationString
	{
		get => _racingWheelCrashProtectionDurationString;

		set
		{
			if ( value != _racingWheelCrashProtectionDurationString )
			{
				_racingWheelCrashProtectionDurationString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelCrashProtectionDurationString()
	{
		if ( _racingWheelCrashProtectionDuration == 0f )
		{
			RacingWheelCrashProtectionDurationString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelCrashProtectionDurationString = $"{_racingWheelCrashProtectionDuration:F1} {DataContext.Instance.Localization[ "SecondsUnits" ]}";
		}
	}

	public ContextSwitches RacingWheelCrashProtectionDurationContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCrashProtectionDurationPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCrashProtectionDurationMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Crash protection force reduction

	private float _racingWheelCrashProtectionForceReduction = 0.95f;

	public float RacingWheelCrashProtectionForceReduction
	{
		get => _racingWheelCrashProtectionForceReduction;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelCrashProtectionForceReduction )
			{
				_racingWheelCrashProtectionForceReduction = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelCrashProtectionForceReductionString();
		}
	}

	private string _racingWheelCrashProtectionForceReductionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCrashProtectionForceReductionString
	{
		get => _racingWheelCrashProtectionForceReductionString;

		set
		{
			if ( value != _racingWheelCrashProtectionForceReductionString )
			{
				_racingWheelCrashProtectionForceReductionString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelCrashProtectionForceReductionString()
	{
		if ( _racingWheelCrashProtectionForceReduction == 0f )
		{
			RacingWheelCrashProtectionForceReductionString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelCrashProtectionForceReductionString = $"{_racingWheelCrashProtectionForceReduction * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches RacingWheelCrashProtectionForceReductionContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCrashProtectionForceReductionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCrashProtectionForceReductionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Curb protection shock velocity

	private float _racingWheelCurbProtectionShockVelocity = 0.5f;

	public float RacingWheelCurbProtectionShockVelocity
	{
		get => _racingWheelCurbProtectionShockVelocity;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelCurbProtectionShockVelocity )
			{
				_racingWheelCurbProtectionShockVelocity = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelCurbProtectionShockVelocityString();
		}
	}

	private string _racingWheelCurbProtectionShockVelocityString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCurbProtectionShockVelocityString
	{
		get => _racingWheelCurbProtectionShockVelocityString;

		set
		{
			if ( value != _racingWheelCurbProtectionShockVelocityString )
			{
				_racingWheelCurbProtectionShockVelocityString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelCurbProtectionShockVelocityString()
	{
		if ( _racingWheelCurbProtectionShockVelocity == 0f )
		{
			RacingWheelCurbProtectionShockVelocityString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelCurbProtectionShockVelocityString = $"{_racingWheelCurbProtectionShockVelocity:F2} {DataContext.Instance.Localization[ "MPSUnits" ]}";
		}
	}

	public ContextSwitches RacingWheelCurbProtectionShockVelocityContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCurbProtectionShockVelocityPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCurbProtectionShockVelocityMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Curb protection duration

	private float _racingWheelCurbProtectionDuration = 0.1f;

	public float RacingWheelCurbProtectionDuration
	{
		get => _racingWheelCurbProtectionDuration;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelCurbProtectionDuration )
			{
				_racingWheelCurbProtectionDuration = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelCurbProtectionDurationString();
		}
	}

	private string _racingWheelCurbProtectionDurationString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCurbProtectionDurationString
	{
		get => _racingWheelCurbProtectionDurationString;

		set
		{
			if ( value != _racingWheelCurbProtectionDurationString )
			{
				_racingWheelCurbProtectionDurationString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelCurbProtectionDurationString()
	{
		if ( _racingWheelCurbProtectionDuration == 0f )
		{
			RacingWheelCurbProtectionDurationString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelCurbProtectionDurationString = $"{_racingWheelCurbProtectionDuration:F2} {DataContext.Instance.Localization[ "SecondsUnits" ]}";
		}
	}

	public ContextSwitches RacingWheelCurbProtectionDurationContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCurbProtectionDurationPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCurbProtectionDurationMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Curb protection force reduction

	private float _racingWheelCurbProtectionForceReduction = 0.75f;

	public float RacingWheelCurbProtectionForceReduction
	{
		get => _racingWheelCurbProtectionForceReduction;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelCurbProtectionForceReduction )
			{
				_racingWheelCurbProtectionForceReduction = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelCurbProtectionForceReductionString();
		}
	}

	private string _racingWheelCurbProtectionForceReductionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelCurbProtectionForceReductionString
	{
		get => _racingWheelCurbProtectionForceReductionString;

		set
		{
			if ( value != _racingWheelCurbProtectionForceReductionString )
			{
				_racingWheelCurbProtectionForceReductionString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelCurbProtectionForceReductionString()
	{
		if ( _racingWheelCurbProtectionForceReduction == 0f )
		{
			RacingWheelCurbProtectionForceReductionString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelCurbProtectionForceReductionString = $"{_racingWheelCurbProtectionForceReduction * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches RacingWheelCurbProtectionForceReductionContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings RacingWheelCurbProtectionForceReductionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelCurbProtectionForceReductionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Parked strength

	private float _racingWheelParkedStrength = 0.1f;

	public float RacingWheelParkedStrength
	{
		get => _racingWheelParkedStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelParkedStrength )
			{
				_racingWheelParkedStrength = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelParkedStrengthString();
		}
	}

	private string _racingWheelParkedStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelParkedStrengthString
	{
		get => _racingWheelParkedStrengthString;

		set
		{
			if ( value != _racingWheelParkedStrengthString )
			{
				_racingWheelParkedStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelParkedStrengthString()
	{
		if ( _racingWheelParkedStrength == 1f )
		{
			RacingWheelParkedStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelParkedStrengthString = $"{_racingWheelParkedStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches RacingWheelParkedStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelParkedStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelParkedStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Parked friction

	private float _racingWheelParkedFriction = 0f;

	public float RacingWheelParkedFriction
	{
		get => _racingWheelParkedFriction;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelParkedFriction )
			{
				_racingWheelParkedFriction = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelParkedFrictionString();
		}
	}

	private string _racingWheelParkedFrictionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelParkedFrictionString
	{
		get => _racingWheelParkedFrictionString;

		set
		{
			if ( value != _racingWheelParkedFrictionString )
			{
				_racingWheelParkedFrictionString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelParkedFrictionString()
	{
		if ( _racingWheelParkedFriction == 0f )
		{
			RacingWheelParkedFrictionString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelParkedFrictionString = $"{_racingWheelParkedFriction * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches RacingWheelParkedFrictionContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelParkedFrictionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelParkedFrictionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Soft lock strength

	private float _racingWheelSoftLockStrength = 0.25f;

	public float RacingWheelSoftLockStrength
	{
		get => _racingWheelSoftLockStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelSoftLockStrength )
			{
				_racingWheelSoftLockStrength = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelSoftLockStrengthString();
		}
	}

	private string _racingWheelSoftLockStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelSoftLockStrengthString
	{
		get => _racingWheelSoftLockStrengthString;

		set
		{
			if ( value != _racingWheelSoftLockStrengthString )
			{
				_racingWheelSoftLockStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelSoftLockStrengthString()
	{
		if ( _racingWheelSoftLockStrength == 0f )
		{
			RacingWheelSoftLockStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelSoftLockStrengthString = $"{_racingWheelSoftLockStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches RacingWheelSoftLockStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelSoftLockStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelSoftLockStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Friction

	private float _racingWheelFriction = 0f;

	public float RacingWheelFriction
	{
		get => _racingWheelFriction;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelFriction )
			{
				_racingWheelFriction = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelFrictionString();
		}
	}

	private string _racingWheelFrictionString = string.Empty;

	[XmlIgnore]
	public string RacingWheelFrictionString
	{
		get => _racingWheelFrictionString;

		set
		{
			if ( value != _racingWheelFrictionString )
			{
				_racingWheelFrictionString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelFrictionString()
	{
		if ( _racingWheelFriction == 0f )
		{
			RacingWheelFrictionString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelFrictionString = $"{_racingWheelFriction * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches RacingWheelFrictionContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelFrictionPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelFrictionMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Wheel centering strength

	private float _racingWheelWheelCenteringStrength = 0.75f;

	public float RacingWheelWheelCenteringStrength
	{
		get => _racingWheelWheelCenteringStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelWheelCenteringStrength )
			{
				_racingWheelWheelCenteringStrength = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelWheelCenteringStrengthString();
		}
	}

	private string _racingWheelWheelCenteringStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelWheelCenteringStrengthString
	{
		get => _racingWheelWheelCenteringStrengthString;

		set
		{
			if ( value != _racingWheelWheelCenteringStrengthString )
			{
				_racingWheelWheelCenteringStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelWheelCenteringStrengthString()
	{
		if ( _racingWheelWheelCenteringStrength == 0f )
		{
			RacingWheelWheelCenteringStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			RacingWheelWheelCenteringStrengthString = $"{_racingWheelWheelCenteringStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches RacingWheelWheelCenteringStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelWheelCenteringStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelWheelCenteringStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Gear change vibrate strength

	private float _racingWheelGearChangeVibrateStrength = 0.0f;

	public float RacingWheelGearChangeVibrateStrength
	{
		get => _racingWheelGearChangeVibrateStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelGearChangeVibrateStrength )
			{
				_racingWheelGearChangeVibrateStrength = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelGearChangeVibrateStrengthString();
		}
	}

	private string _racingWheelGearChangeVibrateStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelGearChangeVibrateStrengthString
	{
		get => _racingWheelGearChangeVibrateStrengthString;

		set
		{
			if ( value != _racingWheelGearChangeVibrateStrengthString )
			{
				_racingWheelGearChangeVibrateStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelGearChangeVibrateStrengthString()
	{
		if ( _racingWheelGearChangeVibrateStrength == 0f )
		{
			RacingWheelGearChangeVibrateStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _racingWheelGearChangeVibrateStrength;

			RacingWheelGearChangeVibrateStrengthString = $"{_racingWheelGearChangeVibrateStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches RacingWheelGearChangeVibrateStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelGearChangeVibrateStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelGearChangeVibrateStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - ABS vibrate strength

	private float _racingWheelABSVibrateStrength = 0.0f;

	public float RacingWheelABSVibrateStrength
	{
		get => _racingWheelABSVibrateStrength;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _racingWheelABSVibrateStrength )
			{
				_racingWheelABSVibrateStrength = value;

				OnPropertyChanged();
			}

			UpdateRacingWheelABSVibrateStrengthString();
		}
	}

	private string _racingWheelABSVibrateStrengthString = string.Empty;

	[XmlIgnore]
	public string RacingWheelABSVibrateStrengthString
	{
		get => _racingWheelABSVibrateStrengthString;

		set
		{
			if ( value != _racingWheelABSVibrateStrengthString )
			{
				_racingWheelABSVibrateStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateRacingWheelABSVibrateStrengthString()
	{
		if ( _racingWheelABSVibrateStrength == 0f )
		{
			RacingWheelABSVibrateStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _racingWheelABSVibrateStrength;

			RacingWheelABSVibrateStrengthString = $"{_racingWheelABSVibrateStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches RacingWheelABSVibrateStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings RacingWheelABSVibrateStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings RacingWheelABSVibrateStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Racing wheel - Send chat messages

	private bool _racingWheelSendChatMessages = true;

	public bool RacingWheelSendChatMessages
	{
		get => _racingWheelSendChatMessages;

		set
		{
			if ( value != _racingWheelSendChatMessages )
			{
				_racingWheelSendChatMessages = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Racing wheel - Input mapped setting update enabled

	private bool _racingWheelInputMappedSettingUpdateEnabled = true;

	public bool RacingWheelInputMappedSettingUpdateEnabled
	{
		get => _racingWheelInputMappedSettingUpdateEnabled;

		set
		{
			if ( value != _racingWheelInputMappedSettingUpdateEnabled )
			{
				_racingWheelInputMappedSettingUpdateEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Racing wheel - Crash protection messages enabled

	private bool _racingWheelCrashProtectionMessagesEnabled = false;

	public bool RacingWheelCrashProtectionMessagesEnabled
	{
		get => _racingWheelCrashProtectionMessagesEnabled;

		set
		{
			if ( value != _racingWheelCrashProtectionMessagesEnabled )
			{
				_racingWheelCrashProtectionMessagesEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Racing wheel - Curb protection messages enabled

	private bool _racingWheelCurbProtectionMessagesEnabled = false;

	public bool RacingWheelCurbProtectionMessagesEnabled
	{
		get => _racingWheelCurbProtectionMessagesEnabled;

		set
		{
			if ( value != _racingWheelCurbProtectionMessagesEnabled )
			{
				_racingWheelCurbProtectionMessagesEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Racing wheel - Center wheel while racing

	private bool _racingWheelCenterWheelWhileRacing = false;

	public bool RacingWheelCenterWheelWhileRacing
	{
		get => _racingWheelCenterWheelWhileRacing;

		set
		{
			if ( value != _racingWheelCenterWheelWhileRacing )
			{
				_racingWheelCenterWheelWhileRacing = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCenterWheelWhileRacingContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Center wheel while parked

	private bool _racingWheelCenterWheelWhileParked = true;

	public bool RacingWheelCenterWheelWhileParked
	{
		get => _racingWheelCenterWheelWhileParked;

		set
		{
			if ( value != _racingWheelCenterWheelWhileParked )
			{
				_racingWheelCenterWheelWhileParked = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelCenterWheelWhileParkedContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Fade enabled

	private bool _racingWheelFadeEnabled = true;

	public bool RacingWheelFadeEnabled
	{
		get => _racingWheelFadeEnabled;

		set
		{
			if ( value != _racingWheelFadeEnabled )
			{
				_racingWheelFadeEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches RacingWheelFadeEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Racing wheel - Allow super strength

	private bool _racingWheelAllowSuperStrength = false;

	public bool RacingWheelAllowSuperStrength
	{
		get => _racingWheelAllowSuperStrength;

		set
		{
			if ( value != _racingWheelAllowSuperStrength )
			{
				_racingWheelAllowSuperStrength = value;

				OnPropertyChanged();
			}

			UpdateRelatedRacingWheelSettings( nameof( RacingWheelMaxForce ) );
			UpdateRelatedRacingWheelSettings( nameof( RacingWheelStrength ) );
		}
	}

	#endregion

	#region Racing wheel - Always enable FFB

	private bool _racingWheelAlwaysEnableFFB = false;

	public bool RacingWheelAlwaysEnableFFB
	{
		get => _racingWheelAlwaysEnableFFB;

		set
		{
			if ( value != _racingWheelAlwaysEnableFFB )
			{
				_racingWheelAlwaysEnableFFB = value;

				OnPropertyChanged();
			}

			_racingWheelPage.UpdateSteeringDeviceSection();
		}
	}

	#endregion

	#region Racing wheel - Simple mode

	private bool _racingWheelSimpleModeEnabled = false;

	public bool RacingWheelSimpleModeEnabled
	{
		get => _racingWheelSimpleModeEnabled;

		set
		{
			if ( value != _racingWheelSimpleModeEnabled )
			{
				_racingWheelSimpleModeEnabled = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.MainWindow.UpdateRacingWheelSimpleMode();
		}
	}

	#endregion

	#region Steering effects - Calibration file name

	private string _steeringEffectsCalibrationFileName = string.Empty;

	public string SteeringEffectsCalibrationFileName
	{
		get => _steeringEffectsCalibrationFileName;

		set
		{
			var app = App.Instance!;

			if ( !app.SettingsFile.PauseSerialization )
			{
				if ( value == null )
				{
					value = string.Empty;
				}

				if ( value != _steeringEffectsCalibrationFileName )
				{
					_steeringEffectsCalibrationFileName = value;

					OnPropertyChanged();

					app.SteeringEffects.LoadCalibration();

					MainWindow._steeringEffectsPage.CalibrationFileNameChanged( _steeringEffectsCalibrationFileName != string.Empty );
				}
			}
		}
	}

	public ContextSwitches SteeringEffectsCalibrationFileNameContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Steering effects - Understeer enabled

	private bool _steeringEffectsUndersteerEnabled = false;

	public bool SteeringEffectsUndersteerEnabled
	{
		get => _steeringEffectsUndersteerEnabled;

		set
		{
			if ( value != _steeringEffectsUndersteerEnabled )
			{
				_steeringEffectsUndersteerEnabled = value;

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerEnabledContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Steering effects - Understeer minimum threshold

	private float _steeringEffectsUndersteerMinimumThreshold = 0.05f;

	public float SteeringEffectsUndersteerMinimumThreshold
	{
		get => _steeringEffectsUndersteerMinimumThreshold;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsUndersteerMinimumThreshold )
			{
				_steeringEffectsUndersteerMinimumThreshold = value;

				SteeringEffectsUndersteerMaximumThreshold = MathF.Max( SteeringEffectsUndersteerMaximumThreshold, _steeringEffectsUndersteerMinimumThreshold );

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}

			UpdateSteeringEffectsUndersteerMinimumThresholdString();
		}
	}

	private string _steeringEffectsUndersteerMinimumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerMinimumThresholdString
	{
		get => _steeringEffectsUndersteerMinimumThresholdString;

		set
		{
			if ( value != _steeringEffectsUndersteerMinimumThresholdString )
			{
				_steeringEffectsUndersteerMinimumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerMinimumThresholdString()
	{
		SteeringEffectsUndersteerMinimumThresholdString = $"{_steeringEffectsUndersteerMinimumThreshold:F2}{DataContext.Instance.Localization[ "DegreesPerSecond" ]}";
	}

	public ContextSwitches SteeringEffectsUndersteerMinimumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerMinimumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerMinimumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer maximum threshold

	private float _steeringEffectsUndersteerMaximumThreshold = 0.15f;

	public float SteeringEffectsUndersteerMaximumThreshold
	{
		get => _steeringEffectsUndersteerMaximumThreshold;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsUndersteerMaximumThreshold )
			{
				_steeringEffectsUndersteerMaximumThreshold = value;

				SteeringEffectsUndersteerMinimumThreshold = MathF.Min( SteeringEffectsUndersteerMinimumThreshold, _steeringEffectsUndersteerMaximumThreshold );

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}

			UpdateSteeringEffectsUndersteerMaximumThresholdString();
		}
	}

	private string _steeringEffectsUndersteerMaximumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerMaximumThresholdString
	{
		get => _steeringEffectsUndersteerMaximumThresholdString;

		set
		{
			if ( value != _steeringEffectsUndersteerMaximumThresholdString )
			{
				_steeringEffectsUndersteerMaximumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerMaximumThresholdString()
	{
		SteeringEffectsUndersteerMaximumThresholdString = $"{_steeringEffectsUndersteerMaximumThreshold:F2}{DataContext.Instance.Localization[ "DegreesPerSecond" ]}";
	}

	public ContextSwitches SteeringEffectsUndersteerMaximumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerMaximumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerMaximumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel vibration pattern

	private RacingWheel.VibrationPattern _steeringEffectsUndersteerWheelVibrationPattern = RacingWheel.VibrationPattern.SineWave;

	public RacingWheel.VibrationPattern SteeringEffectsUndersteerWheelVibrationPattern
	{
		get => _steeringEffectsUndersteerWheelVibrationPattern;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelVibrationPattern )
			{
				_steeringEffectsUndersteerWheelVibrationPattern = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelVibrationPatternContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Understeer wheel vibration strength

	private float _steeringEffectsUndersteerWheelVibrationStrength = 0.1f;

	public float SteeringEffectsUndersteerWheelVibrationStrength
	{
		get => _steeringEffectsUndersteerWheelVibrationStrength;

		set
		{
			value = Math.Clamp( value, 0f, 0.3f );

			if ( value != _steeringEffectsUndersteerWheelVibrationStrength )
			{
				_steeringEffectsUndersteerWheelVibrationStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerWheelVibrationStrengthString();
		}
	}

	private string _steeringEffectsUndersteerWheelVibrationStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelVibrationStrengthString
	{
		get => _steeringEffectsUndersteerWheelVibrationStrengthString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelVibrationStrengthString )
			{
				_steeringEffectsUndersteerWheelVibrationStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerWheelVibrationStrengthString()
	{
		if ( _steeringEffectsUndersteerWheelVibrationStrength == 0f )
		{
			SteeringEffectsUndersteerWheelVibrationStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsUndersteerWheelVibrationStrength;

			SteeringEffectsUndersteerWheelVibrationStrengthString = $"{_steeringEffectsUndersteerWheelVibrationStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelVibrationStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel vibration minimum frequency

	private float _steeringEffectsUndersteerWheelVibrationMinimumFrequency = 15f;

	public float SteeringEffectsUndersteerWheelVibrationMinimumFrequency
	{
		get => _steeringEffectsUndersteerWheelVibrationMinimumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsUndersteerWheelVibrationMinimumFrequency )
			{
				_steeringEffectsUndersteerWheelVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerWheelVibrationMinimumFrequencyString();
		}
	}

	private string _steeringEffectsUndersteerWheelVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelVibrationMinimumFrequencyString
	{
		get => _steeringEffectsUndersteerWheelVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelVibrationMinimumFrequencyString )
			{
				_steeringEffectsUndersteerWheelVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerWheelVibrationMinimumFrequencyString()
	{
		SteeringEffectsUndersteerWheelVibrationMinimumFrequencyString = $"{_steeringEffectsUndersteerWheelVibrationMinimumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
	}

	public ContextSwitches SteeringEffectsUndersteerWheelVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel vibration maximum frequency

	private float _steeringEffectsUndersteerWheelVibrationMaximumFrequency = 50f;

	public float SteeringEffectsUndersteerWheelVibrationMaximumFrequency
	{
		get => _steeringEffectsUndersteerWheelVibrationMaximumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsUndersteerWheelVibrationMaximumFrequency )
			{
				_steeringEffectsUndersteerWheelVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerWheelVibrationMaximumFrequencyString();
		}
	}

	private string _steeringEffectsUndersteerWheelVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelVibrationMaximumFrequencyString
	{
		get => _steeringEffectsUndersteerWheelVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelVibrationMaximumFrequencyString )
			{
				_steeringEffectsUndersteerWheelVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerWheelVibrationMaximumFrequencyString()
	{
		SteeringEffectsUndersteerWheelVibrationMaximumFrequencyString = $"{_steeringEffectsUndersteerWheelVibrationMaximumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
	}

	public ContextSwitches SteeringEffectsUndersteerWheelVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel vibration curve

	private float _steeringEffectsUndersteerWheelVibrationCurve = 0.25f;

	public float SteeringEffectsUndersteerWheelVibrationCurve
	{
		get => _steeringEffectsUndersteerWheelVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsUndersteerWheelVibrationCurve )
			{
				_steeringEffectsUndersteerWheelVibrationCurve = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerWheelVibrationCurveString();
		}
	}

	private string _steeringEffectsUndersteerWheelVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelVibrationCurveString
	{
		get => _steeringEffectsUndersteerWheelVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelVibrationCurveString )
			{
				_steeringEffectsUndersteerWheelVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerWheelVibrationCurveString()
	{
		if ( _steeringEffectsUndersteerWheelVibrationCurve == 0f )
		{
			SteeringEffectsUndersteerWheelVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SteeringEffectsUndersteerWheelVibrationCurveString = $"{_steeringEffectsUndersteerWheelVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelVibrationCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel constant force direction

	private RacingWheel.ConstantForceDirection _steeringEffectsUndersteerWheelConstantForceDirection = RacingWheel.ConstantForceDirection.None;

	public RacingWheel.ConstantForceDirection SteeringEffectsUndersteerWheelConstantForceDirection
	{
		get => _steeringEffectsUndersteerWheelConstantForceDirection;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelConstantForceDirection )
			{
				_steeringEffectsUndersteerWheelConstantForceDirection = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelConstantForceDirectionContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Understeer wheel constant force strength

	private float _steeringEffectsUndersteerWheelConstantForceStrength = 0.1f;

	public float SteeringEffectsUndersteerWheelConstantForceStrength
	{
		get => _steeringEffectsUndersteerWheelConstantForceStrength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _steeringEffectsUndersteerWheelConstantForceStrength )
			{
				_steeringEffectsUndersteerWheelConstantForceStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerWheelConstantForceStrengthString();
		}
	}

	private string _steeringEffectsUndersteerWheelConstantForceStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelConstantForceStrengthString
	{
		get => _steeringEffectsUndersteerWheelConstantForceStrengthString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelConstantForceStrengthString )
			{
				_steeringEffectsUndersteerWheelConstantForceStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerWheelConstantForceStrengthString()
	{
		if ( _steeringEffectsUndersteerWheelConstantForceStrength == 0f )
		{
			SteeringEffectsUndersteerWheelConstantForceStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsUndersteerWheelConstantForceStrength;

			SteeringEffectsUndersteerWheelConstantForceStrengthString = $"{_steeringEffectsUndersteerWheelConstantForceStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}


	public ContextSwitches SteeringEffectsUndersteerWheelConstantForceStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelConstantForceStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelConstantForceStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer wheel constant force curve

	private float _steeringEffectsUndersteerWheelConstantForceCurve = 0f;

	public float SteeringEffectsUndersteerWheelConstantForceCurve
	{
		get => _steeringEffectsUndersteerWheelConstantForceCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsUndersteerWheelConstantForceCurve )
			{
				_steeringEffectsUndersteerWheelConstantForceCurve = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerWheelConstantForceCurveString();
		}
	}

	private string _steeringEffectsUndersteerWheelConstantForceCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerWheelConstantForceCurveString
	{
		get => _steeringEffectsUndersteerWheelConstantForceCurveString;

		set
		{
			if ( value != _steeringEffectsUndersteerWheelConstantForceCurveString )
			{
				_steeringEffectsUndersteerWheelConstantForceCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerWheelConstantForceCurveString()
	{
		if ( _steeringEffectsUndersteerWheelConstantForceCurve == 0f )
		{
			SteeringEffectsUndersteerWheelConstantForceCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SteeringEffectsUndersteerWheelConstantForceCurveString = $"{_steeringEffectsUndersteerWheelConstantForceCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SteeringEffectsUndersteerWheelConstantForceCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerWheelConstantForceCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerWheelConstantForceCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer pedal vibration minimum frequency

	private float _steeringEffectsUndersteerPedalVibrationMinimumFrequency = 0.1f;

	public float SteeringEffectsUndersteerPedalVibrationMinimumFrequency
	{
		get => _steeringEffectsUndersteerPedalVibrationMinimumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsUndersteerPedalVibrationMinimumFrequency )
			{
				_steeringEffectsUndersteerPedalVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerPedalVibrationMinimumFrequencyString();
		}
	}

	private string _steeringEffectsUndersteerPedalVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerPedalVibrationMinimumFrequencyString
	{
		get => _steeringEffectsUndersteerPedalVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsUndersteerPedalVibrationMinimumFrequencyString )
			{
				_steeringEffectsUndersteerPedalVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerPedalVibrationMinimumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsUndersteerPedalVibrationMinimumFrequency ) );

		SteeringEffectsUndersteerPedalVibrationMinimumFrequencyString = $"{_steeringEffectsUndersteerPedalVibrationMinimumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsUndersteerPedalVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer pedal vibration maximum frequency

	private float _steeringEffectsUndersteerPedalVibrationMaximumFrequency = 1f;

	public float SteeringEffectsUndersteerPedalVibrationMaximumFrequency
	{
		get => _steeringEffectsUndersteerPedalVibrationMaximumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsUndersteerPedalVibrationMaximumFrequency )
			{
				_steeringEffectsUndersteerPedalVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerPedalVibrationMaximumFrequencyString();
		}
	}

	private string _steeringEffectsUndersteerPedalVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerPedalVibrationMaximumFrequencyString
	{
		get => _steeringEffectsUndersteerPedalVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsUndersteerPedalVibrationMaximumFrequencyString )
			{
				_steeringEffectsUndersteerPedalVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerPedalVibrationMaximumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsUndersteerPedalVibrationMaximumFrequency ) );

		SteeringEffectsUndersteerPedalVibrationMaximumFrequencyString = $"{_steeringEffectsUndersteerPedalVibrationMaximumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsUndersteerPedalVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Understeer pedal vibration curve

	private float _steeringEffectsUndersteerPedalVibrationCurve = 0f;

	public float SteeringEffectsUndersteerPedalVibrationCurve
	{
		get => _steeringEffectsUndersteerPedalVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsUndersteerPedalVibrationCurve )
			{
				_steeringEffectsUndersteerPedalVibrationCurve = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsUndersteerPedalVibrationCurveString();
		}
	}

	private string _steeringEffectsUndersteerPedalVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsUndersteerPedalVibrationCurveString
	{
		get => _steeringEffectsUndersteerPedalVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsUndersteerPedalVibrationCurveString )
			{
				_steeringEffectsUndersteerPedalVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsUndersteerPedalVibrationCurveString()
	{
		if ( _steeringEffectsUndersteerPedalVibrationCurve == 0f )
		{
			SteeringEffectsUndersteerPedalVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SteeringEffectsUndersteerPedalVibrationCurveString = $"{_steeringEffectsUndersteerPedalVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SteeringEffectsUndersteerPedalVibrationCurveContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsUndersteerPedalVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer enabled

	private bool _steeringEffectsOversteerEnabled = false;

	public bool SteeringEffectsOversteerEnabled
	{
		get => _steeringEffectsOversteerEnabled;

		set
		{
			if ( value != _steeringEffectsOversteerEnabled )
			{
				_steeringEffectsOversteerEnabled = value;

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerEnabledContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Steering effects - Oversteer minimum threshold

	private float _steeringEffectsOversteerMinimumThreshold = 0f;

	public float SteeringEffectsOversteerMinimumThreshold
	{
		get => _steeringEffectsOversteerMinimumThreshold;

		set
		{
			value = Math.Clamp( value, -1f, 2f );

			if ( value != _steeringEffectsOversteerMinimumThreshold )
			{
				_steeringEffectsOversteerMinimumThreshold = value;

				SteeringEffectsOversteerMaximumThreshold = MathF.Max( SteeringEffectsOversteerMaximumThreshold, _steeringEffectsOversteerMinimumThreshold );

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}

			UpdateSteeringEffectsOversteerMinimumThresholdString();
		}
	}

	private string _steeringEffectsOversteerMinimumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerMinimumThresholdString
	{
		get => _steeringEffectsOversteerMinimumThresholdString;

		set
		{
			if ( value != _steeringEffectsOversteerMinimumThresholdString )
			{
				_steeringEffectsOversteerMinimumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerMinimumThresholdString()
	{
		SteeringEffectsOversteerMinimumThresholdString = $"{_steeringEffectsOversteerMinimumThreshold:F2}{DataContext.Instance.Localization[ "DegreesPerSecond" ]}";
	}

	public ContextSwitches SteeringEffectsOversteerMinimumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsOversteerMinimumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerMinimumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer maximum threshold

	private float _steeringEffectsOversteerMaximumThreshold = 0.5f;

	public float SteeringEffectsOversteerMaximumThreshold
	{
		get => _steeringEffectsOversteerMaximumThreshold;

		set
		{
			value = Math.Clamp( value, -1f, 2f );

			if ( value != _steeringEffectsOversteerMaximumThreshold )
			{
				_steeringEffectsOversteerMaximumThreshold = value;

				SteeringEffectsOversteerMinimumThreshold = MathF.Min( SteeringEffectsOversteerMinimumThreshold, _steeringEffectsOversteerMaximumThreshold );

				OnPropertyChanged();

				App.Instance!.SteeringEffects.RedrawCalibrationGraph = true;
			}

			UpdateSteeringEffectsOversteerMaximumThresholdString();
		}
	}

	private string _steeringEffectsOversteerMaximumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerMaximumThresholdString
	{
		get => _steeringEffectsOversteerMaximumThresholdString;

		set
		{
			if ( value != _steeringEffectsOversteerMaximumThresholdString )
			{
				_steeringEffectsOversteerMaximumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerMaximumThresholdString()
	{
		SteeringEffectsOversteerMaximumThresholdString = $"{_steeringEffectsOversteerMaximumThreshold:F2}{DataContext.Instance.Localization[ "DegreesPerSecond" ]}";
	}

	public ContextSwitches SteeringEffectsOversteerMaximumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsOversteerMaximumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerMaximumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel vibration pattern

	private RacingWheel.VibrationPattern _steeringEffectsOversteerWheelVibrationPattern = RacingWheel.VibrationPattern.None;

	public RacingWheel.VibrationPattern SteeringEffectsOversteerWheelVibrationPattern
	{
		get => _steeringEffectsOversteerWheelVibrationPattern;

		set
		{
			if ( value != _steeringEffectsOversteerWheelVibrationPattern )
			{
				_steeringEffectsOversteerWheelVibrationPattern = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelVibrationPatternContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Oversteer wheel vibration strength

	private float _steeringEffectsOversteerWheelVibrationStrength = 0.1f;

	public float SteeringEffectsOversteerWheelVibrationStrength
	{
		get => _steeringEffectsOversteerWheelVibrationStrength;

		set
		{
			value = Math.Clamp( value, 0f, 0.3f );

			if ( value != _steeringEffectsOversteerWheelVibrationStrength )
			{
				_steeringEffectsOversteerWheelVibrationStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerWheelVibrationStrengthString();
		}
	}

	private string _steeringEffectsOversteerWheelVibrationStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelVibrationStrengthString
	{
		get => _steeringEffectsOversteerWheelVibrationStrengthString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelVibrationStrengthString )
			{
				_steeringEffectsOversteerWheelVibrationStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerWheelVibrationStrengthString()
	{
		if ( _steeringEffectsOversteerWheelVibrationStrength == 0f )
		{
			SteeringEffectsOversteerWheelVibrationStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsOversteerWheelVibrationStrength;

			SteeringEffectsOversteerWheelVibrationStrengthString = $"{_steeringEffectsOversteerWheelVibrationStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelVibrationStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelVibrationStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelVibrationStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel vibration minimum frequency

	private float _steeringEffectsOversteerWheelVibrationMinimumFrequency = 15f;

	public float SteeringEffectsOversteerWheelVibrationMinimumFrequency
	{
		get => _steeringEffectsOversteerWheelVibrationMinimumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsOversteerWheelVibrationMinimumFrequency )
			{
				_steeringEffectsOversteerWheelVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerWheelVibrationMinimumFrequencyString();
		}
	}

	private string _steeringEffectsOversteerWheelVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelVibrationMinimumFrequencyString
	{
		get => _steeringEffectsOversteerWheelVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelVibrationMinimumFrequencyString )
			{
				_steeringEffectsOversteerWheelVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerWheelVibrationMinimumFrequencyString()
	{
		SteeringEffectsOversteerWheelVibrationMinimumFrequencyString = $"{_steeringEffectsOversteerWheelVibrationMinimumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
	}

	public ContextSwitches SteeringEffectsOversteerWheelVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel vibration maximum frequency

	private float _steeringEffectsOversteerWheelVibrationMaximumFrequency = 50f;

	public float SteeringEffectsOversteerWheelVibrationMaximumFrequency
	{
		get => _steeringEffectsOversteerWheelVibrationMaximumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsOversteerWheelVibrationMaximumFrequency )
			{
				_steeringEffectsOversteerWheelVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerWheelVibrationMaximumFrequencyString();
		}
	}

	private string _steeringEffectsOversteerWheelVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelVibrationMaximumFrequencyString
	{
		get => _steeringEffectsOversteerWheelVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelVibrationMaximumFrequencyString )
			{
				_steeringEffectsOversteerWheelVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerWheelVibrationMaximumFrequencyString()
	{
		SteeringEffectsOversteerWheelVibrationMaximumFrequencyString = $"{_steeringEffectsOversteerWheelVibrationMaximumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
	}

	public ContextSwitches SteeringEffectsOversteerWheelVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel vibration curve

	private float _steeringEffectsOversteerWheelVibrationCurve = 0.25f;

	public float SteeringEffectsOversteerWheelVibrationCurve
	{
		get => _steeringEffectsOversteerWheelVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsOversteerWheelVibrationCurve )
			{
				_steeringEffectsOversteerWheelVibrationCurve = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerWheelVibrationCurveString();
		}
	}

	private string _steeringEffectsOversteerWheelVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelVibrationCurveString
	{
		get => _steeringEffectsOversteerWheelVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelVibrationCurveString )
			{
				_steeringEffectsOversteerWheelVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerWheelVibrationCurveString()
	{
		if ( _steeringEffectsOversteerWheelVibrationCurve == 0f )
		{
			SteeringEffectsOversteerWheelVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SteeringEffectsOversteerWheelVibrationCurveString = $"{_steeringEffectsOversteerWheelVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelVibrationCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel constant force direction

	private RacingWheel.ConstantForceDirection _steeringEffectsOversteerWheelConstantForceDirection = RacingWheel.ConstantForceDirection.IncreaseForce;

	public RacingWheel.ConstantForceDirection SteeringEffectsOversteerWheelConstantForceDirection
	{
		get => _steeringEffectsOversteerWheelConstantForceDirection;

		set
		{
			if ( value != _steeringEffectsOversteerWheelConstantForceDirection )
			{
				_steeringEffectsOversteerWheelConstantForceDirection = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelConstantForceDirectionContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Oversteer wheel constant force strength

	private float _steeringEffectsOversteerWheelConstantForceStrength = 0.1f;

	public float SteeringEffectsOversteerWheelConstantForceStrength
	{
		get => _steeringEffectsOversteerWheelConstantForceStrength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _steeringEffectsOversteerWheelConstantForceStrength )
			{
				_steeringEffectsOversteerWheelConstantForceStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerWheelConstantForceStrengthString();
		}
	}

	private string _steeringEffectsOversteerWheelConstantForceStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelConstantForceStrengthString
	{
		get => _steeringEffectsOversteerWheelConstantForceStrengthString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelConstantForceStrengthString )
			{
				_steeringEffectsOversteerWheelConstantForceStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerWheelConstantForceStrengthString()
	{
		if ( _steeringEffectsOversteerWheelConstantForceStrength == 0f )
		{
			SteeringEffectsOversteerWheelConstantForceStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsOversteerWheelConstantForceStrength;

			SteeringEffectsOversteerWheelConstantForceStrengthString = $"{_steeringEffectsOversteerWheelConstantForceStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelConstantForceStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelConstantForceStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelConstantForceStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer wheel constant force curve

	private float _steeringEffectsOversteerWheelConstantForceCurve = 0f;

	public float SteeringEffectsOversteerWheelConstantForceCurve
	{
		get => _steeringEffectsOversteerWheelConstantForceCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsOversteerWheelConstantForceCurve )
			{
				_steeringEffectsOversteerWheelConstantForceCurve = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerWheelConstantForceCurveString();
		}
	}

	private string _steeringEffectsOversteerWheelConstantForceCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerWheelConstantForceCurveString
	{
		get => _steeringEffectsOversteerWheelConstantForceCurveString;

		set
		{
			if ( value != _steeringEffectsOversteerWheelConstantForceCurveString )
			{
				_steeringEffectsOversteerWheelConstantForceCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerWheelConstantForceCurveString()
	{
		if ( _steeringEffectsOversteerWheelConstantForceCurve == 0f )
		{
			SteeringEffectsOversteerWheelConstantForceCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SteeringEffectsOversteerWheelConstantForceCurveString = $"{_steeringEffectsOversteerWheelConstantForceCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SteeringEffectsOversteerWheelConstantForceCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsOversteerWheelConstantForceCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerWheelConstantForceCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer pedal vibration minimum frequency

	private float _steeringEffectsOversteerPedalVibrationMinimumFrequency = 0.1f;

	public float SteeringEffectsOversteerPedalVibrationMinimumFrequency
	{
		get => _steeringEffectsOversteerPedalVibrationMinimumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsOversteerPedalVibrationMinimumFrequency )
			{
				_steeringEffectsOversteerPedalVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerPedalVibrationMinimumFrequencyString();
		}
	}

	private string _steeringEffectsOversteerPedalVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerPedalVibrationMinimumFrequencyString
	{
		get => _steeringEffectsOversteerPedalVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsOversteerPedalVibrationMinimumFrequencyString )
			{
				_steeringEffectsOversteerPedalVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerPedalVibrationMinimumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsOversteerPedalVibrationMinimumFrequency ) );

		SteeringEffectsOversteerPedalVibrationMinimumFrequencyString = $"{_steeringEffectsOversteerPedalVibrationMinimumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsOversteerPedalVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsOversteerPedalVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerPedalVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer pedal vibration maximum frequency

	private float _steeringEffectsOversteerPedalVibrationMaximumFrequency = 1f;

	public float SteeringEffectsOversteerPedalVibrationMaximumFrequency
	{
		get => _steeringEffectsOversteerPedalVibrationMaximumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsOversteerPedalVibrationMaximumFrequency )
			{
				_steeringEffectsOversteerPedalVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerPedalVibrationMaximumFrequencyString();
		}
	}

	private string _steeringEffectsOversteerPedalVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerPedalVibrationMaximumFrequencyString
	{
		get => _steeringEffectsOversteerPedalVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsOversteerPedalVibrationMaximumFrequencyString )
			{
				_steeringEffectsOversteerPedalVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerPedalVibrationMaximumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsOversteerPedalVibrationMaximumFrequency ) );

		SteeringEffectsOversteerPedalVibrationMaximumFrequencyString = $"{_steeringEffectsOversteerPedalVibrationMaximumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsOversteerPedalVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsOversteerPedalVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerPedalVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Oversteer pedal vibration curve

	private float _steeringEffectsOversteerPedalVibrationCurve = 0f;

	public float SteeringEffectsOversteerPedalVibrationCurve
	{
		get => _steeringEffectsOversteerPedalVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsOversteerPedalVibrationCurve )
			{
				_steeringEffectsOversteerPedalVibrationCurve = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsOversteerPedalVibrationCurveString();
		}
	}

	private string _steeringEffectsOversteerPedalVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsOversteerPedalVibrationCurveString
	{
		get => _steeringEffectsOversteerPedalVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsOversteerPedalVibrationCurveString )
			{
				_steeringEffectsOversteerPedalVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsOversteerPedalVibrationCurveString()
	{
		if ( _steeringEffectsOversteerPedalVibrationCurve == 0f )
		{
			SteeringEffectsOversteerPedalVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SteeringEffectsOversteerPedalVibrationCurveString = $"{_steeringEffectsOversteerPedalVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SteeringEffectsOversteerPedalVibrationCurveContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsOversteerPedalVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsOversteerPedalVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - SeatOfPants enabled

	private bool _steeringEffectsSeatOfPantsEnabled = false;

	public bool SteeringEffectsSeatOfPantsEnabled
	{
		get => _steeringEffectsSeatOfPantsEnabled;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsEnabled )
			{
				_steeringEffectsSeatOfPantsEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsEnabledContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Steering effects - Seat-of-pants minimum threshold

	private float _steeringEffectsSeatOfPantsMinimumThreshold = 0f;

	public float SteeringEffectsSeatOfPantsMinimumThreshold
	{
		get => _steeringEffectsSeatOfPantsMinimumThreshold;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsSeatOfPantsMinimumThreshold )
			{
				_steeringEffectsSeatOfPantsMinimumThreshold = value;

				SteeringEffectsSeatOfPantsMaximumThreshold = MathF.Max( SteeringEffectsSeatOfPantsMaximumThreshold, _steeringEffectsSeatOfPantsMinimumThreshold );

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsMinimumThresholdString();
		}
	}

	private string _steeringEffectsSeatOfPantsMinimumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsMinimumThresholdString
	{
		get => _steeringEffectsSeatOfPantsMinimumThresholdString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsMinimumThresholdString )
			{
				_steeringEffectsSeatOfPantsMinimumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsMinimumThresholdString()
	{
		var units = SteeringEffectsSeatOfPantsAlgorithm switch
		{
			SteeringEffects.SeatOfPantsAlgorithm.YAcceleration => DataContext.Instance.Localization[ "GForceUnits" ],
			SteeringEffects.SeatOfPantsAlgorithm.YVelocity => DataContext.Instance.Localization[ "MPSUnits" ],
			_ => ""
		};

		SteeringEffectsSeatOfPantsMinimumThresholdString = $"{_steeringEffectsSeatOfPantsMinimumThreshold:F2} {units}";
	}

	public ContextSwitches SteeringEffectsSeatOfPantsMinimumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsMinimumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsMinimumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants maximum threshold

	private float _steeringEffectsSeatOfPantsMaximumThreshold = 10f;

	public float SteeringEffectsSeatOfPantsMaximumThreshold
	{
		get => _steeringEffectsSeatOfPantsMaximumThreshold;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsSeatOfPantsMaximumThreshold )
			{
				_steeringEffectsSeatOfPantsMaximumThreshold = value;

				SteeringEffectsSeatOfPantsMinimumThreshold = MathF.Min( SteeringEffectsSeatOfPantsMinimumThreshold, _steeringEffectsSeatOfPantsMaximumThreshold );

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsMaximumThresholdString();
		}
	}

	private string _steeringEffectsSeatOfPantsMaximumThresholdString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsMaximumThresholdString
	{
		get => _steeringEffectsSeatOfPantsMaximumThresholdString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsMaximumThresholdString )
			{
				_steeringEffectsSeatOfPantsMaximumThresholdString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsMaximumThresholdString()
	{
		var units = SteeringEffectsSeatOfPantsAlgorithm switch
		{
			SteeringEffects.SeatOfPantsAlgorithm.YAcceleration => DataContext.Instance.Localization[ "GForceUnits" ],
			SteeringEffects.SeatOfPantsAlgorithm.YVelocity => DataContext.Instance.Localization[ "MPSUnits" ],
			_ => ""
		};

		SteeringEffectsSeatOfPantsMaximumThresholdString = $"{_steeringEffectsSeatOfPantsMaximumThreshold:F2} {units}";
	}

	public ContextSwitches SteeringEffectsSeatOfPantsMaximumThresholdContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsMaximumThresholdPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsMaximumThresholdMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants algorithm

	private SteeringEffects.SeatOfPantsAlgorithm _steeringEffectsSeatOfPantsAlgorithm = SteeringEffects.SeatOfPantsAlgorithm.YVelocityOverXVelocity;

	public SteeringEffects.SeatOfPantsAlgorithm SteeringEffectsSeatOfPantsAlgorithm
	{
		get => _steeringEffectsSeatOfPantsAlgorithm;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsAlgorithm )
			{
				_steeringEffectsSeatOfPantsAlgorithm = value;

				OnPropertyChanged();

				SteeringEffectsSeatOfPantsMinimumThreshold = _steeringEffectsSeatOfPantsMinimumThreshold;
				SteeringEffectsSeatOfPantsMaximumThreshold = _steeringEffectsSeatOfPantsMaximumThreshold;
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsAlgorithmContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Seat-of-pants wheel vibration pattern

	private RacingWheel.VibrationPattern _steeringEffectsSeatOfPantsWheelVibrationPattern = RacingWheel.VibrationPattern.None;

	public RacingWheel.VibrationPattern SteeringEffectsSeatOfPantsWheelVibrationPattern
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationPattern;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelVibrationPattern )
			{
				_steeringEffectsSeatOfPantsWheelVibrationPattern = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelVibrationPatternContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Seat-of-pants wheel vibration strength

	private float _steeringEffectsSeatOfPantsWheelVibrationStrength = 0.1f;

	public float SteeringEffectsSeatOfPantsWheelVibrationStrength
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationStrength;

		set
		{
			value = Math.Clamp( value, 0f, 0.3f );

			if ( value != _steeringEffectsSeatOfPantsWheelVibrationStrength )
			{
				_steeringEffectsSeatOfPantsWheelVibrationStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsWheelVibrationStrengthString();
		}
	}

	private string _steeringEffectsSeatOfPantsWheelVibrationStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelVibrationStrengthString
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationStrengthString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelVibrationStrengthString )
			{
				_steeringEffectsSeatOfPantsWheelVibrationStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsWheelVibrationStrengthString()
	{
		if ( _steeringEffectsSeatOfPantsWheelVibrationStrength == 0f )
		{
			SteeringEffectsSeatOfPantsWheelVibrationStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsSeatOfPantsWheelVibrationStrength;

			SteeringEffectsSeatOfPantsWheelVibrationStrengthString = $"{_steeringEffectsSeatOfPantsWheelVibrationStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelVibrationStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants wheel vibration minimum frequency

	private float _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequency = 15f;

	public float SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequency )
			{
				_steeringEffectsSeatOfPantsWheelVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString();
		}
	}

	private string _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString )
			{
				_steeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString()
	{
		SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyString = $"{_steeringEffectsSeatOfPantsWheelVibrationMinimumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants wheel vibration maximum frequency

	private float _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequency = 50f;

	public float SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequency )
			{
				_steeringEffectsSeatOfPantsWheelVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString();
		}
	}

	private string _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString )
			{
				_steeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString()
	{
		SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyString = $"{_steeringEffectsSeatOfPantsWheelVibrationMaximumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants wheel vibration curve

	private float _steeringEffectsSeatOfPantsWheelVibrationCurve = 0.25f;

	public float SteeringEffectsSeatOfPantsWheelVibrationCurve
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsSeatOfPantsWheelVibrationCurve )
			{
				_steeringEffectsSeatOfPantsWheelVibrationCurve = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsWheelVibrationCurveString();
		}
	}

	private string _steeringEffectsSeatOfPantsWheelVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelVibrationCurveString
	{
		get => _steeringEffectsSeatOfPantsWheelVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelVibrationCurveString )
			{
				_steeringEffectsSeatOfPantsWheelVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsWheelVibrationCurveString()
	{
		if ( _steeringEffectsSeatOfPantsWheelVibrationCurve == 0f )
		{
			SteeringEffectsSeatOfPantsWheelVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SteeringEffectsSeatOfPantsWheelVibrationCurveString = $"{_steeringEffectsSeatOfPantsWheelVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelVibrationCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants wheel constant force direction

	private RacingWheel.ConstantForceDirection _steeringEffectsSeatOfPantsWheelConstantForceDirection = RacingWheel.ConstantForceDirection.IncreaseForce;

	public RacingWheel.ConstantForceDirection SteeringEffectsSeatOfPantsWheelConstantForceDirection
	{
		get => _steeringEffectsSeatOfPantsWheelConstantForceDirection;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelConstantForceDirection )
			{
				_steeringEffectsSeatOfPantsWheelConstantForceDirection = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelConstantForceDirectionContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Steering effects - Seat-of-pants wheel constant force strength

	private float _steeringEffectsSeatOfPantsWheelConstantForceStrength = 0.1f;

	public float SteeringEffectsSeatOfPantsWheelConstantForceStrength
	{
		get => _steeringEffectsSeatOfPantsWheelConstantForceStrength;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _steeringEffectsSeatOfPantsWheelConstantForceStrength )
			{
				_steeringEffectsSeatOfPantsWheelConstantForceStrength = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsWheelConstantForceStrengthString();
		}
	}

	private string _steeringEffectsSeatOfPantsWheelConstantForceStrengthString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelConstantForceStrengthString
	{
		get => _steeringEffectsSeatOfPantsWheelConstantForceStrengthString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelConstantForceStrengthString )
			{
				_steeringEffectsSeatOfPantsWheelConstantForceStrengthString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsWheelConstantForceStrengthString()
	{
		if ( _steeringEffectsSeatOfPantsWheelConstantForceStrength == 0f )
		{
			SteeringEffectsSeatOfPantsWheelConstantForceStrengthString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var convertedToTorque = RacingWheelWheelForce * _steeringEffectsSeatOfPantsWheelConstantForceStrength;

			SteeringEffectsSeatOfPantsWheelConstantForceStrengthString = $"{_steeringEffectsSeatOfPantsWheelConstantForceStrength * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToTorque:F1}{DataContext.Instance.Localization[ "TorqueUnits" ]})";
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelConstantForceStrengthContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelConstantForceStrengthPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelConstantForceStrengthMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - SeatOfPants wheel constant force curve

	private float _steeringEffectsSeatOfPantsWheelConstantForceCurve = 0.25f;

	public float SteeringEffectsSeatOfPantsWheelConstantForceCurve
	{
		get => _steeringEffectsSeatOfPantsWheelConstantForceCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsSeatOfPantsWheelConstantForceCurve )
			{
				_steeringEffectsSeatOfPantsWheelConstantForceCurve = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsWheelConstantForceCurveString();
		}
	}

	private string _steeringEffectsSeatOfPantsWheelConstantForceCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsWheelConstantForceCurveString
	{
		get => _steeringEffectsSeatOfPantsWheelConstantForceCurveString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsWheelConstantForceCurveString )
			{
				_steeringEffectsSeatOfPantsWheelConstantForceCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsWheelConstantForceCurveString()
	{
		if ( _steeringEffectsSeatOfPantsWheelConstantForceCurve == 0f )
		{
			SteeringEffectsSeatOfPantsWheelConstantForceCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SteeringEffectsSeatOfPantsWheelConstantForceCurveString = $"{_steeringEffectsSeatOfPantsWheelConstantForceCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsWheelConstantForceCurveContextSwitches { get; set; } = new( true, false, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsWheelConstantForceCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsWheelConstantForceCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants pedal vibration minimum frequency

	private float _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency = 0.1f;

	public float SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency )
			{
				_steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString();
		}
	}

	private string _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString )
			{
				_steeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency ) );

		SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyString = $"{_steeringEffectsSeatOfPantsPedalVibrationMinimumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants pedal vibration maximum frequency

	private float _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency = 1f;

	public float SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency )
			{
				_steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString();
		}
	}

	private string _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString )
			{
				_steeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency ) );

		SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyString = $"{_steeringEffectsSeatOfPantsPedalVibrationMaximumFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Seat-of-pants pedal vibration curve

	private float _steeringEffectsSeatOfPantsPedalVibrationCurve = 0f;

	public float SteeringEffectsSeatOfPantsPedalVibrationCurve
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _steeringEffectsSeatOfPantsPedalVibrationCurve )
			{
				_steeringEffectsSeatOfPantsPedalVibrationCurve = value;

				OnPropertyChanged();
			}

			UpdateSteeringEffectsSeatOfPantsPedalVibrationCurveString();
		}
	}

	private string _steeringEffectsSeatOfPantsPedalVibrationCurveString = string.Empty;

	[XmlIgnore]
	public string SteeringEffectsSeatOfPantsPedalVibrationCurveString
	{
		get => _steeringEffectsSeatOfPantsPedalVibrationCurveString;

		set
		{
			if ( value != _steeringEffectsSeatOfPantsPedalVibrationCurveString )
			{
				_steeringEffectsSeatOfPantsPedalVibrationCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSteeringEffectsSeatOfPantsPedalVibrationCurveString()
	{
		if ( _steeringEffectsSeatOfPantsPedalVibrationCurve == 0f )
		{
			SteeringEffectsSeatOfPantsPedalVibrationCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SteeringEffectsSeatOfPantsPedalVibrationCurveString = $"{_steeringEffectsSeatOfPantsPedalVibrationCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SteeringEffectsSeatOfPantsPedalVibrationCurveContextSwitches { get; set; } = new( true, true, false, false, false );
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings SteeringEffectsSeatOfPantsPedalVibrationCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Steering effects - Send chat messages

	private bool _steeringEffectsSendChatMessages = true;

	public bool SteeringEffectsSendChatMessages
	{
		get => _steeringEffectsSendChatMessages;

		set
		{
			if ( value != _steeringEffectsSendChatMessages )
			{
				_steeringEffectsSendChatMessages = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Steering effects - Input mapped setting update enabled

	private bool _steeringEffectsInputMappedSettingUpdateEnabled = true;

	public bool SteeringEffectsInputMappedSettingUpdateEnabled
	{
		get => _steeringEffectsInputMappedSettingUpdateEnabled;

		set
		{
			if ( value != _steeringEffectsInputMappedSettingUpdateEnabled )
			{
				_steeringEffectsInputMappedSettingUpdateEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Pedals - Enabled

	private bool _pedalsEnabled = false;

	public bool PedalsEnabled
	{
		get => _pedalsEnabled;

		set
		{
			if ( value != _pedalsEnabled )
			{
				_pedalsEnabled = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			if ( !app.SettingsFile.PauseSerialization )
			{
				app.Pedals.Refresh();
			}
		}
	}

	#endregion

	#region Pedals - Minimum frequency

	private float _pedalsMinimumFrequency = 0f;

	public float PedalsMinimumFrequency
	{
		get => _pedalsMinimumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _pedalsMinimumFrequency )
			{
				_pedalsMinimumFrequency = value;

				OnPropertyChanged();

				PedalsMaximumFrequency = MathF.Max( PedalsMaximumFrequency, _pedalsMinimumFrequency );

				UpdateRelatedPedalSettings();
			}

			UpdatePedalsMinimumFrequencyString();
		}
	}

	private string _pedalsMinimumFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsMinimumFrequencyString
	{
		get => _pedalsMinimumFrequencyString;

		set
		{
			if ( value != _pedalsMinimumFrequencyString )
			{
				_pedalsMinimumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsMinimumFrequencyString()
	{
		PedalsMinimumFrequencyString = $"{_pedalsMinimumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
	}

	public ContextSwitches PedalsMinimumFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsMinimumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsMinimumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Maximum frequency

	private float _pedalsMaximumFrequency = 50f;

	public float PedalsMaximumFrequency
	{
		get => _pedalsMaximumFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 50f );

			if ( value != _pedalsMaximumFrequency )
			{
				_pedalsMaximumFrequency = value;

				OnPropertyChanged();

				PedalsMinimumFrequency = MathF.Min( PedalsMinimumFrequency, _pedalsMaximumFrequency );

				UpdateRelatedPedalSettings();
			}

			UpdatePedalsMaximumFrequencyString();
		}
	}

	private string _pedalsMaximumFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsMaximumFrequencyString
	{
		get => _pedalsMaximumFrequencyString;

		set
		{
			if ( value != _pedalsMaximumFrequencyString )
			{
				_pedalsMaximumFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsMaximumFrequencyString()
	{
		PedalsMaximumFrequencyString = $"{_pedalsMaximumFrequency:F0} {DataContext.Instance.Localization[ "HertzUnits" ]}";
	}

	public ContextSwitches PedalsMaximumFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsMaximumFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsMaximumFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Frequency curve

	private float _pedalsFrequencyCurve = 0.25f;

	public float PedalsFrequencyCurve
	{
		get => _pedalsFrequencyCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _pedalsFrequencyCurve )
			{
				_pedalsFrequencyCurve = value;

				OnPropertyChanged();
			}

			UpdatePedalsFrequencyCurveString();
		}
	}

	private string _pedalsFrequencyCurveString = string.Empty;

	[XmlIgnore]
	public string PedalsFrequencyCurveString
	{
		get => _pedalsFrequencyCurveString;

		set
		{
			if ( value != _pedalsFrequencyCurveString )
			{
				_pedalsFrequencyCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsFrequencyCurveString()
	{
		if ( _pedalsFrequencyCurve == 0f )
		{
			PedalsFrequencyCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			PedalsFrequencyCurveString = $"{_pedalsFrequencyCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches PedalsFrequencyCurveContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsFrequencyCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsFrequencyCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Minimum Amplitude

	private float _pedalsMinimumAmplitude = 0f;

	public float PedalsMinimumAmplitude
	{
		get => _pedalsMinimumAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsMinimumAmplitude )
			{
				_pedalsMinimumAmplitude = value;

				OnPropertyChanged();

				PedalsMaximumAmplitude = MathF.Max( PedalsMaximumAmplitude, _pedalsMinimumAmplitude );
			}

			UpdatePedalsMinimumAmplitudeString();
		}
	}

	private string _pedalsMinimumAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsMinimumAmplitudeString
	{
		get => _pedalsMinimumAmplitudeString;

		set
		{
			if ( value != _pedalsMinimumAmplitudeString )
			{
				_pedalsMinimumAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsMinimumAmplitudeString()
	{
		PedalsMinimumAmplitudeString = $"{_pedalsMinimumAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsMinimumAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsMinimumAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsMinimumAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Maximum Amplitude

	private float _pedalsMaximumAmplitude = 1f;

	public float PedalsMaximumAmplitude
	{
		get => _pedalsMaximumAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsMaximumAmplitude )
			{
				_pedalsMaximumAmplitude = value;

				OnPropertyChanged();

				PedalsMinimumAmplitude = MathF.Min( PedalsMinimumAmplitude, _pedalsMaximumAmplitude );
			}

			UpdatePedalsMaximumAmplitudeString();
		}
	}

	private string _pedalsMaximumAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsMaximumAmplitudeString
	{
		get => _pedalsMaximumAmplitudeString;

		set
		{
			if ( value != _pedalsMaximumAmplitudeString )
			{
				_pedalsMaximumAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsMaximumAmplitudeString()
	{
		PedalsMaximumAmplitudeString = $"{_pedalsMaximumAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsMaximumAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsMaximumAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsMaximumAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Amplitude curve

	private float _pedalsAmplitudeCurve = 0f;

	public float PedalsAmplitudeCurve
	{
		get => _pedalsAmplitudeCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _pedalsAmplitudeCurve )
			{
				_pedalsAmplitudeCurve = value;

				OnPropertyChanged();
			}

			UpdatePedalsAmplitudeCurveString();
		}
	}

	private string _pedalsAmplitudeCurveString = string.Empty;

	[XmlIgnore]
	public string PedalsAmplitudeCurveString
	{
		get => _pedalsAmplitudeCurveString;

		set
		{
			if ( value != _pedalsAmplitudeCurveString )
			{
				_pedalsAmplitudeCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsAmplitudeCurveString()
	{
		if ( _pedalsAmplitudeCurve == 0f )
		{
			PedalsAmplitudeCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			PedalsAmplitudeCurveString = $"{_pedalsAmplitudeCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches PedalsAmplitudeCurveContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsAmplitudeCurvePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsAmplitudeCurveMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch effect 1

	private Pedals.Effect _pedalsClutchEffect1 = Pedals.Effect.GearChange;

	public Pedals.Effect PedalsClutchEffect1
	{
		get => _pedalsClutchEffect1;

		set
		{
			if ( value != _pedalsClutchEffect1 )
			{
				_pedalsClutchEffect1 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchEffect1ContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Clutch strength 1

	private float _pedalsClutchStrength1 = 1f;

	public float PedalsClutchStrength1
	{
		get => _pedalsClutchStrength1;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsClutchStrength1 )
			{
				_pedalsClutchStrength1 = value;

				OnPropertyChanged();
			}

			UpdatePedalsClutchStrength1String();
		}
	}

	private string _pedalsClutchStrength1String = string.Empty;

	[XmlIgnore]
	public string PedalsClutchStrength1String
	{
		get => _pedalsClutchStrength1String;

		set
		{
			if ( value != _pedalsClutchStrength1String )
			{
				_pedalsClutchStrength1String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsClutchStrength1String()
	{
		PedalsClutchStrength1String = $"{_pedalsClutchStrength1 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsClutchStrength1ContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsClutchStrength1PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchStrength1MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch test 1

	public ButtonMappings PedalsClutchTest1ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch effect 2

	private Pedals.Effect _pedalsClutchEffect2 = Pedals.Effect.ClutchSlip;

	public Pedals.Effect PedalsClutchEffect2
	{
		get => _pedalsClutchEffect2;

		set
		{
			if ( value != _pedalsClutchEffect2 )
			{
				_pedalsClutchEffect2 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchEffect2ContextSwitches { get => PedalsClutchEffect1ContextSwitches; set => PedalsClutchEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Clutch strength 2

	private float _pedalsClutchStrength2 = 1f;

	public float PedalsClutchStrength2
	{
		get => _pedalsClutchStrength2;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsClutchStrength2 )
			{
				_pedalsClutchStrength2 = value;

				OnPropertyChanged();
			}

			UpdatePedalsClutchStrength2String();
		}
	}

	private string _pedalsClutchStrength2String = string.Empty;

	[XmlIgnore]
	public string PedalsClutchStrength2String
	{
		get => _pedalsClutchStrength2String;

		set
		{
			if ( value != _pedalsClutchStrength2String )
			{
				_pedalsClutchStrength2String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsClutchStrength2String()
	{
		PedalsClutchStrength2String = $"{_pedalsClutchStrength2 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsClutchStrength2ContextSwitches { get => PedalsClutchStrength1ContextSwitches; set => PedalsClutchStrength1ContextSwitches = value; }
	public ButtonMappings PedalsClutchStrength2PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchStrength2MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch test 2

	public ButtonMappings PedalsClutchTest2ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch effect 3

	private Pedals.Effect _pedalsClutchEffect3 = Pedals.Effect.None;

	public Pedals.Effect PedalsClutchEffect3
	{
		get => _pedalsClutchEffect3;

		set
		{
			if ( value != _pedalsClutchEffect3 )
			{
				_pedalsClutchEffect3 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsClutchEffect3ContextSwitches { get => PedalsClutchEffect1ContextSwitches; set => PedalsClutchEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Clutch effect 3 strength

	private float _pedalsClutchStrength3 = 1f;

	public float PedalsClutchStrength3
	{
		get => _pedalsClutchStrength3;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsClutchStrength3 )
			{
				_pedalsClutchStrength3 = value;

				OnPropertyChanged();
			}

			UpdatePedalsClutchStrength3String();
		}
	}

	private string _pedalsClutchStrength3String = string.Empty;

	[XmlIgnore]
	public string PedalsClutchStrength3String
	{
		get => _pedalsClutchStrength3String;

		set
		{
			if ( value != _pedalsClutchStrength3String )
			{
				_pedalsClutchStrength3String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsClutchStrength3String()
	{
		PedalsClutchStrength3String = $"{_pedalsClutchStrength3 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsClutchStrength3ContextSwitches { get => PedalsClutchStrength1ContextSwitches; set => PedalsClutchStrength1ContextSwitches = value; }
	public ButtonMappings PedalsClutchStrength3PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchStrength3MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch test 3

	public ButtonMappings PedalsClutchTest3ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake effect 1

	private Pedals.Effect _pedalsBrakeEffect1 = Pedals.Effect.ABSEngaged;

	public Pedals.Effect PedalsBrakeEffect1
	{
		get => _pedalsBrakeEffect1;

		set
		{
			if ( value != _pedalsBrakeEffect1 )
			{
				_pedalsBrakeEffect1 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeEffect1ContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Brake strength 1

	private float _pedalsBrakeStrength1 = 1f;

	public float PedalsBrakeStrength1
	{
		get => _pedalsBrakeStrength1;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsBrakeStrength1 )
			{
				_pedalsBrakeStrength1 = value;

				OnPropertyChanged();
			}

			UpdatePedalsBrakeStrength1String();
		}
	}

	private string _pedalsBrakeStrength1String = string.Empty;

	[XmlIgnore]
	public string PedalsBrakeStrength1String
	{
		get => _pedalsBrakeStrength1String;

		set
		{
			if ( value != _pedalsBrakeStrength1String )
			{
				_pedalsBrakeStrength1String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsBrakeStrength1String()
	{
		PedalsBrakeStrength1String = $"{_pedalsBrakeStrength1 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsBrakeStrength1ContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsBrakeStrength1PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsBrakeStrength1MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake test 1

	public ButtonMappings PedalsBrakeTest1ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake effect 2

	private Pedals.Effect _pedalsBrakeEffect2 = Pedals.Effect.WheelLock;

	public Pedals.Effect PedalsBrakeEffect2
	{
		get => _pedalsBrakeEffect2;

		set
		{
			if ( value != _pedalsBrakeEffect2 )
			{
				_pedalsBrakeEffect2 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeEffect2ContextSwitches { get => PedalsBrakeEffect1ContextSwitches; set => PedalsBrakeEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Brake strength 2

	private float _pedalsBrakeStrength2 = 1f;

	public float PedalsBrakeStrength2
	{
		get => _pedalsBrakeStrength2;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsBrakeStrength2 )
			{
				_pedalsBrakeStrength2 = value;

				OnPropertyChanged();
			}

			UpdatePedalsBrakeStrength2String();
		}
	}

	private string _pedalsBrakeStrength2String = string.Empty;

	[XmlIgnore]
	public string PedalsBrakeStrength2String
	{
		get => _pedalsBrakeStrength2String;

		set
		{
			if ( value != _pedalsBrakeStrength2String )
			{
				_pedalsBrakeStrength2String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsBrakeStrength2String()
	{
		PedalsBrakeStrength2String = $"{_pedalsBrakeStrength2 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsBrakeStrength2ContextSwitches { get => PedalsBrakeStrength1ContextSwitches; set => PedalsBrakeStrength1ContextSwitches = value; }
	public ButtonMappings PedalsBrakeStrength2PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsBrakeStrength2MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake test 2

	public ButtonMappings PedalsBrakeTest2ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake effect 3

	private Pedals.Effect _pedalsBrakeEffect3 = Pedals.Effect.UndersteerEffect;

	public Pedals.Effect PedalsBrakeEffect3
	{
		get => _pedalsBrakeEffect3;

		set
		{
			if ( value != _pedalsBrakeEffect3 )
			{
				_pedalsBrakeEffect3 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsBrakeEffect3ContextSwitches { get => PedalsBrakeEffect1ContextSwitches; set => PedalsBrakeEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Brake effect 3 strength

	private float _pedalsBrakeStrength3 = 1f;

	public float PedalsBrakeStrength3
	{
		get => _pedalsBrakeStrength3;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsBrakeStrength3 )
			{
				_pedalsBrakeStrength3 = value;

				OnPropertyChanged();
			}

			UpdatePedalsBrakeStrength3String();
		}
	}

	private string _pedalsBrakeStrength3String = string.Empty;

	[XmlIgnore]
	public string PedalsBrakeStrength3String
	{
		get => _pedalsBrakeStrength3String;

		set
		{
			if ( value != _pedalsBrakeStrength3String )
			{
				_pedalsBrakeStrength3String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsBrakeStrength3String()
	{
		PedalsBrakeStrength3String = $"{_pedalsBrakeStrength3 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsBrakeStrength3ContextSwitches { get => PedalsBrakeStrength1ContextSwitches; set => PedalsBrakeStrength1ContextSwitches = value; }
	public ButtonMappings PedalsBrakeStrength3PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsBrakeStrength3MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Brake test 3

	public ButtonMappings PedalsBrakeTest3ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle effect 1

	private Pedals.Effect _pedalsThrottleEffect1 = Pedals.Effect.WheelSpin;

	public Pedals.Effect PedalsThrottleEffect1
	{
		get => _pedalsThrottleEffect1;

		set
		{
			if ( value != _pedalsThrottleEffect1 )
			{
				_pedalsThrottleEffect1 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleEffect1ContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Throttle strength 1

	private float _pedalsThrottleStrength1 = 1f;

	public float PedalsThrottleStrength1
	{
		get => _pedalsThrottleStrength1;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsThrottleStrength1 )
			{
				_pedalsThrottleStrength1 = value;

				OnPropertyChanged();
			}

			UpdatePedalsThrottleStrength1String();
		}
	}

	private string _pedalsThrottleStrength1String = string.Empty;

	[XmlIgnore]
	public string PedalsThrottleStrength1String
	{
		get => _pedalsThrottleStrength1String;

		set
		{
			if ( value != _pedalsThrottleStrength1String )
			{
				_pedalsThrottleStrength1String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsThrottleStrength1String()
	{
		PedalsThrottleStrength1String = $"{_pedalsThrottleStrength1 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsThrottleStrength1ContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsThrottleStrength1PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsThrottleStrength1MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle test 1

	public ButtonMappings PedalsThrottleTest1ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle effect 2

	private Pedals.Effect _pedalsThrottleEffect2 = Pedals.Effect.ShiftRPM;

	public Pedals.Effect PedalsThrottleEffect2
	{
		get => _pedalsThrottleEffect2;

		set
		{
			if ( value != _pedalsThrottleEffect2 )
			{
				_pedalsThrottleEffect2 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleEffect2ContextSwitches { get => PedalsThrottleEffect1ContextSwitches; set => PedalsThrottleEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Throttle strength 2

	private float _pedalsThrottleStrength2 = 1f;

	public float PedalsThrottleStrength2
	{
		get => _pedalsThrottleStrength2;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsThrottleStrength2 )
			{
				_pedalsThrottleStrength2 = value;

				OnPropertyChanged();
			}

			UpdatePedalsThrottleStrength2String();
		}
	}

	private string _pedalsThrottleStrength2String = string.Empty;

	[XmlIgnore]
	public string PedalsThrottleStrength2String
	{
		get => _pedalsThrottleStrength2String;

		set
		{
			if ( value != _pedalsThrottleStrength2String )
			{
				_pedalsThrottleStrength2String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsThrottleStrength2String()
	{
		PedalsThrottleStrength2String = $"{_pedalsThrottleStrength2 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsThrottleStrength2ContextSwitches { get => PedalsThrottleStrength1ContextSwitches; set => PedalsThrottleStrength1ContextSwitches = value; }
	public ButtonMappings PedalsThrottleStrength2PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsThrottleStrength2MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle test 2

	public ButtonMappings PedalsThrottleTest2ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle effect 3

	private Pedals.Effect _pedalsThrottleEffect3 = Pedals.Effect.OversteerEffect;

	public Pedals.Effect PedalsThrottleEffect3
	{
		get => _pedalsThrottleEffect3;

		set
		{
			if ( value != _pedalsThrottleEffect3 )
			{
				_pedalsThrottleEffect3 = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsThrottleEffect3ContextSwitches { get => PedalsThrottleEffect1ContextSwitches; set => PedalsThrottleEffect1ContextSwitches = value; }

	#endregion

	#region Pedals - Throttle effect 3 strength

	private float _pedalsThrottleStrength3 = 1f;

	public float PedalsThrottleStrength3
	{
		get => _pedalsThrottleStrength3;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsThrottleStrength3 )
			{
				_pedalsThrottleStrength3 = value;

				OnPropertyChanged();
			}

			UpdatePedalsThrottleStrength3String();
		}
	}

	private string _pedalsThrottleStrength3String = string.Empty;

	[XmlIgnore]
	public string PedalsThrottleStrength3String
	{
		get => _pedalsThrottleStrength3String;

		set
		{
			if ( value != _pedalsThrottleStrength3String )
			{
				_pedalsThrottleStrength3String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsThrottleStrength3String()
	{
		PedalsThrottleStrength3String = $"{_pedalsThrottleStrength3 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsThrottleStrength3ContextSwitches { get => PedalsThrottleStrength1ContextSwitches; set => PedalsThrottleStrength1ContextSwitches = value; }
	public ButtonMappings PedalsThrottleStrength3PlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsThrottleStrength3MinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Throttle test 3

	public ButtonMappings PedalsThrottleTest3ButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into gear frequency

	private float _pedalsShiftIntoGearFrequency = 0.3f;

	public float PedalsShiftIntoGearFrequency
	{
		get => _pedalsShiftIntoGearFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsShiftIntoGearFrequency )
			{
				_pedalsShiftIntoGearFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsShiftIntoGearFrequencyString();
		}
	}

	private string _pedalsShiftIntoGearFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoGearFrequencyString
	{
		get => _pedalsShiftIntoGearFrequencyString;

		set
		{
			if ( value != _pedalsShiftIntoGearFrequencyString )
			{
				_pedalsShiftIntoGearFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsShiftIntoGearFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsShiftIntoGearFrequency ) );

		PedalsShiftIntoGearFrequencyString = $"{_pedalsShiftIntoGearFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsShiftIntoGearFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoGearFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoGearFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into gear amplitude

	private float _pedalsShiftIntoGearAmplitude = 1f;

	public float PedalsShiftIntoGearAmplitude
	{
		get => _pedalsShiftIntoGearAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsShiftIntoGearAmplitude )
			{
				_pedalsShiftIntoGearAmplitude = value;

				OnPropertyChanged();
			}

			UpdatePedalsShiftIntoGearAmplitudeString();
		}
	}

	private string _pedalsShiftIntoGearAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoGearAmplitudeString
	{
		get => _pedalsShiftIntoGearAmplitudeString;

		set
		{
			if ( value != _pedalsShiftIntoGearAmplitudeString )
			{
				_pedalsShiftIntoGearAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsShiftIntoGearAmplitudeString()
	{
		PedalsShiftIntoGearAmplitudeString = $"{_pedalsShiftIntoGearAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsShiftIntoGearAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoGearAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoGearAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into gear duration

	private float _pedalsShiftIntoGearDuration = 0.1f;

	public float PedalsShiftIntoGearDuration
	{
		get => _pedalsShiftIntoGearDuration;

		set
		{
			value = Math.Clamp( value, 0.05f, 1f );

			if ( value != _pedalsShiftIntoGearDuration )
			{
				_pedalsShiftIntoGearDuration = value;

				OnPropertyChanged();
			}

			UpdatePedalsShiftIntoGearDurationString();
		}
	}

	private string _pedalsShiftIntoGearDurationString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoGearDurationString
	{
		get => _pedalsShiftIntoGearDurationString;

		set
		{
			if ( value != _pedalsShiftIntoGearDurationString )
			{
				_pedalsShiftIntoGearDurationString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsShiftIntoGearDurationString()
	{
		PedalsShiftIntoGearDurationString = $"{_pedalsShiftIntoGearDuration:F2} {DataContext.Instance.Localization[ "SecondsUnits" ]}";
	}

	public ContextSwitches PedalsShiftIntoGearDurationContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoGearDurationPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoGearDurationMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into neutral frequency

	private float _pedalsShiftIntoNeutralFrequency = 0.7f;

	public float PedalsShiftIntoNeutralFrequency
	{
		get => _pedalsShiftIntoNeutralFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsShiftIntoNeutralFrequency )
			{
				_pedalsShiftIntoNeutralFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsShiftIntoNeutralFrequencyString();
		}
	}

	private string _pedalsShiftIntoNeutralFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoNeutralFrequencyString
	{
		get => _pedalsShiftIntoNeutralFrequencyString;

		set
		{
			if ( value != _pedalsShiftIntoNeutralFrequencyString )
			{
				_pedalsShiftIntoNeutralFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsShiftIntoNeutralFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsShiftIntoNeutralFrequency ) );

		PedalsShiftIntoNeutralFrequencyString = $"{_pedalsShiftIntoNeutralFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsShiftIntoNeutralFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoNeutralFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoNeutralFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into neutral amplitude

	private float _pedalsShiftIntoNeutralAmplitude = 0.75f;

	public float PedalsShiftIntoNeutralAmplitude
	{
		get => _pedalsShiftIntoNeutralAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsShiftIntoNeutralAmplitude )
			{
				_pedalsShiftIntoNeutralAmplitude = value;

				OnPropertyChanged();
			}

			UpdatePedalsShiftIntoNeutralAmplitudeString();
		}
	}

	private string _pedalsShiftIntoNeutralAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoNeutralAmplitudeString
	{
		get => _pedalsShiftIntoNeutralAmplitudeString;

		set
		{
			if ( value != _pedalsShiftIntoNeutralAmplitudeString )
			{
				_pedalsShiftIntoNeutralAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsShiftIntoNeutralAmplitudeString()
	{
		PedalsShiftIntoNeutralAmplitudeString = $"{_pedalsShiftIntoNeutralAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsShiftIntoNeutralAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoNeutralAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoNeutralAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift into neutral duration

	private float _pedalsShiftIntoNeutralDuration = 0.05f;

	public float PedalsShiftIntoNeutralDuration
	{
		get => _pedalsShiftIntoNeutralDuration;

		set
		{
			value = Math.Clamp( value, 0.05f, 1f );

			if ( value != _pedalsShiftIntoNeutralDuration )
			{
				_pedalsShiftIntoNeutralDuration = value;

				OnPropertyChanged();
			}

			UpdatePedalsShiftIntoNeutralDurationString();
		}
	}

	private string _pedalsShiftIntoNeutralDurationString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftIntoNeutralDurationString
	{
		get => _pedalsShiftIntoNeutralDurationString;

		set
		{
			if ( value != _pedalsShiftIntoNeutralDurationString )
			{
				_pedalsShiftIntoNeutralDurationString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsShiftIntoNeutralDurationString()
	{
		PedalsShiftIntoNeutralDurationString = $"{_pedalsShiftIntoNeutralDuration:F2} {DataContext.Instance.Localization[ "SecondsUnits" ]}";
	}

	public ContextSwitches PedalsShiftIntoNeutralDurationContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftIntoNeutralDurationPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftIntoNeutralDurationMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - ABS engaged frequency

	private float _pedalsABSEngagedFrequency = 0.5f;

	public float PedalsABSEngagedFrequency
	{
		get => _pedalsABSEngagedFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsABSEngagedFrequency )
			{
				_pedalsABSEngagedFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsABSEngagedFrequencyString();
		}
	}

	private string _pedalsABSEngagedFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsABSEngagedFrequencyString
	{
		get => _pedalsABSEngagedFrequencyString;

		set
		{
			if ( value != _pedalsABSEngagedFrequencyString )
			{
				_pedalsABSEngagedFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsABSEngagedFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsABSEngagedFrequency ) );

		PedalsABSEngagedFrequencyString = $"{_pedalsABSEngagedFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsABSEngagedFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsABSEngagedFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsABSEngagedFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - ABS engaged amplitude

	private float _pedalsABSEngagedAmplitude = 1f;

	public float PedalsABSEngagedAmplitude
	{
		get => _pedalsABSEngagedAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsABSEngagedAmplitude )
			{
				_pedalsABSEngagedAmplitude = value;

				OnPropertyChanged();
			}

			UpdatePedalsABSEngagedAmplitudeString();
		}
	}

	private string _pedalsABSEngagedAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsABSEngagedAmplitudeString
	{
		get => _pedalsABSEngagedAmplitudeString;

		set
		{
			if ( value != _pedalsABSEngagedAmplitudeString )
			{
				_pedalsABSEngagedAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsABSEngagedAmplitudeString()
	{
		PedalsABSEngagedAmplitudeString = $"{_pedalsABSEngagedAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsABSEngagedAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsABSEngagedAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsABSEngagedAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - ABS engaged fade with brake enabled

	private bool _pedalsABSEngagedFadeWithBrakeEnabled = true;

	public bool PedalsABSEngagedFadeWithBrakeEnabled
	{
		get => _pedalsABSEngagedFadeWithBrakeEnabled;

		set
		{
			if ( value != _pedalsABSEngagedFadeWithBrakeEnabled )
			{
				_pedalsABSEngagedFadeWithBrakeEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsABSEngagedFadeWithBrakeEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Starting RPM

	private float _pedalsStartingRPM = 0.25f;

	public float PedalsStartingRPM
	{
		get => _pedalsStartingRPM;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsStartingRPM )
			{
				_pedalsStartingRPM = value;

				OnPropertyChanged();
			}

			UpdatePedalsStartingRPMString();
		}
	}

	private string _pedalsStartingRPMString = string.Empty;

	[XmlIgnore]
	public string PedalsStartingRPMString
	{
		get => _pedalsStartingRPMString;

		set
		{
			if ( value != _pedalsStartingRPMString )
			{
				_pedalsStartingRPMString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsStartingRPMString()
	{
		PedalsStartingRPMString = $"{_pedalsStartingRPM * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsStartingRPMContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsStartingRPMPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsStartingRPMMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - RPM vibrate in top gear enabled

	private bool _pedalsRPMVibrateInTopGearEnabled = false;

	public bool PedalsRPMVibrateInTopGearEnabled
	{
		get => _pedalsRPMVibrateInTopGearEnabled;

		set
		{
			if ( value != _pedalsRPMVibrateInTopGearEnabled )
			{
				_pedalsRPMVibrateInTopGearEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsRPMVibrateInTopGearEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - RPM fade with throttle enabled

	private bool _pedalsRPMFadeWithThrottleEnabled = true;

	public bool PedalsRPMFadeWithThrottleEnabled
	{
		get => _pedalsRPMFadeWithThrottleEnabled;

		set
		{
			if ( value != _pedalsRPMFadeWithThrottleEnabled )
			{
				_pedalsRPMFadeWithThrottleEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsRPMFadeWithThrottleEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Shift RPM frequency

	private float _pedalsShiftRPMFrequency = 1f;

	public float PedalsShiftRPMFrequency
	{
		get => _pedalsShiftRPMFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsShiftRPMFrequency )
			{
				_pedalsShiftRPMFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsShiftRPMFrequencyString();
		}
	}

	private string _pedalsShiftRPMFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftRPMFrequencyString
	{
		get => _pedalsShiftRPMFrequencyString;

		set
		{
			if ( value != _pedalsShiftRPMFrequencyString )
			{
				_pedalsShiftRPMFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsShiftRPMFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsShiftRPMFrequency ) );

		PedalsShiftRPMFrequencyString = $"{_pedalsShiftRPMFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsShiftRPMFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftRPMFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftRPMFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift RPM amplitude

	private float _pedalsShiftRPMAmplitude = 1f;

	public float PedalsShiftRPMAmplitude
	{
		get => _pedalsShiftRPMAmplitude;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsShiftRPMAmplitude )
			{
				_pedalsShiftRPMAmplitude = value;

				OnPropertyChanged();
			}

			UpdatePedalsShiftRPMAmplitudeString();
		}
	}

	private string _pedalsShiftRPMAmplitudeString = string.Empty;

	[XmlIgnore]
	public string PedalsShiftRPMAmplitudeString
	{
		get => _pedalsShiftRPMAmplitudeString;

		set
		{
			if ( value != _pedalsShiftRPMAmplitudeString )
			{
				_pedalsShiftRPMAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsShiftRPMAmplitudeString()
	{
		PedalsShiftRPMAmplitudeString = $"{_pedalsShiftRPMAmplitude * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsShiftRPMAmplitudeContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsShiftRPMAmplitudePlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsShiftRPMAmplitudeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Shift RPM pulsate enabled

	private bool _pedalsShiftRPMPulsateEnabled = true;

	public bool PedalsShiftRPMPulsateEnabled
	{
		get => _pedalsShiftRPMPulsateEnabled;

		set
		{
			if ( value != _pedalsShiftRPMPulsateEnabled )
			{
				_pedalsShiftRPMPulsateEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsShiftRPMPulsateEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Wheel lock frequency

	private float _pedalsWheelLockFrequency = 0.2f;

	public float PedalsWheelLockFrequency
	{
		get => _pedalsWheelLockFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsWheelLockFrequency )
			{
				_pedalsWheelLockFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsWheelLockFrequencyString();
		}
	}

	private string _pedalsWheelLockFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsWheelLockFrequencyString
	{
		get => _pedalsWheelLockFrequencyString;

		set
		{
			if ( value != _pedalsWheelLockFrequencyString )
			{
				_pedalsWheelLockFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsWheelLockFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsWheelLockFrequency ) );

		PedalsWheelLockFrequencyString = $"{_pedalsWheelLockFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsWheelLockFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsWheelLockFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsWheelLockFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Wheel lock sensitivity

	private float _pedalsWheelLockSensitivity = 0.95f;

	public float PedalsWheelLockSensitivity
	{
		get => _pedalsWheelLockSensitivity;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsWheelLockSensitivity )
			{
				_pedalsWheelLockSensitivity = value;

				OnPropertyChanged();
			}

			UpdatePedalsWheelLockSensitivityString();
		}
	}

	private string _pedalsWheelLockSensitivityString = string.Empty;

	[XmlIgnore]
	public string PedalsWheelLockSensitivityString
	{
		get => _pedalsWheelLockSensitivityString;

		set
		{
			if ( value != _pedalsWheelLockSensitivityString )
			{
				_pedalsWheelLockSensitivityString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsWheelLockSensitivityString()
	{
		PedalsWheelLockSensitivityString = $"{_pedalsWheelLockSensitivity * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsWheelLockSensitivityContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsWheelLockSensitivityPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsWheelLockSensitivityMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Wheel lock fade with brake enabled

	private bool _pedalsWheelLockFadeWithBrakeEnabled = true;

	public bool PedalsWheelLockFadeWithBrakeEnabled
	{
		get => _pedalsWheelLockFadeWithBrakeEnabled;

		set
		{
			if ( value != _pedalsWheelLockFadeWithBrakeEnabled )
			{
				_pedalsWheelLockFadeWithBrakeEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsWheelLockFadeWithBrakeEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Wheel spin frequency

	private float _pedalsWheelSpinFrequency = 1f;

	public float PedalsWheelSpinFrequency
	{
		get => _pedalsWheelSpinFrequency;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsWheelSpinFrequency )
			{
				_pedalsWheelSpinFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsWheelSpinFrequencyString();
		}
	}

	private string _pedalsWheelSpinFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsWheelSpinFrequencyString
	{
		get => _pedalsWheelSpinFrequencyString;

		set
		{
			if ( value != _pedalsWheelSpinFrequencyString )
			{
				_pedalsWheelSpinFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsWheelSpinFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsWheelSpinFrequency ) );

		PedalsWheelSpinFrequencyString = $"{_pedalsWheelSpinFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsWheelSpinFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsWheelSpinFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsWheelSpinFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Wheel spin sensitivity

	private float _pedalsWheelSpinSensitivity = 0.95f;

	public float PedalsWheelSpinSensitivity
	{
		get => _pedalsWheelSpinSensitivity;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsWheelSpinSensitivity )
			{
				_pedalsWheelSpinSensitivity = value;

				OnPropertyChanged();
			}

			UpdatePedalsWheelSpinSensitivityString();
		}
	}

	private string _pedalsWheelSpinSensitivityString = string.Empty;

	[XmlIgnore]
	public string PedalsWheelSpinSensitivityString
	{
		get => _pedalsWheelSpinSensitivityString;

		set
		{
			if ( value != _pedalsWheelSpinSensitivityString )
			{
				_pedalsWheelSpinSensitivityString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsWheelSpinSensitivityString()
	{
		PedalsWheelSpinSensitivityString = $"{_pedalsWheelSpinSensitivity * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsWheelSpinSensitivityContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsWheelSpinSensitivityPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsWheelSpinSensitivityMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Wheel spin fade with throttle enabled

	private bool _pedalsWheelSpinFadeWithThrottleEnabled = true;

	public bool PedalsWheelSpinFadeWithThrottleEnabled
	{
		get => _pedalsWheelSpinFadeWithThrottleEnabled;

		set
		{
			if ( value != _pedalsWheelSpinFadeWithThrottleEnabled )
			{
				_pedalsWheelSpinFadeWithThrottleEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches PedalsWheelSpinFadeWithThrottleEnabledContextSwitches { get; set; } = new( false, false, false, false, false );

	#endregion

	#region Pedals - Clutch slip start

	private float _pedalsClutchSlipStart = 0.25f;

	public float PedalsClutchSlipStart
	{
		get => _pedalsClutchSlipStart;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsClutchSlipStart )
			{
				_pedalsClutchSlipStart = value;

				OnPropertyChanged();

				PedalsClutchSlipEnd = MathF.Max( PedalsClutchSlipEnd, _pedalsClutchSlipStart );
			}

			UpdatePedalsClutchSlipStartString();
		}
	}

	private string _pedalsClutchSlipStartString = string.Empty;

	[XmlIgnore]
	public string PedalsClutchSlipStartString
	{
		get => _pedalsClutchSlipStartString;

		set
		{
			if ( value != _pedalsClutchSlipStartString )
			{
				_pedalsClutchSlipStartString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsClutchSlipStartString()
	{
		PedalsClutchSlipStartString = $"{_pedalsClutchSlipStart * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsClutchSlipStartContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsClutchSlipStartPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchSlipStartMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch slip end

	private float _pedalsClutchSlipEnd = 0.75f;

	public float PedalsClutchSlipEnd
	{
		get => _pedalsClutchSlipEnd;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _pedalsClutchSlipEnd )
			{
				_pedalsClutchSlipEnd = value;

				OnPropertyChanged();

				PedalsClutchSlipStart = MathF.Min( PedalsClutchSlipStart, _pedalsClutchSlipEnd );
			}

			UpdatePedalsClutchSlipEndString();
		}
	}

	private string _pedalsClutchSlipEndString = string.Empty;

	[XmlIgnore]
	public string PedalsClutchSlipEndString
	{
		get => _pedalsClutchSlipEndString;

		set
		{
			if ( value != _pedalsClutchSlipEndString )
			{
				_pedalsClutchSlipEndString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsClutchSlipEndString()
	{
		PedalsClutchSlipEndString = $"{_pedalsClutchSlipEnd * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ContextSwitches PedalsClutchSlipEndContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsClutchSlipEndPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchSlipEndMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Clutch slip frequency

	private float _pedalsClutchSlipFrequency = 1f;

	public float PedalsClutchSlipFrequency
	{
		get => _pedalsClutchSlipFrequency;

		set
		{
			value = Math.Clamp( value, 0.05f, 1f );

			if ( value != _pedalsClutchSlipFrequency )
			{
				_pedalsClutchSlipFrequency = value;

				OnPropertyChanged();
			}

			UpdatePedalsClutchSlipFrequencyString();
		}
	}

	private string _pedalsClutchSlipFrequencyString = string.Empty;

	[XmlIgnore]
	public string PedalsClutchSlipFrequencyString
	{
		get => _pedalsClutchSlipFrequencyString;

		set
		{
			if ( value != _pedalsClutchSlipFrequencyString )
			{
				_pedalsClutchSlipFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsClutchSlipFrequencyString()
	{
		var convertedToHertz = Math.Round( MathZ.Lerp( PedalsMinimumFrequency, PedalsMaximumFrequency, _pedalsClutchSlipFrequency ) );

		PedalsClutchSlipFrequencyString = $"{_pedalsClutchSlipFrequency * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]} ({convertedToHertz:F0}{DataContext.Instance.Localization[ "HertzUnits" ]})";
	}

	public ContextSwitches PedalsClutchSlipFrequencyContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsClutchSlipFrequencyPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsClutchSlipFrequencyMinusButtonMappings { get; set; } = new();

	#endregion

	#region Pedals - Noise damper

	private float _pedalsNoiseDamper = 0.1f;

	public float PedalsNoiseDamper
	{
		get => _pedalsNoiseDamper;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _pedalsNoiseDamper )
			{
				_pedalsNoiseDamper = value;

				OnPropertyChanged();
			}

			UpdatePedalsNoiseDamperString();
		}
	}

	private string _pedalsNoiseDamperString = string.Empty;

	[XmlIgnore]
	public string PedalsNoiseDamperString
	{
		get => _pedalsNoiseDamperString;

		set
		{
			if ( value != _pedalsNoiseDamperString )
			{
				_pedalsNoiseDamperString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdatePedalsNoiseDamperString()
	{
		if ( _pedalsNoiseDamper == 0f )
		{
			PedalsNoiseDamperString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			PedalsNoiseDamperString = $"{_pedalsNoiseDamper * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches PedalsNoiseDamperContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings PedalsNoiseDamperPlusButtonMappings { get; set; } = new();
	public ButtonMappings PedalsNoiseDamperMinusButtonMappings { get; set; } = new();

	#endregion

	#region Wind - Connect on startup

	private bool _windConnectOnStartup = false;

	public bool WindConnectOnStartup
	{
		get => _windConnectOnStartup;

		set
		{
			if ( value != _windConnectOnStartup )
			{
				_windConnectOnStartup = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Wind - Master wind power

	private float _windMasterWindPower = 1f;

	public float WindMasterWindPower
	{
		get => _windMasterWindPower;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windMasterWindPower )
			{
				_windMasterWindPower = value;

				OnPropertyChanged();
			}

			UpdateWindMasterWindPowerString();
		}
	}

	private string _windMasterWindPowerString = string.Empty;

	[XmlIgnore]
	public string WindMasterWindPowerString
	{
		get => _windMasterWindPowerString;

		set
		{
			if ( value != _windMasterWindPowerString )
			{
				_windMasterWindPowerString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindMasterWindPowerString()
	{
		if ( _windMasterWindPower == 0f )
		{
			WindMasterWindPowerString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			WindMasterWindPowerString = $"{_windMasterWindPower * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches WindMasterWindPowerContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings WindMasterWindPowerPlusButtonMappings { get; set; } = new();
	public ButtonMappings WindMasterWindPowerMinusButtonMappings { get; set; } = new();

	#endregion

	#region Wind - Minimum speed

	private float _windMinimumSpeed = 0f;

	public float WindMinimumSpeed
	{
		get => _windMinimumSpeed;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windMinimumSpeed )
			{
				_windMinimumSpeed = value;

				OnPropertyChanged();
			}

			UpdateWindMinimumSpeedString();
		}
	}

	private string _windMinimumSpeedString = string.Empty;

	[XmlIgnore]
	public string WindMinimumSpeedString
	{
		get => _windMinimumSpeedString;

		set
		{
			if ( value != _windMinimumSpeedString )
			{
				_windMinimumSpeedString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindMinimumSpeedString()
	{
		if ( _windMinimumSpeed == 0f )
		{
			WindMinimumSpeedString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			var app = App.Instance!;

			if ( app.Simulator.DisplayUnits == 0 )
			{
				WindMinimumSpeedString = $"{_windMinimumSpeed * MathZ.MPSToMPH:F0} {DataContext.Instance.Localization[ "MPHUnits" ]}";
			}
			else
			{
				WindMinimumSpeedString = $"{_windMinimumSpeed * MathZ.MPSToKPH:F0} {DataContext.Instance.Localization[ "KPHUnits" ]}";
			}
		}
	}

	public ContextSwitches WindMinimumSpeedContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings WindMinimumSpeedPlusButtonMappings { get; set; } = new();
	public ButtonMappings WindMinimumSpeedMinusButtonMappings { get; set; } = new();

	#endregion

	#region Wind - Curving

	private float _windCurving = 1f;

	public float WindCurving
	{
		get => _windCurving;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windCurving )
			{
				_windCurving = value;

				OnPropertyChanged();
			}

			UpdateWindCurvingString();
		}
	}

	private string _windCurvingString = string.Empty;

	[XmlIgnore]
	public string WindCurvingString
	{
		get => _windCurvingString;

		set
		{
			if ( value != _windCurvingString )
			{
				_windCurvingString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindCurvingString()
	{
		if ( _windCurving == 0f )
		{
			WindCurvingString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			WindCurvingString = $"{_windCurving * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches WindCurvingContextSwitches { get; set; } = new( false, false, false, false, false );
	public ButtonMappings WindCurvingPlusButtonMappings { get; set; } = new();
	public ButtonMappings WindCurvingMinusButtonMappings { get; set; } = new();

	#endregion

	#region Wind - Speed 1

	private float _windSpeed1 = 0f;

	public float WindSpeed1
	{
		get => _windSpeed1;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windSpeed1 )
			{
				_windSpeed1 = value;

				OnPropertyChanged();

				WindSpeed2 = MathF.Max( WindSpeed2, _windSpeed1 );
			}

			UpdateWindSpeed1String();
		}
	}

	private string _windSpeed1String = string.Empty;

	[XmlIgnore]
	public string WindSpeed1String
	{
		get => _windSpeed1String;

		set
		{
			if ( value != _windSpeed1String )
			{
				_windSpeed1String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindSpeed1String()
	{
		var app = App.Instance!;

		if ( app.Simulator.DisplayUnits == 0 )
		{
			WindSpeed1String = $"{_windSpeed1 * MathZ.MPSToMPH:F0}";
		}
		else
		{
			WindSpeed1String = $"{_windSpeed1 * MathZ.MPSToKPH:F0}";
		}
	}

	#endregion

	#region Wind - Fan power 1

	private float _windFanPower1 = 0f;

	public float WindFanPower1
	{
		get => _windFanPower1;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower1 )
			{
				_windFanPower1 = value;

				OnPropertyChanged();
			}

			UpdateWindFanPower1String();
		}
	}

	private string _windFanPower1String = string.Empty;

	[XmlIgnore]
	public string WindFanPower1String
	{
		get => _windFanPower1String;

		set
		{
			if ( value != _windFanPower1String )
			{
				_windFanPower1String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindFanPower1String()
	{
		WindFanPower1String = $"{_windFanPower1 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Wind - Speed 2

	private float _windSpeed2 = 3.313f;

	public float WindSpeed2
	{
		get => _windSpeed2;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windSpeed2 )
			{
				_windSpeed2 = value;

				OnPropertyChanged();

				WindSpeed1 = MathF.Min( WindSpeed1, _windSpeed2 );
				WindSpeed3 = MathF.Max( WindSpeed3, _windSpeed2 );
			}

			UpdateWindSpeed2String();
		}
	}

	private string _windSpeed2String = string.Empty;

	[XmlIgnore]
	public string WindSpeed2String
	{
		get => _windSpeed2String;

		set
		{
			if ( value != _windSpeed2String )
			{
				_windSpeed2String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindSpeed2String()
	{
		var app = App.Instance!;

		if ( app.Simulator.DisplayUnits == 0 )
		{
			WindSpeed2String = $"{_windSpeed2 * MathZ.MPSToMPH:F0}";
		}
		else
		{
			WindSpeed2String = $"{_windSpeed2 * MathZ.MPSToKPH:F0}";
		}
	}

	#endregion

	#region Wind - Fan power 2

	private float _windFanPower2 = 0.125f;

	public float WindFanPower2
	{
		get => _windFanPower2;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower2 )
			{
				_windFanPower2 = value;

				OnPropertyChanged();
			}

			UpdateWindFanPower2String();
		}
	}

	private string _windFanPower2String = string.Empty;

	[XmlIgnore]
	public string WindFanPower2String
	{
		get => _windFanPower2String;

		set
		{
			if ( value != _windFanPower2String )
			{
				_windFanPower2String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindFanPower2String()
	{
		WindFanPower2String = $"{_windFanPower2 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Wind - Speed 3

	private float _windSpeed3 = 9.373f;

	public float WindSpeed3
	{
		get => _windSpeed3;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windSpeed3 )
			{
				_windSpeed3 = value;

				OnPropertyChanged();

				WindSpeed2 = MathF.Min( WindSpeed2, _windSpeed3 );
				WindSpeed4 = MathF.Max( WindSpeed4, _windSpeed3 );
			}

			UpdateWindSpeed3String();
		}
	}

	private string _windSpeed3String = string.Empty;

	[XmlIgnore]
	public string WindSpeed3String
	{
		get => _windSpeed3String;

		set
		{
			if ( value != _windSpeed3String )
			{
				_windSpeed3String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindSpeed3String()
	{
		var app = App.Instance!;

		if ( app.Simulator.DisplayUnits == 0 )
		{
			WindSpeed3String = $"{_windSpeed3 * MathZ.MPSToMPH:F0}";
		}
		else
		{
			WindSpeed3String = $"{_windSpeed3 * MathZ.MPSToKPH:F0}";
		}
	}

	#endregion

	#region Wind - Fan power 3

	private float _windFanPower3 = 0.25f;

	public float WindFanPower3
	{
		get => _windFanPower3;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower3 )
			{
				_windFanPower3 = value;

				OnPropertyChanged();
			}

			UpdateWindFanPower3String();
		}
	}

	private string _windFanPower3String = string.Empty;

	[XmlIgnore]
	public string WindFanPower3String
	{
		get => _windFanPower3String;

		set
		{
			if ( value != _windFanPower3String )
			{
				_windFanPower3String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindFanPower3String()
	{
		WindFanPower3String = $"{_windFanPower3 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Wind - Speed 4

	private float _windSpeed4 = 17.208f;

	public float WindSpeed4
	{
		get => _windSpeed4;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windSpeed4 )
			{
				_windSpeed4 = value;

				OnPropertyChanged();

				WindSpeed3 = MathF.Min( WindSpeed3, _windSpeed4 );
				WindSpeed5 = MathF.Max( WindSpeed5, _windSpeed4 );
			}

			UpdateWindSpeed4String();
		}
	}

	private string _windSpeed4String = string.Empty;

	[XmlIgnore]
	public string WindSpeed4String
	{
		get => _windSpeed4String;

		set
		{
			if ( value != _windSpeed4String )
			{
				_windSpeed4String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindSpeed4String()
	{
		var app = App.Instance!;

		if ( app.Simulator.DisplayUnits == 0 )
		{
			WindSpeed4String = $"{_windSpeed4 * MathZ.MPSToMPH:F0}";
		}
		else
		{
			WindSpeed4String = $"{_windSpeed4 * MathZ.MPSToKPH:F0}";
		}
	}

	#endregion

	#region Wind - Fan power 4

	private float _windFanPower4 = 0.375f;

	public float WindFanPower4
	{
		get => _windFanPower4;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower4 )
			{
				_windFanPower4 = value;

				OnPropertyChanged();
			}

			UpdateWindFanPower4String();
		}
	}

	private string _windFanPower4String = string.Empty;

	[XmlIgnore]
	public string WindFanPower4String
	{
		get => _windFanPower4String;

		set
		{
			if ( value != _windFanPower4String )
			{
				_windFanPower4String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindFanPower4String()
	{
		WindFanPower4String = $"{_windFanPower4 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Wind - Speed 5

	private float _windSpeed5 = 26.494f;

	public float WindSpeed5
	{
		get => _windSpeed5;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windSpeed5 )
			{
				_windSpeed5 = value;

				OnPropertyChanged();

				WindSpeed4 = MathF.Min( WindSpeed4, _windSpeed5 );
				WindSpeed6 = MathF.Max( WindSpeed6, _windSpeed5 );
			}

			UpdateWindSpeed5String();
		}
	}

	private string _windSpeed5String = string.Empty;

	[XmlIgnore]
	public string WindSpeed5String
	{
		get => _windSpeed5String;

		set
		{
			if ( value != _windSpeed5String )
			{
				_windSpeed5String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindSpeed5String()
	{
		var app = App.Instance!;

		if ( app.Simulator.DisplayUnits == 0 )
		{
			WindSpeed5String = $"{_windSpeed5 * MathZ.MPSToMPH:F0}";
		}
		else
		{
			WindSpeed5String = $"{_windSpeed5 * MathZ.MPSToKPH:F0}";
		}
	}

	#endregion

	#region Wind - Fan power 5

	private float _windFanPower5 = 0.5f;

	public float WindFanPower5
	{
		get => _windFanPower5;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower5 )
			{
				_windFanPower5 = value;

				OnPropertyChanged();
			}

			UpdateWindFanPower5String();
		}
	}

	private string _windFanPower5String = string.Empty;

	[XmlIgnore]
	public string WindFanPower5String
	{
		get => _windFanPower5String;

		set
		{
			if ( value != _windFanPower5String )
			{
				_windFanPower5String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindFanPower5String()
	{
		WindFanPower5String = $"{_windFanPower5 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Wind - Speed 6

	private float _windSpeed6 = 37.047f;

	public float WindSpeed6
	{
		get => _windSpeed6;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windSpeed6 )
			{
				_windSpeed6 = value;

				OnPropertyChanged();

				WindSpeed5 = MathF.Min( WindSpeed5, _windSpeed6 );
				WindSpeed7 = MathF.Max( WindSpeed7, _windSpeed6 );
			}

			UpdateWindSpeed6String();
		}
	}

	private string _windSpeed6String = string.Empty;

	[XmlIgnore]
	public string WindSpeed6String
	{
		get => _windSpeed6String;

		set
		{
			if ( value != _windSpeed6String )
			{
				_windSpeed6String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindSpeed6String()
	{
		var app = App.Instance!;

		if ( app.Simulator.DisplayUnits == 0 )
		{
			WindSpeed6String = $"{_windSpeed6 * MathZ.MPSToMPH:F0}";
		}
		else
		{
			WindSpeed6String = $"{_windSpeed6 * MathZ.MPSToKPH:F0}";
		}
	}

	#endregion

	#region Wind - Fan power 6

	private float _windFanPower6 = 0.625f;

	public float WindFanPower6
	{
		get => _windFanPower6;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower6 )
			{
				_windFanPower6 = value;

				OnPropertyChanged();
			}

			UpdateWindFanPower6String();
		}
	}

	private string _windFanPower6String = string.Empty;

	[XmlIgnore]
	public string WindFanPower6String
	{
		get => _windFanPower6String;

		set
		{
			if ( value != _windFanPower6String )
			{
				_windFanPower6String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindFanPower6String()
	{
		WindFanPower6String = $"{_windFanPower6 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Wind - Speed 7

	private float _windSpeed7 = 48.672f;

	public float WindSpeed7
	{
		get => _windSpeed7;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windSpeed7 )
			{
				_windSpeed7 = value;

				OnPropertyChanged();

				WindSpeed6 = MathF.Min( WindSpeed6, _windSpeed7 );
				WindSpeed8 = MathF.Max( WindSpeed8, _windSpeed7 );
			}

			UpdateWindSpeed7String();
		}
	}

	private string _windSpeed7String = string.Empty;

	[XmlIgnore]
	public string WindSpeed7String
	{
		get => _windSpeed7String;

		set
		{
			if ( value != _windSpeed7String )
			{
				_windSpeed7String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindSpeed7String()
	{
		var app = App.Instance!;

		if ( app.Simulator.DisplayUnits == 0 )
		{
			WindSpeed7String = $"{_windSpeed7 * MathZ.MPSToMPH:F0}";
		}
		else
		{
			WindSpeed7String = $"{_windSpeed7 * MathZ.MPSToKPH:F0}";
		}
	}

	#endregion

	#region Wind - Fan power 7

	private float _windFanPower7 = 0.75f;

	public float WindFanPower7
	{
		get => _windFanPower7;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower7 )
			{
				_windFanPower7 = value;

				OnPropertyChanged();
			}

			UpdateWindFanPower7String();
		}
	}

	private string _windFanPower7String = string.Empty;

	[XmlIgnore]
	public string WindFanPower7String
	{
		get => _windFanPower7String;

		set
		{
			if ( value != _windFanPower7String )
			{
				_windFanPower7String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindFanPower7String()
	{
		WindFanPower7String = $"{_windFanPower7 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Wind - Speed 8

	private float _windSpeed8 = 61.374f;

	public float WindSpeed8
	{
		get => _windSpeed8;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windSpeed8 )
			{
				_windSpeed8 = value;

				OnPropertyChanged();

				WindSpeed7 = MathF.Min( WindSpeed7, _windSpeed8 );
				WindSpeed9 = MathF.Max( WindSpeed9, _windSpeed8 );
			}

			UpdateWindSpeed8String();
		}
	}

	private string _windSpeed8String = string.Empty;

	[XmlIgnore]
	public string WindSpeed8String
	{
		get => _windSpeed8String;

		set
		{
			if ( value != _windSpeed8String )
			{
				_windSpeed8String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindSpeed8String()
	{
		var app = App.Instance!;

		if ( app.Simulator.DisplayUnits == 0 )
		{
			WindSpeed8String = $"{_windSpeed8 * MathZ.MPSToMPH:F0}";
		}
		else
		{
			WindSpeed8String = $"{_windSpeed8 * MathZ.MPSToKPH:F0}";
		}
	}

	#endregion

	#region Wind - Fan power 8

	private float _windFanPower8 = 0.8333f;

	public float WindFanPower8
	{
		get => _windFanPower8;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower8 )
			{
				_windFanPower8 = value;

				OnPropertyChanged();
			}

			UpdateWindFanPower8String();
		}
	}

	private string _windFanPower8String = string.Empty;

	[XmlIgnore]
	public string WindFanPower8String
	{
		get => _windFanPower8String;

		set
		{
			if ( value != _windFanPower8String )
			{
				_windFanPower8String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindFanPower8String()
	{
		WindFanPower8String = $"{_windFanPower8 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Wind - Speed 9

	private float _windSpeed9 = 74.935f;

	public float WindSpeed9
	{
		get => _windSpeed9;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windSpeed9 )
			{
				_windSpeed9 = value;

				OnPropertyChanged();

				WindSpeed8 = MathF.Min( WindSpeed8, _windSpeed9 );
				WindSpeed10 = MathF.Max( WindSpeed10, _windSpeed9 );
			}

			UpdateWindSpeed9String();
		}
	}

	private string _windSpeed9String = string.Empty;

	[XmlIgnore]
	public string WindSpeed9String
	{
		get => _windSpeed9String;

		set
		{
			if ( value != _windSpeed9String )
			{
				_windSpeed9String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindSpeed9String()
	{
		var app = App.Instance!;

		if ( app.Simulator.DisplayUnits == 0 )
		{
			WindSpeed9String = $"{_windSpeed9 * MathZ.MPSToMPH:F0}";
		}
		else
		{
			WindSpeed9String = $"{_windSpeed9 * MathZ.MPSToKPH:F0}";
		}
	}

	#endregion

	#region Wind - Fan power 9

	private float _windFanPower9 = 0.9167f;

	public float WindFanPower9
	{
		get => _windFanPower9;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower9 )
			{
				_windFanPower9 = value;

				OnPropertyChanged();
			}

			UpdateWindFanPower9String();
		}
	}

	private string _windFanPower9String = string.Empty;

	[XmlIgnore]
	public string WindFanPower9String
	{
		get => _windFanPower9String;

		set
		{
			if ( value != _windFanPower9String )
			{
				_windFanPower9String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindFanPower9String()
	{
		WindFanPower9String = $"{_windFanPower9 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Wind - Speed 10

	private float _windSpeed10 = 89.408f;

	public float WindSpeed10
	{
		get => _windSpeed10;

		set
		{
			value = Math.Clamp( value, 0f, 100f );

			if ( value != _windSpeed10 )
			{
				_windSpeed10 = value;

				OnPropertyChanged();

				WindSpeed9 = MathF.Min( WindSpeed9, _windSpeed10 );
			}

			UpdateWindSpeed10String();
		}
	}

	private string _windSpeed10String = string.Empty;

	[XmlIgnore]
	public string WindSpeed10String
	{
		get => _windSpeed10String;

		set
		{
			if ( value != _windSpeed10String )
			{
				_windSpeed10String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindSpeed10String()
	{
		var app = App.Instance!;

		if ( app.Simulator.DisplayUnits == 0 )
		{
			WindSpeed10String = $"{_windSpeed10 * MathZ.MPSToMPH:F0}";
		}
		else
		{
			WindSpeed10String = $"{_windSpeed10 * MathZ.MPSToKPH:F0}";
		}
	}

	#endregion

	#region Wind - Fan power 10

	private float _windFanPower10 = 1f;

	public float WindFanPower10
	{
		get => _windFanPower10;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _windFanPower10 )
			{
				_windFanPower10 = value;

				OnPropertyChanged();
			}

			UpdateWindFanPower10String();
		}
	}

	private string _windFanPower10String = string.Empty;

	[XmlIgnore]
	public string WindFanPower10String
	{
		get => _windFanPower10String;

		set
		{
			if ( value != _windFanPower10String )
			{
				_windFanPower10String = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateWindFanPower10String()
	{
		WindFanPower10String = $"{_windFanPower10 * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Seat Belt Tensioner - Connect on startup

	private bool _seatBeltTensionerConnectOnStartup = false;

	public bool SeatBeltTensionerConnectOnStartup
	{
		get => _seatBeltTensionerConnectOnStartup;

		set
		{
			if ( value != _seatBeltTensionerConnectOnStartup )
			{
				_seatBeltTensionerConnectOnStartup = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Seat Belt Tensioner - Minimum

	private float _seatBeltTensionerMinimum = 60f;

	public float SeatBeltTensionerMinimum
	{
		get => _seatBeltTensionerMinimum;

		set
		{
			value = Math.Clamp( value, 0f, 90f );

			if ( value != _seatBeltTensionerMinimum )
			{
				_seatBeltTensionerMinimum = value;

				OnPropertyChanged();

				SeatBeltTensionerNeutral = MathF.Max( SeatBeltTensionerNeutral, _seatBeltTensionerMinimum );

				App.Instance?.SeatBeltTensioner.SendCalibration();
			}

			UpdateSeatBeltTensionerMinimumString();
		}
	}

	private string _seatBeltTensionerMinimumString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerMinimumString
	{
		get => _seatBeltTensionerMinimumString;

		set
		{
			if ( value != _seatBeltTensionerMinimumString )
			{
				_seatBeltTensionerMinimumString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerMinimumString()
	{
		SeatBeltTensionerMinimumString = $"{_seatBeltTensionerMinimum:F1}{DataContext.Instance.Localization[ "Degrees" ]}";
	}

	#endregion

	#region Seat Belt Tensioner - Neutral

	private float _seatBeltTensionerNeutral = 90f;

	public float SeatBeltTensionerNeutral
	{
		get => _seatBeltTensionerNeutral;

		set
		{
			value = Math.Clamp( value, _seatBeltTensionerMinimum, _seatBeltTensionerMaximum );

			if ( value != _seatBeltTensionerNeutral )
			{
				_seatBeltTensionerNeutral = value;

				OnPropertyChanged();

				App.Instance?.SeatBeltTensioner.SendCalibration();
			}

			UpdateSeatBeltTensionerNeutralString();
		}
	}

	private string _seatBeltTensionerNeutralString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerNeutralString
	{
		get => _seatBeltTensionerNeutralString;

		set
		{
			if ( value != _seatBeltTensionerNeutralString )
			{
				_seatBeltTensionerNeutralString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerNeutralString()
	{
		SeatBeltTensionerNeutralString = $"{_seatBeltTensionerNeutral:F1}{DataContext.Instance.Localization[ "Degrees" ]}";
	}

	#endregion

	#region Seat Belt Tensioner - Maximum

	private float _seatBeltTensionerMaximum = 120f;

	public float SeatBeltTensionerMaximum
	{
		get => _seatBeltTensionerMaximum;

		set
		{
			value = Math.Clamp( value, 90f, 180f );

			if ( value != _seatBeltTensionerMaximum )
			{
				_seatBeltTensionerMaximum = value;

				OnPropertyChanged();

				SeatBeltTensionerNeutral = MathF.Min( SeatBeltTensionerNeutral, _seatBeltTensionerMaximum );

				App.Instance?.SeatBeltTensioner.SendCalibration();
			}

			UpdateSeatBeltTensionerMaximumString();
		}
	}

	private string _seatBeltTensionerMaximumString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerMaximumString
	{
		get => _seatBeltTensionerMaximumString;

		set
		{
			if ( value != _seatBeltTensionerMaximumString )
			{
				_seatBeltTensionerMaximumString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerMaximumString()
	{
		SeatBeltTensionerMaximumString = $"{_seatBeltTensionerMaximum:F1}{DataContext.Instance.Localization[ "Degrees" ]}";
	}

	#endregion

	#region Seat Belt Tensioner - Max Motor Speed

	private float _seatBeltTensionerMaxMotorSpeed = 180f;

	public float SeatBeltTensionerMaxMotorSpeed
	{
		get => _seatBeltTensionerMaxMotorSpeed;

		set
		{
			value = Math.Clamp( value, 5f, 240f );

			if ( value != _seatBeltTensionerMaxMotorSpeed )
			{
				_seatBeltTensionerMaxMotorSpeed = value;

				OnPropertyChanged();

				App.Instance?.SeatBeltTensioner.SendMaxMovement();
			}

			UpdateSeatBeltTensionerMaxMotorSpeedString();
		}
	}

	private string _seatBeltTensionerMaxMotorSpeedString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerMaxMotorSpeedString
	{
		get => _seatBeltTensionerMaxMotorSpeedString;

		set
		{
			if ( value != _seatBeltTensionerMaxMotorSpeedString )
			{
				_seatBeltTensionerMaxMotorSpeedString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerMaxMotorSpeedString()
	{
		SeatBeltTensionerMaxMotorSpeedString = $"{(int) MathF.Round( _seatBeltTensionerMaxMotorSpeed )}{DataContext.Instance.Localization[ "DegreesPerSecond" ]}";
	}

	#endregion

	#region Seat Belt Tensioner - Inverted Arms

	private bool _seatBeltTensionerInvertedArms = false;

	public bool SeatBeltTensionerInvertedArms
	{
		get => _seatBeltTensionerInvertedArms;

		set
		{
			if ( value != _seatBeltTensionerInvertedArms )
			{
				_seatBeltTensionerInvertedArms = value;

				OnPropertyChanged();

				App.Instance?.SeatBeltTensioner.SendInvertedArms();
			}
		}
	}

	#endregion

	#region Seat Belt Tensioner - Surge Mode

	private Components.SeatBeltTensioner.AxisMode _seatBeltTensionerSurgeMode = Components.SeatBeltTensioner.AxisMode.Normal;

	public Components.SeatBeltTensioner.AxisMode SeatBeltTensionerSurgeMode
	{
		get => _seatBeltTensionerSurgeMode;

		set
		{
			if ( value != _seatBeltTensionerSurgeMode )
			{
				_seatBeltTensionerSurgeMode = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SeatBeltTensionerSurgeModeContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Surge Subtract Gravity

	private bool _seatBeltTensionerSurgeSubtractGravity = true;

	public bool SeatBeltTensionerSurgeSubtractGravity
	{
		get => _seatBeltTensionerSurgeSubtractGravity;

		set
		{
			if ( value != _seatBeltTensionerSurgeSubtractGravity )
			{
				_seatBeltTensionerSurgeSubtractGravity = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SeatBeltTensionerSurgeSubtractGravityContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Surge Max G

	private float _seatBeltTensionerSurgeMaxG = 4f;

	public float SeatBeltTensionerSurgeMaxG
	{
		get => _seatBeltTensionerSurgeMaxG;

		set
		{
			value = Math.Clamp( value, 0.1f, 50f );

			if ( value != _seatBeltTensionerSurgeMaxG )
			{
				_seatBeltTensionerSurgeMaxG = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerSurgeMaxGString();
		}
	}

	private string _seatBeltTensionerSurgeMaxGString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerSurgeMaxGString
	{
		get => _seatBeltTensionerSurgeMaxGString;

		set
		{
			if ( value != _seatBeltTensionerSurgeMaxGString )
			{
				_seatBeltTensionerSurgeMaxGString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerSurgeMaxGString()
	{
		SeatBeltTensionerSurgeMaxGString = $"{_seatBeltTensionerSurgeMaxG:F2}{DataContext.Instance.Localization[ "GForceUnits" ]}";
	}

	public ContextSwitches SeatBeltTensionerSurgeMaxGContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Surge Dead Zone

	private float _seatBeltTensionerSurgeDeadZone = 0f;

	public float SeatBeltTensionerSurgeDeadZone
	{
		get => _seatBeltTensionerSurgeDeadZone;

		set
		{
			value = Math.Clamp( value, 0f, 0.99f );

			if ( value != _seatBeltTensionerSurgeDeadZone )
			{
				_seatBeltTensionerSurgeDeadZone = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerSurgeDeadZoneString();
		}
	}

	private string _seatBeltTensionerSurgeDeadZoneString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerSurgeDeadZoneString
	{
		get => _seatBeltTensionerSurgeDeadZoneString;

		set
		{
			if ( value != _seatBeltTensionerSurgeDeadZoneString )
			{
				_seatBeltTensionerSurgeDeadZoneString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerSurgeDeadZoneString()
	{
		SeatBeltTensionerSurgeDeadZoneString = $"{_seatBeltTensionerSurgeDeadZone * 100f:F0}%";
	}

	public ContextSwitches SeatBeltTensionerSurgeDeadZoneContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Surge Smoothing

	private float _seatBeltTensionerSurgeSmoothing = 0f;

	public float SeatBeltTensionerSurgeSmoothing
	{
		get => _seatBeltTensionerSurgeSmoothing;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _seatBeltTensionerSurgeSmoothing )
			{
				_seatBeltTensionerSurgeSmoothing = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerSurgeSmoothingString();
		}
	}

	private string _seatBeltTensionerSurgeSmoothingString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerSurgeSmoothingString
	{
		get => _seatBeltTensionerSurgeSmoothingString;

		set
		{
			if ( value != _seatBeltTensionerSurgeSmoothingString )
			{
				_seatBeltTensionerSurgeSmoothingString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerSurgeSmoothingString()
	{
		SeatBeltTensionerSurgeSmoothingString = $"{_seatBeltTensionerSurgeSmoothing * 100f:F0}%";
	}

	public ContextSwitches SeatBeltTensionerSurgeSmoothingContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Surge Curve

	private float _seatBeltTensionerSurgeCurve = 0f;

	public float SeatBeltTensionerSurgeCurve
	{
		get => _seatBeltTensionerSurgeCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _seatBeltTensionerSurgeCurve )
			{
				_seatBeltTensionerSurgeCurve = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerSurgeCurveString();
		}
	}

	private string _seatBeltTensionerSurgeCurveString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerSurgeCurveString
	{
		get => _seatBeltTensionerSurgeCurveString;

		set
		{
			if ( value != _seatBeltTensionerSurgeCurveString )
			{
				_seatBeltTensionerSurgeCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerSurgeCurveString()
	{
		if ( _seatBeltTensionerSurgeCurve == 0f )
		{
			SeatBeltTensionerSurgeCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SeatBeltTensionerSurgeCurveString = $"{_seatBeltTensionerSurgeCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SeatBeltTensionerSurgeCurveContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Sway Mode

	private Components.SeatBeltTensioner.AxisMode _seatBeltTensionerSwayMode = Components.SeatBeltTensioner.AxisMode.Normal;

	public Components.SeatBeltTensioner.AxisMode SeatBeltTensionerSwayMode
	{
		get => _seatBeltTensionerSwayMode;

		set
		{
			if ( value != _seatBeltTensionerSwayMode )
			{
				_seatBeltTensionerSwayMode = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SeatBeltTensionerSwayModeContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Sway Subtract Gravity

	private bool _seatBeltTensionerSwaySubtractGravity = true;

	public bool SeatBeltTensionerSwaySubtractGravity
	{
		get => _seatBeltTensionerSwaySubtractGravity;

		set
		{
			if ( value != _seatBeltTensionerSwaySubtractGravity )
			{
				_seatBeltTensionerSwaySubtractGravity = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SeatBeltTensionerSwaySubtractGravityContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Sway Max G

	private float _seatBeltTensionerSwayMaxG = 2f;

	public float SeatBeltTensionerSwayMaxG
	{
		get => _seatBeltTensionerSwayMaxG;

		set
		{
			value = Math.Clamp( value, 0.1f, 50f );

			if ( value != _seatBeltTensionerSwayMaxG )
			{
				_seatBeltTensionerSwayMaxG = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerSwayMaxGString();
		}
	}

	private string _seatBeltTensionerSwayMaxGString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerSwayMaxGString
	{
		get => _seatBeltTensionerSwayMaxGString;

		set
		{
			if ( value != _seatBeltTensionerSwayMaxGString )
			{
				_seatBeltTensionerSwayMaxGString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerSwayMaxGString()
	{
		SeatBeltTensionerSwayMaxGString = $"{_seatBeltTensionerSwayMaxG:F2}{DataContext.Instance.Localization[ "GForceUnits" ]}";
	}

	public ContextSwitches SeatBeltTensionerSwayMaxGContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Sway Dead Zone

	private float _seatBeltTensionerSwayDeadZone = 0.05f;

	public float SeatBeltTensionerSwayDeadZone
	{
		get => _seatBeltTensionerSwayDeadZone;

		set
		{
			value = Math.Clamp( value, 0f, 0.99f );

			if ( value != _seatBeltTensionerSwayDeadZone )
			{
				_seatBeltTensionerSwayDeadZone = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerSwayDeadZoneString();
		}
	}

	private string _seatBeltTensionerSwayDeadZoneString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerSwayDeadZoneString
	{
		get => _seatBeltTensionerSwayDeadZoneString;

		set
		{
			if ( value != _seatBeltTensionerSwayDeadZoneString )
			{
				_seatBeltTensionerSwayDeadZoneString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerSwayDeadZoneString()
	{
		SeatBeltTensionerSwayDeadZoneString = $"{_seatBeltTensionerSwayDeadZone * 100f:F0}%";
	}

	public ContextSwitches SeatBeltTensionerSwayDeadZoneContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Sway Smoothing

	private float _seatBeltTensionerSwaySmoothing = 0f;

	public float SeatBeltTensionerSwaySmoothing
	{
		get => _seatBeltTensionerSwaySmoothing;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _seatBeltTensionerSwaySmoothing )
			{
				_seatBeltTensionerSwaySmoothing = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerSwaySmoothingString();
		}
	}

	private string _seatBeltTensionerSwaySmoothingString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerSwaySmoothingString
	{
		get => _seatBeltTensionerSwaySmoothingString;

		set
		{
			if ( value != _seatBeltTensionerSwaySmoothingString )
			{
				_seatBeltTensionerSwaySmoothingString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerSwaySmoothingString()
	{
		SeatBeltTensionerSwaySmoothingString = $"{_seatBeltTensionerSwaySmoothing * 100f:F0}%";
	}

	public ContextSwitches SeatBeltTensionerSwaySmoothingContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Sway Curve

	private float _seatBeltTensionerSwayCurve = 0.15f;

	public float SeatBeltTensionerSwayCurve
	{
		get => _seatBeltTensionerSwayCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _seatBeltTensionerSwayCurve )
			{
				_seatBeltTensionerSwayCurve = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerSwayCurveString();
		}
	}

	private string _seatBeltTensionerSwayCurveString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerSwayCurveString
	{
		get => _seatBeltTensionerSwayCurveString;

		set
		{
			if ( value != _seatBeltTensionerSwayCurveString )
			{
				_seatBeltTensionerSwayCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerSwayCurveString()
	{
		if ( _seatBeltTensionerSwayCurve == 0f )
		{
			SeatBeltTensionerSwayCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SeatBeltTensionerSwayCurveString = $"{_seatBeltTensionerSwayCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SeatBeltTensionerSwayCurveContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Heave Mode

	private SeatBeltTensioner.AxisMode _seatBeltTensionerHeaveMode = SeatBeltTensioner.AxisMode.Normal;

	public SeatBeltTensioner.AxisMode SeatBeltTensionerHeaveMode
	{
		get => _seatBeltTensionerHeaveMode;

		set
		{
			if ( value != _seatBeltTensionerHeaveMode )
			{
				_seatBeltTensionerHeaveMode = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SeatBeltTensionerHeaveModeContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Heave Subtract Gravity

	private bool _seatBeltTensionerHeaveSubtractGravity = true;

	public bool SeatBeltTensionerHeaveSubtractGravity
	{
		get => _seatBeltTensionerHeaveSubtractGravity;

		set
		{
			if ( value != _seatBeltTensionerHeaveSubtractGravity )
			{
				_seatBeltTensionerHeaveSubtractGravity = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SeatBeltTensionerHeaveSubtractGravityContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Heave Max G

	private float _seatBeltTensionerHeaveMaxG = 1.5f;

	public float SeatBeltTensionerHeaveMaxG
	{
		get => _seatBeltTensionerHeaveMaxG;

		set
		{
			value = Math.Clamp( value, 0.1f, 50f );

			if ( value != _seatBeltTensionerHeaveMaxG )
			{
				_seatBeltTensionerHeaveMaxG = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerHeaveMaxGString();
		}
	}

	private string _seatBeltTensionerHeaveMaxGString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerHeaveMaxGString
	{
		get => _seatBeltTensionerHeaveMaxGString;

		set
		{
			if ( value != _seatBeltTensionerHeaveMaxGString )
			{
				_seatBeltTensionerHeaveMaxGString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerHeaveMaxGString()
	{
		SeatBeltTensionerHeaveMaxGString = $"{_seatBeltTensionerHeaveMaxG:F2}{DataContext.Instance.Localization[ "GForceUnits" ]}";
	}

	public ContextSwitches SeatBeltTensionerHeaveMaxGContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Heave Dead Zone

	private float _seatBeltTensionerHeaveDeadZone = 0.05f;

	public float SeatBeltTensionerHeaveDeadZone
	{
		get => _seatBeltTensionerHeaveDeadZone;

		set
		{
			value = Math.Clamp( value, 0f, 0.99f );

			if ( value != _seatBeltTensionerHeaveDeadZone )
			{
				_seatBeltTensionerHeaveDeadZone = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerHeaveDeadZoneString();
		}
	}

	private string _seatBeltTensionerHeaveDeadZoneString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerHeaveDeadZoneString
	{
		get => _seatBeltTensionerHeaveDeadZoneString;

		set
		{
			if ( value != _seatBeltTensionerHeaveDeadZoneString )
			{
				_seatBeltTensionerHeaveDeadZoneString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerHeaveDeadZoneString()
	{
		SeatBeltTensionerHeaveDeadZoneString = $"{_seatBeltTensionerHeaveDeadZone * 100f:F0}%";
	}

	public ContextSwitches SeatBeltTensionerHeaveDeadZoneContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Heave Smoothing

	private float _seatBeltTensionerHeaveSmoothing = 0.1f;

	public float SeatBeltTensionerHeaveSmoothing
	{
		get => _seatBeltTensionerHeaveSmoothing;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _seatBeltTensionerHeaveSmoothing )
			{
				_seatBeltTensionerHeaveSmoothing = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerHeaveSmoothingString();
		}
	}

	private string _seatBeltTensionerHeaveSmoothingString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerHeaveSmoothingString
	{
		get => _seatBeltTensionerHeaveSmoothingString;

		set
		{
			if ( value != _seatBeltTensionerHeaveSmoothingString )
			{
				_seatBeltTensionerHeaveSmoothingString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerHeaveSmoothingString()
	{
		SeatBeltTensionerHeaveSmoothingString = $"{_seatBeltTensionerHeaveSmoothing * 100f:F0}%";
	}

	public ContextSwitches SeatBeltTensionerHeaveSmoothingContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Heave Curve

	private float _seatBeltTensionerHeaveCurve = 0.15f;

	public float SeatBeltTensionerHeaveCurve
	{
		get => _seatBeltTensionerHeaveCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _seatBeltTensionerHeaveCurve )
			{
				_seatBeltTensionerHeaveCurve = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerHeaveCurveString();
		}
	}

	private string _seatBeltTensionerHeaveCurveString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerHeaveCurveString
	{
		get => _seatBeltTensionerHeaveCurveString;

		set
		{
			if ( value != _seatBeltTensionerHeaveCurveString )
			{
				_seatBeltTensionerHeaveCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerHeaveCurveString()
	{
		if ( _seatBeltTensionerHeaveCurve == 0f )
		{
			SeatBeltTensionerHeaveCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SeatBeltTensionerHeaveCurveString = $"{_seatBeltTensionerHeaveCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SeatBeltTensionerHeaveCurveContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - Seat of Pants Effect

	private SeatBeltTensioner.AxisMode _seatBeltTensionerSeatOfPantsMode = SeatBeltTensioner.AxisMode.Normal;

	public SeatBeltTensioner.AxisMode SeatBeltTensionerSeatOfPantsMode
	{
		get => _seatBeltTensionerSeatOfPantsMode;

		set
		{
			if ( value != _seatBeltTensionerSeatOfPantsMode )
			{
				_seatBeltTensionerSeatOfPantsMode = value;

				OnPropertyChanged();
			}
		}
	}

	public ContextSwitches SeatBeltTensionerSeatOfPantsModeContextSwitches { get; set; } = new( false, false, false, false, false );

	private float _seatBeltTensionerSeatOfPantsAmplitude = 120f;

	public float SeatBeltTensionerSeatOfPantsAmplitude
	{
		get => _seatBeltTensionerSeatOfPantsAmplitude;

		set
		{
			value = Math.Clamp( value, 0f, 120f );

			if ( value != _seatBeltTensionerSeatOfPantsAmplitude )
			{
				_seatBeltTensionerSeatOfPantsAmplitude = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerSeatOfPantsAmplitudeString();
		}
	}

	private string _seatBeltTensionerSeatOfPantsAmplitudeString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerSeatOfPantsAmplitudeString
	{
		get => _seatBeltTensionerSeatOfPantsAmplitudeString;

		set
		{
			if ( value != _seatBeltTensionerSeatOfPantsAmplitudeString )
			{
				_seatBeltTensionerSeatOfPantsAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerSeatOfPantsAmplitudeString()
	{
		SeatBeltTensionerSeatOfPantsAmplitudeString = $"{_seatBeltTensionerSeatOfPantsAmplitude:F1}{DataContext.Instance.Localization[ "Degrees" ]}";
	}

	private float _seatBeltTensionerSeatOfPantsCurve = 0.25f;

	public float SeatBeltTensionerSeatOfPantsCurve
	{
		get => _seatBeltTensionerSeatOfPantsCurve;

		set
		{
			value = Math.Clamp( value, -1f, 1f );

			if ( value != _seatBeltTensionerSeatOfPantsCurve )
			{
				_seatBeltTensionerSeatOfPantsCurve = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerSeatOfPantsCurveString();
		}
	}

	private string _seatBeltTensionerSeatOfPantsCurveString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerSeatOfPantsCurveString
	{
		get => _seatBeltTensionerSeatOfPantsCurveString;

		set
		{
			if ( value != _seatBeltTensionerSeatOfPantsCurveString )
			{
				_seatBeltTensionerSeatOfPantsCurveString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerSeatOfPantsCurveString()
	{
		if ( _seatBeltTensionerSeatOfPantsCurve == 0f )
		{
			SeatBeltTensionerSeatOfPantsCurveString = DataContext.Instance.Localization[ "OFF" ];
		}
		else
		{
			SeatBeltTensionerSeatOfPantsCurveString = $"{_seatBeltTensionerSeatOfPantsCurve * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
		}
	}

	public ContextSwitches SeatBeltTensionerSeatOfPantsCurveContextSwitches { get; set; } = new( false, true, false, false, false );

	#endregion

	#region Seat Belt Tensioner - ABS / Wheel Lock Effect

	private bool _seatBeltTensionerABSEnabled = true;

	public bool SeatBeltTensionerABSEnabled
	{
		get => _seatBeltTensionerABSEnabled;

		set
		{
			if ( value != _seatBeltTensionerABSEnabled )
			{
				_seatBeltTensionerABSEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	private float _seatBeltTensionerABSFrequency = 10f;

	public float SeatBeltTensionerABSFrequency
	{
		get => _seatBeltTensionerABSFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 15f );

			if ( value != _seatBeltTensionerABSFrequency )
			{
				_seatBeltTensionerABSFrequency = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerABSFrequencyString();
		}
	}

	private string _seatBeltTensionerABSFrequencyString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerABSFrequencyString
	{
		get => _seatBeltTensionerABSFrequencyString;

		set
		{
			if ( value != _seatBeltTensionerABSFrequencyString )
			{
				_seatBeltTensionerABSFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerABSFrequencyString()
	{
		SeatBeltTensionerABSFrequencyString = $"{(int) MathF.Round( _seatBeltTensionerABSFrequency )} Hz";
	}

	private float _seatBeltTensionerABSAmplitude = 30f;

	public float SeatBeltTensionerABSAmplitude
	{
		get => _seatBeltTensionerABSAmplitude;

		set
		{
			value = Math.Clamp( value, 0f, 60f );

			if ( value != _seatBeltTensionerABSAmplitude )
			{
				_seatBeltTensionerABSAmplitude = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerABSAmplitudeString();
		}
	}

	private string _seatBeltTensionerABSAmplitudeString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerABSAmplitudeString
	{
		get => _seatBeltTensionerABSAmplitudeString;

		set
		{
			if ( value != _seatBeltTensionerABSAmplitudeString )
			{
				_seatBeltTensionerABSAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerABSAmplitudeString()
	{
		SeatBeltTensionerABSAmplitudeString = $"{_seatBeltTensionerABSAmplitude:F1}{DataContext.Instance.Localization[ "Degrees" ]}";
	}

	#endregion

	#region Seat Belt Tensioner - Wheel Slip Effect

	private bool _seatBeltTensionerWheelSlipEnabled = true;

	public bool SeatBeltTensionerWheelSlipEnabled
	{
		get => _seatBeltTensionerWheelSlipEnabled;

		set
		{
			if ( value != _seatBeltTensionerWheelSlipEnabled )
			{
				_seatBeltTensionerWheelSlipEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	private float _seatBeltTensionerWheelSlipFrequency = 11f;

	public float SeatBeltTensionerWheelSlipFrequency
	{
		get => _seatBeltTensionerWheelSlipFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 15f );

			if ( value != _seatBeltTensionerWheelSlipFrequency )
			{
				_seatBeltTensionerWheelSlipFrequency = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerWheelSlipFrequencyString();
		}
	}

	private string _seatBeltTensionerWheelSlipFrequencyString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerWheelSlipFrequencyString
	{
		get => _seatBeltTensionerWheelSlipFrequencyString;

		set
		{
			if ( value != _seatBeltTensionerWheelSlipFrequencyString )
			{
				_seatBeltTensionerWheelSlipFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerWheelSlipFrequencyString()
	{
		SeatBeltTensionerWheelSlipFrequencyString = $"{(int) MathF.Round( _seatBeltTensionerWheelSlipFrequency )} Hz";
	}

	private float _seatBeltTensionerWheelSlipAmplitude = 30f;

	public float SeatBeltTensionerWheelSlipAmplitude
	{
		get => _seatBeltTensionerWheelSlipAmplitude;

		set
		{
			value = Math.Clamp( value, 0f, 60f );

			if ( value != _seatBeltTensionerWheelSlipAmplitude )
			{
				_seatBeltTensionerWheelSlipAmplitude = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerWheelSlipAmplitudeString();
		}
	}

	private string _seatBeltTensionerWheelSlipAmplitudeString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerWheelSlipAmplitudeString
	{
		get => _seatBeltTensionerWheelSlipAmplitudeString;

		set
		{
			if ( value != _seatBeltTensionerWheelSlipAmplitudeString )
			{
				_seatBeltTensionerWheelSlipAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerWheelSlipAmplitudeString()
	{
		SeatBeltTensionerWheelSlipAmplitudeString = $"{_seatBeltTensionerWheelSlipAmplitude:F1}{DataContext.Instance.Localization[ "Degrees" ]}";
	}

	#endregion

	#region Seat Belt Tensioner - Rumble Strip Effect

	private bool _seatBeltTensionerRumbleEnabled = true;

	public bool SeatBeltTensionerRumbleEnabled
	{
		get => _seatBeltTensionerRumbleEnabled;

		set
		{
			if ( value != _seatBeltTensionerRumbleEnabled )
			{
				_seatBeltTensionerRumbleEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	private float _seatBeltTensionerRumbleFrequency = 12f;

	public float SeatBeltTensionerRumbleFrequency
	{
		get => _seatBeltTensionerRumbleFrequency;

		set
		{
			value = Math.Clamp( value, 0f, 15f );

			if ( value != _seatBeltTensionerRumbleFrequency )
			{
				_seatBeltTensionerRumbleFrequency = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerRumbleFrequencyString();
		}
	}

	private string _seatBeltTensionerRumbleFrequencyString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerRumbleFrequencyString
	{
		get => _seatBeltTensionerRumbleFrequencyString;

		set
		{
			if ( value != _seatBeltTensionerRumbleFrequencyString )
			{
				_seatBeltTensionerRumbleFrequencyString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerRumbleFrequencyString()
	{
		SeatBeltTensionerRumbleFrequencyString = $"{(int) MathF.Round( _seatBeltTensionerRumbleFrequency )} Hz";
	}

	private float _seatBeltTensionerRumbleAmplitude = 30f;

	public float SeatBeltTensionerRumbleAmplitude
	{
		get => _seatBeltTensionerRumbleAmplitude;

		set
		{
			value = Math.Clamp( value, 0f, 60f );

			if ( value != _seatBeltTensionerRumbleAmplitude )
			{
				_seatBeltTensionerRumbleAmplitude = value;

				OnPropertyChanged();
			}

			UpdateSeatBeltTensionerRumbleAmplitudeString();
		}
	}

	private string _seatBeltTensionerRumbleAmplitudeString = string.Empty;

	[XmlIgnore]
	public string SeatBeltTensionerRumbleAmplitudeString
	{
		get => _seatBeltTensionerRumbleAmplitudeString;

		set
		{
			if ( value != _seatBeltTensionerRumbleAmplitudeString )
			{
				_seatBeltTensionerRumbleAmplitudeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSeatBeltTensionerRumbleAmplitudeString()
	{
		SeatBeltTensionerRumbleAmplitudeString = $"{_seatBeltTensionerRumbleAmplitude:F1}{DataContext.Instance.Localization[ "Degrees" ]}";
	}

	#endregion

	#region AdminBoxx - Connect on startup

#if !ADMINBOXX
	private bool _adminBoxxConnectOnStartup = false;
#endif

	public bool AdminBoxxConnectOnStartup
	{
#if ADMINBOXX
		get => false;
		set { }
#else
		get => _adminBoxxConnectOnStartup;

		set
		{
			if ( value != _adminBoxxConnectOnStartup )
			{
				_adminBoxxConnectOnStartup = value;

				OnPropertyChanged();
			}
		}
#endif
	}

	#endregion

	#region AdminBoxx - Enable XSRC integration

	private bool _adminBoxxEnableXsrcIntegration = false;

	public bool AdminBoxxEnableXsrcIntegration
	{
		get => _adminBoxxEnableXsrcIntegration;

		set
		{
			if ( value != _adminBoxxEnableXsrcIntegration )
			{
				_adminBoxxEnableXsrcIntegration = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region AdminBoxx - Brightness

	private float _adminBoxxBrightness = 0.15f;

	public float AdminBoxxBrightness
	{
		get => _adminBoxxBrightness;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _adminBoxxBrightness )
			{
				_adminBoxxBrightness = value;

				OnPropertyChanged();
			}

			UpdateAdminBoxxBrightnessString();
		}
	}

	private string _adminBoxxBrightnessString = string.Empty;

	[XmlIgnore]
	public string AdminBoxxBrightnessString
	{
		get => _adminBoxxBrightnessString;

		set
		{
			if ( value != _adminBoxxBrightnessString )
			{
				_adminBoxxBrightnessString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateAdminBoxxBrightnessString()
	{
		AdminBoxxBrightnessString = $"{_adminBoxxBrightness * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings AdminBoxxBrightnessPlusButtonMappings { get; set; } = new();
	public ButtonMappings AdminBoxxBrightnessMinusButtonMappings { get; set; } = new();

	#endregion

	#region AdminBoxx - Volume

	private float _adminBoxxVolume = 0.75f;

	public float AdminBoxxVolume
	{
		get => _adminBoxxVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _adminBoxxVolume )
			{
				_adminBoxxVolume = value;

				OnPropertyChanged();
			}

			UpdateAdminBoxxVolumeString();
		}
	}

	private string _adminBoxxVolumeString = string.Empty;

	[XmlIgnore]
	public string AdminBoxxVolumeString
	{
		get => _adminBoxxVolumeString;

		set
		{
			if ( value != _adminBoxxBrightnessString )
			{
				_adminBoxxVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateAdminBoxxVolumeString()
	{
		AdminBoxxVolumeString = $"{_adminBoxxVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings AdminBoxxVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings AdminBoxxVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Overlays - Show when off track

	private bool _overlaysShowWhenOffTrack = false;

	public bool OverlaysShowWhenOffTrack
	{
		get => _overlaysShowWhenOffTrack;

		set
		{
			if ( value != _overlaysShowWhenOffTrack )
			{
				_overlaysShowWhenOffTrack = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGapMonitorWindowVisibility();
			app.UpdateDeltaMonitorWindowVisibility();
			app.UpdateGripOMeterWindowVisibility();
		}
	}

	#endregion

	#region Overlays - Show in replay mode

	private bool _overlaysShowInReplayMode = false;

	public bool OverlaysShowInReplayMode
	{
		get => _overlaysShowInReplayMode;

		set
		{
			if ( value != _overlaysShowInReplayMode )
			{
				_overlaysShowInReplayMode = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGapMonitorWindowVisibility();
			app.UpdateDeltaMonitorWindowVisibility();
			app.UpdateGripOMeterWindowVisibility();
		}
	}

	#endregion

	#region Overlays - Show Gap monitor window

	private bool _overlaysShowGapMonitorWindow = false;

	public bool OverlaysShowGapMonitorWindow
	{
		get => _overlaysShowGapMonitorWindow;

		set
		{
			if ( value != _overlaysShowGapMonitorWindow )
			{
				_overlaysShowGapMonitorWindow = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGapMonitorWindowVisibility();
		}
	}

	#endregion

	#region Overlays - Show Gap monitor title

	private bool _overlaysShowGapMonitorTitle = true;

	public bool OverlaysShowGapMonitorTitle
	{
		get => _overlaysShowGapMonitorTitle;

		set
		{
			if ( value != _overlaysShowGapMonitorTitle )
			{
				_overlaysShowGapMonitorTitle = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGapMonitorWindowVisibility();
		}
	}

	#endregion

	#region Overlays - Gap monitor window scale

	private float _overlaysGapMonitorWindowScale = 1f;

	public float OverlaysGapMonitorWindowScale
	{
		get => _overlaysGapMonitorWindowScale;

		set
		{
			value = Math.Clamp( value, 0.5f, 2f );

			if ( value != _overlaysGapMonitorWindowScale )
			{
				_overlaysGapMonitorWindowScale = value;

				OnPropertyChanged();
			}

			UpdateOverlaysGapMonitorWindowScaleString();
		}
	}

	private string _overlaysGapMonitorWindowScaleString = string.Empty;

	[XmlIgnore]
	public string OverlaysGapMonitorWindowScaleString
	{
		get => _overlaysGapMonitorWindowScaleString;

		set
		{
			if ( value != _overlaysGapMonitorWindowScaleString )
			{
				_overlaysGapMonitorWindowScaleString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateOverlaysGapMonitorWindowScaleString()
	{
		OverlaysGapMonitorWindowScaleString = $"{_overlaysGapMonitorWindowScale * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Overlays - Gap monitor window position

	private Rectangle _overlaysGapMonitorWindowPosition = Rectangle.Empty;

	public Rectangle OverlaysGapMonitorWindowPosition
	{
		get => _overlaysGapMonitorWindowPosition;

		set
		{
			if ( value != _overlaysGapMonitorWindowPosition )
			{
				_overlaysGapMonitorWindowPosition = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Gap monitor window background color

	private string _overlaysGapMonitorWindowBackgroundColor = "#000000";

	public string OverlaysGapMonitorWindowBackgroundColor
	{
		get => _overlaysGapMonitorWindowBackgroundColor;

		set
		{
			if ( value != _overlaysGapMonitorWindowBackgroundColor )
			{
				_overlaysGapMonitorWindowBackgroundColor = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Gap monitor window background opacity

	private float _overlaysGapMonitorWindowBackgroundOpacity = 1f;

	public float OverlaysGapMonitorWindowBackgroundOpacity
	{
		get => _overlaysGapMonitorWindowBackgroundOpacity;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _overlaysGapMonitorWindowBackgroundOpacity )
			{
				_overlaysGapMonitorWindowBackgroundOpacity = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Show Delta monitor window

	private bool _overlaysShowDeltaMonitorWindow = false;

	public bool OverlaysShowDeltaMonitorWindow
	{
		get => _overlaysShowDeltaMonitorWindow;

		set
		{
			if ( value != _overlaysShowDeltaMonitorWindow )
			{
				_overlaysShowDeltaMonitorWindow = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateDeltaMonitorWindowVisibility();
		}
	}

	#endregion

	#region Overlays - Show Delta monitor title

	private bool _overlaysShowDeltaMonitorTitle = true;

	public bool OverlaysShowDeltaMonitorTitle
	{
		get => _overlaysShowDeltaMonitorTitle;

		set
		{
			if ( value != _overlaysShowDeltaMonitorTitle )
			{
				_overlaysShowDeltaMonitorTitle = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateDeltaMonitorWindowVisibility();
		}
	}

	#endregion

	#region Overlays - Delta monitor window scale

	private float _overlaysDeltaMonitorWindowScale = 1f;

	public float OverlaysDeltaMonitorWindowScale
	{
		get => _overlaysDeltaMonitorWindowScale;

		set
		{
			value = Math.Clamp( value, 0.5f, 2f );

			if ( value != _overlaysDeltaMonitorWindowScale )
			{
				_overlaysDeltaMonitorWindowScale = value;

				OnPropertyChanged();
			}

			UpdateOverlaysDeltaMonitorWindowScaleString();
		}
	}

	private string _overlaysDeltaMonitorWindowScaleString = string.Empty;

	[XmlIgnore]
	public string OverlaysDeltaMonitorWindowScaleString
	{
		get => _overlaysDeltaMonitorWindowScaleString;

		set
		{
			if ( value != _overlaysDeltaMonitorWindowScaleString )
			{
				_overlaysDeltaMonitorWindowScaleString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateOverlaysDeltaMonitorWindowScaleString()
	{
		OverlaysDeltaMonitorWindowScaleString = $"{_overlaysDeltaMonitorWindowScale * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Overlays - Delta monitor window position

	private Rectangle _overlaysDeltaMonitorWindowPosition = Rectangle.Empty;

	public Rectangle OverlaysDeltaMonitorWindowPosition
	{
		get => _overlaysDeltaMonitorWindowPosition;

		set
		{
			if ( value != _overlaysDeltaMonitorWindowPosition )
			{
				_overlaysDeltaMonitorWindowPosition = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Delta monitor window background color

	private string _overlaysDeltaMonitorWindowBackgroundColor = "#000000";

	public string OverlaysDeltaMonitorWindowBackgroundColor
	{
		get => _overlaysDeltaMonitorWindowBackgroundColor;

		set
		{
			if ( value != _overlaysDeltaMonitorWindowBackgroundColor )
			{
				_overlaysDeltaMonitorWindowBackgroundColor = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Delta monitor window background opacity

	private float _overlaysDeltaMonitorWindowBackgroundOpacity = 1f;

	public float OverlaysDeltaMonitorWindowBackgroundOpacity
	{
		get => _overlaysDeltaMonitorWindowBackgroundOpacity;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _overlaysDeltaMonitorWindowBackgroundOpacity )
			{
				_overlaysDeltaMonitorWindowBackgroundOpacity = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Show Grip-O-Meter window

	private bool _overlaysShowGripOMeterWindow = false;

	public bool OverlaysShowGripOMeterWindow
	{
		get => _overlaysShowGripOMeterWindow;

		set
		{
			if ( value != _overlaysShowGripOMeterWindow )
			{
				_overlaysShowGripOMeterWindow = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGripOMeterWindowVisibility();
		}
	}

	#endregion

	#region Overlays - Show Grip-O-Meter title

	private bool _overlaysShowGripOMeterTitle = true;

	public bool OverlaysShowGripOMeterTitle
	{
		get => _overlaysShowGripOMeterTitle;

		set
		{
			if ( value != _overlaysShowGripOMeterTitle )
			{
				_overlaysShowGripOMeterTitle = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.UpdateGripOMeterWindowVisibility();
		}
	}

	#endregion

	#region Overlays - Grip-O-Meter window scale

	private float _overlaysGripOMeterWindowScale = 1f;

	public float OverlaysGripOMeterWindowScale
	{
		get => _overlaysGripOMeterWindowScale;

		set
		{
			value = Math.Clamp( value, 0.5f, 2f );

			if ( value != _overlaysGripOMeterWindowScale )
			{
				_overlaysGripOMeterWindowScale = value;

				OnPropertyChanged();
			}

			UpdateOverlaysGripOMeterWindowScaleString();
		}
	}

	private string _overlaysGripOMeterWindowScaleString = string.Empty;

	[XmlIgnore]
	public string OverlaysGripOMeterWindowScaleString
	{
		get => _overlaysGripOMeterWindowScaleString;

		set
		{
			if ( value != _overlaysGripOMeterWindowScaleString )
			{
				_overlaysGripOMeterWindowScaleString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateOverlaysGripOMeterWindowScaleString()
	{
		OverlaysGripOMeterWindowScaleString = $"{_overlaysGripOMeterWindowScale * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Overlays - Grip-O-Meter window position

	private Rectangle _overlaysGripOMeterWindowPosition = Rectangle.Empty;

	public Rectangle OverlaysGripOMeterWindowPosition
	{
		get => _overlaysGripOMeterWindowPosition;

		set
		{
			if ( value != _overlaysGripOMeterWindowPosition )
			{
				_overlaysGripOMeterWindowPosition = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Grip-O-Meter window opacity

	private float _overlaysGripOMeterWindowOpacity = 1f;

	public float OverlaysGripOMeterWindowOpacity
	{
		get => _overlaysGripOMeterWindowOpacity;

		set
		{
			// floor at 10% so the Grip-O-Meter (whose opacity is window-level) can never become fully invisible
			value = Math.Clamp( value, 0.1f, 1f );

			if ( value != _overlaysGripOMeterWindowOpacity )
			{
				_overlaysGripOMeterWindowOpacity = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Show Speech-to-text title

	private bool _overlaysShowSpeechToTextTitle = true;

	public bool OverlaysShowSpeechToTextTitle
	{
		get => _overlaysShowSpeechToTextTitle;

		set
		{
			if ( value != _overlaysShowSpeechToTextTitle )
			{
				_overlaysShowSpeechToTextTitle = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Speech-to-text window scale

	private float _overlaysSpeechToTextWindowScale = 1f;

	public float OverlaysSpeechToTextWindowScale
	{
		get => _overlaysSpeechToTextWindowScale;

		set
		{
			value = Math.Clamp( value, 0.5f, 2f );

			if ( value != _overlaysSpeechToTextWindowScale )
			{
				_overlaysSpeechToTextWindowScale = value;

				OnPropertyChanged();
			}

			UpdateOverlaysSpeechToTextWindowScaleString();
		}
	}

	private string _overlaysSpeechToTextWindowScaleString = string.Empty;

	[XmlIgnore]
	public string OverlaysSpeechToTextWindowScaleString
	{
		get => _overlaysSpeechToTextWindowScaleString;

		set
		{
			if ( value != _overlaysSpeechToTextWindowScaleString )
			{
				_overlaysSpeechToTextWindowScaleString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateOverlaysSpeechToTextWindowScaleString()
	{
		OverlaysSpeechToTextWindowScaleString = $"{_overlaysSpeechToTextWindowScale * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Overlays - Speech-to-text window position

	private Rectangle _overlaysSpeechToTextWindowPosition = Rectangle.Empty;

	public Rectangle OverlaysSpeechToTextWindowPosition
	{
		get => _overlaysSpeechToTextWindowPosition;

		set
		{
			if ( value != _overlaysSpeechToTextWindowPosition )
			{
				_overlaysSpeechToTextWindowPosition = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Speech-to-text window background color

	private string _overlaysSpeechToTextWindowBackgroundColor = "#000000";

	public string OverlaysSpeechToTextWindowBackgroundColor
	{
		get => _overlaysSpeechToTextWindowBackgroundColor;

		set
		{
			if ( value != _overlaysSpeechToTextWindowBackgroundColor )
			{
				_overlaysSpeechToTextWindowBackgroundColor = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Overlays - Speech-to-text window background opacity

	private float _overlaysSpeechToTextWindowBackgroundOpacity = 0.9f;

	public float OverlaysSpeechToTextWindowBackgroundOpacity
	{
		get => _overlaysSpeechToTextWindowBackgroundOpacity;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _overlaysSpeechToTextWindowBackgroundOpacity )
			{
				_overlaysSpeechToTextWindowBackgroundOpacity = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Output device

	private string _soundsOutputDevice = Components.AudioManager.DefaultDeviceName;

	public string SoundsOutputDevice
	{
		get => _soundsOutputDevice;

		set
		{
			if ( value == null )
			{
				// Notify WPF binding to read back the current value so the UI restores correctly.
				// Without this, string null guards silently swallow the null without firing
				// PropertyChanged, leaving the bound ComboBox with a blank selection.
				OnPropertyChanged();
				return;
			}

			if ( value != _soundsOutputDevice )
			{
				_soundsOutputDevice = value;

				OnPropertyChanged();

				App.Instance!.AudioManager.SetOutputDevice( value );
			}
		}
	}

	#endregion

	#region Sounds - Master enabled

	private bool _soundsMasterEnabled = true;

	public bool SoundsMasterEnabled
	{
		get => _soundsMasterEnabled;

		set
		{
			if ( value != _soundsMasterEnabled )
			{
				_soundsMasterEnabled = value;

				OnPropertyChanged();

				var app = App.Instance!;

				if ( _soundsMasterEnabled )
				{
					app.AudioManager.OpenDevice();
				}
				else
				{
					app.AudioManager.CloseDevice();
				}
			}
		}
	}

	#endregion

	#region Sounds - Master volume

	private float _soundsMasterVolume = 0.75f;

	public float SoundsMasterVolume
	{
		get => _soundsMasterVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsMasterVolume )
			{
				_soundsMasterVolume = value;

				OnPropertyChanged();
			}

			UpdateSoundsMasterVolumeString();
		}
	}

	private string _soundsMasterVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsMasterVolumeString
	{
		get => _soundsMasterVolumeString;

		set
		{
			if ( value != _soundsMasterVolumeString )
			{
				_soundsMasterVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsMasterVolumeString()
	{
		SoundsMasterVolumeString = $"{_soundsMasterVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsMasterVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsMasterVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Allow during replays

	private bool _soundsAllowDuringReplays = false;

	public bool SoundsAllowDuringReplays
	{
		get => _soundsAllowDuringReplays;

		set
		{
			if ( value != _soundsAllowDuringReplays )
			{
				_soundsAllowDuringReplays = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Click enabled

	private bool _soundsClickEnabled = true;

	public bool SoundsClickEnabled
	{
		get => _soundsClickEnabled;

		set
		{
			if ( value != _soundsClickEnabled )
			{
				_soundsClickEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Click volume

	private float _soundsClickVolume = 0.75f;

	public float SoundsClickVolume
	{
		get => _soundsClickVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsClickVolume )
			{
				_soundsClickVolume = value;

				OnPropertyChanged();
			}

			UpdateSoundsClickVolumeString();
		}
	}

	private string _soundsClickVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsClickVolumeString
	{
		get => _soundsClickVolumeString;

		set
		{
			if ( value != _soundsClickVolumeString )
			{
				_soundsClickVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsClickVolumeString()
	{
		SoundsClickVolumeString = $"{_soundsClickVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsClickVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsClickVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Click frequency ratio

	private float _soundsClickFrequencyRatio = 1f;

	public float SoundsClickFrequencyRatio
	{
		get => _soundsClickFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 2f );

			if ( value != _soundsClickFrequencyRatio )
			{
				_soundsClickFrequencyRatio = value;

				OnPropertyChanged();
			}

			UpdateSoundsClickFrequencyRatioString();
		}
	}

	private string _soundsClickFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsClickFrequencyRatioString
	{
		get => _soundsClickFrequencyRatioString;

		set
		{
			if ( value != _soundsClickFrequencyRatioString )
			{
				_soundsClickFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsClickFrequencyRatioString()
	{
		var clickPitchShift = _soundsClickFrequencyRatio * 100f - 100f;
		SoundsClickFrequencyRatioString = clickPitchShift == 0f ? DataContext.Instance.Localization[ "OFF" ] : $"{( clickPitchShift >= 0f ? "+" : "" )}{clickPitchShift:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsClickFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsClickFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - ABS engaged enabled

	private bool _soundsABSEngagedEnabled = false;

	public bool SoundsABSEngagedEnabled
	{
		get => _soundsABSEngagedEnabled;

		set
		{
			if ( value != _soundsABSEngagedEnabled )
			{
				_soundsABSEngagedEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - ABS engaged fade with brake

	private bool _soundsABSEngagedFadeWithBrake = false;

	public bool SoundsABSEngagedFadeWithBrake
	{
		get => _soundsABSEngagedFadeWithBrake;

		set
		{
			if ( value != _soundsABSEngagedFadeWithBrake )
			{
				_soundsABSEngagedFadeWithBrake = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - ABS engaged volume

	private float _soundsABSEngagedVolume = 0.75f;

	public float SoundsABSEngagedVolume
	{
		get => _soundsABSEngagedVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsABSEngagedVolume )
			{
				_soundsABSEngagedVolume = value;

				OnPropertyChanged();
			}

			UpdateSoundsABSEngagedVolumeString();
		}
	}

	private string _soundsABSEngagedVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsABSEngagedVolumeString
	{
		get => _soundsABSEngagedVolumeString;

		set
		{
			if ( value != _soundsABSEngagedVolumeString )
			{
				_soundsABSEngagedVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsABSEngagedVolumeString()
	{
		SoundsABSEngagedVolumeString = $"{_soundsABSEngagedVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsABSEngagedVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsABSEngagedVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - ABS engaged frequency ratio

	private float _soundsABSEngagedFrequencyRatio = 1f;

	public float SoundsABSEngagedFrequencyRatio
	{
		get => _soundsABSEngagedFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 2f );

			if ( value != _soundsABSEngagedFrequencyRatio )
			{
				_soundsABSEngagedFrequencyRatio = value;

				OnPropertyChanged();
			}

			UpdateSoundsABSEngagedFrequencyRatioString();
		}
	}

	private string _soundsABSEngagedFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsABSEngagedFrequencyRatioString
	{
		get => _soundsABSEngagedFrequencyRatioString;

		set
		{
			if ( value != _soundsABSEngagedFrequencyRatioString )
			{
				_soundsABSEngagedFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsABSEngagedFrequencyRatioString()
	{
		var absEngagedPitchShift = _soundsABSEngagedFrequencyRatio * 100f - 100f;
		SoundsABSEngagedFrequencyRatioString = absEngagedPitchShift == 0f ? DataContext.Instance.Localization[ "OFF" ] : $"{( absEngagedPitchShift >= 0f ? "+" : "" )}{absEngagedPitchShift:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsABSEngagedFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsABSEngagedFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - ABS engaged loop start ms

	private float _soundsABSEngagedLoopStartMs = 0f;

	public float SoundsABSEngagedLoopStartMs
	{
		get => _soundsABSEngagedLoopStartMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsABSEngagedLoopStartMs )
			{
				_soundsABSEngagedLoopStartMs = value;

				OnPropertyChanged();

				if ( _soundsABSEngagedLoopEndMs < _soundsABSEngagedLoopStartMs )
				{
					SoundsABSEngagedLoopEndMs = _soundsABSEngagedLoopStartMs;
				}
			}

			UpdateSoundsABSEngagedLoopStartMsString();
		}
	}

	private string _soundsABSEngagedLoopStartMsString = string.Empty;

	[XmlIgnore]
	public string SoundsABSEngagedLoopStartMsString
	{
		get => _soundsABSEngagedLoopStartMsString;

		set
		{
			if ( value != _soundsABSEngagedLoopStartMsString )
			{
				_soundsABSEngagedLoopStartMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsABSEngagedLoopStartMsString()
	{
		SoundsABSEngagedLoopStartMsString = $"{_soundsABSEngagedLoopStartMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - ABS engaged loop end ms

	private float _soundsABSEngagedLoopEndMs = 0f;

	public float SoundsABSEngagedLoopEndMs
	{
		get => _soundsABSEngagedLoopEndMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsABSEngagedLoopEndMs )
			{
				_soundsABSEngagedLoopEndMs = value;

				OnPropertyChanged();

				if ( _soundsABSEngagedLoopStartMs > _soundsABSEngagedLoopEndMs )
				{
					SoundsABSEngagedLoopStartMs = _soundsABSEngagedLoopEndMs;
				}
			}

			UpdateSoundsABSEngagedLoopEndMsString();
		}
	}

	private string _soundsABSEngagedLoopEndMsString = string.Empty;

	[XmlIgnore]
	public string SoundsABSEngagedLoopEndMsString
	{
		get => _soundsABSEngagedLoopEndMsString;

		set
		{
			if ( value != _soundsABSEngagedLoopEndMsString )
			{
				_soundsABSEngagedLoopEndMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsABSEngagedLoopEndMsString()
	{
		SoundsABSEngagedLoopEndMsString = $"{_soundsABSEngagedLoopEndMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Wheel lock enabled

	private bool _soundsWheelLockEnabled = false;

	public bool SoundsWheelLockEnabled
	{
		get => _soundsWheelLockEnabled;

		set
		{
			if ( value != _soundsWheelLockEnabled )
			{
				_soundsWheelLockEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Wheel lock fade with brake

	private bool _soundsWheelLockFadeWithBrake = true;

	public bool SoundsWheelLockFadeWithBrake
	{
		get => _soundsWheelLockFadeWithBrake;

		set
		{
			if ( value != _soundsWheelLockFadeWithBrake )
			{
				_soundsWheelLockFadeWithBrake = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Wheel lock volume

	private float _soundsWheelLockVolume = 0.75f;

	public float SoundsWheelLockVolume
	{
		get => _soundsWheelLockVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsWheelLockVolume )
			{
				_soundsWheelLockVolume = value;

				OnPropertyChanged();
			}

			UpdateSoundsWheelLockVolumeString();
		}
	}

	private string _soundsWheelLockVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelLockVolumeString
	{
		get => _soundsWheelLockVolumeString;

		set
		{
			if ( value != _soundsWheelLockVolumeString )
			{
				_soundsWheelLockVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsWheelLockVolumeString()
	{
		SoundsWheelLockVolumeString = $"{_soundsWheelLockVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsWheelLockVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelLockVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel lock frequency ratio

	private float _soundsWheelLockFrequencyRatio = 1f;

	public float SoundsWheelLockFrequencyRatio
	{
		get => _soundsWheelLockFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 2f );

			if ( value != _soundsWheelLockFrequencyRatio )
			{
				_soundsWheelLockFrequencyRatio = value;

				OnPropertyChanged();
			}

			UpdateSoundsWheelLockFrequencyRatioString();
		}
	}

	private string _soundsWheelLockFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelLockFrequencyRatioString
	{
		get => _soundsWheelLockFrequencyRatioString;

		set
		{
			if ( value != _soundsWheelLockFrequencyRatioString )
			{
				_soundsWheelLockFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsWheelLockFrequencyRatioString()
	{
		var wheelLockPitchShift = _soundsWheelLockFrequencyRatio * 100f - 100f;
		SoundsWheelLockFrequencyRatioString = wheelLockPitchShift == 0f ? DataContext.Instance.Localization[ "OFF" ] : $"{( wheelLockPitchShift >= 0f ? "+" : "" )}{wheelLockPitchShift:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsWheelLockFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelLockFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel lock loop start ms

	private float _soundsWheelLockLoopStartMs = 0f;

	public float SoundsWheelLockLoopStartMs
	{
		get => _soundsWheelLockLoopStartMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsWheelLockLoopStartMs )
			{
				_soundsWheelLockLoopStartMs = value;

				OnPropertyChanged();

				if ( _soundsWheelLockLoopEndMs < _soundsWheelLockLoopStartMs )
				{
					SoundsWheelLockLoopEndMs = _soundsWheelLockLoopStartMs;
				}
			}

			UpdateSoundsWheelLockLoopStartMsString();
		}
	}

	private string _soundsWheelLockLoopStartMsString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelLockLoopStartMsString
	{
		get => _soundsWheelLockLoopStartMsString;

		set
		{
			if ( value != _soundsWheelLockLoopStartMsString )
			{
				_soundsWheelLockLoopStartMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsWheelLockLoopStartMsString()
	{
		SoundsWheelLockLoopStartMsString = $"{_soundsWheelLockLoopStartMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Wheel lock loop end ms

	private float _soundsWheelLockLoopEndMs = 0f;

	public float SoundsWheelLockLoopEndMs
	{
		get => _soundsWheelLockLoopEndMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsWheelLockLoopEndMs )
			{
				_soundsWheelLockLoopEndMs = value;

				OnPropertyChanged();

				if ( _soundsWheelLockLoopStartMs > _soundsWheelLockLoopEndMs )
				{
					SoundsWheelLockLoopStartMs = _soundsWheelLockLoopEndMs;
				}
			}

			UpdateSoundsWheelLockLoopEndMsString();
		}
	}

	private string _soundsWheelLockLoopEndMsString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelLockLoopEndMsString
	{
		get => _soundsWheelLockLoopEndMsString;

		set
		{
			if ( value != _soundsWheelLockLoopEndMsString )
			{
				_soundsWheelLockLoopEndMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsWheelLockLoopEndMsString()
	{
		SoundsWheelLockLoopEndMsString = $"{_soundsWheelLockLoopEndMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Wheel lock sensitivity

	private float _soundsWheelLockSensitivity = 0.85f;

	public float SoundsWheelLockSensitivity
	{
		get => _soundsWheelLockSensitivity;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsWheelLockSensitivity )
			{
				_soundsWheelLockSensitivity = value;

				OnPropertyChanged();
			}

			UpdateSoundsWheelLockSensitivityString();
		}
	}

	private string _soundsWheelLockSensitivityString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelLockSensitivityString
	{
		get => _soundsWheelLockSensitivityString;

		set
		{
			if ( value != _soundsWheelLockSensitivityString )
			{
				_soundsWheelLockSensitivityString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsWheelLockSensitivityString()
	{
		SoundsWheelLockSensitivityString = $"{_soundsWheelLockSensitivity * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsWheelLockSensitivityPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelLockSensitivityMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel spin enabled

	private bool _soundsWheelSpinEnabled = false;

	public bool SoundsWheelSpinEnabled
	{
		get => _soundsWheelSpinEnabled;

		set
		{
			if ( value != _soundsWheelSpinEnabled )
			{
				_soundsWheelSpinEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Wheel spin fade with throttle

	private bool _soundsWheelSpinFadeWithThrottle = true;

	public bool SoundsWheelSpinFadeWithThrottle
	{
		get => _soundsWheelSpinFadeWithThrottle;

		set
		{
			if ( value != _soundsWheelSpinFadeWithThrottle )
			{
				_soundsWheelSpinFadeWithThrottle = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Wheel spin volume

	private float _soundsWheelSpinVolume = 0.75f;

	public float SoundsWheelSpinVolume
	{
		get => _soundsWheelSpinVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsWheelSpinVolume )
			{
				_soundsWheelSpinVolume = value;

				OnPropertyChanged();
			}

			UpdateSoundsWheelSpinVolumeString();
		}
	}

	private string _soundsWheelSpinVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelSpinVolumeString
	{
		get => _soundsWheelSpinVolumeString;

		set
		{
			if ( value != _soundsWheelSpinVolumeString )
			{
				_soundsWheelSpinVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsWheelSpinVolumeString()
	{
		SoundsWheelSpinVolumeString = $"{_soundsWheelSpinVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsWheelSpinVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelSpinVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel spin frequency ratio

	private float _soundsWheelSpinFrequencyRatio = 1f;

	public float SoundsWheelSpinFrequencyRatio
	{
		get => _soundsWheelSpinFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 2f );

			if ( value != _soundsWheelSpinFrequencyRatio )
			{
				_soundsWheelSpinFrequencyRatio = value;

				OnPropertyChanged();
			}

			UpdateSoundsWheelSpinFrequencyRatioString();
		}
	}

	private string _soundsWheelSpinFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelSpinFrequencyRatioString
	{
		get => _soundsWheelSpinFrequencyRatioString;

		set
		{
			if ( value != _soundsWheelSpinFrequencyRatioString )
			{
				_soundsWheelSpinFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsWheelSpinFrequencyRatioString()
	{
		var wheelSpinPitchShift = _soundsWheelSpinFrequencyRatio * 100f - 100f;
		SoundsWheelSpinFrequencyRatioString = wheelSpinPitchShift == 0f ? DataContext.Instance.Localization[ "OFF" ] : $"{( wheelSpinPitchShift >= 0f ? "+" : "" )}{wheelSpinPitchShift:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsWheelSpinFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelSpinFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Wheel spin loop start ms

	private float _soundsWheelSpinLoopStartMs = 0f;

	public float SoundsWheelSpinLoopStartMs
	{
		get => _soundsWheelSpinLoopStartMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsWheelSpinLoopStartMs )
			{
				_soundsWheelSpinLoopStartMs = value;

				OnPropertyChanged();

				if ( _soundsWheelSpinLoopEndMs < _soundsWheelSpinLoopStartMs )
				{
					SoundsWheelSpinLoopEndMs = _soundsWheelSpinLoopStartMs;
				}
			}

			UpdateSoundsWheelSpinLoopStartMsString();
		}
	}

	private string _soundsWheelSpinLoopStartMsString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelSpinLoopStartMsString
	{
		get => _soundsWheelSpinLoopStartMsString;

		set
		{
			if ( value != _soundsWheelSpinLoopStartMsString )
			{
				_soundsWheelSpinLoopStartMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsWheelSpinLoopStartMsString()
	{
		SoundsWheelSpinLoopStartMsString = $"{_soundsWheelSpinLoopStartMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Wheel spin loop end ms

	private float _soundsWheelSpinLoopEndMs = 0f;

	public float SoundsWheelSpinLoopEndMs
	{
		get => _soundsWheelSpinLoopEndMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsWheelSpinLoopEndMs )
			{
				_soundsWheelSpinLoopEndMs = value;

				OnPropertyChanged();

				if ( _soundsWheelSpinLoopStartMs > _soundsWheelSpinLoopEndMs )
				{
					SoundsWheelSpinLoopStartMs = _soundsWheelSpinLoopEndMs;
				}
			}

			UpdateSoundsWheelSpinLoopEndMsString();
		}
	}

	private string _soundsWheelSpinLoopEndMsString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelSpinLoopEndMsString
	{
		get => _soundsWheelSpinLoopEndMsString;

		set
		{
			if ( value != _soundsWheelSpinLoopEndMsString )
			{
				_soundsWheelSpinLoopEndMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsWheelSpinLoopEndMsString()
	{
		SoundsWheelSpinLoopEndMsString = $"{_soundsWheelSpinLoopEndMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Wheel spin sensitivity

	private float _soundsWheelSpinSensitivity = 0.85f;

	public float SoundsWheelSpinSensitivity
	{
		get => _soundsWheelSpinSensitivity;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsWheelSpinSensitivity )
			{
				_soundsWheelSpinSensitivity = value;

				OnPropertyChanged();
			}

			UpdateSoundsWheelSpinSensitivityString();
		}
	}

	private string _soundsWheelSpinSensitivityString = string.Empty;

	[XmlIgnore]
	public string SoundsWheelSpinSensitivityString
	{
		get => _soundsWheelSpinSensitivityString;

		set
		{
			if ( value != _soundsWheelSpinSensitivityString )
			{
				_soundsWheelSpinSensitivityString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsWheelSpinSensitivityString()
	{
		SoundsWheelSpinSensitivityString = $"{_soundsWheelSpinSensitivity * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsWheelSpinSensitivityPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsWheelSpinSensitivityMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Understeer enabled

	private bool _soundsUndersteerEnabled = false;

	public bool SoundsUndersteerEnabled
	{
		get => _soundsUndersteerEnabled;

		set
		{
			if ( value != _soundsUndersteerEnabled )
			{
				_soundsUndersteerEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Understeer volume

	private float _soundsUndersteerVolume = 0.75f;

	public float SoundsUndersteerVolume
	{
		get => _soundsUndersteerVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsUndersteerVolume )
			{
				_soundsUndersteerVolume = value;

				OnPropertyChanged();
			}

			UpdateSoundsUndersteerVolumeString();
		}
	}

	private string _soundsUndersteerVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsUndersteerVolumeString
	{
		get => _soundsUndersteerVolumeString;

		set
		{
			if ( value != _soundsUndersteerVolumeString )
			{
				_soundsUndersteerVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsUndersteerVolumeString()
	{
		SoundsUndersteerVolumeString = $"{_soundsUndersteerVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsUndersteerVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsUndersteerVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Understeer frequency ratio

	private float _soundsUndersteerFrequencyRatio = 1f;

	public float SoundsUndersteerFrequencyRatio
	{
		get => _soundsUndersteerFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 2f );

			if ( value != _soundsUndersteerFrequencyRatio )
			{
				_soundsUndersteerFrequencyRatio = value;

				OnPropertyChanged();
			}

			UpdateSoundsUndersteerFrequencyRatioString();
		}
	}

	private string _soundsUndersteerFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsUndersteerFrequencyRatioString
	{
		get => _soundsUndersteerFrequencyRatioString;

		set
		{
			if ( value != _soundsUndersteerFrequencyRatioString )
			{
				_soundsUndersteerFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsUndersteerFrequencyRatioString()
	{
		var understeerPitchShift = _soundsUndersteerFrequencyRatio * 100f - 100f;
		SoundsUndersteerFrequencyRatioString = understeerPitchShift == 0f ? DataContext.Instance.Localization[ "OFF" ] : $"{( understeerPitchShift >= 0f ? "+" : "" )}{understeerPitchShift:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsUndersteerFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsUndersteerFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Understeer loop start ms

	private float _soundsUndersteerLoopStartMs = 0f;

	public float SoundsUndersteerLoopStartMs
	{
		get => _soundsUndersteerLoopStartMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsUndersteerLoopStartMs )
			{
				_soundsUndersteerLoopStartMs = value;

				OnPropertyChanged();

				if ( _soundsUndersteerLoopEndMs < _soundsUndersteerLoopStartMs )
				{
					SoundsUndersteerLoopEndMs = _soundsUndersteerLoopStartMs;
				}
			}

			UpdateSoundsUndersteerLoopStartMsString();
		}
	}

	private string _soundsUndersteerLoopStartMsString = string.Empty;

	[XmlIgnore]
	public string SoundsUndersteerLoopStartMsString
	{
		get => _soundsUndersteerLoopStartMsString;

		set
		{
			if ( value != _soundsUndersteerLoopStartMsString )
			{
				_soundsUndersteerLoopStartMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsUndersteerLoopStartMsString()
	{
		SoundsUndersteerLoopStartMsString = $"{_soundsUndersteerLoopStartMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Understeer loop end ms

	private float _soundsUndersteerLoopEndMs = 0f;

	public float SoundsUndersteerLoopEndMs
	{
		get => _soundsUndersteerLoopEndMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsUndersteerLoopEndMs )
			{
				_soundsUndersteerLoopEndMs = value;

				OnPropertyChanged();

				if ( _soundsUndersteerLoopStartMs > _soundsUndersteerLoopEndMs )
				{
					SoundsUndersteerLoopStartMs = _soundsUndersteerLoopEndMs;
				}
			}

			UpdateSoundsUndersteerLoopEndMsString();
		}
	}

	private string _soundsUndersteerLoopEndMsString = string.Empty;

	[XmlIgnore]
	public string SoundsUndersteerLoopEndMsString
	{
		get => _soundsUndersteerLoopEndMsString;

		set
		{
			if ( value != _soundsUndersteerLoopEndMsString )
			{
				_soundsUndersteerLoopEndMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsUndersteerLoopEndMsString()
	{
		SoundsUndersteerLoopEndMsString = $"{_soundsUndersteerLoopEndMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Oversteer enabled

	private bool _soundsOversteerEnabled = false;

	public bool SoundsOversteerEnabled
	{
		get => _soundsOversteerEnabled;

		set
		{
			if ( value != _soundsOversteerEnabled )
			{
				_soundsOversteerEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Oversteer volume

	private float _soundsOversteerVolume = 0.75f;

	public float SoundsOversteerVolume
	{
		get => _soundsOversteerVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsOversteerVolume )
			{
				_soundsOversteerVolume = value;

				OnPropertyChanged();
			}

			UpdateSoundsOversteerVolumeString();
		}
	}

	private string _soundsOversteerVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsOversteerVolumeString
	{
		get => _soundsOversteerVolumeString;

		set
		{
			if ( value != _soundsOversteerVolumeString )
			{
				_soundsOversteerVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsOversteerVolumeString()
	{
		SoundsOversteerVolumeString = $"{_soundsOversteerVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsOversteerVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsOversteerVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Oversteer frequency ratio

	private float _soundsOversteerFrequencyRatio = 1f;

	public float SoundsOversteerFrequencyRatio
	{
		get => _soundsOversteerFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 2f );

			if ( value != _soundsOversteerFrequencyRatio )
			{
				_soundsOversteerFrequencyRatio = value;

				OnPropertyChanged();
			}

			UpdateSoundsOversteerFrequencyRatioString();
		}
	}

	private string _soundsOversteerFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsOversteerFrequencyRatioString
	{
		get => _soundsOversteerFrequencyRatioString;

		set
		{
			if ( value != _soundsOversteerFrequencyRatioString )
			{
				_soundsOversteerFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsOversteerFrequencyRatioString()
	{
		var oversteerPitchShift = _soundsOversteerFrequencyRatio * 100f - 100f;
		SoundsOversteerFrequencyRatioString = oversteerPitchShift == 0f ? DataContext.Instance.Localization[ "OFF" ] : $"{( oversteerPitchShift >= 0f ? "+" : "" )}{oversteerPitchShift:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsOversteerFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsOversteerFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Oversteer loop start ms

	private float _soundsOversteerLoopStartMs = 0f;

	public float SoundsOversteerLoopStartMs
	{
		get => _soundsOversteerLoopStartMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsOversteerLoopStartMs )
			{
				_soundsOversteerLoopStartMs = value;

				OnPropertyChanged();

				if ( _soundsOversteerLoopEndMs < _soundsOversteerLoopStartMs )
				{
					SoundsOversteerLoopEndMs = _soundsOversteerLoopStartMs;
				}
			}

			UpdateSoundsOversteerLoopStartMsString();
		}
	}

	private string _soundsOversteerLoopStartMsString = string.Empty;

	[XmlIgnore]
	public string SoundsOversteerLoopStartMsString
	{
		get => _soundsOversteerLoopStartMsString;

		set
		{
			if ( value != _soundsOversteerLoopStartMsString )
			{
				_soundsOversteerLoopStartMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsOversteerLoopStartMsString()
	{
		SoundsOversteerLoopStartMsString = $"{_soundsOversteerLoopStartMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Oversteer loop end ms

	private float _soundsOversteerLoopEndMs = 0f;

	public float SoundsOversteerLoopEndMs
	{
		get => _soundsOversteerLoopEndMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsOversteerLoopEndMs )
			{
				_soundsOversteerLoopEndMs = value;

				OnPropertyChanged();

				if ( _soundsOversteerLoopStartMs > _soundsOversteerLoopEndMs )
				{
					SoundsOversteerLoopStartMs = _soundsOversteerLoopEndMs;
				}
			}

			UpdateSoundsOversteerLoopEndMsString();
		}
	}

	private string _soundsOversteerLoopEndMsString = string.Empty;

	[XmlIgnore]
	public string SoundsOversteerLoopEndMsString
	{
		get => _soundsOversteerLoopEndMsString;

		set
		{
			if ( value != _soundsOversteerLoopEndMsString )
			{
				_soundsOversteerLoopEndMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsOversteerLoopEndMsString()
	{
		SoundsOversteerLoopEndMsString = $"{_soundsOversteerLoopEndMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Seat-of-pants enabled

	private bool _soundsSeatOfPantsEnabled = false;

	public bool SoundsSeatOfPantsEnabled
	{
		get => _soundsSeatOfPantsEnabled;

		set
		{
			if ( value != _soundsSeatOfPantsEnabled )
			{
				_soundsSeatOfPantsEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Seat-of-pants volume

	private float _soundsSeatOfPantsVolume = 0.75f;

	public float SoundsSeatOfPantsVolume
	{
		get => _soundsSeatOfPantsVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsSeatOfPantsVolume )
			{
				_soundsSeatOfPantsVolume = value;

				OnPropertyChanged();
			}

			UpdateSoundsSeatOfPantsVolumeString();
		}
	}

	private string _soundsSeatOfPantsVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsSeatOfPantsVolumeString
	{
		get => _soundsSeatOfPantsVolumeString;

		set
		{
			if ( value != _soundsSeatOfPantsVolumeString )
			{
				_soundsSeatOfPantsVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsSeatOfPantsVolumeString()
	{
		SoundsSeatOfPantsVolumeString = $"{_soundsSeatOfPantsVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsSeatOfPantsVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsSeatOfPantsVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Seat-of-pants frequency ratio

	private float _soundsSeatOfPantsFrequencyRatio = 1f;

	public float SoundsSeatOfPantsFrequencyRatio
	{
		get => _soundsSeatOfPantsFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 2f );

			if ( value != _soundsSeatOfPantsFrequencyRatio )
			{
				_soundsSeatOfPantsFrequencyRatio = value;

				OnPropertyChanged();
			}

			UpdateSoundsSeatOfPantsFrequencyRatioString();
		}
	}

	private string _soundsSeatOfPantsFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsSeatOfPantsFrequencyRatioString
	{
		get => _soundsSeatOfPantsFrequencyRatioString;

		set
		{
			if ( value != _soundsSeatOfPantsFrequencyRatioString )
			{
				_soundsSeatOfPantsFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsSeatOfPantsFrequencyRatioString()
	{
		var seatOfPantsPitchShift = _soundsSeatOfPantsFrequencyRatio * 100f - 100f;
		SoundsSeatOfPantsFrequencyRatioString = seatOfPantsPitchShift == 0f ? DataContext.Instance.Localization[ "OFF" ] : $"{( seatOfPantsPitchShift >= 0f ? "+" : "" )}{seatOfPantsPitchShift:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsSeatOfPantsFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsSeatOfPantsFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Seat-of-pants loop start ms

	private float _soundsSeatOfPantsLoopStartMs = 0f;

	public float SoundsSeatOfPantsLoopStartMs
	{
		get => _soundsSeatOfPantsLoopStartMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsSeatOfPantsLoopStartMs )
			{
				_soundsSeatOfPantsLoopStartMs = value;

				OnPropertyChanged();

				if ( _soundsSeatOfPantsLoopEndMs < _soundsSeatOfPantsLoopStartMs )
				{
					SoundsSeatOfPantsLoopEndMs = _soundsSeatOfPantsLoopStartMs;
				}
			}

			UpdateSoundsSeatOfPantsLoopStartMsString();
		}
	}

	private string _soundsSeatOfPantsLoopStartMsString = string.Empty;

	[XmlIgnore]
	public string SoundsSeatOfPantsLoopStartMsString
	{
		get => _soundsSeatOfPantsLoopStartMsString;

		set
		{
			if ( value != _soundsSeatOfPantsLoopStartMsString )
			{
				_soundsSeatOfPantsLoopStartMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsSeatOfPantsLoopStartMsString()
	{
		SoundsSeatOfPantsLoopStartMsString = $"{_soundsSeatOfPantsLoopStartMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Seat-of-pants loop end ms

	private float _soundsSeatOfPantsLoopEndMs = 0f;

	public float SoundsSeatOfPantsLoopEndMs
	{
		get => _soundsSeatOfPantsLoopEndMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsSeatOfPantsLoopEndMs )
			{
				_soundsSeatOfPantsLoopEndMs = value;

				OnPropertyChanged();

				if ( _soundsSeatOfPantsLoopStartMs > _soundsSeatOfPantsLoopEndMs )
				{
					SoundsSeatOfPantsLoopStartMs = _soundsSeatOfPantsLoopEndMs;
				}
			}

			UpdateSoundsSeatOfPantsLoopEndMsString();
		}
	}

	private string _soundsSeatOfPantsLoopEndMsString = string.Empty;

	[XmlIgnore]
	public string SoundsSeatOfPantsLoopEndMsString
	{
		get => _soundsSeatOfPantsLoopEndMsString;

		set
		{
			if ( value != _soundsSeatOfPantsLoopEndMsString )
			{
				_soundsSeatOfPantsLoopEndMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsSeatOfPantsLoopEndMsString()
	{
		SoundsSeatOfPantsLoopEndMsString = $"{_soundsSeatOfPantsLoopEndMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Brake + throttle warning enabled

	private bool _soundsBrakeThrottleWarningEnabled = false;

	public bool SoundsBrakeThrottleWarningEnabled
	{
		get => _soundsBrakeThrottleWarningEnabled;

		set
		{
			if ( value != _soundsBrakeThrottleWarningEnabled )
			{
				_soundsBrakeThrottleWarningEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - Brake + throttle warning volume

	private float _soundsBrakeThrottleWarningVolume = 0.75f;

	public float SoundsBrakeThrottleWarningVolume
	{
		get => _soundsBrakeThrottleWarningVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsBrakeThrottleWarningVolume )
			{
				_soundsBrakeThrottleWarningVolume = value;

				OnPropertyChanged();
			}

			UpdateSoundsBrakeThrottleWarningVolumeString();
		}
	}

	private string _soundsBrakeThrottleWarningVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsBrakeThrottleWarningVolumeString
	{
		get => _soundsBrakeThrottleWarningVolumeString;

		set
		{
			if ( value != _soundsBrakeThrottleWarningVolumeString )
			{
				_soundsBrakeThrottleWarningVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsBrakeThrottleWarningVolumeString()
	{
		SoundsBrakeThrottleWarningVolumeString = $"{_soundsBrakeThrottleWarningVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsBrakeThrottleWarningVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsBrakeThrottleWarningVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Brake + throttle warning frequency ratio

	private float _soundsBrakeThrottleWarningFrequencyRatio = 1f;

	public float SoundsBrakeThrottleWarningFrequencyRatio
	{
		get => _soundsBrakeThrottleWarningFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 2f );

			if ( value != _soundsBrakeThrottleWarningFrequencyRatio )
			{
				_soundsBrakeThrottleWarningFrequencyRatio = value;

				OnPropertyChanged();
			}

			UpdateSoundsBrakeThrottleWarningFrequencyRatioString();
		}
	}

	private string _soundsBrakeThrottleWarningFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsBrakeThrottleWarningFrequencyRatioString
	{
		get => _soundsBrakeThrottleWarningFrequencyRatioString;

		set
		{
			if ( value != _soundsBrakeThrottleWarningFrequencyRatioString )
			{
				_soundsBrakeThrottleWarningFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsBrakeThrottleWarningFrequencyRatioString()
	{
		var pitchShift = _soundsBrakeThrottleWarningFrequencyRatio * 100f - 100f;
		SoundsBrakeThrottleWarningFrequencyRatioString = pitchShift == 0f ? DataContext.Instance.Localization[ "OFF" ] : $"{( pitchShift >= 0f ? "+" : "" )}{pitchShift:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsBrakeThrottleWarningFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsBrakeThrottleWarningFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - Brake throttle warning loop start ms

	private float _soundsBrakeThrottleWarningLoopStartMs = 0f;

	public float SoundsBrakeThrottleWarningLoopStartMs
	{
		get => _soundsBrakeThrottleWarningLoopStartMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsBrakeThrottleWarningLoopStartMs )
			{
				_soundsBrakeThrottleWarningLoopStartMs = value;

				OnPropertyChanged();

				if ( _soundsBrakeThrottleWarningLoopEndMs < _soundsBrakeThrottleWarningLoopStartMs )
				{
					SoundsBrakeThrottleWarningLoopEndMs = _soundsBrakeThrottleWarningLoopStartMs;
				}
			}

			UpdateSoundsBrakeThrottleWarningLoopStartMsString();
		}
	}

	private string _soundsBrakeThrottleWarningLoopStartMsString = string.Empty;

	[XmlIgnore]
	public string SoundsBrakeThrottleWarningLoopStartMsString
	{
		get => _soundsBrakeThrottleWarningLoopStartMsString;

		set
		{
			if ( value != _soundsBrakeThrottleWarningLoopStartMsString )
			{
				_soundsBrakeThrottleWarningLoopStartMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsBrakeThrottleWarningLoopStartMsString()
	{
		SoundsBrakeThrottleWarningLoopStartMsString = $"{_soundsBrakeThrottleWarningLoopStartMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - Brake throttle warning loop end ms

	private float _soundsBrakeThrottleWarningLoopEndMs = 0f;

	public float SoundsBrakeThrottleWarningLoopEndMs
	{
		get => _soundsBrakeThrottleWarningLoopEndMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsBrakeThrottleWarningLoopEndMs )
			{
				_soundsBrakeThrottleWarningLoopEndMs = value;

				OnPropertyChanged();

				if ( _soundsBrakeThrottleWarningLoopStartMs > _soundsBrakeThrottleWarningLoopEndMs )
				{
					SoundsBrakeThrottleWarningLoopStartMs = _soundsBrakeThrottleWarningLoopEndMs;
				}
			}

			UpdateSoundsBrakeThrottleWarningLoopEndMsString();
		}
	}

	private string _soundsBrakeThrottleWarningLoopEndMsString = string.Empty;

	[XmlIgnore]
	public string SoundsBrakeThrottleWarningLoopEndMsString
	{
		get => _soundsBrakeThrottleWarningLoopEndMsString;

		set
		{
			if ( value != _soundsBrakeThrottleWarningLoopEndMsString )
			{
				_soundsBrakeThrottleWarningLoopEndMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsBrakeThrottleWarningLoopEndMsString()
	{
		SoundsBrakeThrottleWarningLoopEndMsString = $"{_soundsBrakeThrottleWarningLoopEndMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - FFB clipping enabled

	private bool _soundsFfbClippingEnabled = false;

	public bool SoundsFfbClippingEnabled
	{
		get => _soundsFfbClippingEnabled;

		set
		{
			if ( value != _soundsFfbClippingEnabled )
			{
				_soundsFfbClippingEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Sounds - FFB clipping volume

	private float _soundsFfbClippingVolume = 0.75f;

	public float SoundsFfbClippingVolume
	{
		get => _soundsFfbClippingVolume;

		set
		{
			value = MathZ.Saturate( value );

			if ( value != _soundsFfbClippingVolume )
			{
				_soundsFfbClippingVolume = value;

				OnPropertyChanged();
			}

			UpdateSoundsFfbClippingVolumeString();
		}
	}

	private string _soundsFfbClippingVolumeString = string.Empty;

	[XmlIgnore]
	public string SoundsFfbClippingVolumeString
	{
		get => _soundsFfbClippingVolumeString;

		set
		{
			if ( value != _soundsFfbClippingVolumeString )
			{
				_soundsFfbClippingVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsFfbClippingVolumeString()
	{
		SoundsFfbClippingVolumeString = $"{_soundsFfbClippingVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsFfbClippingVolumePlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsFfbClippingVolumeMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - FFB clipping frequency ratio

	private float _soundsFfbClippingFrequencyRatio = 1f;

	public float SoundsFfbClippingFrequencyRatio
	{
		get => _soundsFfbClippingFrequencyRatio;

		set
		{
			value = Math.Clamp( value, 0.25f, 2f );

			if ( value != _soundsFfbClippingFrequencyRatio )
			{
				_soundsFfbClippingFrequencyRatio = value;

				OnPropertyChanged();
			}

			UpdateSoundsFfbClippingFrequencyRatioString();
		}
	}

	private string _soundsFfbClippingFrequencyRatioString = string.Empty;

	[XmlIgnore]
	public string SoundsFfbClippingFrequencyRatioString
	{
		get => _soundsFfbClippingFrequencyRatioString;

		set
		{
			if ( value != _soundsFfbClippingFrequencyRatioString )
			{
				_soundsFfbClippingFrequencyRatioString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsFfbClippingFrequencyRatioString()
	{
		var pitchShift = _soundsFfbClippingFrequencyRatio * 100f - 100f;
		SoundsFfbClippingFrequencyRatioString = pitchShift == 0f ? DataContext.Instance.Localization[ "OFF" ] : $"{( pitchShift >= 0f ? "+" : "" )}{pitchShift:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	public ButtonMappings SoundsFfbClippingFrequencyRatioPlusButtonMappings { get; set; } = new();
	public ButtonMappings SoundsFfbClippingFrequencyRatioMinusButtonMappings { get; set; } = new();

	#endregion

	#region Sounds - FFB clipping loop start ms

	private float _soundsFfbClippingLoopStartMs = 0f;

	public float SoundsFfbClippingLoopStartMs
	{
		get => _soundsFfbClippingLoopStartMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsFfbClippingLoopStartMs )
			{
				_soundsFfbClippingLoopStartMs = value;

				OnPropertyChanged();

				if ( _soundsFfbClippingLoopEndMs < _soundsFfbClippingLoopStartMs )
				{
					SoundsFfbClippingLoopEndMs = _soundsFfbClippingLoopStartMs;
				}
			}

			UpdateSoundsFfbClippingLoopStartMsString();
		}
	}

	private string _soundsFfbClippingLoopStartMsString = string.Empty;

	[XmlIgnore]
	public string SoundsFfbClippingLoopStartMsString
	{
		get => _soundsFfbClippingLoopStartMsString;

		set
		{
			if ( value != _soundsFfbClippingLoopStartMsString )
			{
				_soundsFfbClippingLoopStartMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsFfbClippingLoopStartMsString()
	{
		SoundsFfbClippingLoopStartMsString = $"{_soundsFfbClippingLoopStartMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Sounds - FFB clipping loop end ms

	private float _soundsFfbClippingLoopEndMs = 0f;

	public float SoundsFfbClippingLoopEndMs
	{
		get => _soundsFfbClippingLoopEndMs;

		set
		{
			value = Math.Max( 0f, value );

			if ( value != _soundsFfbClippingLoopEndMs )
			{
				_soundsFfbClippingLoopEndMs = value;

				OnPropertyChanged();

				if ( _soundsFfbClippingLoopStartMs > _soundsFfbClippingLoopEndMs )
				{
					SoundsFfbClippingLoopStartMs = _soundsFfbClippingLoopEndMs;
				}
			}

			UpdateSoundsFfbClippingLoopEndMsString();
		}
	}

	private string _soundsFfbClippingLoopEndMsString = string.Empty;

	[XmlIgnore]
	public string SoundsFfbClippingLoopEndMsString
	{
		get => _soundsFfbClippingLoopEndMsString;

		set
		{
			if ( value != _soundsFfbClippingLoopEndMsString )
			{
				_soundsFfbClippingLoopEndMsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateSoundsFfbClippingLoopEndMsString()
	{
		SoundsFfbClippingLoopEndMsString = $"{_soundsFfbClippingLoopEndMs:F0} {DataContext.Instance.Localization[ "Milliseconds" ]}";
	}

	#endregion

	#region Speech to text

	private bool _speechToTextEnabled = false;

	public bool SpeechToTextEnabled
	{
		get => _speechToTextEnabled;

		set
		{
			if ( value != _speechToTextEnabled )
			{
				_speechToTextEnabled = value;

				OnPropertyChanged();

				var app = App.Instance!;

				if ( _speechToTextEnabled )
				{
					if ( app.Simulator.IsConnected )
					{
						_ = app.SpeechToText.EnableAsync();
					}
				}
				else
				{
					_ = app.SpeechToText.DisableAsync();
				}

				// refresh the overlay so the edit-mode preview reflects the enabled state even when not connected to the simulator
				app.UpdateSpeechToTextWindowVisibility();
			}
		}
	}

	#endregion

	#region Speech to text - Recording device

	private string _speechToTextRecordingDevice = Components.SpeechToText.DefaultRecordingDeviceName;

	public string SpeechToTextRecordingDevice
	{
		get => _speechToTextRecordingDevice;

		set
		{
			var recordingDevice = string.IsNullOrWhiteSpace( value ) ? Components.SpeechToText.DefaultRecordingDeviceName : value;

			if ( recordingDevice != _speechToTextRecordingDevice )
			{
				_speechToTextRecordingDevice = recordingDevice;

				OnPropertyChanged();

				App.Instance!.SpeechToText.RecordingDevice = recordingDevice;
			}
		}
	}

	#endregion

	#region Trading paints - Enabled

	private bool _tradingPaintsEnabled = false;

	public bool TradingPaintsEnabled
	{
		get => _tradingPaintsEnabled;

		set
		{
			if ( value != _tradingPaintsEnabled )
			{
				_tradingPaintsEnabled = value;

				OnPropertyChanged();

				App.Instance?.TradingPaints.Reset();
			}
		}
	}

	#endregion

	#region Trading paints - Delete paints on simulator exit

	private bool _tradingPaintsDeletePaintsOnSimulatorExit = false;

	public bool TradingPaintsDeletePaintsOnSimulatorExit
	{
		get => _tradingPaintsDeletePaintsOnSimulatorExit;

		set
		{
			if ( value != _tradingPaintsDeletePaintsOnSimulatorExit )
			{
				_tradingPaintsDeletePaintsOnSimulatorExit = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Trading paints - Folder

	private string _tradingPaintsFolder = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), "iRacing", "paint" );

	public string TradingPaintsFolder
	{
		get => _tradingPaintsFolder;

		set
		{
			if ( value != _tradingPaintsFolder )
			{
				_tradingPaintsFolder = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Trading paints - Max Download Speed

	private float _tradingPaintsMaxDownloadSpeedKbps = 1024f;

	public float TradingPaintsMaxDownloadSpeedKbps
	{
		get => _tradingPaintsMaxDownloadSpeedKbps;

		set
		{
			value = Math.Clamp( value, 64f, 10240f );

			if ( value != _tradingPaintsMaxDownloadSpeedKbps )
			{
				_tradingPaintsMaxDownloadSpeedKbps = value;

				OnPropertyChanged();
			}

			UpdateTradingPaintsMaxDownloadSpeedKbpsString();
		}
	}

	private string _tradingPaintsMaxDownloadSpeedKbpsString = "1024 KB/s";

	[XmlIgnore]
	public string TradingPaintsMaxDownloadSpeedKbpsString
	{
		get => _tradingPaintsMaxDownloadSpeedKbpsString;

		set
		{
			if ( value != _tradingPaintsMaxDownloadSpeedKbpsString )
			{
				_tradingPaintsMaxDownloadSpeedKbpsString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateTradingPaintsMaxDownloadSpeedKbpsString()
	{
		TradingPaintsMaxDownloadSpeedKbpsString = $"{(int) MathF.Round( _tradingPaintsMaxDownloadSpeedKbps )} KB/s";
	}

	#endregion

	#region Trading paints - Re-download

	public ButtonMappings TradingPaintsRedownloadButtonMappings { get; set; } = new();

	#endregion

	#region Graph - Statistics

	private Graph.LayerIndex _graphStatisticsLayerIndex = Graph.LayerIndex.TimerJitter;

	public Graph.LayerIndex GraphStatisticsLayerIndex
	{
		get => _graphStatisticsLayerIndex;

		set
		{
			if ( value != _graphStatisticsLayerIndex )
			{
				_graphStatisticsLayerIndex = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Input torque

	private bool _graphInputTorque = true;

	public bool GraphInputTorque
	{
		get => _graphInputTorque;

		set
		{
			if ( value != _graphInputTorque )
			{
				_graphInputTorque = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Output torque

	private bool _graphOutputTorque = true;

	public bool GraphOutputTorque
	{
		get => _graphOutputTorque;

		set
		{
			if ( value != _graphOutputTorque )
			{
				_graphOutputTorque = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Input torque (60 Hz)

	private bool _graphInputTorque60Hz = false;

	public bool GraphInputTorque60Hz
	{
		get => _graphInputTorque60Hz;

		set
		{
			if ( value != _graphInputTorque60Hz )
			{
				_graphInputTorque60Hz = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Input LFE

	private bool _graphInputLFE = false;

	public bool GraphInputLFE
	{
		get => _graphInputLFE;

		set
		{
			if ( value != _graphInputLFE )
			{
				_graphInputLFE = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Clutch pedal haptics

	private bool _graphClutchPedalHaptics = false;

	public bool GraphClutchPedalHaptics
	{
		get => _graphClutchPedalHaptics;

		set
		{
			if ( value != _graphClutchPedalHaptics )
			{
				_graphClutchPedalHaptics = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Brake pedal haptics

	private bool _graphBrakePedalHaptics = false;

	public bool GraphBrakePedalHaptics
	{
		get => _graphBrakePedalHaptics;

		set
		{
			if ( value != _graphBrakePedalHaptics )
			{
				_graphBrakePedalHaptics = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Throttle pedal haptics

	private bool _graphThrottlePedalHaptics = false;

	public bool GraphThrottlePedalHaptics
	{
		get => _graphThrottlePedalHaptics;

		set
		{
			if ( value != _graphThrottlePedalHaptics )
			{
				_graphThrottlePedalHaptics = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Graph - Timer jitter

	private bool _graphTimerJitter = false;

	public bool GraphTimerJitter
	{
		get => _graphTimerJitter;

		set
		{
			if ( value != _graphTimerJitter )
			{
				_graphTimerJitter = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App Manager - Enabled

	private bool _appManagerEnabled = true;

	public bool AppManagerEnabled
	{
		get => _appManagerEnabled;

		set
		{
			if ( value != _appManagerEnabled )
			{
				_appManagerEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App Manager - Start list

	public List<AppManagerStartEntry> AppManagerStartList { get; set; } = [];

	#endregion

	#region App Manager - Terminate list

	public List<AppManagerTerminateEntry> AppManagerTerminateList { get; set; } = [];

	#endregion

	#region App - Current language code

	private string _appCurrentLanguageCode = "default";

	public string AppCurrentLanguageCode
	{
		get => _appCurrentLanguageCode;

		set
		{
			DataContext.Instance.Localization.LoadLanguage( value ); // Always try to load the language (Localization has it's own _currentLanguageCode)

			if ( value != _appCurrentLanguageCode )
			{
				_appCurrentLanguageCode = value;

				var app = App.Instance!;

				if ( app.Ready )
				{
					OnPropertyChanged();

					app.MainWindow.RefreshWindow();
				}
			}
		}
	}

	#endregion

	#region App - Default page

#if !ADMINBOXX

	private MainWindow.AppPage _appDefaultPage = MainWindow.AppPage.RacingWheel;

#else

	private MainWindow.AppPage _appDefaultPage = MainWindow.AppPage.AdminBoxx;

#endif

	public MainWindow.AppPage AppDefaultPage
	{
		get => _appDefaultPage;

		set
		{
			if ( value != _appDefaultPage )
			{
				_appDefaultPage = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Show splash screen

	private bool _appShowSplashScreen = true;

	public bool AppShowSplashScreen
	{
		get => _appShowSplashScreen;

		set
		{
			if ( value != _appShowSplashScreen )
			{
				_appShowSplashScreen = value;

				OnPropertyChanged();

				var app = App.Instance!;

				var disableSplashScreenFilePath = Path.Combine( App.DocumentsFolder, "DisableSplashScreen.txt" );

				if ( !_appShowSplashScreen )
				{
					try
					{
						if ( !File.Exists( disableSplashScreenFilePath ) )
						{
							File.WriteAllText( disableSplashScreenFilePath, "This file disables the splash screen on startup." );

							app.Logger.WriteLine( "[Settings] Created DisableSplashScreen.txt file" );
						}
					}
					catch ( Exception ex )
					{
						app.Logger.WriteLine( $"[Settings] Failed to create DisableSplashScreen.txt: {ex.Message}" );
					}
				}
				else
				{
					try
					{
						if ( File.Exists( disableSplashScreenFilePath ) )
						{
							File.Delete( disableSplashScreenFilePath );

							app.Logger.WriteLine( "[Settings] Deleted DisableSplashScreen.txt file" );
						}
					}
					catch ( Exception ex )
					{
						app.Logger.WriteLine( $"[Settings] Failed to delete DisableSplashScreen.txt: {ex.Message}" );
					}
				}
			}
		}
	}

	#endregion

	#region App - Topmost window enabled

	private bool _appTopmostWindowEnabled = false;

	public bool AppTopmostWindowEnabled
	{
		get => _appTopmostWindowEnabled;

		set
		{
			if ( value != _appTopmostWindowEnabled )
			{
				_appTopmostWindowEnabled = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.MainWindow.Topmost = _appTopmostWindowEnabled;
		}
	}

	#endregion

	#region App - Remember window position and size

	private bool _appRememberWindowPositionAndSize = false;

	public bool AppRememberWindowPositionAndSize
	{
		get => _appRememberWindowPositionAndSize;

		set
		{
			if ( value != _appRememberWindowPositionAndSize )
			{
				_appRememberWindowPositionAndSize = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Window position and size

	private Rectangle _appWindowPositionAndSize = Rectangle.Empty;

	public Rectangle AppWindowPositionAndSize
	{
		get => _appWindowPositionAndSize;

		set
		{
			if ( value != _appWindowPositionAndSize )
			{
				_appWindowPositionAndSize = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Start with Windows

	private bool _appStartWithWindows = false;

	public bool AppStartWithWindows
	{
		get => _appStartWithWindows;

		set
		{
			if ( value != _appStartWithWindows )
			{
				_appStartWithWindows = value;

				OnPropertyChanged();
			}

			Misc.SetStartWithWindows( _appStartWithWindows );
		}
	}

	#endregion

	#region App - Start minimized

	private bool _appStartMinimized = false;

	public bool AppStartMinimized
	{
		get => _appStartMinimized;

		set
		{
			if ( value != _appStartMinimized )
			{
				_appStartMinimized = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Minimize to system tray

	private bool _appMinimizeToSystemTray = false;

	public bool AppMinimizeToSystemTray
	{
		get => _appMinimizeToSystemTray;

		set
		{
			if ( value != _appMinimizeToSystemTray )
			{
				_appMinimizeToSystemTray = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.MainWindow.UpdateNotifyIcon();
		}
	}

	#endregion

	#region App - Minimize instead of closing

	private bool _appMinimizeInsteadOfClosing = false;

	public bool AppMinimizeInsteadOfClosing
	{
		get => _appMinimizeInsteadOfClosing;

		set
		{
			if ( value != _appMinimizeInsteadOfClosing )
			{
				_appMinimizeInsteadOfClosing = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - UI scale

	private float _appUIScale = 1f;

	public float AppUIScale
	{
		get => _appUIScale;

		set
		{
			value = Math.Clamp( value, 0.5f, 2f );

			if ( value != _appUIScale )
			{
				_appUIScale = value;

				OnPropertyChanged();
			}

			UpdateAppUIScaleString();
		}
	}

	private string _appUIScaleString = string.Empty;

	[XmlIgnore]
	public string AppUIScaleString
	{
		get => _appUIScaleString;

		set
		{
			if ( value != _appUIScaleString )
			{
				_appUIScaleString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateAppUIScaleString()
	{
		AppUIScaleString = $"{_appUIScale * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region App - Check for updates

	private bool _appCheckForUpdates = true;

	public bool AppCheckForUpdates
	{
		get => _appCheckForUpdates;

		set
		{
			if ( value != _appCheckForUpdates )
			{
				_appCheckForUpdates = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Check for updates on interval

	private bool _appCheckForUpdatesOnInterval = true;

	public bool AppCheckForUpdatesOnInterval
	{
		get => _appCheckForUpdatesOnInterval;

		set
		{
			if ( value != _appCheckForUpdatesOnInterval )
			{
				_appCheckForUpdatesOnInterval = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - Update check interval

	private float _appUpdateCheckIntervalHours = 1f;

	public float AppUpdateCheckIntervalHours
	{
		get => _appUpdateCheckIntervalHours;

		set
		{
			value = float.IsNaN( value ) ? 1f : value;

			value = Math.Clamp( value, 1f, 168f );

			if ( value != _appUpdateCheckIntervalHours )
			{
				_appUpdateCheckIntervalHours = value;

				OnPropertyChanged();
			}

			UpdateAppUpdateCheckIntervalHoursString();
		}
	}

	private string _appUpdateCheckIntervalHoursString = string.Empty;

	[XmlIgnore]
	public string AppUpdateCheckIntervalHoursString
	{
		get => _appUpdateCheckIntervalHoursString;

		set
		{
			if ( value != _appUpdateCheckIntervalHoursString )
			{
				_appUpdateCheckIntervalHoursString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateAppUpdateCheckIntervalHoursString()
	{
		AppUpdateCheckIntervalHoursString = $"{_appUpdateCheckIntervalHours:F0}{DataContext.Instance.Localization[ "HoursUnits" ]}";
	}

	#endregion

	#region App - Light theme enabled

	private bool _appLightThemeEnabled = false;

	public bool AppLightThemeEnabled
	{
		get => _appLightThemeEnabled;

		set
		{
			if ( value != _appLightThemeEnabled )
			{
				_appLightThemeEnabled = value;

				OnPropertyChanged();
			}

			var app = App.Instance!;

			app.ApplyTheme( _appLightThemeEnabled );
		}
	}

	#endregion

	#region App - Wizard has run

	private bool _appWizardHasRun = false;

	public bool AppWizardHasRun
	{
		get => _appWizardHasRun;

		set
		{
			if ( value != _appWizardHasRun )
			{
				_appWizardHasRun = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region App - CPU affinity

	private ulong _appAffinityMaskBits = CpuAffinityHelper.GetDefaultAffinityMask();

	public ulong AppAffinityMaskBits
	{
		get => _appAffinityMaskBits;

		set
		{
			if ( value != _appAffinityMaskBits )
			{
				_appAffinityMaskBits = value;

				OnPropertyChanged();

				try
				{
					CpuAffinityHelper.SetCpuAffinity( _appAffinityMaskBits );
				}
				catch ( Exception ex )
				{
					var app = App.Instance!;

					app.Logger.WriteLine( $"[Settings] Failed to set CPU affinity: {ex.Message}" );
				}
			}
		}
	}

	#endregion

	#region App - Toggle main window

	public ButtonMappings AppToggleMainWindowButtonMappings { get; set; } = new();

	#endregion

	#region Commentary — Master enable

	private bool _commentaryEnabled = false;

	public bool CommentaryEnabled
	{
		get => _commentaryEnabled;

		set
		{
			if ( value != _commentaryEnabled )
			{
				_commentaryEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Commentary — ElevenLabs Language

	private string _commentaryElevenLabsLanguage = "en-US";

	public string CommentaryElevenLabsLanguage
	{
		get => _commentaryElevenLabsLanguage;

		set
		{
			value = string.IsNullOrWhiteSpace( value ) ? "en-US" : value.Trim();

			if ( value != _commentaryElevenLabsLanguage )
			{
				_commentaryElevenLabsLanguage = value;

				App.Instance?.Commentary.Initialize( value );

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Commentary — Master volume

	private float _commentaryMasterVolume = 0.85f;

	public float CommentaryMasterVolume
	{
		get => _commentaryMasterVolume;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _commentaryMasterVolume )
			{
				_commentaryMasterVolume = value;

				OnPropertyChanged();
			}

			UpdateCommentaryMasterVolumeString();
		}
	}

	private string _commentaryMasterVolumeString = string.Empty;

	[XmlIgnore]
	public string CommentaryMasterVolumeString
	{
		get => _commentaryMasterVolumeString;

		set
		{
			if ( value != _commentaryMasterVolumeString )
			{
				_commentaryMasterVolumeString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateCommentaryMasterVolumeString()
	{
		CommentaryMasterVolumeString = $"{_commentaryMasterVolume * 100f:F0}{DataContext.Instance.Localization[ "Percent" ]}";
	}

	#endregion

	#region Commentary — ElevenLabs API key (DPAPI — not serialized to Settings.xml)

	[XmlIgnore]
	public string CommentaryElevenLabsApiKey
	{
		get => ElevenLabsKeyStore.LoadKey( "tts" );

		set
		{
			ElevenLabsKeyStore.SaveKey( "tts", value ?? string.Empty );

			OnPropertyChanged();
		}
	}

	#endregion

	#region Speech to text — ElevenLabs API key (DPAPI — not serialized to Settings.xml)

	[XmlIgnore]
	public string SpeechToTextElevenLabsApiKey
	{
		get => ElevenLabsKeyStore.LoadKey( "stt" );

		set
		{
			ElevenLabsKeyStore.SaveKey( "stt", value ?? string.Empty );

			OnPropertyChanged();
		}
	}

	#endregion

	#region Commentary — ElevenLabs Model

	private string _commentaryElevenLabsModelId = "";

	public string CommentaryElevenLabsModelId
	{
		get => _commentaryElevenLabsModelId;

		set
		{
			if ( value != _commentaryElevenLabsModelId )
			{
				_commentaryElevenLabsModelId = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Commentary — Voice slots

	private List<VoiceSlotSettings> _commentaryVoiceSlots = VoiceSlotSettings.CreateDefaults();

	public List<VoiceSlotSettings> CommentaryVoiceSlots
	{
		get => _commentaryVoiceSlots;

		set
		{
			_commentaryVoiceSlots = value ?? VoiceSlotSettings.CreateDefaults();

			// Ensure all slots are always present, filling any missing tail entries with defaults.
			// (Older settings files saved with 5 slots gain the appended MAIRA slot here.)
			var defaults = VoiceSlotSettings.CreateDefaults();

			while ( _commentaryVoiceSlots.Count < defaults.Count )
			{
				_commentaryVoiceSlots.Add( defaults[ _commentaryVoiceSlots.Count ] );
			}

			OnPropertyChanged();
		}
	}

	#endregion

	#region Commentary — Spotter enabled

	private bool _commentarySpotterEnabled = true;

	public bool CommentarySpotterEnabled
	{
		get => _commentarySpotterEnabled;

		set
		{
			if ( value != _commentarySpotterEnabled )
			{
				_commentarySpotterEnabled = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Commentary — Spotter proximity calls (per session phase)

	private bool _commentarySpotterProximityPractice = true;

	public bool CommentarySpotterProximityPractice
	{
		get => _commentarySpotterProximityPractice;

		set
		{
			if ( value != _commentarySpotterProximityPractice )
			{
				_commentarySpotterProximityPractice = value;

				OnPropertyChanged();
			}
		}
	}

	private bool _commentarySpotterProximityQualifying = true;

	public bool CommentarySpotterProximityQualifying
	{
		get => _commentarySpotterProximityQualifying;

		set
		{
			if ( value != _commentarySpotterProximityQualifying )
			{
				_commentarySpotterProximityQualifying = value;

				OnPropertyChanged();
			}
		}
	}

	private bool _commentarySpotterProximityRace = true;

	public bool CommentarySpotterProximityRace
	{
		get => _commentarySpotterProximityRace;

		set
		{
			if ( value != _commentarySpotterProximityRace )
			{
				_commentarySpotterProximityRace = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Commentary — Spotter flag calls (per session phase)

	private bool _commentarySpotterFlagCallsPractice = true;

	public bool CommentarySpotterFlagCallsPractice
	{
		get => _commentarySpotterFlagCallsPractice;

		set
		{
			if ( value != _commentarySpotterFlagCallsPractice )
			{
				_commentarySpotterFlagCallsPractice = value;

				OnPropertyChanged();
			}
		}
	}

	private bool _commentarySpotterFlagCallsQualifying = true;

	public bool CommentarySpotterFlagCallsQualifying
	{
		get => _commentarySpotterFlagCallsQualifying;

		set
		{
			if ( value != _commentarySpotterFlagCallsQualifying )
			{
				_commentarySpotterFlagCallsQualifying = value;

				OnPropertyChanged();
			}
		}
	}

	private bool _commentarySpotterFlagCallsRace = true;

	public bool CommentarySpotterFlagCallsRace
	{
		get => _commentarySpotterFlagCallsRace;

		set
		{
			if ( value != _commentarySpotterFlagCallsRace )
			{
				_commentarySpotterFlagCallsRace = value;

				OnPropertyChanged();
			}
		}
	}

	#endregion

	#region Commentary — Spotter car proximity reminder interval

	private float _commentarySpotterCarProximityReminderInterval = 3.0f;

	public float CommentarySpotterCarProximityReminderInterval
	{
		get => _commentarySpotterCarProximityReminderInterval;

		set
		{
			value = Math.Clamp( value, 1.0f, 20.0f );

			if ( value != _commentarySpotterCarProximityReminderInterval )
			{
				_commentarySpotterCarProximityReminderInterval = value;

				OnPropertyChanged();
			}

			UpdateCommentarySpotterCarProximityReminderIntervalString();
		}
	}

	private string _commentarySpotterCarProximityReminderIntervalString = string.Empty;

	[XmlIgnore]
	public string CommentarySpotterCarProximityReminderIntervalString
	{
		get => _commentarySpotterCarProximityReminderIntervalString;

		set
		{
			if ( value != _commentarySpotterCarProximityReminderIntervalString )
			{
				_commentarySpotterCarProximityReminderIntervalString = value;

				OnPropertyChanged();
			}
		}
	}

	private void UpdateCommentarySpotterCarProximityReminderIntervalString()
	{
		CommentarySpotterCarProximityReminderIntervalString = $"{_commentarySpotterCarProximityReminderInterval:F1} {DataContext.Instance.Localization[ "SecondsUnits" ]}";
	}

	#endregion

	#region Commentary — Disabled phrase groups

	// Commentary phrase groups (event keys) the user has switched off in the phrase editor. A key is
	// present here only when it is disabled; an absent key means enabled. Storing just the disabled keys
	// keeps the settings file minimal and makes "absent = enabled" the default, so existing settings files
	// and any newly added event keys default to on. Enforcement lives in CommentaryTemplates.GetRandomPhrase.
	public SerializableDictionary<string, bool> CommentaryDisabledEventKeys { get; set; } = [];

	public bool IsCommentaryEventKeyEnabled( string eventKey )
	{
		return !CommentaryDisabledEventKeys.ContainsKey( eventKey );
	}

	public void SetCommentaryEventKeyEnabled( string eventKey, bool enabled )
	{
		if ( enabled )
		{
			CommentaryDisabledEventKeys.Remove( eventKey );
		}
		else
		{
			CommentaryDisabledEventKeys[ eventKey ] = true;
		}
	}

	#endregion
}
