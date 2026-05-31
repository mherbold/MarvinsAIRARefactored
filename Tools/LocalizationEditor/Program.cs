namespace LocalizationEditor;

/// <summary>
/// LocalizationEditor — bulk-edit TTS JSON phrase files and resx localization files
/// across all languages in one safe, UTF-8-correct operation.
///
/// Usage:
///   LocalizationEditor tts  &lt;command&gt; [args...]
///   LocalizationEditor resx &lt;command&gt; [args...]
///
/// TTS commands:
///   list-keys                             List all event keys; flag languages missing each
///   show-key     &lt;key&gt;                    Show all phrase variants for key across languages
///   add-key      &lt;key&gt;                    Add key — phrases sourced from AddTestPhrases() in this file
///   remove-key   &lt;key&gt;                    Remove key from all language files
///   rename-key   &lt;oldKey&gt; &lt;newKey&gt;        Rename key in all language files
///   set-phrases  &lt;key&gt; &lt;lang&gt; &lt;p1&gt; ...   Replace phrases for one language (lang="*" for all)
///   validate                              Check for missing/extra/empty keys
///   sync-keys                             Add missing keys to lagging language files
///
/// Resx commands:
///   list-keys                             List all string keys; flag languages missing each
///   show-key     &lt;key&gt;                    Show value for key across all language files
///   add-key      &lt;key&gt;                    Add key — values sourced from AddResxKey() in this file
///   remove-key   &lt;key&gt;                    Remove key from all files
///   rename-key   &lt;oldKey&gt; &lt;newKey&gt;        Rename key in all files
///   set-value    &lt;key&gt; &lt;lang&gt; &lt;value&gt;     Set value for key in one language (lang="base" for base)
///   validate                              Check for missing/extra/empty keys
///   sync-keys                             Add missing keys to lagging language files
/// </summary>
internal static class Program
{
	private static int Main(string[] args)
	{
		if (args.Length < 2)
		{
			PrintUsage();
			return 1;
		}

		var mode = args[0].ToLowerInvariant();
		var command = args[1].ToLowerInvariant();
		var rest = args[2..];

		try
		{
			Console.OutputEncoding = System.Text.Encoding.UTF8;

			switch (mode)
			{
				case "tts":
					return RunTts(command, rest);

				case "resx":
					return RunResx(command, rest);

				default:
					Console.Error.WriteLine($"Unknown mode '{mode}'. Use 'tts' or 'resx'.");
					return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"ERROR: {ex.Message}");
			return 2;
		}
	}

	// -------------------------------------------------------------------------
	// TTS dispatch
	// -------------------------------------------------------------------------

	private static int RunTts(string command, string[] args) => command switch
	{
		"list-keys" => Run(TtsEditor.ListKeys),
		"show-key"  => Run(() => TtsEditor.ShowKey(Require(args, 0, "key"))),
		"add-key"      => Run(() => AddTtsKey(Require(args, 0, "key"))),
			"overwrite-key"=> Run(() => OverwriteTtsKey(Require(args, 0, "key"))),
			"remove-key"   => Run(() => TtsEditor.RemoveKey(Require(args, 0, "key"))),
		"rename-key"=> Run(() => TtsEditor.RenameKey(Require(args, 0, "oldKey"), Require(args, 1, "newKey"))),
		"set-phrases"=> Run(() => TtsEditor.SetPhrases(
							Require(args, 0, "key"),
							Require(args, 1, "lang"),
							args[2..])),
		"validate"  => Run(TtsEditor.Validate),
		"sync-keys" => Run(() => TtsEditor.SyncKeys(new())),
		_ => UnknownCommand(command)
	};

	// -------------------------------------------------------------------------
	// Resx dispatch
	// -------------------------------------------------------------------------

	private static int RunResx(string command, string[] args) => command switch
	{
		"list-keys"  => Run(ResxEditor.ListKeys),
		"show-key"   => Run(() => ResxEditor.ShowKey(Require(args, 0, "key"))),
		"add-key"    => Run(() => AddResxKey(Require(args, 0, "key"))),
		"remove-key" => Run(() => ResxEditor.RemoveKey(Require(args, 0, "key"))),
		"rename-key" => Run(() => ResxEditor.RenameKey(Require(args, 0, "oldKey"), Require(args, 1, "newKey"))),
		"set-value"  => Run(() =>
						{
							var lang = Require(args, 1, "lang");
							ResxEditor.SetValue(
								Require(args, 0, "key"),
								lang == "base" ? null : lang,
								Require(args, 2, "value"));
						}),
		"validate"   => Run(ResxEditor.Validate),
		"sync-keys"  => Run(() => ResxEditor.SyncKeys(new())),
		_ => UnknownCommand(command)
	};

	// -------------------------------------------------------------------------
	// Key-add factories
	// These methods are what the agent (GitHub Copilot) edits when it needs to
	// bulk-add a new key with full translations. Edit the dictionaries below,
	// then run: LocalizationEditor tts add-key <key>
	//       or: LocalizationEditor resx add-key <key>
	// -------------------------------------------------------------------------

	/// <summary>
	/// Provides per-language phrase arrays for a TTS add-key operation.
	/// Edit this method to add new keys with full translations.
	/// </summary>
	private static void AddTtsKey(string key)
	{
		var phrases = key switch
		{
			"TestPhrase0" => BuildTestPhrase0(),
				"TestPhrase1" => BuildTestPhrase1(),
				"TestPhrase2" => BuildTestPhrase2(),
				"TestPhrase3" => BuildTestPhrase3(),
				"TestPhrase4" => BuildTestPhrase4(),
				"SpotterFlagOneLapToGreen" => BuildSpotterFlagOneLapToGreen(),
				"SpotterFlagGreen"         => BuildSpotterFlagGreen(),
				"SpotterFlagBlue"          => BuildSpotterFlagBlue(),
				"SpotterFlagYellowWaving"  => BuildSpotterFlagYellowWaving(),
				"SpotterFlagCautionWaving" => BuildSpotterFlagCautionWaving(),
				"SpotterFlagDebris"        => BuildSpotterFlagDebris(),
				"SpotterFlagWhite"         => BuildSpotterFlagWhite(),
				"SpotterFlagCheckered"     => BuildSpotterFlagCheckered(),
				"SpotterFlagBlack"         => BuildSpotterFlagBlack(),
				"SpotterFlagDisqualify"    => BuildSpotterFlagDisqualify(),
				"SpotterFlagRepair"        => BuildSpotterFlagRepair(),
				"SpotterFlagStartReady"    => BuildSpotterFlagStartReady(),
					_ => throw new InvalidOperationException(
				$"No phrase data defined for TTS key '{key}'. " +
				$"Add a case to AddTtsKey() in Program.cs.")
		};

		TtsEditor.AddKey(key, phrases);
	}

	private static void OverwriteTtsKey(string key)
	{
		var phrases = key switch
		{
			"TestPhrase0"              => BuildTestPhrase0(),
			"TestPhrase1"              => BuildTestPhrase1(),
			"TestPhrase2"              => BuildTestPhrase2(),
			"TestPhrase3"              => BuildTestPhrase3(),
			"TestPhrase4"              => BuildTestPhrase4(),
			"SpotterFlagOneLapToGreen" => BuildSpotterFlagOneLapToGreen(),
			"SpotterFlagGreen"         => BuildSpotterFlagGreen(),
			"SpotterFlagBlue"          => BuildSpotterFlagBlue(),
			"SpotterFlagYellowWaving"  => BuildSpotterFlagYellowWaving(),
			"SpotterFlagCautionWaving" => BuildSpotterFlagCautionWaving(),
			"SpotterFlagDebris"        => BuildSpotterFlagDebris(),
			"SpotterFlagWhite"         => BuildSpotterFlagWhite(),
			"SpotterFlagCheckered"     => BuildSpotterFlagCheckered(),
			"SpotterFlagBlack"         => BuildSpotterFlagBlack(),
			"SpotterFlagDisqualify"    => BuildSpotterFlagDisqualify(),
			"SpotterFlagRepair"        => BuildSpotterFlagRepair(),
			"SpotterFlagStartReady"    => BuildSpotterFlagStartReady(),
			_ => throw new InvalidOperationException(
				$"No phrase data defined for TTS key '{key}'. " +
				$"Add a case to OverwriteTtsKey() in Program.cs.")
		};

		TtsEditor.OverwriteKey(key, phrases);
	}

	/// <summary>
	/// Provides per-language values for a resx add-key operation.
	/// Edit this method to add new keys with full translations.
	/// </summary>
	private static void AddResxKey(string key)
	{
		var values = key switch
		{
			"ResetFanPowerCurveToDefaults" => new Dictionary<string, string>
				{
					["ca-ES"]   = "Restableix als valors predeterminats",
					["cs-CZ"]   = "Obnovit výchozí hodnoty",
					["cy-GB"]   = "Ailosod i'r Rhagosodiadau",
					["da-DK"]   = "Nulstil til standarder",
					["de-DE"]   = "Auf Standardwerte zurücksetzen",
					["es-ES"]   = "Restablecer valores predeterminados",
					["es-MX"]   = "Restablecer valores predeterminados",
					["fi-FI"]   = "Palauta oletusasetukset",
					["fr-CA"]   = "Réinitialiser aux valeurs par défaut",
					["fr-FR"]   = "Réinitialiser aux valeurs par défaut",
					["he-IL"]   = "איפוס לברירות מחדל",
					["hu-HU"]   = "Visszaállítás alapértékekre",
					["hy-AM"]   = "Վերականգնել կանխադրված արժեքները",
					["it-IT"]   = "Ripristina valori predefiniti",
					["ja-JP"]   = "デフォルトにリセット",
					["nb-NO"]   = "Tilbakestill til standard",
					["nl-NL"]   = "Terugzetten naar standaard",
					["pl-PL"]   = "Przywróć ustawienia domyślne",
					["pt-BR"]   = "Redefinir para padrões",
					["pt-PT"]   = "Repor predefinições",
					["ro-RO"]   = "Resetare la valorile implicite",
					["ru-RU"]   = "Сбросить до значений по умолчанию",
					["sv-SE"]   = "Återställ till standard",
					["th-TH"]   = "รีเซ็ตเป็นค่าเริ่มต้น",
					["tr-TR"]   = "Varsayılanlara sıfırla",
					["uk-UA"]   = "Скинути до стандартних значень",
					["zh-Hans"] = "重置为默认值",
				},
				"FlagCalls" => new Dictionary<string, string>
				{
					["ca-ES"]   = "Avisos de bandera",
					["cs-CZ"]   = "Hlášení vlajek",
					["cy-GB"]   = "Galwadau baner",
					["da-DK"]   = "Flagopkald",
					["de-DE"]   = "Flaggen-Ansagen",
					["es-ES"]   = "Avisos de bandera",
					["es-MX"]   = "Avisos de bandera",
					["fi-FI"]   = "Lippuilmoitukset",
					["fr-CA"]   = "Annonces de drapeau",
					["fr-FR"]   = "Annonces de drapeau",
					["he-IL"]   = "קריאות דגל",
					["hu-HU"]   = "Zászlójelzések",
					["hy-AM"]   = "Դրոշի հայտարարություններ",
					["it-IT"]   = "Avvisi bandiera",
					["ja-JP"]   = "フラグ通知",
					["nb-NO"]   = "Flaggmeldinger",
					["nl-NL"]   = "Vlagmeldingen",
					["pl-PL"]   = "Komunikaty flag",
					["pt-BR"]   = "Avisos de bandeira",
					["pt-PT"]   = "Avisos de bandeira",
					["ro-RO"]   = "Anunțuri steag",
					["ru-RU"]   = "Объявления флагов",
					["sv-SE"]   = "Flagganrop",
					["th-TH"]   = "การแจ้งเตือนธง",
					["tr-TR"]   = "Bayrak duyuruları",
					["uk-UA"]   = "Оголошення прапорів",
					["zh-Hans"] = "旗帜通报",
				},
			_ => throw new InvalidOperationException(
				$"No value data defined for resx key '{key}'. " +
				$"Add a case to AddResxKey() in Program.cs.")
		};

		ResxEditor.AddKey(key, "Flag Calls", values);
	}

	// -------------------------------------------------------------------------
	// TestPhrase phrase data — full translations for all 25 TTS languages
	// -------------------------------------------------------------------------

	// Slot 0 — Crew Chief
	private static Dictionary<string, string[]> BuildTestPhrase0() => new()
	{
		["en-US"] = ["Testing voice.", "Comm check — how do you read me?", "This is a voice test."],
		["cs-CZ"] = ["Testování hlasu.", "Zkouška komunikace — slyšíte mě?", "Toto je zkouška hlasu."],
		["da-DK"] = ["Tester stemme.", "Komm-tjek — hører du mig?", "Dette er en stemmetest."],
		["de-DE"] = ["Sprachtest.", "Funkprüfung — können Sie mich hören?", "Das ist ein Sprachtest."],
		["es-ES"] = ["Probando voz.", "Comprobación de comunicación — ¿me recibes?", "Esto es una prueba de voz."],
		["es-MX"] = ["Probando voz.", "Prueba de radio — ¿me escuchas?", "Esto es una prueba de voz."],
		["fi-FI"] = ["Testataan ääntä.", "Yhteyden tarkistus — kuuletko minut?", "Tämä on äänitesti."],
		["fr-CA"] = ["Test de voix.", "Vérification comm — tu me reçois?", "Ceci est un test de voix."],
		["fr-FR"] = ["Test de voix.", "Vérification comm — vous me recevez?", "Ceci est un test de voix."],
		["he-IL"] = ["בודק קול.", "בדיקת תקשורת — האם אתה שומע אותי?", "זהו מבחן קול."],
		["hu-HU"] = ["Hangpróba.", "Kommunikációs teszt — hallasz engem?", "Ez egy hangpróba."],
		["it-IT"] = ["Prova voce.", "Controllo comunicazioni — mi ricevi?", "Questo è un test vocale."],
		["ja-JP"] = ["ボイステスト。", "通信確認 — 聞こえますか？", "これはボイステストです。"],
		["nb-NO"] = ["Tester stemme.", "Sambandssjekk — hører du meg?", "Dette er en stemmetest."],
		["nl-NL"] = ["Spraaktest.", "Communicatiecheck — kun je me horen?", "Dit is een spraaktest."],
		["pl-PL"] = ["Test głosu.", "Sprawdzenie łączności — słyszysz mnie?", "To jest test głosu."],
		["pt-BR"] = ["Testando voz.", "Verificação de rádio — você me ouve?", "Isso é um teste de voz."],
		["pt-PT"] = ["A testar a voz.", "Verificação de comunicação — está a ouvir-me?", "Isto é um teste de voz."],
		["ro-RO"] = ["Test voce.", "Verificare comunicații — mă auzi?", "Acesta este un test vocal."],
		["ru-RU"] = ["Тест голоса.", "Проверка связи — слышите меня?", "Это тест голоса."],
		["sv-SE"] = ["Testar röst.", "Kommunikationstest — hör du mig?", "Det här är ett rösttest."],
		["th-TH"] = ["ทดสอบเสียง.", "ตรวจสอบการสื่อสาร — ได้ยินฉันไหม?", "นี่คือการทดสอบเสียง."],
		["tr-TR"] = ["Ses testi.", "İletişim kontrolü — beni duyuyor musun?", "Bu bir ses testidir."],
		["uk-UA"] = ["Тест голосу.", "Перевірка зв'язку — чуєте мене?", "Це тест голосу."],
		["zh-Hans"] = ["语音测试。", "通讯检查 — 能听到我吗？", "这是一个语音测试。"],
	};

