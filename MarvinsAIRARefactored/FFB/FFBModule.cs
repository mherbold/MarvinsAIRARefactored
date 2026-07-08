
namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Abstract runtime base for every FFB module. One instance lives inside an <see cref="FFBStackEngine"/>.
/// Setting values are resolved into the flat <see cref="_v"/> array (in effective-descriptor order, so
/// index 0 is always the reserved Enabled switch) at Configure time; the hot path reads them with no
/// dictionary lookups, no allocation, and no locks. Edit-time knob changes arrive via <see cref="SetValue"/>
/// as atomic single-float writes, which the 360 Hz reader tolerates.
/// </summary>
public abstract class FFBModule
{
	public FFBModuleData Model = null!;

	/// <summary>The engine that owns this module (set on Rebuild). Curb-consuming modules read
	/// <see cref="FFBStackEngine.CurbProtectionFactor"/> from it, exactly where the old code read
	/// curbProtectionLerpFactor.</summary>
	public FFBStackEngine Owner = null!;

	protected float[] _v = [];                 // resolved setting values, effective-descriptor order (0 = Enabled)

	public int InputAIndex;                     // index into the engine signal array
	public int InputBIndex;                     // index into the engine signal array

	public bool IsGenerator { get; private set; }
	public bool IsSource { get; private set; }
	public bool IsOutput { get; private set; }

	/// <summary>Reserved Enabled switch (effective-setting index 0). Read by the engine each tick.</summary>
	public bool Enabled => _v.Length > 0 && _v[ 0 ] != 0f;

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
	}

	/// <summary>Zero all internal (per-tick recursive) state. Called on Rebuild and stack/context change.</summary>
	public abstract void Reset();

	/// <summary>
	/// Runs before the main signal loop each tick. Protection modules update their timers/factors here so
	/// downstream consumers (e.g. the curb-protection factor) exist before the signal loop reaches them,
	/// reproducing the old ordering where curb/crash timers advanced before the algorithm ran.
	/// </summary>
	public virtual void PrePass( in FFBTickContext ctx ) { }

	/// <summary>Compute this module's output from its (already-computed) input signals. Hot path.</summary>
	public abstract float Process( in FFBTickContext ctx, float inputA, float inputB );

	/// <summary>
	/// Publish any engine-level aggregates this module owns (e.g. protection thresholds read by Simulator, or
	/// the Source60 prediction mode/blend read by RacingWheel). Called on Rebuild and after every edit-time
	/// SetValue, never on the hot path.
	/// </summary>
	public virtual void PublishAggregates() { }

	/// <summary>Edit-time atomic write of a single resolved setting value (effective-list index).</summary>
	public void SetValue( int settingIndex, float value )
	{
		if ( ( settingIndex >= 0 ) && ( settingIndex < _v.Length ) )
		{
			_v[ settingIndex ] = value;
		}
	}
}
