
using System.Windows.Input;

using UserControl = System.Windows.Controls.UserControl;
using ScrollEventArgs = System.Windows.Controls.Primitives.ScrollEventArgs;

namespace MarvinsAIRARefactored.Pages;

public partial class SimulatorPage : UserControl
{
	public SimulatorPage()
	{
		InitializeComponent();

		HeaderData_HeaderDataViewer.Initialize( HeaderData_ScrollBar );
		SessionInfo_SessionInfoViewer.Initialize( SessionInfo_ScrollBar );
		TelemetryData_TelemetryDataViewer.Initialize( TelemetryData_ScrollBar );
	}

	#region User Control Events

	private void HeaderData_HeaderDataViewer_MouseWheel( object sender, MouseWheelEventArgs e )
	{
		var delta = e.Delta / 30.0f;

		if ( delta > 0 )
		{
			delta = MathF.Max( 1, delta );
		}
		else
		{
			delta = MathF.Min( -1, delta );
		}

		HeaderData_ScrollBar.Value -= delta;

		HeaderData_HeaderDataViewer.ScrollIndex = (int) HeaderData_ScrollBar.Value;
	}

	private void HeaderData_ScrollBar_Scroll( object sender, ScrollEventArgs e )
	{
		HeaderData_HeaderDataViewer.ScrollIndex = (int) e.NewValue;
	}

	private void SessionInfo_SessionInfoViewer_MouseWheel( object sender, MouseWheelEventArgs e )
	{
		var delta = e.Delta / 30.0f;

		if ( delta > 0 )
		{
			delta = MathF.Max( 1, delta );
		}
		else
		{
			delta = MathF.Min( -1, delta );
		}

		SessionInfo_ScrollBar.Value -= delta;

		SessionInfo_SessionInfoViewer.ScrollIndex = (int) SessionInfo_ScrollBar.Value;
	}

	private void SessionInfo_ScrollBar_Scroll( object sender, ScrollEventArgs e )
	{
		SessionInfo_SessionInfoViewer.ScrollIndex = (int) e.NewValue;
	}

	private void TelemetryData_TelemetryDataViewer_MouseWheel( object sender, MouseWheelEventArgs e )
	{
		var delta = e.Delta / 30.0f;

		if ( delta > 0 )
		{
			delta = MathF.Max( 1, delta );
		}
		else
		{
			delta = MathF.Min( -1, delta );
		}

		TelemetryData_ScrollBar.Value -= delta;

		TelemetryData_TelemetryDataViewer.ScrollIndex = (int) TelemetryData_ScrollBar.Value;
	}

	private void TelemetryData_ScrollBar_Scroll( object sender, ScrollEventArgs e )
	{
		TelemetryData_TelemetryDataViewer.ScrollIndex = (int) e.NewValue;
	}

	#endregion
}
