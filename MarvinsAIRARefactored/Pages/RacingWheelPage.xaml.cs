
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.FFB;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Pages;

public partial class RacingWheelPage : UserControl
{
	private const double PreviewZoomSize = 256.0;
	private const double PreviewZoomFactor = 6.0;
	private const double PreviewZoomPopupOffset = 32.0;

	public RacingWheelPage()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void Power_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelEnableForceFeedback = !MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelEnableForceFeedback;
	}

	private void Test_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.RacingWheel.PlayTestSignal = true;
	}

	private void Reset_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.RacingWheel.ResetForceFeedback = true;
	}

	private void Set_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.RacingWheel.AutoSetMaxForce = true;
	}

	private void Clear_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.RacingWheel.ClearPeakTorque = true;
	}

	private void Preview_ScrollViewer_PreviewMouseWheel( object sender, MouseWheelEventArgs e )
	{
		e.Handled = true;

		var eventArg = new MouseWheelEventArgs( e.MouseDevice, e.Timestamp, e.Delta )
		{
			RoutedEvent = MouseWheelEvent,
			Source = sender
		};

		var parent = ( (ScrollViewer) sender ).Parent as UIElement;

		parent?.RaiseEvent( eventArg );
	}

	private void Preview_ScrollViewer_Loaded( object sender, RoutedEventArgs e )
	{
		var scrollViewer = (ScrollViewer) sender;
		var half = scrollViewer.ScrollableWidth / 2;

		scrollViewer.ScrollToHorizontalOffset( half );
	}

	private void AlgorithmPreview_Image_MouseEnter( object sender, MouseEventArgs e )
	{
		if ( AlgorithmPreview_Image.Source == null )
		{
			return;
		}

		var cursorPosition = e.GetPosition( AlgorithmPreview_Image );

		UpdatePreviewZoom( cursorPosition );
		UpdatePreviewPopupPosition( cursorPosition );

		PreviewZoom_Popup.IsOpen = true;
	}

	private void AlgorithmPreview_Image_MouseLeave( object sender, MouseEventArgs e )
	{
		PreviewZoom_Popup.IsOpen = false;
	}

	private void AlgorithmPreview_Image_MouseMove( object sender, MouseEventArgs e )
	{
		if ( !PreviewZoom_Popup.IsOpen )
		{
			return;
		}

		var cursorPosition = e.GetPosition( AlgorithmPreview_Image );

		UpdatePreviewZoom( cursorPosition );
		UpdatePreviewPopupPosition( cursorPosition );
	}

	private void UpdatePreviewZoom( Point position )
	{
		var imageWidth = AlgorithmPreview_Image.ActualWidth;
		var imageHeight = AlgorithmPreview_Image.ActualHeight;

		if ( imageWidth <= 0d || imageHeight <= 0d )
		{
			return;
		}

		var regionWidth = PreviewZoomSize / PreviewZoomFactor;
		var regionHeight = PreviewZoomSize / PreviewZoomFactor;

		var halfRegionWidth = regionWidth / 2d;
		var halfRegionHeight = regionHeight / 2d;

		var left = position.X - halfRegionWidth;
		var top = position.Y - halfRegionHeight;

		if ( left < 0d )
		{
			left = 0d;
		}

		if ( top < 0d )
		{
			top = 0d;
		}

		if ( left + regionWidth > imageWidth )
		{
			left = imageWidth - regionWidth;
		}

		if ( top + regionHeight > imageHeight )
		{
			top = imageHeight - regionHeight;
		}

		var xNorm = left / imageWidth;
		var yNorm = top / imageHeight;
		var wNorm = regionWidth / imageWidth;
		var hNorm = regionHeight / imageHeight;

		PreviewZoom_Brush.Viewbox = new Rect( xNorm, yNorm, wNorm, hNorm );
	}

	private void UpdatePreviewPopupPosition( Point cursorPosition )
	{
		PreviewZoom_Popup.HorizontalOffset = cursorPosition.X + PreviewZoomPopupOffset;
		PreviewZoom_Popup.VerticalOffset = cursorPosition.Y + PreviewZoomPopupOffset;
	}

	private void StartRecording_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.RecordingManager.StartRecording();
	}

	#endregion

	#region Logic

	public void UpdateSteeringDeviceOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RacingWheelPage] UpdateSteeringDeviceOptions >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<Guid, string>();

		if ( app.DirectInput.ForceFeedbackDeviceList.Count == 0 )
		{
			dictionary.Add( Guid.Empty, localization[ "NoFFBDevicesFound" ] );
		}
		else
		{
			dictionary.Add( Guid.Empty, localization[ "FFBDeviceNotSelected" ] );
		}

		app.DirectInput.ForceFeedbackDeviceList.ToList().ForEach( keyValuePair => dictionary[ keyValuePair.Key ] = keyValuePair.Value );

		if ( !dictionary.ContainsKey( settings.RacingWheelSteeringDeviceGuid ) )
		{
			dictionary.Add( settings.RacingWheelSteeringDeviceGuid, $"{localization[ "DeviceNotFound" ]} [{settings.RacingWheelSteeringDeviceGuid}]" );
		}

		SteeringDevice_MairaComboBox.ItemsSource = dictionary.OrderBy( keyValuePair => keyValuePair.Value ).ToList();
		SteeringDevice_MairaComboBox.OffValue = Guid.Empty;

		app.Logger.WriteLine( "[RacingWheelPage] <<< UpdateSteeringDeviceOptions" );
	}

	private bool _refreshingStackSelector = false;

	public void UpdateFFBStackOptions()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_refreshingStackSelector = true;

		var items = new List<KeyValuePair<string, string>>();

		foreach ( var stackName in settings.RacingWheelStacks.Keys )
		{
			items.Add( new KeyValuePair<string, string>( stackName, stackName ) );
		}

		Stack_MairaComboBox.ItemsSource = items;
		Stack_MairaComboBox.SelectedValue = settings.RacingWheelSelectedStackName;

		var isBuiltIn = settings.RacingWheelStacks.TryGetValue( settings.RacingWheelSelectedStackName, out var stack ) && stack.IsBuiltIn;

		// Built-in stacks cannot be renamed or deleted, but can be reset to their defaults.
		RenameStack_MairaButton.Disabled = isBuiltIn;
		DeleteStack_MairaButton.Disabled = isBuiltIn;
		ResetStack_MairaButton.Visibility = isBuiltIn ? Visibility.Visible : Visibility.Collapsed;

		_refreshingStackSelector = false;

		// Rebuild the module cards so their (localized) module names and setting labels refresh — this runs from
		// MainWindow.RefreshWindow, which is the app's relocalization entry point, so the editor follows a
		// runtime language switch.
		MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelStackViewModel.RebuildFromCurrentSelection();
	}

	private void Stack_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( _refreshingStackSelector )
		{
			return;
		}

		if ( Stack_MairaComboBox.SelectedValue is not string stackName )
		{
			return;
		}

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( stackName == settings.RacingWheelSelectedStackName )
		{
			return;
		}

		settings.SelectFFBStack( stackName );

		App.Instance!.SettingsFile.QueueForSerialization = true;

		UpdateFFBStackOptions();
	}

	private void NewStack_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var window = new RenameControllerProfileWindow( string.Empty ) { Owner = app.MainWindow };

		window.ShowDialog();

		if ( !window.Confirmed )
		{
			return;
		}

		var name = window.ProfileName.Trim();

		if ( ( name == string.Empty ) || settings.RacingWheelStacks.ContainsKey( name ) )
		{
			return;
		}

		settings.CreateFFBStack( name, copyFromCurrent: true );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBStackOptions();
	}

	private void RenameStack_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var currentName = settings.RacingWheelSelectedStackName;

		if ( settings.RacingWheelStacks.TryGetValue( currentName, out var currentStack ) && currentStack.IsBuiltIn )
		{
			return;
		}

		var window = new RenameControllerProfileWindow( currentName ) { Owner = app.MainWindow };

		window.ShowDialog();

		if ( !window.Confirmed )
		{
			return;
		}

		var newName = window.ProfileName.Trim();

		if ( ( newName == string.Empty ) || ( newName == currentName ) || settings.RacingWheelStacks.ContainsKey( newName ) )
		{
			return;
		}

		settings.RenameFFBStack( currentName, newName );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBStackOptions();
	}

	private void DeleteStack_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var currentName = settings.RacingWheelSelectedStackName;

		if ( settings.RacingWheelStacks.TryGetValue( currentName, out var currentStack ) && currentStack.IsBuiltIn )
		{
			return;
		}

		var window = new DeleteControllerProfileWindow( currentName ) { Owner = app.MainWindow };

		window.ShowDialog();

		if ( !window.Confirmed )
		{
			return;
		}

		settings.DeleteFFBStack( currentName );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBStackOptions();
	}

	private void ResetStack_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.ResetBuiltInFFBStack( settings.RacingWheelSelectedStackName );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBStackOptions();
	}

	private void StackValuesScope_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var window = new UpdateContextSwitchesWindow( settings.RacingWheelStackValuesContextSwitches ) { Owner = app.MainWindow };

		window.ShowDialog();
	}

	private void AddModule_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelStackViewModel.AddSelectedModule();
	}

	private void RemoveModule_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( ( sender is MairaButton mairaButton ) && ( mairaButton.Tag is FFBModuleViewModel moduleViewModel ) )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelStackViewModel.RemoveModule( moduleViewModel );
		}
	}

	private void MoveModuleUp_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( ( sender is MairaButton mairaButton ) && ( mairaButton.Tag is FFBModuleViewModel moduleViewModel ) )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelStackViewModel.MoveModule( moduleViewModel, -1 );
		}
	}

	private void MoveModuleDown_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( ( sender is MairaButton mairaButton ) && ( mairaButton.Tag is FFBModuleViewModel moduleViewModel ) )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelStackViewModel.MoveModule( moduleViewModel, 1 );
		}
	}

	public void UpdatePreviewRecordingsOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RacingWheelPage] UpdatePreviewRecordingsOptions >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var dictionary = new Dictionary<string, string>();

		if ( app.RecordingManager.Recordings.Count == 0 )
		{
			dictionary.Add( string.Empty, localization[ "NoRecordingsFound" ] );
		}

		foreach ( var recording in app.RecordingManager.Recordings )
		{
			dictionary.Add( recording.Key, recording.Value.Description! );
		}

		app.Dispatcher.Invoke( () =>
		{
			PreviewRecordings_MairaComboBox.ItemsSource = dictionary.OrderBy( keyValuePair => keyValuePair.Value ).ToList();
		} );

		app.Logger.WriteLine( "[RacingWheelPage] <<< UpdatePreviewRecordingsOptions" );
	}

	public void UpdateLFERecordingDeviceOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RacingWheelPage] UpdateLFERecordingDeviceOptions >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<string, string>
		{
			{ Components.LFE.DisabledDeviceName, localization[ "Disabled" ] }
		};

		foreach ( var deviceName in app.LFE.CaptureDeviceNames )
		{
			dictionary[ deviceName ] = deviceName;
		}

		if ( !string.IsNullOrEmpty( settings.RacingWheelLFERecordingDeviceName ) && !dictionary.ContainsKey( settings.RacingWheelLFERecordingDeviceName ) )
		{
			dictionary.Add( settings.RacingWheelLFERecordingDeviceName, $"{localization[ "DeviceNotFound" ]} [{settings.RacingWheelLFERecordingDeviceName}]" );
		}

		LFERecordingDevice_MairaComboBox.ItemsSource = dictionary.OrderBy( keyValuePair => keyValuePair.Value ).ToList();
		LFERecordingDevice_MairaComboBox.OffValue = Components.LFE.DisabledDeviceName;

		app.Logger.WriteLine( "[RacingWheelPage] <<< UpdateLFERecordingDeviceOptions" );
	}

	public void UpdateSteeringDeviceSection()
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		app.Dispatcher.Invoke( () =>
		{
			// update power button

			ImageSource? imageSource;

			var blink = false;

			if ( !settings.RacingWheelEnableForceFeedback )
			{
				imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/power-red.png" ) as ImageSource;

				blink = true;
			}
			else if ( !app.Simulator.IsConnected )
			{
				imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/power-blue.png" ) as ImageSource;
			}
			else if ( !app.DirectInput.ForceFeedbackInitialized )
			{
				imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/power-yellow.png" ) as ImageSource;
			}
			else
			{
				imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/power-green.png" ) as ImageSource;
			}

			if ( imageSource != null )
			{
				Power_MairaMappableButton.Icon = imageSource;
				Power_MairaMappableButton.Blink = blink;
			}

			// update test, reset, set, and clear buttons

			var disabled = !app.DirectInput.ForceFeedbackInitialized;

			Test_MairaMappableButton.Disabled = disabled;
			Reset_MairaMappableButton.Disabled = disabled;
			Set_MairaMappableButton.Disabled = disabled;
			Clear_MairaMappableButton.Disabled = disabled;

			// update steering device error message

			if ( app.DirectInput.ForceFeedbackInitialized )
			{
				SteeringDeviceFaultReason_TextBlock.Visibility = Visibility.Collapsed;
			}
			else
			{
				if ( !settings.RacingWheelEnableForceFeedback )
				{
					SteeringDeviceFaultReason_TextBlock.Text = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "FFBIsDisabled" ];
				}
				else if ( !app.Simulator.IsConnected )
				{
					SteeringDeviceFaultReason_TextBlock.Text = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "SimulatorNotRunning" ];
				}
				else if ( app.Simulator.SimMode != "full" )
				{
					SteeringDeviceFaultReason_TextBlock.Text = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "SimModeIsNotFull" ];
				}
				else if ( app.RacingWheel.SuspendForceFeedback )
				{
					if ( app.SteeringEffects.IsCalibrating )
					{
						SteeringDeviceFaultReason_TextBlock.Text = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "CalibrationIsRunning" ];
					}
					else
					{
						SteeringDeviceFaultReason_TextBlock.Text = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "FFBIsEnabledInSimulator" ];
					}
				}
				else
				{
					SteeringDeviceFaultReason_TextBlock.Text = app.DirectInput.ForceFeedbackErrorMessage;
				}

				SteeringDeviceFaultReason_TextBlock.Visibility = Visibility.Visible;
			}
		} );
	}

	#endregion
}
