using System;
using System.Collections.Generic;

namespace Pitch.Core
{
    public enum TifoPhase { Idle, Unfurl, Mosaic, Chant, Fold }

    public struct BroadcastLine
    {
        public string Title;
        public string Subtitle;
        public float Duration;
        public float Age;
    }

    public struct CameraShake
    {
        public float Amplitude;
        public float Duration;
        public float Age;
    }

    /// <summary>
    /// Everything the HUD and the broadcast layer need, independent of the UI
    /// framework, so the same state drives uGUI on device and headless tests.
    /// </summary>
    public sealed class PresentationState
    {
        public string HomeName = "NORTHPORT";
        public string AwayName = "AURELIA";
        public string HomeAbbreviation = "NPT";
        public string AwayAbbreviation = "AUR";
        public int HomeScore;
        public int AwayScore;
        public int DisplayMinute;
        public int DisplaySecond;
        public bool ShowStoppage;
        public int StoppageMinutes;
        public string PhaseLabel = "KICK OFF";
        public string PossessionLabel = "";
        public TifoPhase Tifo = TifoPhase.Idle;
        public float TifoProgress;
        public float CelebrationTimer;
        public CelebrateStyle Celebration;
        public CameraShake Shake;
        public bool ShowReplay;
        public float ReplayProgress;
        public bool BallHighlight = true;
        public bool ShowRadar = true;
        public string LastEvent;
        public readonly Queue<BroadcastLine> LowerThirds = new Queue<BroadcastLine>();
        public BroadcastLine ActiveLowerThird;
        public float LowerThirdAge;

        public string ScoreLine => $"{HomeAbbreviation} {HomeScore} - {AwayScore} {AwayAbbreviation}";
        public string ClockLine => ShowStoppage ? $"{DisplayMinute:00}:{DisplaySecond:00} +{StoppageMinutes}" : $"{DisplayMinute:00}:{DisplaySecond:00}";

        public void PushLowerThird(string title, string subtitle, float duration = 3.2f)
        {
            LowerThirds.Enqueue(new BroadcastLine { Title = title, Subtitle = subtitle, Duration = duration });
        }

        public void TickLowerThirds(float dt)
        {
            if (ActiveLowerThird.Duration <= 0f && LowerThirds.Count > 0)
            {
                ActiveLowerThird = LowerThirds.Dequeue();
                LowerThirdAge = 0f;
            }
            if (ActiveLowerThird.Duration > 0f)
            {
                LowerThirdAge += dt;
                if (LowerThirdAge >= ActiveLowerThird.Duration)
                {
                    ActiveLowerThird = default;
                    LowerThirdAge = 0f;
                }
            }
        }

        public void ShakeCamera(float amplitude, float duration)
        {
            Shake = new CameraShake { Amplitude = amplitude, Duration = duration, Age = 0f };
        }

        public float ShakeOffset(float time)
        {
            if (Shake.Duration <= 0f || Shake.Age >= Shake.Duration) return 0f;
            float falloff = 1f - Shake.Age / Shake.Duration;
            return MathF.Sin(time * 47f) * Shake.Amplitude * falloff;
        }

        public void Tick(float dt)
        {
            TickLowerThirds(dt);
            if (CelebrationTimer > 0f) CelebrationTimer = MathF.Max(0f, CelebrationTimer - dt);
            if (Shake.Duration > 0f) Shake = new CameraShake
            {
                Amplitude = Shake.Amplitude,
                Duration = Shake.Duration,
                Age = Shake.Age + dt,
            };
            if (Tifo != TifoPhase.Idle)
            {
                TifoProgress = MathF.Min(1f, TifoProgress + dt / 2f);
            }
        }
    }

    public enum IntroPhase
    {
        TunnelWide,
        TrophyApproach,
        WalkPast,
        TouchTrophy,
        LineUp,
        Anthem,
        Handshake,
        Ready,
        Done,
    }

    public struct IntroCameraKey
    {
        public Vec3 Position;
        public Vec3 LookAt;
        public float FieldOfView;
        public IntroCameraKey(Vec3 position, Vec3 lookAt, float fov) { Position = position; LookAt = lookAt; FieldOfView = fov; }
    }

