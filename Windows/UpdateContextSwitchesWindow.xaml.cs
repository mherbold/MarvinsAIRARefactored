
using System.Windows;

using MarvinsAIRARefactored.DataContext;

namespace MarvinsAIRARefactored.Windows;

public partial class UpdateContextSwitchesWindow : Window
{
	public UpdateContextSwitchesWindow( ContextSwitches contextSwitches )
	{
		InitializeComponent();

		DataContext = contextSwitches;
	}

	private void ThumbsUp_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Close();
	}
}
