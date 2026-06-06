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
		new PropertyMetadata( string.Empty ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
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
}