    /// <summary>
    /// World Cup style pre-kick-off sequence: tunnel wide shot, both teams walking
    /// past the trophy on its plinth, players touching/kissing it, line-up and
    /// anthem, all timed and skippable. Original trophy geometry, no licensed IP.
    /// </summary>
    public sealed class IntroDirector
    {
        private static readonly (IntroPhase phase, float duration)[] Timeline =
        {
            (IntroPhase.TunnelWide, 4.5f),
            (IntroPhase.TrophyApproach, 4.0f),
            (IntroPhase.WalkPast, 8.0f),
            (IntroPhase.TouchTrophy, 5.0f),
            (IntroPhase.LineUp, 5.0f),
            (IntroPhase.Anthem, 5.0f),
            (IntroPhase.Handshake, 3.0f),
            (IntroPhase.Ready, 1.0f),
        };

        public IntroPhase Phase { get; private set; } = IntroPhase.TunnelWide;
        public float PhaseTime { get; private set; }
        public float Elapsed { get; private set; }
        public float TotalDuration { get; private set; }
        public bool Skipped { get; private set; }
        public bool Finished => Phase == IntroPhase.Done || Skipped;
        public float Progress => TotalDuration <= 0f ? 1f : PitchMath.Clamp01(Elapsed / TotalDuration);
        public int PlayersWhoTouchedTrophy { get; private set; }

        public IntroDirector()
        {
            foreach (var entry in Timeline) TotalDuration += entry.duration;
        }

        public void Tick(float dt)
        {
            if (Finished) return;
            Elapsed += dt;
            PhaseTime += dt;

            for (int guard = 0; guard < Timeline.Length + 1; guard++)
            {
                var current = Timeline[(int)Phase];
                if (PhaseTime < current.duration) break;
                PhaseTime -= current.duration;
                Phase = (IntroPhase)((int)Phase + 1);
                if (Phase >= IntroPhase.Done) break;
                if (Phase == IntroPhase.TouchTrophy) PlayersWhoTouchedTrophy = 3;
            }
        }

        public void Skip()
        {
            Skipped = true;
            Phase = IntroPhase.Done;
        }

        public IntroCameraKey CameraAt(float time)
        {
            // A broadcast dolly. The trophy plinth sits on the centre spot, the
            // tunnel mouth is at the south west corner; the camera sweeps from a
            // low tunnel shot to a high crane shot over the centre circle.
            float t = PitchMath.Clamp01(time / MathF.Max(0.01f, TotalDuration));
            Vec3 position;
            Vec3 look;
            float fov;

            if (t < 0.18f)
            {
                float k = t / 0.18f;
                position = Vec3.Lerp(new Vec3(-38f, 2.1f, -40f), new Vec3(-22f, 3.4f, -26f), k);
                look = Vec3.Lerp(new Vec3(-16f, 1.4f, -18f), new Vec3(0f, 1.2f, 0f), k);
                fov = PitchMath.Lerp(34f, 40f, k);
            }
            else if (t < 0.45f)
            {
                float k = (t - 0.18f) / 0.27f;
                position = Vec3.Lerp(new Vec3(-22f, 3.4f, -26f), new Vec3(-8f, 1.5f, -8f), k);
                look = Vec3.Lerp(new Vec3(0f, 1.2f, 0f), new Vec3(0f, 1.0f, 0f), k);
                fov = PitchMath.Lerp(40f, 28f, k);
            }
            else if (t < 0.75f)
            {
                float k = (t - 0.45f) / 0.30f;
                float angle = PitchMath.Lerp(-2.2f, 0.4f, k);
                position = new Vec3(MathF.Sin(angle) * 9f, 1.4f + k * 4.5f, MathF.Cos(angle) * 9f);
                look = new Vec3(0f, 1.0f, 0f);
                fov = PitchMath.Lerp(30f, 42f, k);
            }
            else
            {
                float k = (t - 0.75f) / 0.25f;
                position = Vec3.Lerp(new Vec3(0f, 6.5f, 9f), new Vec3(0f, 18f, 32f), k);
                look = Vec3.Lerp(new Vec3(0f, 1.2f, 0f), new Vec3(0f, 0f, 8f), k);
                fov = PitchMath.Lerp(44f, 38f, k);
            }

            return new IntroCameraKey(position, look, fov);
        }

        public void Reset()
        {
            Phase = IntroPhase.TunnelWide;
            PhaseTime = 0f;
            Elapsed = 0f;
            Skipped = false;
            PlayersWhoTouchedTrophy = 0;
        }
    }
}
