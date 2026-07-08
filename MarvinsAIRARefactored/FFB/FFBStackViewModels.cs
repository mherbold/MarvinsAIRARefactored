
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace MarvinsAIRARefactored.FFB;

// View-models backing the RacingWheelPage FFB stack editor. The editor binds to
// DataContext.Instance.RacingWheelStackViewModel, which mirrors the currently selected stack's modules. Edits
// mutate the FFBStack model, push atomic values into both engines, keep the per-context store in sync, queue
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
		[ FFBModuleRegistry.OutputType ] = "Output",
		[ FFBModuleRegistry.LowPassFilterType ] = "Low-pass filter",
		[ FFBModuleRegistry.DetailExtractorType ] = "Detail extractor",
		[ FFBModuleRegistry.GainType ] = "Gain",
		[ FFBModuleRegistry.MixerType ] = "Mixer",
		[ FFBModuleRegistry.RateLimiterType ] = "Rate limiter",
		[ FFBModuleRegistry.SlewCompressorType ] = "Slew compressor",
		[ FFBModuleRegistry.CompressorType ] = "Compressor",
		[ FFBModuleRegistry.DetailEnhancerType ] = "Detail enhancer",
		[ FFBModuleRegistry.SmootherType ] = "Smoother",
		[ FFBModuleRegistry.AdaptiveBlendType ] = "Adaptive blend",
		[ FFBModuleRegistry.CurveType ] = "Curve",
		[ FFBModuleRegistry.SoftLimiterType ] = "Soft limiter",
		[ FFBModuleRegistry.MaximumType ] = "Maximum",
		[ FFBModuleRegistry.MinimumType ] = "Minimum",
		[ FFBModuleRegistry.CrashProtectionType ] = "Crash protection",
		[ FFBModuleRegistry.CurbProtectionType ] = "Curb protection",
		[ FFBModuleRegistry.ParkedStrengthType ] = "Parked strength",
		[ FFBModuleRegistry.LFEMixType ] = "LFE mix",
		[ FFBModuleRegistry.SoftLockType ] = "Soft lock",
		[ FFBModuleRegistry.FrictionType ] = "Friction",
		[ FFBModuleRegistry.WheelCenteringType ] = "Wheel centering",
		[ FFBModuleRegistry.UndersteerForceType ] = "Understeer force",
		[ FFBModuleRegistry.OversteerForceType ] = "Oversteer force",
		[ FFBModuleRegistry.SeatOfPantsForceType ] = "Seat-of-pants force",
		[ FFBModuleRegistry.UndersteerVibrationType ] = "Understeer vibration",
		[ FFBModuleRegistry.OversteerVibrationType ] = "Oversteer vibration",
		[ FFBModuleRegistry.SeatOfPantsVibrationType ] = "Seat-of-pants vibration",
		[ FFBModuleRegistry.ShiftRPMVibrationType ] = "Shift RPM vibration",
		[ FFBModuleRegistry.GearChangeVibrationType ] = "Gear change vibration",
		[ FFBModuleRegistry.ABSVibrationType ] = "ABS vibration",
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

		return string.IsNullOrEmpty( localized ) ? fallback : localized;
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
	private readonly string _moduleId;
	private readonly FFBModuleData _model;
	private readonly FFBSettingDescriptor _descriptor;

	private float _value;
	private string _valueString = string.Empty;

	public FFBModuleSettingViewModel( string moduleId, FFBModuleData model, FFBSettingDescriptor descriptor )
	{
		_moduleId = moduleId;
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

	private void OnPropertyChanged( [CallerMemberName] string? propertyName = null ) => PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );

	public FFBSettingType SettingType => _descriptor.Type;

	// FFBSetting{Key} (new, precise) -> {Key} (reuse an existing translated key like Strength/Curve/Duration) -> humanized.
	public string Label => FFBDisplayNames.Localize( _descriptor.LocalizationKey, FFBDisplayNames.Localize( _descriptor.Key, FFBDisplayNames.Humanize( _descriptor.Key ) ) );
	public float Minimum => _descriptor.Min;
	public float Maximum => _descriptor.Max;
	public float ClickStepSize => _descriptor.ClickStepSize;
	public float DragStepSize => _descriptor.DragStepSize;
	public float? DefaultValue => _descriptor.DefaultValue;
	public bool ShowCurve => _descriptor.ShowCurve;

	public List<KeyValuePair<int, string>> ChoiceOptions { get; } = [];

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

			_value = clamped;
			_model.SettingValues[ _descriptor.Key ] = clamped;

			var app = App.Instance!;

			app.RacingWheel.SetEngineValue( _moduleId, _descriptor.Key, clamped );

			ValueString = Format( clamped );

			DataContext.DataContext.Instance.Settings.SyncFFBStackModuleValues( true );

			app.SettingsFile.RecordChangedSetting( $"ffb:{_moduleId}/{_descriptor.Key}", $"[Settings] Updating FFB module setting {_moduleId}/{_descriptor.Key} to {clamped}" );
			app.SettingsFile.QueueForSerialization = true;

			app.RacingWheel.UpdateAlgorithmPreview = true;

			OnPropertyChanged();
			OnPropertyChanged( nameof( IsOn ) );
			OnPropertyChanged( nameof( ChoiceIndex ) );
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
	private readonly FFBStackViewModel _owner;
	private readonly FFBModuleData _model;
	private readonly FFBModuleDescriptor _descriptor;

	private string _displayName = string.Empty;
	private List<KeyValuePair<string, string>> _eligibleInputs = [];

	public FFBModuleViewModel( FFBStackViewModel owner, FFBModuleData model, FFBModuleDescriptor descriptor )
	{
		_owner = owner;
		_model = model;
		_descriptor = descriptor;

		foreach ( var settingDescriptor in descriptor.Settings )
		{
			Settings.Add( new FFBModuleSettingViewModel( model.ModuleId, model, settingDescriptor ) );
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged( [CallerMemberName] string? propertyName = null ) => PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );

	public string ModuleId => _model.ModuleId;
	public string ModuleType => _model.ModuleType;

	public bool IsFixed => _descriptor.IsSource || _descriptor.IsOutput;
	public bool IsGenerator => _descriptor.IsGenerator;
	public bool CanToggleEnabled => !IsFixed;
	public bool CanRemove => !IsFixed;
	public bool CanReorder => !IsFixed;

	public int SignalInputCount => _descriptor.SignalInputCount;
	public bool ShowInputA => _descriptor.SignalInputCount >= 1;
	public bool ShowInputB => _descriptor.SignalInputCount >= 2;
	public string InputALabel => _descriptor.SignalInputCount >= 2 ? "Input A" : "Input";
	public string InputBLabel => "Input B";

	public ObservableCollection<FFBModuleSettingViewModel> Settings { get; } = [];

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

			DataContext.DataContext.Instance.Settings.SyncFFBStackModuleValues( true );

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

		// routing is structure: rebuild the live engine and refresh the preview
		var app = App.Instance!;

		app.RacingWheel.RebuildLiveEngine();
		app.SettingsFile.QueueForSerialization = true;
		app.RacingWheel.UpdateAlgorithmPreview = true;

		OnPropertyChanged( isInputB ? nameof( InputBSelectedId ) : nameof( InputASelectedId ) );
	}
}