	// Slot 1 — Spotter
	private static Dictionary<string, string[]> BuildTestPhrase1() => new()
	{
		["en-US"] = ["Spotter online.", "Spotter here, comms check.", "Spotter active."],
		["cs-CZ"] = ["Spotter online.", "Spotter tady, zkouška komunikace.", "Spotter aktivní."],
		["da-DK"] = ["Spotter online.", "Spotter her, komm-tjek.", "Spotter aktiv."],
		["de-DE"] = ["Spotter online.", "Spotter hier, Funkprüfung.", "Spotter aktiv."],
		["es-ES"] = ["Observador en línea.", "Observador aquí, prueba de comunicación.", "Observador activo."],
		["es-MX"] = ["Observador en línea.", "Observador aquí, prueba de radio.", "Observador activo."],
		["fi-FI"] = ["Tarkkailija online.", "Tarkkailija täällä, yhteyden tarkistus.", "Tarkkailija aktiivinen."],
		["fr-CA"] = ["Guetteur en ligne.", "Guetteur ici, vérification comm.", "Guetteur actif."],
		["fr-FR"] = ["Guetteur en ligne.", "Guetteur ici, vérification comm.", "Guetteur actif."],
		["he-IL"] = ["ספוטר מקוון.", "ספוטר כאן, בדיקת תקשורת.", "ספוטר פעיל."],
		["hu-HU"] = ["Spotter online.", "Spotter itt, kommunikációs teszt.", "Spotter aktív."],
		["it-IT"] = ["Spotter online.", "Spotter qui, controllo comunicazioni.", "Spotter attivo."],
		["ja-JP"] = ["スポッターオンライン。", "スポッターです、通信確認。", "スポッター起動中。"],
		["nb-NO"] = ["Spotter online.", "Spotter her, sambandssjekk.", "Spotter aktiv."],
		["nl-NL"] = ["Spotter online.", "Spotter hier, communicatiecheck.", "Spotter actief."],
		["pl-PL"] = ["Spotter online.", "Spotter tutaj, sprawdzenie łączności.", "Spotter aktywny."],
		["pt-BR"] = ["Observador online.", "Observador aqui, verificação de rádio.", "Observador ativo."],
		["pt-PT"] = ["Observador online.", "Observador aqui, verificação de comunicação.", "Observador ativo."],
		["ro-RO"] = ["Spotter online.", "Spotter aici, verificare comunicații.", "Spotter activ."],
		["ru-RU"] = ["Споттер на связи.", "Споттер здесь, проверка связи.", "Споттер активен."],
		["sv-SE"] = ["Spotter online.", "Spotter här, kommunikationstest.", "Spotter aktiv."],
		["th-TH"] = ["สปอตเตอร์ออนไลน์.", "สปอตเตอร์ที่นี่, ตรวจสอบการสื่อสาร.", "สปอตเตอร์ทำงาน."],
		["tr-TR"] = ["Spotter çevrimiçi.", "Spotter burada, iletişim kontrolü.", "Spotter aktif."],
		["uk-UA"] = ["Споттер онлайн.", "Споттер тут, перевірка зв'язку.", "Споттер активний."],
		["zh-Hans"] = ["观察员上线。", "观察员在此，通讯检查。", "观察员已就位。"],
	};

	// Slot 2 — Sportscaster 1
	private static Dictionary<string, string[]> BuildTestPhrase2() => new()
	{
		["en-US"] = ["[excitedly] Sportscaster one, testing!", "Mic check — sportscaster one here.", "[cheerfully] Sportscaster one online."],
		["cs-CZ"] = ["[excitedly] Sportovní komentátor jedna, testování!", "Zkouška mikrofonu — sportovní komentátor jedna.", "[cheerfully] Sportovní komentátor jedna online."],
		["da-DK"] = ["[excitedly] Sportskommentator et, tester!", "Mik-tjek — sportskommentator et her.", "[cheerfully] Sportskommentator et online."],
		["de-DE"] = ["[excitedly] Sportkommentator eins, Test!", "Mikrofonprüfung — Sportkommentator eins hier.", "[cheerfully] Sportkommentator eins online."],
		["es-ES"] = ["[excitedly] ¡Comentarista uno, probando!", "Prueba de micrófono — comentarista uno aquí.", "[cheerfully] Comentarista uno en línea."],
		["es-MX"] = ["[excitedly] ¡Comentarista uno, probando!", "Prueba de micro — comentarista uno aquí.", "[cheerfully] Comentarista uno en línea."],
		["fi-FI"] = ["[excitedly] Selostaja yksi, testataan!", "Mikrofonitesti — selostaja yksi täällä.", "[cheerfully] Selostaja yksi online."],
		["fr-CA"] = ["[excitedly] Commentateur un, test!", "Vérification micro — commentateur un ici.", "[cheerfully] Commentateur un en ligne."],
		["fr-FR"] = ["[excitedly] Commentateur un, test!", "Vérification micro — commentateur un ici.", "[cheerfully] Commentateur un en ligne."],
		["he-IL"] = ["[excitedly] פרשן ספורט אחד, בדיקה!", "בדיקת מיקרופון — פרשן ספורט אחד כאן.", "[cheerfully] פרשן ספורט אחד מקוון."],
		["hu-HU"] = ["[excitedly] Kommentátor egy, teszt!", "Mikrofonpróba — kommentátor egy itt.", "[cheerfully] Kommentátor egy online."],
		["it-IT"] = ["[excitedly] Commentatore uno, test!", "Controllo microfono — commentatore uno qui.", "[cheerfully] Commentatore uno online."],
		["ja-JP"] = ["[excitedly] スポーツキャスター1、テスト！", "マイクチェック — スポーツキャスター1です。", "[cheerfully] スポーツキャスター1オンライン。"],
		["nb-NO"] = ["[excitedly] Sportskommentator en, tester!", "Mik-sjekk — sportskommentator en her.", "[cheerfully] Sportskommentator en online."],
		["nl-NL"] = ["[excitedly] Sportcommentator één, test!", "Microfooncheck — sportcommentator één hier.", "[cheerfully] Sportcommentator één online."],
		["pl-PL"] = ["[excitedly] Komentator sportowy jeden, test!", "Sprawdzenie mikrofonu — komentator jeden tutaj.", "[cheerfully] Komentator jeden online."],
		["pt-BR"] = ["[excitedly] Comentarista um, testando!", "Teste de microfone — comentarista um aqui.", "[cheerfully] Comentarista um online."],
		["pt-PT"] = ["[excitedly] Comentador um, a testar!", "Teste de microfone — comentador um aqui.", "[cheerfully] Comentador um online."],
		["ro-RO"] = ["[excitedly] Comentator sportiv unu, test!", "Verificare microfon — comentator unu aici.", "[cheerfully] Comentator unu online."],
		["ru-RU"] = ["[excitedly] Спортивный комментатор один, тест!", "Проверка микрофона — комментатор один здесь.", "[cheerfully] Комментатор один на связи."],
		["sv-SE"] = ["[excitedly] Sportkommentator ett, testar!", "Mikrofontest — sportkommentator ett här.", "[cheerfully] Sportkommentator ett online."],
		["th-TH"] = ["[excitedly] ผู้บรรยากีฬาหนึ่ง, ทดสอบ!", "ตรวจสอบไมค์ — ผู้บรรยากีฬาหนึ่งที่นี่.", "[cheerfully] ผู้บรรยากีฬาหนึ่งออนไลน์."],
		["tr-TR"] = ["[excitedly] Spor spikeri bir, test!", "Mikrofon kontrolü — spor spikeri bir burada.", "[cheerfully] Spor spikeri bir çevrimiçi."],
		["uk-UA"] = ["[excitedly] Спортивний коментатор один, тест!", "Перевірка мікрофона — коментатор один тут.", "[cheerfully] Коментатор один онлайн."],
		["zh-Hans"] = ["[excitedly] 体育解说员一号，测试！", "麦克风检查 — 解说员一号在此。", "[cheerfully] 解说员一号上线。"],
	};

	// Slot 3 — Sportscaster 2
	private static Dictionary<string, string[]> BuildTestPhrase3() => new()
	{
		["en-US"] = ["Sportscaster two, testing.", "Mic check — sportscaster two here.", "Sportscaster two online."],
		["cs-CZ"] = ["Sportovní komentátor dva, testování.", "Zkouška mikrofonu — sportovní komentátor dva.", "Sportovní komentátor dva online."],
		["da-DK"] = ["Sportskommentator to, tester.", "Mik-tjek — sportskommentator to her.", "Sportskommentator to online."],
		["de-DE"] = ["Sportkommentator zwei, Test.", "Mikrofonprüfung — Sportkommentator zwei hier.", "Sportkommentator zwei online."],
		["es-ES"] = ["Comentarista dos, probando.", "Prueba de micrófono — comentarista dos aquí.", "Comentarista dos en línea."],
		["es-MX"] = ["Comentarista dos, probando.", "Prueba de micro — comentarista dos aquí.", "Comentarista dos en línea."],
		["fi-FI"] = ["Selostaja kaksi, testataan.", "Mikrofonitesti — selostaja kaksi täällä.", "Selostaja kaksi online."],
		["fr-CA"] = ["Commentateur deux, test.", "Vérification micro — commentateur deux ici.", "Commentateur deux en ligne."],
		["fr-FR"] = ["Commentateur deux, test.", "Vérification micro — commentateur deux ici.", "Commentateur deux en ligne."],
		["he-IL"] = ["פרשן ספורט שניים, בדיקה.", "בדיקת מיקרופון — פרשן ספורט שניים כאן.", "פרשן ספורט שניים מקוון."],
		["hu-HU"] = ["Kommentátor kettő, teszt.", "Mikrofonpróba — kommentátor kettő itt.", "Kommentátor kettő online."],
		["it-IT"] = ["Commentatore due, test.", "Controllo microfono — commentatore due qui.", "Commentatore due online."],
		["ja-JP"] = ["スポーツキャスター2、テスト。", "マイクチェック — スポーツキャスター2です。", "スポーツキャスター2オンライン。"],
		["nb-NO"] = ["Sportskommentator to, tester.", "Mik-sjekk — sportskommentator to her.", "Sportskommentator to online."],
		["nl-NL"] = ["Sportcommentator twee, test.", "Microfooncheck — sportcommentator twee hier.", "Sportcommentator twee online."],
		["pl-PL"] = ["Komentator sportowy dwa, test.", "Sprawdzenie mikrofonu — komentator dwa tutaj.", "Komentator dwa online."],
		["pt-BR"] = ["Comentarista dois, testando.", "Teste de microfone — comentarista dois aqui.", "Comentarista dois online."],
		["pt-PT"] = ["Comentador dois, a testar.", "Teste de microfone — comentador dois aqui.", "Comentador dois online."],
		["ro-RO"] = ["Comentator sportiv doi, test.", "Verificare microfon — comentator doi aici.", "Comentator doi online."],
		["ru-RU"] = ["Спортивный комментатор два, тест.", "Проверка микрофона — комментатор два здесь.", "Комментатор два на связи."],
		["sv-SE"] = ["Sportkommentator två, testar.", "Mikrofontest — sportkommentator två här.", "Sportkommentator två online."],
		["th-TH"] = ["ผู้บรรยากีฬาสอง, ทดสอบ.", "ตรวจสอบไมค์ — ผู้บรรยากีฬาสองที่นี่.", "ผู้บรรยากีฬาสองออนไลน์."],
		["tr-TR"] = ["Spor spikeri iki, test.", "Mikrofon kontrolü — spor spikeri iki burada.", "Spor spikeri iki çevrimiçi."],
		["uk-UA"] = ["Спортивний коментатор два, тест.", "Перевірка мікрофона — коментатор два тут.", "Коментатор два онлайн."],
		["zh-Hans"] = ["体育解说员二号，测试。", "麦克风检查 — 解说员二号在此。", "解说员二号上线。"],
	};

