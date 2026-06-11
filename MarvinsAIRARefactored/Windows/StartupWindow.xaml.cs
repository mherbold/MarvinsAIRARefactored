using System.Windows;
using System.Windows.Threading;

namespace MarvinsAIRARefactored.Windows;

public partial class StartupWindow : Window
{
	public StartupWindow()
	{
		InitializeComponent();
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
