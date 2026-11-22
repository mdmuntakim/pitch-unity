using System;
using System.Collections.Generic;

namespace Pitch.Core
{
    /// <summary>
    /// Team brain: compact defensive block, pressing, marking, passing lanes,
    /// off-ball runs that respect the offside line, and a goalkeeper. It only
    /// issues intents through MatchEngine, so AI and human input share one path.
    /// </summary>
    public sealed class TeamAI
    {
        public readonly TeamState Team;
        private readonly Random _rng;
        private float _thinkTimer;

        public TeamAI(TeamState team, int seed)
        {
            Team = team;
            _rng = new Random(seed);
        }

        public void Update(MatchEngine match, float dt)
        {
            if (match.Phase != MatchPhase.InPlay) return;

            var ball = match.Ball.Position;
            float dir = Team.AttackDir;
            var carrier = match.Carrier;
            bool inPossession = carrier != null && carrier.Side == Team.Side;
            bool ballLoose = carrier == null;

            var goalkeeper = Team.Goalkeeper;
            var opponents = match.Team(PitchSpec.Opponent(Team.Side)).Players;

            var presser = NearestOutfield(ball, exclude: null);
            var cover = presser != null ? NearestOutfield(ball, exclude: presser) : null;

            foreach (var player in Team.Players)
            {
                player.TickTimers(dt);
                if (player.State == PlayerState.Down || player.State == PlayerState.Celebrate)
                    continue;
                if (player.Role == PlayerRole.Goalkeeper) continue;

                if (player.IsUserControlled && inPossession && player == carrier)
                    continue;

                if (player == carrier)
                {
                    Carry(match, player, opponents, dir, dt);
                    continue;
                }

                Vec3 target;
                bool sprint;

                if (inPossession)
                {
                    target = SupportTarget(match, player, ball, dir);
                    sprint = player.Role == PlayerRole.Winger || player.Role == PlayerRole.Striker;
                }
                else if (ballLoose)
                {
                    // Chase a loose ball, but keep the shape behind it.
                    if (player == presser)
                    {
                        target = match.Ball.Position + match.Ball.Velocity * 0.18f;
                        sprint = true;
                    }
                    else
                    {
                        target = DefendTarget(match, player, ball, dir, false);
                        sprint = false;
                    }
                }
                else
                {
                    bool isPresser = player == presser;
                    bool isCover = player == cover;
                    target = DefendTarget(match, player, ball, dir, false);
                    if (isPresser)
                    {
                        var chased = carrier != null ? carrier.Position + carrier.Velocity * 0.22f : ball;
                        target = chased;
                        sprint = true;
                        float dist = Vec3.FlatDistance(player.Position, chased);
                        if (dist < 1.35f && player.CanAct && player.DecisionTimer <= 0f && carrier != null)
                        {
                            player.DecisionTimer = 0.9f;
                            match.TryTackle(player, carrier);
                        }
                    }
                    else if (isCover)
                    {
                        target = CoverTarget(match, ball, dir);
                        sprint = true;
                    }
                    else
                    {
                        target = MarkTarget(match, player, ball, opponents, dir);
                        sprint = false;
                    }
                }

                player.SteerTowards(target, dt, 1f, sprint && player.Stamina > 18f);
            }

            UpdateGoalkeeper(match, goalkeeper, dt);
        }

        private PlayerAgent NearestOutfield(Vec3 point, PlayerAgent exclude)
        {
            PlayerAgent best = null;
            float bestDistance = float.MaxValue;
            foreach (var p in Team.Players)
            {
                if (p == exclude || p.Role == PlayerRole.Goalkeeper) continue;
                if (p.State == PlayerState.Down || p.State == PlayerState.Celebrate) continue;
                float d = Vec3.FlatDistance(p.Position, point);
                if (d < bestDistance) { bestDistance = d; best = p; }
            }
            return best;
        }

