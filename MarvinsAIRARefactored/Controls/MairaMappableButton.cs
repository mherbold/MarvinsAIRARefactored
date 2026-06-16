
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Controls;

public class MairaMappableButton : MairaButton
{
	private static readonly ConditionalWeakTable<MairaMappableButton, object?> _instances = new();

	public MairaMappableButton()
	{
		Loaded += MairaMappableButton_Loaded;

		TextBlock.PreviewMouseRightButtonDown += MappableMairaButton_Label_PreviewMouseRightButtonDown;
		Button.PreviewMouseRightButtonDown += MappableMairaButton_Button_PreviewMouseRightButtonDown;

		_instances.Add( this, null );
	}

	// Re-evaluates the mapped (orange border) state for every mappable button in the app. The
	// per-button IsMapped state is only computed on Loaded and after the button's own right-click
	// mapping flow, so when mappings are changed elsewhere (e.g. the first-run wizard mutating the
	// same ButtonMappings objects) the already-loaded buttons would otherwise stay stale.
	public static void RefreshAll()
	{
		foreach ( var instance in _instances )
		{
			instance.Key.IsMapped = instance.Key.HasAnyMappedButton();
		}
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
