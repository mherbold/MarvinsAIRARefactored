
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Windows;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MarvinsAIRARefactored.Controls;

public class MappableImageButton : ImageButton
{
	public MappableImageButton()
	{
		PreviewMouseRightButtonDown += MappableImageButton_PreviewMouseRightButtonDown;
	}

	public static readonly DependencyProperty ButtonMappingsProperty = DependencyProperty.Register( nameof( ButtonMappings ), typeof( Settings.ButtonMappings ), typeof( MappableImageButton ), new PropertyMetadata( null ) );

	public Settings.ButtonMappings ButtonMappings
	{
		get => (Settings.ButtonMappings) GetValue( ButtonMappingsProperty );
		set => SetValue( ButtonMappingsProperty, value );
	}

	private void MappableImageButton_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		OnRightClick( e );
	}

	private void OnRightClick( MouseButtonEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			e.Handled = true;

			if ( ButtonMappings != null )
			{
				var updateButtonMappingsWindow = new UpdateButtonMappingsWindow( ButtonMappings )
				{
					Owner = app.MainWindow
				};

				updateButtonMappingsWindow.ShowDialog();

				UpdateImageSources();
			}
		}
	}

	private bool HasAnyMappedButton()
	{
		if ( ( ButtonMappings != null ) && ( ButtonMappings.MappedButtons.Count > 0 ) )
		{
			foreach ( var mappedButton in ButtonMappings.MappedButtons )
			{
				if ( mappedButton.ClickButton.DeviceInstanceGuid != Guid.Empty )
				{
					return true;
				}
			}
		}

		return false;
	}

	protected override void UpdateImageSources()
	{
		base.UpdateImageSources();

		if ( Small )
		{
			if ( HasAnyMappedButton() )
			{
				Normal_Image.Source = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/round_button_mapped_small.png" ) as ImageSource;
			}
		}
		else
		{
			if ( HasAnyMappedButton() )
			{
				Normal_Image.Source = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/round_button_mapped.png" ) as ImageSource;
			}
		}
	}
}
