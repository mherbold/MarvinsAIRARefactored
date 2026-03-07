
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

		Loaded += MairaSwitch_Loaded;
	}

	#region Dependency Properties

	public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register( nameof( IsOn ), typeof( bool ), typeof( MairaSwitch ), new PropertyMetadata( false ) );

	public bool IsOn
	{
		get => (bool) GetValue( IsOnProperty );
		set => SetValue( IsOnProperty, value );
	}

	public static readonly DependencyProperty LabelProperty = DependencyProperty.Register( nameof( Label ), typeof( string ), typeof( MairaSwitch ), new PropertyMetadata( string.Empty ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
	}

	public static readonly DependencyProperty LabelPositionProperty = DependencyProperty.Register( nameof( LabelPosition ), typeof( string ), typeof( MairaSwitch ), new PropertyMetadata( "Right" ) );

	public string LabelPosition
	{
		get => (string) GetValue( LabelPositionProperty );
		set => SetValue( LabelPositionProperty, value );
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

	private void MairaSwitch_Loaded( object sender, RoutedEventArgs e )
	{
		Loaded -= MairaSwitch_Loaded;

		Toggled?.Invoke( this, EventArgs.Empty );
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
}
