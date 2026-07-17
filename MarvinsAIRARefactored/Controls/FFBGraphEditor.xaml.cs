
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.FFB;

namespace MarvinsAIRARefactored.Controls;

/// <summary>
/// Node-graph editor for the FFB graph. Nodes are the module view-models — the vibration generator modules
/// included, rendered as standalone nodes with no connectors (positions live in the model via
/// <see cref="FFBModuleViewModel.NodeX"/>/<see cref="FFBModuleViewModel.NodeY"/>); wires are bezier
/// paths recomputed from those positions, so no visual-tree measuring is needed. Dragging a node writes positions
/// through the view-model and queues one settings serialization when the drag ends — node positions are
/// presentation state and never rebuild an engine. Pressing on (or near) a connector dot starts a wire drag
/// instead: a dashed pending wire follows the mouse and dropping it on a compatible connector commits through the
/// same input-selection setters as the settings-panel combos (eligibility/cycle rules, topological re-sort, engine
/// rebuild, and the deferred card rebuild are all shared with that path).
/// <para>The viewport is a height-limited clipped canvas with no scroll bar; its height is user-resizable via the
/// grab handle on the editor/preview seam (Settings.RacingWheelFFBGraphEditorHeight, bound in XAML).
/// Ctrl+wheel zooms about the cursor and left-dragging empty space pans, both of them driven by a RenderTransform
/// on the content rather than by scrolling — a scroll viewer could not pan far enough, since its range stops at the
/// content extent. <see cref="ClampPan"/> bounds that transform so at least one whole node always stays on screen.
/// Zoom and pan are in-memory view state: they are never serialized.</para>
/// </summary>
public partial class FFBGraphEditor : UserControl
{
	// Geometry shared with the node DataTemplate: the template consumes these via x:Static (node box size,
	// connector dot size, connector margins), so the XAML visuals and the wire/hit-test math here derive
	// from one set of numbers and can never drift apart. NodeWidth/NodeHeight come from FFBGraphTopology
	// (the layout/snap source of truth); the connector metrics are editor-local.
	public const double NodeWidth = FFBGraphTopology.NodeWidth;
	public const double NodeHeight = FFBGraphTopology.NodeHeight;

	// The visible border fills the whole layout box (so a snapped node sits flush on the grid), and the
	// connector dot centers sit exactly ON the box edges — each dot hangs half outside via a negative
	// half-diameter margin.
	public const double ConnectorDiameter = 10.0;
	private const double ConnectorRadius = ConnectorDiameter / 2.0;

	// connector center Y positions for the dual-input layout (input A upper-left, input B lower-left),
	// derived from the node height so they stay at the thirds when it changes
	private const double DualInputAOffsetY = NodeHeight / 3.0;
	private const double DualInputBOffsetY = NodeHeight * 2.0 / 3.0;

	// the template margins that realize those center positions for the edge-aligned 10px dots
	public static readonly Thickness SingleInputConnectorMargin = new( -ConnectorRadius, 0.0, 0.0, 0.0 );
	public static readonly Thickness DualInputAConnectorMargin = new( -ConnectorRadius, DualInputAOffsetY - ConnectorRadius, 0.0, 0.0 );
	public static readonly Thickness DualInputBConnectorMargin = new( -ConnectorRadius, 0.0, 0.0, NodeHeight - DualInputBOffsetY - ConnectorRadius );
	public static readonly Thickness OutputConnectorMargin = new( 0.0, 0.0, -ConnectorRadius, 0.0 );

	private const double MaxNodeX = 8000.0;
	private const double MaxNodeY = 3000.0;
	private const double DragThreshold = 3.0;

	// Ctrl+wheel zooms about the cursor. Per design, wheel UP zooms OUT and wheel DOWN zooms IN (the inverse of
	// the usual Windows convention) — the wheel is being pushed away from the viewer, pushing the graph away too.
	private const double MinZoom = 0.25;
	private const double MaxZoom = 2.0;
	private const double ZoomStep = 1.1;

	// the connector dots are only 10px, so grabbing and dropping use forgiving radii (nearest connector wins)
	private const double ConnectorGrabRadius = 14.0;
	private const double ConnectorDropRadius = 20.0;

	private enum ConnectorKind { None, InputA, InputB, Output }
	private enum WireDragMode { None, FromOutput, FromInput }

	private FFBGraphViewModel? _viewModel = null;
	private readonly List<FFBModuleViewModel> _subscribedModules = [];

	private FFBModuleViewModel? _dragModule = null;
	private FrameworkElement? _dragElement = null;
	private Point _dragStartPoint;
	private double _dragStartNodeX = 0.0;
	private double _dragStartNodeY = 0.0;
	private bool _dragMoved = false;

	private WireDragMode _wireDragMode = WireDragMode.None;
	private FFBModuleViewModel? _wireDragModule = null;
	private bool _wireDragIsInputB = false;
	private Path? _pendingWirePath = null;

