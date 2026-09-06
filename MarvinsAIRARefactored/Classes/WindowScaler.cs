using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MarvinsAIRARefactored.Classes;

// Scales an app dialog window's content to follow the global AppUIScale setting, the same way MainWindow does.
//
// Overlay windows are intentionally NOT scaled through here -- they have their own independent per-overlay
// scale control. The Help window is also excluded because it hosts a WebView2 (the browser has its own
// content zoom and does not survive a WPF LayoutTransform cleanly), as is the startup splash.
//
// A Window cannot carry a LayoutTransform itself, so we transform its root content element instead. For the
// (typical) non-resizable dialogs the window is switched to size-to-content, letting the window chrome
// shrink/grow with the scaled content, with any fixed size constraints declared on the Window moved onto the
// root element so they scale along with everything else (e.g. a 600px max height becomes 300px at 50% scale).
// A resizable window keeps its chrome size under the user's control instead: its declared design size and
// min/max constraints are scaled up front, and the transformed content stretches to fill whatever size the
// user drags the window to.
public static class WindowScaler
{
	// Call this once, right after InitializeComponent(), on each dialog window that should follow AppUIScale.
	public static void ApplyAppUIScale( Window window )
	{
		if ( window.Content is not FrameworkElement root )
		{
			return;
		}

		if ( window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip )
		{
			// the declared sizes are the design at 100% scale - a live AppUIScale change while the window is open
			// re-flows the content within the size the user has, so only the initial/min/max sizes are stamped here
			var appUIScale = (double) DataContext.DataContext.Instance.Settings.AppUIScale;

			ScaleSizeConstraint( window, Window.WidthProperty, appUIScale );
			ScaleSizeConstraint( window, Window.HeightProperty, appUIScale );
			ScaleSizeConstraint( window, FrameworkElement.MinWidthProperty, appUIScale );
			ScaleSizeConstraint( window, FrameworkElement.MinHeightProperty, appUIScale );
			ScaleSizeConstraint( window, FrameworkElement.MaxWidthProperty, appUIScale );
			ScaleSizeConstraint( window, FrameworkElement.MaxHeightProperty, appUIScale );
		}
		else
		{
			// move any fixed sizing off the window and onto the root element so the transform can actually resize the window
			MoveSizeConstraint( window, Window.WidthProperty, root, FrameworkElement.WidthProperty );
			MoveSizeConstraint( window, Window.HeightProperty, root, FrameworkElement.HeightProperty );
			MoveSizeConstraint( window, FrameworkElement.MinWidthProperty, root, FrameworkElement.MinWidthProperty );
			MoveSizeConstraint( window, FrameworkElement.MinHeightProperty, root, FrameworkElement.MinHeightProperty );
			MoveSizeConstraint( window, FrameworkElement.MaxWidthProperty, root, FrameworkElement.MaxWidthProperty );
			MoveSizeConstraint( window, FrameworkElement.MaxHeightProperty, root, FrameworkElement.MaxHeightProperty );

			window.SizeToContent = SizeToContent.WidthAndHeight;
		}

		var scaleTransform = new ScaleTransform();

		// bound to the singleton settings rather than the window's DataContext, since some dialogs repurpose their DataContext
		var binding = new System.Windows.Data.Binding( "Settings.AppUIScale" ) { Source = DataContext.DataContext.Instance };

		BindingOperations.SetBinding( scaleTransform, ScaleTransform.ScaleXProperty, binding );
		BindingOperations.SetBinding( scaleTransform, ScaleTransform.ScaleYProperty, binding );

		root.LayoutTransform = scaleTransform;
	}

	// Multiplies a locally-set size constraint on the window itself by the current UI scale (resizable windows only).
	private static void ScaleSizeConstraint( Window window, DependencyProperty windowProperty, double appUIScale )
	{
		if ( window.ReadLocalValue( windowProperty ) is double value && !double.IsNaN( value ) && !double.IsInfinity( value ) )
		{
			window.SetValue( windowProperty, value * appUIScale );
		}
	}

	// Moves a locally-set size constraint from the window onto the root element (without clobbering one the root already declares).
	private static void MoveSizeConstraint( Window window, DependencyProperty windowProperty, FrameworkElement root, DependencyProperty rootProperty )
	{
		if ( window.ReadLocalValue( windowProperty ) is double value && !double.IsNaN( value ) && !double.IsInfinity( value ) )
		{
			if ( root.ReadLocalValue( rootProperty ) == DependencyProperty.UnsetValue )
			{
				root.SetValue( rootProperty, value );
			}

			window.ClearValue( windowProperty );
		}
	}
}
