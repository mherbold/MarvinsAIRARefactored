
using Simagic;

namespace MarvinsAIRARefactored.Components;

public class Pedals
{
	private HPR _hpr = new HPR();

	public void Initialize()
	{
		var pedals = _hpr.Initialize();

		var app = App.Instance;

		app?.Dispatcher.BeginInvoke( () =>
		{
			switch ( pedals )
			{
				case HPR.Pedals.None:
					app.MainWindow.Pedals_Device_Label.Content = DataContext.Instance.Localization[ "PedalsNone" ];
					break;

				case HPR.Pedals.P1000:
					app.MainWindow.Pedals_Device_Label.Content = DataContext.Instance.Localization[ "PedalsP1000" ];
					break;

				case HPR.Pedals.P2000:
					app.MainWindow.Pedals_Device_Label.Content = DataContext.Instance.Localization[ "PedalsP2000" ];
					break;
			}
		} );
	}

	public void Tick( App app )
	{
	}
}