	// Slot 4 — Pit Reporter
	private static Dictionary<string, string[]> BuildTestPhrase4() => new()
	{
		["en-US"] = ["Pit reporter, testing.", "Pit lane, mic check.", "Pit reporter online."],
		["cs-CZ"] = ["Pitový reportér, testování.", "Pit lane, zkouška mikrofonu.", "Pitový reportér online."],
		["da-DK"] = ["Pit-reporter, tester.", "Pit lane, mik-tjek.", "Pit-reporter online."],
		["de-DE"] = ["Pit-Reporter, Test.", "Boxengasse, Mikrofonprüfung.", "Pit-Reporter online."],
		["es-ES"] = ["Reportero de pits, probando.", "Pit lane, prueba de micrófono.", "Reportero de pits en línea."],
		["es-MX"] = ["Reportero de pits, probando.", "Pit lane, prueba de micro.", "Reportero de pits en línea."],
		["fi-FI"] = ["Pit-raportoija, testataan.", "Pit lane, mikrofonitesti.", "Pit-raportoija online."],
		["fr-CA"] = ["Journaliste des stands, test.", "Pit lane, vérification micro.", "Journaliste des stands en ligne."],
		["fr-FR"] = ["Journaliste des stands, test.", "Pit lane, vérification micro.", "Journaliste des stands en ligne."],
		["he-IL"] = ["כתב הפיט, בדיקה.", "פיט ליין, בדיקת מיקרופון.", "כתב הפיט מקוון."],
		["hu-HU"] = ["Pit-riporter, teszt.", "Boxutca, mikrofonpróba.", "Pit-riporter online."],
		["it-IT"] = ["Reporter del pit, test.", "Corsia dei box, controllo microfono.", "Reporter del pit online."],
		["ja-JP"] = ["ピットレポーター、テスト。", "ピットレーン、マイクチェック。", "ピットレポーターオンライン。"],
		["nb-NO"] = ["Pit-reporter, tester.", "Pit lane, mik-sjekk.", "Pit-reporter online."],
		["nl-NL"] = ["Pit-reporter, test.", "Pit lane, microfooncheck.", "Pit-reporter online."],
		["pl-PL"] = ["Reporter z pit lane, test.", "Pit lane, sprawdzenie mikrofonu.", "Reporter z pit lane online."],
		["pt-BR"] = ["Repórter do pit, testando.", "Pit lane, teste de microfone.", "Repórter do pit online."],
		["pt-PT"] = ["Repórter do pit, a testar.", "Pit lane, teste de microfone.", "Repórter do pit online."],
		["ro-RO"] = ["Reporter pit, test.", "Pit lane, verificare microfon.", "Reporter pit online."],
		["ru-RU"] = ["Репортёр с пит-лейна, тест.", "Пит-лейн, проверка микрофона.", "Репортёр с пит-лейна на связи."],
		["sv-SE"] = ["Pit-reporter, testar.", "Pit lane, mikrofontest.", "Pit-reporter online."],
		["th-TH"] = ["นักข่าวพิต, ทดสอบ.", "พิตเลน, ตรวจสอบไมค์.", "นักข่าวพิตออนไลน์."],
		["tr-TR"] = ["Pit muhabiri, test.", "Pit lane, mikrofon kontrolü.", "Pit muhabiri çevrimiçi."],
		["uk-UA"] = ["Репортер з піт-лейну, тест.", "Піт-лейн, перевірка мікрофона.", "Репортер з піт-лейну онлайн."],
		["zh-Hans"] = ["维修区记者，测试。", "维修区通道，麦克风检查。", "维修区记者上线。"],
	};

	// -------------------------------------------------------------------------
	// SpotterFlag phrase data — full translations for all 25 TTS languages
	// -------------------------------------------------------------------------

	private static Dictionary<string, string[]> BuildSpotterFlagOneLapToGreen() => new()
	{
		["en-US"] = ["One lap to green!", "One lap remaining before green!", "Get ready — one lap to the green flag!", "Almost there — one more lap before we go green!"],
		["cs-CZ"] = ["Jeden kolo do zelené!", "Zbývá jedno kolo do zeleného startu!", "Připrav se — jedno kolo do zelené vlajky!", "Skoro tam — ještě jedno kolo a jedeme!"],
		["da-DK"] = ["Et omgang til grønt!", "Et omgang tilbage til grøn!", "Gør dig klar — et omgang til det grønne flag!", "Næsten — endnu et omgang før vi kører grønt!"],
		["de-DE"] = ["Eine Runde bis zur Freigabe!", "Noch eine Runde bis zur Freigabe!", "Mach dich bereit — eine Runde bis zur Grünen Flagge!", "Fast da — noch eine Runde bis wir freigegeben werden!"],
		["es-ES"] = ["¡Una vuelta para la salida!", "¡Queda una vuelta para la salida!", "¡Prepárate — una vuelta para la bandera verde!", "¡Casi llegamos — una vuelta más antes de que salgamos!"],
		["es-MX"] = ["¡Una vuelta para la salida!", "¡Queda una vuelta para el verde!", "¡Prepárate — una vuelta para la bandera verde!", "¡Ya mero — una vuelta más y arrancamos!"],
		["fi-FI"] = ["Yksi kierros vihreään!", "Yksi kierros jäljellä ennen vihreää!", "Valmistaudu — yksi kierros vihreään lippuun!", "Melkein perillä — yksi kierros ennen kuin lähdemme!"],
		["fr-CA"] = ["Un tour avant le vert!", "Un tour restant avant le vert!", "Prépare-toi — un tour avant le drapeau vert!", "On y est presque — encore un tour avant le vert!"],
		["fr-FR"] = ["Un tour avant le vert!", "Un tour restant avant le vert!", "Préparez-vous — un tour avant le drapeau vert!", "On y est presque — encore un tour avant le vert!"],
		["he-IL"] = ["סיבוב אחד לדגל ירוק!", "סיבוב אחד נותר לפני ירוק!", "התכונן — סיבוב אחד לדגל הירוק!", "כמעט — עוד סיבוב אחד לפני שיוצאים!"],
		["hu-HU"] = ["Egy kör a zöld előtt!", "Egy kör maradt a zöld rajtig!", "Készülj — egy kör a zöld zászlóig!", "Már majdnem — még egy kör és mehetünk!"],
		["it-IT"] = ["Un giro al verde!", "Un giro al via verde!", "Preparati — un giro alla bandiera verde!", "Ci siamo quasi — ancora un giro prima del verde!"],
		["ja-JP"] = ["グリーンまで1周！", "グリーンスタートまで残り1周！", "準備して — グリーンフラッグまで1周！", "もうすぐ — あと1周でスタートだ！"],
		["nb-NO"] = ["En runde til grønt!", "En runde igjen til grønt!", "Gjør deg klar — en runde til det grønne flagget!", "Nesten — en runde til før vi går grønt!"],
		["nl-NL"] = ["Eén ronde tot groen!", "Nog één ronde voor het groene vlag!", "Maak je klaar — één ronde tot de groene vlag!", "Bijna — nog één ronde voor we groen gaan!"],
		["pl-PL"] = ["Jedno okrążenie do zielonej!", "Pozostało jedno okrążenie do startu!", "Gotuj się — jedno okrążenie do zielonej flagi!", "Prawie — jeszcze jedno okrążenie i ruszamy!"],
		["pt-BR"] = ["Uma volta para o verde!", "Uma volta restante antes do verde!", "Prepare-se — uma volta para a bandeira verde!", "Quase lá — mais uma volta antes do verde!"],
		["pt-PT"] = ["Uma volta para o verde!", "Uma volta para a bandeira verde!", "Prepara-te — uma volta para a bandeira verde!", "Quase lá — mais uma volta antes do verde!"],
		["ro-RO"] = ["Un tur până la verde!", "A mai rămas un tur până la verde!", "Pregătește-te — un tur până la steagul verde!", "Aproape — mai un tur și plecăm la verde!"],
		["ru-RU"] = ["Один круг до зелёного!", "Остался один круг до старта!", "Приготовься — один круг до зелёного флага!", "Уже близко — ещё один круг и стартуем!"],
		["sv-SE"] = ["Ett varv till grönt!", "Ett varv kvar till grönt!", "Gör dig redo — ett varv till gröna flaggan!", "Nästan — ett varv till innan vi går grönt!"],
		["th-TH"] = ["อีกหนึ่งรอบก่อนเขียว!", "เหลืออีกหนึ่งรอบก่อนธงเขียว!", "เตรียมตัว — อีกหนึ่งรอบก่อนธงเขียว!", "เกือบแล้ว — อีกหนึ่งรอบก็ออกตัว!"],
		["tr-TR"] = ["Yeşile bir tur kaldı!", "Yeşil bayrak öncesi bir tur kaldı!", "Hazırlan — yeşil bayrak öncesi bir tur!", "Neredeyse — bir tur daha ve yeşil!"],
		["uk-UA"] = ["Один круг до зеленого!", "Залишився один круг до старту!", "Готуйся — один круг до зеленого прапора!", "Майже — ще один круг і їдемо!"],
		["zh-Hans"] = ["绿灯前还有一圈！", "绿旗前还剩一圈！", "准备好 — 还有一圈就绿旗了！", "快到了 — 再跑一圈就出发！"],
	};

	private static Dictionary<string, string[]> BuildSpotterFlagGreen() => new()
	{
		["en-US"] = ["[excitedly] Green flag, go go go!", "[excitedly] Green flag! Let's go!", "[excitedly] We're green, punch it!", "[excitedly] Green flag is out — go, go, go!", "[excitedly] It's green, hit the gas!"],
		["cs-CZ"] = ["[excitedly] Zelená vlajka, jeď jeď jeď!", "[excitedly] Zelená vlajka! Jedeme!", "[excitedly] Zelená, přidej plyn!", "[excitedly] Zelená vlajka — jeď, jeď, jeď!", "[excitedly] Zelená, šlápni na to!"],
		["da-DK"] = ["[excitedly] Grønt flag, kør kør kør!", "[excitedly] Grønt flag! Afsted!", "[excitedly] Vi er grønne, tryk på det!", "[excitedly] Grønt flag ude — kør, kør, kør!", "[excitedly] Det er grønt, giv gas!"],
		["de-DE"] = ["[excitedly] Grünes Licht, los los los!", "[excitedly] Grüne Flagge! Los geht's!", "[excitedly] Freigabe, gib Gas!", "[excitedly] Grüne Flagge — los, los, los!", "[excitedly] Grün, Vollgas!"],
		["es-ES"] = ["[excitedly] ¡Bandera verde, vamos vamos vamos!", "[excitedly] ¡Bandera verde! ¡Vamos!", "[excitedly] ¡Verde, a fondo!", "[excitedly] ¡Bandera verde — vamos, vamos, vamos!", "[excitedly] ¡Verde, pisa el acelerador!"],
		["es-MX"] = ["[excitedly] ¡Bandera verde, ándale ándale!", "[excitedly] ¡Bandera verde! ¡Vámonos!", "[excitedly] ¡Verde, dale gas!", "[excitedly] ¡Bandera verde — dale, dale, dale!", "[excitedly] ¡Verde, pégale al acelerador!"],
		["fi-FI"] = ["[excitedly] Vihreä lippu, mene mene mene!", "[excitedly] Vihreä lippu! Mennään!", "[excitedly] Vihreä, kaasu pohjaan!", "[excitedly] Vihreä lippu — mene, mene, mene!", "[excitedly] Vihreä, paina kaasua!"],
		["fr-CA"] = ["[excitedly] Drapeau vert, allez allez allez!", "[excitedly] Drapeau vert! On y va!", "[excitedly] C'est vert, appuie!", "[excitedly] Drapeau vert — allez, allez, allez!", "[excitedly] C'est vert, gaz à fond!"],
		["fr-FR"] = ["[excitedly] Drapeau vert, allez allez allez!", "[excitedly] Drapeau vert! On y va!", "[excitedly] C'est vert, appuyez!", "[excitedly] Drapeau vert — allez, allez, allez!", "[excitedly] C'est vert, gaz à fond!"],
		["he-IL"] = ["[excitedly] דגל ירוק, לך לך לך!", "[excitedly] דגל ירוק! בואו נלך!", "[excitedly] ירוק, לחץ על הגז!", "[excitedly] דגל ירוק — לך, לך, לך!", "[excitedly] ירוק, תשים את הדריכה!"],
		["hu-HU"] = ["[excitedly] Zöld zászló, hajrá hajrá hajrá!", "[excitedly] Zöld zászló! Menjünk!", "[excitedly] Zöld, nyomj rá!", "[excitedly] Zöld zászló — hajrá, hajrá, hajrá!", "[excitedly] Zöld, gázt!"],
		["it-IT"] = ["[excitedly] Bandiera verde, via via via!", "[excitedly] Bandiera verde! Andiamo!", "[excitedly] Verde, vai a tutta!", "[excitedly] Bandiera verde — via, via, via!", "[excitedly] Verde, piede sull'acceleratore!"],
		["ja-JP"] = ["[excitedly] グリーンフラッグ、行け行け行け！", "[excitedly] グリーンフラッグ！行くぞ！", "[excitedly] グリーンだ、踏み込め！", "[excitedly] グリーンフラッグ — 行け、行け、行け！", "[excitedly] グリーン、アクセル全開！"],
		["nb-NO"] = ["[excitedly] Grønt flagg, kjør kjør kjør!", "[excitedly] Grønt flagg! La oss gå!", "[excitedly] Vi er grønne, tråkk til!", "[excitedly] Grønt flagg ute — kjør, kjør, kjør!", "[excitedly] Det er grønt, gass!"],
		["nl-NL"] = ["[excitedly] Groene vlag, rijd rijd rijd!", "[excitedly] Groene vlag! Laten we gaan!", "[excitedly] Groen, trap erop!", "[excitedly] Groene vlag — rijd, rijd, rijd!", "[excitedly] Het is groen, gas geven!"],
		["pl-PL"] = ["[excitedly] Zielona flaga, jedź jedź jedź!", "[excitedly] Zielona flaga! Jedziemy!", "[excitedly] Zielona, wciśnij gaz!", "[excitedly] Zielona flaga — jedź, jedź, jedź!", "[excitedly] Zielona, gaz do dechy!"],
		["pt-BR"] = ["[excitedly] Bandeira verde, vai vai vai!", "[excitedly] Bandeira verde! Vamos!", "[excitedly] Verde, acelera!", "[excitedly] Bandeira verde — vai, vai, vai!", "[excitedly] Verde, pisa fundo!"],
		["pt-PT"] = ["[excitedly] Bandeira verde, vai vai vai!", "[excitedly] Bandeira verde! Vamos lá!", "[excitedly] Verde, acelera!", "[excitedly] Bandeira verde — vai, vai, vai!", "[excitedly] Verde, pisa a fundo!"],
		["ro-RO"] = ["[excitedly] Steag verde, du-te du-te du-te!", "[excitedly] Steag verde! Hai să mergem!", "[excitedly] Verde, apasă pedala!", "[excitedly] Steag verde — du-te, du-te, du-te!", "[excitedly] Verde, gaz!"],
		["ru-RU"] = ["[excitedly] Зелёный флаг, газ газ газ!", "[excitedly] Зелёный флаг! Вперёд!", "[excitedly] Зелёный, жми!", "[excitedly] Зелёный флаг — газ, газ, газ!", "[excitedly] Зелёный, дави на газ!"],
		["sv-SE"] = ["[excitedly] Grönt flagg, kör kör kör!", "[excitedly] Grönt flagg! Låt oss åka!", "[excitedly] Vi är gröna, tryck på det!", "[excitedly] Grönt flagg ute — kör, kör, kör!", "[excitedly] Det är grönt, full gas!"],
		["th-TH"] = ["[excitedly] ธงเขียว ไปไปไป!", "[excitedly] ธงเขียว! ไปเลย!", "[excitedly] เขียวแล้ว เหยียบเลย!", "[excitedly] ธงเขียวออกแล้ว — ไป ไป ไป!", "[excitedly] เขียวแล้ว เหยียบคันเร่ง!"],
		["tr-TR"] = ["[excitedly] Yeşil bayrak, git git git!", "[excitedly] Yeşil bayrak! Haydi gidelim!", "[excitedly] Yeşil, bas gazı!", "[excitedly] Yeşil bayrak çıktı — git, git, git!", "[excitedly] Yeşil, gazı bas!"],
		["uk-UA"] = ["[excitedly] Зелений прапор, їдь їдь їдь!", "[excitedly] Зелений прапор! Вперед!", "[excitedly] Зелений, тисни!", "[excitedly] Зелений прапор — їдь, їдь, їдь!", "[excitedly] Зелений, жми на газ!"],
		["zh-Hans"] = ["[excitedly] 绿旗，冲冲冲！", "[excitedly] 绿旗！出发！", "[excitedly] 绿旗了，踩油门！", "[excitedly] 绿旗出来了 — 冲，冲，冲！", "[excitedly] 绿旗，踩下去！"],
	};

