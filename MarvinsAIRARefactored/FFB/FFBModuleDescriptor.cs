
namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// How a module setting is presented in the graph editor and how its stored float is interpreted.
/// </summary>
public enum FFBSettingType
{
	Knob,    // continuous value edited with a MairaKnob
	Switch,  // boolean, stored as 0 or 1
	Choice   // enumerated option, stored as the option index
}

/// <summary>
/// Describes one setting of a module: its stable serialized key, editor metadata, range, and step sizes.
/// Values are always stored as float in <see cref="FFBModuleData.SettingValues"/> (bools as 0/1, choices as
/// their index). The descriptor is the single source of truth for defaults, ranges, and formatting.
/// </summary>
public sealed class FFBSettingDescriptor
{
	public required string Key;                 // stable, serialized (e.g. "Boost")
	public required string LocalizationKey;     // reuse existing keys where they exist
	public FFBSettingType Type = FFBSettingType.Knob;
	public float Min;
	public float Max = 1f;
	public float DefaultValue;
	public float ClickStepSize = 0.01f;         // +/- button increment (set deliberately per setting)
	public float DragStepSize = 1f / 5760f;     // drag increment; rule of thumb = (Max - Min) / 5760
	public string[]? ChoiceLocalizationKeys;    // Choice type: stored value = index into this array
	public Func<FFBFormatContext, string>? FormatValue;   // builds the ValueString (unit/percent); null => plain number
	public bool ShowCurve;                      // MairaKnob curve visualization

	/// <summary>Clamp a raw value to this setting's range (choices/switches clamp to their discrete span).</summary>
	public float Clamp( float value )
	{
		return Math.Clamp( value, Min, Max );
	}
}

/// <summary>
/// Describes a module type: its stable registry key, signal arity, family flags, declared settings, and the
/// factory that produces a runtime instance. A reserved "Enabled" switch is prepended to every module's
/// effective setting list automatically (see <see cref="EffectiveSettings"/>), so module authors never
/// declare it and it participates in per-context values like any other setting.
/// </summary>
public sealed class FFBModuleDescriptor
{
	public required string TypeKey;             // stable, serialized (e.g. "DetailBooster")
	public required string LocalizationKey;
	public int SignalInputCount;                // 0 (generator/source), 1, or 2
	public string? InputALocalizationKey;
	public string? InputBLocalizationKey;
	public bool IsGenerator;                    // contributes to the normalized vibration bus, not the main chain
	public bool IsSource;                       // fixed source module (emits a ctx torque sample)
	public bool IsOutput;                       // fixed final module (normalizes + curve/limiter/clamp)
	public required FFBSettingDescriptor[] Settings;   // declared settings (WITHOUT the reserved Enabled)
	public required Func<FFBModule> CreateRuntime;

	/// <summary>The reserved "Enabled" switch, prepended to every module. Index 0 of the effective list.</summary>
	public static readonly FFBSettingDescriptor EnabledSetting = new()
	{
		Key = "Enabled",
		LocalizationKey = "FFBSettingEnabled",
		Type = FFBSettingType.Switch,
		Min = 0f,
		Max = 1f,
		DefaultValue = 1f,
		ClickStepSize = 1f,
		DragStepSize = 1f
	};

	private FFBSettingDescriptor[]? _effectiveSettings;

	/// <summary>Declared settings with the reserved Enabled switch prepended (index 0). Cached.</summary>
	public FFBSettingDescriptor[] EffectiveSettings
	{
		get
		{
			if ( _effectiveSettings == null )
			{
				_effectiveSettings = new FFBSettingDescriptor[ Settings.Length + 1 ];
				_effectiveSettings[ 0 ] = EnabledSetting;

				Array.Copy( Settings, 0, _effectiveSettings, 1, Settings.Length );
			}

			return _effectiveSettings;
		}
	}

	/// <summary>Find the effective-list index of a setting key (0 = Enabled), or -1 if absent.</summary>
	public int IndexOfSetting( string key )
	{
		var effectiveSettings = EffectiveSettings;

		for ( var i = 0; i < effectiveSettings.Length; i++ )
		{
			if ( effectiveSettings[ i ].Key == key )
			{
				return i;
			}
		}

		return -1;
	}
}