/// <summary>The stack editor's root VM: the module cards for the currently selected stack, plus structure edits.</summary>
public sealed class FFBStackViewModel : INotifyPropertyChanged
{
	private FFBStack? _stack;

	public ObservableCollection<FFBModuleViewModel> Modules { get; } = [];

	// Built on access (not in the constructor) so it is not evaluated during DataContext static construction,
	// before Localization exists, and so its module names re-localize on a language switch.
	public List<KeyValuePair<string, string>> AddableModuleTypes => BuildAddableModuleTypes();

	private string _selectedAddableModuleType = string.Empty;

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged( [CallerMemberName] string? propertyName = null ) => PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );

	public string SelectedAddableModuleType
	{
		get => _selectedAddableModuleType;

		set
		{
			if ( value != _selectedAddableModuleType )
			{
				_selectedAddableModuleType = value;

				OnPropertyChanged();
			}
		}
	}

	private static List<KeyValuePair<string, string>> BuildAddableModuleTypes()
	{
		var list = new List<KeyValuePair<string, string>>();

		foreach ( var descriptor in FFBModuleRegistry.All )
		{
			// sources and output are fixed and cannot be added by the user
			if ( descriptor.IsSource || descriptor.IsOutput )
			{
				continue;
			}

			list.Add( new KeyValuePair<string, string>( descriptor.TypeKey, FFBDisplayNames.Module( descriptor.TypeKey ) ) );
		}

		return list.OrderBy( pair => pair.Value ).ToList();
	}

	/// <summary>Rebuild the whole card tree from the currently selected stack. UI thread only.</summary>
	public void RebuildFromCurrentSelection()
	{
		Modules.Clear();

		var settings = DataContext.DataContext.Instance.Settings;

		if ( !settings.RacingWheelStacks.TryGetValue( settings.RacingWheelSelectedStackName, out var stack ) )
		{
			_stack = null;

			return;
		}

		_stack = stack;

		var moduleViewModels = new List<FFBModuleViewModel>();

		foreach ( var module in stack.Modules )
		{
			var descriptor = FFBModuleRegistry.TryGet( module.ModuleType );

			if ( descriptor != null )
			{
				moduleViewModels.Add( new FFBModuleViewModel( this, module, descriptor ) );
			}
		}

		for ( var i = 0; i < moduleViewModels.Count; i++ )
		{
			moduleViewModels[ i ].DisplayName = $"{i + 1} · {FFBDisplayNames.Module( moduleViewModels[ i ].ModuleType )}";
		}

		for ( var i = 0; i < moduleViewModels.Count; i++ )
		{
			var eligible = new List<KeyValuePair<string, string>>();

			for ( var j = 0; j < i; j++ )
			{
				if ( !moduleViewModels[ j ].IsGenerator )
				{
					eligible.Add( new KeyValuePair<string, string>( moduleViewModels[ j ].ModuleId, moduleViewModels[ j ].DisplayName ) );
				}
			}

			moduleViewModels[ i ].SetEligibleInputs( eligible );
		}

		foreach ( var moduleViewModel in moduleViewModels )
		{
			Modules.Add( moduleViewModel );
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

	public void AddSelectedModule()
	{
		if ( !string.IsNullOrEmpty( SelectedAddableModuleType ) )
		{
			AddModule( SelectedAddableModuleType );
		}
	}

	public void AddModule( string moduleType )
	{
		if ( ( _stack == null ) || ( FFBModuleRegistry.TryGet( moduleType ) == null ) )
		{
			return;
		}

		var module = new FFBModuleData( Guid.NewGuid().ToString( "N" ), moduleType )
		{
			InputAModuleId = FFBStack.Source360ModuleId,
			InputBModuleId = FFBStack.Source360ModuleId
		};

		_stack.Modules.Insert( _stack.OutputIndex, module );

		CommitStructureChange();
	}

	public void RemoveModule( FFBModuleViewModel moduleViewModel )
	{
		if ( ( _stack == null ) || !moduleViewModel.CanRemove )
		{
			return;
		}

		_stack.Modules.RemoveAll( module => module.ModuleId == moduleViewModel.ModuleId );

		// repoint any inputs that referenced the removed module back to the 360 Hz source
		foreach ( var module in _stack.Modules )
		{
			if ( module.InputAModuleId == moduleViewModel.ModuleId )
			{
				module.InputAModuleId = FFBStack.Source360ModuleId;
			}

			if ( module.InputBModuleId == moduleViewModel.ModuleId )
			{
				module.InputBModuleId = FFBStack.Source360ModuleId;
			}
		}

		CommitStructureChange();
	}

	public void MoveModule( FFBModuleViewModel moduleViewModel, int direction )
	{
		if ( ( _stack == null ) || !moduleViewModel.CanReorder )
		{
			return;
		}

		var index = _stack.Modules.FindIndex( module => module.ModuleId == moduleViewModel.ModuleId );
		var target = index + direction;

		// user modules live strictly between the two sources (slots 0-1) and the trailing Output module
		if ( ( index < 2 ) || ( target < 2 ) || ( target >= _stack.OutputIndex ) )
		{
			return;
		}

		( _stack.Modules[ index ], _stack.Modules[ target ] ) = ( _stack.Modules[ target ], _stack.Modules[ index ] );

		ValidateInputReferences();

		CommitStructureChange();
	}

	// A reorder or removal can leave an input pointing at a module that is no longer earlier in the list; reset
	// any such forward/dangling reference to the 360 Hz source (the engine falls back the same way).
	private void ValidateInputReferences()
	{
		if ( _stack == null )
		{
			return;
		}

		var indexById = new Dictionary<string, int>( StringComparer.Ordinal );

		for ( var i = 0; i < _stack.Modules.Count; i++ )
		{
			indexById[ _stack.Modules[ i ].ModuleId ] = i;
		}

		for ( var i = 0; i < _stack.Modules.Count; i++ )
		{
			var module = _stack.Modules[ i ];

			if ( !indexById.TryGetValue( module.InputAModuleId, out var indexA ) || ( indexA >= i ) )
			{
				module.InputAModuleId = FFBStack.Source360ModuleId;
			}

			if ( !indexById.TryGetValue( module.InputBModuleId, out var indexB ) || ( indexB >= i ) )
			{
				module.InputBModuleId = FFBStack.Source360ModuleId;
			}
		}
	}

	private void CommitStructureChange()
	{
		var app = App.Instance!;

		app.RacingWheel.RebuildLiveEngine();

		DataContext.DataContext.Instance.Settings.SyncFFBStackModuleValues( true );

		app.SettingsFile.QueueForSerialization = true;
		app.RacingWheel.UpdateAlgorithmPreview = true;

		RebuildFromCurrentSelection();
	}
}

/// <summary>Picks the knob / switch / choice template for a module setting based on its type.</summary>
public sealed class FFBSettingTemplateSelector : DataTemplateSelector
{
	public DataTemplate? KnobTemplate { get; set; }
	public DataTemplate? SwitchTemplate { get; set; }
	public DataTemplate? ChoiceTemplate { get; set; }

	public override DataTemplate? SelectTemplate( object item, DependencyObject container )
	{
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
