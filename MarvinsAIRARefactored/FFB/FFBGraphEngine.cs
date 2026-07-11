
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Evaluates one <see cref="FFBGraph"/>. Two instances exist at runtime: a live engine (driven by the 360 Hz
/// worker thread) and a preview engine (driven by the dispatcher over a recording). Both are built from the
/// same graph model but keep independent state.
/// <para>Threading: structure edits build a brand-new engine on the UI thread and the caller swaps a volatile
/// reference so the 360 Hz reader picks it up on its next tick. Knob edits arrive via <see cref="SetValue"/> as
/// atomic single-float writes the reader tolerates. The arrays (<c>_modules</c>, <c>_signals</c>) are never
/// mutated in place after Rebuild — always rebuild-and-swap.</para>
/// </summary>
public sealed class FFBGraphEngine
{
	private FFBModule[] _modules = [];
	private FFBModuleDescriptor[] _descriptors = [];
	private float[] _signals = [];
	private int _fallbackSourceIndex;

	private readonly Dictionary<string, int> _indexById = new( StringComparer.Ordinal );

	// results, read after Process
	public float MainOutput;              // Output module result (normalized, pre-fade)
	public float VibrationOutput;         // sum of generator modules (normalized)

	// published per-tick / edit-time aggregates
	public float CurbProtectionFactor;    // set by the CurbProtection module's PrePass (0 when absent/inactive)
	public bool CrashProtectionActive;
	public bool CurbProtectionActive;

	// aggregates refreshed on Rebuild/SetValue, read by Simulator triggers (thresholds) and RacingWheel (prediction)
	public float CrashLongGForceThreshold = 20f;
	public float CrashLatGForceThreshold = 20f;
	public float CurbShockVelocityThreshold = 0f;
	public RacingWheel.PredictionMode PredictionMode = RacingWheel.PredictionMode.Disabled;
	public float PredictionBlend = 0f;

	/// <summary>The graph model this engine was last built from (for the preview replay / editor).</summary>
	public FFBGraph? Graph { get; private set; }

	/// <summary>
	/// Rebuild the engine from a graph model, plus the optional vibration graph whose generator modules are
	/// appended after the main chain (generators have no signal inputs and only feed the vibration bus, so
	/// their position in the evaluation order is irrelevant). UI thread only; allocates the module and signal
	/// arrays. A module may only reference an EARLIER module's output (or a source); forward/dangling references
	/// fall back to the 360 Hz source, which also covers reorder/remove edges.
	/// </summary>
	public void Rebuild( FFBGraph graph, FFBGraph? vibrationGraph = null )
	{
		Graph = graph;

		var allModules = new List<FFBModuleData>( graph.Modules );

		if ( vibrationGraph != null )
		{
			allModules.AddRange( vibrationGraph.Modules );
		}

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
		}

		_modules = modules;
		_descriptors = descriptors;
		_signals = new float[ moduleCount ];

		RefreshAggregates();
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
		CurbProtectionFactor = 0f;
		CrashProtectionActive = false;
		CurbProtectionActive = false;
	}

	/// <summary>Recompute the edit-time aggregates (thresholds, prediction mode/blend) by asking each module to publish.</summary>
	public void RefreshAggregates()
	{
		CrashLongGForceThreshold = 20f;
		CrashLatGForceThreshold = 20f;
		CurbShockVelocityThreshold = 0f;
		PredictionMode = RacingWheel.PredictionMode.Disabled;
		PredictionBlend = 0f;

		for ( var i = 0; i < _modules.Length; i++ )
		{
			_modules[ i ].PublishAggregates();
		}
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

		RefreshAggregates();
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
	/// Hot path. PrePass all modules (so protection timers / the curb factor exist before the signal loop —
	/// reproducing the old ordering where curb/crash advanced before the algorithm ran), then evaluate every
	/// module in list order. Generators feed the normalized vibration bus; the Output module publishes
	/// <see cref="MainOutput"/>.
	/// </summary>
	public void Process( in FFBTickContext ctx )
	{
		var modules = _modules;
		var descriptors = _descriptors;
		var signals = _signals;

		VibrationOutput = 0f;
		CurbProtectionFactor = 0f;
		CrashProtectionActive = false;
		CurbProtectionActive = false;

		for ( var i = 0; i < modules.Length; i++ )
		{
			modules[ i ].PrePass( in ctx );
		}

		for ( var i = 0; i < modules.Length; i++ )
		{
			var module = modules[ i ];

			var inputA = signals[ module.InputAIndex ];
			var inputB = signals[ module.InputBIndex ];

			if ( module.IsGenerator )
			{
				signals[ i ] = 0f;

				if ( module.Enabled )
				{
					VibrationOutput += module.Process( in ctx, inputA, inputB );
				}
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

			if ( module.IsOutput )
			{
				MainOutput = signals[ i ];
			}
		}
	}
}
