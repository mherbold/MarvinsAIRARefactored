
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.FFB;

namespace MarvinsAIRARefactored.Controls;

/// <summary>
/// Node-graph editor for the FFB graph. Nodes are the non-generator module view-models (positions live in the
/// model via <see cref="FFBModuleViewModel.NodeX"/>/<see cref="FFBModuleViewModel.NodeY"/>); wires are display-only
/// bezier paths recomputed from those positions, so no visual-tree measuring is needed. Dragging a node writes
/// positions through the view-model and queues one settings serialization when the drag ends — node positions are
/// presentation state and never rebuild an engine.
/// </summary>
public partial class FFBGraphEditor : UserControl
{
	// geometry — must match the node DataTemplate in FFBGraphEditor.xaml
	private const double NodeWidth = FFBGraphTopology.NodeWidth;
	private const double NodeHeight = FFBGraphTopology.NodeHeight;
	private const double ConnectorInset = 5.0;
	private const double DualInputAOffsetY = 18.0;
	private const double DualInputBOffsetY = 36.0;

	private const double MinCanvasHeight = 320.0;
	private const double MaxNodeX = 8000.0;
	private const double MaxNodeY = 3000.0;
	private const double DragThreshold = 3.0;

	private FFBGraphViewModel? _viewModel = null;
	private readonly List<FFBModuleViewModel> _subscribedModules = [];

	private FFBModuleViewModel? _dragModule = null;
	private FrameworkElement? _dragElement = null;
	private Point _dragStartPoint;
	private double _dragStartNodeX = 0.0;
	private double _dragStartNodeY = 0.0;
	private bool _dragMoved = false;

	public FFBGraphEditor()
	{
		InitializeComponent();

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

		Nodes_ItemsControl.ItemsSource = _viewModel.MainModules;

		_viewModel.MainModules.CollectionChanged += MainModules_CollectionChanged;
		_viewModel.Wires.CollectionChanged += Wires_CollectionChanged;
		_viewModel.PropertyChanged += ViewModel_PropertyChanged;

		ResubscribeModules();
		RedrawWires();
		UpdateCanvasSize();
		UpdateSnapToGridButtonVisual();
	}

	private void FFBGraphEditor_Unloaded( object sender, RoutedEventArgs e )
	{
		if ( _viewModel == null )
		{
			return;
		}

		_viewModel.MainModules.CollectionChanged -= MainModules_CollectionChanged;
		_viewModel.Wires.CollectionChanged -= Wires_CollectionChanged;
		_viewModel.PropertyChanged -= ViewModel_PropertyChanged;

		UnsubscribeModules();

		Nodes_ItemsControl.ItemsSource = null;

		_viewModel = null;
	}

	private void MainModules_CollectionChanged( object? sender, NotifyCollectionChangedEventArgs e )
	{
		ResubscribeModules();
		RedrawWires();
		UpdateCanvasSize();
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

		foreach ( var module in _viewModel.MainModules )
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

		_dragModule = module;
		_dragElement = element;
		_dragStartPoint = e.GetPosition( Nodes_ItemsControl );
		_dragStartNodeX = module.NodeX;
		_dragStartNodeY = module.NodeY;
		_dragMoved = false;

		element.CaptureMouse();

		e.Handled = true;
	}

	private void Node_MouseMove( object sender, MouseEventArgs e )
	{
		if ( ( _dragModule == null ) || ( _dragElement == null ) || !_dragElement.IsMouseCaptured )
		{
			return;
		}

		var position = e.GetPosition( Nodes_ItemsControl );

		var deltaX = position.X - _dragStartPoint.X;
		var deltaY = position.Y - _dragStartPoint.Y;

		if ( !_dragMoved && ( Math.Abs( deltaX ) < DragThreshold ) && ( Math.Abs( deltaY ) < DragThreshold ) )
		{
			return;
		}

		_dragMoved = true;

		// the canvas scrolls horizontally and grows to fit, so nodes may be dragged past the viewport edge; the
		// lower bound keeps a small margin so nodes never sit flush on the canvas's top/left edges
		var nodeX = Math.Clamp( _dragStartNodeX + deltaX, FFBGraphTopology.GridOrigin, MaxNodeX );
		var nodeY = Math.Clamp( _dragStartNodeY + deltaY, FFBGraphTopology.GridOrigin, MaxNodeY );

		if ( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelFFBGraphSnapToGrid )
		{
			nodeX = FFBGraphTopology.Snap( nodeX );
			nodeY = FFBGraphTopology.Snap( nodeY );
		}

		_dragModule.NodeX = nodeX;
		_dragModule.NodeY = nodeY;
	}

