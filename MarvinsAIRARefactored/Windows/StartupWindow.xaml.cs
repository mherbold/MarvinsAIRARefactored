using System.Windows;
using System.Windows.Threading;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Windows;

public partial class StartupWindow : Window
{
	public StartupWindow()
	{
		InitializeComponent();
		Version_TextBlock.Text = Misc.GetVersion();
	}

	public void UpdateProgress( double value, string statusText )
	{
		ArgumentNullException.ThrowIfNull( statusText );

		Startup_MairaProgressBar.Value = value;
		Status_TextBlock.Text = statusText;
		UpdateLayout();
		Dispatcher.Invoke( () => { }, DispatcherPriority.Render );
	}
}
