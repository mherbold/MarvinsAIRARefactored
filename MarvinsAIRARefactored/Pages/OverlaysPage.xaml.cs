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
		DeltaMonitor,
		GripOMeter,
		SpeechToText
	}

	public OverlaysPage()
	{
		InitializeComponent();
	}

	private void ResetAllOverlays_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		// reset all overlay window scales to 100%
		settings.OverlaysGapMonitorWindowScale = 1f;
		settings.OverlaysDeltaMonitorWindowScale = 1f;
		settings.OverlaysGripOMeterWindowScale = 1f;
		settings.OverlaysSpeechToTextWindowScale = 1f;

		// reset all overlay window positions to 0,0 (persisted for windows that aren't currently open)
		settings.OverlaysGapMonitorWindowPosition = System.Drawing.Rectangle.Empty;
		settings.OverlaysDeltaMonitorWindowPosition = System.Drawing.Rectangle.Empty;
		settings.OverlaysGripOMeterWindowPosition = System.Drawing.Rectangle.Empty;
		settings.OverlaysSpeechToTextWindowPosition = System.Drawing.Rectangle.Empty;

		// move any open windows to 0,0 immediately
		app.GapMonitorWindow?.ResetWindow();
		app.DeltaMonitorWindow?.ResetWindow();
		app.GripOMeterWindow?.ResetWindow();
		app.SpeechToTextWindow?.ResetWindow();
	}

	private void MakeAllOverlaysDraggable_MairaSwitch_Toggled( object sender, EventArgs e )
	{
		var app = App.Instance!;

		app.OverlaysDraggable = MakeAllOverlaysDraggable_MairaSwitch.IsOn;

		app.UpdateGapMonitorWindowVisibility();
		app.UpdateDeltaMonitorWindowVisibility();
		app.UpdateGripOMeterWindowVisibility();
		app.UpdateSpeechToTextWindowVisibility();
	}

	public void ScrollToSection( ScrollViewer scrollViewer, OverlaySection section )
	{
		var targetElement = section switch
		{
			OverlaySection.GapMonitor => (FrameworkElement) GapMonitor_MairaGroupBox,
			OverlaySection.DeltaMonitor => DeltaMonitor_MairaGroupBox,
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
