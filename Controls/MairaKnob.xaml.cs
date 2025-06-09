
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaKnob : UserControl
{
	private Point _lastMousePosition;
	private bool _isDragging = false;
	private readonly RotateTransform _knobRotation = new( 0, 0.5, 0.5 );

	public MairaKnob()
	{
		InitializeComponent();

		KnobImage.RenderTransform = _knobRotation;

		UpdateLabelVisual();
	}

	#region Dependency Properties

	public static readonly DependencyProperty TitleProperty = DependencyProperty.Register( nameof( Title ), typeof( string ), typeof( MairaKnob ), new PropertyMetadata( string.Empty, OnTitleChanged ) );

	public string Title
	{
		get => (string) GetValue( TitleProperty );
		set => SetValue( TitleProperty, value );
	}

	private static void OnTitleChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		var control = (MairaKnob) d;

		control.UpdateLabelVisual();
	}

	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register( nameof( Value ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 0f, OnValueChanged ) );

	public float Value
	{
		get => (float) GetValue( ValueProperty );
		set => SetValue( ValueProperty, value );
	}

	private static void OnValueChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		var control = (MairaKnob) d;

		control.UpdateKnobVisual( (float) e.OldValue, (float) e.NewValue );
	}

	public static readonly DependencyProperty ValueStringProperty = DependencyProperty.Register( nameof( ValueString ), typeof( string ), typeof( MairaKnob ), new PropertyMetadata( "0" ) );

	public string ValueString
	{
		get => (string) GetValue( ValueStringProperty );
		set => SetValue( ValueStringProperty, value );
	}

	public static readonly DependencyProperty SmallValueChangeStepProperty = DependencyProperty.Register( nameof( SmallValueChangeStep ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 0.01f ) );

	public float SmallValueChangeStep
	{
		get => (float) GetValue( SmallValueChangeStepProperty );
		set => SetValue( SmallValueChangeStepProperty, value );
	}

	public static readonly DependencyProperty LargeValueChangeStepProperty = DependencyProperty.Register( nameof( LargeValueChangeStep ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 0.1f ) );

	public float LargeValueChangeStep
	{
		get => (float) GetValue( LargeValueChangeStepProperty );
		set => SetValue( LargeValueChangeStepProperty, value );
	}

	public static readonly DependencyProperty RotationMultiplierProperty = DependencyProperty.Register( nameof( RotationMultiplier ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 1f ) );

	public float RotationMultiplier
	{
		get => (float) GetValue( RotationMultiplierProperty );
		set => SetValue( RotationMultiplierProperty, value );
	}

	public static readonly DependencyProperty ValueChangedCallbackProperty = DependencyProperty.Register( nameof( ValueChangedCallback ), typeof( Action<float> ), typeof( MairaKnob ) );

	public Action<float> ValueChangedCallback
	{
		get => (Action<float>) GetValue( ValueChangedCallbackProperty );
		set => SetValue( ValueChangedCallbackProperty, value );
	}

	public static readonly DependencyProperty ContextSwitchesProperty = DependencyProperty.Register( nameof( ContextSwitches ), typeof( ContextSwitches ), typeof( MairaKnob ), new PropertyMetadata( null ) );

	public ContextSwitches ContextSwitches
	{
		get => (ContextSwitches) GetValue( ContextSwitchesProperty );
		set => SetValue( ContextSwitchesProperty, value );
	}

	public static readonly DependencyProperty PlusButtonMappingsProperty = DependencyProperty.Register( nameof( PlusButtonMappings ), typeof( ButtonMappings ), typeof( MairaKnob ) );

	public ButtonMappings PlusButtonMappings
	{
		get => (ButtonMappings) GetValue( PlusButtonMappingsProperty );
		set => SetValue( PlusButtonMappingsProperty, value );
	}

	public static readonly DependencyProperty MinusButtonMappingsProperty = DependencyProperty.Register( nameof( MinusButtonMappings ), typeof( ButtonMappings ), typeof( MairaKnob ) );

	public ButtonMappings MinusButtonMappings
	{
		get => (ButtonMappings) GetValue( MinusButtonMappingsProperty );
		set => SetValue( MinusButtonMappingsProperty, value );
	}

	#endregion

	#region Event Handlers

	private void KnobImage_Image_MouseDown( object sender, MouseButtonEventArgs e )
	{
		Mouse.Capture( (Image) sender );
		Mouse.OverrideCursor = Cursors.SizeWE;

		_lastMousePosition = e.GetPosition( null );

		_isDragging = true;
	}

	private void KnobImage_Image_MouseUp( object sender, MouseButtonEventArgs e )
	{
		if ( _isDragging )
		{
			Mouse.Capture( null );
			Mouse.OverrideCursor = null;

			_isDragging = false;
		}
	}

	private void KnobImage_Image_MouseMove( object sender, MouseEventArgs e )
	{
		if ( _isDragging )
		{
			var newPosition = e.GetPosition( null );

			var delta = ( newPosition.X - _lastMousePosition.X ) + ( newPosition.Y - _lastMousePosition.Y );

			_lastMousePosition = newPosition;

			AdjustValue( (float) delta * SmallValueChangeStep );
		}
	}

	private void Plus_MairaMappableButton_Click( object sender, RoutedEventArgs e ) => AdjustValue( LargeValueChangeStep );
	private void Minus_MairaMappableButton_Click( object sender, RoutedEventArgs e ) => AdjustValue( -LargeValueChangeStep );

	private void Label_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			e.Handled = true;

			if ( ContextSwitches != null )
			{
				app.Logger.WriteLine( "[MairaKnob] Showing update context switches window" );

				var updateContextSwitchesWindow = new UpdateContextSwitchesWindow( ContextSwitches )
				{
					Owner = app.MainWindow
				};

				updateContextSwitchesWindow.ShowDialog();
			}
		}
	}

	#endregion

	#region Logic

	private void AdjustValue( float amount )
	{
		float oldValue = Value;
		float newValue = oldValue + amount;

		Value = newValue;

		ValueChangedCallback?.Invoke( newValue );
	}

	private void UpdateLabelVisual()
	{
		if ( Title == string.Empty )
		{
			Label.Visibility = Visibility.Collapsed;
		}
		else
		{
			Label.Visibility = Visibility.Visible;
		}
	}

	private void UpdateKnobVisual( float oldValue, float newValue )
	{
		float delta = newValue - oldValue;

		_knobRotation.Angle += delta * RotationMultiplier * 50f;
	}

	#endregion
}
