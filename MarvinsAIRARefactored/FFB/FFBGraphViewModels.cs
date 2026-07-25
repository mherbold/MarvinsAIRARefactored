
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace MarvinsAIRARefactored.FFB;

// View-models backing the RacingWheelPage FFB graph editor. The editor binds to
// DataContext.Instance.RacingWheelGraphViewModel, which mirrors the currently selected graph's modules. Edits
// mutate the FFBGraph model, push atomic values into both engines, keep the per-context store in sync, queue
// serialization, and refresh the preview — the same responsibilities the old per-setting INPC setters had.
//
// Milestone 5 will localize the display names/labels (currently derived from the stable keys); for now they are
// humanized English so the editor is readable.

public static partial class FFBDisplayNames
{
	private static readonly Dictionary<string, string> ModuleNames = new( StringComparer.Ordinal )
	{
		[ FFBModuleRegistry.Source60HzType ] = "60 Hz source",
		[ FFBModuleRegistry.Source360HzType ] = "360 Hz source",
		[ FFBModuleRegistry.SourceLFEType ] = "LFE source",
		[ FFBModuleRegistry.SourceWheelVelocityType ] = "Wheel velocity source",
		[ FFBModuleRegistry.SourceSoftLockType ] = "Soft lock source",
		[ FFBModuleRegistry.SourceWheelCenteringType ] = "Wheel centering source",
		[ FFBModuleRegistry.OutputType ] = "Output",
		[ FFBModuleRegistry.LowPassFilterType ] = "Low-pass filter",
		[ FFBModuleRegistry.HighPassFilterType ] = "High-pass filter",
		[ FFBModuleRegistry.GainType ] = "Gain",
		[ FFBModuleRegistry.AddType ] = "Add",
		[ FFBModuleRegistry.SubtractType ] = "Subtract",
		[ FFBModuleRegistry.BlendType ] = "Blend",
		[ FFBModuleRegistry.SlewLimiterType ] = "Slew limiter",
		[ FFBModuleRegistry.SlewCompressorType ] = "Slew compressor",
		[ FFBModuleRegistry.CompressorType ] = "Compressor",
		[ FFBModuleRegistry.TransientEnhancerType ] = "Transient enhancer",
		[ FFBModuleRegistry.AdaptiveSmootherType ] = "Adaptive smoother",
		[ FFBModuleRegistry.InterpolatorType ] = "60 Hz interpolator",
		[ FFBModuleRegistry.PredictionType ] = "Prediction",
		[ FFBModuleRegistry.AdaptiveBlendType ] = "Adaptive blend",
		[ FFBModuleRegistry.CrashProtectionType ] = "Crash protection",
		[ FFBModuleRegistry.CurbProtectionType ] = "Curb protection",
		[ FFBModuleRegistry.UndersteerForceType ] = "Understeer force",
		[ FFBModuleRegistry.OversteerForceType ] = "Oversteer force",
		[ FFBModuleRegistry.SeatOfPantsForceType ] = "Seat-of-pants force",
		[ FFBModuleRegistry.UndersteerVibrationType ] = "Understeer vibration",
		[ FFBModuleRegistry.OversteerVibrationType ] = "Oversteer vibration",
		[ FFBModuleRegistry.SeatOfPantsVibrationType ] = "Seat-of-pants vibration",
		[ FFBModuleRegistry.ShiftRPMVibrationType ] = "Shift RPM vibration",
		[ FFBModuleRegistry.GearChangeVibrationType ] = "Gear change vibration",
		[ FFBModuleRegistry.ABSVibrationType ] = "ABS vibration",
		[ FFBModuleRegistry.EngineRPMVibrationType ] = "Engine RPM vibration",
		[ FFBModuleRegistry.SpeedGainType ] = "Speed gain",
		[ FFBModuleRegistry.RoadTextureType ] = "Road texture",
		[ FFBModuleRegistry.SlipTextureType ] = "Slip texture",
		[ FFBModuleRegistry.TorqueDitherType ] = "Torque dither"
	};

	/// <summary>Localized module display name (falls back to the English map, then a humanized key).</summary>
	public static string Module( string typeKey )
	{
		var fallback = ModuleNames.TryGetValue( typeKey, out var name ) ? name : Humanize( typeKey );

		return Localize( "FFBModule" + typeKey, fallback );
	}

	// Built-in per-module-type descriptions — the default node description every module carries (shown as the
	// node's tooltip, under the settings card, and beside the pinned quick controls). Shipped translated via the
	// FFBModuleDescription{TypeKey} localization keys; this map is the English fallback. A custom graph's node
	// can override its description (FFBModuleData.Description); built-in graphs always show these defaults.
	private static readonly Dictionary<string, string> ModuleDescriptions = new( StringComparer.Ordinal )
	{
		[ FFBModuleRegistry.Source60HzType ] = "iRacing's native 60 Hz force feedback signal, exactly as the simulator sends it.",
		[ FFBModuleRegistry.Source360HzType ] = "iRacing's high-rate force feedback signal — six samples per 60 Hz frame, with the most detail.",
		[ FFBModuleRegistry.SourceLFEType ] = "The simulator's low-frequency effects audio, turned into wheel force. Mix it in with an add module.",
		[ FFBModuleRegistry.SourceWheelVelocityType ] = "A force that opposes how fast the wheel is being turned — mix it in for damping.",
		[ FFBModuleRegistry.SourceFrictionType ] = "True dry friction: the wheel holds where you leave it, with constant sliding drag past the stick region.",
		[ FFBModuleRegistry.SourceSoftLockType ] = "Pushes back hard when the wheel is turned past the car's steering lock, like a physical end stop.",
		[ FFBModuleRegistry.SourceWheelCenteringType ] = "A spring force that pulls the wheel back toward center. Route it through a speed gain module to keep it parked-only.",
		[ FFBModuleRegistry.OutputType ] = "The end of the chain — converts the signal to wheel output, with an optional response curve and soft limiter.",
		[ FFBModuleRegistry.LowPassFilterType ] = "Keeps the slow body of the signal and smooths away everything faster than the cutoff.",
		[ FFBModuleRegistry.HighPassFilterType ] = "Removes the slow body of the signal and keeps only the fast detail above the cutoff.",
		[ FFBModuleRegistry.GainType ] = "Scales the signal by a fixed multiplier — use a negative gain to invert it.",
		[ FFBModuleRegistry.AddType ] = "Adds two signals together.",
		[ FFBModuleRegistry.SubtractType ] = "Subtracts input B from input A — for example, subtracting a low-pass copy of a signal leaves just the detail.",
		[ FFBModuleRegistry.BlendType ] = "Crossfades between two signals — 0% is all input A, 100% is all input B.",
		[ FFBModuleRegistry.SlewLimiterType ] = "Caps how fast the signal can change — anything under the limit passes untouched, faster movement is slowed down.",
		[ FFBModuleRegistry.SlewCompressorType ] = "Squeezes fast signal changes instead of hard-limiting them — slow movement passes untouched.",
		[ FFBModuleRegistry.CompressorType ] = "Squeezes the signal above a threshold, keeping peaks under control without hard clipping.",
		[ FFBModuleRegistry.TransientEnhancerType ] = "Extracts the fast detail and emphasizes attacks — bumps and curbs pop without boosting the decay.",
		[ FFBModuleRegistry.AdaptiveSmootherType ] = "One-knob smoother that adapts to the signal — strong smoothing when quiet, minimal lag on fast transients.",
		[ FFBModuleRegistry.InterpolatorType ] = "Removes the staircase steps from a 60 Hz signal by ramping smoothly across each frame, at the cost of one frame of latency.",
		[ FFBModuleRegistry.PredictionType ] = "Shifts a 360 Hz signal into the future to counteract latency — an adaptive filter that learns each car as you drive.",
		[ FFBModuleRegistry.Prediction60HzType ] = "Shifts the 60 Hz signal into the future to counteract latency — learns from your steering and the car's motion as you drive.",
		[ FFBModuleRegistry.AdaptiveBlendType ] = "Follows the detail of input A while being pulled toward input B — the pull relaxes at each peak so transients ride through.",
		[ FFBModuleRegistry.CrashProtectionType ] = "Fades the force way down when a crash is detected, then ramps back smoothly.",
		[ FFBModuleRegistry.CurbProtectionType ] = "Reduces force during hard curb strikes, then ramps back smoothly.",
		[ FFBModuleRegistry.UndersteerForceType ] = "A steering force driven by the understeer effect, so you can feel the front tires losing grip.",
		[ FFBModuleRegistry.OversteerForceType ] = "A steering force driven by the oversteer effect, so you can feel the rear of the car stepping out.",
		[ FFBModuleRegistry.SeatOfPantsForceType ] = "A steering force driven by the seat-of-pants effect — the feel of the chassis loading up.",
		[ FFBModuleRegistry.UndersteerVibrationType ] = "Vibrates the wheel when the front tires start to slide.",
		[ FFBModuleRegistry.OversteerVibrationType ] = "Vibrates the wheel when the rear tires start to slide.",
		[ FFBModuleRegistry.SeatOfPantsVibrationType ] = "Vibrates the wheel with the chassis motion — weight transfer you can feel.",
		[ FFBModuleRegistry.ShiftRPMVibrationType ] = "Buzzes the wheel in pulses when the engine reaches the shift point.",
		[ FFBModuleRegistry.GearChangeVibrationType ] = "A short vibration burst on every gear change.",
		[ FFBModuleRegistry.ABSVibrationType ] = "Pulses the wheel while the brakes' ABS is active.",
		[ FFBModuleRegistry.EngineRPMVibrationType ] = "A rumble that tracks the engine speed, voiced like a V8 — silent while the engine is off.",
		[ FFBModuleRegistry.SpeedGainType ] = "Scales the signal with car speed — fade forces in or out as the car speeds up.",
		[ FFBModuleRegistry.RoadTextureType ] = "A rumble that speeds up with the car, like road surface texture — silent when parked.",
		[ FFBModuleRegistry.SlipTextureType ] = "A rumble that appears whenever either end of the car is sliding, driven by the understeer and oversteer effects.",
		[ FFBModuleRegistry.TorqueDitherType ] = "Adds a tiny alternating dither below a threshold to keep the wheel mechanism live and reveal faint detail."
	};

