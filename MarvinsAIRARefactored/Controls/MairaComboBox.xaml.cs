
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaComboBox : UserControl
{
	private readonly SolidColorBrush _normalSelectedValueBrush = new( (Color) ColorConverter.ConvertFromString( "#ff5b2e" ) );

	public MairaComboBox()
	{
		InitializeComponent();

		_normalSelectedValueBrush.Freeze();

		UpdateSelectedValueVisuals();
	}

	#region User Control Events

	private void TextBlock_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		var app = App.Instance!;

		e.Handled = true;

		if ( ContextSwitches != null )
		{
			app.Logger.WriteLine( "[MairaComboBox] Showing update context switches window" );

			var updateContextSwitchesWindow = new UpdateContextSwitchesWindow( ContextSwitches )
			{
				Owner = app.MainWindow
			};

			updateContextSwitchesWindow.ShowDialog();
		}
	}

	#endregion

	#region Dependency Properties

	public static readonly DependencyProperty LabelProperty = DependencyProperty.Register( nameof( Label ), typeof( string ), typeof( MairaComboBox ), new PropertyMetadata( string.Empty ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
	}

	public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register( nameof( ItemsSource ), typeof( object ), typeof( MairaComboBox ), new PropertyMetadata( null, ItemsSourceChanged ) );

	public object ItemsSource
	{
		get => GetValue( ItemsSourceProperty );
		set => SetValue( ItemsSourceProperty, value );
	}

	public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register( nameof( SelectedValue ), typeof( object ), typeof( MairaComboBox ), new PropertyMetadata( null, SelectedValueChanged ) );

	public object SelectedValue
	{
		get => GetValue( SelectedValueProperty );
		set => SetValue( SelectedValueProperty, value );
	}

	public static readonly DependencyProperty OffValueProperty = DependencyProperty.Register( nameof( OffValue ), typeof( object ), typeof( MairaComboBox ), new PropertyMetadata( null, OffValueChanged ) );

	public object OffValue
	{
		get => GetValue( OffValueProperty );
		set => SetValue( OffValueProperty, value );
	}

	public static readonly DependencyProperty ContextSwitchesProperty = DependencyProperty.Register( nameof( ContextSwitches ), typeof( ContextSwitches ), typeof( MairaComboBox ), new PropertyMetadata( null ) );

	public ContextSwitches ContextSwitches
	{
		get => (ContextSwitches) GetValue( ContextSwitchesProperty );
		set => SetValue( ContextSwitchesProperty, value );
	}

	#endregion

	#region Dependency Property Changed Events

	private static void ItemsSourceChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( App.Instance!.Ready )
		{
			if ( d is MairaComboBox mairaComboBox )
			{
				var valueToRestore = mairaComboBox.SelectedValue;

				mairaComboBox.Dispatcher.BeginInvoke( (Action) ( () =>
				{
					if ( mairaComboBox.ComboBox is null )
					{
						return;
					}

					// Replacing ItemsSource leaves the inner ComboBox with SelectedValue set but
					// SelectedItem null (WPF orphaned-SelectedValue state). UpdateTarget() and
					// re-setting SelectedValue are both no-ops when the value hasn't changed.
					// Directly find the matching item and set SelectedItem to force the display.
					if ( mairaComboBox.ComboBox.SelectedItem is null && valueToRestore is not null )
					{
						var selectedValuePath = mairaComboBox.ComboBox.SelectedValuePath;

						foreach ( var item in mairaComboBox.ComboBox.Items )
						{
							var itemKeyValue = item?.GetType().GetProperty( selectedValuePath )?.GetValue( item );

							if ( Equals( itemKeyValue, valueToRestore ) )
							{
								mairaComboBox.ComboBox.SelectedItem = item;
								break;
							}
						}
					}

					mairaComboBox.UpdateSelectedValueVisuals();
				} ), System.Windows.Threading.DispatcherPriority.DataBind );
			}
		}
	}

	private static void SelectedValueChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaComboBox mairaComboBox )
		{
			mairaComboBox.UpdateSelectedValueVisuals();
		}
	}

	private static void OffValueChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaComboBox mairaComboBox )
		{
			mairaComboBox.UpdateSelectedValueVisuals();
		}
	}

	public event SelectionChangedEventHandler SelectionChanged
	{
		add { ComboBox.SelectionChanged += value; }
		remove { ComboBox.SelectionChanged -= value; }
	}

	#endregion

	#region Logic

	private void UpdateSelectedValueVisuals()
	{
		if ( SelectedValue?.ToString() == OffValue?.ToString() )
		{
			ComboBox.Foreground = (System.Windows.Media.Brush) System.Windows.Application.Current.FindResource( "Brush.Foreground.Muted" );
		}
		else
		{
			ComboBox.Foreground = _normalSelectedValueBrush;
		}
	}

	#endregion
}
