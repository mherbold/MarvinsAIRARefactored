
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Serializable model of a single module placed in a graph: which module type it is, where its two signal
/// inputs come from (module IDs of earlier nodes, or a source), and its baseline setting values. Per-context
/// value overrides are stored separately (keyed by "{ModuleId}/{settingKey}"), so this baseline is the
/// graph's default tuning.
/// </summary>
public class FFBModuleData
{
	public string ModuleId { get; set; } = "";                          // stable & unique app-wide
	public string ModuleType { get; set; } = "";                        // registry key, e.g. "DetailBooster"
	public string InputAModuleId { get; set; } = FFBGraph.Source360ModuleId;
	public string InputBModuleId { get; set; } = FFBGraph.Source360ModuleId;
	public SerializableDictionary<string, float> SettingValues { get; set; } = [];  // bools 0/1, choices as index
	public float NodeX { get; set; } = 0f;                              // node editor canvas position; 0,0 everywhere = needs auto-layout
	public float NodeY { get; set; } = 0f;

	public FFBModuleData() { }

	public FFBModuleData( string moduleId, string moduleType )
	{
		ModuleId = moduleId;
		ModuleType = moduleType;
	}

	/// <summary>Deep copy — used when creating a new user graph from the current one, or resetting a built-in.</summary>
	public FFBModuleData Clone()
	{
		var clone = new FFBModuleData( ModuleId, ModuleType )
		{
			InputAModuleId = InputAModuleId,
			InputBModuleId = InputBModuleId,
			NodeX = NodeX,
			NodeY = NodeY
		};

		foreach ( var pair in SettingValues )
		{
			clone.SettingValues[ pair.Key ] = pair.Value;
		}

		return clone;
	}
}

/// <summary>
/// A named, ordered chain of modules. Slots 0 and 1 are always the two fixed sources (60 Hz / 360 Hz) and the
/// last slot is always the fixed Output module; those three are non-removable. Modules may only reference the
/// output of an <em>earlier</em> module (or a source) — the list order is the evaluation order. The editor
/// allows free acyclic wiring and calls <see cref="FFBGraphTopology.SortTopologically"/> after every structure
/// edit to restore that invariant.
/// </summary>
public class FFBGraph
{
	public const string Source60ModuleId = "Source60";
	public const string Source360ModuleId = "Source360";
	public const string OutputModuleId = "Output";

	public string Name { get; set; } = "";
	public bool IsBuiltIn { get; set; } = false;
	public List<FFBModuleData> Modules { get; set; } = [];   // slots 0-1 = sources, last = Output

	/// <summary>
	/// Build an empty graph containing only the two fixed sources and the trailing Output module, wired
	/// Source360 -> Output. New user graphs and every built-in start from this shape.
	/// </summary>
	public static FFBGraph CreateEmpty( string name, bool isBuiltIn = false )
	{
		var graph = new FFBGraph { Name = name, IsBuiltIn = isBuiltIn };

		graph.Modules.Add( new FFBModuleData( Source60ModuleId, FFBModuleRegistry.Source60HzType ) );
		graph.Modules.Add( new FFBModuleData( Source360ModuleId, FFBModuleRegistry.Source360HzType ) );

		graph.Modules.Add( new FFBModuleData( OutputModuleId, FFBModuleRegistry.OutputType )
		{
			InputAModuleId = Source360ModuleId
		} );

		return graph;
	}

	/// <summary>Deep copy of the whole graph (used to seed a new graph from an existing one).</summary>
	public FFBGraph Clone()
	{
		var clone = new FFBGraph { Name = Name, IsBuiltIn = IsBuiltIn };

		foreach ( var module in Modules )
		{
			clone.Modules.Add( module.Clone() );
		}

		return clone;
	}

	/// <summary>Index of the trailing Output module, or the last module if none is flagged (should not happen).</summary>
	public int OutputIndex
	{
		get
		{
			for ( var i = Modules.Count - 1; i >= 0; i-- )
			{
				if ( Modules[ i ].ModuleType == FFBModuleRegistry.OutputType )
				{
					return i;
				}
			}

			return Modules.Count - 1;
		}
	}
}
