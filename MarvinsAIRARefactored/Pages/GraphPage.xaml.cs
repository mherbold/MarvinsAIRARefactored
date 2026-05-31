
using System.Windows;
using System.Windows.Input;

using Cursors = System.Windows.Input.Cursors;
using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Pages;

public partial class GraphPage : UserControl
{
	private bool _isDraggable = false;

	public GraphPage()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void Target_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		if ( BottomPanel_StackPanel.Visibility == Visibility.Visible )
		{
			Misc.ApplyToTaggedElements( app.MainWindow.Root, "HideWhenGraphIsSoloed", element => element.Visibility = Visibility.Collapsed );

			app.MainWindow.WindowStyle = WindowStyle.None;
			app.MainWindow.ResizeMode = ResizeMode.NoResize;
			app.MainWindow.SizeToContent = SizeToContent.Height;

			app.MainWindow.Grid.Margin = new Thickness( 0 );
			app.MainWindow.AppPage_ContentControl.Margin = new Thickness( 0 );

			Border.Cursor = Cursors.SizeAll;

			_isDraggable = true;
		}
		else
		{
			Misc.ApplyToTaggedElements( app.MainWindow.Root, "HideWhenGraphIsSoloed", element => element.Visibility = Visibility.Visible );

			app.MainWindow.WindowStyle = WindowStyle.SingleBorderWindow;
			app.MainWindow.ResizeMode = ResizeMode.CanResizeWithGrip;
			app.MainWindow.SizeToContent = SizeToContent.Manual;

			app.MainWindow.Grid.Margin = new Thickness( 0, 0, 0, 20 );
			app.MainWindow.AppPage_ContentControl.Margin = new Thickness( 20, 0, 20, 0 );

			Border.Cursor = null;

			_isDraggable = false;
		}
	}

	private void Border_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( _isDraggable )
		{
			App.Instance!.MainWindow.DragMove();
		}
	}

	#endregion
}
