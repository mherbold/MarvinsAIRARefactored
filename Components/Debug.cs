
using System.Runtime.CompilerServices;

namespace MarvinsAIRARefactored.Components;

public class Debug
{
	public string Label_1 { private get; set; } = string.Empty;
	public string Label_2 { private get; set; } = string.Empty;
	public string Label_3 { private get; set; } = string.Empty;
	public string Label_4 { private get; set; } = string.Empty;
	public string Label_5 { private get; set; } = string.Empty;
	public string Label_6 { private get; set; } = string.Empty;
	public string Label_7 { private get; set; } = string.Empty;
	public string Label_8 { private get; set; } = string.Empty;
	public string Label_9 { private get; set; } = string.Empty;
	public string Label_10 { private get; set; } = string.Empty;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Tick( App app )
	{
		if ( app.MainWindow.DebugTabItemIsVisible )
		{
			app.MainWindow.Debug_Label_1.Content = Label_1;
			app.MainWindow.Debug_Label_2.Content = Label_2;
			app.MainWindow.Debug_Label_3.Content = Label_3;
			app.MainWindow.Debug_Label_4.Content = Label_4;
			app.MainWindow.Debug_Label_5.Content = Label_5;
			app.MainWindow.Debug_Label_6.Content = Label_6;
			app.MainWindow.Debug_Label_7.Content = Label_7;
			app.MainWindow.Debug_Label_8.Content = Label_8;
			app.MainWindow.Debug_Label_9.Content = Label_9;
			app.MainWindow.Debug_Label_10.Content = Label_10;
		}
	}
}