        private Vec3 SupportTarget(MatchEngine match, PlayerAgent player, Vec3 ball, float dir)
        {
            Vec3 target = player.HomePosition;
            float ballDepth = PitchSpec.DistanceFromGoalLine(ball, dir);
            float homeDepth = PitchSpec.DistanceFromGoalLine(player.HomePosition, dir);

            // Slide with the ball, keep width and stagger the runs.
            target.X += (ball.X - target.X) * 0.22f;
            target.Z += dir * (ballDepth - 52.5f) * 0.3f;

            if (player.Role == PlayerRole.Striker || player.Role == PlayerRole.Winger)
            {
                // Run in behind when the ball is advanced. Stay onside.
                float stagger = (player.Number % 3) * 0.8f;
                float line = match.SecondLastDefenderDepth(Team.Side);
                float wanted = MathF.Min(ballDepth + 10f + stagger, line - 0.9f);
                wanted = MathF.Max(wanted, homeDepth);
                target.Z += dir * (wanted - PitchSpec.DistanceFromGoalLine(target, dir));
                if (ballDepth > 60f)
                {
                    float wide = player.Role == PlayerRole.Winger ? 6f : 0f;
                    target.X += MathF.Sign(player.HomePosition.X) * wide;
                }
            }
            else if (player.Role == PlayerRole.CentreMid || player.Role == PlayerRole.AttackingMid)
            {
                // Offer a passing lane behind the carrier.
                target.Z -= dir * 4f;
            }
            else if (player.Role == PlayerRole.FullBack || player.Role == PlayerRole.WingBack)
            {
                if (ballDepth > 62f && player.Number % 2 == 0)
                {
                    target.Z += dir * 9f; // overlap
                    target.X += MathF.Sign(player.HomePosition.X) * 5f;
                }
            }

            target = PitchSpec.ClampToPitch(target, 1.5f);
            return target;
        }

        private Vec3 DefendTarget(MatchEngine match, PlayerAgent player, Vec3 ball, float dir, bool isPresser)
        {
            Vec3 ownGoal = PitchSpec.DefendingGoalFor(dir);
            float ballDepth = PitchSpec.DistanceFromGoalLine(ball, dir);
            float blockDepth = Math.Clamp(ballDepth * 0.55f + 15f + (1f - Team.LineHeight) * 4f, 12f, 50f);
            Vec3 target = player.HomePosition;

            // Compact horizontally and squeeze vertically around the block depth.
            target.X = ownGoal.X + (target.X - ownGoal.X) * (0.82f + Team.PressIntensity * 0.12f);
            float homeDepth = PitchSpec.DistanceFromGoalLine(target, dir);
            float depth = MathF.Min(homeDepth, blockDepth + (player.Role == PlayerRole.Striker ? 12f : 4f));
            depth = MathF.Max(depth, 8f);
            target.Z = ownGoal.Z + dir * depth;
            target.Z += dir * (ballDepth - 52.5f) * 0.12f;

            if (isPresser)
            {
                target = ball;
            }
            return PitchSpec.ClampToPitch(target, 1.5f);
        }

        private Vec3 CoverTarget(MatchEngine match, Vec3 ball, float dir)
        {
            Vec3 ownGoal = PitchSpec.DefendingGoalFor(dir);
            Vec3 toGoal = (ownGoal - ball).Flat.Normalized;
            return PitchSpec.ClampToPitch(ball + toGoal * 5.5f, 1.5f);
        }

        private Vec3 MarkTarget(MatchEngine match, PlayerAgent player, Vec3 ball, List<PlayerAgent> opponents, float dir)
        {
            Vec3 ownGoal = PitchSpec.DefendingGoalFor(dir);
            PlayerAgent mark = null;
            float best = 14f;
            foreach (var opp in opponents)
            {
                if (opp.Role == PlayerRole.Goalkeeper) continue;
                float d = Vec3.FlatDistance(opp.Position, player.HomePosition);
                if (d < best)
                {
                    best = d;
                    mark = opp;
                }
            }

            if (mark == null) return DefendTarget(match, player, ball, dir, false);

            Vec3 goalSide = (ownGoal - mark.Position).Flat.Normalized;
            return PitchSpec.ClampToPitch(mark.Position + goalSide * 1.9f, 1.5f);
        }