	private bool _isPanning = false;
	private Point _panStartPoint;
	private double _panStartPanX = 0.0;
	private double _panStartPanY = 0.0;

	private bool _canvasRefreshQueued = false;

	public FFBGraphEditor()
	{
		InitializeComponent();

		// the snap-grid dot tile derives from the topology constants, so changing GridSize moves the dots and
		// the snapping together: one tile per grid cell, dot at the tile's center, and the viewport shifted by
		// half a cell so the dot centers land exactly on the origin-anchored snap points
		var gridSize = (double) FFBGraphTopology.GridSize;
		var halfGridSize = gridSize / 2.0;

		GridDots_Brush.Viewbox = new Rect( 0.0, 0.0, gridSize, gridSize );
		GridDots_Brush.Viewport = new Rect( -halfGridSize, -halfGridSize, gridSize, gridSize );

		GridDot_EllipseGeometry.Center = new Point( halfGridSize, halfGridSize );

		Loaded += FFBGraphEditor_Loaded;
		Unloaded += FFBGraphEditor_Unloaded;

		SizeChanged += ( sender, e ) => RedrawWires();
	}

	#region View-model wiring

	private void FFBGraphEditor_Loaded( object sender, RoutedEventArgs e )
	{
		if ( _viewModel != null )
		{
			return;
		}

		_viewModel = MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelGraphViewModel;

		Nodes_ItemsControl.ItemsSource = _viewModel.Modules;

		_viewModel.Modules.CollectionChanged += Modules_CollectionChanged;
		_viewModel.Wires.CollectionChanged += Wires_CollectionChanged;
		_viewModel.PropertyChanged += ViewModel_PropertyChanged;

		ResubscribeModules();
		RedrawWires();
		UpdateCanvasSize();
		UpdateSnapToGridButtonVisual();
		UpdateStructureLockVisuals();
	}

	private void FFBGraphEditor_Unloaded( object sender, RoutedEventArgs e )
	{
		if ( _viewModel == null )
		{
			return;
		}

		_viewModel.Modules.CollectionChanged -= Modules_CollectionChanged;
		_viewModel.Wires.CollectionChanged -= Wires_CollectionChanged;
		_viewModel.PropertyChanged -= ViewModel_PropertyChanged;

		UnsubscribeModules();

		Nodes_ItemsControl.ItemsSource = null;

		_viewModel = null;
	}

	private void Modules_CollectionChanged( object? sender, NotifyCollectionChangedEventArgs e )
	{
		ResubscribeModules();
		RedrawWires();
		UpdateStructureLockVisuals();

		// the VM rebuilds the collection item-by-item on every structure edit (Clear + one Add per node);
		// re-clamping the pan against those partial node sets yanks the view toward whichever nodes happen to
		// exist mid-rebuild, so defer one size/clamp pass until the rebuild has finished and the set is complete
		if ( !_canvasRefreshQueued )
		{
			_canvasRefreshQueued = true;

			Dispatcher.BeginInvoke( () =>
			{
				_canvasRefreshQueued = false;

				UpdateCanvasSize();
			}, DispatcherPriority.Background );
		}
	}

	// Built-in graphs are structurally locked — adding/removing modules and re-laying-out are disabled (node/wire
	// drags are blocked in the mouse handlers). The VM tree is rebuilt on every graph switch, so the
	// collection-changed event is a reliable refresh point.
	private void UpdateStructureLockVisuals()
	{
		var isBuiltIn = _viewModel?.IsFFBGraphBuiltIn == true;

		AddModule_MairaButton.Disabled = isBuiltIn;
		AutoLayout_MairaButton.Disabled = isBuiltIn;

		UpdateRemoveModuleButtonState();
	}

	// The remove button follows the selection: disabled when nothing is selected, when the Output module is
	// selected (it is fixed), or when the graph is a built-in (CanRemove folds all three together).
	private void UpdateRemoveModuleButtonState()
	{
		RemoveModule_MairaButton.Disabled = _viewModel?.SelectedModule?.CanRemove != true;
	}

	private void Wires_CollectionChanged( object? sender, NotifyCollectionChangedEventArgs e )
	{
		RedrawWires();
	}

	private void ViewModel_PropertyChanged( object? sender, PropertyChangedEventArgs e )
	{
		if ( e.PropertyName == nameof( FFBGraphViewModel.SelectedModule ) )
		{
			RedrawWires();   // restroke — wires touching the selected node are highlighted

			UpdateRemoveModuleButtonState();
		}
	}

	// The whole VM tree is recreated on every structure edit, so drop old subscriptions wholesale and re-attach.
	private void ResubscribeModules()
	{
		UnsubscribeModules();

		if ( _viewModel == null )
		{
			return;
		}

		foreach ( var module in _viewModel.Modules )
		{
			module.PropertyChanged += Module_PropertyChanged;

			_subscribedModules.Add( module );
		}
	}

