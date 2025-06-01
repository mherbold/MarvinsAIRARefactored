
using System.Windows;

using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Windows;

public partial class UpdateButtonMappingsWindow : Window
{
	public static bool WindowIsOpen { get; private set; } = false;

	private readonly Settings.ButtonMappings _buttonMappings;

	public UpdateButtonMappingsWindow( Settings.ButtonMappings buttonMappings )
	{
		WindowIsOpen = true;

		InitializeComponent();

		_buttonMappings = buttonMappings;

		if ( _buttonMappings.MappedButtons.Count == 0 )
		{
			PlusButton_Click( this, new RoutedEventArgs() );
		}
		else
		{
			foreach ( var mappedButton in _buttonMappings.MappedButtons )
			{
				var buttonMapping = new ButtonMapping( mappedButton );

				StackPanel.Children.Insert( StackPanel.Children.Count - 1, buttonMapping );
			}
		}
	}

	private void PlusButton_Click( object sender, RoutedEventArgs e )
	{
		var mappedButton = new Settings.ButtonMappings.MappedButton();

		var buttonMapping = new ButtonMapping( mappedButton );

		StackPanel.Children.Insert( StackPanel.Children.Count - 1, buttonMapping );
	}

	private void Window_Closed( object sender, EventArgs e )
	{
		_buttonMappings.MappedButtons.Clear();

		foreach ( var child in StackPanel.Children )
		{
			if ( child is ButtonMapping buttonMapping )
			{
				buttonMapping.StopRecording();

				_buttonMappings.MappedButtons.Add( buttonMapping.MappedButton );
			}
		}

		var app = App.Instance;

		if ( app != null )
		{
			app.SettingsFile.QueueForSerialization = true;
		}

		WindowIsOpen = false;
	}
}
