
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

public partial class NewFFBGraphWindow : Window
{
	public bool Confirmed { get; private set; } = false;
	public bool CopyFromCurrent { get; private set; } = false;
	public string GraphName { get; private set; } = string.Empty;

	public NewFFBGraphWindow()
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		Loaded += NewFFBGraphWindow_Loaded;
	}

	private void NewFFBGraphWindow_Loaded( object sender, RoutedEventArgs e )
	{
		GraphName_MairaTextBox.Focus();
	}

	private void Confirm( bool copyFromCurrent )
	{
		Confirmed = true;
		CopyFromCurrent = copyFromCurrent;
		GraphName = GraphName_MairaTextBox.Value ?? string.Empty;

		Close();
	}

	private void CloneCurrentGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirm( copyFromCurrent: true );
	}

	private void StartEmptyGraph_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirm( copyFromCurrent: false );
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = false;

		Close();
	}
}
