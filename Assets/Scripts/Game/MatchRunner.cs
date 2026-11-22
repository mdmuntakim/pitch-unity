using System;
using Pitch.Core;
using UnityEngine;

namespace Pitch.Game
{
    public static class MatchEvents
    {
        public static event Action<MatchEvent> Raised;
        public static void Raise(MatchEvent matchEvent) => Raised?.Invoke(matchEvent);
    }

    /// <summary>
    /// Drives the fixed 60 Hz simulation from Unity's variable frame delta and
    /// forwards match events to the presentation layer. Rendering interpolates
    /// between the last two fixed steps.
    /// </summary>
    public sealed class MatchRunner : MonoBehaviour
    {
        private MatchEngine _match;
        private float _slowMotionUntil;

        public void Initialise(MatchEngine match)
        {
            _match = match;
        }

        public void Rebind(MatchEngine match)
        {
            _match = match;
        }

        private void Update()
        {
            if (_match == null) return;

            if (!_match.Paused)
            {
                float delta = Time.deltaTime;
                if (Time.time < _slowMotionUntil) delta *= 0.35f;
                _match.Advance(delta);

                foreach (var matchEvent in _match.DrainEvents())
                {
                    if (matchEvent.Type == MatchEventType.Goal || matchEvent.Type == MatchEventType.OwnGoal)
                        _slowMotionUntil = Time.time + 1.2f;
                    MatchEvents.Raise(matchEvent);
                }
            }

            var hud = GameBootstrap.Instance != null ? GameBootstrap.Instance.Hud : null;
            if (hud != null) hud.Refresh(_match);
        }
    }
}
