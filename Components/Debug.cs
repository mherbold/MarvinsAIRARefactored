
using Image = System.Windows.Controls.Image;

namespace MarvinsAIRARefactored.Components;

public class Debug
{
	private readonly Graph _debugGraph;
	private readonly Statistics _debugStatistics = new( 500 );

	public Debug( Image debugGraphImage )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( "[Debug] Constructor >>>" );

		_debugGraph = new Graph( debugGraphImage );

		app?.Logger.WriteLine( "[Debug] <<< Constructor" );
	}

	public void Update( float value )
	{
		var app = App.Instance;

		if ( app != null )
		{
			if ( app.MainWindow.DebugTabItemIsVisible )
			{
				_debugStatistics.Update( value );

				_debugGraph.DrawGradientLine( value, 255, 255, 255 );
				_debugGraph.Advance();
			}
		}
	}

	public void Tick( App app )
	{
		if ( app.MainWindow.DebugTabItemIsVisible )
		{
			_debugGraph.UpdateImage();

			app.MainWindow.Debug_MinMaxAvg.Content = $"{_debugStatistics.MinimumValue,5:F2} {_debugStatistics.MaximumValue,5:F2} {_debugStatistics.AverageValue,5:F2}";
			app.MainWindow.Debug_VarStdDev.Content = $"{_debugStatistics.Variance,5:F2} {_debugStatistics.StandardDeviation,5:F2}";

			app.MainWindow.Debug_Label.Content = $"{app.Simulator.SteeringWheelVelocity,10:F7}";
		}
	}
}
