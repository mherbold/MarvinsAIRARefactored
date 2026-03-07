
using System.Windows;

using MarvinsAIRARefactored.Controls;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class SpeechToTextPage : UserControl
{
	public SpeechToTextPage()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void ResetOverlayWindow_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.SpeechToTextWindow.ResetWindow();
	}

	#endregion

	#region Logic

	public void UpdateLanguageOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SpeechToTextPage] UpdateLanguageOptions >>>" );

		var dictionary = new Dictionary<string, string>
		{
			// ---- European ----
			{ "en-US", "English (US)" },
			{ "en-GB", "English (UK)" },
			{ "fr-FR", "français" },
			{ "fr-CA", "français (Canada)" },
			{ "de-DE", "Deutsch" },
			{ "it-IT", "italiano" },
			{ "es-ES", "español (España)" },
			{ "es-MX", "español (México)" },
			{ "pt-PT", "português (Portugal)" },
			{ "pt-BR", "português (Brasil)" },
			{ "nl-NL", "Nederlands" },
			{ "nl-BE", "Nederlands (België)" },
			{ "pl-PL", "polski" },
			{ "cs-CZ", "čeština" },
			{ "sk-SK", "slovenčina" },
			{ "sl-SI", "slovenščina" },
			{ "hr-HR", "hrvatski" },
			{ "sr-RS", "српски" },
			{ "bs-BA", "bosanski" },
			{ "mk-MK", "македонски" },
			{ "bg-BG", "български" },
			{ "ro-RO", "română" },
			{ "hu-HU", "magyar" },
			{ "fi-FI", "suomi" },
			{ "sv-SE", "svenska" },
			{ "da-DK", "dansk" },
			{ "no-NO", "norsk bokmål" },
			{ "is-IS", "íslenska" },
			{ "et-EE", "eesti" },
			{ "lv-LV", "latviešu" },
			{ "lt-LT", "lietuvių" },
			{ "el-GR", "Ελληνικά" },
			{ "mt-MT", "Malti" },
			{ "ga-IE", "Gaeilge" },
			{ "cy-GB", "Cymraeg" },
			{ "ru-RU", "русский" },
			{ "uk-UA", "українська" },
			{ "be-BY", "беларуская" },
			{ "tr-TR", "Türkçe" },
			{ "sq-AL", "shqip" },

			// ---- Middle East / Central Asia ----
			{ "he-IL", "עברית" },
			{ "ar-SA", "العربية" },
			{ "fa-IR", "فارسی" },
			{ "ur-PK", "اردو" },
			{ "kk-KZ", "қазақ тілі" },
			{ "uz-UZ", "oʻzbekcha" },
			{ "az-AZ", "azərbaycan dili" },
			{ "hy-AM", "հայերեն" },
			{ "ka-GE", "ქართული" },

			// ---- South Asia ----
			{ "hi-IN", "हिन्दी" },
			{ "bn-BD", "বাংলা (বাংলাদেশ)" },
			{ "bn-IN", "বাংলা (ভারত)" },
			{ "ta-IN", "தமிழ்" },
			{ "te-IN", "తెలుగు" },
			{ "ml-IN", "മലയാളം" },
			{ "kn-IN", "ಕನ್ನಡ" },
			{ "gu-IN", "ગુજરાતી" },
			{ "pa-IN", "ਪੰਜਾਬੀ" },
			{ "si-LK", "සිංහල" },
			{ "ne-NP", "नेपाली" },

			// ---- East Asia ----
			{ "zh-CN", "中文 (简体)" },
			{ "zh-TW", "中文 (繁體)" },
			{ "zh-HK", "中文 (香港)" },
			{ "ja-JP", "日本語" },
			{ "ko-KR", "한국어" },

			// ---- Southeast Asia ----
			{ "th-TH", "ไทย" },
			{ "vi-VN", "Tiếng Việt" },
			{ "id-ID", "Bahasa Indonesia" },
			{ "ms-MY", "Bahasa Melayu" },
			{ "km-KH", "ភាសាខ្មែរ" },
			{ "lo-LA", "ລາວ" },
			{ "my-MM", "ဗမာ" },
			{ "fil-PH", "Filipino" },

			// ---- Other notable ----
			{ "af-ZA", "Afrikaans" },
			{ "sw-KE", "Kiswahili" },
			{ "zu-ZA", "isiZulu" }
		};

		Language_MairaComboBox.ItemsSource = dictionary.ToList();
		Language_MairaComboBox.SelectedValue = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.SpeechToTextLanguageCode;

		app.Logger.WriteLine( "[SpeechToTextPage] <<< UpdateLanguageOptions" );
	}

	#endregion
}
