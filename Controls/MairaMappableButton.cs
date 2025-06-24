
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Controls;

public class MairaMappableButton : MairaButton
{
	public MairaMappableButton()
	{
		Label.PreviewMouseRightButtonDown += MappableMairaButton_Label_PreviewMouseRightButtonDown;
		Button.PreviewMouseRightButtonDown += MappableMairaButton_Button_PreviewMouseRightButtonDown;
	}

	public static readonly DependencyProperty ContextSwitchesProperty = DependencyProperty.Register( nameof( ContextSwitches ), typeof( ContextSwitches ), typeof( MairaMappableButton ), new PropertyMetadata( null ) );

	public ContextSwitches ContextSwitches
	{
		get => (ContextSwitches) GetValue( ContextSwitchesProperty );
		set => SetValue( ContextSwitchesProperty, value );
	}

	public static readonly DependencyProperty ButtonMappingsProperty = DependencyProperty.Register( nameof( ButtonMappings ), typeof( ButtonMappings ), typeof( MairaMappableButton ), new PropertyMetadata( null ) );

	public ButtonMappings ButtonMappings
	{
		get => (ButtonMappings) GetValue( ButtonMappingsProperty );
		set => SetValue( ButtonMappingsProperty, value );
	}

	private void MappableMairaButton_Label_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		var app = App.Instance!;

		e.Handled = true;

		if ( ContextSwitches != null )
		{
			app.Logger.WriteLine( "[MairaMappableButton] Showing update context switches window" );

			var updateContextSwitchesWindow = new UpdateContextSwitchesWindow( ContextSwitches )
			{
				Owner = app.MainWindow
			};

			updateContextSwitchesWindow.ShowDialog();
		}
	}

	private void MappableMairaButton_Button_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		var app = App.Instance!;

		e.Handled = true;

		if ( ButtonMappings != null )
		{
			app.Logger.WriteLine( "[MairaMappableButton] Showing update button mappings window" );

			var updateButtonMappingsWindow = new UpdateButtonMappingsWindow( ButtonMappings )
			{
				Owner = app.MainWindow
			};

			updateButtonMappingsWindow.ShowDialog();

			UpdateImageSources();
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
