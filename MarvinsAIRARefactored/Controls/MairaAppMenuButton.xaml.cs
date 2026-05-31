
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls
{
	public sealed partial class MairaAppMenuButton : UserControl
	{
		public MairaAppMenuButton()
		{
			InitializeComponent();
		}

		#region User Control Events

		private void MenuButton_Click( object sender, RoutedEventArgs e )
		{
			IsMenuOpen = true;
		}

		#endregion

		#region Dependency Properties

		public static readonly DependencyProperty IsMenuOpenProperty = DependencyProperty.Register( nameof( IsMenuOpen ), typeof( bool ), typeof( MairaAppMenuButton ), new FrameworkPropertyMetadata( false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault ) );

		public bool IsMenuOpen
		{
			get => (bool) GetValue( IsMenuOpenProperty );
			set => SetValue( IsMenuOpenProperty, value );
		}

		#endregion
	}
}
