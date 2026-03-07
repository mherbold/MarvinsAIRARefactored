
using MarvinsAIRARefactored.Classes;
using static MarvinsAIRARefactored.Components.Sounds;

namespace MarvinsAIRARefactored.Components;

public class Sounds
{
	public enum SoundEffectType : int
	{
		Click,
		ABSEngaged,
		WheelLock,
		WheelSpin,
		Understeer,
		Oversteer,
		SeatOfPants
	}

	public class SoundEffect( string SoundKey, Func<float> volumeProvider, Func<float> frequencyRatioProvider, bool loopSound )
	{
		public string SoundKey { get; } = SoundKey;
		public Func<float> GetVolume { get; } = volumeProvider;
		public Func<float> GetFrequencyRatio { get; } = frequencyRatioProvider;
		public bool LoopSound { get; } = loopSound;

		public bool IsPlaying { get; set; } = false;
		public bool ShouldBePlaying { get; set; } = false;
		public float Volume { get; set; } = 0f;
	}

	private readonly Dictionary<SoundEffectType, SoundEffect> _soundEffects = new() {
		{ SoundEffectType.Click, new SoundEffect( "click", () => DataContext.DataContext.Instance.Settings.SoundsClickVolume, () => DataContext.DataContext.Instance.Settings.SoundsClickFrequencyRatio, false ) },
		{ SoundEffectType.ABSEngaged, new SoundEffect( "abs_engaged",() => DataContext.DataContext.Instance.Settings.SoundsABSEngagedVolume,() => DataContext.DataContext.Instance.Settings.SoundsABSEngagedFrequencyRatio, true ) },
		{ SoundEffectType.WheelLock, new SoundEffect( "wheel_lock",() => DataContext.DataContext.Instance.Settings.SoundsWheelLockVolume,() => DataContext.DataContext.Instance.Settings.SoundsWheelLockFrequencyRatio, true ) },
		{ SoundEffectType.WheelSpin, new SoundEffect( "wheel_spin",() => DataContext.DataContext.Instance.Settings.SoundsWheelSpinVolume,() => DataContext.DataContext.Instance.Settings.SoundsWheelSpinFrequencyRatio, true ) },
		{ SoundEffectType.Understeer, new SoundEffect( "understeer",() => DataContext.DataContext.Instance.Settings.SoundsUndersteerVolume,() => DataContext.DataContext.Instance.Settings.SoundsUndersteerFrequencyRatio, true ) },
		{ SoundEffectType.Oversteer, new SoundEffect( "oversteer",() => DataContext.DataContext.Instance.Settings.SoundsOversteerVolume,() => DataContext.DataContext.Instance.Settings.SoundsOversteerFrequencyRatio, true ) },
		{ SoundEffectType.SeatOfPants, new SoundEffect( "seat_of_pants",() => DataContext.DataContext.Instance.Settings.SoundsSeatOfPantsVolume,() => DataContext.DataContext.Instance.Settings.SoundsSeatOfPantsFrequencyRatio, true ) }
	};

	private SoundEffectType? _testSoundEffectType = null;
	private int _testSoundCounter = 0;

	public void Initialize()
	{
		var app = App.Instance!;

		foreach ( var keyValuePair in _soundEffects )
		{
			app.AudioManager.LoadSound( "Effects", keyValuePair.Value.SoundKey );
		}
	}

	public void Test( SoundEffectType soundEffectType )
	{
		_testSoundEffectType = soundEffectType;
		_testSoundCounter = _soundEffects[ soundEffectType ].LoopSound ? 60 : 1;
	}

	public void Play( SoundEffectType soundEffectType, float volume = 1f )
	{
		var app = App.Instance!;

		var settings = DataContext.DataContext.Instance.Settings;

		var soundEffect = _soundEffects[ soundEffectType ];

		soundEffect.Volume = volume;

		var finalVolume  = soundEffect.Volume * soundEffect.GetVolume() * settings.SoundsMasterVolume;

		app.AudioManager.Play( soundEffect.SoundKey, finalVolume, soundEffect.GetFrequencyRatio(), soundEffect.LoopSound );

		soundEffect.IsPlaying = true;
	}

