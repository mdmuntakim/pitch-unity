using System;
using Pitch.Core;
using UnityEngine;

namespace Pitch.Game
{
    /// <summary>
    /// Commentary-free stadium sound, generated at runtime (no audio files, no
    /// licensed chants): filtered crowd noise with excitement swells, a goal
    /// roar, a referee whistle and a kick thump. All original synthesis.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        private MatchEngine _match;
        private AudioSource _crowd;
        private AudioSource _effects;
        private AudioClip _whistle;
        private AudioClip _thump;
        private AudioClip _roar;
        private float _excitement;
        private float _targetExcitement;
        private System.Random _rng = new System.Random(5);

        public void Initialise(MatchEngine match)
        {
            _match = match;
            _crowd = gameObject.AddComponent<AudioSource>();
            _crowd.loop = true;
            _crowd.spatialBlend = 0f;
            _crowd.volume = 0.5f;
            _crowd.clip = BuildCrowdLoop();
            _crowd.Play();

            _effects = gameObject.AddComponent<AudioSource>();
            _effects.spatialBlend = 0f;
            _effects.volume = 0.8f;
            _whistle = BuildWhistle();
            _thump = BuildThump();
            _roar = BuildRoar();

            MatchEvents.Raised += OnMatchEvent;
        }

        public void Rebind(MatchEngine match) => _match = match;

        private void OnDestroy() => MatchEvents.Raised -= OnMatchEvent;

        private void OnMatchEvent(MatchEvent matchEvent)
        {
            switch (matchEvent.Type)
            {
                case MatchEventType.Goal:
                case MatchEventType.OwnGoal:
                    _targetExcitement = 1f;
                    if (_effects != null && _roar != null) _effects.PlayOneShot(_roar, 1f);
                    break;
                case MatchEventType.Shot:
                    if (_effects != null && _thump != null) _effects.PlayOneShot(_thump, 0.55f);
                    break;
                case MatchEventType.Save:
                case MatchEventType.Post:
                    _targetExcitement = Mathf.Max(_targetExcitement, 0.8f);
                    break;
                case MatchEventType.Foul:
                case MatchEventType.Offside:
                    if (_effects != null && _whistle != null) _effects.PlayOneShot(_whistle, 0.7f);
                    _targetExcitement = Mathf.Max(_targetExcitement, 0.55f);
                    break;
                case MatchEventType.Tifo:
                case MatchEventType.KickOff:
                    _targetExcitement = Mathf.Max(_targetExcitement, 0.7f);
                    break;
                case MatchEventType.Celebration:
                    _targetExcitement = 1f;
                    break;
            }
        }

        private void Update()
        {
            _targetExcitement = Mathf.MoveTowards(_targetExcitement, 0.25f, Time.deltaTime * 0.1f);
            _excitement = Mathf.Lerp(_excitement, _targetExcitement, 1f - Mathf.Exp(-1.6f * Time.deltaTime));
            if (_crowd != null) _crowd.volume = Mathf.Lerp(0.32f, 0.92f, _excitement);
        }

        private AudioClip BuildCrowdLoop()
        {
            const int rate = 32000;
            const float seconds = 8f;
            int samples = (int)(rate * seconds);
            var data = new float[samples * 2];
            float low = 0f;
            float mid = 0f;
            for (int i = 0; i < samples; i++)
            {
                float white = (float)(_rng.NextDouble() * 2.0 - 1.0);
                low += (white - low) * 0.008f;
                mid += (white - mid) * 0.09f;
                float swell = 0.55f + 0.45f * Mathf.Sin(i / (float)rate * Mathf.PI * 2f / seconds * 2f + 0.7f);
                float sample = (low * 2.4f + mid * 0.55f + white * 0.06f) * 0.35f * swell;
                data[i * 2] = sample;
                data[i * 2 + 1] = sample * 0.97f;
            }
            var clip = AudioClip.Create("Crowd Ambience", samples, 2, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip BuildWhistle()
        {
            const int rate = 32000;
            int samples = (int)(rate * 0.55f);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-6f * t) * Mathf.Clamp01(t / 0.03f);
                float warble = 1f + 0.02f * Mathf.Sin(t * 90f);
                data[i] = Mathf.Sin(t * 2800f * warble) * 0.35f * envelope
                          + Mathf.Sin(t * 4200f) * 0.12f * envelope;
            }
            var clip = AudioClip.Create("Whistle", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip BuildThump()
        {
            const int rate = 32000;
            int samples = (int)(rate * 0.18f);
            var data = new float[samples];
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float freq = Mathf.Lerp(160f, 55f, t / 0.18f);
                phase += freq / rate * Mathf.PI * 2f;
                data[i] = Mathf.Sin(phase) * Mathf.Exp(-24f * t) * 0.8f;
            }
            var clip = AudioClip.Create("Kick", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip BuildRoar()
        {
            const int rate = 32000;
            const float seconds = 4.5f;
            int samples = (int)(rate * seconds);
            var data = new float[samples * 2];
            float low = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float white = (float)(_rng.NextDouble() * 2.0 - 1.0);
                low += (white - low) * 0.02f;
                float envelope = Mathf.Clamp01(t / 0.25f) * Mathf.Exp(-0.55f * t);
                float sample = (low * 3.2f + white * 0.12f) * envelope * 0.6f;
                data[i * 2] = sample;
                data[i * 2 + 1] = sample * 0.94f;
            }
            var clip = AudioClip.Create("Goal Roar", samples, 2, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
