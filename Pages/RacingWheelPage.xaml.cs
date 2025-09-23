
using MarvinsAIRARefactored.Components;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static MarvinsAIRARefactored.Components.RacingWheel;
using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class RacingWheelPage : UserControl
{
	public static Dictionary<RacingWheel.Algorithm, string> AlgorithmName { get; } = new Dictionary<RacingWheel.Algorithm, string>
	{
		{ RacingWheel.Algorithm.Native60Hz, MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Native60Hz" ] },
		{ RacingWheel.Algorithm.Native360Hz, MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Native360Hz" ] },
		{ RacingWheel.Algorithm.DetailBooster, MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "DetailBooster" ] },
		{ RacingWheel.Algorithm.DeltaLimiter, MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "DeltaLimiter" ] },
		{ RacingWheel.Algorithm.DetailBoosterOn60Hz, MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "DetailBoosterOn60Hz" ] },
		{ RacingWheel.Algorithm.DeltaLimiterOn60Hz, MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "DeltaLimiterOn60Hz" ] },
		{ RacingWheel.Algorithm.ZeAlanLeTwist, MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "ZeAlanLeTwist" ] } ,
		{ RacingWheel.Algorithm.MultiAdjust, MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "MultiAdjust" ] }
	};
	
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

		app.Dispatcher.Invoke( () =>
		{
			SteeringDevice_MairaComboBox.ItemsSource = dictionary.OrderBy( keyValuePair => keyValuePair.Value );
			SteeringDevice_MairaComboBox.SelectedValue = settings.RacingWheelSteeringDeviceGuid;
			SteeringDevice_MairaComboBox.OffValue = Guid.Empty;
		} );

		app.Logger.WriteLine( "[RacingWheelPage] <<< UpdateSteeringDeviceOptions" );
	}

	public void UpdateLFERecordingDeviceOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RacingWheelPage] UpdateLFERecordingDeviceOptions >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<Guid, string>();

		app.LFE.CaptureDeviceList.ToList().ForEach( keyValuePair => dictionary[ keyValuePair.Key ] = keyValuePair.Value );

		if ( !dictionary.ContainsKey( settings.RacingWheelLFERecordingDeviceGuid ) )
		{
			dictionary.Add( settings.RacingWheelLFERecordingDeviceGuid, $"{localization[ "DeviceNotFound" ]} [{settings.RacingWheelLFERecordingDeviceGuid}]" );
		}

		app.Dispatcher.Invoke( () =>
		{
			LFERecordingDevice_MairaComboBox.ItemsSource = dictionary.OrderBy( keyValuePair => keyValuePair.Value );
			LFERecordingDevice_MairaComboBox.OffValue = Guid.Empty;
		} );

		app.Logger.WriteLine( "[RacingWheelPage] <<< UpdateLFERecordingDeviceOptions" );
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
			PreviewRecordings_MairaComboBox.ItemsSource = dictionary.OrderBy( keyValuePair => keyValuePair.Value );
		} );

		app.Logger.WriteLine( "[RacingWheelPage] <<< UpdatePreviewRecordingsOptions" );
	}

	public void UpdateAlgorithmOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RacingWheelPage] UpdateAlgorithmOptions >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		app.Dispatcher.Invoke( () =>
		{
			Algorithm_MairaComboBox.ItemsSource = AlgorithmName;
			Algorithm_MairaComboBox.SelectedValue = settings.RacingWheelAlgorithm;
		} );

		app.Logger.WriteLine( "[RacingWheelPage] <<< UpdateAlgorithmOptions" );
	}

	#endregion
}
