
namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Pure structural helpers for <see cref="FFBGraph"/>: reachability (cycle prevention for the editor's
/// eligible-input lists), the stable topological sort that restores the "inputs reference earlier modules"
/// invariant after free re-wiring, and the automatic node layout used the first time a graph is shown in the
/// node editor. No engine or UI dependencies — everything operates on the serializable model only.
/// </summary>
public static class FFBGraphTopology
{
	// node box metrics shared between the auto-layout and the FFBGraphEditor control
	public const float NodeWidth = 170f;
	public const float NodeHeight = 54f;
	public const float HorizontalGap = 70f;
	public const float VerticalGap = 30f;
	public const float LayoutMargin = 20f;

	// snap-to-grid cell size — half of the node box height plus 10 px of padding
	public const float GridSize = ( NodeHeight + 10f ) / 2f;

	// the grid is offset from the canvas origin so snapped nodes never sit flush on the top/left edges. The
	// vertical home sits a few px lower than the horizontal so snapped nodes line up on the intended row baseline.
	public const float GridOriginX = 10f;
	public const float GridOriginY = 16f;

	/// <summary>
	/// The set of modules that consume <paramref name="moduleId"/>'s output, directly or transitively, following
	/// only inputs the module's descriptor actually uses. Includes <paramref name="moduleId"/> itself, so the
	/// result is exactly the set of modules that must not be offered as an input to it (a cycle would form).
	/// </summary>
	public static HashSet<string> ReachableFrom( FFBGraph graph, string moduleId )
	{
		var reachable = new HashSet<string>( StringComparer.Ordinal ) { moduleId };

		var grew = true;

		while ( grew )
		{
			grew = false;

			foreach ( var module in graph.Modules )
			{
				if ( reachable.Contains( module.ModuleId ) )
				{
					continue;
				}

				var signalInputCount = FFBModuleRegistry.TryGet( module.ModuleType )?.SignalInputCount ?? 0;

				if ( ( ( signalInputCount >= 1 ) && reachable.Contains( module.InputAModuleId ) ) || ( ( signalInputCount >= 2 ) && reachable.Contains( module.InputBModuleId ) ) )
				{
					reachable.Add( module.ModuleId );

					grew = true;
				}
			}
		}

		return reachable;
	}

	/// <summary>
	/// The module every dangling or invalid input reference falls back to: the 360 Hz source when present, then
	/// the 60 Hz source, then any other source already on the graph — null only when the graph has no source at
	/// all (see <see cref="EnsureFallbackSource"/>).
	/// </summary>
	public static string? FallbackSourceModuleId( FFBGraph graph )
	{
		var source = graph.Modules.FirstOrDefault( module => module.ModuleType == FFBModuleRegistry.Source360HzType )
			?? graph.Modules.FirstOrDefault( module => module.ModuleType == FFBModuleRegistry.Source60HzType )
			?? graph.Modules.FirstOrDefault( module => FFBModuleRegistry.TryGet( module.ModuleType )?.IsSource == true );

		return source?.ModuleId;
	}

	/// <summary>
	/// Sources are optional now, but any module with a signal input (the Output module always has one) needs a
	/// fallback target — when a graph has no source at all (e.g. the last one was just removed), the 360 Hz
	/// source is added back automatically, placed below the existing nodes. Returns the fallback source id.
	/// </summary>
	public static string EnsureFallbackSource( FFBGraph graph )
	{
		var fallbackModuleId = FallbackSourceModuleId( graph );

		if ( fallbackModuleId != null )
		{
			return fallbackModuleId;
		}

		var nodeY = LayoutMargin;

		foreach ( var module in graph.Modules )
		{
			nodeY = Math.Max( nodeY, module.NodeY + NodeHeight + VerticalGap );
		}

		graph.Modules.Insert( 0, new FFBModuleData( FFBGraph.Source360ModuleId, FFBModuleRegistry.Source360HzType )
		{
			NodeX = LayoutMargin,
			NodeY = nodeY
		} );

		return FFBGraph.Source360ModuleId;
	}

	/// <summary>
	/// Stable topological sort of the module list into <c>[sources, main-signal modules in dependency order
	/// (current relative order as the tiebreak), generators, Output]</c>, followed by a repair pass that resets
	/// any remaining forward or dangling input reference to the fallback source (the engine falls back the same
	/// way). A source-less graph gets the 360 Hz source re-added first. Call after every structure edit
	/// (re-wire, add, remove) and before rebuilding the engines.
	/// </summary>
	public static void SortTopologically( FFBGraph graph )
	{
		EnsureFallbackSource( graph );

		var sources = new List<FFBModuleData>();
		var main = new List<FFBModuleData>();
		var generators = new List<FFBModuleData>();
		var outputs = new List<FFBModuleData>();

		foreach ( var module in graph.Modules )
		{
			var descriptor = FFBModuleRegistry.TryGet( module.ModuleType );

			if ( descriptor?.IsSource == true )
			{
				sources.Add( module );
			}
			else if ( descriptor?.IsOutput == true )
			{
				outputs.Add( module );
			}
			else if ( descriptor?.IsGenerator == true )
			{
				generators.Add( module );
			}
			else
			{
				main.Add( module );
			}
		}

		// Kahn's algorithm over the main-signal modules only; sources are always satisfied, and a dependency on
		// anything else (generator, output, missing module) is ignored here — the repair pass resets it below.
		var mainIds = new HashSet<string>( main.Select( module => module.ModuleId ), StringComparer.Ordinal );
		var placed = new HashSet<string>( StringComparer.Ordinal );
		var sorted = new List<FFBModuleData>();

		while ( main.Count > 0 )
		{
			var index = main.FindIndex( module =>
			{
				var signalInputCount = FFBModuleRegistry.TryGet( module.ModuleType )?.SignalInputCount ?? 0;

				var inputAReady = ( signalInputCount < 1 ) || !mainIds.Contains( module.InputAModuleId ) || placed.Contains( module.InputAModuleId );
				var inputBReady = ( signalInputCount < 2 ) || !mainIds.Contains( module.InputBModuleId ) || placed.Contains( module.InputBModuleId );

				return inputAReady && inputBReady;
			} );

			// cycle safety net — should be unreachable (the editor never offers a cyclic wiring); break it here
			// and let the repair pass reset the offending reference
			if ( index < 0 )
			{
				index = 0;
			}

			var next = main[ index ];

			main.RemoveAt( index );
			sorted.Add( next );
			placed.Add( next.ModuleId );
		}

		graph.Modules.Clear();
		graph.Modules.AddRange( sources );
		graph.Modules.AddRange( sorted );
		graph.Modules.AddRange( generators );
		graph.Modules.AddRange( outputs );

		RepairInputReferences( graph );
	}

	/// <summary>
	/// Reset any input reference that points at a missing module or at a module that is not earlier in the list
	/// back to the fallback source (360 Hz → 60 Hz → any other source). The engine's <c>ResolveInput</c> falls
	/// back identically, so this only makes the stored model match what the engine would actually do.
	/// </summary>
	public static void RepairInputReferences( FFBGraph graph )
	{
		var fallbackModuleId = FallbackSourceModuleId( graph ) ?? FFBGraph.Source360ModuleId;

		var indexById = new Dictionary<string, int>( StringComparer.Ordinal );

		for ( var i = 0; i < graph.Modules.Count; i++ )
		{
			indexById[ graph.Modules[ i ].ModuleId ] = i;
		}

		for ( var i = 0; i < graph.Modules.Count; i++ )
		{
			var module = graph.Modules[ i ];

			if ( !indexById.TryGetValue( module.InputAModuleId, out var indexA ) || ( indexA >= i ) )
			{
				module.InputAModuleId = fallbackModuleId;
			}

			if ( !indexById.TryGetValue( module.InputBModuleId, out var indexB ) || ( indexB >= i ) )
			{
				module.InputBModuleId = fallbackModuleId;
			}
		}
	}

	/// <summary>Snap a single coordinate to the nearest grid point about the given axis origin (used live while
	/// dragging with snap enabled). Pass <see cref="GridOriginX"/> for X and <see cref="GridOriginY"/> for Y.</summary>
	public static double Snap( double value, double origin )
	{
		return Math.Max( origin, Math.Round( ( value - origin ) / GridSize ) * GridSize + origin );
	}

	/// <summary>Snap every canvas node's upper-left corner to the nearest grid point (the snap-to-grid toggle
	/// was just switched on). Generators are skipped — they never appear on the node canvas.</summary>
	public static void SnapAllToGrid( FFBGraph graph )
	{
		foreach ( var module in graph.Modules )
		{
			if ( FFBModuleRegistry.TryGet( module.ModuleType )?.IsGenerator == true )
			{
				continue;
			}

			module.NodeX = (float) Snap( module.NodeX, GridOriginX );
			module.NodeY = (float) Snap( module.NodeY, GridOriginY );
		}
	}

	/// <summary>True when every non-generator module still sits at 0,0 — i.e. the graph has never been laid out
	/// in the node editor (legacy data, a freshly migrated built-in, or a reset built-in).</summary>
	public static bool NeedsAutoLayout( FFBGraph graph )
	{
		foreach ( var module in graph.Modules )
		{
			if ( FFBModuleRegistry.TryGet( module.ModuleType )?.IsGenerator == true )
			{
				continue;
			}

			if ( ( module.NodeX != 0f ) || ( module.NodeY != 0f ) )
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Layered left-to-right auto-layout: each non-generator module's column is its longest path from the sources
	/// (sources leftmost, Output forced rightmost), and modules sharing a column are stacked vertically, centered
	/// against the tallest column. Generators are skipped — they never appear on the node canvas.
	/// </summary>
	public static void ApplyAutoLayout( FFBGraph graph )
	{
		// depth per module — modules appear after their inputs in list order (SortTopologically guarantees it),
		// so a single forward pass resolves every longest path
		var depthById = new Dictionary<string, int>( StringComparer.Ordinal );

		var maxDepth = 0;

		foreach ( var module in graph.Modules )
		{
			var descriptor = FFBModuleRegistry.TryGet( module.ModuleType );

			if ( descriptor?.IsGenerator == true )
			{
				continue;
			}

			var depth = 0;

			if ( descriptor?.IsSource != true )
			{
				var signalInputCount = descriptor?.SignalInputCount ?? 0;

				if ( ( signalInputCount >= 1 ) && depthById.TryGetValue( module.InputAModuleId, out var depthA ) )
				{
					depth = Math.Max( depth, depthA + 1 );
				}

				if ( ( signalInputCount >= 2 ) && depthById.TryGetValue( module.InputBModuleId, out var depthB ) )
				{
					depth = Math.Max( depth, depthB + 1 );
				}

				depth = Math.Max( depth, 1 );
			}

			depthById[ module.ModuleId ] = depth;

			maxDepth = Math.Max( maxDepth, depth );
		}

		// the Output module always gets its own rightmost column, even when a parallel branch runs deeper
		foreach ( var module in graph.Modules )
		{
			if ( FFBModuleRegistry.TryGet( module.ModuleType )?.IsOutput == true )
			{
				depthById[ module.ModuleId ] = maxDepth + 1;
			}
		}

		// stack each column top-down, centering shorter columns against the tallest one
		var rowsPerDepth = new Dictionary<int, int>();

		foreach ( var pair in depthById )
		{
			rowsPerDepth[ pair.Value ] = rowsPerDepth.TryGetValue( pair.Value, out var rows ) ? rows + 1 : 1;
		}

		var tallestColumn = rowsPerDepth.Count > 0 ? rowsPerDepth.Values.Max() : 1;
		var nextRowPerDepth = new Dictionary<int, int>();

		foreach ( var module in graph.Modules )
		{
			if ( !depthById.TryGetValue( module.ModuleId, out var depth ) )
			{
				continue;
			}

			var row = nextRowPerDepth.TryGetValue( depth, out var nextRow ) ? nextRow : 0;

			nextRowPerDepth[ depth ] = row + 1;

			var columnOffset = ( tallestColumn - rowsPerDepth[ depth ] ) * ( NodeHeight + VerticalGap ) / 2f;

			module.NodeX = LayoutMargin + depth * ( NodeWidth + HorizontalGap );
			module.NodeY = LayoutMargin + columnOffset + row * ( NodeHeight + VerticalGap );
		}
	}
}
