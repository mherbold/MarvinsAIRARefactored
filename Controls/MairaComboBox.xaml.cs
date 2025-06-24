
using System.Windows;
using System.Windows.Input;

using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaComboBox : UserControl
{
	public MairaComboBox()
	{
		InitializeComponent();

		UpdateLabelVisual();
	}

	#region Dependency Properties

	public static readonly DependencyProperty TitleProperty = DependencyProperty.Register( nameof( Title ), typeof( string ), typeof( MairaComboBox ), new PropertyMetadata( string.Empty, OnTitleChanged ) );

	public string Title
	{
		get => (string) GetValue( TitleProperty );
		set => SetValue( TitleProperty, value );
	}

	private static void OnTitleChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		var control = (MairaComboBox) d;

		control.UpdateLabelVisual();
	}

	public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register( nameof( ItemsSource ), typeof( object ), typeof( MairaComboBox ) );

	public object ItemsSource
	{
		get => GetValue( ItemsSourceProperty );
		set => SetValue( ItemsSourceProperty, value );
	}

	public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register( nameof( SelectedValue ), typeof( object ), typeof( MairaComboBox ) );

	public object SelectedValue
	{
		get => GetValue( SelectedValueProperty );
		set => SetValue( SelectedValueProperty, value );
	}

	public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register( nameof( SelectedItem ), typeof( object ), typeof( MairaComboBox ) );

	public object SelectedItem
	{
		get => GetValue( SelectedItemProperty );
		set => SetValue( SelectedItemProperty, value );
	}

	public static readonly DependencyProperty ContextSwitchesProperty = DependencyProperty.Register( nameof( ContextSwitches ), typeof( ContextSwitches ), typeof( MairaComboBox ), new PropertyMetadata( null ) );

	public ContextSwitches ContextSwitches
	{
		get => (ContextSwitches) GetValue( ContextSwitchesProperty );
		set => SetValue( ContextSwitchesProperty, value );
	}

	#endregion

	#region Event Handlers

	private void Label_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
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

	#region Logic

	private void UpdateLabelVisual()
	{
		if ( Title == string.Empty )
		{
			Label.Visibility = Visibility.Collapsed;
		}
		else
		{
			Label.Visibility = Visibility.Visible;
		}
	}

	#endregion
}
