
using System.Linq;
using System.Windows;

using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using TextBlock = System.Windows.Controls.TextBlock;
using UserControl = System.Windows.Controls.UserControl;
using Localization = MarvinsAIRARefactored.Components.Localization;
using Settings = MarvinsAIRARefactored.DataContext.Settings;

using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.Pages;

public partial class PedalsPage : UserControl
{
	private const string ActiveBrushKey = "Brush.Accent";
	private const string SuppressedBrushKey = "Brush.SteeringEffects.Understeer";

	private static readonly Brush IdleBrush = CreateFrozenBrush( 0x88, 0x88, 0x88 );

	public PedalsPage()
	{
		InitializeComponent();
	}

	private static SolidColorBrush CreateFrozenBrush( byte r, byte g, byte b )
	{
		var brush = new SolidColorBrush( Color.FromRgb( r, g, b ) );

		brush.Freeze();

		return brush;
	}

	#region User Control Events

	private void Device_Label_MouseLeftButtonUp( object sender, System.Windows.Input.MouseButtonEventArgs e )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.PedalsEnabled = !settings.PedalsEnabled;
	}

	private void ClutchTest1_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 0, 0 );
	}

	private void ClutchTest2_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 0, 1 );
	}

	private void ClutchTest3_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 0, 2 );
	}

	private void BrakeTest1_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 1, 0 );
	}

	private void BrakeTest2_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 1, 1 );
	}

	private void BrakeTest3_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 1, 2 );
	}

	private void ThrottleTest1_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 2, 0 );
	}

	private void ThrottleTest2_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 2, 1 );
	}

	private void ThrottleTest3_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 2, 2 );
	}

	#endregion

	#region Logic

	public void UpdateEffectOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[PedalsPage] UpdateEffectOptions >>>" );

		// the display order deliberately keeps shift RPM next to RPM rather than following the enum declaration
		Pedals.Effect[] orderedEffects =
		[
			Pedals.Effect.None,
			Pedals.Effect.GearChange,
			Pedals.Effect.ABSEngaged,
			Pedals.Effect.RPM,
			Pedals.Effect.ShiftRPM,
			Pedals.Effect.UndersteerEffect,
			Pedals.Effect.OversteerEffect,
			Pedals.Effect.SeatOfPantsEffect,
			Pedals.Effect.WheelLock,
			Pedals.Effect.WheelSpin,
			Pedals.Effect.ClutchSlip
		];

		// the labels come from the shared settings helper, so these combo boxes and the tuning profile manager can
		// never say different things about the same setting
		var effectOptions = orderedEffects.Select( effect => new KeyValuePair<Pedals.Effect, string>( effect, Settings.FormatPedalsEffectString( effect ) ) ).ToList();

		ClutchEffect1_MairaComboBox.ItemsSource = effectOptions.ToList();
		ClutchEffect2_MairaComboBox.ItemsSource = effectOptions.ToList();
		ClutchEffect3_MairaComboBox.ItemsSource = effectOptions.ToList();

		BrakeEffect1_MairaComboBox.ItemsSource = effectOptions.ToList();
		BrakeEffect2_MairaComboBox.ItemsSource = effectOptions.ToList();
		BrakeEffect3_MairaComboBox.ItemsSource = effectOptions.ToList();

		ThrottleEffect1_MairaComboBox.ItemsSource = effectOptions.ToList();
		ThrottleEffect2_MairaComboBox.ItemsSource = effectOptions.ToList();
		ThrottleEffect3_MairaComboBox.ItemsSource = effectOptions.ToList();

		app.Logger.WriteLine( "[PedalsPage] <<< UpdateEffectOptions" );
	}

	public void UpdateEffectStatuses()
	{
		var app = App.Instance!;

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		UpdateStatusTextBlock( ClutchStatus1_TextBlock, app.Pedals.GetEffectStatus( 0, 0 ), localization );
		UpdateStatusTextBlock( ClutchStatus2_TextBlock, app.Pedals.GetEffectStatus( 0, 1 ), localization );
		UpdateStatusTextBlock( ClutchStatus3_TextBlock, app.Pedals.GetEffectStatus( 0, 2 ), localization );

		UpdateStatusTextBlock( BrakeStatus1_TextBlock, app.Pedals.GetEffectStatus( 1, 0 ), localization );
		UpdateStatusTextBlock( BrakeStatus2_TextBlock, app.Pedals.GetEffectStatus( 1, 1 ), localization );
		UpdateStatusTextBlock( BrakeStatus3_TextBlock, app.Pedals.GetEffectStatus( 1, 2 ), localization );

		UpdateStatusTextBlock( ThrottleStatus1_TextBlock, app.Pedals.GetEffectStatus( 2, 0 ), localization );
		UpdateStatusTextBlock( ThrottleStatus2_TextBlock, app.Pedals.GetEffectStatus( 2, 1 ), localization );
		UpdateStatusTextBlock( ThrottleStatus3_TextBlock, app.Pedals.GetEffectStatus( 2, 2 ), localization );
	}

	private static void UpdateStatusTextBlock( TextBlock textBlock, Pedals.EffectStatus status, Localization localization )
	{
		string text;

		switch ( status )
		{
			case Pedals.EffectStatus.Active: text = localization[ "Active" ]; break;
			case Pedals.EffectStatus.Suppressed: text = localization[ "Suppressed" ]; break;
			case Pedals.EffectStatus.Idle: text = localization[ "Idle" ]; break;
			default: text = string.Empty; break;
		}

		if ( textBlock.Text != text )
		{
			textBlock.Text = text;
		}

		// only re-apply the foreground when the status actually changes (the active / suppressed colors follow the theme)

		if ( textBlock.Tag is Pedals.EffectStatus cachedStatus && ( cachedStatus == status ) )
		{
			return;
		}

		textBlock.Tag = status;

		switch ( status )
		{
			case Pedals.EffectStatus.Active:
				textBlock.SetResourceReference( System.Windows.Controls.TextBlock.ForegroundProperty, ActiveBrushKey );
				break;

			case Pedals.EffectStatus.Suppressed:
				textBlock.SetResourceReference( System.Windows.Controls.TextBlock.ForegroundProperty, SuppressedBrushKey );
				break;

			default:
				textBlock.Foreground = IdleBrush;
				break;
		}
	}

	#endregion
}