	// NOTE: SpotterFlagStartGo is intentionally merged into SpotterFlagGreen.

	private static Dictionary<string, string[]> BuildSpotterFlagBlue() => new()
	{
		["en-US"] = ["Blue flag — let them by.", "Blue flag, move over.", "You've got a blue flag — give way.", "Blue flag shown — let the leader past.", "Move aside, blue flag."],
		["cs-CZ"] = ["Modrá vlajka — uvolni jim cestu.", "Modrá vlajka, uhni.", "Máš modrou vlajku — dej jim přednost.", "Modrá vlajka — nech lídra projet.", "Ustup, modrá vlajka."],
		["da-DK"] = ["Blåt flag — lad dem passere.", "Blåt flag, flyt over.", "Du har et blåt flag — lad dem forbi.", "Blåt flag vist — lad lederen passere.", "Flyt til siden, blåt flag."],
		["de-DE"] = ["Blaue Flagge — lass sie vorbei.", "Blaue Flagge, rüberfahren.", "Du hast eine blaue Flagge — gib die Bahn frei.", "Blaue Flagge gezeigt — lass den Führenden vorbei.", "Platz machen, blaue Flagge."],
		["es-ES"] = ["Bandera azul — déjalos pasar.", "Bandera azul, apártate.", "Tienes bandera azul — cede el paso.", "Bandera azul mostrada — deja pasar al líder.", "Hazte a un lado, bandera azul."],
		["es-MX"] = ["Bandera azul — déjalos pasar.", "Bandera azul, échate a un lado.", "Tienes bandera azul — dale paso.", "Bandera azul — deja pasar al líder.", "Hazte a un lado, bandera azul."],
		["fi-FI"] = ["Sininen lippu — päästä heidät ohi.", "Sininen lippu, siirry sivuun.", "Sinulla on sininen lippu — väistä.", "Sininen lippu — päästä johtaja ohi.", "Siirry sivuun, sininen lippu."],
		["fr-CA"] = ["Drapeau bleu — laisse-les passer.", "Drapeau bleu, pousse-toi.", "T'as un drapeau bleu — cède le passage.", "Drapeau bleu montré — laisse passer le leader.", "Déplace-toi, drapeau bleu."],
		["fr-FR"] = ["Drapeau bleu — laissez-les passer.", "Drapeau bleu, dégagez.", "Vous avez un drapeau bleu — cédez le passage.", "Drapeau bleu montré — laissez passer le leader.", "Dégagez, drapeau bleu."],
		["he-IL"] = ["דגל כחול — תן להם לעבור.", "דגל כחול, הזז.", "יש לך דגל כחול — תן דרך.", "דגל כחול הוצג — תן למוביל לעבור.", "הזז הצידה, דגל כחול."],
		["hu-HU"] = ["Kék zászló — enged el őket.", "Kék zászló, húzz félre.", "Kék zászlót kaptál — adj utat.", "Kék zászló — enged el a vezetőt.", "Húzz félre, kék zászló."],
		["it-IT"] = ["Bandiera blu — lasciali passare.", "Bandiera blu, spostati.", "Hai la bandiera blu — cedi il passo.", "Bandiera blu mostrata — lascia passare il leader.", "Fatti da parte, bandiera blu."],
		["ja-JP"] = ["ブルーフラッグ — 道を空けろ。", "ブルーフラッグ、よけてください。", "ブルーフラッグが出た — 譲れ。", "ブルーフラッグ — トップを先に行かせろ。", "どいて、ブルーフラッグ。"],
		["nb-NO"] = ["Blått flagg — la dem passere.", "Blått flagg, flytt over.", "Du har et blått flagg — gi vei.", "Blått flagg vist — la lederen passere.", "Flytt til siden, blått flagg."],
		["nl-NL"] = ["Blauwe vlag — laat ze voorbij.", "Blauwe vlag, ga opzij.", "Blauwe vlag — geef ruimte.", "Blauwe vlag getoond — laat de leider voorbij.", "Opzij, blauwe vlag."],
		["pl-PL"] = ["Niebieska flaga — przepuść ich.", "Niebieska flaga, zjadź na bok.", "Masz niebieską flagę — ustąp drogi.", "Niebieska flaga — przepuść lidera.", "Zjedź na bok, niebieska flaga."],
		["pt-BR"] = ["Bandeira azul — deixe-os passar.", "Bandeira azul, mova-se.", "Você tem bandeira azul — dê passagem.", "Bandeira azul mostrada — deixe o líder passar.", "Abra caminho, bandeira azul."],
		["pt-PT"] = ["Bandeira azul — deixa-os passar.", "Bandeira azul, afasta-te.", "Tens bandeira azul — cede passagem.", "Bandeira azul mostrada — deixa o líder passar.", "Abre caminho, bandeira azul."],
		["ro-RO"] = ["Steag albastru — lasă-i să treacă.", "Steag albastru, dă-te la o parte.", "Ai steag albastru — cedează trecerea.", "Steag albastru arătat — lasă liderul să treacă.", "Dă-te la o parte, steag albastru."],
		["ru-RU"] = ["Синий флаг — пропусти их.", "Синий флаг, уступи дорогу.", "Синий флаг — дай пройти.", "Синий флаг — пропусти лидера.", "Уступай, синий флаг."],
		["sv-SE"] = ["Blått flagg — låt dem passera.", "Blått flagg, flytta på dig.", "Du har ett blått flagg — ge vika.", "Blått flagg visat — låt ledaren passera.", "Flytta på dig, blått flagg."],
		["th-TH"] = ["ธงน้ำเงิน — ให้เขาผ่านไป.", "ธงน้ำเงิน, ขยับออกไป.", "คุณมีธงน้ำเงิน — ให้ทาง.", "แสดงธงน้ำเงิน — ให้ผู้นำผ่านไป.", "ขยับออก, ธงน้ำเงิน."],
		["tr-TR"] = ["Mavi bayrak — geçmelerine izin ver.", "Mavi bayrak, çekil.", "Mavi bayrak aldın — yol ver.", "Mavi bayrak gösterildi — liderin geçmesine izin ver.", "Çekil, mavi bayrak."],
		["uk-UA"] = ["Синій прапор — пропусти їх.", "Синій прапор, поступися.", "Синій прапор — дай дорогу.", "Синій прапор — пропусти лідера.", "Поступися, синій прапор."],
		["zh-Hans"] = ["蓝旗 — 让他们过去。", "蓝旗，让开。", "蓝旗 — 让出位置。", "举蓝旗 — 让领先者通过。", "让开，蓝旗。"],
	};

	private static Dictionary<string, string[]> BuildSpotterFlagYellowWaving() => new()
	{
		["en-US"] = ["[urgently] Yellow flag — problem ahead, be careful!", "[urgently] Yellow flag waving, slow down!", "[urgently] Yellow flag out, there's trouble ahead!", "[urgently] Yellow flag — watch yourself up there!", "[urgently] Caution, yellow flag — stay alert!"],
		["cs-CZ"] = ["[urgently] Žlutá vlajka — problém vpředu, buď opatrný!", "[urgently] Žlutá vlajka mává, zpomal!", "[urgently] Žlutá vlajka, vpředu je problém!", "[urgently] Žlutá vlajka — dej tam pozor!", "[urgently] Pozor, žlutá vlajka — buď ostražitý!"],
		["da-DK"] = ["[urgently] Gult flag — problem forude, vær forsigtig!", "[urgently] Gult flag vinker, sæt farten ned!", "[urgently] Gult flag ude, der er problemer forude!", "[urgently] Gult flag — hold øje derude!", "[urgently] Forsigtig, gult flag — hold dig skarp!"],
		["de-DE"] = ["[urgently] Gelbe Flagge — Problem voraus, sei vorsichtig!", "[urgently] Gelbe Flagge geschwenkt, langsamer fahren!", "[urgently] Gelbe Flagge raus, es gibt Probleme voraus!", "[urgently] Gelbe Flagge — pass dort vorne auf!", "[urgently] Achtung, gelbe Flagge — bleib wachsam!"],
		["es-ES"] = ["[urgently] ¡Bandera amarilla — problema adelante, ten cuidado!", "[urgently] ¡Bandera amarilla ondeando, reduce la velocidad!", "[urgently] ¡Bandera amarilla, hay problemas adelante!", "[urgently] ¡Bandera amarilla — ten cuidado ahí delante!", "[urgently] ¡Precaución, bandera amarilla — mantente alerta!"],
		["es-MX"] = ["[urgently] ¡Bandera amarilla — hay un problema adelante, cuidado!", "[urgently] ¡Bandera amarilla ondeando, frena!", "[urgently] ¡Bandera amarilla, hay problemas adelante!", "[urgently] ¡Bandera amarilla — aguas ahí adelante!", "[urgently] ¡Precaución, bandera amarilla — mantente alerta!"],
		["fi-FI"] = ["[urgently] Keltainen lippu — ongelma edessä, ole varovainen!", "[urgently] Keltainen lippu heiluu, hidasta!", "[urgently] Keltainen lippu, edessä on ongelmia!", "[urgently] Keltainen lippu — varo siellä edessä!", "[urgently] Varoitus, keltainen lippu — pysy valppaana!"],
		["fr-CA"] = ["[urgently] Drapeau jaune — problème devant, sois prudent!", "[urgently] Drapeau jaune agité, ralentis!", "[urgently] Drapeau jaune sorti, il y a un problème devant!", "[urgently] Drapeau jaune — fais attention là-devant!", "[urgently] Attention, drapeau jaune — reste alerte!"],
		["fr-FR"] = ["[urgently] Drapeau jaune — problème devant, soyez prudent!", "[urgently] Drapeau jaune agité, ralentissez!", "[urgently] Drapeau jaune sorti, il y a un problème devant!", "[urgently] Drapeau jaune — faites attention là-devant!", "[urgently] Attention, drapeau jaune — restez alerte!"],
		["he-IL"] = ["[urgently] דגל צהוב — בעיה קדימה, היה זהיר!", "[urgently] דגל צהוב מניף, האט!", "[urgently] דגל צהוב, יש בעיה קדימה!", "[urgently] דגל צהוב — שים לב שם קדימה!", "[urgently] זהירות, דגל צהוב — הישאר ערני!"],
		["hu-HU"] = ["[urgently] Sárga zászló — probléma előtte, légy óvatos!", "[urgently] Sárga zászló lobog, lassíts!", "[urgently] Sárga zászló kint, probléma van előtte!", "[urgently] Sárga zászló — vigyázz ott előtte!", "[urgently] Figyelem, sárga zászló — maradj éber!"],
		["it-IT"] = ["[urgently] Bandiera gialla — problema avanti, stai attento!", "[urgently] Bandiera gialla sventolante, rallenta!", "[urgently] Bandiera gialla fuori, c'è un problema avanti!", "[urgently] Bandiera gialla — tieni d'occhio lì avanti!", "[urgently] Attenzione, bandiera gialla — rimani all'erta!"],
		["ja-JP"] = ["[urgently] イエローフラッグ — 前方にトラブル、注意！", "[urgently] イエローフラッグ振られてる、スピードダウン！", "[urgently] イエローフラッグ出た、前方にトラブルあり！", "[urgently] イエローフラッグ — 前方に気をつけろ！", "[urgently] 注意、イエローフラッグ — 油断するな！"],
		["nb-NO"] = ["[urgently] Gult flagg — problem foran, vær forsiktig!", "[urgently] Gult flagg vinker, senk farten!", "[urgently] Gult flagg ute, det er problemer foran!", "[urgently] Gult flagg — pass deg der fremme!", "[urgently] Forsiktig, gult flagg — hold deg skjerpet!"],
		["nl-NL"] = ["[urgently] Gele vlag — probleem voor je, wees voorzichtig!", "[urgently] Gele vlag zwaait, rem af!", "[urgently] Gele vlag buiten, er is een probleem voor je!", "[urgently] Gele vlag — let op daar voor je!", "[urgently] Opgelet, gele vlag — blijf alert!"],
		["pl-PL"] = ["[urgently] Żółta flaga — problem z przodu, ostrożnie!", "[urgently] Żółta flaga macha, zwolnij!", "[urgently] Żółta flaga, z przodu są problemy!", "[urgently] Żółta flaga — uważaj tam z przodu!", "[urgently] Uwaga, żółta flaga — bądź czujny!"],
		["pt-BR"] = ["[urgently] Bandeira amarela — problema à frente, cuidado!", "[urgently] Bandeira amarela balançando, reduza a velocidade!", "[urgently] Bandeira amarela, há problemas à frente!", "[urgently] Bandeira amarela — cuidado lá na frente!", "[urgently] Atenção, bandeira amarela — fique alerta!"],
		["pt-PT"] = ["[urgently] Bandeira amarela — problema à frente, cuidado!", "[urgently] Bandeira amarela a acenar, reduz!", "[urgently] Bandeira amarela fora, há problemas à frente!", "[urgently] Bandeira amarela — atenção lá à frente!", "[urgently] Atenção, bandeira amarela — mantém-te alerta!"],
		["ro-RO"] = ["[urgently] Steag galben — problemă înainte, fii atent!", "[urgently] Steag galben fluturând, încetinește!", "[urgently] Steag galben afară, sunt probleme înainte!", "[urgently] Steag galben — ai grijă acolo în față!", "[urgently] Atenție, steag galben — rămâi vigilent!"],
		["ru-RU"] = ["[urgently] Жёлтый флаг — впереди проблема, осторожно!", "[urgently] Жёлтый флаг машет, сбрось скорость!", "[urgently] Жёлтый флаг — впереди опасность!", "[urgently] Жёлтый флаг — смотри там впереди!", "[urgently] Осторожно, жёлтый флаг — будь настороже!"],
		["sv-SE"] = ["[urgently] Gult flagg — problem framåt, var försiktig!", "[urgently] Gult flagg viftar, sakta ner!", "[urgently] Gult flagg ute, det är problem framåt!", "[urgently] Gult flagg — se upp där framme!", "[urgently] Försiktig, gult flagg — håll dig skärpt!"],
		["th-TH"] = ["[urgently] ธงเหลือง — มีปัญหาข้างหน้า ระวังด้วย!", "[urgently] ธงเหลืองโบก, ชะลอความเร็ว!", "[urgently] ธงเหลืองออกแล้ว มีปัญหาข้างหน้า!", "[urgently] ธงเหลือง — ระวังข้างหน้าด้วย!", "[urgently] ระวัง, ธงเหลือง — ตื่นตัวไว้!"],
		["tr-TR"] = ["[urgently] Sarı bayrak — önde sorun var, dikkatli ol!", "[urgently] Sarı bayrak sallıyor, yavaşla!", "[urgently] Sarı bayrak çıktı, önde sorun var!", "[urgently] Sarı bayrak — orası dikkatli ol!", "[urgently] Dikkat, sarı bayrak — uyanık kal!"],
		["uk-UA"] = ["[urgently] Жовтий прапор — попереду проблема, будь обережний!", "[urgently] Жовтий прапор махає, зменш швидкість!", "[urgently] Жовтий прапор — попереду небезпека!", "[urgently] Жовтий прапор — стережись там попереду!", "[urgently] Обережно, жовтий прапор — будь напоготові!"],
		["zh-Hans"] = ["[urgently] 黄旗 — 前方有问题，小心！", "[urgently] 黄旗挥动，减速！", "[urgently] 黄旗出来了，前方有麻烦！", "[urgently] 黄旗 — 注意前方！", "[urgently] 注意，黄旗 — 保持警觉！"],
	};

