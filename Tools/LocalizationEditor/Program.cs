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
		"add-key"   => Run(() => AddTtsKey(Require(args, 0, "key"))),
		"remove-key"=> Run(() => TtsEditor.RemoveKey(Require(args, 0, "key"))),
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
			_ => throw new InvalidOperationException(
				$"No phrase data defined for TTS key '{key}'. " +
				$"Add a case to AddTtsKey() in Program.cs.")
		};

		TtsEditor.AddKey(key, phrases);
	}

	/// <summary>
	/// Provides per-language values for a resx add-key operation.
	/// Edit this method to add new keys with full translations.
	/// </summary>
	private static void AddResxKey(string key)
	{
		throw new InvalidOperationException(
			$"No value data defined for resx key '{key}'. " +
			$"Add a case to AddResxKey() in Program.cs.");
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
