
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;

using PInvoke;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaDualSlider : UserControl
{
	private POINT _draggingCenter;
	private bool _isDraggingLeftHandle;

	public MairaDualSlider()
	{
		InitializeComponent();

		IsVisibleChanged += OnIsVisibleChanged;
	}

	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();

		LeftDragHandle_Image.PreviewMouseLeftButtonDown += LeftDragHandle_Image_PreviewMouseLeftButtonDown;
		LeftDragHandle_Image.PreviewMouseLeftButtonUp += DragHandle_Image_PreviewMouseLeftButtonUp;
		LeftDragHandle_Image.PreviewMouseMove += LeftDragHandle_Image_PreviewMouseMove;
		LeftDragHandle_Image.LostMouseCapture += DragHandle_Image_LostMouseCapture;

		RightDragHandle_Image.PreviewMouseLeftButtonDown += RightDragHandle_Image_PreviewMouseLeftButtonDown;
		RightDragHandle_Image.PreviewMouseLeftButtonUp += DragHandle_Image_PreviewMouseLeftButtonUp;
		RightDragHandle_Image.PreviewMouseMove += RightDragHandle_Image_PreviewMouseMove;
		RightDragHandle_Image.LostMouseCapture += DragHandle_Image_LostMouseCapture;
	}

	#region Event handlers

	private async void OnIsVisibleChanged( object sender, DependencyPropertyChangedEventArgs e )
	{
		if ( IsVisible )
		{
			InvalidateMeasure();
			InvalidateArrange();

			await Dispatcher.InvokeAsync( UpdateDragHandleVisuals, DispatcherPriority.Render );
		}
	}

	private void LeftDragHandle_Image_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( e.LeftButton == MouseButtonState.Pressed )
		{
			IsDragging = true;

			_isDraggingLeftHandle = true;

			User32.GetCursorPos( out _draggingCenter );

			_ = User32.ShowCursor( false );

			LeftDragHandle_Image.CaptureMouse();

			e.Handled = true;
		}
	}

	private void RightDragHandle_Image_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		var app = App.Instance!;

		if ( e.LeftButton == MouseButtonState.Pressed )
		{
			IsDragging = true;

			_isDraggingLeftHandle = false;

			User32.GetCursorPos( out _draggingCenter );

			_ = User32.ShowCursor( false );

			RightDragHandle_Image.CaptureMouse();

			app.Wind.StartPreview( RightValue );

			e.Handled = true;
		}
	}

	private void DragHandle_Image_PreviewMouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( IsDragging && ( e.ChangedButton == MouseButton.Left ) )
		{
			EndDrag();
		}
	}

	private void LeftDragHandle_Image_PreviewMouseMove( object sender, MouseEventArgs e )
	{
		if ( IsDragging )
		{
			User32.GetCursorPos( out POINT current );

			var delta = ( current.x - _draggingCenter.x ) + ( current.y - _draggingCenter.y );

			if ( delta != 0 )
			{
				LeftValue = MathZ.Saturate( LeftValue - delta * 0.001f );

				User32.SetCursorPos( _draggingCenter.x, _draggingCenter.y );
			}
		}
	}

	private void RightDragHandle_Image_PreviewMouseMove( object sender, MouseEventArgs e )
	{
		var app = App.Instance!;

		if ( IsDragging )
		{
			User32.GetCursorPos( out POINT current );

			var delta = ( current.x - _draggingCenter.x ) + ( current.y - _draggingCenter.y );

			if ( delta != 0 )
			{
				RightValue = MathZ.Saturate( RightValue - delta * 0.001f );

				app.Wind.StartPreview( RightValue );

				User32.SetCursorPos( _draggingCenter.x, _draggingCenter.y );
			}
		}
	}

	private void DragHandle_Image_LostMouseCapture( object sender, MouseEventArgs e )
	{
		if ( IsDragging )
		{
			EndDrag();
		}
	}

	#endregion

	#region Dependency Properties

	public static readonly DependencyProperty IsDraggingProperty = DependencyProperty.Register( nameof( IsDragging ), typeof( bool ), typeof( MairaDualSlider ), new PropertyMetadata( false ) );

	public bool IsDragging
	{
		get => (bool) GetValue( IsDraggingProperty );
		set => SetValue( IsDraggingProperty, value );
	}

	public static readonly DependencyProperty LeftValueProperty = DependencyProperty.Register( nameof( LeftValue ), typeof( float ), typeof( MairaDualSlider ), new FrameworkPropertyMetadata( 0f, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLeftValueChanged ) );

	public float LeftValue
	{
		get => (float) GetValue( LeftValueProperty );
		set => SetValue( LeftValueProperty, value );
	}

	public static readonly DependencyProperty LeftValueStringProperty = DependencyProperty.Register( nameof( LeftValueString ), typeof( string ), typeof( MairaDualSlider ), new PropertyMetadata( string.Empty ) );

	public string LeftValueString
	{
		get => (string) GetValue( LeftValueStringProperty );
		set => SetValue( LeftValueStringProperty, value );
	}

	public static readonly DependencyProperty RightValueProperty = DependencyProperty.Register( nameof( RightValue ), typeof( float ), typeof( MairaDualSlider ), new FrameworkPropertyMetadata( 0f, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRightValueChanged ) );

	public float RightValue
	{
		get => (float) GetValue( RightValueProperty );
		set => SetValue( RightValueProperty, value );
	}

	public static readonly DependencyProperty RightValueStringProperty = DependencyProperty.Register( nameof( RightValueString ), typeof( string ), typeof( MairaDualSlider ), new PropertyMetadata( string.Empty ) );

	public string RightValueString
	{
		get => (string) GetValue( RightValueStringProperty );
		set => SetValue( RightValueStringProperty, value );
	}

	#endregion

	#region Dependency Property Changed Events

	private static void OnLeftValueChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaDualSlider mairaDualSlider )
		{
			mairaDualSlider.UpdateDragHandleVisuals();
		}
	}

	private static void OnRightValueChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaDualSlider mairaDualSlider )
		{
			mairaDualSlider.UpdateDragHandleVisuals();
		}
	}

	#endregion

	#region Logic

	private void EndDrag()
	{
		var app = App.Instance!;

		IsDragging = false;

		Misc.MoveCursorToElement( _isDraggingLeftHandle ? LeftDragHandle_Image : RightDragHandle_Image );

		_ = User32.ShowCursor( true );

		Mouse.Capture( null );

		if ( !_isDraggingLeftHandle )
		{
			app.Wind.StopPreview();
		}
	}

	private void UpdateDragHandleVisuals()
	{
		var rowHeight = Grid.RowDefinitions[ 1 ].ActualHeight;

		LeftDragHandle_Image.Margin = new Thickness( 0, 0, 0, LeftValue * ( rowHeight - LeftDragHandle_Image.ActualHeight ) );
		RightDragHandle_Image.Margin = new Thickness( 0, 0, 0, RightValue * ( rowHeight - RightDragHandle_Image.ActualHeight ) );
	}

	#endregion
}