	private static Dictionary<string, string[]> BuildSpotterFlagCautionWaving() => new()
	{
		["en-US"] = ["The caution flag is out!", "We've got a caution on track.", "Full course caution — slow down.", "Caution flag waving — hold your pace.", "Caution is out, back off."],
		["cs-CZ"] = ["Žlutá vlajka je venku!", "Máme žlutou vlajku na trati.", "Plná kurzová žlutá — zpomal.", "Žlutá vlajka mává — drž tempo.", "Žlutá je venku, ulevit."],
		["da-DK"] = ["Forsigtighedsflagget er ude!", "Vi har en forsigtighedstilstand på banen.", "Fuld bane-caution — sæt farten ned.", "Forsigtighedsflag vifter — hold tempoet.", "Caution er ude, træk dig tilbage."],
		["de-DE"] = ["Die Gelbphase ist aktiv!", "Wir haben ein Gelbphase auf der Strecke.", "Vollständige Gelbphase — langsamer fahren.", "Gelbphase — Tempo halten.", "Gelbphase aktiv, zurückhalten."],
		["es-ES"] = ["¡La bandera de precaución está fuera!", "Tenemos precaución en pista.", "Precaución de todo el circuito — reduce la velocidad.", "Bandera de precaución ondeando — mantén tu ritmo.", "La precaución está fuera, frena."],
		["es-MX"] = ["¡La bandera de precaución está afuera!", "Tenemos precaución en pista.", "Precaución en todo el circuito — reduce velocidad.", "Bandera de precaución ondeando — mantén tu ritmo.", "La precaución está afuera, frena."],
		["fi-FI"] = ["Varoituslippu on ulkona!", "Meillä on varoitus radalla.", "Koko radan varoitus — hidasta.", "Varoituslippu heiluu — pidä vauhti.", "Varoitus on ulkona, jätä tilaa."],
		["fr-CA"] = ["Le drapeau de caution est sorti!", "On a une caution sur la piste.", "Caution pleine course — ralentis.", "Drapeau de caution agité — maintiens ton rythme.", "La caution est sortie, recule."],
		["fr-FR"] = ["Le drapeau de précaution est sorti!", "Nous avons une précaution sur la piste.", "Précaution pleine course — ralentissez.", "Drapeau de précaution agité — maintenez votre rythme.", "La précaution est sortie, reculez."],
		["he-IL"] = ["דגל הזהירות בחוץ!", "יש לנו זהירות על המסלול.", "זהירות מסלול מלא — האט.", "דגל הזהירות מניף — שמור על הקצב.", "הזהירות בחוץ, הישאר מאחור."],
		["hu-HU"] = ["A sárga zászló kint van!", "Sárga zászló van a pályán.", "Teljes pályás sárga — lassíts.", "Sárga zászló lobog — tartsd a tempót.", "Sárga zászló kint, tarts távolságot."],
		["it-IT"] = ["La bandiera di cautela è fuori!", "Abbiamo una cautela in pista.", "Cautela su tutto il circuito — rallenta.", "Bandiera di cautela sventolante — mantieni il ritmo.", "La cautela è fuori, stai indietro."],
		["ja-JP"] = ["コーションフラッグが出た！", "コース上にコーション発令。", "フルコースコーション — スピードダウン。", "コーションフラッグ振られてる — ペース維持。", "コーション発令、離れろ。"],
		["nb-NO"] = ["Forsiktighetsflagget er ute!", "Vi har en forsiktighetstilstand på banen.", "Full bane-caution — senk farten.", "Forsiktighetsflagg vifter — hold tempoet.", "Caution er ute, trekk deg tilbake."],
		["nl-NL"] = ["De cautionvlag is buiten!", "Er is een caution op het circuit.", "Volledige course caution — rem af.", "Cautionvlag wappert — houd je tempo.", "Caution is buiten, geef ruimte."],
		["pl-PL"] = ["Flaga ostrożności jest na zewnątrz!", "Mamy ostrożność na torze.", "Pełna ostrożność toru — zwolnij.", "Flaga ostrożności macha — utrzymaj tempo.", "Ostrożność na zewnątrz, trzymaj dystans."],
		["pt-BR"] = ["A bandeira de cautela está fora!", "Temos cautela na pista.", "Cautela de pista toda — reduza a velocidade.", "Bandeira de cautela balançando — mantenha seu ritmo.", "A cautela está fora, recue."],
		["pt-PT"] = ["A bandeira de cautela está fora!", "Temos cautela na pista.", "Cautela em toda a pista — reduz.", "Bandeira de cautela a acenar — mantém o teu ritmo.", "A cautela está fora, recua."],
		["ro-RO"] = ["Steagul de precauție este afară!", "Avem precauție pe pistă.", "Precauție pe tot circuitul — încetinește.", "Steag de precauție fluturând — menține ritmul.", "Precauția este afară, trage-te înapoi."],
		["ru-RU"] = ["Жёлтый флаг предупреждения выставлен!", "На трассе режим осторожности.", "Полный кортеж — снизь скорость.", "Флаг предупреждения машет — держи темп.", "Предупреждение вышло, держись подальше."],
		["sv-SE"] = ["Försiktighetsflaggan är ute!", "Vi har ett cautionläge på banan.", "Full-bane caution — sakta ner.", "Försiktighetsflagg viftar — håll ditt tempo.", "Caution är ute, dra dig tillbaka."],
		["th-TH"] = ["ธงคอชั่นออกแล้ว!", "มีคอชั่นบนสนาม.", "คอชั่นทั้งสนาม — ชะลอลง.", "ธงคอชั่นโบก — รักษาความเร็ว.", "คอชั่นออกแล้ว, ถอยห่าง."],
		["tr-TR"] = ["İkaz bayrağı çıktı!", "Pistte ikaz var.", "Tam pist ikazı — yavaşla.", "İkaz bayrağı sallıyor — hızını koru.", "İkaz çıktı, geri çekil."],
		["uk-UA"] = ["Жовтий прапор попередження виставлено!", "На трасі режим обережності.", "Повне жовте — знизь швидкість.", "Прапор попередження махає — тримай темп.", "Попередження вийшло, тримайся далі."],
		["zh-Hans"] = ["注意旗出来了！", "赛道上出现注意旗。", "全程注意 — 减速。", "注意旗挥动 — 保持节奏。", "注意旗出了，退后一些。"],
	};

	private static Dictionary<string, string[]> BuildSpotterFlagDebris() => new()
	{
		["en-US"] = ["[urgently] Watch for debris on the road!", "[urgently] Debris on track — be careful!", "[urgently] There's debris ahead, stay alert!", "[urgently] Watch out, debris on the racing line!"],
		["cs-CZ"] = ["[urgently] Pozor na trosky na trati!", "[urgently] Trosky na trati — buď opatrný!", "[urgently] Vpředu jsou trosky, buď ostražitý!", "[urgently] Pozor, trosky na závodní linii!"],
		["da-DK"] = ["[urgently] Hold øje med affald på banen!", "[urgently] Affald på banen — vær forsigtig!", "[urgently] Der er affald forude, hold dig skarp!", "[urgently] Se op, affald på racerlinjen!"],
		["de-DE"] = ["[urgently] Achtung, Trümmer auf der Strecke!", "[urgently] Trümmer auf der Strecke — pass auf!", "[urgently] Trümmer voraus, bleib wachsam!", "[urgently] Vorsicht, Trümmer auf der Ideallinie!"],
		["es-ES"] = ["[urgently] ¡Cuidado con los escombros en la pista!", "[urgently] ¡Escombros en pista — ten cuidado!", "[urgently] ¡Hay escombros adelante, mantente alerta!", "[urgently] ¡Cuidado, escombros en la línea de carrera!"],
		["es-MX"] = ["[urgently] ¡Ojo con los escombros en la pista!", "[urgently] ¡Escombros en pista — cuidado!", "[urgently] ¡Hay escombros adelante, mantente alerta!", "[urgently] ¡Aguas, escombros en la línea de carrera!"],
		["fi-FI"] = ["[urgently] Varokaa trümmereita radalla!", "[urgently] Roskaa radalla — ole varovainen!", "[urgently] Edessä on roskaa, pysy valppaana!", "[urgently] Varoa, roskaa ajolinjalla!"],
		["fr-CA"] = ["[urgently] Attention aux débris sur la piste!", "[urgently] Débris sur la piste — sois prudent!", "[urgently] Il y a des débris devant, reste alerte!", "[urgently] Attention, débris sur la trajectoire de course!"],
		["fr-FR"] = ["[urgently] Attention aux débris sur la piste!", "[urgently] Débris sur la piste — soyez prudent!", "[urgently] Il y a des débris devant, restez alerte!", "[urgently] Attention, débris sur la trajectoire de course!"],
		["he-IL"] = ["[urgently] שים לב לפסולת על הכביש!", "[urgently] פסולת על המסלול — היה זהיר!", "[urgently] יש פסולת קדימה, הישאר ערני!", "[urgently] שים לב, פסולת על קו המרוץ!"],
		["hu-HU"] = ["[urgently] Figyelj a törmelékre a pályán!", "[urgently] Törmelék a pályán — légy óvatos!", "[urgently] Előtted törmelék van, maradj éber!", "[urgently] Vigyázz, törmelék az ideális vonalon!"],
		["it-IT"] = ["[urgently] Attenzione ai detriti in pista!", "[urgently] Detriti in pista — stai attento!", "[urgently] Ci sono detriti avanti, rimani all'erta!", "[urgently] Attenzione, detriti sulla traiettoria di gara!"],
		["ja-JP"] = ["[urgently] 路面にデブリがある、注意！", "[urgently] コースにデブリ — 気をつけろ！", "[urgently] 前方にデブリあり、油断するな！", "[urgently] 注意、レーシングラインにデブリ！"],
		["nb-NO"] = ["[urgently] Hold øye med rusk på banen!", "[urgently] Rusk på banen — vær forsiktig!", "[urgently] Det er rusk foran, hold deg skjerpet!", "[urgently] Se opp, rusk på racerlinjen!"],
		["nl-NL"] = ["[urgently] Let op puin op de baan!", "[urgently] Puin op het circuit — wees voorzichtig!", "[urgently] Er is puin voor je, blijf alert!", "[urgently] Pas op, puin op de racelijn!"],
		["pl-PL"] = ["[urgently] Uważaj na gruz na torze!", "[urgently] Gruz na torze — bądź ostrożny!", "[urgently] Z przodu jest gruz, bądź czujny!", "[urgently] Uwaga, gruz na linii wyścigu!"],
		["pt-BR"] = ["[urgently] Cuidado com destroços na pista!", "[urgently] Destroços na pista — tome cuidado!", "[urgently] Há destroços à frente, fique alerta!", "[urgently] Cuidado, destroços na linha de corrida!"],
		["pt-PT"] = ["[urgently] Atenção aos destroços na pista!", "[urgently] Destroços na pista — tem cuidado!", "[urgently] Há destroços à frente, mantém-te alerta!", "[urgently] Atenção, destroços na linha de corrida!"],
		["ro-RO"] = ["[urgently] Atenție la moloz pe pistă!", "[urgently] Moloz pe pistă — fii atent!", "[urgently] Sunt moloz înainte, rămâi vigilent!", "[urgently] Atenție, moloz pe linia de cursă!"],
		["ru-RU"] = ["[urgently] Осторожно, обломки на трассе!", "[urgently] Обломки на трассе — будь внимателен!", "[urgently] Впереди обломки, не расслабляйся!", "[urgently] Осторожно, обломки на гоночной линии!"],
		["sv-SE"] = ["[urgently] Se upp för skräp på banan!", "[urgently] Skräp på banan — var försiktig!", "[urgently] Det finns skräp framåt, håll dig skärpt!", "[urgently] Se upp, skräp på racinglinjen!"],
		["th-TH"] = ["[urgently] ระวังเศษซากบนถนน!", "[urgently] เศษซากบนสนาม — ระวังด้วย!", "[urgently] มีเศษซากข้างหน้า ตื่นตัวไว้!", "[urgently] ระวัง, เศษซากบนไลน์แข่ง!"],
		["tr-TR"] = ["[urgently] Yolda enkaz var, dikkat et!", "[urgently] Pistte enkaz — dikkatli ol!", "[urgently] İleride enkaz var, uyanık kal!", "[urgently] Dikkat, yarış hattında enkaz!"],
		["uk-UA"] = ["[urgently] Обережно, уламки на трасі!", "[urgently] Уламки на трасі — будь обережний!", "[urgently] Попереду уламки, не розслабляйся!", "[urgently] Обережно, уламки на гоночній лінії!"],
		["zh-Hans"] = ["[urgently] 注意赛道上有碎片！", "[urgently] 赛道有碎片 — 小心！", "[urgently] 前方有碎片，保持警觉！", "[urgently] 当心，赛道路线上有碎片！"],
	};

