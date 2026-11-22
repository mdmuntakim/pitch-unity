using System.Collections.Generic;
using Pitch.Core;
using UnityEngine;

namespace Pitch.Game
{
    /// <summary>
    /// World Cup style pre-kick-off walk-past. Two original teams walk in single
    /// file past an original trophy on a plinth at the centre spot, a few stop to
    /// touch it, the crowd roars and the broadcast camera dollies around. The
    /// real kick-off elevens are hidden until the intro ends.
    /// </summary>
    public sealed class TrophyIntroView : MonoBehaviour
    {
        private MatchEngine _match;
        private readonly List<PlayerVisual> _walkers = new List<PlayerVisual>();
        private readonly List<PlayerAgent> _actors = new List<PlayerAgent>();
        private Transform _root;

        public static TrophyIntroView Create(Transform parent, MatchEngine match)
        {
            var go = new GameObject("Trophy Intro");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<TrophyIntroView>();
            view._root = go.transform;
            view.Build(match);
            return view;
        }

        public void Rebind(MatchEngine match)
        {
            _match = match;
            foreach (var walker in _walkers) if (walker != null) Destroy(walker.gameObject);
            _walkers.Clear();
            _actors.Clear();
            Build(match);
        }

        private void Build(MatchEngine match)
        {
            _match = match;
            if (match == null) return;
            var rng = new System.Random(41);
            for (int team = 0; team < 2; team++)
            {
                var kit = team == 0 ? KitPalette.Home : KitPalette.Away;
                for (int i = 0; i < 11; i++)
                {
                    var actor = new PlayerAgent
                    {
                        Side = team == 0 ? TeamSide.Home : TeamSide.Away,
                        Number = i + 1,
                        Name = "Intro",
                        Role = PlayerRole.CentreMid,
                        Pace = 0.65f,
                        Celebration = (CelebrateStyle)(1 + rng.Next(5)),
                    };
                    var visual = PlayerVisual.Create(_root, actor, kit);
                    _actors.Add(actor);
                    _walkers.Add(visual);
                }
            }
        }

        private void Update()
        {
            if (_match == null) return;
            var intro = _match.Intro;
            bool show = _match.Phase == MatchPhase.Intro && !intro.Finished;
            if (!show)
            {
                foreach (var walker in _walkers) if (walker != null) walker.gameObject.SetActive(false);
                var stadium = GameBootstrap.Instance != null ? GameBootstrap.Instance.Stadium : null;
                if (stadium != null) stadium.SetTrophyVisible(false);
                var bootView = GameBootstrap.Instance != null ? GameBootstrap.Instance.View : null;
                if (bootView != null) bootView.gameObject.SetActive(true);
                return;
            }

            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Stadium != null) boot.Stadium.SetTrophyVisible(true);
            if (boot != null && boot.View != null) boot.View.gameObject.SetActive(false);

            float t = Mathf.Clamp01((intro.Elapsed - 2.5f) / 13f);
            for (int i = 0; i < _walkers.Count; i++)
            {
                bool home = i < 11;
                int index = home ? i : i - 11;
                float lane = home ? -2.3f : 2.3f;
                float startZ = home ? -34f : 34f;
                float endZ = home ? 30f : -30f;
                float lead = index * 2.1f;
                float local = Mathf.Clamp01((t * 34f - lead) / 22f);
                float z = Mathf.Lerp(startZ, endZ, local) * (home ? 1f : 1f);
                if (home) z = Mathf.Lerp(startZ, endZ, local);
                else z = Mathf.Lerp(startZ, endZ, local);

                // Three players stop at the trophy and touch it.
                bool toucher = index == 4 || index == 5 || index == 7;
                if (toucher && Mathf.Abs(z) < 3.2f && t > 0.35f)
                {
                    z = Mathf.Sign(z) * 2.4f;
                }

                float bob = Mathf.Abs(Mathf.Sin(Time.time * 7f + index)) * 0.05f;
                var visual = _walkers[i];
                visual.transform.position = new Vector3(lane, bob, z);
                visual.transform.rotation = Quaternion.Euler(0f, home ? 0f : 180f, 0f);
            }
        }

        private void OnDestroy()
        {
            foreach (var walker in _walkers) if (walker != null) Destroy(walker.gameObject);
        }
    }
}