	private void UnsubscribeModules()
	{
		foreach ( var module in _subscribedModules )
		{
			module.PropertyChanged -= Module_PropertyChanged;
		}

		_subscribedModules.Clear();
	}

	private void Module_PropertyChanged( object? sender, PropertyChangedEventArgs e )
	{
		if ( ( e.PropertyName == nameof( FFBModuleViewModel.NodeX ) ) || ( e.PropertyName == nameof( FFBModuleViewModel.NodeY ) ) )
		{
			RedrawWires();
			UpdateCanvasSize();
		}
	}

	#endregion

	#region Node dragging & selection

	private void Node_MouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( ( sender is not FrameworkElement element ) || ( element.DataContext is not FFBModuleViewModel module ) || ( _viewModel == null ) )
		{
			return;
		}

		// selecting here is safe: the settings panel lives BELOW the graph, so its height change cannot shift
		// the canvas under the still-pressed cursor
		_viewModel.SelectedModule = module;

		// a built-in graph's structure is locked — selection works, but no node or wire drags
		if ( _viewModel.IsFFBGraphBuiltIn )
		{
			e.Handled = true;

			return;
		}

		// a press on (or near) a connector dot starts a wire drag instead of a node drag
		var connector = HitTestConnector( module, e.GetPosition( element ), ConnectorGrabRadius );

		if ( connector != ConnectorKind.None )
		{
			_wireDragMode = ( connector == ConnectorKind.Output ) ? WireDragMode.FromOutput : WireDragMode.FromInput;
			_wireDragModule = module;
			_wireDragIsInputB = connector == ConnectorKind.InputB;
			_dragElement = element;

			element.CaptureMouse();

			e.Handled = true;

			return;
		}

		_dragModule = module;
		_dragElement = element;
		_dragStartPoint = e.GetPosition( Nodes_ItemsControl );
		_dragStartNodeX = module.NodeX;
		_dragStartNodeY = module.NodeY;
		_dragMoved = false;

		element.CaptureMouse();

