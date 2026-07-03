
using System.Windows;
using System.Windows.Input;

using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using Vector = System.Windows.Vector;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaTriangleBalance : UserControl
{
	// Triangle vertices in Triangle_Canvas coordinates - must match Triangle_Path.Data in the XAML.
	// A = sway (top), B = surge (bottom left), C = heave (bottom right).
	private static readonly Point VertexA = new( 100.0, 8.0 );
	private static readonly Point VertexB = new( 8.0, 152.0 );
	private static readonly Point VertexC = new( 192.0, 152.0 );

	private bool _isDragging = false;

	public MairaTriangleBalance()
	{
		InitializeComponent();

		Loaded += ( sender, e ) => UpdateDotVisual();
	}

	#region User Control Events

	private void Triangle_Canvas_MouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( e.ClickCount == 2 )
		{
			SwayWeight = 1f / 3f;
			SurgeWeight = 1f / 3f;

			return;
		}

		_isDragging = true;

		Triangle_Canvas.CaptureMouse();

		ApplyPointerPosition( e.GetPosition( Triangle_Canvas ) );
	}

	private void Triangle_Canvas_MouseMove( object sender, MouseEventArgs e )
	{
		if ( _isDragging )
		{
			ApplyPointerPosition( e.GetPosition( Triangle_Canvas ) );
		}
	}

	private void Triangle_Canvas_MouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( _isDragging )
		{
			EndDrag();
		}
	}

	private void Triangle_Canvas_LostMouseCapture( object sender, MouseEventArgs e )
	{
		if ( _isDragging )
		{
			_isDragging = false;
		}
	}

	private void Label_TextBlock_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		var app = App.Instance!;

		e.Handled = true;

		if ( ContextSwitches != null )
		{
			app.Logger.WriteLine( "[MairaTriangleBalance] Showing update context switches window" );

			var updateContextSwitchesWindow = new UpdateContextSwitchesWindow( ContextSwitches )
			{
				Owner = app.MainWindow
			};

			updateContextSwitchesWindow.ShowDialog();
		}
	}

	#endregion

	#region Dependency Properties

	public static readonly DependencyProperty LabelProperty = DependencyProperty.Register( nameof( Label ), typeof( string ), typeof( MairaTriangleBalance ), new PropertyMetadata( string.Empty ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
	}

	public static readonly DependencyProperty SwayWeightProperty = DependencyProperty.Register( nameof( SwayWeight ), typeof( float ), typeof( MairaTriangleBalance ), new FrameworkPropertyMetadata( 1f / 3f, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnWeightChanged ) );

	public float SwayWeight
	{
		get => (float) GetValue( SwayWeightProperty );
		set => SetValue( SwayWeightProperty, value );
	}

	public static readonly DependencyProperty SurgeWeightProperty = DependencyProperty.Register( nameof( SurgeWeight ), typeof( float ), typeof( MairaTriangleBalance ), new FrameworkPropertyMetadata( 1f / 3f, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnWeightChanged ) );

	public float SurgeWeight
	{
		get => (float) GetValue( SurgeWeightProperty );
		set => SetValue( SurgeWeightProperty, value );
	}

	public static readonly DependencyProperty ContextSwitchesProperty = DependencyProperty.Register( nameof( ContextSwitches ), typeof( ContextSwitches ), typeof( MairaTriangleBalance ), new PropertyMetadata( null ) );

	public ContextSwitches ContextSwitches
	{
		get => (ContextSwitches) GetValue( ContextSwitchesProperty );
		set => SetValue( ContextSwitchesProperty, value );
	}

	#endregion

	#region Dependency Property Changed Events

	private static void OnWeightChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaTriangleBalance mairaTriangleBalance )
		{
			mairaTriangleBalance.UpdateDotVisual();
		}
	}

	#endregion

	#region Logic

	private void EndDrag()
	{
		_isDragging = false;

		Triangle_Canvas.ReleaseMouseCapture();
	}

	// Convert the pointer position to barycentric weights (Cramer's rule), clamp into the triangle,
	// and push the sway / surge weights into the dependency properties (heave is the remainder).
	private void ApplyPointerPosition( Point position )
	{
		var v0 = VertexC - VertexA;
		var v1 = VertexB - VertexA;
		var v2 = position - VertexA;

		var d00 = v0 * v0;
		var d01 = v0 * v1;
		var d11 = v1 * v1;
		var d20 = v2 * v0;
		var d21 = v2 * v1;

		var denominator = d00 * d11 - d01 * d01;

		var heaveWeight = (float) ( ( d11 * d20 - d01 * d21 ) / denominator );
		var surgeWeight = (float) ( ( d00 * d21 - d01 * d20 ) / denominator );
		var swayWeight = 1f - heaveWeight - surgeWeight;

		swayWeight = MathF.Max( 0f, swayWeight );
		surgeWeight = MathF.Max( 0f, surgeWeight );
		heaveWeight = MathF.Max( 0f, heaveWeight );

		var weightSum = swayWeight + surgeWeight + heaveWeight;

		if ( weightSum > 0f )
		{
			swayWeight /= weightSum;
			surgeWeight /= weightSum;
		}
		else
		{
			swayWeight = 1f / 3f;
			surgeWeight = 1f / 3f;
		}

		SwayWeight = swayWeight;
		SurgeWeight = surgeWeight;
	}

	private void UpdateDotVisual()
	{
		var swayWeight = Math.Clamp( SwayWeight, 0f, 1f );
		var surgeWeight = Math.Clamp( SurgeWeight, 0f, 1f );
		var heaveWeight = MathF.Max( 0f, 1f - swayWeight - surgeWeight );

		var weightSum = swayWeight + surgeWeight + heaveWeight;

		if ( weightSum <= 0f )
		{
			swayWeight = surgeWeight = heaveWeight = 1f / 3f;
			weightSum = 1f;
		}

		swayWeight /= weightSum;
		surgeWeight /= weightSum;
		heaveWeight /= weightSum;

		var position = new Point(
			swayWeight * VertexA.X + surgeWeight * VertexB.X + heaveWeight * VertexC.X,
			swayWeight * VertexA.Y + surgeWeight * VertexB.Y + heaveWeight * VertexC.Y );

		System.Windows.Controls.Canvas.SetLeft( Dot_Ellipse, position.X - Dot_Ellipse.Width / 2.0 );
		System.Windows.Controls.Canvas.SetTop( Dot_Ellipse, position.Y - Dot_Ellipse.Height / 2.0 );
	}

	#endregion
}
