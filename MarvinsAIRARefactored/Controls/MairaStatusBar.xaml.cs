
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaStatusBar : UserControl
{
	public enum StatusStyleEnum
	{
		Normal,
		Warning,
		Error
	};

	public MairaStatusBar()
	{
		InitializeComponent();
	}

	#region Dependency Properties

	public static readonly DependencyProperty StatusText1Property = DependencyProperty.Register( nameof( StatusText1 ), typeof( string ), typeof( MairaStatusBar ), new PropertyMetadata( string.Empty ) );

	public string StatusText1
	{
		get => (string) GetValue( StatusText1Property );
		set => SetValue( StatusText1Property, value );
	}

	public static readonly DependencyProperty StatusText2Property = DependencyProperty.Register( nameof( StatusText2 ), typeof( string ), typeof( MairaStatusBar ), new PropertyMetadata( string.Empty ) );

	public string StatusText2
	{
		get => (string) GetValue( StatusText2Property );
		set => SetValue( StatusText2Property, value );
	}

	public static readonly DependencyProperty StatusText3Property = DependencyProperty.Register( nameof( StatusText3 ), typeof( string ), typeof( MairaStatusBar ), new PropertyMetadata( string.Empty ) );

	public string StatusText3
	{
		get => (string) GetValue( StatusText3Property );
		set => SetValue( StatusText3Property, value );
	}

	public static readonly DependencyProperty StatusText4Property = DependencyProperty.Register( nameof( StatusText4 ), typeof( string ), typeof( MairaStatusBar ), new PropertyMetadata( string.Empty ) );

	public string StatusText4
	{
		get => (string) GetValue( StatusText4Property );
		set => SetValue( StatusText4Property, value );
	}

	public static readonly DependencyProperty StatusStyleProperty = DependencyProperty.Register( nameof( StatusStyle ), typeof( StatusStyleEnum ), typeof( MairaStatusBar ), new PropertyMetadata( StatusStyleEnum.Normal ) );

	public StatusStyleEnum StatusStyle
	{
		get => (StatusStyleEnum) GetValue( StatusStyleProperty );
		set => SetValue( StatusStyleProperty, value );
	}

	#endregion
}
