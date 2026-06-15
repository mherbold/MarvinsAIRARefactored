using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class OverlaysPage : UserControl
{
	public enum OverlaySection
	{
		GapMonitor,
		GripOMeter,
		SpeechToText
	}

	public OverlaysPage()
	{
		InitializeComponent();
	}

	private void ResetGapMonitorWindow_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.GapMonitorWindow?.ResetWindow();
	}

	private void ResetGripOMeterWindow_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.GripOMeterWindow?.ResetWindow();
	}

	private void ResetSpeechToTextWindow_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.SpeechToTextWindow?.ResetWindow();
	}

	private void MakeAllOverlaysDraggable_MairaSwitch_Toggled( object sender, EventArgs e )
	{
		var app = App.Instance!;

		app.OverlaysDraggable = MakeAllOverlaysDraggable_MairaSwitch.IsOn;

		app.UpdateGapMonitorWindowVisibility();
		app.UpdateGripOMeterWindowVisibility();
		app.UpdateSpeechToTextWindowVisibility();
	}

	public void ScrollToSection( ScrollViewer scrollViewer, OverlaySection section )
	{
		var targetElement = section switch
		{
			OverlaySection.GapMonitor => (FrameworkElement) GapMonitor_MairaGroupBox,
			OverlaySection.GripOMeter => GripOMeter_MairaGroupBox,
			OverlaySection.SpeechToText => SpeechToText_MairaGroupBox,
			_ => null
		};

		if ( targetElement == null )
		{
			return;
		}

		scrollViewer.UpdateLayout();

		var transform = targetElement.TransformToAncestor( scrollViewer );
		var targetPosition = transform.Transform( new System.Windows.Point( 0, 0 ) );

		scrollViewer.ScrollToVerticalOffset( scrollViewer.VerticalOffset + targetPosition.Y );
	}
}
