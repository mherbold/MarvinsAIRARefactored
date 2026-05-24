# Settings & Per-Context System

## Related Source Files
- `DataContext/Settings.cs` — All persistent user settings (serialized to XML)
- `DataContext/ContextSettings.cs` — Per-context override values (active context's current values)
- `DataContext/ContextSwitches.cs` — Flags controlling which axes create a context for a setting
- `DataContext/Context.cs` — Immutable context key (wheelbase GUID + car + track + config + wet/dry)
- `DataContext/DataContext.cs` — Global singleton root (`INotifyPropertyChanged`)
- `Components/SettingsFile.cs` — XML serialization and persistence
- `Classes/Serializer.cs` — `XmlSerializer` helpers
- `Classes/SerializableDictionary.cs` — XML-serializable dictionary used by `ContextSettingsDictionary`
- `Windows/UpdateContextSwitchesWindow.xaml/.cs` — UI for configuring which axes create a context

---

## Settings Persistence

Settings are serialized to XML at:
```
My Documents\MarvinsAIRA Refactored\Settings.xml
```
`SettingsFile` queues serialization (not immediate) to avoid excessive I/O. Serialization is performed by `Serializer` using `XmlSerializer`.

---

## Per-Context Settings System

Settings can be **overridden per context**. A `Context` is an immutable key composed of:

| Axis | Type | Example |
|---|---|---|
| Wheelbase GUID | `Guid` | DirectInput device GUID |
| Car name | `string` | `"Ferrari 488 GT3 Evo"` |
| Track name | `string` | `"Spa-Francorchamps"` |
| Track configuration name | `string` | `"Grand Prix"` |
| Wet/dry | `bool` | `true` = wet |

`ContextSwitches` is a 5-boolean struct (in constructor order: wheelbase, car, track, trackConfig, wetDry) that controls which axes are included when building the `Context` key for a given setting. This lets each setting have independently configurable granularity.

### How `UpdateSettings()` Works

`Settings.UpdateSettings()` is called whenever the active context may have changed (simulator connect, weather change, car/track change, etc.). It:
1. Uses **reflection** to find all `*ContextSwitches` properties in `Settings`.
2. Builds the appropriate `Context` key for each property using the active values and the flags in its `ContextSwitches`.
3. Looks up (or creates) the matching entry in `ContextSettingsDictionary`.
4. Copies the stored context value into the matching `ContextSettings` property — or writes the current setting value back if no override exists yet.

**Components read from `ContextSettings`**, not directly from `Settings`, so they always use the active context's value.

---

## Adding a New Per-Context Setting — Checklist

### 1. `DataContext/Settings.cs`
Add the setting and its companion `ContextSwitches` property immediately after each other:
```csharp
public float MySetting { get; set; } = 5.0f;
public ContextSwitches MySettingContextSwitches { get; set; } = new( false, true, false, false, false );
```
- Default `new( false, true, false, false, false )` = per-car context only.
- Place them in the **same order as the controls appear in the UI page** (left-to-right, top-to-bottom).

### 2. `DataContext/ContextSettings.cs`
Add a matching auto-property with the **same name and same default**:
```csharp
public float MySetting { get; set; } = 5.0f;
```
- Order must match the order in `Settings.cs`.
- Only settings that have a `ContextSwitches` companion belong here.

### 3. XAML page
Add the `ContextSwitches` binding to the control:
```xml
<controls:MairaKnob Value="{Binding Settings.MySetting, Mode=TwoWay}"
					 ContextSwitches="{Binding Settings.MySettingContextSwitches}" />
```

### Rules
| Rule | Detail |
|---|---|
| Companion name | Must be exactly `[SettingName]ContextSwitches` — reflection finds it by this convention |
| `ContextSettings` name | Must exactly match the setting name in `Settings.cs` |
| `ContextSwitches` XAML binding | Do **not** add `Mode=TwoWay` — it is a reference type modified in-place |
| Default axes | Always `new( false, true, false, false, false )` (per-car only) unless there is a specific reason |
| Grouping | Place companion directly below its paired setting in the same `#region` in both files |

---

## `Settings.cs` Property Ordering Rule

Settings in `Settings.cs` must appear in the **same order as the controls appear in the UI page**:
- Read columns **left-to-right**, then **top-to-bottom**.

This order must be mirrored in `ContextSettings.cs` for any setting that has a `ContextSwitches` companion.

---

## Right-Click Context Configuration

`MairaKnob` and `MairaSwitch` detect a right-click on their label. If `ContextSwitches != null`, they open `UpdateContextSwitchesWindow` so the user can toggle which axes create a unique context for that setting. This happens automatically when the `ContextSwitches` binding is present — no extra code is needed in pages.
