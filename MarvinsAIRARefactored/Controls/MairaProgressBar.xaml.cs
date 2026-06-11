using System.Globalization;
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaProgressBar : UserControl
{
	public MairaProgressBar()
	{
		InitializeComponent();
	}

	public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
		nameof( Label ),
		typeof( string ),
		typeof( MairaProgressBar ),
		new PropertyMetadata( string.Empty, OnLabelVisibilityInputChanged ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
	}

	public static readonly DependencyProperty ShowLabelProperty = DependencyProperty.Register(
		nameof( ShowLabel ),
		typeof( bool ),
		typeof( MairaProgressBar ),
		new PropertyMetadata( true, OnLabelVisibilityInputChanged ) );

	public bool ShowLabel
	{
		get => (bool) GetValue( ShowLabelProperty );
		set => SetValue( ShowLabelProperty, value );
	}

	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
		nameof( Value ),
		typeof( double ),
		typeof( MairaProgressBar ),
		new PropertyMetadata( 0d, OnValueChanged ) );

	public double Value
	{
		get => (double) GetValue( ValueProperty );
		set => SetValue( ValueProperty, value );
	}

	public static readonly DependencyProperty ProgressBarHeightProperty = DependencyProperty.Register(
		nameof( ProgressBarHeight ),
		typeof( double ),
		typeof( MairaProgressBar ),
		new PropertyMetadata( 20d ) );

	public double ProgressBarHeight
	{
		get => (double) GetValue( ProgressBarHeightProperty );
		set => SetValue( ProgressBarHeightProperty, value );
	}

	public static readonly DependencyProperty UseNearCompletionAccentProperty = DependencyProperty.Register(
		nameof( UseNearCompletionAccent ),
		typeof( bool ),
		typeof( MairaProgressBar ),
		new PropertyMetadata( true ) );

	public bool UseNearCompletionAccent
	{
		get => (bool) GetValue( UseNearCompletionAccentProperty );
		set => SetValue( UseNearCompletionAccentProperty, value );
	}

	public static readonly DependencyProperty ShowPercentageTextProperty = DependencyProperty.Register(
		nameof( ShowPercentageText ),
		typeof( bool ),
		typeof( MairaProgressBar ),
		new PropertyMetadata( true ) );

	public bool ShowPercentageText
	{
		get => (bool) GetValue( ShowPercentageTextProperty );
		set => SetValue( ShowPercentageTextProperty, value );
	}

	private static readonly DependencyPropertyKey IsLabelVisiblePropertyKey = DependencyProperty.RegisterReadOnly(
		nameof( IsLabelVisible ),
		typeof( bool ),
		typeof( MairaProgressBar ),
		new PropertyMetadata( false ) );

	public static readonly DependencyProperty IsLabelVisibleProperty = IsLabelVisiblePropertyKey.DependencyProperty;

	public bool IsLabelVisible
	{
		get => (bool) GetValue( IsLabelVisibleProperty );
		private set => SetValue( IsLabelVisiblePropertyKey, value );
	}

	private static readonly DependencyPropertyKey PercentageTextPropertyKey = DependencyProperty.RegisterReadOnly(
		nameof( PercentageText ),
		typeof( string ),
		typeof( MairaProgressBar ),
		new PropertyMetadata( "0.0%" ) );

	public static readonly DependencyProperty PercentageTextProperty = PercentageTextPropertyKey.DependencyProperty;

	public string PercentageText
	{
		get => (string) GetValue( PercentageTextProperty );
		private set => SetValue( PercentageTextPropertyKey, value );
	}

	private static readonly DependencyPropertyKey IsNearCompletionPropertyKey = DependencyProperty.RegisterReadOnly(
		nameof( IsNearCompletion ),
		typeof( bool ),
		typeof( MairaProgressBar ),
		new PropertyMetadata( false ) );

	public static readonly DependencyProperty IsNearCompletionProperty = IsNearCompletionPropertyKey.DependencyProperty;

	public bool IsNearCompletion
	{
		get => (bool) GetValue( IsNearCompletionProperty );
		private set => SetValue( IsNearCompletionPropertyKey, value );
	}

	private static void OnValueChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaProgressBar progressBar )
		{
			var clampedValue = Math.Clamp( progressBar.Value, 0d, 100d );

			if ( Math.Abs( clampedValue - progressBar.Value ) > double.Epsilon )
			{
				progressBar.SetCurrentValue( ValueProperty, clampedValue );
				return;
			}

			var currentCulture = CultureInfo.CurrentCulture;
			progressBar.PercentageText = string.Format( currentCulture, "{0:F1}%", clampedValue );
			progressBar.IsNearCompletion = clampedValue > 90d;
		}
	}

	private static void OnLabelVisibilityInputChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaProgressBar progressBar )
		{
			progressBar.UpdateLabelVisibilityState();
		}
	}

	private void UpdateLabelVisibilityState()
	{
		IsLabelVisible = ShowLabel && !string.IsNullOrWhiteSpace( Label );
	}
}
