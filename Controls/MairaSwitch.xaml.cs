
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaSwitch : UserControl
{
	public MairaSwitch()
	{
		InitializeComponent();
		UpdateVisualState();
	}

	#region Dependency Properties

	public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register( nameof( IsOn ), typeof( bool ), typeof( MairaSwitch ), new PropertyMetadata( false, OnIsOnChanged ) );

	public bool IsOn
	{
		get => (bool) GetValue( IsOnProperty );
		set => SetValue( IsOnProperty, value );
	}

	private static void OnIsOnChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		( (MairaSwitch) d ).UpdateVisualState();
		( (MairaSwitch) d ).Toggled?.Invoke( d, EventArgs.Empty );
	}

	public static readonly DependencyProperty TitleProperty = DependencyProperty.Register( nameof( Title ), typeof( string ), typeof( MairaSwitch ), new PropertyMetadata( string.Empty ) );

	public string Title
	{
		get => (string) GetValue( TitleProperty );
		set => SetValue( TitleProperty, value );
	}

	public static readonly DependencyProperty LabelPositionProperty = DependencyProperty.Register( nameof( LabelPosition ), typeof( string ), typeof( MairaSwitch ), new PropertyMetadata( "Right", OnLabelPositionChanged ) );

	public string LabelPosition
	{
		get => (string) GetValue( LabelPositionProperty );
		set => SetValue( LabelPositionProperty, value );
	}

	private static void OnLabelPositionChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		var control = d as MairaSwitch;

		control?.UpdateLayoutBasedOnLabelPosition();
	}

	public static readonly DependencyProperty ContextSwitchesProperty = DependencyProperty.Register( nameof( ContextSwitches ), typeof( ContextSwitches ), typeof( MairaSwitch ), new PropertyMetadata( null ) );

	public ContextSwitches ContextSwitches
	{
		get => (ContextSwitches) GetValue( ContextSwitchesProperty );
		set => SetValue( ContextSwitchesProperty, value );
	}

	#endregion

	#region Event Handlers

	public event EventHandler? Toggled;

	private void Button_Click( object sender, EventArgs e )
	{
		IsOn = !IsOn;

		Toggled?.Invoke( this, e );
	}

	private void TextBlock_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		var app = App.Instance!;

		e.Handled = true;

		if ( ContextSwitches != null )
		{
			app.Logger.WriteLine( "[MairaSwitch] Showing update context switches window" );

			var updateContextSwitchesWindow = new UpdateContextSwitchesWindow( ContextSwitches )
			{
				Owner = app.MainWindow
			};

			updateContextSwitchesWindow.ShowDialog();
		}
	}

	#endregion

	#region Logic

	private void UpdateLayoutBasedOnLabelPosition()
	{
		if ( LayoutRoot != null )
		{
			if ( LabelPosition == "Top" )
			{
				Grid.SetRow( TextBlock, 0 );
				Grid.SetColumn( TextBlock, 0 );
				Grid.SetRow( Button, 1 );
				Grid.SetColumn( Button, 0 );

				LayoutRoot.RowDefinitions.Clear();
				LayoutRoot.ColumnDefinitions.Clear();
				LayoutRoot.RowDefinitions.Add( new RowDefinition { Height = GridLength.Auto } );
				LayoutRoot.RowDefinitions.Add( new RowDefinition { Height = GridLength.Auto } );

				TextBlock.TextAlignment = TextAlignment.Center;
				TextBlock.Margin = new Thickness( 0, 0, 0, 7 );
				TextBlock.Padding = new Thickness( 5 );
			}
			else
			{
				Grid.SetRow( Button, 0 );
				Grid.SetColumn( Button, 0 );
				Grid.SetRow( TextBlock, 0 );
				Grid.SetColumn( TextBlock, 1 );

				LayoutRoot.RowDefinitions.Clear();
				LayoutRoot.ColumnDefinitions.Clear();
				LayoutRoot.ColumnDefinitions.Add( new ColumnDefinition { Width = GridLength.Auto } );
				LayoutRoot.ColumnDefinitions.Add( new ColumnDefinition { Width = GridLength.Auto } );

				TextBlock.TextAlignment = TextAlignment.Left;
			}
		}
	}

	private void UpdateVisualState()
	{
		OnImage.Visibility = IsOn ? Visibility.Visible : Visibility.Hidden;
		OffImage.Visibility = IsOn ? Visibility.Hidden : Visibility.Visible;
	}

	#endregion
}
