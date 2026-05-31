
using System.Windows;
using System.Windows.Input;

using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using TextBox = System.Windows.Controls.TextBox;

namespace MarvinsAIRARefactored.Classes;

public static class TextBoxBehaviors
{
	public static readonly DependencyProperty IsNumericOnlyProperty = DependencyProperty.RegisterAttached( "IsNumericOnly", typeof( bool ), typeof( TextBoxBehaviors ), new PropertyMetadata( false, OnIsNumericOnlyChanged ) );

	public static bool GetIsNumericOnly( TextBox textBox ) => (bool) textBox.GetValue( IsNumericOnlyProperty );
	public static void SetIsNumericOnly( TextBox textBox, bool value ) => textBox.SetValue( IsNumericOnlyProperty, value );

	private static void OnIsNumericOnlyChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is TextBox textBox )
		{
			if ( (bool) e.NewValue )
			{
				textBox.PreviewKeyDown += TextBox_PreviewKeyDown;
				textBox.PreviewTextInput += TextBox_PreviewTextInput;

				DataObject.AddPastingHandler( textBox, OnPaste );
			}
			else
			{
				textBox.PreviewKeyDown -= TextBox_PreviewKeyDown;
				textBox.PreviewTextInput -= TextBox_PreviewTextInput;

				DataObject.RemovePastingHandler( textBox, OnPaste );
			}
		}
	}

	private static void TextBox_PreviewKeyDown( object sender, System.Windows.Input.KeyEventArgs e )
	{
		if ( e.Key == Key.Space )
		{
			e.Handled = true;

			return;
		}

		switch ( e.Key )
		{
			case Key.Back:
			case Key.Delete:
			case Key.Tab:
			case Key.Left:
			case Key.Right:
			case Key.Home:
			case Key.End:
			{
				e.Handled = false;
				break;
			}
		}
	}

	private static void TextBox_PreviewTextInput( object sender, TextCompositionEventArgs e )
	{
		if ( sender is TextBox textBox )
		{
			e.Handled = !IsInsertionNumeric( textBox, e.Text );
		}
	}

	private static void OnPaste( object sender, DataObjectPastingEventArgs e )
	{
		if ( sender is not TextBox textBox )
		{
			e.CancelCommand();

			return;
		}

		if ( e.DataObject.GetDataPresent( DataFormats.Text ) )
		{
			var pastedText = e.DataObject.GetData( DataFormats.Text ) as string ?? string.Empty;

			if ( !IsInsertionNumeric( textBox, pastedText ) )
			{
				e.CancelCommand();
			}
		}
		else
		{
			e.CancelCommand();
		}
	}

	private static bool IsInsertionNumeric( TextBox textBox, string input )
	{
		if ( string.IsNullOrEmpty( input ) ) return false;
		if ( input.Any( char.IsWhiteSpace ) ) return false;

		var selectionStart = textBox.SelectionStart;
		var selectionLength = textBox.SelectionLength;

		var current = textBox.Text ?? string.Empty;
		var before = current[ ..selectionStart ];
		var after = current[ ( selectionStart + selectionLength ).. ];

		var proposed = before + input + after;

		if ( proposed.Length == 0 ) return false;

		if ( proposed.Any( c => !char.IsDigit( c ) ) ) return false;

		if ( proposed.Length > 1 && proposed.StartsWith( '0' ) ) return false;

		return int.TryParse( proposed, out _ );
	}
}
