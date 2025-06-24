
using System.Runtime.CompilerServices;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Components;

public class Graph : GraphBase
{
	public enum LayerIndex
	{
		InputTorque,
		OutputTorque,
		InputTorque60Hz,
		InputLFE,
		ClutchPedalHaptics,
		BrakePedalHaptics,
		ThrottlePedalHaptics,
		TimerJitter,
		Count
	}

	private readonly Layer[] _layerArray = new Layer[ (int) LayerIndex.Count ];
	private readonly Statistics[] _statisticsArray = new Statistics[ (int) LayerIndex.Count ];

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Graph] Initialize >>>" );

		Initialize( app.MainWindow.Graph_Image );

		for ( var layerIndex = 0; layerIndex < (int) LayerIndex.Count; layerIndex++ )
		{
			_layerArray[ layerIndex ] = new Layer();
			_statisticsArray[ layerIndex ] = new Statistics( 500 );
		}

		app.Logger.WriteLine( "[Graph] Initialize >>>" );
	}

	public static void SetMairaComboBoxItemsSource( MairaComboBox mairaComboBox )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Graph] SetMairaComboBoxItemsSource >>>" );

		var selectedLayerIndex = mairaComboBox.SelectedValue as LayerIndex?;

		var dictionary = new Dictionary<LayerIndex, string>
			{
				{ LayerIndex.InputTorque, DataContext.DataContext.Instance.Localization[ "InputTorque" ] },
				{ LayerIndex.OutputTorque, DataContext.DataContext.Instance.Localization[ "OutputTorque" ] },
				{ LayerIndex.InputTorque60Hz, DataContext.DataContext.Instance.Localization[ "InputTorque60Hz" ] },
				{ LayerIndex.InputLFE, DataContext.DataContext.Instance.Localization[ "InputLFE" ] },
				{ LayerIndex.ClutchPedalHaptics, DataContext.DataContext.Instance.Localization[ "ClutchPedalHaptics" ] },
				{ LayerIndex.BrakePedalHaptics, DataContext.DataContext.Instance.Localization[ "BrakePedalHaptics" ] },
				{ LayerIndex.ThrottlePedalHaptics, DataContext.DataContext.Instance.Localization[ "ThrottlePedalHaptics" ] },
				{ LayerIndex.TimerJitter, DataContext.DataContext.Instance.Localization[ "TimerJitter" ] }
			};

		mairaComboBox.ItemsSource = dictionary;

		if ( selectedLayerIndex != null )
		{
			mairaComboBox.SelectedValue = selectedLayerIndex;
		}
		else
		{
			mairaComboBox.SelectedValue = LayerIndex.OutputTorque;
		}

		app.Logger.WriteLine( "[Graph] <<< SetMairaComboBoxItemsSource" );
	}

	public void SetLayerColors( LayerIndex layerIndex, float minR, float minG, float minB, float maxR, float maxG, float maxB )
	{
		var layer = _layerArray[ (int) layerIndex ];

		layer.minR = minR;
		layer.minG = minG;
		layer.minB = minB;

		layer.maxR = maxR;
		layer.maxG = maxG;
		layer.maxB = maxB;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void UpdateLayer( LayerIndex layerIndex, float rawValue, float normalizedValue )
	{
		var app = App.Instance!;

		if ( app.MainWindow.GraphTabItemIsVisible )
		{
			_statisticsArray[ (int) layerIndex ].Update( rawValue );

			_layerArray[ (int) layerIndex ].value = normalizedValue;
		}
	}

	public void Update()
	{
		var app = App.Instance!;

		if ( app.MainWindow.GraphTabItemIsVisible )
		{
			var settings = DataContext.DataContext.Instance.Settings;

			for ( var layerIndex = LayerIndex.InputTorque; layerIndex < LayerIndex.Count; layerIndex++ )
			{
				var showLayer = layerIndex switch
				{
					LayerIndex.InputTorque => settings.GraphInputTorque,
					LayerIndex.OutputTorque => settings.GraphOutputTorque,
					LayerIndex.InputTorque60Hz => settings.GraphInputTorque60Hz,
					LayerIndex.InputLFE => settings.GraphInputLFE,
					LayerIndex.ClutchPedalHaptics => settings.GraphClutchPedalHaptics,
					LayerIndex.BrakePedalHaptics => settings.GraphBrakePedalHaptics,
					LayerIndex.ThrottlePedalHaptics => settings.GraphThrottlePedalHaptics,
					LayerIndex.TimerJitter => settings.GraphTimerJitter,
					_ => false
				};

				if ( showLayer )
				{
					var layer = _layerArray[ (int) layerIndex ];

					Update( layer.value, layer.minR, layer.minG, layer.minB, layer.maxR, layer.maxG, layer.maxB );
				}
			}

			FinishUpdates();
		}
	}

	public void Tick( App app )
	{
		WritePixels();

		var statistics = _statisticsArray[ (int) DataContext.DataContext.Instance.Settings.GraphStatisticsLayerIndex ];

		app.MainWindow.Graph_Minimum_Label.Content = $"{statistics.MinimumValue:F2}";
		app.MainWindow.Graph_Maximum_Label.Content = $"{statistics.MaximumValue:F2}";
		app.MainWindow.Graph_Average_Label.Content = $"{statistics.AverageValue:F2}";
		app.MainWindow.Graph_Variance_Label.Content = $"{statistics.Variance:F2}";
		app.MainWindow.Graph_StandardDeviation_Label.Content = $"{statistics.StandardDeviation:F2}";
	}

	private class Layer
	{
		public float value;

		public float minR;
		public float minG;
		public float minB;

		public float maxR;
		public float maxG;
		public float maxB;
	}
}

