
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
		[ FFBModuleRegistry.OutputType ] = "Output",
		[ FFBModuleRegistry.LowPassFilterType ] = "Low-pass filter",
		[ FFBModuleRegistry.HighPassFilterType ] = "High-pass filter",
		[ FFBModuleRegistry.GainType ] = "Gain",
		[ FFBModuleRegistry.AddType ] = "Add",
		[ FFBModuleRegistry.BlendType ] = "Blend",
		[ FFBModuleRegistry.SlewLimiterType ] = "Slew limiter",
		[ FFBModuleRegistry.SlewCompressorType ] = "Slew compressor",
		[ FFBModuleRegistry.CompressorType ] = "Compressor",
		[ FFBModuleRegistry.TransientEnhancerType ] = "Transient enhancer",
		[ FFBModuleRegistry.AdaptiveSmootherType ] = "Adaptive smoother",
		[ FFBModuleRegistry.AdaptiveBlendType ] = "Adaptive blend",
		[ FFBModuleRegistry.CurveType ] = "Curve",
		[ FFBModuleRegistry.SoftLimiterType ] = "Soft limiter",
		[ FFBModuleRegistry.MaximumType ] = "Maximum",
		[ FFBModuleRegistry.MinimumType ] = "Minimum",
		[ FFBModuleRegistry.CrashProtectionType ] = "Crash protection",
		[ FFBModuleRegistry.CurbProtectionType ] = "Curb protection",
		[ FFBModuleRegistry.ParkedStrengthType ] = "Parked strength",
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

			DataContext.DataContext.Instance.Settings.SyncFFBGraphModuleValues( true );

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
			Settings.Add( new FFBModuleSettingViewModel( model.ModuleId, model, settingDescriptor ) );
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged( [CallerMemberName] string? propertyName = null ) => PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );

	public string ModuleId => _model.ModuleId;
	public string ModuleType => _model.ModuleType;

	public FFBModuleDescriptor Descriptor => _descriptor;

	public bool IsFixed => _descriptor.IsSource || _descriptor.IsOutput;
	public bool IsGenerator => _descriptor.IsGenerator;
	public bool IsOutput => _descriptor.IsOutput;
	public bool CanToggleEnabled => !IsFixed;
	public bool CanRemove => !IsFixed;

	public int SignalInputCount => _descriptor.SignalInputCount;
	public bool ShowInputA => _descriptor.SignalInputCount >= 1;
	public bool ShowInputB => _descriptor.SignalInputCount >= 2;
	public string InputALabel => _descriptor.SignalInputCount >= 2 ? FFBDisplayNames.Localize( "InputA", "Input A" ) : FFBDisplayNames.Localize( "Input", "Input" );
	public string InputBLabel => FFBDisplayNames.Localize( "InputB", "Input B" );

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

/// <summary>The graph editor's root VM: the module cards for the currently selected graph, plus structure edits.</summary>
public sealed class FFBGraphViewModel : INotifyPropertyChanged
{
	private FFBGraph? _graph;

	private string? _selectedModuleId = null;
	private FFBModuleViewModel? _selectedModule = null;
	private bool _rebuildQueued = false;

	public ObservableCollection<FFBModuleViewModel> Modules { get; } = [];

	/// <summary>The non-generator modules (sources, DSP/effects, Output) — the nodes shown on the graph canvas.</summary>
	public ObservableCollection<FFBModuleViewModel> MainModules { get; } = [];

	/// <summary>The generator modules — the cards shown in the vibration effects section.</summary>
	public ObservableCollection<FFBModuleViewModel> GeneratorModules { get; } = [];

	/// <summary>Display-only wires between the graph canvas nodes.</summary>
	public ObservableCollection<FFBNodeWireViewModel> Wires { get; } = [];

	// Built on access (not in the constructor) so it is not evaluated during DataContext static construction,
	// before Localization exists, and so its module names re-localize on a language switch.
	public List<KeyValuePair<string, string>> AddableModuleTypes => BuildAddableModuleTypes( generators: false );
	public List<KeyValuePair<string, string>> AddableGeneratorModuleTypes => BuildAddableModuleTypes( generators: true );

	private string _selectedAddableModuleType = string.Empty;
	private string _selectedAddableGeneratorModuleType = string.Empty;

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

	public string SelectedAddableGeneratorModuleType
	{
		get => _selectedAddableGeneratorModuleType;

		set
		{
			if ( value != _selectedAddableGeneratorModuleType )
			{
				_selectedAddableGeneratorModuleType = value;

				OnPropertyChanged();
			}
		}
	}

