
using System.Windows;
using System.Windows.Controls;

using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaButtonMapping : UserControl
{
	public ButtonMappings.MappedButton MappedButton { get; private set; }

	private bool _isRecording = false;

	public MairaButtonMapping( ButtonMappings.MappedButton mappedButton )
	{
		MappedButton = mappedButton;

		InitializeComponent();

		UpdateLabels();

		Record_MairaButton.ButtonIcon_Image.Visibility = Visibility.Hidden;
	}

	private void Record_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( _isRecording )
		{
			StopRecording();
		}
		else
		{
			StartRecording();
		}
	}

	private void Trash_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( Parent is StackPanel stackPanel )
		{
			stackPanel.Children.Remove( this );
		}
	}

	private void StartRecording()
	{
		var app = App.Instance!;

		if ( !_isRecording )
		{
			_isRecording = true;

			MappedButton.ClickButton = new();
			MappedButton.HoldButton = new();

			app.DirectInput.OnInput += OnInput;

			Dispatcher.BeginInvoke( () =>
			{
				Record_MairaButton.Blink = true;
				Record_MairaButton.ButtonIcon_Image.Visibility = Visibility.Visible;
			} );

			UpdateLabels();
		}
	}

	public void StopRecording()
	{
		var app = App.Instance!;

		if ( _isRecording )
		{
			_isRecording = false;

			app.DirectInput.OnInput -= OnInput;

			Dispatcher.BeginInvoke( () =>
			{
				Record_MairaButton.Blink = false;
				Record_MairaButton.ButtonIcon_Image.Visibility = Visibility.Hidden;
			} );

			UpdateLabels();
		}
	}

	private void UpdateLabels()
	{
		Dispatcher.BeginInvoke( () =>
		{
			if ( _isRecording )
			{
				FirstButton_Label.Content = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "WaitingForInput" ];

				FirstButton_Label.Visibility = Visibility.Visible;
				SecondButton_Label.Visibility = Visibility.Collapsed;
			}
			else if ( MappedButton.ClickButton.DeviceInstanceGuid == Guid.Empty )
			{
				FirstButton_Label.Content = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "PressTheRecordButton" ];

				FirstButton_Label.Visibility = Visibility.Visible;
				SecondButton_Label.Visibility = Visibility.Collapsed;
			}
			else if ( MappedButton.HoldButton.DeviceInstanceGuid == Guid.Empty )
			{
				FirstButton_Label.Content = $"{MappedButton.ClickButton.DeviceProductName} {MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Button" ]} {MappedButton.ClickButton.ButtonNumber} {MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Click" ]}";

				FirstButton_Label.Visibility = Visibility.Visible;
				SecondButton_Label.Visibility = Visibility.Collapsed;
			}
			else
			{
				FirstButton_Label.Content = $"{MappedButton.HoldButton.DeviceProductName} {MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Button" ]} {MappedButton.HoldButton.ButtonNumber} {MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Hold" ]}";
				SecondButton_Label.Content = $"{MappedButton.ClickButton.DeviceProductName} {MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Button" ]} {MappedButton.ClickButton.ButtonNumber} {MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Click" ]}";

				FirstButton_Label.Visibility = Visibility.Visible;
				SecondButton_Label.Visibility = Visibility.Visible;
			}
		} );
	}

	private void OnInput( string deviceProductName, Guid deviceInstanceGuid, int buttonNumber, bool isPressed )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[ButtonMapping] OnInput: {deviceProductName}, {deviceInstanceGuid}, {buttonNumber}, {isPressed}" );

		if ( _isRecording )
		{
			if ( !isPressed )
			{
				StopRecording();
			}
			else if ( MappedButton.ClickButton.DeviceInstanceGuid == Guid.Empty )
			{
				MappedButton.ClickButton = new ButtonMappings.MappedButton.Button()
				{
					DeviceProductName = deviceProductName,
					DeviceInstanceGuid = deviceInstanceGuid,
					ButtonNumber = buttonNumber
				};
			}
			else if ( MappedButton.HoldButton.DeviceInstanceGuid == Guid.Empty )
			{
				MappedButton.HoldButton = MappedButton.ClickButton;

				MappedButton.ClickButton = new ButtonMappings.MappedButton.Button()
				{
					DeviceProductName = deviceProductName,
					DeviceInstanceGuid = deviceInstanceGuid,
					ButtonNumber = buttonNumber
				};
			}
		}
	}
}