	private static Dictionary<string, string[]> BuildSpotterFlagWhite() => new()
	{
		["en-US"] = ["White flag — final lap!", "White flag is out, last lap!", "That's the white flag — one to go!", "White flag shown, make it count!"],
		["cs-CZ"] = ["Bílá vlajka — poslední kolo!", "Bílá vlajka je venku, poslední kolo!", "To je bílá vlajka — jedno kolo zbývá!", "Bílá vlajka ukázána, využij to!"],
		["da-DK"] = ["Hvidt flag — sidste omgang!", "Hvidt flag ude, sidste omgang!", "Det er det hvide flag — et omgang tilbage!", "Hvidt flag vist, udnyt det!"],
		["de-DE"] = ["Weiße Flagge — letzte Runde!", "Weiße Flagge raus, letzte Runde!", "Das ist die weiße Flagge — noch eine Runde!", "Weiße Flagge gezeigt, mach es zählen!"],
		["es-ES"] = ["¡Bandera blanca — última vuelta!", "¡Bandera blanca fuera, última vuelta!", "¡Es la bandera blanca — una vuelta más!", "¡Bandera blanca mostrada, que cuente!"],
		["es-MX"] = ["¡Bandera blanca — última vuelta!", "¡Bandera blanca afuera, última vuelta!", "¡Es la bandera blanca — una vuelta más!", "¡Bandera blanca, que cuente!"],
		["fi-FI"] = ["Valkoinen lippu — viimeinen kierros!", "Valkoinen lippu on ulkona, viimeinen kierros!", "Se on valkoinen lippu — yksi kierros jäljellä!", "Valkoinen lippu näytetty, tee siitä arvokas!"],
		["fr-CA"] = ["Drapeau blanc — dernier tour!", "Drapeau blanc sorti, dernier tour!", "C'est le drapeau blanc — un tour restant!", "Drapeau blanc montré, profites-en!"],
		["fr-FR"] = ["Drapeau blanc — dernier tour!", "Drapeau blanc sorti, dernier tour!", "C'est le drapeau blanc — un tour restant!", "Drapeau blanc montré, faites-en bon usage!"],
		["he-IL"] = ["דגל לבן — סיבוב אחרון!", "דגל לבן בחוץ, סיבוב אחרון!", "זה הדגל הלבן — עוד סיבוב אחד!", "דגל לבן הוצג, תנצל את זה!"],
		["hu-HU"] = ["Fehér zászló — utolsó kör!", "Fehér zászló kint, utolsó kör!", "Ez a fehér zászló — még egy kör!", "Fehér zászló mutatva, hajtsd meg!"],
		["it-IT"] = ["Bandiera bianca — ultimo giro!", "Bandiera bianca fuori, ultimo giro!", "Ecco la bandiera bianca — un giro ancora!", "Bandiera bianca mostrata, rendilo utile!"],
		["ja-JP"] = ["ホワイトフラッグ — 最終ラップ！", "ホワイトフラッグが出た、最終ラップ！", "ホワイトフラッグ — あと1周！", "ホワイトフラッグ、勝負を決めろ！"],
		["nb-NO"] = ["Hvitt flagg — siste runde!", "Hvitt flagg ute, siste runde!", "Det er det hvite flagget — en runde igjen!", "Hvitt flagg vist, gjør det gjeldende!"],
		["nl-NL"] = ["Witte vlag — laatste ronde!", "Witte vlag buiten, laatste ronde!", "Dat is de witte vlag — nog één ronde!", "Witte vlag getoond, maak het tellen!"],
		["pl-PL"] = ["Biała flaga — ostatnie okrążenie!", "Biała flaga na zewnątrz, ostatnie okrążenie!", "To biała flaga — jedno okrążenie zostało!", "Biała flaga pokazana, warto to wykorzystać!"],
		["pt-BR"] = ["Bandeira branca — última volta!", "Bandeira branca fora, última volta!", "É a bandeira branca — uma volta restante!", "Bandeira branca mostrada, faça valer!"],
		["pt-PT"] = ["Bandeira branca — última volta!", "Bandeira branca fora, última volta!", "É a bandeira branca — uma volta!", "Bandeira branca mostrada, aproveita!"],
		["ro-RO"] = ["Steag alb — ultimul tur!", "Steag alb afară, ultimul tur!", "Acesta este steagul alb — un tur rămas!", "Steag alb arătat, profită de el!"],
		["ru-RU"] = ["Белый флаг — последний круг!", "Белый флаг выставлен, последний круг!", "Это белый флаг — ещё один круг!", "Белый флаг, сделай всё возможное!"],
		["sv-SE"] = ["Vitt flagg — sista varvet!", "Vitt flagg ute, sista varvet!", "Det är det vita flagget — ett varv kvar!", "Vitt flagg visat, gör det värt!"],
		["th-TH"] = ["ธงขาว — รอบสุดท้าย!", "ธงขาวออกแล้ว, รอบสุดท้าย!", "นั่นคือธงขาว — อีกรอบเดียว!", "ธงขาวแสดงแล้ว, ทำให้มันคุ้มค่า!"],
		["tr-TR"] = ["Beyaz bayrak — son tur!", "Beyaz bayrak çıktı, son tur!", "Beyaz bayrak bu — bir tur kaldı!", "Beyaz bayrak gösterildi, değerini koy!"],
		["uk-UA"] = ["Білий прапор — останнє коло!", "Білий прапор виставлено, останнє коло!", "Це білий прапор — ще одне коло!", "Білий прапор, зроби все можливе!"],
		["zh-Hans"] = ["白旗 — 最后一圈！", "白旗出来了，最后一圈！", "白旗 — 还有一圈！", "白旗展示，全力以赴！"],
	};

	private static Dictionary<string, string[]> BuildSpotterFlagCheckered() => new()
	{
		["en-US"] = ["[excitedly] The checkered flag is out!", "[excitedly] Checkered flag — that's the finish!", "[excitedly] It's the checker, race is done!", "[excitedly] Checkered flag flies — race over!"],
		["cs-CZ"] = ["[excitedly] Šachovnicová vlajka je venku!", "[excitedly] Šachovnicová vlajka — to je cíl!", "[excitedly] Je to šachovnice, závod je u konce!", "[excitedly] Šachovnicová vlajka letí — závod skončil!"],
		["da-DK"] = ["[excitedly] Ternet flag er ude!", "[excitedly] Ternet flag — det er målstregen!", "[excitedly] Det er ternet flag, løbet er slut!", "[excitedly] Ternet flag flyver — løb slut!"],
		["de-DE"] = ["[excitedly] Die karierte Flagge ist draußen!", "[excitedly] Karierte Flagge — das ist das Ziel!", "[excitedly] Das Schachbrettflagge, Rennen vorbei!", "[excitedly] Karierte Flagge weht — Rennen beendet!"],
		["es-ES"] = ["[excitedly] ¡La bandera a cuadros está afuera!", "[excitedly] ¡Bandera a cuadros — esa es la meta!", "[excitedly] ¡Es el cuadros, la carrera ha terminado!", "[excitedly] ¡Bandera a cuadros vuela — carrera terminada!"],
		["es-MX"] = ["[excitedly] ¡La bandera a cuadros está afuera!", "[excitedly] ¡Bandera a cuadros — esa es la meta!", "[excitedly] ¡La cuadros, la carrera terminó!", "[excitedly] ¡Bandera a cuadros vuela — carrera terminada!"],
		["fi-FI"] = ["[excitedly] Ruudullinen lippu on ulkona!", "[excitedly] Ruudullinen lippu — se on maali!", "[excitedly] Se on ruudullinen, kilpailu on ohi!", "[excitedly] Ruudullinen lippu lentää — kilpailu ohi!"],
		["fr-CA"] = ["[excitedly] Le drapeau à damier est sorti!", "[excitedly] Drapeau à damier — c'est la fin!", "[excitedly] C'est le damier, la course est terminée!", "[excitedly] Drapeau à damier vole — course terminée!"],
		["fr-FR"] = ["[excitedly] Le drapeau à damier est sorti!", "[excitedly] Drapeau à damier — c'est la fin!", "[excitedly] C'est le damier, la course est terminée!", "[excitedly] Drapeau à damier vole — course terminée!"],
		["he-IL"] = ["[excitedly] דגל השחמט בחוץ!", "[excitedly] דגל השחמט — זה הסיום!", "[excitedly] זה השחמט, המרוץ נגמר!", "[excitedly] דגל השחמט מתנפנף — המרוץ הסתיים!"],
		["hu-HU"] = ["[excitedly] A kockás zászló kint van!", "[excitedly] Kockás zászló — ez a cél!", "[excitedly] Ez a kocka, a verseny véget ért!", "[excitedly] Kockás zászló lebeg — verseny vége!"],
		["it-IT"] = ["[excitedly] La bandiera a scacchi è fuori!", "[excitedly] Bandiera a scacchi — è il traguardo!", "[excitedly] È il traguardo, la gara è finita!", "[excitedly] Bandiera a scacchi vola — gara finita!"],
		["ja-JP"] = ["[excitedly] チェッカーフラッグが出た！", "[excitedly] チェッカーフラッグ — ゴールだ！", "[excitedly] チェッカーだ、レース終了！", "[excitedly] チェッカーフラッグが翻る — レース終わり！"],
		["nb-NO"] = ["[excitedly] Det rutete flagget er ute!", "[excitedly] Rutet flagg — det er mållinjen!", "[excitedly] Det er sjakkbrettet, løpet er over!", "[excitedly] Rutet flagg flyver — løp over!"],
		["nl-NL"] = ["[excitedly] De geblokte vlag is buiten!", "[excitedly] Geblokte vlag — dat is de finish!", "[excitedly] Het is de geblokte, race is klaar!", "[excitedly] Geblokte vlag vliegt — race voorbij!"],
		["pl-PL"] = ["[excitedly] Flaga w kratę jest na zewnątrz!", "[excitedly] Flaga w kratę — to meta!", "[excitedly] To flaga w kratę, wyścig skończony!", "[excitedly] Flaga w kratę leci — wyścig zakończony!"],
		["pt-BR"] = ["[excitedly] A bandeira quadriculada está fora!", "[excitedly] Bandeira quadriculada — essa é a chegada!", "[excitedly] É o xadrez, a corrida acabou!", "[excitedly] Bandeira quadriculada voa — corrida encerrada!"],
		["pt-PT"] = ["[excitedly] A bandeira xadrez está fora!", "[excitedly] Bandeira xadrez — é a meta!", "[excitedly] É o xadrez, a corrida acabou!", "[excitedly] Bandeira xadrez a voar — corrida terminada!"],
		["ro-RO"] = ["[excitedly] Steagul alb-negru este afară!", "[excitedly] Steag alb-negru — asta e finalul!", "[excitedly] E steagul, cursa s-a terminat!", "[excitedly] Steagul alb-negru zboară — cursă terminată!"],
		["ru-RU"] = ["[excitedly] Клетчатый флаг выставлен!", "[excitedly] Клетчатый флаг — это финиш!", "[excitedly] Клетчатый, гонка завершена!", "[excitedly] Клетчатый флаг реет — гонка окончена!"],
		["sv-SE"] = ["[excitedly] Det rutiga flagget är ute!", "[excitedly] Rutigt flagg — det är mållinjen!", "[excitedly] Det är schackbrädet, loppet är klart!", "[excitedly] Rutigt flagg flyger — lopp avslutat!"],
		["th-TH"] = ["[excitedly] ธงตาหมากรุกออกแล้ว!", "[excitedly] ธงตาหมากรุก — นั่นคือเส้นชัย!", "[excitedly] ตาหมากรุกออกแล้ว, แข่งเสร็จแล้ว!", "[excitedly] ธงตาหมากรุกโบก — จบการแข่งขัน!"],
		["tr-TR"] = ["[excitedly] Damalı bayrak çıktı!", "[excitedly] Damalı bayrak — bitiş!", "[excitedly] Damalı bayrak, yarış bitti!", "[excitedly] Damalı bayrak uçuyor — yarış sona erdi!"],
		["uk-UA"] = ["[excitedly] Клітчастий прапор виставлено!", "[excitedly] Клітчастий прапор — це фініш!", "[excitedly] Клітчастий, гонка завершена!", "[excitedly] Клітчастий прапор майорить — гонка закінчена!"],
		["zh-Hans"] = ["[excitedly] 方格旗出来了！", "[excitedly] 方格旗 — 终点到了！", "[excitedly] 方格旗，比赛结束！", "[excitedly] 方格旗飞扬 — 比赛结束！"],
	};

