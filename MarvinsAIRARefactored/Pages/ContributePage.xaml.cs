
using System.Diagnostics;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class ContributePage : UserControl
{
	public ContributePage()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void Hyperlink_RequestNavigate( object sender, System.Windows.Navigation.RequestNavigateEventArgs e )
	{
		Process.Start( new ProcessStartInfo( e.Uri.AbsoluteUri ) { UseShellExecute = true } );

		e.Handled = true;
	}

	#endregion
}