        private void Carry(MatchEngine match, PlayerAgent carrier, List<PlayerAgent> opponents, float dir, float dt)
        {
            Vec3 goal = PitchSpec.AttackingGoalFor(dir);
            float goalDistance = PitchSpec.DistanceFromGoalLine(carrier.Position, dir);
            Vec3 toGoal = (goal - carrier.Position).Flat.Normalized;

            PlayerAgent nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var opp in opponents)
            {
                float d = Vec3.FlatDistance(opp.Position, carrier.Position);
                if (d < nearestDistance) { nearestDistance = d; nearest = opp; }
            }

            bool pressured = nearestDistance < 2.05f;
            bool inShootingRange = goalDistance < 24f + carrier.Shooting * 9f;
            Vec3 goalMouth = goal + new Vec3(
                (float)(_rng.NextDouble() * 2.0 - 1.0) * (PitchSpec.GoalHalfWidth - 0.6f),
                0f, 0f);
            float angle = MathF.Abs(Vec3.Dot(toGoal, (goalMouth - carrier.Position).Flat.Normalized));

            if (carrier.DecisionTimer <= 0f)
            {
                carrier.DecisionTimer = 0.22f + (float)_rng.NextDouble() * 0.22f;

                if (inShootingRange && angle > 0.55f && !(pressured && nearestDistance < 1.2f))
                {
                    float power = Math.Clamp(0.45f + carrier.Shooting * 0.4f + (float)_rng.NextDouble() * 0.12f, 0.35f, 1f);
                    match.Shoot(carrier, goalMouth, power, (float)(_rng.NextDouble() - 0.5) * 0.6f);
                    return;
                }

                if (pressured || (float)_rng.NextDouble() < 0.1f + Team.Tempo * 0.28f)
                {
                    if (TryChoosePass(match, carrier, opponents, dir, out var target, out var lofted, out var through))
                    {
                        match.ExecutePass(carrier, target, lofted, through);
                        return;
                    }
                }

                if (pressured && goalDistance > 70f && (float)_rng.NextDouble() < 0.5f)
                {
                    match.ClearBall(carrier, dir);
                    return;
                }
            }

            // Dribble towards goal, bending away from the nearest defender.
            Vec3 target2 = carrier.Position + toGoal * 8f;
            if (nearest != null && nearestDistance < 5f)
            {
                Vec3 away = (carrier.Position - nearest.Position).Flat.Normalized;
                target2 += away * (5f - nearestDistance) * 0.9f;
            }
            carrier.SteerTowards(target2, dt, 0.98f, true);
        }

        private bool TryChoosePass(MatchEngine match, PlayerAgent carrier, List<PlayerAgent> opponents, float dir,
            out Vec3 target, out bool lofted, out bool through)
        {
            target = Vec3.Zero;
            lofted = false;
            through = false;
            float bestScore = -1f;

            float carrierDepth = PitchSpec.DistanceFromGoalLine(carrier.Position, dir);
            foreach (var mate in Team.Players)
            {
                if (mate == carrier || mate.Role == PlayerRole.Goalkeeper) continue;
                if (mate.State == PlayerState.Down) continue;
                float distance = Vec3.FlatDistance(mate.Position, carrier.Position);
                if (distance < 4f || distance > 42f) continue;

                float mateDepth = PitchSpec.DistanceFromGoalLine(mate.Position, dir);
                float forwardGain = mateDepth - carrierDepth;
                float lane = LaneClearance(carrier.Position, mate.Position, opponents, out bool blocked);
                float score = forwardGain * 0.28f + lane * 1.7f - distance * 0.03f;
                if (mateDepth > match.SecondLastDefenderDepth(Team.Side) - 0.5f)
                {
                    // A runner beyond the line is the through ball option.
                    score += 0.9f;
                }
                if (blocked) score -= 1.2f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bool inBehind = mateDepth > match.SecondLastDefenderDepth(Team.Side) + 1.5f;
                    through = inBehind && forwardGain > 6f;
                    Vec3 lead = mate.Velocity * (through ? 0.55f : 0.28f);
                    target = mate.Position + lead;
                    target = PitchSpec.ClampToPitch(target, 2f);
                    lofted = blocked && distance > 16f;
                }
            }

