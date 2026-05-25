
using System.Windows;
using System.Windows.Input;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Controls;

public class MairaMappableButton : MairaButton
{
	public MairaMappableButton()
	{
		Loaded += MairaMappableButton_Loaded;

		TextBlock.PreviewMouseRightButtonDown += MappableMairaButton_Label_PreviewMouseRightButtonDown;
		Button.PreviewMouseRightButtonDown += MappableMairaButton_Button_PreviewMouseRightButtonDown;
	}

	#region User Control Events

	private void MairaMappableButton_Loaded( object sender, RoutedEventArgs e )
	{
		IsMapped = HasAnyMappedButton();
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

			IsMapped = HasAnyMappedButton();
		}
	}

	#endregion

	#region Dependency Properties

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

	#endregion

	#region Logic

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

	#endregion
}
