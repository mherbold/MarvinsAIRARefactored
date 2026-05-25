using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace MarvinsAIRARefactored.Classes;

/// <summary>
/// Serializable settings for one commentary voice slot.
/// Five slots are maintained in Settings.VoiceSlots (Crew Chief, Spotter, Sportscaster 1, Sportscaster 2, Pit Reporter).
/// </summary>
public class VoiceSlotSettings : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged( [CallerMemberName] string? name = null ) =>
		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( name ) );

	private static string Pct => DataContext.DataContext.Instance.Localization[ "Percent" ];

	// -------------------------------------------------------------------------
	// Simple properties (no display string needed)
	// -------------------------------------------------------------------------

	/// <summary>User-editable display name shown in the settings page header.</summary>
	public string RoleLabel { get; set; } = string.Empty;

	/// <summary>Whether this slot is active. When false, no commentary is generated for this role.</summary>
	private bool _enabled = true;
	public bool Enabled
	{
		get => _enabled;
		set
		{
			if ( value != _enabled )
			{
				_enabled = value;
				OnPropertyChanged();
			}
		}
	}

	/// <summary>ElevenLabs voice ID (e.g. "pNInz6obpgDQGcFmaJgB").</summary>
	public string VoiceId { get; set; } = string.Empty;

	/// <summary>
	/// Human-readable voice name — populated from /v1/voices after a successful API call.
	/// Stored so the settings page can display the name without an extra round-trip on every open.
	/// </summary>
	public string VoiceName { get; set; } = string.Empty;

	/// <summary>Enables ElevenLabs speaker boost processing for improved clarity.</summary>
	public bool SpeakerBoost { get; set; } = true;

	// -------------------------------------------------------------------------
	// Knob properties with percentage display strings
	// -------------------------------------------------------------------------

	/// <summary>
	/// Voice consistency vs. expressiveness (0–1).
	/// Lower values produce more varied, emotional delivery; higher values are more consistent.
	/// </summary>
	private float _stability = 0.5f;

	public float Stability
	{
		get => _stability;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _stability )
			{
				_stability = value;
				OnPropertyChanged();
			}

			StabilityString = $"{_stability * 100f:F0}{Pct}";
		}
	}

	private string _stabilityString = string.Empty;

	[XmlIgnore]
	public string StabilityString
	{
		get => _stabilityString;

		set
		{
			if ( value != _stabilityString )
			{
				_stabilityString = value;
				OnPropertyChanged();
			}
		}
	}

	/// <summary>
	/// Style exaggeration (0–1).
	/// Higher values amplify the voice's natural style; best kept below 0.8 to avoid artifacts.
	/// </summary>
	private float _style = 0.5f;

	public float Style
	{
		get => _style;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _style )
			{
				_style = value;
				OnPropertyChanged();
			}

			StyleString = $"{_style * 100f:F0}{Pct}";
		}
	}

	private string _styleString = string.Empty;

	[XmlIgnore]
	public string StyleString
	{
		get => _styleString;

		set
		{
			if ( value != _styleString )
			{
				_styleString = value;
				OnPropertyChanged();
			}
		}
	}

	/// <summary>How closely the output matches the original voice sample (0–1).</summary>
	private float _similarityBoost = 0.75f;

	public float SimilarityBoost
	{
		get => _similarityBoost;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _similarityBoost )
			{
				_similarityBoost = value;
				OnPropertyChanged();
			}

			SimilarityBoostString = $"{_similarityBoost * 100f:F0}{Pct}";
		}
	}

	private string _similarityBoostString = string.Empty;

	[XmlIgnore]
	public string SimilarityBoostString
	{
		get => _similarityBoostString;

		set
		{
			if ( value != _similarityBoostString )
			{
				_similarityBoostString = value;
				OnPropertyChanged();
			}
		}
	}

	/// <summary>Per-slot volume multiplier (0–1), applied on top of MasterVolume.</summary>
	private float _volume = 1.0f;

	public float Volume
	{
		get => _volume;

		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( value != _volume )
			{
				_volume = value;
				OnPropertyChanged();
			}

			VolumeString = $"{_volume * 100f:F0}{Pct}";
		}
	}

	private string _volumeString = string.Empty;

	[XmlIgnore]
	public string VolumeString
	{
		get => _volumeString;

		set
		{
			if ( value != _volumeString )
			{
				_volumeString = value;
				OnPropertyChanged();
			}
		}
	}

	// -------------------------------------------------------------------------
	// Default-personality factory methods
	// -------------------------------------------------------------------------

	/// <summary>
	/// Crew Chief — deep, authoritative, calm under pressure. Tactical and direct.
	/// ElevenLabs voice: Adam (pNInz6obpgDQGcFmaJgB)
	/// </summary>
	public static VoiceSlotSettings CreateCrewChief() => new()
	{
		RoleLabel = "Crew Chief",
		Enabled = true,
		VoiceId = "pNInz6obpgDQGcFmaJgB",
		VoiceName = "Adam",
		Stability = 0.75f,
		Style = 0.30f,
		SimilarityBoost = 0.80f,
		SpeakerBoost = true,
		Volume = 1.0f
	};

	/// <summary>
	/// Spotter — clipped, fast, high-clarity. Safety-critical, no drama.
	/// ElevenLabs voice: Liam (TX3LPaxmHKxFdv7VOQHJ)
	/// </summary>
	public static VoiceSlotSettings CreateSpotter() => new()
	{
		RoleLabel = "Spotter",
		Enabled = true,
		VoiceId = "TX3LPaxmHKxFdv7VOQHJ",
		VoiceName = "Liam",
		Stability = 0.90f,
		Style = 0.15f,
		SimilarityBoost = 0.75f,
		SpeakerBoost = true,
		Volume = 1.0f
	};

	/// <summary>
	/// Sportscaster 1 (Lead) — warm, theatrical, excited on big moments. Main play-by-play.
	/// ElevenLabs voice: George (JBFqnCBsd6RMkjVDRZzb)
	/// </summary>
	public static VoiceSlotSettings CreateSportscaster1() => new()
	{
		RoleLabel = "Sportscaster 1",
		Enabled = true,
		VoiceId = "JBFqnCBsd6RMkjVDRZzb",
		VoiceName = "George",
		Stability = 0.35f,
		Style = 0.75f,
		SimilarityBoost = 0.75f,
		SpeakerBoost = true,
		Volume = 1.0f
	};

	/// <summary>
	/// Sportscaster 2 (Color) — analytical, measured, conversational. Insight and strategy.
	/// ElevenLabs voice: Daniel (onwK4e9ZLuTAKqWW03F9)
	/// </summary>
	public static VoiceSlotSettings CreateSportscaster2() => new()
	{
		RoleLabel = "Sportscaster 2",
		Enabled = true,
		VoiceId = "onwK4e9ZLuTAKqWW03F9",
		VoiceName = "Daniel",
		Stability = 0.55f,
		Style = 0.50f,
		SimilarityBoost = 0.75f,
		SpeakerBoost = true,
		Volume = 1.0f
	};

	/// <summary>
	/// Pit Reporter — energetic, on-the-ground, slightly breathless. Pit lane presence.
	/// ElevenLabs voice: Jessica (cgSgspJ2msm6clMCkdW9)
	/// </summary>
	public static VoiceSlotSettings CreatePitReporter() => new()
	{
		RoleLabel = "Pit Reporter",
		Enabled = true,
		VoiceId = "cgSgspJ2msm6clMCkdW9",
		VoiceName = "Jessica",
		Stability = 0.45f,
		Style = 0.65f,
		SimilarityBoost = 0.80f,
		SpeakerBoost = true,
		Volume = 1.0f
	};

	/// <summary>
	/// Returns the default slot list in canonical order:
	/// [0] Crew Chief, [1] Spotter, [2] Sportscaster 1, [3] Sportscaster 2, [4] Pit Reporter.
	/// </summary>
	public static List<VoiceSlotSettings> CreateDefaults() =>
	[
		CreateCrewChief(),
		CreateSpotter(),
		CreateSportscaster1(),
		CreateSportscaster2(),
		CreatePitReporter()
	];
}
