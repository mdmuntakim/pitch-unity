using System;
using System.Collections.Generic;

namespace Pitch.Core
{
    public enum CardColour { None, Yellow, Red }

    public struct FoulDecision
    {
        public bool IsFoul;
        public bool IsPenalty;
        public CardColour Card;
        public string Reason;
    }

    /// <summary>
    /// Referee approximation: contact based tackle fouls, reckless/serious foul
    /// cards, penalty area decisions and a delayed offside flag.
    /// </summary>
    public sealed class Referee
    {
        public float AdvantageTimer;

        public FoulDecision JudgeTackle(PlayerAgent fouler, PlayerAgent victim, bool wonBall, bool fromBehind, bool inPenaltyArea)
        {
            var decision = new FoulDecision { Card = CardColour.None };
            if (wonBall && !fromBehind)
            {
                decision.IsFoul = false;
                return decision;
            }

            float mistime = 1f - fouler.Tackling * 0.55f - fouler.Control * 0.15f;
            bool contact = !wonBall || fromBehind;
            if (!contact)
            {
                decision.IsFoul = false;
                return decision;
            }

            // A clean ball win from behind can still be a foul if it takes the man.
            // Losing the ball is not automatically a foul: only reckless contact is.
            bool reckless = fouler.Aggression > 0.66f && mistime > 0.45f;
            bool foul = fromBehind || reckless;
            if (!foul)
            {
                decision.IsFoul = false;
                return decision;
            }

            decision.IsFoul = true;
            decision.IsPenalty = inPenaltyArea;
            decision.Reason = fromBehind ? "foul from behind" : "late challenge";
            float severity = fouler.Aggression * 0.5f + (fromBehind ? 0.35f : 0f);
            if (severity > 0.8f || (fromBehind && severity > 0.65f))
                decision.Card = CardColour.Red;
            else if (severity > 0.45f)
                decision.Card = CardColour.Yellow;
            return decision;
        }

        /// <summary>
        /// Offside: an attacker is offside when, at the moment the pass is played,
        /// any part of him is nearer to the opponents' goal line than both the ball
        /// and the second last opponent, in the opponents' half.
        /// </summary>
        public static bool IsOffside(PlayerAgent receiver, Vec3 ballAtPass, float secondLastDefenderDistance, float attackDir)
        {
            float receiverDistance = PitchSpec.DistanceFromGoalLine(receiver.Position, attackDir);
            if (receiverDistance >= PitchSpec.HalfLength - 0.05f) return false;

            float ballDistance = PitchSpec.DistanceFromGoalLine(ballAtPass, attackDir);
            float limit = MathF.Min(ballDistance, secondLastDefenderDistance);
            return receiverDistance < limit - 0.35f;
        }

        public static CardColour Escalate(CardColour current, CardColour next)
        {
            if (current == CardColour.Red || next == CardColour.Red) return CardColour.Red;
            if (current == CardColour.Yellow && next == CardColour.Yellow) return CardColour.Red;
            return next;
        }
    }

    /// <summary>Rolling snapshot buffer used for goal replays and broadcast cuts.</summary>
    public sealed class ReplayBuffer
    {
        public struct Snapshot
        {
            public float Time;
            public Vec3 Ball;
            public Vec3[] Players;
            public float[] Facings;
        }

        private readonly Snapshot[] _frames;
        private int _head;
        private int _count;
        private readonly float _interval;
        private float _accumulator;

        public ReplayBuffer(int capacity = 600, float interval = 1f / 30f)
        {
            _frames = new Snapshot[capacity];
            _interval = interval;
        }

        public int Count => _count;

        public void Sample(float dt, BallPhysics ball, IEnumerable<TeamState> teams)
        {
            _accumulator += dt;
            if (_accumulator < _interval) return;
            _accumulator -= _interval;

            var players = new List<PlayerAgent>();
            foreach (var t in teams) players.AddRange(t.Players);
            var snapshot = new Snapshot
            {
                Time = 0f,
                Ball = ball.Position,
                Players = new Vec3[players.Count],
                Facings = new float[players.Count],
            };
            for (int i = 0; i < players.Count; i++)
            {
                snapshot.Players[i] = players[i].Position;
                snapshot.Facings[i] = players[i].Facing;
            }

            _head = (_head + 1) % _frames.Length;
            _frames[_head] = snapshot;
            _count = Math.Min(_count + 1, _frames.Length);
        }

        /// <summary>Frames from the last <paramref name="seconds"/> in chronological order.</summary>
        public List<Snapshot> LastSeconds(float seconds)
        {
            var result = new List<Snapshot>();
            int wanted = Math.Min(_count, (int)(seconds / _interval));
            for (int i = wanted - 1; i >= 0; i--)
            {
                int index = (_head - i + _frames.Length * 2) % _frames.Length;
                if (_count == 0) continue;
                result.Add(_frames[index]);
            }
            return result;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
            _accumulator = 0f;
        }
    }
}
