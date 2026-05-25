
using System.Windows;
using System.Windows.Controls;

namespace MarvinsAIRARefactored.Controls;

public class MairaGroupBox : HeaderedContentControl
{
	static MairaGroupBox()
	{
		DefaultStyleKeyProperty.OverrideMetadata( typeof( MairaGroupBox ), new FrameworkPropertyMetadata( typeof( MairaGroupBox ) ) );
	}

	#region Dependency Properties

	public static readonly DependencyProperty LabelProperty = DependencyProperty.Register( nameof( Label ), typeof( string ), typeof( MairaGroupBox ), new PropertyMetadata( string.Empty ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
	}

	public static readonly DependencyProperty SubLabelProperty = DependencyProperty.Register( nameof( SubLabel ), typeof( string ), typeof( MairaGroupBox ), new PropertyMetadata( string.Empty ) );

	public string SubLabel
	{
		get => (string) GetValue( SubLabelProperty );
		set => SetValue( SubLabelProperty, value );
	}

	public static readonly DependencyProperty HelpTopicProperty = DependencyProperty.Register( nameof( HelpTopic ), typeof( string ), typeof( MairaGroupBox ), new FrameworkPropertyMetadata( default( string ) ) );

	public string? HelpTopic
	{
		get => (string?) GetValue( HelpTopicProperty );
		set => SetValue( HelpTopicProperty, value );
	}

	#endregion
}