		e.Handled = true;
	}

	private void Node_MouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( ( sender is not FrameworkElement element ) || ( element.DataContext is not FFBModuleViewModel module ) || ( _viewModel == null ) )
		{
			return;
		}

		// lock the preview graph to this node without moving the selection (and without starting any drag) — the
		// settings panel keeps showing the selected module while the preview shows this one; left-clicking any
		// node re-couples the preview to the selection
		_viewModel.PreviewModule = module;

		e.Handled = true;
	}

	private void Node_MouseMove( object sender, MouseEventArgs e )
	{
		if ( ( _dragElement == null ) || !_dragElement.IsMouseCaptured )
		{
			// not dragging — show a crosshair when hovering a connector dot so the wire-drag affordance is visible
			// (not on a built-in graph, where wires cannot be dragged)
			if ( ( sender is FrameworkElement hoverElement ) && ( hoverElement.DataContext is FFBModuleViewModel hoverModule ) )
			{
				var canDragWires = _viewModel?.IsFFBGraphBuiltIn != true;

				hoverElement.Cursor = canDragWires && ( HitTestConnector( hoverModule, e.GetPosition( hoverElement ), ConnectorGrabRadius ) != ConnectorKind.None ) ? Cursors.Cross : Cursors.Hand;
			}

			return;
		}

		if ( _wireDragMode != WireDragMode.None )
		{
			UpdatePendingWire( e.GetPosition( Nodes_ItemsControl ) );

			return;
		}

		if ( _dragModule == null )
		{
			return;
		}

		UpdateDraggedNodePosition( e.GetPosition( Nodes_ItemsControl ) );

		UpdateAutoPan( e.GetPosition( Viewport_Canvas ) );
	}

	private void UpdateDraggedNodePosition( Point position )
	{
		if ( _dragModule == null )
		{
			return;
		}

		var deltaX = position.X - _dragStartPoint.X;
		var deltaY = position.Y - _dragStartPoint.Y;

		if ( !_dragMoved && ( Math.Abs( deltaX ) < DragThreshold ) && ( Math.Abs( deltaY ) < DragThreshold ) )
		{
			return;
		}

		_dragMoved = true;

		// the canvas scrolls horizontally and grows to fit, so nodes may be dragged past the viewport edge; the
		// canvas origin is the position floor
		var nodeX = Math.Clamp( _dragStartNodeX + deltaX, 0.0, MaxNodeX );
		var nodeY = Math.Clamp( _dragStartNodeY + deltaY, 0.0, MaxNodeY );

		if ( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelFFBGraphSnapToGrid )
		{
			nodeX = FFBGraphTopology.Snap( nodeX );
			nodeY = FFBGraphTopology.Snap( nodeY );
		}

		_dragModule.NodeX = nodeX;
		_dragModule.NodeY = nodeY;
	}

	// Slow crawl when a node is dragged past a viewport edge. A timer (rather than relying on MouseMove) keeps the
	// pan going while the cursor sits still beyond the edge — each tick pans the view AND re-derives the node
	// position, since the content slides under the stationary cursor. Both axes now, as the view is height-limited.
	private const double AutoPanPixelsPerTick = 4.0;   // at ~60 Hz ticks ≈ a slow 240 px/s crawl

	private DispatcherTimer? _autoPanTimer = null;
	private double _autoPanDirectionX = 0.0;
	private double _autoPanDirectionY = 0.0;

	// panning the content is the opposite sign to chasing the cursor: the cursor beyond the RIGHT edge has to
	// reveal content further right, which slides the content LEFT (a decreasing translate)
	private static double AutoPanDirection( double position, double viewportExtent )
	{
		if ( position < 0.0 )
		{
			return 1.0;
		}

		return ( position > viewportExtent ) ? -1.0 : 0.0;
	}

	private void UpdateAutoPan( Point viewportPosition )
	{
		_autoPanDirectionX = AutoPanDirection( viewportPosition.X, Viewport_Canvas.ActualWidth );
		_autoPanDirectionY = AutoPanDirection( viewportPosition.Y, Viewport_Canvas.ActualHeight );

		if ( ( _autoPanDirectionX == 0.0 ) && ( _autoPanDirectionY == 0.0 ) )
		{
			_autoPanTimer?.Stop();
		}
		else
		{
			if ( _autoPanTimer == null )
			{
				_autoPanTimer = new DispatcherTimer( DispatcherPriority.Render ) { Interval = TimeSpan.FromMilliseconds( 16.0 ) };

				_autoPanTimer.Tick += AutoPanTimer_Tick;
			}

			_autoPanTimer.Start();
		}
	}

	private void AutoPanTimer_Tick( object? sender, EventArgs e )
	{
		if ( ( _dragModule == null ) || ( _dragElement == null ) || !_dragElement.IsMouseCaptured || ( ( _autoPanDirectionX == 0.0 ) && ( _autoPanDirectionY == 0.0 ) ) )
		{
			_autoPanTimer?.Stop();

			return;
		}

		Pan_TranslateTransform.X += _autoPanDirectionX * AutoPanPixelsPerTick;
		Pan_TranslateTransform.Y += _autoPanDirectionY * AutoPanPixelsPerTick;

		ClampPan();

		UpdateDraggedNodePosition( Mouse.GetPosition( Nodes_ItemsControl ) );

		// re-evaluate the edge condition — the cursor may have moved back inside without a MouseMove firing yet
		UpdateAutoPan( Mouse.GetPosition( Viewport_Canvas ) );
	}

	private void Node_MouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( _dragElement == null )
		{
			return;
		}

		_dragElement.ReleaseMouseCapture();

		if ( _wireDragMode != WireDragMode.None )
		{
			CompleteWireDrag( e.GetPosition( Nodes_ItemsControl ) );
		}
		else if ( _dragMoved )
		{
			// one serialization for the whole drag — positions are display-only, no engine rebuild
			App.Instance!.SettingsFile.QueueForSerialization = true;
		}

		_dragModule = null;
		_dragElement = null;
		_dragMoved = false;
		_wireDragMode = WireDragMode.None;
		_wireDragModule = null;
		_wireDragIsInputB = false;

		_autoPanTimer?.Stop();

		// the node set moved, so the pan limits moved with it (clamping is suppressed mid-drag to avoid fighting it)
		ClampPan();

		e.Handled = true;
	}

	private void AddModule_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( _viewModel == null )
		{
			return;
		}

		var window = new Windows.AddFFBModuleWindow( _viewModel.AddableModuleTypes ) { Owner = App.Instance!.MainWindow };

		window.ShowDialog();

		if ( window.SelectedModuleType != null )
		{
			_viewModel.AddModule( window.SelectedModuleType );
		}
	}

	private void RemoveModule_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( _viewModel?.SelectedModule is { } selectedModule )
		{
			_viewModel.RemoveModule( selectedModule );
		}
	}

	private void AutoLayout_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		_viewModel?.AutoLayout();

		// the fresh layout starts at the origin, so bring the view back there too (zoom is left alone)
		Pan_TranslateTransform.X = 0.0;
		Pan_TranslateTransform.Y = 0.0;

		ClampPan();
	}

	private void SnapToGrid_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.RacingWheelFFBGraphSnapToGrid = !settings.RacingWheelFFBGraphSnapToGrid;

		App.Instance!.SettingsFile.QueueForSerialization = true;

		UpdateSnapToGridButtonVisual();

		// switching the toggle on pulls every node onto the grid right away
		if ( settings.RacingWheelFFBGraphSnapToGrid )
		{
			_viewModel?.SnapAllToGrid();
		}
	}

	private void UpdateSnapToGridButtonVisual()
	{
		SnapToGrid_MairaButton.HighlightIcon = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelFFBGraphSnapToGrid;
	}

	#endregion

	#region Wire dragging

	// connector centers in node-local coordinates — must match the connector Ellipses in the DataTemplate
	private static ConnectorKind HitTestConnector( FFBModuleViewModel module, Point positionInNode, double radius )
	{
		var best = ConnectorKind.None;
		var bestDistanceSquared = radius * radius;

		void Consider( ConnectorKind kind, double connectorX, double connectorY )
		{
			var deltaX = positionInNode.X - connectorX;
			var deltaY = positionInNode.Y - connectorY;

			var distanceSquared = deltaX * deltaX + deltaY * deltaY;

			if ( distanceSquared <= bestDistanceSquared )
			{
				bestDistanceSquared = distanceSquared;
				best = kind;
			}
		}

		if ( module.HasOutputConnector )
		{
			Consider( ConnectorKind.Output, NodeWidth, NodeHeight / 2.0 );
		}

		if ( module.ShowInputA )
		{
			Consider( ConnectorKind.InputA, 0.0, module.ShowInputB ? DualInputAOffsetY : NodeHeight / 2.0 );
		}

		if ( module.ShowInputB )
		{
			Consider( ConnectorKind.InputB, 0.0, DualInputBOffsetY );
		}

		return best;
	}

	private static Point OutputConnectorPoint( FFBModuleViewModel module )
	{
		return new Point( module.NodeX + NodeWidth, module.NodeY + NodeHeight / 2.0 );
	}

	private static Point InputConnectorPoint( FFBModuleViewModel module, bool isInputB )
	{
		var inputOffsetY = module.ShowInputB ? ( isInputB ? DualInputBOffsetY : DualInputAOffsetY ) : NodeHeight / 2.0;

		return new Point( module.NodeX, module.NodeY + inputOffsetY );
	}

	// the wire-drop eligibility rule is the target's EligibleInputs list — the exact same set the settings-panel
	// input combos offer (excludes self, generators, the Output module, and anything downstream = no cycles)
	private static bool IsEligibleInput( FFBModuleViewModel target, string sourceModuleId )
	{
		foreach ( var eligibleInput in target.EligibleInputs )
		{
			if ( eligibleInput.Key == sourceModuleId )
			{
				return true;
			}
		}

		return false;
	}

	private ( FFBModuleViewModel target, bool isInputB )? FindInputConnectorAt( Point position, FFBModuleViewModel sourceModule )
	{
		( FFBModuleViewModel, bool )? best = null;

		var bestDistanceSquared = ConnectorDropRadius * ConnectorDropRadius;

		if ( _viewModel == null )
		{
			return best;
		}

		foreach ( var module in _viewModel.Modules )
		{
			if ( !IsEligibleInput( module, sourceModule.ModuleId ) )
			{
				continue;
			}

			for ( var inputIndex = 0; inputIndex < module.SignalInputCount; inputIndex++ )
			{
				var isInputB = inputIndex == 1;

				var connectorPoint = InputConnectorPoint( module, isInputB );

				var deltaX = position.X - connectorPoint.X;
				var deltaY = position.Y - connectorPoint.Y;

				var distanceSquared = deltaX * deltaX + deltaY * deltaY;

				if ( distanceSquared <= bestDistanceSquared )
				{
					bestDistanceSquared = distanceSquared;
					best = ( module, isInputB );
				}
			}
		}

		return best;
	}

	private FFBModuleViewModel? FindOutputConnectorAt( Point position, FFBModuleViewModel targetModule )
	{
		FFBModuleViewModel? best = null;

		var bestDistanceSquared = ConnectorDropRadius * ConnectorDropRadius;

		if ( _viewModel == null )
		{
			return best;
		}

		foreach ( var module in _viewModel.Modules )
		{
			// the eligible-input list already excludes the Output module and the generators
			if ( !IsEligibleInput( targetModule, module.ModuleId ) )
			{
				continue;
			}

			var connectorPoint = OutputConnectorPoint( module );

			var deltaX = position.X - connectorPoint.X;
			var deltaY = position.Y - connectorPoint.Y;

			var distanceSquared = deltaX * deltaX + deltaY * deltaY;

			if ( distanceSquared <= bestDistanceSquared )
			{
				bestDistanceSquared = distanceSquared;
				best = module;
			}
		}

		return best;
	}

	private void UpdatePendingWire( Point position )
	{
		if ( _wireDragModule == null )
		{
			return;
		}

		Point start;
		Point end;
		bool validDrop;

		if ( _wireDragMode == WireDragMode.FromOutput )
		{
			start = OutputConnectorPoint( _wireDragModule );

			var target = FindInputConnectorAt( position, _wireDragModule );

			validDrop = target != null;

			// snap the loose end onto the connector while hovering a valid drop point
			end = ( target != null ) ? InputConnectorPoint( target.Value.target, target.Value.isInputB ) : position;
		}
		else
		{
			end = InputConnectorPoint( _wireDragModule, _wireDragIsInputB );

			var source = FindOutputConnectorAt( position, _wireDragModule );

			validDrop = source != null;

			start = ( source != null ) ? OutputConnectorPoint( source ) : position;
		}

		if ( ( _pendingWirePath == null ) || !Wires_Canvas.Children.Contains( _pendingWirePath ) )
		{
			// RedrawWires clears the canvas wholesale (e.g. the selection change at drag start), so re-add lazily
			_pendingWirePath = new Path
			{
				StrokeThickness = 2.0,
				StrokeDashArray = new DoubleCollection { 4.0, 3.0 },
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				IsHitTestVisible = false
			};

			Wires_Canvas.Children.Add( _pendingWirePath );
		}

		var controlOffset = Math.Max( 50.0, Math.Abs( end.X - start.X ) / 2.0 );

		var pathFigure = new PathFigure { StartPoint = start };

		pathFigure.Segments.Add( new BezierSegment( new Point( start.X + controlOffset, start.Y ), new Point( end.X - controlOffset, end.Y ), end, isStroked: true ) );

		_pendingWirePath.Data = new PathGeometry { Figures = { pathFigure } };
		_pendingWirePath.SetResourceReference( Shape.StrokeProperty, validDrop ? "Brush.Accent" : "Brush.Foreground.Muted" );
		_pendingWirePath.Opacity = validDrop ? 1.0 : 0.7;
	}

	private void CompleteWireDrag( Point position )
	{
		if ( _pendingWirePath != null )
		{
			Wires_Canvas.Children.Remove( _pendingWirePath );

			_pendingWirePath = null;
		}

		if ( _wireDragModule == null )
		{
			return;
		}

		// committing through the input-selection setters runs the exact same path as the settings-panel combos
		// (topological re-sort, engine rebuild, sync + serialization, deferred card rebuild); an ineligible or
		// empty drop point simply abandons the pending wire
		if ( _wireDragMode == WireDragMode.FromOutput )
		{
			var target = FindInputConnectorAt( position, _wireDragModule );

			if ( target != null )
			{
				if ( target.Value.isInputB )
				{
					target.Value.target.InputBSelectedId = _wireDragModule.ModuleId;
				}
				else
				{
					target.Value.target.InputASelectedId = _wireDragModule.ModuleId;
				}
			}
		}
		else
		{
			var source = FindOutputConnectorAt( position, _wireDragModule );

			if ( source != null )
			{
				if ( _wireDragIsInputB )
				{
					_wireDragModule.InputBSelectedId = source.ModuleId;
				}
				else
				{
					_wireDragModule.InputASelectedId = source.ModuleId;
				}
			}
		}
	}

	#endregion

	#region Wire drawing

	private void RedrawWires()
	{
		Wires_Canvas.Children.Clear();

		if ( _viewModel == null )
		{
			return;
		}

		var selectedModuleId = _viewModel.SelectedModule?.ModuleId;

		foreach ( var wire in _viewModel.Wires )
		{
			var source = FindModule( wire.SourceModuleId );
			var target = FindModule( wire.TargetModuleId );

			if ( ( source == null ) || ( target == null ) )
			{
				continue;
			}

			var start = OutputConnectorPoint( source );
			var end = InputConnectorPoint( target, wire.IsInputB );

			var pathFigure = new PathFigure { StartPoint = start };

			if ( end.X <= start.X )
			{
				// backward wire (the consumer's left edge is flush with or left of its source's right edge): a
				// plain bezier would cross over both node boxes, so route it through two invisible waypoints, one
				// vertically in line with each connector dot, at the vertical halfway point between the two dots —
				// but never more than GridSize + NodeHeight / 2 from its own dot (one grid cell past the node edge)
				var midY = ( start.Y + end.Y ) / 2.0;
				var maxWaypointOffset = FFBGraphTopology.GridSize + NodeHeight / 2.0;

				var outputWaypoint = new Point( start.X, Math.Clamp( midY, start.Y - maxWaypointOffset, start.Y + maxWaypointOffset ) );
				var inputWaypoint = new Point( end.X, Math.Clamp( midY, end.Y - maxWaypointOffset, end.Y + maxWaypointOffset ) );

				const double stub = 60.0;

				if ( Point.Subtract( inputWaypoint, outputWaypoint ).Length < 1.0 )
				{
					// flush nodes close enough that both waypoints land on the same spot — collapse to one so the
					// degenerate middle segment doesn't draw a wiggle
					pathFigure.Segments.Add( new BezierSegment( new Point( start.X + stub, start.Y ), new Point( outputWaypoint.X + stub, outputWaypoint.Y ), outputWaypoint, isStroked: true ) );
					pathFigure.Segments.Add( new BezierSegment( new Point( outputWaypoint.X - stub, outputWaypoint.Y ), new Point( end.X - stub, end.Y ), end, isStroked: true ) );
				}
				else
				{
					// three cubics with matching tangents at each waypoint: out of the source heading right, turn
					// through the output-side waypoint aiming at the input-side waypoint, run straight across, then
					// hook back into the target input from the left — the tangent at each waypoint points along the
					// line between the two waypoints, so the middle segment is straight and the turns are smooth
					var waypointDirection = inputWaypoint - outputWaypoint;

					waypointDirection.Normalize();

					var waypointStub = waypointDirection * stub;

					pathFigure.Segments.Add( new BezierSegment( new Point( start.X + stub, start.Y ), outputWaypoint - waypointStub, outputWaypoint, isStroked: true ) );
					pathFigure.Segments.Add( new BezierSegment( outputWaypoint + waypointStub, inputWaypoint - waypointStub, inputWaypoint, isStroked: true ) );
					pathFigure.Segments.Add( new BezierSegment( inputWaypoint + waypointStub, new Point( end.X - stub, end.Y ), end, isStroked: true ) );
				}
			}
			else
			{
				var controlOffset = Math.Max( 50.0, ( end.X - start.X ) / 2.0 );

				pathFigure.Segments.Add( new BezierSegment( new Point( start.X + controlOffset, start.Y ), new Point( end.X - controlOffset, end.Y ), end, isStroked: true ) );
			}

			var path = new Path
			{
				Data = new PathGeometry { Figures = { pathFigure } },
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				IsHitTestVisible = false
			};

			var touchesSelection = ( selectedModuleId != null ) && ( ( wire.SourceModuleId == selectedModuleId ) || ( wire.TargetModuleId == selectedModuleId ) );

			if ( touchesSelection )
			{
				path.SetResourceReference( Shape.StrokeProperty, "Brush.Accent" );
				path.StrokeThickness = 2.5;
			}
			else
			{
				path.SetResourceReference( Shape.StrokeProperty, "Brush.Foreground.Muted" );
				path.StrokeThickness = 1.5;
				path.Opacity = 0.8;
			}

			Wires_Canvas.Children.Add( path );
		}
	}

	private FFBModuleViewModel? FindModule( string moduleId )
	{
		if ( _viewModel == null )
		{
			return null;
		}

		foreach ( var module in _viewModel.Modules )
		{
			if ( module.ModuleId == moduleId )
			{
				return module;
			}
		}

		return null;
	}

	private void UpdateCanvasSize()
	{
		var maxNodeX = 0.0;
		var maxNodeY = 0.0;

		if ( _viewModel != null )
		{
			foreach ( var module in _viewModel.Modules )
			{
				maxNodeX = Math.Max( maxNodeX, module.NodeX );
				maxNodeY = Math.Max( maxNodeY, module.NodeY );
			}
		}

		// size the canvas to the node extents plus one grid cell of margin, so the grid-dot field always ends
		// LayoutMargin past the outermost nodes instead of stretching to the (usually larger) viewport
		LayoutRoot_Grid.MinWidth = maxNodeX + NodeWidth + FFBGraphTopology.LayoutMargin;
		LayoutRoot_Grid.MinHeight = maxNodeY + NodeHeight + FFBGraphTopology.LayoutMargin;

		// a moving node moves the pan limits with it — but not mid-drag, where re-clamping would shove the content
		// out from under the cursor and fight the drag (the drag's own auto-pan clamps instead, and so does drag end)
		if ( _dragModule == null )
		{
			ClampPan();
		}
	}

	#endregion

	#region Zoom & pan

	/// <summary>
	/// Ctrl+wheel zooms about the cursor; a plain wheel is handed back to the page so the racing wheel page keeps
	/// scrolling when the cursor is over the node canvas (the canvas itself no longer scrolls at all).
	/// </summary>
	private void Viewport_Canvas_PreviewMouseWheel( object sender, MouseWheelEventArgs e )
	{
		if ( Keyboard.Modifiers.HasFlag( ModifierKeys.Control ) )
		{
			// wheel up (positive delta) zooms OUT, wheel down zooms IN
			ZoomAt( e.GetPosition( Viewport_Canvas ), ( e.Delta > 0 ) ? ( 1.0 / ZoomStep ) : ZoomStep );

			e.Handled = true;

			return;
		}

		e.Handled = true;

		var eventArgs = new MouseWheelEventArgs( e.MouseDevice, e.Timestamp, e.Delta )
		{
			RoutedEvent = MouseWheelEvent,
			Source = sender
		};

		var parent = ( (FrameworkElement) sender ).Parent as UIElement;

		parent?.RaiseEvent( eventArgs );
	}

	/// <summary>Scale about a viewport point, so the content sitting under the cursor stays under the cursor.</summary>
	private void ZoomAt( Point viewportPosition, double factor )
	{
		var oldScale = Zoom_ScaleTransform.ScaleX;
		var newScale = Math.Clamp( oldScale * factor, MinZoom, MaxZoom );

		if ( newScale == oldScale )
		{
			return;
		}

		var contentX = ( viewportPosition.X - Pan_TranslateTransform.X ) / oldScale;
		var contentY = ( viewportPosition.Y - Pan_TranslateTransform.Y ) / oldScale;

		Zoom_ScaleTransform.ScaleX = newScale;
		Zoom_ScaleTransform.ScaleY = newScale;

		Pan_TranslateTransform.X = viewportPosition.X - contentX * newScale;
		Pan_TranslateTransform.Y = viewportPosition.Y - contentY * newScale;

		ClampPan();
	}

	/// <summary>
	/// Left-dragging empty canvas pans the graph. A left-press ON a node never reaches here — Node_MouseLeftButtonDown
	/// takes it (selection / node or wire drag) and marks it handled — so arriving here already means the cursor was
	/// over empty space.
	/// </summary>
	private void Viewport_Canvas_MouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		_isPanning = true;
		_panStartPoint = e.GetPosition( Viewport_Canvas );
		_panStartPanX = Pan_TranslateTransform.X;
		_panStartPanY = Pan_TranslateTransform.Y;

		Viewport_Canvas.CaptureMouse();
		Viewport_Canvas.Cursor = Cursors.SizeAll;

		e.Handled = true;
	}

	private void Viewport_Canvas_MouseMove( object sender, MouseEventArgs e )
	{
		if ( !_isPanning )
		{
			return;
		}

		var position = e.GetPosition( Viewport_Canvas );

		Pan_TranslateTransform.X = _panStartPanX + ( position.X - _panStartPoint.X );
		Pan_TranslateTransform.Y = _panStartPanY + ( position.Y - _panStartPoint.Y );

		ClampPan();
	}

	private void Viewport_Canvas_MouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( !_isPanning )
		{
			return;
		}

		Viewport_Canvas.ReleaseMouseCapture();   // raises LostMouseCapture, which ends the pan

		e.Handled = true;
	}

	// covers the ordinary release above AND any capture stolen out from under the pan (alt-tab, a modal, ...)
	private void Viewport_Canvas_LostMouseCapture( object sender, MouseEventArgs e )
	{
		_isPanning = false;

		Viewport_Canvas.Cursor = Cursors.Arrow;
	}

	private void Viewport_Canvas_SizeChanged( object sender, SizeChangedEventArgs e )
	{
		// the pan limits depend on the viewport size — re-clamp so the graph stays on screen after a resize
		UpdateCanvasSize();
		ClampPan();
	}

	/// <summary>
	/// Keep the graph on screen. The outermost node on each side may travel no further than the opposite view
	/// boundary, so at every pan limit at least one whole node stays visible: pan right until the LEFT-most node's
	/// right edge reaches the right boundary, pan left until the RIGHT-most node's left edge reaches the left
	/// boundary, and the same on the Y axis. Re-run on zoom, pan, resize, and whenever the node set changes.
	/// </summary>
	private void ClampPan()
	{
		if ( ( _viewModel == null ) || ( _viewModel.Modules.Count == 0 ) )
		{
			return;
		}

		var viewportWidth = Viewport_Canvas.ActualWidth;
		var viewportHeight = Viewport_Canvas.ActualHeight;

		if ( ( viewportWidth <= 0.0 ) || ( viewportHeight <= 0.0 ) )
		{
			return;   // not laid out yet — SizeChanged will clamp once it is
		}

		var minNodeX = double.MaxValue;
		var maxNodeX = double.MinValue;
		var minNodeY = double.MaxValue;
		var maxNodeY = double.MinValue;

		foreach ( var module in _viewModel.Modules )
		{
			minNodeX = Math.Min( minNodeX, module.NodeX );
			maxNodeX = Math.Max( maxNodeX, module.NodeX );
			minNodeY = Math.Min( minNodeY, module.NodeY );
			maxNodeY = Math.Max( maxNodeY, module.NodeY );
		}

		var scale = Zoom_ScaleTransform.ScaleX;

		Pan_TranslateTransform.X = ClampPanAxis( Pan_TranslateTransform.X, -maxNodeX * scale, viewportWidth - ( minNodeX + NodeWidth ) * scale );
		Pan_TranslateTransform.Y = ClampPanAxis( Pan_TranslateTransform.Y, -maxNodeY * scale, viewportHeight - ( minNodeY + NodeHeight ) * scale );
	}

	// One node zoomed bigger than the viewport inverts the bounds (min > max). Panning across that node's overflow
	// is still the right behavior, so swap the bounds rather than collapsing to a single locked position.
	private static double ClampPanAxis( double pan, double minPan, double maxPan )
	{
		return ( minPan <= maxPan ) ? Math.Clamp( pan, minPan, maxPan ) : Math.Clamp( pan, maxPan, minPan );
	}

	#endregion
}
