
using System.Windows;
using System.Windows.Markup;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls;

[ContentProperty( nameof( ExpanderContent ) )]
public partial class MairaExpander : UserControl
{
	public MairaExpander()
	{
		InitializeComponent();
	}

	#region Dependency Properties

	public static readonly DependencyProperty LabelProperty =
		DependencyProperty.Register( nameof( Label ), typeof( string ), typeof( MairaExpander ), new PropertyMetadata( string.Empty ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
	}

	public static readonly DependencyProperty IsExpandedProperty =
		DependencyProperty.Register( nameof( IsExpanded ), typeof( bool ), typeof( MairaExpander ), new PropertyMetadata( false ) );

	public bool IsExpanded
	{
		get => (bool) GetValue( IsExpandedProperty );
		set => SetValue( IsExpandedProperty, value );
	}

	public static readonly DependencyProperty ExpanderContentProperty =
		DependencyProperty.Register( nameof( ExpanderContent ), typeof( object ), typeof( MairaExpander ), new PropertyMetadata( null ) );

	public object? ExpanderContent
	{
		get => GetValue( ExpanderContentProperty );
		set => SetValue( ExpanderContentProperty, value );
	}

	#endregion

	private void HeaderButton_Click( object sender, RoutedEventArgs e )
	{
		IsExpanded = !IsExpanded;
	}
}