	private static Dictionary<string, string[]> BuildSpotterFlagBlack() => new()
	{
		["en-US"] = ["[seriously] You've got a black flag — come in!", "[seriously] Black flag shown, bring it in.", "[seriously] Black flag — head to the pits now.", "[seriously] They're showing you the black flag, box this lap."],
		["cs-CZ"] = ["[seriously] Máš černou vlajku — přijeď do boxů!", "[seriously] Černá vlajka ukázána, jeď do boxů.", "[seriously] Černá vlajka — ihned do boxů.", "[seriously] Ukazují ti černou vlajku, zaparkuj toto kolo."],
		["da-DK"] = ["[seriously] Du har et sort flag — kom ind!", "[seriously] Sort flag vist, kør ind.", "[seriously] Sort flag — gå til pit nu.", "[seriously] De viser dig det sorte flag, kør ind denne omgang."],
		["de-DE"] = ["[seriously] Du hast die schwarze Flagge — komm rein!", "[seriously] Schwarze Flagge gezeigt, fahr rein.", "[seriously] Schwarze Flagge — jetzt in die Box.", "[seriously] Sie zeigen dir die schwarze Flagge, box diese Runde."],
		["es-ES"] = ["[seriously] ¡Tienes bandera negra — entra!", "[seriously] Bandera negra mostrada, entra al pit.", "[seriously] Bandera negra — ve al pit ahora.", "[seriously] Te están mostrando la bandera negra, entra esta vuelta."],
		["es-MX"] = ["[seriously] ¡Tienes bandera negra — entra!", "[seriously] Bandera negra mostrada, entra al pit.", "[seriously] Bandera negra — ve al pit ahora.", "[seriously] Te están mostrando la bandera negra, entra esta vuelta."],
		["fi-FI"] = ["[seriously] Sinulla on musta lippu — tule sisään!", "[seriously] Musta lippu näytetty, aja sisään.", "[seriously] Musta lippu — mene varikolle nyt.", "[seriously] He näyttävät sinulle mustaa lippua, tule varikolle tällä kierroksella."],
		["fr-CA"] = ["[seriously] T'as un drapeau noir — rentre!", "[seriously] Drapeau noir montré, rentre.", "[seriously] Drapeau noir — direction les stands maintenant.", "[seriously] Ils te montrent le drapeau noir, rentre ce tour."],
		["fr-FR"] = ["[seriously] Vous avez un drapeau noir — rentrez!", "[seriously] Drapeau noir montré, rentrez.", "[seriously] Drapeau noir — direction les stands maintenant.", "[seriously] Ils vous montrent le drapeau noir, rentrez ce tour."],
		["he-IL"] = ["[seriously] יש לך דגל שחור — כנס!", "[seriously] דגל שחור הוצג, כנס לפיטים.", "[seriously] דגל שחור — לך לפיטים עכשיו.", "[seriously] הם מראים לך דגל שחור, כנס בסיבוב הזה."],
		["hu-HU"] = ["[seriously] Fekete zászlód van — gyere be!", "[seriously] Fekete zászló mutatva, gyere be.", "[seriously] Fekete zászló — most a boxba.", "[seriously] Fekete zászlót mutatnak, jöjj be ebben a körben."],
		["it-IT"] = ["[seriously] Hai la bandiera nera — vieni ai box!", "[seriously] Bandiera nera mostrata, rientra.", "[seriously] Bandiera nera — vai ai box ora.", "[seriously] Ti stanno mostrando la bandiera nera, rientra questo giro."],
		["ja-JP"] = ["[seriously] ブラックフラッグが出た — ピットに戻れ！", "[seriously] ブラックフラッグ、ピットに入れ。", "[seriously] ブラックフラッグ — 今すぐピットへ。", "[seriously] ブラックフラッグが提示されている、今周ボックスに入れ。"],
		["nb-NO"] = ["[seriously] Du har et svart flagg — kom inn!", "[seriously] Svart flagg vist, kjør inn.", "[seriously] Svart flagg — gå til pit nå.", "[seriously] De viser deg det svarte flagget, kjør inn denne runden."],
		["nl-NL"] = ["[seriously] Je hebt een zwarte vlag — kom binnen!", "[seriously] Zwarte vlag getoond, kom binnen.", "[seriously] Zwarte vlag — naar de pit nu.", "[seriously] Ze tonen je de zwarte vlag, kom deze ronde binnen."],
		["pl-PL"] = ["[seriously] Masz czarną flagę — wracaj!", "[seriously] Czarna flaga pokazana, wracaj do boksów.", "[seriously] Czarna flaga — do boksów teraz.", "[seriously] Pokazują ci czarną flagę, wejdź w to okrążenie."],
		["pt-BR"] = ["[seriously] Você tem bandeira preta — entre!", "[seriously] Bandeira preta mostrada, venha para o pit.", "[seriously] Bandeira preta — vá ao pit agora.", "[seriously] Estão mostrando a bandeira preta, entre nesta volta."],
		["pt-PT"] = ["[seriously] Tens bandeira preta — vem para os boxes!", "[seriously] Bandeira preta mostrada, vem para os boxes.", "[seriously] Bandeira preta — vai aos boxes agora.", "[seriously] Estão a mostrar-te a bandeira preta, entra nesta volta."],
		["ro-RO"] = ["[seriously] Ai steag negru — vino înăuntru!", "[seriously] Steag negru arătat, intră în pit.", "[seriously] Steag negru — mergi la pit acum.", "[seriously] Îți arată steagul negru, intră în tur acesta."],
		["ru-RU"] = ["[seriously] Тебе показали чёрный флаг — заезжай!", "[seriously] Чёрный флаг, заезжай в боксы.", "[seriously] Чёрный флаг — немедленно в пит.", "[seriously] Тебе показывают чёрный флаг, заедь в эту гонку."],
		["sv-SE"] = ["[seriously] Du har ett svart flagg — kom in!", "[seriously] Svart flagg visat, kör in.", "[seriously] Svart flagg — åk till pit nu.", "[seriously] De visar dig det svarta flagget, box detta varvet."],
		["th-TH"] = ["[seriously] คุณมีธงดำ — เข้ามาเลย!", "[seriously] แสดงธงดำแล้ว, เข้าพิต.", "[seriously] ธงดำ — เข้าพิตตอนนี้.", "[seriously] พวกเขาแสดงธงดำให้คุณ, เข้าพิตรอบนี้."],
		["tr-TR"] = ["[seriously] Siyah bayrak aldın — içeri gel!", "[seriously] Siyah bayrak gösterildi, içeri gir.", "[seriously] Siyah bayrak — şimdi pite git.", "[seriously] Sana siyah bayrak gösteriyorlar, bu turda box yap."],
		["uk-UA"] = ["[seriously] Тобі показали чорний прапор — заїжджай!", "[seriously] Чорний прапор, заїжджай у бокси.", "[seriously] Чорний прапор — негайно на піт.", "[seriously] Тобі показують чорний прапор, заїдь у цьому колі."],
		["zh-Hans"] = ["[seriously] 你被举黑旗 — 进站！", "[seriously] 黑旗展示，进入维修区。", "[seriously] 黑旗 — 现在进维修区。", "[seriously] 他们在向你展示黑旗，本圈进站。"],
	};

	private static Dictionary<string, string[]> BuildSpotterFlagDisqualify() => new()
	{
		["en-US"] = ["[seriously] You've been disqualified — come back to the pits.", "[seriously] Disqualification flag — return to pit lane.", "[seriously] You've been DQ'd, bring the car in.", "[seriously] Black flag with cross — you're disqualified, pit now."],
		["cs-CZ"] = ["[seriously] Byl jsi diskvalifikován — vrať se do boxů.", "[seriously] Vlajka diskvalifikace — vrať se do pit lane.", "[seriously] Byl jsi DQ, přiveď auto.", "[seriously] Černá vlajka s křížem — jsi diskvalifikován, do boxů."],
		["da-DK"] = ["[seriously] Du er blevet diskvalificeret — kom tilbage til pit.", "[seriously] Diskvalifikationsflag — vend tilbage til pit lane.", "[seriously] Du er DQ'd, tag bilen ind.", "[seriously] Sort flag med kryds — du er diskvalificeret, pit nu."],
		["de-DE"] = ["[seriously] Du wurdest disqualifiziert — kehre in die Boxen zurück.", "[seriously] Disqualifikationsflagge — kehre in die Boxengasse zurück.", "[seriously] Du wurdest disqualifiziert, fahr das Auto rein.", "[seriously] Schwarze Flagge mit Kreuz — du bist disqualifiziert, Box jetzt."],
		["es-ES"] = ["[seriously] Has sido descalificado — vuelve a los pits.", "[seriously] Bandera de descalificación — regresa al pit lane.", "[seriously] Estás descalificado, lleva el coche al pit.", "[seriously] Bandera negra con cruz — estás descalificado, entra ahora."],
		["es-MX"] = ["[seriously] Fuiste descalificado — regresa a los pits.", "[seriously] Bandera de descalificación — regresa al pit lane.", "[seriously] Quedaste descalificado, lleva el coche.", "[seriously] Bandera negra con cruz — estás descalificado, entra ahora."],
		["fi-FI"] = ["[seriously] Sinut on hylätty — tule takaisin varikolle.", "[seriously] Hylkäyslippu — palaa pit lanelle.", "[seriously] Sinut on hylätty, tuo auto sisään.", "[seriously] Musta lippu ristillä — sinut on hylätty, varikolle nyt."],
		["fr-CA"] = ["[seriously] T'as été disqualifié — reviens aux stands.", "[seriously] Drapeau de disqualification — retourne au pit lane.", "[seriously] T'as été disqualifié, ramène la voiture.", "[seriously] Drapeau noir avec croix — t'es disqualifié, pit maintenant."],
		["fr-FR"] = ["[seriously] Vous avez été disqualifié — revenez aux stands.", "[seriously] Drapeau de disqualification — retournez au pit lane.", "[seriously] Vous avez été disqualifié, ramenez la voiture.", "[seriously] Drapeau noir avec croix — vous êtes disqualifié, pit maintenant."],
		["he-IL"] = ["[seriously] פסלת — חזור לפיטים.", "[seriously] דגל פסילה — חזור לנתיב הפיטים.", "[seriously] אתה DQ, הבא את המכונית.", "[seriously] דגל שחור עם צלב — אתה פסול, לפיטים עכשיו."],
		["hu-HU"] = ["[seriously] Kizártak — gyere vissza a boxba.", "[seriously] Kizárás zászló — térj vissza a boxutcába.", "[seriously] Kizártak, hozd be az autót.", "[seriously] Fekete zászló kereszttel — ki vagy zárva, boxba most."],
		["it-IT"] = ["[seriously] Sei stato squalificato — torna ai box.", "[seriously] Bandiera di squalifica — torna alla pit lane.", "[seriously] Sei stato squalificato, porta la macchina ai box.", "[seriously] Bandiera nera con croce — sei squalificato, pit ora."],
		["ja-JP"] = ["[seriously] 失格になった — ピットに戻れ。", "[seriously] 失格フラッグ — ピットレーンに戻れ。", "[seriously] 失格だ、車を戻せ。", "[seriously] 黒十字旗 — 失格、今すぐピットへ。"],
		["nb-NO"] = ["[seriously] Du er blitt diskvalifisert — kom tilbake til pit.", "[seriously] Diskvalifiseringsflagg — returner til pit lane.", "[seriously] Du er DQ'd, ta bilen inn.", "[seriously] Svart flagg med kryss — du er diskvalifisert, pit nå."],
		["nl-NL"] = ["[seriously] Je bent gediskwalificeerd — kom terug naar de pit.", "[seriously] Diskwalificatievlag — keer terug naar de pit lane.", "[seriously] Je bent gediskwalificeerd, breng de auto binnen.", "[seriously] Zwarte vlag met kruis — je bent gediskwalificeerd, pit nu."],
		["pl-PL"] = ["[seriously] Zostałeś zdyskwalifikowany — wróć do boksów.", "[seriously] Flaga dyskwalifikacji — wróć do pit lane.", "[seriously] Zostałeś DQ, przyjedź bolidem.", "[seriously] Czarna flaga z krzyżem — jesteś zdyskwalifikowany, do boksów."],
		["pt-BR"] = ["[seriously] Você foi desclassificado — volte para o pit.", "[seriously] Bandeira de desclassificação — retorne ao pit lane.", "[seriously] Você foi DQ, traga o carro.", "[seriously] Bandeira preta com cruz — você está desclassificado, pit agora."],
		["pt-PT"] = ["[seriously] Foste desqualificado — volta aos boxes.", "[seriously] Bandeira de desqualificação — regressa ao pit lane.", "[seriously] Foste desqualificado, traz o carro.", "[seriously] Bandeira preta com cruz — estás desqualificado, pit agora."],
		["ro-RO"] = ["[seriously] Ai fost descalificat — întoarce-te la pit.", "[seriously] Steag de descalificare — întoarce-te la pit lane.", "[seriously] Ai fost DQ, adu mașina înăuntru.", "[seriously] Steag negru cu cruce — ești descalificat, pit acum."],
		["ru-RU"] = ["[seriously] Ты дисквалифицирован — возвращайся в боксы.", "[seriously] Флаг дисквалификации — вернись на пит-лейн.", "[seriously] Тебя дисквалифицировали, заедь в боксы.", "[seriously] Чёрный флаг с крестом — ты дисквалифицирован, в пит сейчас."],
		["sv-SE"] = ["[seriously] Du är diskvalificerad — kom tillbaka till pit.", "[seriously] Diskvalificeringsflagga — återvänd till pit lane.", "[seriously] Du är diskvalificerad, ta in bilen.", "[seriously] Svart flagga med kors — du är diskvalificerad, pit nu."],
		["th-TH"] = ["[seriously] คุณถูกตัดสิทธิ์ — กลับไปที่พิต.", "[seriously] ธงตัดสิทธิ์ — กลับไปยังพิตเลน.", "[seriously] คุณถูก DQ แล้ว, เอารถเข้ามา.", "[seriously] ธงดำมีกากบาท — คุณถูกตัดสิทธิ์, เข้าพิตตอนนี้."],
		["tr-TR"] = ["[seriously] Diskalifiye edildin — pit'e geri dön.", "[seriously] Diskalifiye bayrağı — pit lane'e geri dön.", "[seriously] Diskalifiye oldun, arabayı getir.", "[seriously] Çarpı işaretli siyah bayrak — diskalifiye oldun, pit yap."],
		["uk-UA"] = ["[seriously] Тебе дискваліфікували — повертайся в бокси.", "[seriously] Прапор дискваліфікації — повертайся на піт-лейн.", "[seriously] Тебе дискваліфікували, заїжджай.", "[seriously] Чорний прапор з хрестом — ти дискваліфікований, в піт зараз."],
		["zh-Hans"] = ["[seriously] 你被取消资格了 — 回到维修区。", "[seriously] 取消资格旗 — 返回维修区通道。", "[seriously] 你被DQ了，把车开进来。", "[seriously] 带叉黑旗 — 你被取消资格了，现在进站。"],
	};