	/// <summary>Localized built-in description for a module type (falls back to the English map, then empty).</summary>
	public static string ModuleDescription( string typeKey )
	{
		var fallback = ModuleDescriptions.TryGetValue( typeKey, out var description ) ? description : string.Empty;

		return Localize( "FFBModuleDescription" + typeKey, fallback );
	}

	/// <summary>Look up a localization key, falling back to <paramref name="fallback"/> when the key is absent
	/// (empty), or when the localization table is not yet available (e.g. during very early construction).</summary>
	public static string Localize( string localizationKey, string fallback )
	{
		if ( string.IsNullOrEmpty( localizationKey ) )
		{
			return fallback;
		}

		var localization = DataContext.DataContext.Instance?.Localization;

		if ( localization == null )
		{
			return fallback;
		}

		var localized = localization[ localizationKey ];

		// A missing key renders as "!Key!" (see Localization's indexer) — treat that as absent so the
		// fallback chain (e.g. FFBSetting{Key} -> {Key} -> humanized) can continue past unknown keys.
		return string.IsNullOrEmpty( localized ) || localized.StartsWith( '!' ) ? fallback : localized;
	}

	[GeneratedRegex( "(?<=[a-z0-9])(?=[A-Z])" )]
	private static partial Regex CamelBoundary();

	/// <summary>Insert spaces at camelCase boundaries so a stable key reads as a label ("TotalCompressionRate" -> "Total compression rate").</summary>
	public static string Humanize( string key )
	{
		if ( string.IsNullOrEmpty( key ) )
		{
			return key;
		}

		var spaced = CamelBoundary().Replace( key, " " );

		return string.Concat( char.ToUpperInvariant( spaced[ 0 ] ), spaced[ 1.. ].ToLowerInvariant() );
	}
}

/// <summary>View-model for one module setting (a knob, switch, or choice). Its Value setter is the single write
/// path: clamp, update the model, push into both engines, refresh the value string, sync the per-context store,
/// record + queue serialization, and refresh the preview.</summary>
public sealed class FFBModuleSettingViewModel : INotifyPropertyChanged
{
	private readonly FFBModuleViewModel _ownerModule;
	private readonly string _moduleId;
	private readonly FFBModuleData _model;
	private readonly FFBSettingDescriptor _descriptor;

	private float _value;
	private string _valueString = string.Empty;

