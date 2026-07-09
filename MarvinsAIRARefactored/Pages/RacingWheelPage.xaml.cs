
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

	private bool _refreshingGraphSelector = false;

	public void UpdateFFBGraphOptions()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_refreshingGraphSelector = true;

		var items = new List<KeyValuePair<string, string>>();

		foreach ( var graphName in settings.RacingWheelFFBGraphs.Keys )
		{
			items.Add( new KeyValuePair<string, string>( graphName, graphName ) );
		}

		Graph_MairaComboBox.ItemsSource = items;
		Graph_MairaComboBox.SelectedValue = settings.RacingWheelSelectedFFBGraphName;

		var isBuiltIn = settings.RacingWheelFFBGraphs.TryGetValue( settings.RacingWheelSelectedFFBGraphName, out var graph ) && graph.IsBuiltIn;

		// Built-in graphs cannot be renamed or deleted, but can be reset to their defaults.
		RenameGraph_MairaButton.Disabled = isBuiltIn;
		DeleteGraph_MairaButton.Disabled = isBuiltIn;
		ResetGraph_MairaButton.Visibility = isBuiltIn ? Visibility.Visible : Visibility.Collapsed;

		_refreshingGraphSelector = false;

		// Rebuild the module cards so their (localized) module names and setting labels refresh — this runs from
		// MainWindow.RefreshWindow, which is the app's relocalization entry point, so the editor follows a
		// runtime language switch.
		MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelGraphViewModel.RebuildFromCurrentSelection();
	}

	private void Graph_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( _refreshingGraphSelector )
		{
			return;
		}

		if ( Graph_MairaComboBox.SelectedValue is not string graphName )
		{
			return;
		}

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( graphName == settings.RacingWheelSelectedFFBGraphName )
		{
			return;
		}

		settings.SelectFFBGraph( graphName );

		App.Instance!.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void NewGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var window = new NewFFBGraphWindow { Owner = app.MainWindow };

		window.ShowDialog();

		if ( !window.Confirmed )
		{
			return;
		}

		var name = window.GraphName.Trim();

		if ( ( name == string.Empty ) || settings.RacingWheelFFBGraphs.ContainsKey( name ) )
		{
			return;
		}

		settings.CreateFFBGraph( name, window.CopyFromCurrent );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void RenameGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var currentName = settings.RacingWheelSelectedFFBGraphName;

		if ( settings.RacingWheelFFBGraphs.TryGetValue( currentName, out var currentGraph ) && currentGraph.IsBuiltIn )
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

		if ( ( newName == string.Empty ) || ( newName == currentName ) || settings.RacingWheelFFBGraphs.ContainsKey( newName ) )
		{
			return;
		}

		settings.RenameFFBGraph( currentName, newName );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void DeleteGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var currentName = settings.RacingWheelSelectedFFBGraphName;

		if ( settings.RacingWheelFFBGraphs.TryGetValue( currentName, out var currentGraph ) && currentGraph.IsBuiltIn )
		{
			return;
		}

		var window = new DeleteControllerProfileWindow( currentName ) { Owner = app.MainWindow };

		window.ShowDialog();

		if ( !window.Confirmed )
		{
			return;
		}

		settings.DeleteFFBGraph( currentName );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void ResetGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.ResetBuiltInFFBGraph( settings.RacingWheelSelectedFFBGraphName );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void AddModule_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelGraphViewModel.AddSelectedModule();
	}

	private void RemoveModule_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( ( sender is MairaButton mairaButton ) && ( mairaButton.Tag is FFBModuleViewModel moduleViewModel ) )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelGraphViewModel.RemoveModule( moduleViewModel );
		}
	}

	private void AddGeneratorModule_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelGraphViewModel.AddSelectedGeneratorModule();
	}

	private void ChooseRecording_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var window = new ChooseRecordingWindow { Owner = app.MainWindow };

		window.ShowDialog();
	}

	// The recordings list now lives in the choose-recording dialog; refresh it if one is open (this is still
	// called when the recording manager loads or saves recordings).
	public void UpdatePreviewRecordingsOptions()
	{
		var app = App.Instance!;

		app.Dispatcher.Invoke( () =>
		{
			ChooseRecordingWindow.Current?.RefreshRecordingsList();
		} );
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