	private static Dictionary<string, string[]> BuildSpotterFlagRepair() => new()
	{
		["en-US"] = ["[seriously] Meatball flag — bring your car in for repairs.", "[seriously] You've got the meatball flag, pit for repairs.", "[seriously] Repair flag shown — your car needs attention in the pits.", "[seriously] Meatball flag out — pit this lap for repairs."],
		["cs-CZ"] = ["[seriously] Vlajka meatball — přiveď auto do boxů na opravy.", "[seriously] Máš vlajku meatball, do boxů na opravy.", "[seriously] Opravná vlajka ukázána — tvoje auto potřebuje péči v boxech.", "[seriously] Vlajka meatball venku — zastav toto kolo na opravy."],
		["da-DK"] = ["[seriously] Kødfrikadelleflag — tag din bil ind til reparationer.", "[seriously] Du har kødfrikadelleflagget, pit til reparationer.", "[seriously] Reparationsflag vist — din bil trænger til opmærksomhed i pit.", "[seriously] Kødfrikadelleflag ude — pit denne omgang til reparationer."],
		["de-DE"] = ["[seriously] Meatball-Flagge — bring dein Auto zur Reparatur in die Boxen.", "[seriously] Du hast die Meatball-Flagge, Box für Reparaturen.", "[seriously] Reparaturflagge gezeigt — dein Auto braucht Aufmerksamkeit in den Boxen.", "[seriously] Meatball-Flagge raus — diese Runde für Reparaturen boxen."],
		["es-ES"] = ["[seriously] Bandera meatball — lleva tu auto al pit para reparaciones.", "[seriously] Tienes la bandera meatball, pit para reparaciones.", "[seriously] Bandera de reparación mostrada — tu auto necesita atención en el pit.", "[seriously] Bandera meatball fuera — entra esta vuelta para reparaciones."],
		["es-MX"] = ["[seriously] Bandera meatball — lleva tu coche al pit para reparaciones.", "[seriously] Tienes la bandera meatball, pit para reparaciones.", "[seriously] Bandera de reparación mostrada — tu coche necesita atención en el pit.", "[seriously] Bandera meatball afuera — entra esta vuelta para reparaciones."],
		["fi-FI"] = ["[seriously] Meatball-lippu — tuo autosi varikolle korjauksia varten.", "[seriously] Sinulla on meatball-lippu, varikolle korjauksia varten.", "[seriously] Korjauslippu näytetty — autosi tarvitsee huomiota varikoilla.", "[seriously] Meatball-lippu ulkona — tule varikolle tällä kierroksella korjauksia varten."],
		["fr-CA"] = ["[seriously] Drapeau meatball — ramène ta voiture au stand pour réparations.", "[seriously] T'as le drapeau meatball, pit pour réparations.", "[seriously] Drapeau de réparation montré — ta voiture a besoin d'attention au stand.", "[seriously] Drapeau meatball sorti — pit ce tour pour réparations."],
		["fr-FR"] = ["[seriously] Drapeau meatball — ramenez votre voiture au stand pour réparations.", "[seriously] Vous avez le drapeau meatball, pit pour réparations.", "[seriously] Drapeau de réparation montré — votre voiture a besoin d'attention au stand.", "[seriously] Drapeau meatball sorti — pit ce tour pour réparations."],
		["he-IL"] = ["[seriously] דגל מיטבול — הבא את מכוניתך לפיט לתיקונים.", "[seriously] יש לך דגל מיטבול, לפיט לתיקונים.", "[seriously] דגל תיקון הוצג — מכוניתך זקוקה לתשומת לב בפיטים.", "[seriously] דגל מיטבול בחוץ — כנס בסיבוב הזה לתיקונים."],
		["hu-HU"] = ["[seriously] Meatball-zászló — hozd be az autódat javításra.", "[seriously] Kaptad a meatball-zászlót, boxba javításra.", "[seriously] Javítási zászló mutatva — az autód figyelmet igényel a boxban.", "[seriously] Meatball-zászló kint — javítás miatt boxba ebben a körben."],
		["it-IT"] = ["[seriously] Bandiera meatball — porta la tua macchina ai box per riparazioni.", "[seriously] Hai la bandiera meatball, pit per riparazioni.", "[seriously] Bandiera di riparazione mostrata — la tua macchina ha bisogno di attenzione ai box.", "[seriously] Bandiera meatball fuori — pit questo giro per riparazioni."],
		["ja-JP"] = ["[seriously] ミートボールフラッグ — 修理のためにピットに戻れ。", "[seriously] ミートボールフラッグが出た、修理のためピットへ。", "[seriously] 修理フラッグ提示 — ピットで車のメンテが必要。", "[seriously] ミートボールフラッグ — 今周修理のためピットイン。"],
		["nb-NO"] = ["[seriously] Kjøttbolleflagg — ta bilen inn for reparasjoner.", "[seriously] Du har kjøttbolleflagget, pit for reparasjoner.", "[seriously] Reparasjonsflagg vist — bilen din trenger oppmerksomhet i pit.", "[seriously] Kjøttbolleflagg ute — pit denne runden for reparasjoner."],
		["nl-NL"] = ["[seriously] Gehaktbalflag — breng je auto naar de pit voor reparaties.", "[seriously] Je hebt de gehaktbalvlag, pit voor reparaties.", "[seriously] Reparatievlag getoond — je auto heeft aandacht nodig in de pit.", "[seriously] Gehaktbalvlag buiten — pit deze ronde voor reparaties."],
		["pl-PL"] = ["[seriously] Flaga meatball — przywieź auto do boksów na naprawy.", "[seriously] Masz flagę meatball, do boksów na naprawy.", "[seriously] Flaga naprawy pokazana — twój samochód wymaga uwagi w boksach.", "[seriously] Flaga meatball na zewnątrz — wejdź w to okrążenie na naprawy."],
		["pt-BR"] = ["[seriously] Bandeira meatball — traga seu carro ao pit para reparos.", "[seriously] Você tem a bandeira meatball, pit para reparos.", "[seriously] Bandeira de reparo mostrada — seu carro precisa de atenção no pit.", "[seriously] Bandeira meatball fora — pit nesta volta para reparos."],
		["pt-PT"] = ["[seriously] Bandeira meatball — traz o teu carro aos boxes para reparações.", "[seriously] Tens a bandeira meatball, pit para reparações.", "[seriously] Bandeira de reparação mostrada — o teu carro precisa de atenção nos boxes.", "[seriously] Bandeira meatball fora — pit nesta volta para reparações."],
		["ro-RO"] = ["[seriously] Steag meatball — adu mașina la pit pentru reparații.", "[seriously] Ai steagul meatball, pit pentru reparații.", "[seriously] Steag de reparație arătat — mașina ta are nevoie de atenție la pit.", "[seriously] Steag meatball afară — pit în turul acesta pentru reparații."],
		["ru-RU"] = ["[seriously] Флаг «колобок» — заедь в боксы для ремонта.", "[seriously] Тебе показывают флаг «колобок», заедь на ремонт.", "[seriously] Флаг ремонта — твоя машина нуждается в обслуживании в боксах.", "[seriously] Флаг «колобок» выставлен — заедь в эту гонку на ремонт."],
		["sv-SE"] = ["[seriously] Köttbullsflagga — ta in din bil för reparationer.", "[seriously] Du har köttbullsflaggan, pit för reparationer.", "[seriously] Reparationsflagga visad — din bil behöver uppmärksamhet i pit.", "[seriously] Köttbullsflagga ute — pit detta varvet för reparationer."],
		["th-TH"] = ["[seriously] ธงมีตบอล — นำรถเข้าพิตเพื่อซ่อมแซม.", "[seriously] คุณมีธงมีตบอล, เข้าพิตเพื่อซ่อมแซม.", "[seriously] แสดงธงซ่อมแซม — รถของคุณต้องการการดูแลในพิต.", "[seriously] ธงมีตบอลออกแล้ว — เข้าพิตรอบนี้เพื่อซ่อมแซม."],
		["tr-TR"] = ["[seriously] Meatball bayrağı — arabayı tamir için pit'e getir.", "[seriously] Meatball bayrağı aldın, tamir için pit yap.", "[seriously] Tamir bayrağı gösterildi — araban pit'te ilgi gerektiriyor.", "[seriously] Meatball bayrağı çıktı — bu turda tamir için pit yap."],
		["uk-UA"] = ["[seriously] Прапор «фрикадельки» — заїжджай у бокси для ремонту.", "[seriously] Тобі показують прапор «фрикадельки», заїдь на ремонт.", "[seriously] Прапор ремонту — твій автомобіль потребує обслуговування в боксах.", "[seriously] Прапор «фрикадельки» виставлено — заїдь у цьому колі на ремонт."],
		["zh-Hans"] = ["[seriously] 肉丸旗 — 把车开进维修区修理。", "[seriously] 你收到了肉丸旗，进站修理。", "[seriously] 维修旗展示 — 你的车需要在维修区维护。", "[seriously] 肉丸旗出来了 — 本圈进站维修。"],
	};

	private static Dictionary<string, string[]> BuildSpotterFlagStartReady() => new()
	{
		["en-US"] = ["Get ready!", "Stand by for the start.", "Prepare yourself — start is coming!", "Almost time — get ready to go!"],
		["cs-CZ"] = ["Připrav se!", "Stůj v pohotovosti ke startu.", "Připrav se — start se blíží!", "Skoro čas — připrav se k jízdě!"],
		["da-DK"] = ["Gør dig klar!", "Stå klar til start.", "Forbered dig — start er på vej!", "Næsten tid — gør dig klar til at køre!"],
		["de-DE"] = ["Mach dich bereit!", "Bereit zum Start.", "Mach dich bereit — der Start kommt!", "Fast Zeit — mach dich bereit loszufahren!"],
		["es-ES"] = ["¡Prepárate!", "En espera para la salida.", "¡Prepárate — la salida está llegando!", "¡Casi la hora — prepárate para ir!"],
		["es-MX"] = ["¡Prepárate!", "En espera para la salida.", "¡Prepárate — ya casi la salida!", "¡Ya merito — prepárate para salir!"],
		["fi-FI"] = ["Valmistaudu!", "Valmiudessa starttia varten.", "Valmistaudu — startti on tulossa!", "Melkein aika — valmistaudu lähtemään!"],
		["fr-CA"] = ["Prépare-toi!", "En attente pour le départ.", "Prépare-toi — le départ arrive!", "C'est presque l'heure — prépare-toi à partir!"],
		["fr-FR"] = ["Préparez-vous!", "En attente pour le départ.", "Préparez-vous — le départ arrive!", "C'est presque l'heure — préparez-vous à partir!"],
		["he-IL"] = ["התכונן!", "המתן לזינוק.", "התכונן — הזינוק מגיע!", "כמעט הזמן — התכונן לצאת!"],
		["hu-HU"] = ["Készülj!", "Készen állj a rajtra.", "Készülj — a rajt közeledik!", "Már majdnem — készülj az indulásra!"],
		["it-IT"] = ["Preparati!", "In attesa della partenza.", "Preparati — la partenza sta arrivando!", "Quasi tempo — preparati a partire!"],
		["ja-JP"] = ["準備して！", "スタートに備えてください。", "準備して — スタートが来るぞ！", "もうすぐ — 出発の準備をしろ！"],
		["nb-NO"] = ["Gjør deg klar!", "Stå klar for start.", "Forbered deg — starten er på vei!", "Nesten tid — gjør deg klar til å kjøre!"],
		["nl-NL"] = ["Maak je klaar!", "Klaarstaan voor de start.", "Bereid je voor — de start komt!", "Bijna tijd — maak je klaar om te gaan!"],
		["pl-PL"] = ["Gotuj się!", "Czekaj na start.", "Przygotuj się — start nadchodzi!", "Prawie czas — gotuj się do jazdy!"],
		["pt-BR"] = ["Prepare-se!", "Em espera para a largada.", "Prepare-se — a largada está chegando!", "Quase na hora — prepare-se para ir!"],
		["pt-PT"] = ["Prepara-te!", "Em espera para a partida.", "Prepara-te — a largada está a chegar!", "Quase na hora — prepara-te para partir!"],
		["ro-RO"] = ["Pregătește-te!", "Stai în așteptare pentru start.", "Pregătește-te — startul vine!", "Aproape timp — pregătește-te să pleci!"],
		["ru-RU"] = ["Приготовься!", "Ожидай старта.", "Готовься — старт скоро!", "Уже почти — готовься к старту!"],
		["sv-SE"] = ["Gör dig redo!", "Stå redo för starten.", "Förbered dig — starten är på väg!", "Nästan dags — gör dig redo att köra!"],
		["th-TH"] = ["เตรียมตัว!", "รอสัญญาณออกตัว.", "เตรียมตัว — กำลังจะออกตัว!", "เกือบถึงเวลาแล้ว — เตรียมตัวออกไป!"],
		["tr-TR"] = ["Hazır ol!", "Başlangıç için beklemede.", "Hazırlan — start geliyor!", "Neredeyse zaman — gitmek için hazırlan!"],
		["uk-UA"] = ["Готуйся!", "Очікуй старту.", "Готуйся — старт скоро!", "Вже майже — готуйся до старту!"],
		["zh-Hans"] = ["准备好！", "待命准备起跑。", "准备好 — 起跑就要来了！", "快到时间了 — 准备出发！"],
	};

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	private static int Run(Action action)
	{
		action();
		return 0;
	}

	private static string Require(string[] args, int index, string name)
	{
		if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
			throw new ArgumentException($"Missing required argument: <{name}>");

		return args[index];
	}

	private static int UnknownCommand(string command)
	{
		Console.Error.WriteLine($"Unknown command '{command}'.");
		PrintUsage();
		return 1;
	}

	private static void PrintUsage()
	{
		Console.WriteLine("""
			LocalizationEditor — bulk-edit TTS JSON and resx localization files

			Usage:
			  LocalizationEditor tts  <command> [args]
			  LocalizationEditor resx <command> [args]

			Commands (both modes):
			  list-keys
			  show-key     <key>
			  add-key      <key>
			  remove-key   <key>
			  rename-key   <oldKey> <newKey>
			  validate
			  sync-keys

			TTS-only:
			  set-phrases  <key> <lang> <phrase1> [phrase2 ...]   (lang="*" for all)

			Resx-only:
			  set-value    <key> <lang> <value>   (lang="base" for the base file)

			To add a new key with translations: edit the Add*Key() factory methods in
			Program.cs, then run: LocalizationEditor tts add-key <key>
			""");
	}
}
