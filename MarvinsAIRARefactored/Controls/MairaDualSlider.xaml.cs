
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Windows.Win32;

using MarvinsAIRARefactored.Classes;

using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using Point = System.Drawing.Point;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaDualSlider : UserControl
{
	private Point _draggingCenter;
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

			PInvoke.GetCursorPos( out _draggingCenter );

			_ = PInvoke.ShowCursor( false );

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

			PInvoke.GetCursorPos( out _draggingCenter );

			_ = PInvoke.ShowCursor( false );

			RightDragHandle_Image.CaptureMouse();

			app.TyphoonWind.StartPreview( ( RightValue - RightMinValue ) / ( RightMaxValue - RightMinValue ) );

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
			PInvoke.GetCursorPos( out System.Drawing.Point current );

			var delta = ( current.X - _draggingCenter.X ) + ( current.Y - _draggingCenter.Y );

			if ( delta != 0 )
			{
				LeftValue = Math.Clamp( LeftValue - delta * 0.001f * ( LeftMaxValue - LeftMinValue ), LeftMinValue, LeftMaxValue );

				PInvoke.SetCursorPos( _draggingCenter.X, _draggingCenter.Y );
			}
		}
	}

	private void RightDragHandle_Image_PreviewMouseMove( object sender, MouseEventArgs e )
	{
		var app = App.Instance!;

		if ( IsDragging )
		{
			PInvoke.GetCursorPos( out System.Drawing.Point current );

			var delta = ( current.X - _draggingCenter.X ) + ( current.Y - _draggingCenter.Y );

			if ( delta != 0 )
			{
				RightValue = Math.Clamp( RightValue - delta * 0.001f * ( RightMaxValue - RightMinValue ), RightMinValue, RightMaxValue );

				app.TyphoonWind.StartPreview( ( RightValue - RightMinValue ) / ( RightMaxValue - RightMinValue ) );

				PInvoke.SetCursorPos( _draggingCenter.X, _draggingCenter.Y );
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

	public static readonly DependencyProperty LeftMinValueProperty = DependencyProperty.Register( nameof( LeftMinValue ), typeof( float ), typeof( MairaDualSlider ), new PropertyMetadata( 0f ) );

	public float LeftMinValue
	{
		get => (float) GetValue( LeftMinValueProperty );
		set => SetValue( LeftMinValueProperty, value );
	}

	public static readonly DependencyProperty LeftMaxValueProperty = DependencyProperty.Register( nameof( LeftMaxValue ), typeof( float ), typeof( MairaDualSlider ), new PropertyMetadata( 1f ) );

	public float LeftMaxValue
	{
		get => (float) GetValue( LeftMaxValueProperty );
		set => SetValue( LeftMaxValueProperty, value );
	}

	public static readonly DependencyProperty RightMinValueProperty = DependencyProperty.Register( nameof( RightMinValue ), typeof( float ), typeof( MairaDualSlider ), new PropertyMetadata( 0f ) );

	public float RightMinValue
	{
		get => (float) GetValue( RightMinValueProperty );
		set => SetValue( RightMinValueProperty, value );
	}

	public static readonly DependencyProperty RightMaxValueProperty = DependencyProperty.Register( nameof( RightMaxValue ), typeof( float ), typeof( MairaDualSlider ), new PropertyMetadata( 1f ) );

	public float RightMaxValue
	{
		get => (float) GetValue( RightMaxValueProperty );
		set => SetValue( RightMaxValueProperty, value );
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

		_ = PInvoke.ShowCursor( true );

		Mouse.Capture( null );

		if ( !_isDraggingLeftHandle )
		{
			app.TyphoonWind.StopPreview();
		}
	}

	private void UpdateDragHandleVisuals()
	{
		var rowHeight = Grid.RowDefinitions[ 1 ].ActualHeight;

		var leftNormalized = ( LeftValue - LeftMinValue ) / ( LeftMaxValue - LeftMinValue );
		var rightNormalized = ( RightValue - RightMinValue ) / ( RightMaxValue - RightMinValue );

		LeftDragHandle_Image.Margin = new Thickness( 0, 0, 0, leftNormalized * ( rowHeight - LeftDragHandle_Image.ActualHeight ) );
		RightDragHandle_Image.Margin = new Thickness( 0, 0, 0, rightNormalized * ( rowHeight - RightDragHandle_Image.ActualHeight ) );
	}

	#endregion
}
