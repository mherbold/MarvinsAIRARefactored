
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Controls;

// A MairaSwitch that can have a physical input mapped to it (toggles the switch). Mirrors MairaMappableButton:
// right-clicking the checkbox opens the mapping window, and a mapped switch shows the orange border (IsMapped,
// defined on the MairaSwitch base). The label keeps its base-class right-click behavior (context switches).
public class MairaMappableSwitch : MairaSwitch
{
	private static readonly ConditionalWeakTable<MairaMappableSwitch, object?> _instances = new();

	public MairaMappableSwitch()
	{
		Loaded += MairaMappableSwitch_Loaded;

		Button.PreviewMouseRightButtonDown += MappableMairaSwitch_Button_PreviewMouseRightButtonDown;

		_instances.Add( this, null );
	}

	// Re-evaluates the mapped (orange border) state for every mappable switch in the app. The per-switch
	// IsMapped state is only computed on Loaded and after the switch's own right-click mapping flow, so when
	// mappings are changed elsewhere (e.g. switching controller profiles) the already-loaded switches would
	// otherwise stay stale.
	public static void RefreshAll()
	{
		foreach ( var instance in _instances )
		{
			instance.Key.IsMapped = instance.Key.HasAnyMappedButton();
		}
	}

	#region User Control Events

	private void MairaMappableSwitch_Loaded( object sender, RoutedEventArgs e )
	{
		IsMapped = HasAnyMappedButton();
	}

	private void MappableMairaSwitch_Button_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		var app = App.Instance!;

		e.Handled = true;

		if ( ButtonMappings != null )
		{
			app.Logger.WriteLine( "[MairaMappableSwitch] Showing update button mappings window" );

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

	public static readonly DependencyProperty ButtonMappingsProperty = DependencyProperty.Register( nameof( ButtonMappings ), typeof( ButtonMappings ), typeof( MairaMappableSwitch ), new PropertyMetadata( null ) );

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
