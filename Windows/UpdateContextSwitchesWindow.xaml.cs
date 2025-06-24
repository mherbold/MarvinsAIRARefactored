
using System.Windows;

using MarvinsAIRARefactored.DataContext;

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
}
