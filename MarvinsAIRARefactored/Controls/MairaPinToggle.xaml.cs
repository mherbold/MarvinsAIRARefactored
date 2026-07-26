
using System.Windows;
using System.Windows.Input;

namespace MarvinsAIRARefactored.Controls;

// A small pin-icon toggle — the pinned/unpinned affordance for FFB graph settings. Click flips IsOn;
// visual state (gray vs. MAIRA orange) lives in the XAML style. IsOn binds two-way by default so the
// usual IsOn="{Binding IsPinned}" just works. (Base class comes from the XAML partial — naming
// UserControl here would be ambiguous with the WinForms one.)
public partial class MairaPinToggle
{
	public MairaPinToggle()
	{
		InitializeComponent();

		MouseLeftButtonDown += MairaPinToggle_MouseLeftButtonDown;
	}

	private void MairaPinToggle_MouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		IsOn = !IsOn;

		e.Handled = true;
	}

	public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register( nameof( IsOn ), typeof( bool ), typeof( MairaPinToggle ), new FrameworkPropertyMetadata( false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault ) );

	public bool IsOn
	{
		get => (bool) GetValue( IsOnProperty );
		set => SetValue( IsOnProperty, value );
	}

	public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register( nameof( IconSize ), typeof( double ), typeof( MairaPinToggle ), new PropertyMetadata( 20.0 ) );

	public double IconSize
	{
		get => (double) GetValue( IconSizeProperty );
		set => SetValue( IconSizeProperty, value );
	}
}