	/// <summary>
	/// The module selected on the node canvas — the settings panel shows its card and the preview taps its
	/// signals. Session-only (not persisted); falls back to the Output module whenever the stored selection
	/// disappears (graph switch, context reload, module removal). Selection is presentation state, so setting it
	/// never rebuilds an engine — it only refreshes the preview.
	/// </summary>
	public FFBModuleViewModel? SelectedModule
	{
		get => _selectedModule;

		set
		{
			if ( value == _selectedModule )
			{
				return;
			}

			_selectedModule = value;
			_selectedModuleId = value?.ModuleId;

			foreach ( var module in Modules )
			{
				module.IsSelected = module == value;
			}

			OnPropertyChanged();

			var app = App.Instance;

			if ( app != null )
			{
				app.RacingWheel.UpdateAlgorithmPreview = true;
			}
		}
	}

	private static List<KeyValuePair<string, string>> BuildAddableModuleTypes( bool generators )
	{
		var list = new List<KeyValuePair<string, string>>();

		foreach ( var descriptor in FFBModuleRegistry.All )
		{
			// sources and output are fixed and cannot be added by the user
			if ( descriptor.IsSource || descriptor.IsOutput )
			{
				continue;
			}

			if ( descriptor.IsGenerator != generators )
			{
				continue;
			}

			list.Add( new KeyValuePair<string, string>( descriptor.TypeKey, FFBDisplayNames.Module( descriptor.TypeKey ) ) );
		}

		return list.OrderBy( pair => pair.Value ).ToList();
	}

	/// <summary>Rebuild the whole card tree from the currently selected graph. UI thread only.</summary>
	public void RebuildFromCurrentSelection()
	{
		Modules.Clear();
		MainModules.Clear();
		GeneratorModules.Clear();
		Wires.Clear();

		var settings = DataContext.DataContext.Instance.Settings;

		if ( !settings.RacingWheelFFBGraphs.TryGetValue( settings.RacingWheelSelectedFFBGraphName, out var graph ) )
		{
			_graph = null;

			SelectedModule = null;

			return;
		}

		_graph = graph;

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

		// eligible inputs: any non-generator, non-Output module that is not downstream of the consumer (no cycles)
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
				if ( candidate.IsGenerator || candidate.IsOutput || downstream.Contains( candidate.ModuleId ) )
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

			if ( moduleViewModel.IsGenerator )
			{
				GeneratorModules.Add( moduleViewModel );
			}
			else
			{
				MainModules.Add( moduleViewModel );
			}
		}

		// one display wire per visible input of each canvas node
		foreach ( var moduleViewModel in MainModules )
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

		_selectedModule = null;   // force the setter to fire even when the id did not change

		SelectedModule = restored;
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

	public void AddSelectedGeneratorModule()
	{
		if ( !string.IsNullOrEmpty( SelectedAddableGeneratorModuleType ) )
		{
			AddModule( SelectedAddableGeneratorModuleType );
		}
	}

	public void AddModule( string moduleType )
	{
		if ( _graph == null )
		{
			return;
		}

		var descriptor = FFBModuleRegistry.TryGet( moduleType );

		if ( descriptor == null )
		{
			return;
		}

		var module = new FFBModuleData( Guid.NewGuid().ToString( "N" ), moduleType )
		{
			InputAModuleId = FFBGraph.Source360ModuleId,
			InputBModuleId = FFBGraph.Source360ModuleId
		};

		if ( !descriptor.IsGenerator )
		{
			PlaceNewNode( module );

			_selectedModuleId = module.ModuleId;   // select the new node once the rebuild recreates the VMs
		}

		_graph.Modules.Insert( _graph.OutputIndex, module );

		CommitStructureChange();
	}

	// Drop a new node just left of the Output node, stepping down until the spot is free so consecutive adds
	// don't stack on top of each other. An explicit position also keeps NeedsAutoLayout from re-laying-out the
	// whole graph on the next rebuild.
	private void PlaceNewNode( FFBModuleData module )
	{
		if ( _graph == null )
		{
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
		if ( ( _graph == null ) || !moduleViewModel.CanRemove )
		{
			return;
		}

		var removedModule = _graph.Modules.FirstOrDefault( module => module.ModuleId == moduleViewModel.ModuleId );

		_graph.Modules.RemoveAll( module => module.ModuleId == moduleViewModel.ModuleId );

		// splice the removed module out of the chain: repoint any inputs that referenced it to the removed
		// module's own input A, so the signal keeps flowing through the same path; fall back to the 360 Hz
		// source only when the removed module had no usable input A (no inputs, or a dangling reference)
		var replacementModuleId = removedModule?.InputAModuleId;

		if ( string.IsNullOrEmpty( replacementModuleId ) || !_graph.Modules.Any( module => module.ModuleId == replacementModuleId ) )
		{
			replacementModuleId = FFBGraph.Source360ModuleId;
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

		CommitStructureChange();
	}

	/// <summary>Re-run the automatic layered layout over the current graph's nodes (the auto-layout button on the
	/// node editor). Positions are display-only, so no engine rebuild — just persist and refresh the canvas.</summary>
	public void AutoLayout()
	{
		if ( _graph == null )
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

	/// <summary>Snap every canvas node to the grid (the snap-to-grid toggle was just switched on).</summary>
	public void SnapAllToGrid()
	{
		if ( _graph == null )
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
