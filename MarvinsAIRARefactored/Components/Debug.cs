
using System.Runtime.CompilerServices;

using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public class Debug
{
	private const int UpdateInterval = 6;

	public readonly string[] Message = [ string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty ];

	private int _updateCounter = UpdateInterval + 1;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter <= 0 )
		{
			_updateCounter = UpdateInterval;

			if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.Debug )
			{
				MainWindow._debugPage.Debug_TextBlock_1.Text = Message[ 0 ];
				MainWindow._debugPage.Debug_TextBlock_2.Text = Message[ 1 ];
				MainWindow._debugPage.Debug_TextBlock_3.Text = Message[ 2 ];
				MainWindow._debugPage.Debug_TextBlock_4.Text = Message[ 3 ];
				MainWindow._debugPage.Debug_TextBlock_5.Text = Message[ 4 ];
				MainWindow._debugPage.Debug_TextBlock_6.Text = Message[ 5 ];
				MainWindow._debugPage.Debug_TextBlock_7.Text = Message[ 6 ];
				MainWindow._debugPage.Debug_TextBlock_8.Text = Message[ 7 ];
				MainWindow._debugPage.Debug_TextBlock_9.Text = Message[ 8 ];
				MainWindow._debugPage.Debug_TextBlock_10.Text = Message[ 9 ];
				MainWindow._debugPage.Debug_TextBlock_11.Text = Message[ 10 ];
				MainWindow._debugPage.Debug_TextBlock_12.Text = Message[ 11 ];
				MainWindow._debugPage.Debug_TextBlock_13.Text = Message[ 12 ];
			}
		}
	}
}
