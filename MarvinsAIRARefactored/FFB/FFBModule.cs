
namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Abstract runtime base for every FFB module. One instance lives inside an <see cref="FFBGraphEngine"/>.
/// Setting values are resolved into the flat <see cref="_v"/> array (in effective-descriptor order, so
/// index 0 is always the reserved Enabled switch) at Configure time; the hot path reads them with no
/// dictionary lookups, no allocation, and no locks. Edit-time knob changes arrive via <see cref="SetValue"/>
/// as atomic single-float writes, which the 360 Hz reader tolerates.
/// </summary>
public abstract class FFBModule
{
	public FFBModuleData Model = null!;

	/// <summary>The engine that owns this module (set on Rebuild). Modules publish engine-level aggregates
	/// through it (protection active flags, Simulator trigger thresholds, prediction mode/blend).</summary>
	public FFBGraphEngine Owner = null!;

	protected float[] _v = [];                 // resolved setting values, effective-descriptor order (0 = Enabled)

	public int InputAIndex;                     // index into the engine signal array
	public int InputBIndex;                     // index into the engine signal array

	public bool IsGenerator { get; private set; }
	public bool IsSource { get; private set; }
	public bool IsOutput { get; private set; }

	/// <summary>Reserved Enabled switch (effective-setting index 0). Read by the engine each tick — cached as a
	/// plain bool (refreshed in Configure and SetValue) so the hot path doesn't re-test the array every read.</summary>
	public bool Enabled { get; private set; }

	/// <summary>Session-only "test this effect" override, driven by the editor's vibrate toggle — never
	/// serialized, and an engine rebuild resets it. Event-driven effect modules treat it as their trigger
	/// being continuously active, so the user can feel the effect without having to provoke it in the sim.</summary>
	public bool TestActive;

	/// <summary>
	/// Resolve descriptor + serialized model into <see cref="_v"/>. Missing keys fall back to the descriptor
	/// default (the same "missing ⇒ default" behavior as the settings overlay loader).
	/// </summary>
	public virtual void Configure( FFBModuleDescriptor descriptor, FFBModuleData model )
	{
		Model = model;

		IsGenerator = descriptor.IsGenerator;
		IsSource = descriptor.IsSource;
		IsOutput = descriptor.IsOutput;

		var effectiveSettings = descriptor.EffectiveSettings;

		_v = new float[ effectiveSettings.Length ];

		for ( var i = 0; i < effectiveSettings.Length; i++ )
		{
			var settingDescriptor = effectiveSettings[ i ];

			_v[ i ] = model.SettingValues.TryGetValue( settingDescriptor.Key, out var value ) ? value : settingDescriptor.DefaultValue;
		}

		Enabled = _v.Length > 0 && _v[ 0 ] != 0f;

		OnValuesChanged();
	}

	/// <summary>Zero all internal (per-tick recursive) state. Called on Rebuild and graph/context change.</summary>
	public abstract void Reset();

	/// <summary>
	/// Runs before the main signal loop each tick. Protection modules update their timers/factors here so
	/// downstream consumers (e.g. the curb-protection factor) exist before the signal loop reaches them,
	/// reproducing the old ordering where curb/crash timers advanced before the algorithm ran.
	/// A module that overrides this MUST also override <see cref="HasPrePass"/> to return true — the engine
	/// only dispatches PrePass to modules that advertise it.
	/// </summary>
	public virtual void PrePass( in FFBTickContext ctx ) { }

	/// <summary>True when this module overrides <see cref="PrePass"/>. Evaluated once at Rebuild, so the hot
	/// path never dispatches the no-op base PrePass to the (vast majority of) modules without one.</summary>
	public virtual bool HasPrePass => false;

	/// <summary>
	/// Recompute cached derivations of <see cref="_v"/> (filter coefficients, curve exponents, per-tick unit
	/// conversions). Called at the end of Configure and after every edit-time SetValue — never on the hot path.
	/// Each cached field must individually be a valid value: the 360 Hz reader may observe a partially-updated
	/// set for one tick (same tolerance class as raw <see cref="_v"/> reads). <see cref="TestActive"/> edits
	/// don't touch <see cref="_v"/> and don't fire this hook.
	/// </summary>
	protected virtual void OnValuesChanged() { }

	/// <summary>Compute this module's output from its (already-computed) input signals. Hot path.</summary>
	public abstract float Process( in FFBTickContext ctx, float inputA, float inputB );

	/// <summary>
	/// Publish any engine-level aggregates this module owns (e.g. protection thresholds read by Simulator, or
	/// the Source60 prediction mode/blend read by RacingWheel). Called on Rebuild and after every edit-time
	/// SetValue, never on the hot path.
	/// </summary>
	public virtual void PublishAggregates() { }

	/// <summary>Edit-time atomic write of a single resolved setting value (effective-list index), followed by a
	/// refresh of the cached Enabled flag and the module's cached value derivations.</summary>
	public void SetValue( int settingIndex, float value )
	{
		if ( ( settingIndex >= 0 ) && ( settingIndex < _v.Length ) )
		{
			_v[ settingIndex ] = value;

			if ( settingIndex == 0 )
			{
				Enabled = _v[ 0 ] != 0f;
			}

			OnValuesChanged();
		}
	}
}
