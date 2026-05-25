
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace MarvinsAIRARefactored.Windows
{
	public partial class HelpWindow : Window
	{
		private readonly string _url;

		public HelpWindow( string url )
		{
			InitializeComponent();

			_url = url;

			var hyperlink = new Hyperlink( new Run( _url ) ) { NavigateUri = new Uri( _url ) };

			hyperlink.RequestNavigate += ( _, e ) =>
			{
				try
				{
					Process.Start( new ProcessStartInfo( e.Uri.AbsoluteUri ) { UseShellExecute = true } );
				}
				catch
				{
				}

				e.Handled = true;
			};

			UrlText.Inlines.Clear();
			UrlText.Inlines.Add( hyperlink );

			Loaded += HelpWindow_Loaded;
		}

		private async void HelpWindow_Loaded( object sender, RoutedEventArgs e )
		{
			try
			{
				await Browser.EnsureCoreWebView2Async();

				Browser.Source = new Uri( _url );
			}
			catch
			{
			}
		}

		private void Close_Click( object sender, RoutedEventArgs e ) => Close();
	}
}