	private void Node_MouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( _dragElement == null )
		{
			return;
		}

		_dragElement.ReleaseMouseCapture();

		if ( _dragMoved )
		{
			// one serialization for the whole drag — positions are display-only, no engine rebuild
			App.Instance!.SettingsFile.QueueForSerialization = true;
		}

		_dragModule = null;
		_dragElement = null;
		_dragMoved = false;

		e.Handled = true;
	}

	private void AutoLayout_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		_viewModel?.AutoLayout();

		Editor_ScrollViewer.ScrollToHorizontalOffset( 0.0 );
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
		SnapToGrid_MairaButton.Opacity = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelFFBGraphSnapToGrid ? 1.0 : 0.4;
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

			var start = new Point( source.NodeX + NodeWidth - ConnectorInset, source.NodeY + NodeHeight / 2.0 );

			var inputOffsetY = target.ShowInputB ? ( wire.IsInputB ? DualInputBOffsetY : DualInputAOffsetY ) : NodeHeight / 2.0;

			var end = new Point( target.NodeX + ConnectorInset, target.NodeY + inputOffsetY );

			var pathFigure = new PathFigure { StartPoint = start };

			if ( end.X - start.X < 20.0 )
			{
				// backward wire (the consumer sits left of its source): a plain bezier would cross over both node
				// boxes, so route it through an invisible waypoint halfway between the modules — through the
				// vertical gap between them when there is one, or wrapped underneath both when they share a row
				var sourceTop = source.NodeY;
				var sourceBottom = source.NodeY + NodeHeight;
				var targetTop = target.NodeY;
				var targetBottom = target.NodeY + NodeHeight;

				double channelY;

				if ( targetTop > sourceBottom + 10.0 )
				{
					channelY = ( sourceBottom + targetTop ) / 2.0;
				}
				else if ( sourceTop > targetBottom + 10.0 )
				{
					channelY = ( targetBottom + sourceTop ) / 2.0;
				}
				else
				{
					channelY = Math.Max( sourceBottom, targetBottom ) + 25.0;
				}

				var waypoint = new Point( ( start.X + end.X ) / 2.0, channelY );

				const double stub = 60.0;

				// two cubics with matching tangents at the waypoint: out of the source heading right, sweep into
				// the channel heading left, then hook back into the target input from the left
				pathFigure.Segments.Add( new BezierSegment( new Point( start.X + stub, start.Y ), new Point( waypoint.X + stub, channelY ), waypoint, isStroked: true ) );
				pathFigure.Segments.Add( new BezierSegment( new Point( waypoint.X - stub, channelY ), new Point( end.X - stub, end.Y ), end, isStroked: true ) );
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

		foreach ( var module in _viewModel.MainModules )
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
			foreach ( var module in _viewModel.MainModules )
			{
				maxNodeX = Math.Max( maxNodeX, module.NodeX );
				maxNodeY = Math.Max( maxNodeY, module.NodeY );
			}
		}

		// grow the canvas to the node extents so the scroll viewer can reach every node
		LayoutRoot_Grid.MinWidth = maxNodeX + NodeWidth + FFBGraphTopology.LayoutMargin;
		LayoutRoot_Grid.MinHeight = Math.Max( MinCanvasHeight, maxNodeY + NodeHeight + FFBGraphTopology.LayoutMargin );
	}

	// Vertical wheel input would otherwise be swallowed by the horizontal-only scroll viewer; hand it back to
	// the page so the whole racing wheel page keeps scrolling when the cursor is over the node canvas.
	private void Editor_ScrollViewer_PreviewMouseWheel( object sender, MouseWheelEventArgs e )
	{
		e.Handled = true;

		var eventArgs = new MouseWheelEventArgs( e.MouseDevice, e.Timestamp, e.Delta )
		{
			RoutedEvent = MouseWheelEvent,
			Source = sender
		};

		var parent = ( (ScrollViewer) sender ).Parent as UIElement;

		parent?.RaiseEvent( eventArgs );
	}

	#endregion
}
