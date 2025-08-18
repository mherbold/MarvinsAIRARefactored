
using MarvinsAIRARefactored.DataContext;
using System.Diagnostics;
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

public partial class UpdateContextSwitchesWindow : Window
{
	public UpdateContextSwitchesWindow( ContextSwitches contextSwitches )
	{
		var app = App.Instance!;

		app.MainWindow.MakeWindowVisible();

		InitializeComponent();

		DataContext = contextSwitches;
	}

	private void ThumbsUp_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Close();
	}

	private void Window_Closed( object sender, EventArgs e )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[UpdateContextSwitchesWindow] Window closed" );

		MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.UpdateSettings( true );
	}
}
