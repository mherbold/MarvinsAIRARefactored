
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Evaluates one <see cref="FFBGraph"/>. Two instances exist at runtime: a live engine (driven by the telemetry
/// thread in six-sample 360 Hz bursts, one per telemetry frame) and a preview engine (driven by the dispatcher
/// over a recording). Both are built from the same graph model but keep independent state.
/// <para>Threading: structure edits build a brand-new engine on the UI thread and the caller swaps a volatile
/// reference so the 360 Hz reader picks it up on its next frame. Knob edits arrive via <see cref="SetValue"/> as
/// atomic single-float writes the reader tolerates. The arrays (<c>_modules</c>, <c>_signals</c>,
/// <c>_prePassModules</c>) are never mutated in place after Rebuild — always rebuild-and-swap.</para>
/// </summary>
public sealed class FFBGraphEngine
{
	private FFBModule[] _modules = [];
	private FFBModuleDescriptor[] _descriptors = [];
	private float[] _signals = [];
	private FFBModule[] _prePassModules = [];   // the (few) modules that advertise HasPrePass — see Process
	private int _outputModuleIndex = -1;        // last IsOutput module, or -1 when the graph has none
	private int _fallbackSourceIndex;

	private readonly Dictionary<string, int> _indexById = new( StringComparer.Ordinal );

	// results, read after Process
	public float MainOutput;              // Output module result (normalized, pre-fade)
	public float VibrationOutput;         // sum of generator modules (normalized)

	// "any protection node active" flags — cleared at the top of every Process tick, then OR'd back in by each
	// crash/curb protection module's PrePass (the modules self-trigger against their own thresholds, so these
	// are the only shared protection state left; they drive the graph gutter, G Tensioner, and announcements)
	public bool CrashProtectionActive;
	public bool CurbProtectionActive;

	/// <summary>The graph model this engine was last built from (for the preview replay / editor).</summary>
	public FFBGraph? Graph { get; private set; }

	/// <summary>
	/// Rebuild the engine from a graph model (generator modules live in the same graph now — they have no signal
	/// inputs and only feed the vibration bus, so their position in the evaluation order is irrelevant). UI
	/// thread only; allocates the module and signal arrays. A module may only reference an EARLIER module's
	/// output (or a source); forward/dangling references fall back to the 360 Hz source, which also covers
	/// reorder/remove edges.
	/// </summary>
	public void Rebuild( FFBGraph graph )
	{
		Graph = graph;

		var allModules = graph.Modules;

		var moduleCount = allModules.Count;

		var modules = new FFBModule[ moduleCount ];
		var descriptors = new FFBModuleDescriptor[ moduleCount ];

		_indexById.Clear();

		for ( var i = 0; i < moduleCount; i++ )
		{
			_indexById[ allModules[ i ].ModuleId ] = i;
		}

		// locate the fallback target for invalid input references: the 360 Hz source, then the 60 Hz source,
		// then any other source present (the editor guarantees a graph never ends up source-less, so index 0
		// is a defensive last resort only)

		if ( !_indexById.TryGetValue( FFBGraph.Source360ModuleId, out var fallbackSourceIndex ) && !_indexById.TryGetValue( FFBGraph.Source60ModuleId, out fallbackSourceIndex ) )
		{
			fallbackSourceIndex = 0;

			for ( var i = 0; i < moduleCount; i++ )
			{
				if ( FFBModuleRegistry.TryGet( allModules[ i ].ModuleType )?.IsSource == true )
				{
					fallbackSourceIndex = i;

					break;
				}
			}
		}

		_fallbackSourceIndex = fallbackSourceIndex;

		var prePassModules = new List<FFBModule>();
		var outputModuleIndex = -1;

		for ( var i = 0; i < moduleCount; i++ )
		{
			var model = allModules[ i ];

			var descriptor = FFBModuleRegistry.TryGet( model.ModuleType ) ?? FFBModuleRegistry.Get( FFBModuleRegistry.Source360HzType );

			var module = descriptor.CreateRuntime();

			module.Owner = this;
			module.Configure( descriptor, model );

			module.InputAIndex = ResolveInput( model.InputAModuleId, i );
			module.InputBIndex = ResolveInput( model.InputBModuleId, i );

			module.Reset();

			modules[ i ] = module;
			descriptors[ i ] = descriptor;

			if ( module.HasPrePass )
			{
				prePassModules.Add( module );
			}

			if ( module.IsOutput )
			{
				outputModuleIndex = i;
			}
		}

		_modules = modules;
		_descriptors = descriptors;
		_signals = new float[ moduleCount ];
		_prePassModules = [ .. prePassModules ];
		_outputModuleIndex = outputModuleIndex;
	}

	/// <summary>An input reference is valid only if it resolves to an EARLIER module; otherwise fall back to the fallback source.</summary>
	private int ResolveInput( string moduleId, int consumerIndex )
	{
		if ( _indexById.TryGetValue( moduleId, out var index ) && ( index < consumerIndex ) )
		{
			return index;
		}

		return _fallbackSourceIndex;
	}

	/// <summary>Zero every module's internal state (used before a preview replay and on graph/context change).</summary>
	public void ResetState()
	{
		for ( var i = 0; i < _modules.Length; i++ )
		{
			_modules[ i ].Reset();
		}

		MainOutput = 0f;
		VibrationOutput = 0f;
		CrashProtectionActive = false;
		CurbProtectionActive = false;
	}

/// <summary>Edit-time write of a module's session-only test override (see <see cref="FFBModule.TestActive"/>).
	/// Atomic bool write, tolerated by the 360 Hz reader. Lost on Rebuild by design — the editor re-applies it
	/// to the preview engine on every preview refresh.</summary>
	public void SetTestActive( string moduleId, bool active )
	{
		if ( _indexById.TryGetValue( moduleId, out var index ) )
		{
			_modules[ index ].TestActive = active;
		}
	}

	/// <summary>Edit-time atomic write of a single module setting value, then refresh aggregates. UI thread only.</summary>
	public void SetValue( string moduleId, string key, float value )
	{
		if ( !_indexById.TryGetValue( moduleId, out var index ) )
		{
			return;
		}

		var settingIndex = _descriptors[ index ].IndexOfSetting( key );

		if ( settingIndex < 0 )
		{
			return;
		}

		_modules[ index ].SetValue( settingIndex, value );
	}

	/// <summary>Index of a module in the signal array, or -1 when the id is unknown. Preview taps only.</summary>
	public int IndexOf( string moduleId )
	{
		return _indexById.TryGetValue( moduleId, out var index ) ? index : -1;
	}

	/// <summary>
	/// A module's signal value from the last <see cref="Process"/> call (0 when out of range). Preview engine
	/// only — call on the same thread that drives Process, after it returns; never tap the live engine this way.
	/// </summary>
	public float GetSignal( int index )
	{
		var signals = _signals;

		return ( index >= 0 ) && ( index < signals.Length ) ? signals[ index ] : 0f;
	}

	/// <summary>A module's resolved input indices (already fallback-resolved to the fallback source where the
	/// stored reference was invalid). Preview taps only; returns the fallback source for an out-of-range index.</summary>
	public ( int inputAIndex, int inputBIndex ) GetResolvedInputs( int index )
	{
		var modules = _modules;

		if ( ( index < 0 ) || ( index >= modules.Length ) )
		{
			return ( _fallbackSourceIndex, _fallbackSourceIndex );
		}

		return ( modules[ index ].InputAIndex, modules[ index ].InputBIndex );
	}

	/// <summary>
	/// Hot path. PrePass the modules that have one (so protection timers/scales advance before the signal
	/// loop — reproducing the old ordering where curb/crash advanced before the algorithm ran), then
	/// evaluate every module in list order. Generators feed the normalized vibration bus; the Output module
	/// publishes <see cref="MainOutput"/> (a graph without one leaves MainOutput at its previous value).
	/// </summary>
	public void Process( in FFBTickContext ctx )
	{
		var modules = _modules;
		var descriptors = _descriptors;
		var signals = _signals;
		var prePassModules = _prePassModules;

		VibrationOutput = 0f;
		CrashProtectionActive = false;
		CurbProtectionActive = false;

		for ( var i = 0; i < prePassModules.Length; i++ )
		{
			prePassModules[ i ].PrePass( in ctx );
		}

		for ( var i = 0; i < modules.Length; i++ )
		{
			var module = modules[ i ];

			var inputA = signals[ module.InputAIndex ];
			var inputB = signals[ module.InputBIndex ];

			if ( module.IsGenerator )
			{
				// the generator's output lands in its signal slot too, so the preview can tap a selected
				// generator node — but it never feeds another module's input, only the vibration bus
				signals[ i ] = module.Enabled ? module.Process( in ctx, inputA, inputB ) : 0f;

				VibrationOutput += signals[ i ];
			}
			else if ( module.Enabled )
			{
				signals[ i ] = module.Process( in ctx, inputA, inputB );
			}
			else
			{
				// disabled module passes input A through; a disabled inputless module (any source) has nothing
				// to pass — it goes silent (its input indices only hold the fallback source, never a real wiring)
				signals[ i ] = descriptors[ i ].SignalInputCount == 0 ? 0f : inputA;
			}
		}

		if ( _outputModuleIndex >= 0 )
		{
			MainOutput = signals[ _outputModuleIndex ];
		}
	}
}