	public void Tick( App app )
	{
		// shortcut to settings

		var settings = DataContext.DataContext.Instance.Settings;

		// reset sound effects

		foreach ( var keyValuePair in _soundEffects )
		{
			keyValuePair.Value.ShouldBePlaying = false;
		}

		// play test sound effect

		if ( _testSoundEffectType != null )
		{
			_soundEffects[ (SoundEffectType) _testSoundEffectType ].ShouldBePlaying = true;
			_soundEffects[ (SoundEffectType) _testSoundEffectType ].Volume = 1f;

			_testSoundCounter--;

			if ( _testSoundCounter == 0 )
			{
				_testSoundEffectType = null;
			}
		}

		// master sound switch can disable everything

		if ( settings.SoundsMasterEnabled )
		{
			// abs engaged

			if ( settings.SoundsABSEngagedEnabled )
			{
				if ( app.Simulator.BrakeABSactive )
				{
					_soundEffects[ SoundEffectType.ABSEngaged ].ShouldBePlaying = true;
					_soundEffects[ SoundEffectType.ABSEngaged ].Volume = settings.SoundsABSEngagedFadeWithBrake ? ( app.Simulator.Brake * 0.9f + 0.1f ) : 1f;
				}
			}

			// wheel lock

			if ( settings.SoundsWheelLockEnabled )
			{
				if ( ( app.Simulator.CurrentRpmSpeedRatio > 0f ) && ( app.Simulator.Gear > 0 ) )
				{
					var difference = app.Simulator.CurrentRpmSpeedRatio - app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ];
					var differencePct = ( difference / app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ] ) - ( 1f - settings.SoundsWheelLockSensitivity );

					if ( differencePct > 0f )
					{
						_soundEffects[ SoundEffectType.WheelLock ].ShouldBePlaying = true;
						_soundEffects[ SoundEffectType.WheelLock ].Volume = MathZ.Saturate( differencePct / 0.03f ) * ( ( settings.SoundsWheelLockFadeWithBrake ) ? ( app.Simulator.Brake * 0.9f + 0.1f ) : 1f );
					}
				}
			}

			// wheel spin

			if ( settings.SoundsWheelSpinEnabled )
			{
				if ( ( app.Simulator.CurrentRpmSpeedRatio > 0f ) && ( app.Simulator.Gear > 0 ) )
				{
					var difference = app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ] - app.Simulator.CurrentRpmSpeedRatio;
					var differencePct = ( difference / app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ] ) - ( 1f - settings.SoundsWheelSpinSensitivity );

					if ( differencePct > 0f )
					{
						_soundEffects[ SoundEffectType.WheelSpin ].ShouldBePlaying = true;
						_soundEffects[ SoundEffectType.WheelSpin ].Volume = MathZ.Saturate( differencePct / 0.03f ) * ( ( settings.SoundsWheelSpinFadeWithThrottle ) ? ( app.Simulator.Throttle * 0.9f + 0.1f ) : 1f );
					}
				}
			}

			// understeer

			if ( settings.SoundsUndersteerEnabled )
			{
				if ( app.SteeringEffects.UndersteerEffect > 0f )
				{
					_soundEffects[ SoundEffectType.Understeer ].ShouldBePlaying = true;
					_soundEffects[ SoundEffectType.Understeer ].Volume = app.SteeringEffects.UndersteerEffect;
				}
			}

			// oversteer

			if ( settings.SoundsOversteerEnabled )
			{
				if ( app.SteeringEffects.OversteerEffect > 0f )
				{
					_soundEffects[ SoundEffectType.Oversteer ].ShouldBePlaying = true;
					_soundEffects[ SoundEffectType.Oversteer ].Volume = app.SteeringEffects.OversteerEffect;
				}
			}

			// seat-of-pants

			if ( settings.SoundsSeatOfPantsEnabled )
			{
				if ( app.SteeringEffects.SeatOfPantsEffect != 0f )
				{
					_soundEffects[ SoundEffectType.SeatOfPants ].ShouldBePlaying = true;
					_soundEffects[ SoundEffectType.SeatOfPants ].Volume = MathF.Abs( app.SteeringEffects.SeatOfPantsEffect );
				}
			}
		}

		// play sounds that should be playing and stop sounds that should not be playing

		foreach ( var keyValuePair in _soundEffects )
		{
			var soundEffect = keyValuePair.Value;

			if ( soundEffect.ShouldBePlaying )
			{
				float finalVolume = soundEffect.Volume * soundEffect.GetVolume() * settings.SoundsMasterVolume;

				if ( soundEffect.IsPlaying )
				{
					app.AudioManager.Update( soundEffect.SoundKey, finalVolume, soundEffect.GetFrequencyRatio() );
				}
				else
				{
					app.AudioManager.Play( soundEffect.SoundKey, finalVolume, soundEffect.GetFrequencyRatio(), soundEffect.LoopSound );

					soundEffect.IsPlaying = true;
				}
			}
			else if ( soundEffect.IsPlaying )
			{
				if ( soundEffect.LoopSound )
				{
					app.AudioManager.Stop( soundEffect.SoundKey );

					soundEffect.IsPlaying = false;
				}
				else
				{
					soundEffect.IsPlaying = app.AudioManager.IsPlaying( soundEffect.SoundKey );
				}
			}
		}
	}
}
