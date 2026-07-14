
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.FFB;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Pages;

public partial class RacingWheelPage : UserControl
{
	private const double PreviewZoomSize = 256.0;
	private const double PreviewZoomFactor = 6.0;
	private const double PreviewZoomPopupOffset = 32.0;

	// the track map panel shows roughly this many meters of track across its (PreviewZoomSize-wide) window
	private const double TrackMapMetersAcross = 500.0;

	// track map cache — rebuilt when the loaded recording data changes (reference comparison; the path and
	// geometry are derived purely from that list)
	private List<RecordingData>? _trackMapDataList = null;
	private Point[]? _trackMapPath = null;

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
		CenterPreviewScrollViewer();
	}

	/// <summary>
	/// Scrolls the preview graph to its horizontal middle — called when the scroll viewer first loads and
	/// whenever the preview image is resized to a newly loaded recording (recordings are dynamic-length, so the
	/// image width changes per recording). The scroll is deferred until after the pending layout pass so
	/// ScrollableWidth reflects the new image width.
	/// </summary>
	public void CenterPreviewScrollViewer()
	{
		Dispatcher.BeginInvoke( System.Windows.Threading.DispatcherPriority.Loaded, () =>
		{
			Preview_ScrollViewer.ScrollToHorizontalOffset( Preview_ScrollViewer.ScrollableWidth / 2d );
		} );
	}

	private void AlgorithmPreview_Image_MouseEnter( object sender, MouseEventArgs e )
	{
		if ( AlgorithmPreview_Image.Source == null )
		{
			return;
		}

		var cursorPosition = e.GetPosition( AlgorithmPreview_Image );

		UpdatePreviewZoom( cursorPosition );
		UpdatePreviewSampleData( cursorPosition );
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
		UpdatePreviewSampleData( cursorPosition );
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

	/// <summary>
	/// Fills the data card next to the zoom square with the recorded telemetry at the sample under the cursor —
	/// the preview bitmap is one pixel per recorded sample, so the cursor X position is the sample index. The
	/// card hides itself when there's no recorded sample under the cursor (no recording loaded, or the cursor is
	/// past the end of a short recording).
	/// </summary>
	private void UpdatePreviewSampleData( Point cursorPosition )
	{
		var app = App.Instance!;

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var recordingDataList = app.RecordingManager.Recording?.Data;

		// drop the cached track map as soon as the loaded data changes so the old samples can be collected
		// (RecordingManager unloads the other recordings when a new one loads — don't keep them alive here)
		if ( ( _trackMapDataList != null ) && !ReferenceEquals( recordingDataList, _trackMapDataList ) )
		{
			_trackMapDataList = null;
			_trackMapPath = null;

			TrackMap_Path.Data = null;
		}

		var sampleIndex = (int) cursorPosition.X;

		if ( ( recordingDataList == null ) || ( sampleIndex < 0 ) || ( sampleIndex >= recordingDataList.Count ) )
		{
			PreviewData_Border.Visibility = Visibility.Collapsed;
			TrackMap_Border.Visibility = Visibility.Collapsed;

			return;
		}

		PreviewData_Border.Visibility = Visibility.Visible;
		TrackMap_Border.Visibility = Visibility.Visible;

		UpdateTrackMap( recordingDataList, sampleIndex );

		var recordingData = recordingDataList[ sampleIndex ];

		const float radiansToDegrees = 180f / MathF.PI;

		PreviewDataTime_TextBlock.Text = $"{sampleIndex / 360f:F2}{localization[ "SecondsUnits" ]}";
		PreviewDataTrackPosition_TextBlock.Text = $"{recordingData.TrackPosition:F0}{localization[ "MetersUnits" ]}";

		PreviewDataTorque60Hz_TextBlock.Text = $"{recordingData.InputTorque60Hz:F2}{localization[ "TorqueUnits" ]}";
		PreviewDataTorque360Hz_TextBlock.Text = $"{recordingData.InputTorque360Hz:F2}{localization[ "TorqueUnits" ]}";
		PreviewDataLFE_TextBlock.Text = $"{recordingData.LFEMagnitude:F2}";

		PreviewDataSteeringAngle_TextBlock.Text = $"{recordingData.SteeringWheelAngle * radiansToDegrees:F1}{localization[ "Degrees" ]}";
		PreviewDataSteeringVelocity_TextBlock.Text = $"{recordingData.SteeringWheelVelocity * radiansToDegrees:F0}{localization[ "DegreesPerSecond" ]}";
		PreviewDataWheelPosition_TextBlock.Text = $"{recordingData.WheelPosition:F2}";
		PreviewDataWheelVelocity_TextBlock.Text = $"{recordingData.WheelVelocity:F2}";

		PreviewDataSpeed_TextBlock.Text = $"{recordingData.VelocityMS:F1}{localization[ "MPSUnits" ]}";
		PreviewDataRPM_TextBlock.Text = $"{recordingData.RPM:F0}";
		PreviewDataGear_TextBlock.Text = recordingData.Gear switch
		{
			< 0 => "R",
			0 => "N",
			_ => recordingData.Gear.ToString()
		};
		PreviewDataABS_TextBlock.Text = recordingData.ABSActive ? localization[ "ON" ] : localization[ "OFF" ];

		PreviewDataLateralGForce_TextBlock.Text = $"{recordingData.LateralGForce:F2}{localization[ "GForceUnits" ]}";
		PreviewDataLongitudinalGForce_TextBlock.Text = $"{recordingData.LongitudinalGForce:F2}{localization[ "GForceUnits" ]}";
		PreviewDataShockVelocity_TextBlock.Text = $"{recordingData.MaxShockVelocity:F2}{localization[ "MPSUnits" ]}";

		PreviewDataUndersteer_TextBlock.Text = $"{recordingData.UndersteerEffect * 100f:F0}{localization[ "Percent" ]}";
		PreviewDataOversteer_TextBlock.Text = $"{recordingData.OversteerEffect * 100f:F0}{localization[ "Percent" ]}";
		PreviewDataSeatOfPants_TextBlock.Text = $"{recordingData.SeatOfPantsEffect * 100f:F0}{localization[ "Percent" ]}";
		PreviewDataSkidSlip_TextBlock.Text = $"{recordingData.SkidSlip * 100f:F0}{localization[ "Percent" ]}";
	}

	/// <summary>
	/// Keeps the track map panel in sync with the cursor: the recorded segment's polyline (integrated once per
	/// loaded recording, see <see cref="TrackMap"/>) is drawn north-up at a fixed scale, and the panel's
	/// transform slides the map so the sample under the cursor sits at the center — under the orange car dot.
	/// </summary>
	private void UpdateTrackMap( List<RecordingData> recordingDataList, int sampleIndex )
	{
		if ( !ReferenceEquals( recordingDataList, _trackMapDataList ) )
		{
			_trackMapDataList = recordingDataList;
			_trackMapPath = TrackMap.BuildPath( recordingDataList );

			var geometry = new StreamGeometry();

			using ( var context = geometry.Open() )
			{
				context.BeginFigure( _trackMapPath[ 0 ], false, false );

				// one point per 60 Hz telemetry frame is plenty for the polyline (values repeat 6× at 360 Hz)
				for ( var i = 6; i < _trackMapPath.Length; i += 6 )
				{
					context.LineTo( _trackMapPath[ i ], true, true );
				}

				context.LineTo( _trackMapPath[ ^1 ], true, true );
			}

			geometry.Freeze();

			TrackMap_Path.Data = geometry;
		}

		var carPoint = _trackMapPath![ sampleIndex ];

		var scale = PreviewZoomSize / TrackMapMetersAcross;

		var matrix = new Matrix( scale, 0d, 0d, scale, PreviewZoomSize / 2d - scale * carPoint.X, PreviewZoomSize / 2d - scale * carPoint.Y );

		TrackMap_MatrixTransform.Matrix = matrix;

		// the start/end markers ride the map — same transform, applied to their canvas positions
		var startPoint = matrix.Transform( _trackMapPath[ 0 ] );
		var endPoint = matrix.Transform( _trackMapPath[ ^1 ] );

		Canvas.SetLeft( TrackMapStart_Ellipse, startPoint.X - TrackMapStart_Ellipse.Width / 2d );
		Canvas.SetTop( TrackMapStart_Ellipse, startPoint.Y - TrackMapStart_Ellipse.Height / 2d );

		Canvas.SetLeft( TrackMapEnd_Ellipse, endPoint.X - TrackMapEnd_Ellipse.Width / 2d );
		Canvas.SetTop( TrackMapEnd_Ellipse, endPoint.Y - TrackMapEnd_Ellipse.Height / 2d );
	}

	private void StartRecording_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.RecordingManager.ToggleRecording();
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

	// The graph selectors group their options into "Built-in" and "Custom" categories using the underscore-key
	// header convention MairaComboBox renders as non-selectable accent rows (an underscore key never collides
	// with a real graph name selection). Categories with no graphs are omitted.
	private static List<KeyValuePair<string, string>> BuildGraphSelectorItems( SerializableDictionary<string, FFBGraph> graphs )
	{
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var items = new List<KeyValuePair<string, string>>();

		var builtInNames = graphs.Where( pair => pair.Value.IsBuiltIn ).Select( pair => pair.Key ).OrderBy( name => name, StringComparer.OrdinalIgnoreCase ).ToList();
		var customNames = graphs.Where( pair => !pair.Value.IsBuiltIn ).Select( pair => pair.Key ).OrderBy( name => name, StringComparer.OrdinalIgnoreCase ).ToList();

		if ( builtInNames.Count > 0 )
		{
			items.Add( new KeyValuePair<string, string>( "_builtIn", localization[ "BuiltInGraphs" ] ) );

			foreach ( var graphName in builtInNames )
			{
				items.Add( new KeyValuePair<string, string>( graphName, graphName ) );
			}
		}

		if ( customNames.Count > 0 )
		{
			items.Add( new KeyValuePair<string, string>( "_custom", localization[ "CustomGraphs" ] ) );

			foreach ( var graphName in customNames )
			{
				items.Add( new KeyValuePair<string, string>( graphName, graphName ) );
			}
		}

		return items;
	}

	public void UpdateFFBGraphOptions()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_refreshingGraphSelector = true;

		Graph_MairaComboBox.ItemsSource = BuildGraphSelectorItems( settings.RacingWheelFFBGraphs );
		Graph_MairaComboBox.SelectedValue = settings.RacingWheelSelectedFFBGraphName;

		var isBuiltIn = settings.RacingWheelFFBGraphs.TryGetValue( settings.RacingWheelSelectedFFBGraphName, out var graph ) && graph.IsBuiltIn;

		// Built-in graphs cannot be renamed or deleted, but can be reset to their shipped defaults.
		RenameGraph_MairaButton.Disabled = isBuiltIn;
		DeleteGraph_MairaButton.Disabled = isBuiltIn;
		ResetGraph_MairaButton.Visibility = isBuiltIn ? Visibility.Visible : Visibility.Collapsed;

		// same treatment for the vibration graph selector
		VibrationGraph_MairaComboBox.ItemsSource = BuildGraphSelectorItems( settings.RacingWheelVibrationGraphs );
		VibrationGraph_MairaComboBox.SelectedValue = settings.RacingWheelSelectedVibrationGraphName;

		var vibrationIsBuiltIn = settings.RacingWheelVibrationGraphs.TryGetValue( settings.RacingWheelSelectedVibrationGraphName, out var vibrationGraph ) && vibrationGraph.IsBuiltIn;

		RenameVibrationGraph_MairaButton.Disabled = vibrationIsBuiltIn;
		DeleteVibrationGraph_MairaButton.Disabled = vibrationIsBuiltIn;
		ResetVibrationGraph_MairaButton.Visibility = vibrationIsBuiltIn ? Visibility.Visible : Visibility.Collapsed;

		// a built-in vibration graph's structure is locked — no adding generator modules
		AddGeneratorModule_MairaButton.Disabled = vibrationIsBuiltIn;

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

	private void ExportGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( settings.RacingWheelFFBGraphs.TryGetValue( settings.RacingWheelSelectedFFBGraphName, out var graph ) )
		{
			ExportGraph( graph, FFBGraphExportFile.FFBGraphType );
		}
	}

	private void ImportGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		ImportGraph( FFBGraphExportFile.FFBGraphType );
	}

	// Shared by the FFB and vibration export buttons: pick a file, write the graph. The suggested file name is
	// the graph name with any filesystem-invalid characters stripped.
	private static void ExportGraph( FFBGraph graph, string graphType )
	{
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var fileName = string.Concat( graph.Name.Split( Path.GetInvalidFileNameChars() ) ).Trim();

		var dialog = new SaveFileDialog
		{
			Title = localization[ "ExportGraph" ],
			FileName = fileName,
			DefaultExt = FFBGraphPort.FileExtension,
			AddExtension = true,
			Filter = $"{localization[ "MairaGraphFiles" ]} (*{FFBGraphPort.FileExtension})|*{FFBGraphPort.FileExtension}"
		};

		if ( dialog.ShowDialog() != true )
		{
			return;
		}

		try
		{
			FFBGraphPort.Export( graph, graphType, dialog.FileName );
		}
		catch ( Exception exception )
		{
			ErrorWindow.ShowModal( localization[ "ExportGraphFailed" ], exception );
		}
	}

	// Shared by the FFB and vibration import buttons: pick a file and validate + load it. A graph the user does not
	// already have is added as a new user graph (unique name, fresh module ids, the file's GraphId kept so a later
	// re-import is recognized). A graph they already have (same GraphId) opens the import-settings dialog, letting
	// them apply the file's module settings onto the existing graph (current context / baseline / both) or import a
	// separate copy. Validation failures show their own localized message; anything unexpected shows the generic one.
	private void ImportGraph( string graphType )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var dialog = new OpenFileDialog
		{
			Title = localization[ "ImportGraph" ],
			Filter = $"{localization[ "MairaGraphFiles" ]} (*{FFBGraphPort.FileExtension})|*{FFBGraphPort.FileExtension}",
			CheckFileExists = true
		};

		if ( dialog.ShowDialog() != true )
		{
			return;
		}

		var isVibration = graphType == FFBGraphExportFile.VibrationGraphType;

		FFBGraph graph;

		try
		{
			graph = FFBGraphPort.Import( dialog.FileName, graphType );
		}
		catch ( FFBGraphPort.ImportException importException )
		{
			ErrorWindow.ShowModal( importException.Message );

			return;
		}
		catch ( Exception exception )
		{
			ErrorWindow.ShowModal( localization[ "ImportGraphFailed" ], exception );

			return;
		}

		try
		{
			var matchingGraphName = settings.FindMatchingGraphName( graph, isVibration );

			if ( matchingGraphName == null )
			{
				if ( isVibration )
				{
					settings.ImportVibrationGraph( graph );
				}
				else
				{
					settings.ImportFFBGraph( graph );
				}
			}
			else
			{
				var ( contextAvailable, contextLabel ) = settings.GetGraphImportContextInfo( isVibration );

				var choice = ImportGraphSettingsWindow.ShowModal( matchingGraphName, contextLabel, contextAvailable );

				switch ( choice )
				{
					case ImportGraphSettingsWindow.Choice.UpdateCurrentContext:
						settings.ApplyImportedGraphValues( matchingGraphName, graph, isVibration, toCurrentContext: true, toBaseline: false );
						break;

					case ImportGraphSettingsWindow.Choice.UpdateBaseline:
						settings.ApplyImportedGraphValues( matchingGraphName, graph, isVibration, toCurrentContext: false, toBaseline: true );
						break;

					case ImportGraphSettingsWindow.Choice.UpdateBoth:
						settings.ApplyImportedGraphValues( matchingGraphName, graph, isVibration, toCurrentContext: true, toBaseline: true );
						break;

					case ImportGraphSettingsWindow.Choice.NewCopy:
						if ( isVibration )
						{
							settings.ImportVibrationGraph( graph, asNewCopy: true );
						}
						else
						{
							settings.ImportFFBGraph( graph, asNewCopy: true );
						}
						break;

					case ImportGraphSettingsWindow.Choice.Cancel:
						return;
				}
			}
		}
		catch ( Exception exception )
		{
			ErrorWindow.ShowModal( localization[ "ImportGraphFailed" ], exception );

			return;
		}

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void VibrationGraph_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( _refreshingGraphSelector )
		{
			return;
		}

		if ( VibrationGraph_MairaComboBox.SelectedValue is not string graphName )
		{
			return;
		}

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( graphName == settings.RacingWheelSelectedVibrationGraphName )
		{
			return;
		}

		settings.SelectVibrationGraph( graphName );

		App.Instance!.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void NewVibrationGraph_MairaButton_Click( object sender, RoutedEventArgs e )
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

		if ( ( name == string.Empty ) || settings.RacingWheelVibrationGraphs.ContainsKey( name ) )
		{
			return;
		}

		settings.CreateVibrationGraph( name, window.CopyFromCurrent );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void RenameVibrationGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var currentName = settings.RacingWheelSelectedVibrationGraphName;

		if ( settings.RacingWheelVibrationGraphs.TryGetValue( currentName, out var currentGraph ) && currentGraph.IsBuiltIn )
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

		if ( ( newName == string.Empty ) || ( newName == currentName ) || settings.RacingWheelVibrationGraphs.ContainsKey( newName ) )
		{
			return;
		}

		settings.RenameVibrationGraph( currentName, newName );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void DeleteVibrationGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var currentName = settings.RacingWheelSelectedVibrationGraphName;

		if ( settings.RacingWheelVibrationGraphs.TryGetValue( currentName, out var currentGraph ) && currentGraph.IsBuiltIn )
		{
			return;
		}

		var window = new DeleteControllerProfileWindow( currentName ) { Owner = app.MainWindow };

		window.ShowDialog();

		if ( !window.Confirmed )
		{
			return;
		}

		settings.DeleteVibrationGraph( currentName );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void ResetVibrationGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.ResetBuiltInVibrationGraph( settings.RacingWheelSelectedVibrationGraphName );

		app.SettingsFile.QueueForSerialization = true;

		UpdateFFBGraphOptions();
	}

	private void ExportVibrationGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( settings.RacingWheelVibrationGraphs.TryGetValue( settings.RacingWheelSelectedVibrationGraphName, out var graph ) )
		{
			ExportGraph( graph, FFBGraphExportFile.VibrationGraphType );
		}
	}

	private void ImportVibrationGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		ImportGraph( FFBGraphExportFile.VibrationGraphType );
	}


	private void RemoveModule_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( ( sender is MairaButton mairaButton ) && ( mairaButton.Tag is FFBModuleViewModel moduleViewModel ) )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelGraphViewModel.RemoveModule( moduleViewModel );
		}
	}

	private void TestModule_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( ( sender is MairaButton mairaButton ) && ( mairaButton.Tag is FFBModuleViewModel moduleViewModel ) )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelGraphViewModel.ToggleTestActive( moduleViewModel );
		}
	}

	private void AddGeneratorModule_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var viewModel = MarvinsAIRARefactored.DataContext.DataContext.Instance.RacingWheelGraphViewModel;

		var window = new AddFFBModuleWindow( viewModel.AddableGeneratorModuleTypes ) { Owner = App.Instance!.MainWindow };

		window.ShowDialog();

		if ( window.SelectedModuleType != null )
		{
			viewModel.AddModule( window.SelectedModuleType );
		}
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
