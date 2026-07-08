
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Evaluates one <see cref="FFBStack"/>. Two instances exist at runtime: a live engine (driven by the 360 Hz
/// worker thread) and a preview engine (driven by the dispatcher over a recording). Both are built from the
/// same stack model but keep independent state.
/// <para>Threading: structure edits build a brand-new engine on the UI thread and the caller swaps a volatile
/// reference so the 360 Hz reader picks it up on its next tick. Knob edits arrive via <see cref="SetValue"/> as
/// atomic single-float writes the reader tolerates. The arrays (<c>_modules</c>, <c>_signals</c>) are never
/// mutated in place after Rebuild — always rebuild-and-swap.</para>
/// </summary>
public sealed class FFBStackEngine
{
	private FFBModule[] _modules = [];
	private FFBModuleDescriptor[] _descriptors = [];
	private float[] _signals = [];
	private int _source360Index;

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

	/// <summary>The stack model this engine was last built from (for the preview replay / editor).</summary>
	public FFBStack? Stack { get; private set; }

	/// <summary>
	/// Rebuild the engine from a stack model. UI thread only; allocates the module and signal arrays. A module
	/// may only reference an EARLIER module's output (or a source); forward/dangling references fall back to the
	/// 360 Hz source, which also covers reorder/remove edges.
	/// </summary>
	public void Rebuild( FFBStack stack )
	{
		Stack = stack;

		var moduleCount = stack.Modules.Count;

		var modules = new FFBModule[ moduleCount ];
		var descriptors = new FFBModuleDescriptor[ moduleCount ];

		_indexById.Clear();

		for ( var i = 0; i < moduleCount; i++ )
		{
			_indexById[ stack.Modules[ i ].ModuleId ] = i;
		}

		// locate the 360 Hz source (the fallback target for invalid input references)

		_source360Index = _indexById.TryGetValue( FFBStack.Source360ModuleId, out var source360Index ) ? source360Index : 0;

		for ( var i = 0; i < moduleCount; i++ )
		{
			var model = stack.Modules[ i ];

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

	/// <summary>An input reference is valid only if it resolves to an EARLIER module; otherwise fall back to the 360 Hz source.</summary>
	private int ResolveInput( string moduleId, int consumerIndex )
	{
		if ( _indexById.TryGetValue( moduleId, out var index ) && ( index < consumerIndex ) )
		{
			return index;
		}

		return _source360Index;
	}

	/// <summary>Zero every module's internal state (used before a preview replay and on stack/context change).</summary>
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

	/// <summary>
	/// Hot path. PrePass all modules (so protection timers / the curb factor exist before the signal loop —
	/// reproducing the old ordering where curb/crash advanced before the algorithm ran), then evaluate every
	/// module in list order. Generators feed the normalized vibration bus; the Output module publishes
	/// <see cref="MainOutput"/>.
	/// </summary>
	public void Process( in FFBTickContext ctx )
	{
		var modules = _modules;
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
			else if ( module.Enabled || module.IsSource )
			{
				signals[ i ] = module.Process( in ctx, inputA, inputB );
			}
			else
			{
				signals[ i ] = inputA;   // disabled module passes input A through
			}

			if ( module.IsOutput )
			{
				MainOutput = signals[ i ];
			}
		}
	}
}
