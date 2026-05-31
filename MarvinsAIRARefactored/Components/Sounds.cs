
using MarvinsAIRARefactored.Classes;

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
		SeatOfPants,
		BrakeThrottleWarning,
		FfbClipping
	}

	public class SoundEffect( string SoundKey, Func<float> volumeProvider, Func<float> frequencyRatioProvider, bool loopSound, Func<float>? loopStartMsProvider = null, Func<float>? loopEndMsProvider = null )
	{
		public string SoundKey { get; } = SoundKey;
		public Func<float> GetVolume { get; } = volumeProvider;
		public Func<float> GetFrequencyRatio { get; } = frequencyRatioProvider;
		public bool LoopSound { get; } = loopSound;
		public uint GetLoopStartMs() => (uint) ( loopStartMsProvider?.Invoke() ?? 0f );
		public uint GetLoopEndMs() => (uint) ( loopEndMsProvider?.Invoke() ?? 0f );

		public bool IsPlaying { get; set; } = false;
		public bool ShouldBePlaying { get; set; } = false;
		public float Volume { get; set; } = 0f;
	}

	private readonly Dictionary<SoundEffectType, SoundEffect> _soundEffects = new() {
		{ SoundEffectType.Click,                new SoundEffect( "click",                   () => DataContext.DataContext.Instance.Settings.SoundsClickVolume,                 () => DataContext.DataContext.Instance.Settings.SoundsClickFrequencyRatio,                 false ) },
		{ SoundEffectType.ABSEngaged,           new SoundEffect( "abs_engaged",             () => DataContext.DataContext.Instance.Settings.SoundsABSEngagedVolume,            () => DataContext.DataContext.Instance.Settings.SoundsABSEngagedFrequencyRatio,            true,  () => DataContext.DataContext.Instance.Settings.SoundsABSEngagedLoopStartMs,            () => DataContext.DataContext.Instance.Settings.SoundsABSEngagedLoopEndMs ) },
		{ SoundEffectType.WheelLock,            new SoundEffect( "wheel_lock",              () => DataContext.DataContext.Instance.Settings.SoundsWheelLockVolume,             () => DataContext.DataContext.Instance.Settings.SoundsWheelLockFrequencyRatio,             true,  () => DataContext.DataContext.Instance.Settings.SoundsWheelLockLoopStartMs,             () => DataContext.DataContext.Instance.Settings.SoundsWheelLockLoopEndMs ) },
		{ SoundEffectType.WheelSpin,            new SoundEffect( "wheel_spin",              () => DataContext.DataContext.Instance.Settings.SoundsWheelSpinVolume,             () => DataContext.DataContext.Instance.Settings.SoundsWheelSpinFrequencyRatio,             true,  () => DataContext.DataContext.Instance.Settings.SoundsWheelSpinLoopStartMs,             () => DataContext.DataContext.Instance.Settings.SoundsWheelSpinLoopEndMs ) },
		{ SoundEffectType.Understeer,           new SoundEffect( "understeer",              () => DataContext.DataContext.Instance.Settings.SoundsUndersteerVolume,            () => DataContext.DataContext.Instance.Settings.SoundsUndersteerFrequencyRatio,            true,  () => DataContext.DataContext.Instance.Settings.SoundsUndersteerLoopStartMs,            () => DataContext.DataContext.Instance.Settings.SoundsUndersteerLoopEndMs ) },
		{ SoundEffectType.Oversteer,            new SoundEffect( "oversteer",               () => DataContext.DataContext.Instance.Settings.SoundsOversteerVolume,             () => DataContext.DataContext.Instance.Settings.SoundsOversteerFrequencyRatio,             true,  () => DataContext.DataContext.Instance.Settings.SoundsOversteerLoopStartMs,             () => DataContext.DataContext.Instance.Settings.SoundsOversteerLoopEndMs ) },
		{ SoundEffectType.SeatOfPants,          new SoundEffect( "seat_of_pants",           () => DataContext.DataContext.Instance.Settings.SoundsSeatOfPantsVolume,           () => DataContext.DataContext.Instance.Settings.SoundsSeatOfPantsFrequencyRatio,           true,  () => DataContext.DataContext.Instance.Settings.SoundsSeatOfPantsLoopStartMs,           () => DataContext.DataContext.Instance.Settings.SoundsSeatOfPantsLoopEndMs ) },
		{ SoundEffectType.BrakeThrottleWarning, new SoundEffect( "brake_throttle_warning",  () => DataContext.DataContext.Instance.Settings.SoundsBrakeThrottleWarningVolume,  () => DataContext.DataContext.Instance.Settings.SoundsBrakeThrottleWarningFrequencyRatio,  true,  () => DataContext.DataContext.Instance.Settings.SoundsBrakeThrottleWarningLoopStartMs,  () => DataContext.DataContext.Instance.Settings.SoundsBrakeThrottleWarningLoopEndMs ) },
		{ SoundEffectType.FfbClipping,          new SoundEffect( "ffb_clipping",            () => DataContext.DataContext.Instance.Settings.SoundsFfbClippingVolume,           () => DataContext.DataContext.Instance.Settings.SoundsFfbClippingFrequencyRatio,           true,  () => DataContext.DataContext.Instance.Settings.SoundsFfbClippingLoopStartMs,           () => DataContext.DataContext.Instance.Settings.SoundsFfbClippingLoopEndMs ) },
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

		var finalVolume = soundEffect.Volume * soundEffect.GetVolume() * settings.SoundsMasterVolume;

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

		// suppress all non-click sounds during replays if not allowed

		var isReplayActive = app.Simulator.IsReplayPlaying && !settings.SoundsAllowDuringReplays;

		// master sound switch can disable everything

		if ( settings.SoundsMasterEnabled )
		{
			// abs engaged

			if ( settings.SoundsABSEngagedEnabled && !isReplayActive )
			{
				if ( app.Simulator.BrakeABSactive )
				{
					_soundEffects[ SoundEffectType.ABSEngaged ].ShouldBePlaying = true;
					_soundEffects[ SoundEffectType.ABSEngaged ].Volume = settings.SoundsABSEngagedFadeWithBrake ? ( app.Simulator.Brake * 0.9f + 0.1f ) : 1f;
				}
			}

			// wheel lock

			if ( settings.SoundsWheelLockEnabled && !isReplayActive )
			{
				if ( ( app.Simulator.CurrentRpmSpeedRatio > 0f ) && ( app.Simulator.Gear > 0 ) && ( app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ] > 0f ) )
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

			if ( settings.SoundsWheelSpinEnabled && !isReplayActive )
			{
				if ( ( app.Simulator.CurrentRpmSpeedRatio > 0f ) && ( app.Simulator.Gear > 0 ) && ( app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ] > 0f ) )
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

			if ( settings.SoundsUndersteerEnabled && !isReplayActive )
			{
				if ( app.SteeringEffects.UndersteerEffect > 0f )
				{
					_soundEffects[ SoundEffectType.Understeer ].ShouldBePlaying = true;
					_soundEffects[ SoundEffectType.Understeer ].Volume = app.SteeringEffects.UndersteerEffect;
				}
			}

			// oversteer

			if ( settings.SoundsOversteerEnabled && !isReplayActive )
			{
				if ( app.SteeringEffects.OversteerEffect > 0f )
				{
					_soundEffects[ SoundEffectType.Oversteer ].ShouldBePlaying = true;
					_soundEffects[ SoundEffectType.Oversteer ].Volume = app.SteeringEffects.OversteerEffect;
				}
			}

			// seat-of-pants

			if ( settings.SoundsSeatOfPantsEnabled && !isReplayActive )
			{
				if ( app.SteeringEffects.SeatOfPantsEffect != 0f )
				{
					_soundEffects[ SoundEffectType.SeatOfPants ].ShouldBePlaying = true;
					_soundEffects[ SoundEffectType.SeatOfPants ].Volume = MathF.Abs( app.SteeringEffects.SeatOfPantsEffect );
				}
			}

			// brake + throttle warning

			if ( settings.SoundsBrakeThrottleWarningEnabled && !isReplayActive )
			{
				if ( app.Simulator.Brake > 0f && app.Simulator.Throttle > 0f )
				{
					_soundEffects[ SoundEffectType.BrakeThrottleWarning ].ShouldBePlaying = true;
					_soundEffects[ SoundEffectType.BrakeThrottleWarning ].Volume = 1f;
				}
			}

			// FFB clipping

			if ( settings.SoundsFfbClippingEnabled && !isReplayActive )
			{
				if ( app.RacingWheel.IsFFBClipping )
				{
					_soundEffects[ SoundEffectType.FfbClipping ].ShouldBePlaying = true;
					_soundEffects[ SoundEffectType.FfbClipping ].Volume = 1f;
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
					app.AudioManager.Play( soundEffect.SoundKey, finalVolume, soundEffect.GetFrequencyRatio(), soundEffect.LoopSound, soundEffect.GetLoopStartMs(), soundEffect.GetLoopEndMs() );

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

		// FMOD requires a per-tick update call for streaming, callbacks, and 3D processing
		app.AudioManager.Update();
	}
}
