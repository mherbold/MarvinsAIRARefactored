
using System.Runtime.CompilerServices;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public class Graph : GraphBase
{
	private const int UpdateInterval = 6;

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

	private int _updateCounter = UpdateInterval + 2;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Graph] Initialize >>>" );

		Initialize( MainWindow._graphPage.Image );

		for ( var layerIndex = 0; layerIndex < (int) LayerIndex.Count; layerIndex++ )
		{
			_layerArray[ layerIndex ] = new Layer();
		}

		app.Logger.WriteLine( "[Graph] <<< Initialize" );
	}

	public void SetLayerColors( LayerIndex layerIndex, float r, float g, float b )
	{
		var layer = _layerArray[ (int) layerIndex ];

		layer.r = r;
		layer.g = g;
		layer.b = b;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void UpdateLayer( LayerIndex layerIndex, float normalizedValue )
	{
		if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.Graph )
		{
			_layerArray[ (int) layerIndex ].value = normalizedValue;
		}
	}

	public void Update()
	{
		if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.Graph )
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

					Update( layer.value, layer.r, layer.g, layer.b );
				}
			}

			FinishUpdates();
		}
	}

	public void Tick( App app )
	{
		if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.Graph )
		{
			WritePixels();

			_updateCounter--;

			if ( _updateCounter <= 0 )
			{
				_updateCounter = UpdateInterval;
			}
		}
	}

	private class Layer
	{
		public float value;

		public float r;
		public float g;
		public float b;
	}
}
