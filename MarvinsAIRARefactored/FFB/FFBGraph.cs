
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
	public string Description { get; set; } = "";                       // override of the node's description (rides export/import); empty = the module type's localized default shows; on built-in graphs the override is localized via FFBNodeDescription* keys with this text as the English fallback
	public string InputAModuleId { get; set; } = FFBGraph.Source360ModuleId;
	public string InputBModuleId { get; set; } = FFBGraph.Source360ModuleId;
	public SerializableDictionary<string, float> SettingValues { get; set; } = [];  // bools 0/1, choices as index
	public List<string> PinnedSettings { get; set; } = [];              // setting keys surfaced as the graph's quick controls above the editor
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
			Description = Description,
			InputAModuleId = InputAModuleId,
			InputBModuleId = InputBModuleId,
			NodeX = NodeX,
			NodeY = NodeY
		};

		foreach ( var pair in SettingValues )
		{
			clone.SettingValues[ pair.Key ] = pair.Value;
		}

		clone.PinnedSettings.AddRange( PinnedSettings );

		return clone;
	}
}

/// <summary>
/// A named, ordered chain of modules. The last slot is always the fixed Output module (non-removable). Sources
/// are ordinary optional modules now — added and removed at will, limited to one instance of each source type
/// per graph — except that a graph always retains at least one source (removing the last one re-adds the 360 Hz
/// source, since the Output module always needs an input). Modules may only reference the output of an
/// <em>earlier</em> module (or a source) — the list order is the evaluation order. The editor allows free
/// acyclic wiring and calls <see cref="FFBGraphTopology.SortTopologically"/> after every structure edit to
/// restore that invariant.
/// </summary>
public class FFBGraph
{
	public const string Source60ModuleId = "Source60";
	public const string Source360ModuleId = "Source360";
	public const string SourceLFEModuleId = "SourceLFE";
	public const string SourceWheelVelocityModuleId = "SourceWheelVelocity";
	public const string SourceSoftLockModuleId = "SourceSoftLock";
	public const string SourceWheelCenteringModuleId = "SourceWheelCentering";
	public const string OutputModuleId = "Output";

	// Stable identity that survives export/import (and renames). Written into exported files so a re-import of the
	// same graph is recognized as an update to the live module settings rather than a brand-new copy. Unique
	// app-wide: user graphs get a random id at creation, copies of an existing graph mint a fresh id, and built-ins
	// carry a fixed id baked into their shipped file so the same built-in matches across installs. The per-context
	// module value overlay is keyed by this id (see FFBGraphValues), so uniqueness is what keeps one graph's values
	// out of another's. Empty on legacy graphs/files - assigned lazily on load/import.
	public string GraphId { get; set; } = "";
	public string Name { get; set; } = "";

	// Free-text description shown below the graph selector (rides export/import). For BUILT-IN graphs the UI
	// resolves a localization key derived from the graph name first (so shipped descriptions arrive translated)
	// and only falls back to this stored text — see FFBGraphViewModel.GraphDescription.
	public string Description { get; set; } = "";

	public bool IsBuiltIn { get; set; } = false;
	public List<FFBModuleData> Modules { get; set; } = [];   // dependency order, last = Output

	/// <summary>
	/// Canonical module id for a source type. Sources are one-per-graph, so each source type has a single
	/// well-known id that survives remove/re-add — all the fallback and preview plumbing keys off these ids.
	/// </summary>
	public static string CanonicalSourceId( string sourceTypeKey )
	{
		return sourceTypeKey switch
		{
			FFBModuleRegistry.Source60HzType => Source60ModuleId,
			FFBModuleRegistry.Source360HzType => Source360ModuleId,
			_ => sourceTypeKey   // the newer source type keys are their own canonical ids
		};
	}

	/// <summary>
	/// True for the well-known module ids every graph shares — the canonical one-per-graph source ids and the
	/// fixed Output id — as opposed to a user module's per-graph GUID. Anything keyed by module id alone (the
	/// knob button mappings) must not treat a shared id as owned by a single graph.
	/// </summary>
	public static bool IsSharedModuleId( string moduleId )
	{
		if ( moduleId == OutputModuleId )
		{
			return true;
		}

		foreach ( var descriptor in FFBModuleRegistry.All )
		{
			if ( descriptor.IsSource && ( CanonicalSourceId( descriptor.TypeKey ) == moduleId ) )
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Build an empty graph containing only the 360 Hz source and the trailing Output module, wired
	/// Source360 -> Output. New user graphs and every built-in start from this shape; further sources are
	/// added at will.
	/// </summary>
	public static FFBGraph CreateEmpty( string name, bool isBuiltIn = false )
	{
		var graph = new FFBGraph { GraphId = Guid.NewGuid().ToString( "N" ), Name = name, IsBuiltIn = isBuiltIn };

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
		var clone = new FFBGraph { GraphId = GraphId, Name = Name, Description = Description, IsBuiltIn = IsBuiltIn };

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
