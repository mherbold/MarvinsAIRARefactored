
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

	[GeneratedRegex( @"\*\*(?<text>.+?)\*\*" )]
	private static partial Regex BoldRegex();

	public NewVersionAvailableWindow( string currentVersion, string changeLog )
	{
		var app = App.Instance!;

		app.MainWindow.MakeWindowVisible();

		InitializeComponent();

		CurrentVersion_TextBlock.Text = currentVersion;

		PopulateChangeLog( changeLog );
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
