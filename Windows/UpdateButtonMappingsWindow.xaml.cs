
using System.Windows;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Windows;

public partial class UpdateButtonMappingsWindow : Window
{
	public static bool WindowIsOpen { get; private set; } = false;

	private readonly ButtonMappings _buttonMappings;

	public UpdateButtonMappingsWindow( ButtonMappings buttonMappings )
	{
		WindowIsOpen = true;

		var app = App.Instance!;

		app.MainWindow.MakeWindowVisible();

		InitializeComponent();

		_buttonMappings = buttonMappings;

		if ( _buttonMappings.MappedButtons.Count == 0 )
		{
			Plus_MairaButton_Click( this, new RoutedEventArgs() );
		}
		else
		{
			foreach ( var mappedButton in _buttonMappings.MappedButtons )
			{
				var buttonMapping = new MairaButtonMapping( mappedButton );

				StackPanel.Children.Insert( StackPanel.Children.Count - 1, buttonMapping );
			}
		}
	}

	private void Plus_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var mappedButton = new ButtonMappings.MappedButton();

		var buttonMapping = new MairaButtonMapping( mappedButton );

		StackPanel.Children.Insert( StackPanel.Children.Count - 1, buttonMapping );
	}

	private void ThumbsUp_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Close();
	}

	private void Window_Closed( object sender, EventArgs e )
	{
		_buttonMappings.MappedButtons.Clear();

		foreach ( var child in StackPanel.Children )
		{
			if ( child is MairaButtonMapping buttonMapping )
			{
				buttonMapping.StopRecording();

				_buttonMappings.MappedButtons.Add( buttonMapping.MappedButton );
			}
		}

		var app = App.Instance!;

		app.SettingsFile.QueueForSerialization = true;

		WindowIsOpen = false;
	}
}
