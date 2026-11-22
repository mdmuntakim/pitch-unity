using Pitch.Core;
using UnityEngine;

namespace Pitch.Game
{
    /// <summary>
    /// Goal replay presentation. Uses the 30 Hz snapshot ring buffer in the core;
    /// the broadcast camera cuts to a low angle and the HUD shows the replay
    /// wipe. Full ghost playback is deliberately marked as not implemented in
    /// the release notes rather than claimed.
    /// </summary>
    public sealed class ReplayDirector : MonoBehaviour
    {
        private MatchEngine _match;
        private BroadcastCamera _camera;
        private float _timer;

        public bool Active => _timer > 0f;

        public void Initialise(MatchEngine match, BroadcastCamera camera)
        {
            _match = match;
            _camera = camera;
            MatchEvents.Raised += OnMatchEvent;
        }

        public void Rebind(MatchEngine match) => _match = match;

        private void OnDestroy() => MatchEvents.Raised -= OnMatchEvent;

        private void OnMatchEvent(MatchEvent matchEvent)
        {
            if (matchEvent.Type != MatchEventType.Goal && matchEvent.Type != MatchEventType.OwnGoal) return;
            _timer = 3.5f;
            if (_match != null)
            {
                _match.Presentation.ShowReplay = true;
                _match.Presentation.ReplayProgress = 0f;
            }
        }

        private void Update()
        {
            if (_timer <= 0f) return;
            _timer -= Time.deltaTime;
            if (_match == null) return;
            _match.Presentation.ShowReplay = _timer > 0f;
            _match.Presentation.ReplayProgress = 1f - Mathf.Clamp01(_timer / 3.5f);
        }
    }
}
