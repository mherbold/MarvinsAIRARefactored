
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MarvinsAIRARefactored.Windows;

public partial class NewVersionAvailableWindow : Window
{
	public bool DownloadUpdate { get; private set; } = false;

	[GeneratedRegex( @"^(?<indent>[ \t]*)[-*+][ \t]+(?<text>.*)$" )]
	private static partial Regex BulletRegex();

	[GeneratedRegex( @"^(?<hashes>#{1,6})[ \t]+(?<text>.*)$" )]
	private static partial Regex HeadingRegex();

	[GeneratedRegex( @"^[ \t]*(?<char>[-*_])(?:[ \t]*\k<char>){2,}[ \t]*$" )]
	private static partial Regex HorizontalRuleRegex();

	[GeneratedRegex( @"\*\*(?<text>.+?)\*\*" )]
	private static partial Regex BoldRegex();

	public NewVersionAvailableWindow( string currentVersion, string changeLog )
	{
		var app = App.Instance!;

		app.MainWindow.MakeWindowVisible();

		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		LimitChangeLogHeight();

		CurrentVersion_TextBlock.Text = currentVersion;

		PopulateChangeLog( changeLog );
	}

	// Caps the changelog scroll region so the whole dialog always fits on screen and the Download/Cancel buttons
	// stay reachable -- even with a very long changelog. A fixed cap is not safe because WindowScaler wraps the
	// content in an AppUIScale LayoutTransform (up to 2x) and switches the window to size-to-content, so a fixed
	// 500px region becomes ~1000px on a high UI scale and can push the buttons off the bottom of the screen.
	private void LimitChangeLogHeight()
	{
		// The scroll viewer lives inside the scaled root, so its MaxHeight is in pre-transform units and renders at
		// MaxHeight * appUIScale on screen. We budget the on-screen work area, subtract the (non-scrolling) chrome,
		// then convert what is left back into the scroll viewer's own units by dividing out the scale.
		var appUIScale = Math.Max( 0.5, global::MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.AppUIScale );

		var workAreaHeight = SystemParameters.WorkArea.Height;

		// Window title bar + borders are not scaled by the LayoutTransform.
		const double unscaledChromeHeight = 48.0;

		// Logical height of everything in the dialog other than the scroll viewer (version text, labels, the download
		// question, the buttons, and all the surrounding margins) -- this content IS scaled by appUIScale.
		const double scaledNonScrollContentHeight = 280.0;

		var availableForScrollOnScreen = workAreaHeight - unscaledChromeHeight - ( scaledNonScrollContentHeight * appUIScale );

		var maxHeight = availableForScrollOnScreen / appUIScale;

		// Keep a usable minimum even on tiny screens, and avoid an oversized dialog on very large ones.
		ChangeLog_ScrollViewer.MaxHeight = Math.Clamp( maxHeight, 120.0, 700.0 );
	}

	private void PopulateChangeLog( string changeLog )
	{
		ChangeLog_Panel.Children.Clear();

		var textBlockStyle = (Style) FindResource( "MairaTextBlockStyle" );

		var lines = changeLog.Split( [ "\r\n", "\n", "\r" ], StringSplitOptions.None );

		// Tracks the leading-whitespace width of each open bullet level so that
		// arbitrary indentation (2 spaces, 4 spaces, tabs, ...) maps onto clean
		// nesting levels regardless of how the source markdown was written.
		var indentWidthStack = new List<int>();

		foreach ( var rawLine in lines )
		{
			var line = rawLine.TrimEnd();

			if ( string.IsNullOrWhiteSpace( line ) )
			{
				indentWidthStack.Clear();

				continue;
			}

			var headingMatch = HeadingRegex().Match( line );

			if ( headingMatch.Success )
			{
				indentWidthStack.Clear();

				ChangeLog_Panel.Children.Add( CreateHeadingTextBlock( headingMatch.Groups[ "text" ].Value.Trim(), textBlockStyle ) );

				continue;
			}

			if ( HorizontalRuleRegex().IsMatch( line ) )
			{
				indentWidthStack.Clear();

				ChangeLog_Panel.Children.Add( CreateHorizontalRule() );

				continue;
			}

			var bulletMatch = BulletRegex().Match( line );

			if ( bulletMatch.Success )
			{
				var indentWidth = GetIndentWidth( bulletMatch.Groups[ "indent" ].Value );

				while ( indentWidthStack.Count > 0 && indentWidth < indentWidthStack[ ^1 ] )
				{
					indentWidthStack.RemoveAt( indentWidthStack.Count - 1 );
				}

				if ( indentWidthStack.Count == 0 || indentWidth > indentWidthStack[ ^1 ] )
				{
					indentWidthStack.Add( indentWidth );
				}

				var bulletLevel = indentWidthStack.Count - 1;

				ChangeLog_Panel.Children.Add( CreateBulletRow( bulletLevel, bulletMatch.Groups[ "text" ].Value.Trim(), textBlockStyle ) );

				continue;
			}

			indentWidthStack.Clear();

			ChangeLog_Panel.Children.Add( CreateParagraphTextBlock( line.Trim(), textBlockStyle ) );
		}
	}