	public FFBModuleSettingViewModel( FFBModuleViewModel ownerModule, FFBModuleData model, FFBSettingDescriptor descriptor )
	{
		_ownerModule = ownerModule;
		_moduleId = model.ModuleId;
		_model = model;
		_descriptor = descriptor;

		_value = model.SettingValues.TryGetValue( descriptor.Key, out var stored ) ? stored : descriptor.DefaultValue;
		_valueString = Format( _value );

		if ( descriptor.Type == FFBSettingType.Choice && ( descriptor.ChoiceLocalizationKeys != null ) )
		{
			for ( var i = 0; i < descriptor.ChoiceLocalizationKeys.Length; i++ )
			{
				ChoiceOptions.Add( new KeyValuePair<int, string>( i, FFBDisplayNames.Localize( descriptor.ChoiceLocalizationKeys[ i ], FFBDisplayNames.Humanize( descriptor.ChoiceLocalizationKeys[ i ] ) ) ) );
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>Raised after a new value has been committed through the <see cref="Value"/> write path, with
	/// (oldValue, newValue). Lets the owning module VM react to one setting to retune another (e.g. the filter
	/// slope switch rescaling the cutoff). Not raised by <see cref="Reload"/> — reloads restore stored state.</summary>
	public event Action<float, float>? ValueCommitted;

	private void OnPropertyChanged( [CallerMemberName] string? propertyName = null ) => PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );

	public FFBSettingType SettingType => _descriptor.Type;

	public string Key => _descriptor.Key;

	public bool BreakRow => _descriptor.BreakRow;

	// FFBSetting{Key} (new, precise) -> {Key} (reuse an existing translated key like Strength/Curve/Duration) -> humanized.
	public string Label => FFBDisplayNames.Localize( _descriptor.LocalizationKey, FFBDisplayNames.Localize( _descriptor.Key, FFBDisplayNames.Humanize( _descriptor.Key ) ) );
	public float Minimum => _descriptor.Min;
	public float Maximum => _descriptor.Max;
	public float ClickStepSize => _descriptor.ClickStepSize;
	public float DragStepSize => _descriptor.DragStepSize;
	public float EditUnitScale => _descriptor.EditUnitScale;
	public float? DefaultValue => _descriptor.DefaultValue;
	public bool ShowCurve => _descriptor.ShowCurve;

	public List<KeyValuePair<int, string>> ChoiceOptions { get; } = [];

	/// <summary>Pinned = surfaced in the quick-controls row above the editor block — the handful of settings the
	/// graph's author intends the typical user to play with. Stored in the graph itself (it rides export/import,
	/// and built-ins ship with a curated set). The same setting VM instance backs both the settings column and
	/// the quick-controls row, so the two stay in sync automatically.</summary>
	public bool IsPinned
	{
		get => _model.PinnedSettings.Contains( _descriptor.Key );

		set
		{
			if ( value == IsPinned )
			{
				return;
			}

			if ( value )
			{
				_model.PinnedSettings.Add( _descriptor.Key );
			}
			else
			{
				_model.PinnedSettings.Remove( _descriptor.Key );
			}

			App.Instance!.SettingsFile.QueueForSerialization = true;

			OnPropertyChanged();

			_ownerModule.Owner.RefreshPinnedSettings();
		}
	}

	/// <summary>The pin switches only show on custom graphs — built-in graphs ship with a curated pinned set
	/// (like their locked structure). Evaluated at bind time; the VM tree is rebuilt on every graph switch.</summary>
	public bool ShowPinSwitch => !_ownerModule.Owner.IsFFBGraphBuiltIn;

	// Knob-only (only the knob template binds these): the +/- input mappings for this module setting, stored on
	// Settings keyed "{ModuleId}/{SettingKey}/Plus|Minus" so they survive VM rebuilds but never ride graph
	// export/import. Created lazily the first time a module card's knob binds them.
	public Classes.ButtonMappings PlusButtonMappings => DataContext.DataContext.Instance.Settings.GetFFBModuleButtonMappings( $"{_moduleId}/{_descriptor.Key}/Plus" );
	public Classes.ButtonMappings MinusButtonMappings => DataContext.DataContext.Instance.Settings.GetFFBModuleButtonMappings( $"{_moduleId}/{_descriptor.Key}/Minus" );

	public float Value
	{
		get => _value;

		set
		{
			var clamped = _descriptor.Clamp( value );

			if ( clamped == _value )
			{
				return;
			}

			var previousValue = _value;

			_value = clamped;
			_model.SettingValues[ _descriptor.Key ] = clamped;

			var app = App.Instance!;

			app.RacingWheel.SetEngineValue( _moduleId, _descriptor.Key, clamped );

			ValueString = Format( clamped );

			DataContext.DataContext.Instance.Settings.SyncFFBGraphModuleValues( true );

			app.SettingsFile.RecordChangedSetting( $"ffb:{_moduleId}/{_descriptor.Key}", $"[Settings] Updating FFB module setting {_moduleId}/{_descriptor.Key} to {clamped}" );
			app.SettingsFile.QueueForSerialization = true;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			OnPropertyChanged();
			OnPropertyChanged( nameof( IsOn ) );
			OnPropertyChanged( nameof( ChoiceIndex ) );

			ValueCommitted?.Invoke( previousValue, clamped );
		}
	}

	public string ValueString
	{
		get => _valueString;

		private set
		{
			if ( value != _valueString )
			{
				_valueString = value;

				OnPropertyChanged();
			}
		}
	}

	/// <summary>Switch view over <see cref="Value"/> (0/1).</summary>
	public bool IsOn
	{
		get => _value != 0f;
		set => Value = value ? 1f : 0f;
	}

	/// <summary>Choice view over <see cref="Value"/> (the option index).</summary>
	public int ChoiceIndex
	{
		get => (int) _value;
		set => Value = value;
	}

	private string Format( float value )
	{
		if ( _descriptor.FormatValue == null )
		{
			return value.ToString( "0.####" );
		}

		return _descriptor.FormatValue( new FFBFormatContext( value, ( key, fallback ) => _model.SettingValues.TryGetValue( key, out var stored ) ? stored : fallback ) );
	}

	/// <summary>Recompute the displayed value string in place (the value is unchanged). Used when a WheelForce/MaxForce change moved a scaled display.</summary>
	public void RefreshValueString() => ValueString = Format( _value );

	/// <summary>Refresh the displayed value string (e.g. after a per-context reload changed the model value).</summary>
	public void Reload()
	{
		_value = _model.SettingValues.TryGetValue( _descriptor.Key, out var stored ) ? stored : _descriptor.DefaultValue;

		ValueString = Format( _value );

		OnPropertyChanged( nameof( Value ) );
		OnPropertyChanged( nameof( IsOn ) );
		OnPropertyChanged( nameof( ChoiceIndex ) );
	}
}

/// <summary>View-model for one module card: display name, enable toggle, input routing combos, and setting VMs.</summary>
public sealed class FFBModuleViewModel : INotifyPropertyChanged
{
	private readonly FFBGraphViewModel _owner;
	private readonly FFBModuleData _model;
	private readonly FFBModuleDescriptor _descriptor;

	private string _displayName = string.Empty;
	private List<KeyValuePair<string, string>> _eligibleInputs = [];

	public FFBModuleViewModel( FFBGraphViewModel owner, FFBModuleData model, FFBModuleDescriptor descriptor )
	{
		_owner = owner;
		_model = model;
		_descriptor = descriptor;

		foreach ( var settingDescriptor in descriptor.Settings )
		{
			Settings.Add( new FFBModuleSettingViewModel( this, model, settingDescriptor ) );
		}

		// presentation copy of the settings for the card's fixed-column grid: a BreakRow setting is pushed to
		// the start of the next row by padding the current row with invisible spacers
		foreach ( var setting in Settings )
		{
			if ( setting.BreakRow )
			{
				while ( SettingsPresentation.Count % SettingsPanelColumns != 0 )
				{
					SettingsPresentation.Add( FFBSettingSpacer.Instance );
				}
			}

			SettingsPresentation.Add( setting );

			// the node's second line mirrors the settings live — any setting edit refreshes the digest
			setting.PropertyChanged += ( sender, e ) => OnPropertyChanged( nameof( SettingsSummary ) );
		}

		if ( ( model.ModuleType == FFBModuleRegistry.LowPassFilterType ) || ( model.ModuleType == FFBModuleRegistry.HighPassFilterType ) )
		{
			var slopeSetting = Settings.FirstOrDefault( setting => setting.Key == "Slope" );
			var cutoffSetting = Settings.FirstOrDefault( setting => setting.Key == "Cutoff" );

			if ( ( slopeSetting != null ) && ( cutoffSetting != null ) )
			{
				// Retune the cutoff when the slope changes so the filter keeps doing the same job. A two-pole
				// Butterworth needs its cutoff √2 higher than a one-pole to match it — that factor equalizes
				// both the noise bandwidth (π/2·f vs π/(2√2)·f, i.e. the same total smoothing) and the
				// low-frequency group delay (1/(2πf) vs √2/(2πf), i.e. the same felt lag) — the two-pole then
				// wins on steeper rolloff above the cutoff. The same factor applies to the high-pass, since it
				// is the exact complement of the same low-pass core.
				slopeSetting.ValueCommitted += ( oldValue, newValue ) =>
				{
					var wasTwoPole = (int) oldValue == Modules.LowPassCore.SlopeTwoPole;
					var isTwoPole = (int) newValue == Modules.LowPassCore.SlopeTwoPole;

					if ( wasTwoPole != isTwoPole )
					{
						var retunedCutoff = isTwoPole ? cutoffSetting.Value * MathF.Sqrt( 2f ) : cutoffSetting.Value / MathF.Sqrt( 2f );

						cutoffSetting.Value = MathF.Round( retunedCutoff, 1 );
					}
				};
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged( [CallerMemberName] string? propertyName = null ) => PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );

	public string ModuleId => _model.ModuleId;
	public string ModuleType => _model.ModuleType;

	public FFBGraphViewModel Owner => _owner;

	public FFBModuleDescriptor Descriptor => _descriptor;

	// only the Output module is fixed now — sources are added and removed at will (one instance of each source
	// type per graph; removing the last source re-adds the 360 Hz source since Output always needs an input).
	// Modules of a built-in graph cannot be removed (structure is locked; settings stay editable) — the VM tree
	// is rebuilt wholesale on every graph switch, so no change notification is needed for that.
	public bool IsFixed => _descriptor.IsOutput;
	public bool IsGenerator => _descriptor.IsGenerator;
	public bool IsOutput => _descriptor.IsOutput;
	public bool IsSource => _descriptor.IsSource;
	public bool CanToggleEnabled => !IsFixed;
	public bool CanRemove => !IsFixed && !_owner.IsFFBGraphBuiltIn;
	public bool CanTest => _descriptor.CanTest;

	// Vibration generators render as standalone nodes: no inputs (SignalInputCount is 0) and no output connector
	// either — they only feed the vibration bus, never another module's signal input.
	public bool HasOutputConnector => !IsOutput && !IsGenerator;

	/// <summary>
	/// True for an understeer / oversteer / seat-of-pants module whose effect is switched off on the steering
	/// effects page — and for the slip texture module when BOTH understeer and oversteer are off (its amplitude
	/// is their sum, so it is silent with both gone). The engine feeds those modules a zero effect value while
	/// the switch is off (force modules pass through, vibration generators go silent), the node renders dimmed
	/// like a disabled module, and the settings card shows a notice. Re-raised via
	/// <see cref="NotifySteeringEffectDisabledChanged"/> when one of the switches flips.
	/// </summary>
	public bool SteeringEffectDisabled
	{
		get
		{
			var settings = DataContext.DataContext.Instance?.Settings;

			if ( settings == null )
			{
				return false;
			}

			return ModuleType switch
			{
				FFBModuleRegistry.UndersteerForceType or FFBModuleRegistry.UndersteerVibrationType => !settings.SteeringEffectsUndersteerEnabled,
				FFBModuleRegistry.OversteerForceType or FFBModuleRegistry.OversteerVibrationType => !settings.SteeringEffectsOversteerEnabled,
				FFBModuleRegistry.SeatOfPantsForceType or FFBModuleRegistry.SeatOfPantsVibrationType => !settings.SteeringEffectsSeatOfPantsEnabled,
				FFBModuleRegistry.SlipTextureType => !settings.SteeringEffectsUndersteerEnabled && !settings.SteeringEffectsOversteerEnabled,
				_ => false
			};
		}
	}

	/// <summary>The settings-card notice shown next to the module name while <see cref="SteeringEffectDisabled"/>.
	/// The slip texture module gets its own wording since it isn't a single effect's module — it goes silent
	/// because both of the effects feeding it are disabled.</summary>
	public string SteeringEffectDisabledNotice => ModuleType == FFBModuleRegistry.SlipTextureType
		? FFBDisplayNames.Localize( "UndersteerOversteerDisabledNotice", "Understeer and oversteer are disabled on the steering effects page" )
		: FFBDisplayNames.Localize( "SteeringEffectDisabledNotice", "This effect is disabled on the steering effects page" );

	public void NotifySteeringEffectDisabledChanged() => OnPropertyChanged( nameof( SteeringEffectDisabled ) );

	private bool _isTestActive = false;

	/// <summary>Session-only "test this effect" toggle — forces the module's trigger active in both engines so
	/// the user can feel/see the effect on demand. Deliberately NOT serialized and NOT synced per-context; it
	/// resets whenever the module VMs are rebuilt (structure edit, graph switch) and on app exit.</summary>
	public bool IsTestActive
	{
		get => _isTestActive;

		set
		{
			if ( value != _isTestActive )
			{
				_isTestActive = value;

				var app = App.Instance;

				if ( app != null )
				{
					app.RacingWheel.SetEngineTestActive( _model.ModuleId, value );

					app.RacingWheel.UpdateAlgorithmPreview = true;
				}

				OnPropertyChanged();
			}
		}
	}

	/// <summary>Generator (vibration) tests shake the physical wheel, and the wheel's FFB fades out while off
	/// track — so their test buttons are disabled until the player is on track (pushed from RacingWheel's UI
	/// update via <see cref="FFBGraphViewModel.NotifyIsOnTrackChanged"/>). Event-driven effect tests (speed
	/// gain / crash / curb protection) render through the preview and stay enabled everywhere.</summary>
	public bool TestDisabled => IsGenerator && !_owner.IsOnTrack;

	public void NotifyTestDisabledChanged() => OnPropertyChanged( nameof( TestDisabled ) );

	public int SignalInputCount => _descriptor.SignalInputCount;
	public bool ShowInputA => _descriptor.SignalInputCount >= 1;
	public bool ShowInputB => _descriptor.SignalInputCount >= 2;

	// column count for the multi-column settings-card presentation (the BreakRow spacer math below pads to
	// this). RacingWheelPage.xaml currently stacks the settings vertically and binds Settings directly, so
	// SettingsPresentation is unused there — kept in case the layout returns to a grid.
	public const int SettingsPanelColumns = 3;

	public ObservableCollection<FFBModuleSettingViewModel> Settings { get; } = [];

	/// <summary>The multi-column presentation of the settings: <see cref="Settings"/> plus invisible spacers
	/// padding out the row before each BreakRow setting, so related settings can be grouped onto their own
	/// row. Unused while the settings panel stacks vertically.</summary>
	public ObservableCollection<object> SettingsPresentation { get; } = [];

	public string DisplayName
	{
		get => _displayName;

		set
		{
			if ( value != _displayName )
			{
				_displayName = value;

				OnPropertyChanged();
			}
		}
	}

	/// <summary>One-line settings digest for the node's second line — every setting's display value in declaration
	/// order, comma-separated ("1.50x" / "one pole, 12.5 Hz"). Knobs reuse the same formatted string the settings
	/// card shows, choices show the selected option's label, and switches contribute their label only while ON.
	/// Empty for settings-less modules (the node template collapses the line).</summary>
	public string SettingsSummary
	{
		get
		{
			var parts = new List<string>();

			foreach ( var setting in Settings )
			{
				switch ( setting.SettingType )
				{
					case FFBSettingType.Knob:
						// the *Units localization keys carry one leading space (" Hz") for appending — trim it here
						parts.Add( setting.ValueString.Trim() );
						break;

					case FFBSettingType.Choice:
						if ( ( setting.ChoiceIndex >= 0 ) && ( setting.ChoiceIndex < setting.ChoiceOptions.Count ) )
						{
							parts.Add( setting.ChoiceOptions[ setting.ChoiceIndex ].Value );
						}
						break;

					case FFBSettingType.Switch:
						if ( setting.IsOn )
						{
							parts.Add( setting.Label );
						}
						break;
				}
			}

			return string.Join( ", ", parts );
		}
	}

	/// <summary>
	/// The node's description — shown as the node's tooltip on the graph canvas, under the settings on the
	/// selected module's card, and beside the pinned quick controls. Every module type ships a localized
	/// built-in default; a node in a CUSTOM graph can override it (stored in the graph, rides export/import,
	/// edited via the pencil button which opens the description editor window). Clearing the override reverts
	/// to the built-in default. Nodes of built-in graphs always show the localized default.
	/// </summary>
	public string NodeDescription
	{
		get
		{
			if ( _model.Description == string.Empty )
			{
				return FFBDisplayNames.ModuleDescription( _model.ModuleType );
			}

			// built-in graphs ship their node-description overrides translated (keys maintained by the
			// update-builtin-graphs skill); the text stored in the graph file is the English fallback
			return _owner.IsFFBGraphBuiltIn
				? FFBDisplayNames.Localize( FFBGraphViewModel.NodeDescriptionLocalizationKey( _owner.GraphName, _model.ModuleId ), _model.Description )
				: _model.Description;
		}

		set
		{
			if ( _owner.IsFFBGraphBuiltIn || ( value == _model.Description ) )
			{
				return;
			}

			_model.Description = value;

			App.Instance!.SettingsFile.QueueForSerialization = true;

			OnPropertyChanged();
		}
	}

	/// <summary>Node descriptions are editable on custom graphs only — built-in graphs always show the localized
	/// per-module-type default (same rule as the graph description itself).</summary>
	public bool CanEditNodeDescription => !_owner.IsFFBGraphBuiltIn;

	private bool _isSelected = false;

	/// <summary>Whether this module is the one selected in the node editor (drives the node highlight and which
	/// module the settings panel and preview show). Session-only — set via <see cref="FFBGraphViewModel.SelectedModule"/>.</summary>
	public bool IsSelected
	{
		get => _isSelected;

		set
		{
			if ( value != _isSelected )
			{
				_isSelected = value;

				OnPropertyChanged();
			}
		}
	}

	private bool _isPreviewLocked = false;

	/// <summary>Whether the preview graph is locked to this module (drives the node's preview highlight). Follows
	/// the selection on left-click; a right-click locks it to a different node while the selection — and the
	/// settings panel — stay put. Session-only — set via <see cref="FFBGraphViewModel.PreviewModule"/>.</summary>
	public bool IsPreviewLocked
	{
		get => _isPreviewLocked;

		set
		{
			if ( value != _isPreviewLocked )
			{
				_isPreviewLocked = value;

				OnPropertyChanged();
			}
		}
	}

	/// <summary>Node editor canvas position. Written during a drag — display-only, so no engine rebuild here; the
	/// editor queues one serialization when the drag ends.</summary>
	public double NodeX
	{
		get => _model.NodeX;

		set
		{
			if ( (float) value != _model.NodeX )
			{
				_model.NodeX = (float) value;

				OnPropertyChanged();
			}
		}
	}

	public double NodeY
	{
		get => _model.NodeY;

		set
		{
			if ( (float) value != _model.NodeY )
			{
				_model.NodeY = (float) value;

				OnPropertyChanged();
			}
		}
	}

	public List<KeyValuePair<string, string>> EligibleInputs
	{
		get => _eligibleInputs;

		private set
		{
			_eligibleInputs = value;

			OnPropertyChanged();
		}
	}

	public void SetEligibleInputs( List<KeyValuePair<string, string>> eligibleInputs )
	{
		EligibleInputs = eligibleInputs;

		// re-notify the selection so the combo re-resolves against the new item list
		OnPropertyChanged( nameof( InputASelectedId ) );
		OnPropertyChanged( nameof( InputBSelectedId ) );
	}

	public bool Enabled
	{
		get => !_model.SettingValues.TryGetValue( "Enabled", out var value ) || ( value != 0f );

		set
		{
			var newValue = value ? 1f : 0f;

			if ( newValue == ( Enabled ? 1f : 0f ) )
			{
				return;
			}

			_model.SettingValues[ "Enabled" ] = newValue;

			var app = App.Instance!;

			app.RacingWheel.SetEngineValue( _model.ModuleId, "Enabled", newValue );

			DataContext.DataContext.Instance.Settings.SyncFFBGraphModuleValues( true );

			app.SettingsFile.QueueForSerialization = true;
			app.RacingWheel.UpdateAlgorithmPreview = true;

			OnPropertyChanged();
		}
	}

	public string InputASelectedId
	{
		get => _model.InputAModuleId;
		set => SetInput( isInputB: false, value );
	}

	public string InputBSelectedId
	{
		get => _model.InputBModuleId;
		set => SetInput( isInputB: true, value );
	}

	private void SetInput( bool isInputB, string value )
	{
		// built-in graphs are structurally locked — wiring cannot change (the editor blocks wire drags too;
		// this guard covers any other caller)
		if ( _owner.IsFFBGraphBuiltIn )
		{
			return;
		}

		if ( string.IsNullOrEmpty( value ) )
		{
			return;
		}

		if ( isInputB )
		{
			if ( value == _model.InputBModuleId )
			{
				return;
			}

			_model.InputBModuleId = value;
		}
		else
		{
			if ( value == _model.InputAModuleId )
			{
				return;
			}

			_model.InputAModuleId = value;
		}

		OnPropertyChanged( isInputB ? nameof( InputBSelectedId ) : nameof( InputASelectedId ) );

		// routing is structure: re-derive the evaluation order, rebuild the live engine, and refresh the preview.
		// The card rebuild is deferred so the combo box driving this setter finishes its selection-changed first.
		_owner.CommitWiringChange();
	}
}

/// <summary>One display-only wire on the node canvas: the source module's output feeding one of the target
/// module's inputs. Rebuilt wholesale whenever the card tree rebuilds — re-wiring happens via the input combos.</summary>
public sealed class FFBNodeWireViewModel( string sourceModuleId, string targetModuleId, bool isInputB )
{
	public string SourceModuleId { get; } = sourceModuleId;
	public string TargetModuleId { get; } = targetModuleId;
	public bool IsInputB { get; } = isInputB;
}

/// <summary>The graph editor's root VM: the module cards for the currently selected FFB graph (the vibration
/// generator modules included — they render as standalone nodes), plus structure edits.</summary>
public sealed class FFBGraphViewModel : INotifyPropertyChanged
{
	private FFBGraph? _graph;

	private DataContext.Settings? _subscribedSettings = null;

	private string? _selectedModuleId = null;
	private FFBModuleViewModel? _selectedModule = null;

	private string? _previewModuleId = null;
	private FFBModuleViewModel? _previewModule = null;
	private bool _rebuildQueued = false;

	/// <summary>Every module of the selected graph — the nodes shown on the graph canvas.</summary>
	public ObservableCollection<FFBModuleViewModel> Modules { get; } = [];

	/// <summary>Display-only wires between the graph canvas nodes.</summary>
	public ObservableCollection<FFBNodeWireViewModel> Wires { get; } = [];

	/// <summary>The selected graph's pinned settings — its quick controls, rendered above the editor block in
	/// module order. Each entry pairs the SHARED setting VM with its owning module VM (for the caption — labels
	/// like "Strength" repeat across modules). Rebuilt on graph switch and on every pin toggle.</summary>
	public ObservableCollection<FFBPinnedGroupViewModel> PinnedGroups { get; } = [];

	public bool HasPinnedSettings => PinnedGroups.Count > 0;

	public void RefreshPinnedSettings()
	{
		PinnedGroups.Clear();

		foreach ( var module in Modules )
		{
			FFBPinnedGroupViewModel? group = null;

			foreach ( var setting in module.Settings )
			{
				if ( setting.IsPinned )
				{
					group ??= new FFBPinnedGroupViewModel( module );

					group.Settings.Add( setting );
				}
			}

			if ( group != null )
			{
				PinnedGroups.Add( group );
			}
		}

		OnPropertyChanged( nameof( HasPinnedSettings ) );
	}

	// Built on access (not in the constructor) so it is not evaluated during DataContext static construction,
	// before Localization exists, and so its module names re-localize on a language switch.
	public List<KeyValuePair<string, string>> AddableModuleTypes => BuildAddableModuleTypes();

	// Built-in graphs are structurally locked: no adding/removing modules, no rewiring, no moving nodes — only
	// their module settings may be changed. Users clone a built-in into a custom graph to alter its structure.
	public bool IsFFBGraphBuiltIn => _graph?.IsBuiltIn == true;

	// The description text box is only editable on custom graphs (built-ins show localized read-only text).
	public bool IsFFBGraphCustom => ( _graph != null ) && !_graph.IsBuiltIn;

	/// <summary>
	/// The selected graph's description, shown below the graph selector. Custom graphs edit it freely (stored in
	/// the graph, rides export/import; the text box commits on focus loss). Built-in graphs resolve a
	/// localization key derived from the graph name first — so shipped descriptions arrive translated — and fall
	/// back to the text stored in the shipped file.
	/// </summary>
	public string GraphDescription
	{
		get
		{
			if ( _graph == null )
			{
				return string.Empty;
			}

			return _graph.IsBuiltIn ? FFBDisplayNames.Localize( DescriptionLocalizationKey( _graph.Name ), _graph.Description ) : _graph.Description;
		}

		set
		{
			if ( ( _graph == null ) || _graph.IsBuiltIn || ( value == _graph.Description ) )
			{
				return;
			}

			_graph.Description = value;

			App.Instance!.SettingsFile.QueueForSerialization = true;

			OnPropertyChanged();
		}
	}

	/// <summary>"Marvin's easy detail adjustment" -> "FFBGraphDescriptionMarvinseasydetailadjustment" — the
	/// non-alphanumerics are dropped, casing kept, so each built-in graph maps to a stable resource key.</summary>
	public static string DescriptionLocalizationKey( string graphName )
	{
		return "FFBGraphDescription" + string.Concat( graphName.Where( char.IsLetterOrDigit ) );
	}

	/// <summary>"Marvin's native 60 Hz" + "Source360" -> "FFBNodeDescriptionMarvinsnative60HzSource360" — the
	/// per-node description override keys for BUILT-IN graphs (same alnum-only derivation as the graph
	/// description key; module ids are canonical source/output names or GUIDs, both stable across the clone →
	/// save-over-the-shipped-file update workflow). Maintained by the update-builtin-graphs skill.</summary>
	public static string NodeDescriptionLocalizationKey( string graphName, string moduleId )
	{
		return "FFBNodeDescription" + string.Concat( graphName.Where( char.IsLetterOrDigit ) ) + string.Concat( moduleId.Where( char.IsLetterOrDigit ) );
	}

	/// <summary>The selected graph's name (empty when no graph is selected) — the module VMs use it to derive
	/// their built-in node-description localization keys.</summary>
	public string GraphName => _graph?.Name ?? string.Empty;

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged( [CallerMemberName] string? propertyName = null ) => PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );

	/// <summary>
	/// The module selected on the node canvas — the settings panel shows its card. Selecting a module also locks
	/// the preview to it (see <see cref="PreviewModule"/>). Session-only (not persisted); falls back to the Output
	/// module whenever the stored selection disappears (graph switch, context reload, module removal). Selection is
	/// presentation state, so setting it never rebuilds an engine — it only refreshes the preview.
	/// </summary>
	public FFBModuleViewModel? SelectedModule
	{
		get => _selectedModule;

		set
		{
			if ( value == _selectedModule )
			{
				// left-clicking the already-selected module still re-locks the preview to it
				PreviewModule = value;

				return;
			}

			_selectedModule = value;
			_selectedModuleId = value?.ModuleId;

			foreach ( var module in Modules )
			{
				module.IsSelected = module == value;
			}

			OnPropertyChanged();

			PreviewModule = value;
		}
	}

	/// <summary>
	/// The module whose signals the preview graph taps. Normally follows <see cref="SelectedModule"/> (left-click),
	/// but right-clicking a node locks the preview to that node alone — so one module's settings can be adjusted
	/// while watching another module's input/output. Session-only; a structure rebuild restores it by id, falling
	/// back to the selected module when the previewed module no longer exists.
	/// </summary>
	public FFBModuleViewModel? PreviewModule
	{
		get => _previewModule;

		set
		{
			if ( value == _previewModule )
			{
				return;
			}

			_previewModule = value;
			_previewModuleId = value?.ModuleId;

			foreach ( var module in Modules )
			{
				module.IsPreviewLocked = module == value;
			}

			OnPropertyChanged();

			var app = App.Instance;

			if ( app != null )
			{
				app.RacingWheel.UpdateAlgorithmPreview = true;
			}
		}
	}

	// the add-module picker (AddFFBModuleWindow) groups the module types by category (a header row's Key
	// starts with an underscore, which the picker's item style renders as a disabled accent-colored header —
	// an underscore key can never match a real module type). The vibration generators (both the steering and
	// the other kind) share one "Vibration effects" group at the end of the list.
	private static readonly ( FFBModuleCategory[] categories, string localizationKey, string fallback )[] AddableCategories =
	[
		( [ FFBModuleCategory.Source ], "FFBCategorySources", "Sources" ),
		( [ FFBModuleCategory.GenericDSP ], "FFBCategoryGenericDSP", "Generic DSP" ),
		( [ FFBModuleCategory.Mixer ], "FFBCategoryMixers", "Mixers" ),
		( [ FFBModuleCategory.Effect ], "FFBCategoryEffects", "Effects" ),
		( [ FFBModuleCategory.SteeringVibration, FFBModuleCategory.OtherVibration ], "VibrationEffects", "Vibration effects" )
	];

	private List<KeyValuePair<string, string>> BuildAddableModuleTypes()
	{
		var addable = new List<( FFBModuleCategory category, string typeKey, string displayName )>();

		foreach ( var descriptor in FFBModuleRegistry.All )
		{
			// only the Output module is fixed and cannot be added by the user
			if ( descriptor.IsOutput )
			{
				continue;
			}

			// sources are one-per-graph — a source type already on the graph is not offered again
			if ( descriptor.IsSource && ( _graph?.Modules.Any( module => module.ModuleType == descriptor.TypeKey ) == true ) )
			{
				continue;
			}

			addable.Add( ( descriptor.Category, descriptor.TypeKey, FFBDisplayNames.Module( descriptor.TypeKey ) ) );
		}

		var list = new List<KeyValuePair<string, string>>();

		foreach ( var (categories, localizationKey, fallback) in AddableCategories )
		{
			// within a category the registry's hand-curated declaration order is kept (not alphabetical)
			var categoryEntries = addable.Where( entry => categories.Contains( entry.category ) ).ToList();

			if ( categoryEntries.Count == 0 )
			{
				continue;   // e.g. every source type is already on the graph
			}

			list.Add( new KeyValuePair<string, string>( "_" + localizationKey, FFBDisplayNames.Localize( localizationKey, fallback ) ) );

			foreach ( var entry in categoryEntries )
			{
				list.Add( new KeyValuePair<string, string>( entry.typeKey, entry.displayName ) );
			}
		}

		return list;
	}

	/// <summary>Rebuild the whole card tree from the currently selected FFB graph. UI thread only.</summary>
	public void RebuildFromCurrentSelection()
	{
		Modules.Clear();
		Wires.Clear();

		var settings = DataContext.DataContext.Instance.Settings;

		EnsureSettingsSubscription( settings );

		settings.RacingWheelFFBGraphs.TryGetValue( settings.RacingWheelSelectedFFBGraphName, out var graph );

		_graph = graph;

		if ( graph == null )
		{
			SelectedModule = null;

			RefreshPinnedSettings();

			OnPropertyChanged( nameof( GraphDescription ) );
			OnPropertyChanged( nameof( IsFFBGraphCustom ) );

			return;
		}

		// lay the nodes out automatically the first time this graph is shown in the node editor (legacy data,
		// freshly migrated built-ins, and reset built-ins all arrive with every position at 0,0)
		if ( FFBGraphTopology.NeedsAutoLayout( graph ) )
		{
			FFBGraphTopology.ApplyAutoLayout( graph );

			App.Instance!.SettingsFile.QueueForSerialization = true;
		}

		var moduleViewModels = new List<FFBModuleViewModel>();

		foreach ( var module in graph.Modules )
		{
			var descriptor = FFBModuleRegistry.TryGet( module.ModuleType );

			if ( descriptor != null )
			{
				moduleViewModels.Add( new FFBModuleViewModel( this, module, descriptor ) );
			}
		}

		// display names have no list-index prefix (the evaluation order is derived automatically now); duplicate
		// module types are disambiguated as "Gain", "Gain (2)", ...
		var nameCounts = new Dictionary<string, int>( StringComparer.Ordinal );

		foreach ( var moduleViewModel in moduleViewModels )
		{
			var name = FFBDisplayNames.Module( moduleViewModel.ModuleType );

			var count = nameCounts.TryGetValue( name, out var seen ) ? seen + 1 : 1;

			nameCounts[ name ] = count;

			moduleViewModel.DisplayName = count > 1 ? $"{name} ({count})" : name;
		}

		// eligible inputs: any non-Output, non-generator module that is not downstream of the consumer (no cycles)
		foreach ( var moduleViewModel in moduleViewModels )
		{
			if ( moduleViewModel.SignalInputCount < 1 )
			{
				continue;
			}

			var downstream = FFBGraphTopology.ReachableFrom( graph, moduleViewModel.ModuleId );

			var eligible = new List<KeyValuePair<string, string>>();

			foreach ( var candidate in moduleViewModels )
			{
				if ( candidate.IsOutput || candidate.IsGenerator || downstream.Contains( candidate.ModuleId ) )
				{
					continue;
				}

				eligible.Add( new KeyValuePair<string, string>( candidate.ModuleId, candidate.DisplayName ) );
			}

			moduleViewModel.SetEligibleInputs( eligible );
		}

		foreach ( var moduleViewModel in moduleViewModels )
		{
			Modules.Add( moduleViewModel );
		}

		// one display wire per visible input of each canvas node (generators have none)
		foreach ( var moduleViewModel in Modules )
		{
			var module = graph.Modules.Find( m => m.ModuleId == moduleViewModel.ModuleId );

			if ( module == null )
			{
				continue;
			}

			if ( moduleViewModel.ShowInputA )
			{
				Wires.Add( new FFBNodeWireViewModel( module.InputAModuleId, module.ModuleId, isInputB: false ) );
			}

			if ( moduleViewModel.ShowInputB )
			{
				Wires.Add( new FFBNodeWireViewModel( module.InputBModuleId, module.ModuleId, isInputB: true ) );
			}
		}

		// restore the selection by id (the VM instances were just recreated), falling back to the Output module
		var restored = moduleViewModels.Find( moduleViewModel => moduleViewModel.ModuleId == _selectedModuleId )
			?? moduleViewModels.Find( moduleViewModel => moduleViewModel.IsOutput );

		var restoredPreviewId = _previewModuleId;   // captured before the selection restore re-locks the preview

		_selectedModule = null;   // force the setter to fire even when the id did not change
		_previewModule = null;

		SelectedModule = restored;

		// restore a diverged preview lock by id; when its module is gone the preview stays with the selection
		var restoredPreview = moduleViewModels.Find( moduleViewModel => moduleViewModel.ModuleId == restoredPreviewId );

		if ( restoredPreview != null )
		{
			PreviewModule = restoredPreview;
		}

		// the addable-types list depends on which source types are present, so it re-queries with the graph
		OnPropertyChanged( nameof( AddableModuleTypes ) );

		// the structure lock follows whichever graph is now selected
		OnPropertyChanged( nameof( IsFFBGraphBuiltIn ) );

		// the quick-controls row and description follow the graph too
		RefreshPinnedSettings();

		OnPropertyChanged( nameof( GraphDescription ) );
		OnPropertyChanged( nameof( IsFFBGraphCustom ) );
	}

	// The steering effects page's per-effect enable switches gate the understeer/oversteer/seat-of-pants
	// modules, so their node dimming + settings-card notice must follow the switches live. Subscribed lazily
	// from RebuildFromCurrentSelection (NOT the constructor — this VM is built during DataContext's static
	// construction, before DataContext.Instance exists) and re-attached if the Settings instance is swapped
	// by a settings-file load.
	private void EnsureSettingsSubscription( DataContext.Settings settings )
	{
		if ( ReferenceEquals( _subscribedSettings, settings ) )
		{
			return;
		}

		if ( _subscribedSettings != null )
		{
			_subscribedSettings.PropertyChanged -= Settings_PropertyChanged;
		}

		settings.PropertyChanged += Settings_PropertyChanged;

		_subscribedSettings = settings;
	}

	private void Settings_PropertyChanged( object? sender, PropertyChangedEventArgs e )
	{
		if ( e.PropertyName is not ( nameof( DataContext.Settings.SteeringEffectsUndersteerEnabled )
			or nameof( DataContext.Settings.SteeringEffectsOversteerEnabled )
			or nameof( DataContext.Settings.SteeringEffectsSeatOfPantsEnabled ) ) )
		{
			return;
		}

		// per-context reloads can flip these switches from a simulator thread — the module VMs are UI-bound
		var dispatcher = System.Windows.Application.Current?.Dispatcher;

		if ( ( dispatcher != null ) && !dispatcher.CheckAccess() )
		{
			dispatcher.InvokeAsync( () => Settings_PropertyChanged( sender, e ) );

			return;
		}

		foreach ( var module in Modules )
		{
			module.NotifySteeringEffectDisabledChanged();
		}

		// the gate also silences the modules in the preview replay
		var app = App.Instance;

		if ( app != null )
		{
			app.RacingWheel.UpdateAlgorithmPreview = true;
		}
	}

	/// <summary>Recompute every knob's value string. WheelForce/MaxForce-scaled displays (strengths, output min/max, compression thresholds) depend on live force settings, so this is called when those change. UI thread only.</summary>
	public void RefreshValueStrings()
	{
		foreach ( var module in Modules )
		{
			foreach ( var setting in module.Settings )
			{
				setting.RefreshValueString();
			}
		}
	}

	private bool _isOnTrack;

	/// <summary>Last on-track state pushed by RacingWheel's UI update — gates the generator test buttons (see
	/// <see cref="FFBModuleViewModel.TestDisabled"/>).</summary>
	public bool IsOnTrack => _isOnTrack;

	/// <summary>Called from RacingWheel's UI update whenever it refreshes the page. On an on-track transition
	/// the generator test buttons flip enabled/disabled, and leaving the track releases any running generator
	/// test — the faded wheel goes silent anyway, and the vibration must not come back unannounced the next
	/// time the player gets in the car. UI thread only.</summary>
	public void NotifyIsOnTrackChanged( bool isOnTrack )
	{
		if ( isOnTrack == _isOnTrack )
		{
			return;
		}

		_isOnTrack = isOnTrack;

		foreach ( var module in Modules )
		{
			if ( module.IsGenerator )
			{
				if ( !isOnTrack )
				{
					module.IsTestActive = false;
				}

				module.NotifyTestDisabledChanged();
			}
		}
	}

	// gear-change test auto-release (see ToggleTestActive) — created lazily, lives for the app's lifetime
	private System.Windows.Threading.DispatcherTimer? _gearChangeTestReleaseTimer;

	/// <summary>Toggle the session-only test override for EVERY module of the clicked module's type — a test
	/// exercises the effect, and the effect may be split across duplicates (e.g. two crash protection modules
	/// on different branches), so they test as one. All their buttons highlight together.</summary>
	public void ToggleTestActive( FFBModuleViewModel moduleViewModel )
	{
		var newState = !moduleViewModel.IsTestActive;

		foreach ( var module in Modules )
		{
			if ( module.ModuleType == moduleViewModel.ModuleType )
			{
				module.IsTestActive = newState;
			}
		}

		// the gear-change test is a one-shot: the engine plays its burst on the toggle's rising edge and the
		// toggle releases itself right after (a started burst always finishes on its own, so releasing the
		// override never cuts it short — the release just re-arms the button)
		if ( newState && ( moduleViewModel.ModuleType == FFBModuleRegistry.GearChangeVibrationType ) )
		{
			if ( _gearChangeTestReleaseTimer == null )
			{
				var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds( 400.0 ) };

				timer.Tick += ( _, _ ) =>
				{
					timer.Stop();

					foreach ( var module in Modules )
					{
						if ( module.ModuleType == FFBModuleRegistry.GearChangeVibrationType )
						{
							module.IsTestActive = false;
						}
					}
				};

				_gearChangeTestReleaseTimer = timer;
			}

			_gearChangeTestReleaseTimer.Stop();
			_gearChangeTestReleaseTimer.Start();
		}
	}

	public void AddModule( string moduleType )
	{
		var descriptor = FFBModuleRegistry.TryGet( moduleType );

		if ( descriptor == null )
		{
			return;
		}

		if ( ( _graph == null ) || _graph.IsBuiltIn )
		{
			return;
		}

		// generators are standalone nodes — no wiring or splicing, parked below the existing nodes
		if ( descriptor.IsGenerator )
		{
			var generatorModule = new FFBModuleData( Guid.NewGuid().ToString( "N" ), moduleType )
			{
				InputAModuleId = string.Empty,
				InputBModuleId = string.Empty
			};

			FFBGraphTopology.PlaceGeneratorNode( _graph, generatorModule );

			_selectedModuleId = generatorModule.ModuleId;   // select the new node once the rebuild recreates the VMs

			_graph.Modules.Insert( _graph.OutputIndex, generatorModule );

			CommitStructureChange();

			return;
		}

		// sources are one-per-graph — adding a second instance of a present source type is a no-op (the add
		// list already hides them; this guards other callers)
		if ( descriptor.IsSource && _graph.Modules.Any( existingModule => existingModule.ModuleType == moduleType ) )
		{
			return;
		}

		// wire the new module's input A from the currently selected module, so a freshly added module drops
		// straight into the signal path being worked on; the Output module and generators cannot feed a signal
		// input (and a brand-new module has no downstream, so no cycle is possible), those fall back to the
		// 360 Hz source → 60 Hz source → any other source cascade (a source-less graph gets the 360 Hz source
		// re-added by the topological sort below, which these references then resolve to)
		var fallbackSourceId = FFBGraphTopology.FallbackSourceModuleId( _graph ) ?? FFBGraph.Source360ModuleId;

		var inputSourceId = fallbackSourceId;

		var wiredFromSelectedModule = ( _selectedModule != null ) && !_selectedModule.IsGenerator && !_selectedModule.IsOutput;

		if ( wiredFromSelectedModule )
		{
			inputSourceId = _selectedModule!.ModuleId;
		}

		// a source keeps its canonical well-known id (one-per-graph makes it collision-free), so the fallback
		// and preview plumbing recognize it across remove/re-add cycles
		var module = new FFBModuleData( descriptor.IsSource ? FFBGraph.CanonicalSourceId( moduleType ) : Guid.NewGuid().ToString( "N" ), moduleType )
		{
			InputAModuleId = inputSourceId,
			InputBModuleId = fallbackSourceId
		};

		// the node is placed relative to the module it splices in after and the first module that was consuming
		// that output (its new downstream neighbor) — captured here, before the splice repoints those inputs
		var splicedAfterModule = ( wiredFromSelectedModule && ( descriptor.SignalInputCount >= 1 ) ) ? _graph.Modules.Find( existingModule => existingModule.ModuleId == inputSourceId ) : null;
		var downstreamModule = ( splicedAfterModule != null ) ? _graph.Modules.Find( existingModule => ( existingModule.InputAModuleId == inputSourceId ) || ( existingModule.InputBModuleId == inputSourceId ) ) : null;

		// splice the new module INTO the selected module's output wire rather than just hanging off it: every
		// input (A or B, on any downstream module) that was consuming the selected module's output now consumes
		// the new module's output instead. Only applies when the new module actually took the selected module as
		// its input A — a new source is not connected to the selection, so nothing gets rewired. The new module
		// isn't in the list yet, so its own input A (the selected module) is untouched and no cycle can form.
		if ( wiredFromSelectedModule && ( descriptor.SignalInputCount >= 1 ) )
		{
			foreach ( var existingModule in _graph.Modules )
			{
				if ( existingModule.InputAModuleId == inputSourceId )
				{
					existingModule.InputAModuleId = module.ModuleId;
				}

				if ( existingModule.InputBModuleId == inputSourceId )
				{
					existingModule.InputBModuleId = module.ModuleId;
				}
			}
		}

		PlaceNewNode( module, splicedAfterModule, downstreamModule );

		_selectedModuleId = module.ModuleId;   // select the new node once the rebuild recreates the VMs

		_graph.Modules.Insert( _graph.OutputIndex, module );

		CommitStructureChange();
	}

	// Place a new node relative to the module it was spliced in after: exactly halfway to the downstream
	// neighbor when there is one, otherwise one column to the right. When the add wasn't spliced (no usable
	// selection, or a new source), fall back to just left of the Output node, stepping down until the spot is
	// free so consecutive adds don't stack on top of each other. An explicit position also keeps
	// NeedsAutoLayout from re-laying-out the whole graph on the next rebuild.
	private void PlaceNewNode( FFBModuleData module, FFBModuleData? splicedAfterModule, FFBModuleData? downstreamModule )
	{
		if ( _graph == null )
		{
			return;
		}

		if ( splicedAfterModule != null )
		{
			if ( downstreamModule != null )
			{
				module.NodeX = ( splicedAfterModule.NodeX + downstreamModule.NodeX ) / 2f;
				module.NodeY = ( splicedAfterModule.NodeY + downstreamModule.NodeY ) / 2f;
			}
			else
			{
				module.NodeX = splicedAfterModule.NodeX + FFBGraphTopology.NodeWidth + FFBGraphTopology.HorizontalGap;
				module.NodeY = splicedAfterModule.NodeY;
			}

			return;
		}

		var output = _graph.Modules.Find( m => m.ModuleId == FFBGraph.OutputModuleId );

		var x = Math.Max( FFBGraphTopology.LayoutMargin, ( output?.NodeX ?? 0f ) - FFBGraphTopology.NodeWidth - FFBGraphTopology.HorizontalGap );
		var y = Math.Max( FFBGraphTopology.LayoutMargin, output?.NodeY ?? FFBGraphTopology.LayoutMargin );

		while ( _graph.Modules.Exists( m => ( Math.Abs( m.NodeX - x ) < 10f ) && ( Math.Abs( m.NodeY - y ) < 10f ) ) )
		{
			y += FFBGraphTopology.NodeHeight + FFBGraphTopology.VerticalGap;
		}

		module.NodeX = x;
		module.NodeY = y;
	}

	public void RemoveModule( FFBModuleViewModel moduleViewModel )
	{
		if ( !moduleViewModel.CanRemove || ( _graph == null ) )
		{
			return;
		}

		var removedModule = _graph.Modules.FirstOrDefault( module => module.ModuleId == moduleViewModel.ModuleId );

		_graph.Modules.RemoveAll( module => module.ModuleId == moduleViewModel.ModuleId );

		// splice the removed module out of the chain: repoint any inputs that referenced it to the removed
		// module's own input A, so the signal keeps flowing through the same path; fall back to the source
		// cascade when the removed module had no usable input A (a source, or a dangling reference) — and if
		// this was the last source, the topological sort below re-adds the 360 Hz source, which these
		// references then resolve to
		var replacementModuleId = ( removedModule != null ) && ( moduleViewModel.SignalInputCount >= 1 ) ? removedModule.InputAModuleId : null;

		if ( string.IsNullOrEmpty( replacementModuleId ) || !_graph.Modules.Any( module => module.ModuleId == replacementModuleId ) )
		{
			replacementModuleId = FFBGraphTopology.FallbackSourceModuleId( _graph ) ?? FFBGraph.Source360ModuleId;
		}

		foreach ( var module in _graph.Modules )
		{
			if ( module.InputAModuleId == moduleViewModel.ModuleId )
			{
				module.InputAModuleId = replacementModuleId;
			}

			if ( module.InputBModuleId == moduleViewModel.ModuleId )
			{
				module.InputBModuleId = replacementModuleId;
			}
		}

		DataContext.DataContext.Instance.Settings.RemoveFFBModuleButtonMappings( [ moduleViewModel.ModuleId ] );

		CommitStructureChange();
	}

	/// <summary>Nudges a module knob setting by its click step in the given direction (+1/-1) — the fire body
	/// for mapped module-knob inputs (see App.RebuildButtonMappingIndex). No-op when the module isn't part of
	/// the currently selected graphs: module ids are graph-local, so a mapping only drives the graph its module
	/// lives in.</summary>
	public void AdjustModuleKnob( string moduleId, string settingKey, float direction )
	{
		foreach ( var moduleViewModel in Modules )
		{
			if ( moduleViewModel.ModuleId != moduleId )
			{
				continue;
			}

			foreach ( var settingViewModel in moduleViewModel.Settings )
			{
				if ( ( settingViewModel.Key == settingKey ) && ( settingViewModel.SettingType == FFBSettingType.Knob ) )
				{
					settingViewModel.Value += direction * settingViewModel.ClickStepSize;

					return;
				}
			}

			return;
		}
	}

	/// <summary>Re-run the automatic layered layout over the current graph's nodes (the auto-layout button on the
	/// node editor). Positions are display-only, so no engine rebuild — just persist and refresh the canvas.</summary>
	public void AutoLayout()
	{
		if ( ( _graph == null ) || _graph.IsBuiltIn )
		{
			return;
		}

		FFBGraphTopology.ApplyAutoLayout( _graph );

		if ( DataContext.DataContext.Instance.Settings.RacingWheelFFBGraphSnapToGrid )
		{
			FFBGraphTopology.SnapAllToGrid( _graph );
		}

		App.Instance!.SettingsFile.QueueForSerialization = true;

		RebuildFromCurrentSelection();
	}

	/// <summary>Snap every canvas node to the grid (the snap-to-grid toggle was just switched on). Built-in
	/// graphs keep their shipped layout — their node positions are locked.</summary>
	public void SnapAllToGrid()
	{
		if ( ( _graph == null ) || _graph.IsBuiltIn )
		{
			return;
		}

		FFBGraphTopology.SnapAllToGrid( _graph );

		App.Instance!.SettingsFile.QueueForSerialization = true;

		RebuildFromCurrentSelection();
	}

	/// <summary>An input re-wire committed from a module's input combo. The evaluation order is re-derived and the
	/// engines rebuilt immediately, but the card rebuild is deferred to the dispatcher so the combo box whose
	/// selection-changed drove this call is not torn down mid-event.</summary>
	public void CommitWiringChange()
	{
		CommitStructureChange( deferRebuild: true );
	}

	private void CommitStructureChange( bool deferRebuild = false )
	{
		if ( _graph != null )
		{
			// restore the "inputs reference earlier modules" invariant before any engine sees the new structure
			FFBGraphTopology.SortTopologically( _graph );
		}

		var app = App.Instance!;

		app.RacingWheel.RebuildLiveEngine();

		DataContext.DataContext.Instance.Settings.SyncFFBGraphModuleValues( true );

		app.SettingsFile.QueueForSerialization = true;
		app.RacingWheel.UpdateAlgorithmPreview = true;

		if ( deferRebuild )
		{
			QueueRebuild();
		}
		else
		{
			RebuildFromCurrentSelection();
		}
	}

	private void QueueRebuild()
	{
		if ( _rebuildQueued )
		{
			return;
		}

		var dispatcher = System.Windows.Application.Current?.Dispatcher;

		if ( dispatcher == null )
		{
			RebuildFromCurrentSelection();

			return;
		}

		_rebuildQueued = true;

		dispatcher.BeginInvoke( System.Windows.Threading.DispatcherPriority.Background, () =>
		{
			_rebuildQueued = false;

			RebuildFromCurrentSelection();
		} );
	}
}

/// <summary>Picks the knob / switch / choice template for a module setting based on its type.</summary>
/// <summary>Inert sentinel filling an empty settings-panel grid cell (row padding before a BreakRow setting).</summary>
public sealed class FFBSettingSpacer
{
	public static readonly FFBSettingSpacer Instance = new();
}

/// <summary>One node's group in the quick-controls block above the editor: the owning module VM (for the
/// group's caption and description — shown once per node, however many of its settings are pinned) plus that
/// node's pinned setting VMs. The setting VMs are the SHARED instances the settings column binds, so the two
/// stay in sync.</summary>
public sealed class FFBPinnedGroupViewModel( FFBModuleViewModel module )
{
	public FFBModuleViewModel Module { get; } = module;
	public ObservableCollection<FFBModuleSettingViewModel> Settings { get; } = [];
}

public sealed class FFBSettingTemplateSelector : DataTemplateSelector
{
	public DataTemplate? KnobTemplate { get; set; }
	public DataTemplate? SwitchTemplate { get; set; }
	public DataTemplate? ChoiceTemplate { get; set; }
	public DataTemplate? SpacerTemplate { get; set; }

	public override DataTemplate? SelectTemplate( object item, DependencyObject container )
	{
		if ( item is FFBSettingSpacer )
		{
			return SpacerTemplate;
		}

		if ( item is FFBModuleSettingViewModel settingViewModel )
		{
			return settingViewModel.SettingType switch
			{
				FFBSettingType.Switch => SwitchTemplate,
				FFBSettingType.Choice => ChoiceTemplate,
				_ => KnobTemplate
			};
		}

		return base.SelectTemplate( item, container );
	}
}
