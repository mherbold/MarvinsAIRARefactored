
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class TradingPaintsPage : UserControl
{
	public TradingPaintsPage()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void Redownload_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.TradingPaints.Reset();
	}

	#endregion
}