	private static int GetIndentWidth( string indent )
	{
		var indentWidth = 0;

		foreach ( var indentCharacter in indent )
		{
			indentWidth += ( indentCharacter == '\t' ) ? 4 : 1;
		}

		return indentWidth;
	}

	private static string GetBulletGlyph( int bulletLevel ) => ( bulletLevel % 3 ) switch
	{
		0 => "•", // • filled circle
		1 => "◦", // ◦ open circle
		_ => "▪", // ▪ filled square
	};

	private UIElement CreateBulletRow( int bulletLevel, string text, Style textBlockStyle )
	{
		const double indentPerLevel = 28.0;

		var bulletRowGrid = new Grid
		{
			Margin = new Thickness( bulletLevel * indentPerLevel, 0, 0, 6 ),
		};

		bulletRowGrid.ColumnDefinitions.Add( new ColumnDefinition { Width = GridLength.Auto } );
		bulletRowGrid.ColumnDefinitions.Add( new ColumnDefinition { Width = new GridLength( 1, GridUnitType.Star ) } );

		var bulletGlyphTextBlock = new TextBlock
		{
			Style = textBlockStyle,
			Text = GetBulletGlyph( bulletLevel ),
			FontSize = 20,
			Margin = new Thickness( 0, 0, 12, 0 ),
			VerticalAlignment = VerticalAlignment.Top,
		};

		bulletGlyphTextBlock.SetResourceReference( TextBlock.ForegroundProperty, "Brush.Foreground.Secondary" );

		Grid.SetColumn( bulletGlyphTextBlock, 0 );

		var bulletTextTextBlock = new TextBlock
		{
			Style = textBlockStyle,
			FontSize = 20,
			TextWrapping = TextWrapping.Wrap,
			VerticalAlignment = VerticalAlignment.Top,
		};

		AddInlineMarkup( bulletTextTextBlock, text );

		Grid.SetColumn( bulletTextTextBlock, 1 );

		bulletRowGrid.Children.Add( bulletGlyphTextBlock );
		bulletRowGrid.Children.Add( bulletTextTextBlock );

		return bulletRowGrid;
	}

	private TextBlock CreateParagraphTextBlock( string text, Style textBlockStyle )
	{
		var paragraphTextBlock = new TextBlock
		{
			Style = textBlockStyle,
			FontSize = 20,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness( 0, 0, 0, 6 ),
		};

		AddInlineMarkup( paragraphTextBlock, text );

		return paragraphTextBlock;
	}

	private static UIElement CreateHorizontalRule()
	{
		var horizontalRuleBorder = new Border
		{
			Height = 2,
			Margin = new Thickness( 0, 8, 0, 14 ),
		};

		horizontalRuleBorder.SetResourceReference( Border.BackgroundProperty, "Brush.Foreground.Secondary" );

		return horizontalRuleBorder;
	}

	private TextBlock CreateHeadingTextBlock( string text, Style textBlockStyle )
	{
		var headingTextBlock = new TextBlock
		{
			Style = textBlockStyle,
			FontSize = 24,
			FontWeight = FontWeights.Bold,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness( 0, 8, 0, 8 ),
		};

		headingTextBlock.SetResourceReference( TextBlock.ForegroundProperty, "Brush.Accent" );

		AddInlineMarkup( headingTextBlock, text );

		return headingTextBlock;
	}

	private static void AddInlineMarkup( TextBlock textBlock, string text )
	{
		var lastIndex = 0;

		foreach ( Match boldMatch in BoldRegex().Matches( text ) )
		{
			if ( boldMatch.Index > lastIndex )
			{
				textBlock.Inlines.Add( new Run( text[ lastIndex..boldMatch.Index ] ) );
			}

			textBlock.Inlines.Add( new Run( boldMatch.Groups[ "text" ].Value ) { FontWeight = FontWeights.Bold } );

			lastIndex = boldMatch.Index + boldMatch.Length;
		}

		if ( lastIndex < text.Length )
		{
			textBlock.Inlines.Add( new Run( text[ lastIndex.. ] ) );
		}
	}

	private void Download_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		DownloadUpdate = true;

		Close();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		DownloadUpdate = false;

		Close();
	}
}
