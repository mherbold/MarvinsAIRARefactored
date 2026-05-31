
using System.Windows;
using System.Windows.Input;

namespace MarvinsAIRARefactored.Classes;

public static class HelpService
{
	public static string BaseUrl { get; set; } = "https://mairapp.com/home/documentation/";

	public static readonly DependencyProperty HelpTopicProperty = DependencyProperty.RegisterAttached( "HelpTopic", typeof( string ), typeof( HelpService ), new FrameworkPropertyMetadata( null, FrameworkPropertyMetadataOptions.Inherits ) );

	public static void SetHelpTopic( DependencyObject element, string? value ) => element.SetValue( HelpTopicProperty, value );
	public static string? GetHelpTopic( DependencyObject element ) => (string?) element.GetValue( HelpTopicProperty );

	public static ICommand OpenHelpCommand { get; } = new RoutedUICommand( "OpenHelp", "OpenHelp", typeof( HelpService ) );

	public static void ExecuteOpenHelp( object? parameter )
	{
		var topic = parameter as string;

		if ( string.IsNullOrWhiteSpace( topic ) ) return;

		var helpWindow = new Windows.HelpWindow( $"{BaseUrl}{topic}" )
		{
			Owner = App.Instance!.MainWindow
		};

		helpWindow.Show();
	}
}