            return bestScore > 0.15f;
        }

        private static float LaneClearance(Vec3 from, Vec3 to, List<PlayerAgent> opponents, out bool blocked)
        {
            float clearance = 30f;
            blocked = false;
            foreach (var opp in opponents)
            {
                float d = PitchMath.FlatSegmentDistance(opp.Position, from, to);
                if (d < clearance) clearance = d;
            }
            blocked = clearance < 1.4f;
            return MathF.Min(clearance, 6f) / 6f;
        }

        private void UpdateGoalkeeper(MatchEngine match, PlayerAgent gk, float dt)
        {
            if (gk == null || gk.State == PlayerState.Down) return;
            float dir = Team.AttackDir;
            Vec3 ownGoal = PitchSpec.DefendingGoalFor(dir);
            Vec3 ball = match.Ball.Position;
            float ballDistance = Vec3.FlatDistance(gk.Position, ball);

            if (match.GoalkeeperHasBall == gk)
            {
                match.GoalkeeperHoldTime += dt;
                gk.FaceTowards(ball, dt);
                if (match.GoalkeeperHoldTime > 1.1f)
                {
                    match.DistributeFromKeeper(gk);
                }
                return;
            }

            // Sweeper keeper: come out for balls in the area, otherwise cover the angle.
            float depth = PitchSpec.DistanceFromOwnGoal(ball, Team.Side);
            bool ballInOwnBox = PitchSpec.IsInPenaltyArea(ball, PitchSpec.Opponent(Team.Side)) &&
                                PitchSpec.DistanceFromGoalLine(ball, dir) < 17f;
            Vec3 target;
            if (ballInOwnBox && ballDistance < 16f)
            {
                target = ball;
            }
            else
            {
                Vec3 toBall = (ball - ownGoal).Flat.Normalized;
                float advanced = Math.Clamp(depth * 0.09f, 0.6f, 4.2f);
                target = ownGoal + toBall * advanced;
                target.X = Math.Clamp(target.X, -PitchSpec.GoalHalfWidth - 1.2f, PitchSpec.GoalHalfWidth + 1.2f);
            }

            // Shot stopping: dive at the predicted crossing point.
            Vec3 v = match.Ball.Velocity;
            bool shotAtGoal = v.FlatLength > 9f && Vec3.Dot((ownGoal - ball).Flat.Normalized, v.Flat.Normalized) > 0.55f;
            if (shotAtGoal && depth < 30f)
            {
                float timeToLine = MathF.Max(0.08f, (depth - 0.4f) / MathF.Max(1f, -Vec3.Dot(v.Flat, (ownGoal - ball).Flat.Normalized)));
                Vec3 predicted = ball + v * timeToLine;
                target = new Vec3(predicted.X, 0f, ownGoal.Z + dir * 0.6f);
                target.X = Math.Clamp(target.X, -PitchSpec.GoalHalfWidth - 0.4f, PitchSpec.GoalHalfWidth + 0.4f);
                if (ballDistance < 9f)
                {
                    gk.State = PlayerState.KeeperDive;
                    gk.ActionCooldown = 0.55f;
                }
            }

            gk.SteerTowards(target, dt, 1.2f, true);
            gk.FaceTowards(ball, dt);
        }
    }

    public enum MatchPhase
    {
        PreMatch,
        Intro,
        KickOff,
        InPlay,
        GoalCelebration,
        HalfTime,
        FullTime,
        ThrowIn,
        GoalKick,
        Corner,
        FreeKick,
        Penalty,
    }

    public enum RestartType { KickOff, ThrowIn, GoalKick, Corner, FreeKick, Penalty }
}
