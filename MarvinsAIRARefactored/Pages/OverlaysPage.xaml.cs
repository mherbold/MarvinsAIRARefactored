using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class OverlaysPage : UserControl
{
	public OverlaysPage()
	{
		InitializeComponent();
	}

	private void ResetGapMonitorWindow_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.GapMonitorWindow?.ResetWindow();
	}
}
