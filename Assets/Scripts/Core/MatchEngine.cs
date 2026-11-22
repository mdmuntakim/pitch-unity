using System;
using System.Collections.Generic;

namespace Pitch.Core
{
    public sealed class MatchConfig
    {
        public string HomeFormation = "4-3-3";
        public string AwayFormation = "4-4-2";
        /// <summary>Real seconds in one half. The displayed clock still runs 45 minutes.</summary>
        public float HalfRealSeconds = 300f;
        public int HalfDisplayMinutes = 45;
        public int Seed = 20261122;
        public bool IntroEnabled = true;
        public bool SwapEndsAtHalfTime = true;
        public float RestartDelay = 1.3f;
        public float GoalCelebrationSeconds = 7f;
        public float HalfTimeSeconds = 4f;
        public int MaxSubstitutions = 5;
        public float UserAutoRestartSeconds = 6f;
    }

    public enum MatchEventType
    {
        Intro,
        KickOff,
        Goal,
        OwnGoal,
        Shot,
        Save,
        Post,
        Foul,
        YellowCard,
        RedCard,
        Offside,
        ThrowIn,
        GoalKick,
        Corner,
        FreeKick,
        Penalty,
        Substitution,
        HalfTime,
        FullTime,
        Celebration,
        Tifo,
        PossessionChange,
    }

    public struct MatchEvent
    {
        public MatchEventType Type;
        public TeamSide Side;
        public int Minute;
        public int PlayerNumber;
        public string Text;
        public Vec3 Position;
    }

    public sealed class MatchStats
    {
        public float HomePossessionSeconds;
        public float AwayPossessionSeconds;
        public int HomeShots;
        public int AwayShots;
        public int HomeShotsOnTarget;
        public int AwayShotsOnTarget;
        public int HomeFouls;
        public int AwayFouls;
        public int HomeCorners;
        public int AwayCorners;
        public int HomeOffsides;
        public int AwayOffsides;
        public int HomeSaves;
        public int AwaySaves;

        public float PossessionPercent(TeamSide side)
        {
            float total = HomePossessionSeconds + AwayPossessionSeconds;
            if (total < 0.001f) return 50f;
            return (side == TeamSide.Home ? HomePossessionSeconds : AwayPossessionSeconds) / total * 100f;
        }
    }

    public struct RestartState
    {
        public RestartType Type;
        public TeamSide Side;
        public Vec3 Position;
        public PlayerAgent Taker;
        public PlayerAgent Receiver;
        public float Timer;
        public float WaitTimer;
        public bool Ready;
        public bool Taken;
    }

    /// <summary>
    /// The match: laws of the game, clock, phases, set pieces, fouls, cards,
    /// offside, substitutions, statistics and every presentation trigger. Fixed
    /// 60 Hz step, render interpolation is the adapter's job.
    /// </summary>
    public sealed class MatchEngine
    {
        public const float FixedDt = 1f / 60f;

        public readonly MatchConfig Config;
        public readonly BallPhysics Ball = new BallPhysics();
        public readonly TeamState Home;
        public readonly TeamState Away;
        public readonly Referee Referee = new Referee();
        public readonly PresentationState Presentation = new PresentationState();
        public readonly IntroDirector Intro = new IntroDirector();
        public readonly ReplayBuffer Replay = new ReplayBuffer();
        public readonly ControlModel Controls = new ControlModel();
        public readonly MatchStats Stats = new MatchStats();
        public readonly List<MatchEvent> Events = new List<MatchEvent>();

        public MatchPhase Phase { get; private set; } = MatchPhase.PreMatch;
        public int Half { get; private set; } = 1;
        public float ClockSeconds { get; private set; }
        public float PhaseTime { get; private set; }
        public int StoppageMinutes { get; private set; }
        public int StoppageSeconds { get; private set; }
        public bool Paused { get; set; }

        public TeamSide? PossessionSide { get; private set; }
        public PlayerAgent Carrier { get; private set; }
        public PlayerAgent GoalkeeperHasBall { get; private set; }
        public float GoalkeeperHoldTime { get; set; }
        public PlayerAgent UserPlayer { get; private set; }
        public RestartState Restart;
        public PlayerAgent LastTouchPlayer { get; private set; }
        public TeamSide? LastTouchSide { get; private set; }
        public PlayerAgent PendingReceiver { get; private set; }
        public bool PendingOffside { get; private set; }
        public float OffsideTimer { get; private set; }
        public bool HomeUserControlled = true;
        public float MatchRealSeconds { get; private set; }
        public float InterpolationAlpha => Math.Clamp(_accumulator / FixedDt, 0f, 1f);
        public int FixedSteps { get; private set; }
        public int DebugShootCalls;
        public int DebugPassCalls;

        // Public hint for the renderer: which team owns the blue possession ring.
        public TeamSide? RingOwnerSide => Carrier != null && Carrier.Side == TeamSide.Home ? TeamSide.Home : null;

        private readonly TeamAI _homeAi;
        private readonly TeamAI _awayAi;
        private readonly Random _rng;
        private float _accumulator;
        private float _halfExtraRealSeconds;
        private float _ballFreeTimer;
        private PlayerAgent _ballKicker;
        private bool _kickOffHome;

        private static readonly string[] FirstNames =
        {
            "Aldo", "Bram", "Cato", "Dario", "Elias", "Fenn", "Goran", "Hugo", "Ilias", "Jonas",
            "Kian", "Lars", "Milo", "Nico", "Otto", "Pavel", "Quin", "Rui", "Sami", "Timo",
            "Ugo", "Viktor", "Wim", "Xavi", "Yann", "Zeno", "Arne", "Bruno", "Cyrus", "Dane",
        };

        private static readonly string[] LastNames =
        {
            "Aksoy", "Bakker", "Costa", "Duarte", "Engel", "Ferrer", "Gallo", "Horvat", "Ito", "Jansen",
            "Keller", "Lund", "Moreau", "Novak", "Okafor", "Pereira", "Rossi", "Silva", "Tavares", "Vidal",
        };

        public MatchEngine(MatchConfig config)
        {
            Config = config ?? new MatchConfig();
            _rng = new Random(Config.Seed);
            Home = BuildTeam(TeamSide.Home, "NORTHPORT", "NPT", Config.HomeFormation, 11);
            Away = BuildTeam(TeamSide.Away, "AURELIA", "AUR", Config.AwayFormation, 23);
            Home.AttackDir = 1f;
            Away.AttackDir = -1f;
            Home.RefreshHomePositions();
            Away.RefreshHomePositions();
            _homeAi = new TeamAI(Home, Config.Seed ^ 0x51ED);
            _awayAi = new TeamAI(Away, Config.Seed ^ 0x2B7F);

            Presentation.HomeName = Home.Name;
            Presentation.AwayName = Away.Name;
            Presentation.HomeAbbreviation = "NPT";
            Presentation.AwayAbbreviation = "AUR";
            Presentation.BallHighlight = Controls.Settings.BallHighlight;

            ResetForKickOff(TeamSide.Home);
            Phase = Config.IntroEnabled ? MatchPhase.Intro : MatchPhase.KickOff;
            if (Config.IntroEnabled)
            {
                Intro.Reset();
                PushEvent(MatchEventType.Intro, TeamSide.Home, "Trophy presentation", Vec3.Zero);
            }
            else
            {
                BeginKickOff(TeamSide.Home, ceremony: true);
            }
        }

        public TeamState Team(TeamSide side) => side == TeamSide.Home ? Home : Away;
        public TeamState Opponent(TeamSide side) => side == TeamSide.Home ? Away : Home;
        public float AttackDir(TeamSide side) => Team(side).AttackDir;
        public float DefendDir(TeamSide side) => -AttackDir(side);
        public Vec3 AttackingGoal(TeamSide side) => PitchSpec.AttackingGoalFor(AttackDir(side));
        public Vec3 DefendingGoal(TeamSide side) => PitchSpec.DefendingGoalFor(AttackDir(side));

        /// <summary>Depth (metres from the goal being attacked) of the second last defender.</summary>
        public float SecondLastDefenderDepth(TeamSide attackingSide)
        {
            float dir = AttackDir(attackingSide);
            var opponent = Opponent(attackingSide);
            var depths = new List<float>();
            foreach (var p in opponent.Players)
            {
                if (p.State == PlayerState.Down) continue;
                depths.Add(PitchSpec.DistanceFromGoalLine(p.Position, dir));
            }
            depths.Sort();
            return depths.Count >= 2 ? depths[1] : PitchSpec.HalfLength;
        }

        public void Advance(float deltaSeconds)
        {
            if (Paused) return;
            _accumulator += Math.Clamp(deltaSeconds, 0f, 0.25f);
            int guard = 0;
            while (_accumulator >= FixedDt && guard++ < 8)
            {
                _accumulator -= FixedDt;
                StepFixed();
            }
        }

        public void StepFixed()
        {
            if (Paused) return;
            const float dt = FixedDt;
            FixedSteps++;
            MatchRealSeconds += dt;
            foreach (var team in new[] { Home, Away })
                foreach (var p in team.Players)
                    p.PreviousPosition = p.Position;
            PhaseTime += dt;
            Presentation.Tick(dt);
            Controls.Tick(dt);
            OffsideTimer = MathF.Max(0f, OffsideTimer - dt);

            switch (Phase)
            {
                case MatchPhase.Intro:
                    Intro.Tick(dt);
                    if (Intro.Finished)
                    {
                        BeginKickOff(_kickOffHome ? TeamSide.Home : TeamSide.Away, ceremony: true);
                    }
                    break;
                case MatchPhase.KickOff:
                case MatchPhase.ThrowIn:
                case MatchPhase.GoalKick:
                case MatchPhase.Corner:
                case MatchPhase.FreeKick:
                case MatchPhase.Penalty:
                    StepRestart(dt);
                    break;
                case MatchPhase.InPlay:
                    StepPlay(dt);
                    break;
                case MatchPhase.GoalCelebration:
                    StepCelebration(dt);
                    break;
                case MatchPhase.HalfTime:
                    if (PhaseTime >= Config.HalfTimeSeconds) StartSecondHalf();
                    break;
                case MatchPhase.FullTime:
                    break;
            }

            UpdatePresentation();
        }

        private void StepPlay(float dt)
        {
            ClockSeconds += dt;
            if (Carrier != null)
            {
                if (Carrier.Side == TeamSide.Home) Stats.HomePossessionSeconds += dt;
                else Stats.AwayPossessionSeconds += dt;
            }

            UpdateUserControl(dt);
            _homeAi.Update(this, dt);
            _awayAi.Update(this, dt);

            // Keep a carrier only while he is actually near the ball.
            if (Carrier != null && Vec3.FlatDistance(Carrier.Position, Ball.Position) > 1.6f && Ball.Position.Y > 0.6f)
            {
                Carrier = null;
            }

            _ballFreeTimer = MathF.Max(0f, _ballFreeTimer - dt);

            if (GoalkeeperHasBall != null)
            {
                Ball.Position = GoalkeeperHasBall.Position + new Vec3(0f, 1.1f, 0f);
                Ball.Velocity = Vec3.Zero;
            }
            else
            {
                Ball.Step(dt);
            }

            HandlePlayerBallContact(dt);
            HandleGoalAndOut();
            CheckHalfEnd(dt);
            Replay.Sample(dt, Ball, new[] { Home, Away });

            if (Phase == MatchPhase.InPlay && Carrier == null && GoalkeeperHasBall == null)
            {
                TryPickUpLooseBall();
            }
        }

        private void UpdateUserControl(float dt)
        {
            if (!HomeUserControlled) return;

            if (Carrier != null && Carrier.Side == TeamSide.Home)
            {
                UserPlayer = Carrier;
            }
            else if (UserPlayer == null || UserPlayer.Side != TeamSide.Home || UserPlayer.State == PlayerState.Down ||
                     (Carrier != null && Carrier.Side == TeamSide.Away))
            {
                UserPlayer = NearestHomeOutfield(Ball.Position);
            }

            if (UserPlayer == null) return;
            foreach (var p in Home.Players) p.IsUserControlled = p == UserPlayer;

            bool hasBall = Carrier != null && Carrier.Side == TeamSide.Home && Carrier == UserPlayer;
            bool canShoot = hasBall && PitchSpec.DistanceFromGoalLine(UserPlayer.Position, Home.AttackDir) < 30f;
            bool wide = MathF.Abs(UserPlayer.Position.X) > PitchSpec.HalfWidth - 8f;
            Controls.SetContext(hasBall, canShoot, wide, Carrier != null && Carrier.Side == TeamSide.Away);

            if (hasBall)
            {
                Vec3 move = Controls.MoveInput;
                if (move.FlatLength > 0.05f)
                {
                    UserPlayer.MoveManual(move, dt, Controls.SprintHeld);
                }
                else
                {
                    UserPlayer.SteerTowards(UserPlayer.Position, dt);
                }
            }
            else if (!hasBall && Carrier == null)
            {
                // Manual pressing: the selected player chases the ball with the stick.
                Vec3 move = Controls.MoveInput;
                if (move.FlatLength > 0.05f)
                {
                    UserPlayer.MoveManual(move, dt, Controls.SprintHeld);
                }
            }
        }

        private PlayerAgent NearestHomeOutfield(Vec3 point)
        {
            PlayerAgent best = null;
            float bestDistance = float.MaxValue;
            foreach (var p in Home.Players)
            {
                if (p.Role == PlayerRole.Goalkeeper || p.State == PlayerState.Down) continue;
                float d = Vec3.FlatDistance(p.Position, point);
                if (d < bestDistance) { bestDistance = d; best = p; }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Controls
        // ------------------------------------------------------------------

        public void SubmitAction(ActionButton button)
        {
            if (Phase != MatchPhase.InPlay && Phase != MatchPhase.ThrowIn && Phase != MatchPhase.GoalKick &&
                Phase != MatchPhase.Corner && Phase != MatchPhase.FreeKick && Phase != MatchPhase.Penalty)
                return;

            if (Restart.Type == RestartType.Penalty && Phase == MatchPhase.Penalty && Restart.Side == TeamSide.Home)
            {
                TakePenalty();
                return;
            }

            var p = UserPlayer;
            bool hasBall = p != null && Carrier == p && Carrier.Side == TeamSide.Home;

            if (button == ActionButton.Shoot && hasBall)
            {
                float power = Controls.ConsumeShotCharge();
                Shoot(p, AimedShotTarget(p), power, Controls.MoveInput.X * 0.8f);
                return;
            }
            if (!hasBall)
            {
                if (button == ActionButton.PressureTackle || button == ActionButton.Contextual)
                {
                    var opponentCarrier = Carrier;
                    if (opponentCarrier != null && p != null && Vec3.FlatDistance(p.Position, opponentCarrier.Position) < 2f)
                        TryTackle(p, opponentCarrier);
                    else if (p != null)
                        p.SteerTowards(Ball.Position, FixedDt, 1.1f, true);
                }
                if (button == ActionButton.Contextual && Carrier == null && p != null)
                {
                    // switch to the player nearest the ball
                    UserPlayer = NearestHomeOutfield(Ball.Position);
                }
                return;
            }

            switch (button)
            {
                case ActionButton.Pass:
                    PassToBest(p, false, false);
                    break;
                case ActionButton.ThroughBall:
                    PassToBest(p, false, true);
                    break;
                case ActionButton.LobCross:
                    PassToBest(p, true, false);
                    break;
                case ActionButton.Contextual:
                    if (Controls.Contextual == ContextualAction.FinesseShot)
                    {
                        Shoot(p, AimedShotTarget(p), 0.62f, Controls.MoveInput.X * 1.2f);
                    }
                    else
                    {
                        PassToBest(p, false, false);
                    }
                    break;
            }
        }

        private Vec3 AimedShotTarget(PlayerAgent p)
        {
            Vec3 goal = AttackingGoal(p.Side);
            float dir = AttackDir(p.Side);
            float aim = Math.Clamp(Controls.MoveInput.X, -1f, 1f);
            return goal + new Vec3(aim * (PitchSpec.GoalHalfWidth - 0.45f), MathF.Max(0.3f, 0.6f), dir * 0.2f);
        }

        // ------------------------------------------------------------------
        // Passing / shooting / tackling
        // ------------------------------------------------------------------

        public void PassToBest(PlayerAgent from, bool lofted, bool through)
        {
            float dir = AttackDir(from.Side);
            PlayerAgent best = null;
            float bestScore = float.MinValue;
            Vec3 target = Vec3.Zero;

            foreach (var mate in Team(from.Side).Players)
            {
                if (mate == from || mate.Role == PlayerRole.Goalkeeper || mate.State == PlayerState.Down) continue;
                float d = Vec3.FlatDistance(from.Position, mate.Position);
                if (d < 3f || d > 46f) continue;
                float gain = PitchSpec.DistanceFromGoalLine(mate.Position, dir) - PitchSpec.DistanceFromGoalLine(from.Position, dir);
                float lane = 1f;
                foreach (var opp in Opponent(from.Side).Players)
                {
                    float laneDistance = PitchMath.FlatSegmentDistance(opp.Position, from.Position, mate.Position);
                    if (laneDistance < 1.2f) lane = 0.1f;
                    else if (laneDistance < 3f) lane = MathF.Min(lane, 0.6f);
                }
                float score = gain * 0.1f + lane * 1.4f - d * 0.02f;
                if (through && PitchSpec.DistanceFromGoalLine(mate.Position, dir) > SecondLastDefenderDepth(from.Side) + 2f) score += 1.6f;
                if (!through && gain > 25f) score -= 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = mate;
                    target = mate.Position + mate.Velocity * (through ? 0.9f : 0.25f);
                }
            }

            if (best == null)
            {
                ClearBall(from, dir);
                return;
            }

            ExecutePass(from, PitchSpec.ClampToPitch(target, 2f), lofted, through, best);
        }

        public void ExecutePass(PlayerAgent from, Vec3 target, bool lofted, bool through, PlayerAgent intendedReceiver = null)
        {
            if (from == null) return;

            Vec3 delta = target - Ball.Position;
            float distance = delta.FlatLength;
            float dir = AttackDir(from.Side);

            if (distance < 0.5f) { ClearBall(from, dir); return; }

            intendedReceiver ??= NearestTeammate(from, target);

            float accuracy = from.Passing * 0.09f;
            float error = (float)(_rng.NextDouble() - 0.5) * accuracy * MathF.Min(1f, distance / 20f);
            Vec3 right = new Vec3(1f, 0f, 0f);
            target += right * error;

            Vec3 flatDir = (target - Ball.Position).Flat.Normalized;
            float speed;
            Vec3 velocity;
            if (lofted || distance > 26f)
            {
                speed = Math.Clamp(9f + distance * 0.55f, 10f, 26f);
                float flightTime = MathF.Max(0.4f, distance / MathF.Max(6f, speed * 0.75f));
                float vy = 0.5f * -PitchSpec.Gravity * flightTime;
                velocity = flatDir * (distance / flightTime) + new Vec3(0f, MathF.Min(vy, 12f), 0f);
                if (distance < 12f)
                {
                    speed = Math.Clamp(8f + distance * 0.5f, 8f, 16f);
                    velocity = flatDir * speed + new Vec3(0f, 2.2f, 0f);
                }
            }
            else
            {
                speed = Math.Clamp(8f + distance * 0.42f, 8f, 24f);
                velocity = flatDir * speed + new Vec3(0f, 0.25f, 0f);
            }

            Ball.Position = Ball.Position.WithY(MathF.Max(Ball.Position.Y, PitchSpec.BallRadius));
            Vec3 spin = new Vec3(0f, (float)(_rng.NextDouble() - 0.5) * 6f, 0f);
            Ball.Kick(velocity, spin);
            DebugPassCalls++;
            _ballFreeTimer = 0.4f;
            _ballKicker = from;

            from.State = PlayerState.Pass;
            from.ActionCooldown = 0.22f;
            Carrier = null;
            GoalkeeperHasBall = null;
            GoalkeeperHoldTime = 0f;
            LastTouchPlayer = from;
            LastTouchSide = from.Side;
            PossessionSide = from.Side;
            PendingReceiver = intendedReceiver;

            if (intendedReceiver != null)
            {
                bool offside = Referee.IsOffside(intendedReceiver, from.Position, SecondLastDefenderDepth(from.Side), dir);
                PendingOffside = offside && !through;
                OffsideTimer = offside ? 3.5f : 0f;
            }
            else
            {
                PendingOffside = false;
            }
        }

        private PlayerAgent NearestTeammate(PlayerAgent from, Vec3 point)
        {
            PlayerAgent best = null;
            float bestDistance = float.MaxValue;
            foreach (var mate in Team(from.Side).Players)
            {
                if (mate == from || mate.Role == PlayerRole.Goalkeeper) continue;
                float d = Vec3.FlatDistance(mate.Position, point);
                if (d < bestDistance) { bestDistance = d; best = mate; }
            }
            return best;
        }

        public void Shoot(PlayerAgent shooter, Vec3 target, float power, float curve)
        {
            if (shooter == null) return;
            power = Math.Clamp(power, 0.15f, 1f);
            Vec3 goal = AttackingGoal(shooter.Side);
            Vec3 delta = target - shooter.Position;
            float distance = delta.FlatLength;
            float dir = AttackDir(shooter.Side);

            float speed = PitchMath.Lerp(15f, 31f, power);
            Vec3 flatDir = (target - shooter.Position).Flat.Normalized;
            float loft = distance > 18f ? MathF.Min(0.34f, distance / 70f) : 0.06f;
            Vec3 velocity = flatDir * speed + new Vec3(0f, speed * loft * (0.5f + power * 0.5f), 0f);
            Vec3 spin = new Vec3(0f, curve * 18f, 0f); // vertical axis spin bends the shot

            Ball.Position = shooter.Position + flatDir * 0.42f + new Vec3(0f, 0.22f, 0f);
            Ball.Kick(velocity, spin);
            _ballFreeTimer = 0.4f;
            _ballKicker = shooter;
            shooter.State = PlayerState.Shoot;
            shooter.ActionCooldown = 0.45f;
            shooter.FaceTowards(target, FixedDt, 20f);
            Carrier = null;
            GoalkeeperHasBall = null;
            GoalkeeperHoldTime = 0f;
            LastTouchPlayer = shooter;
            LastTouchSide = shooter.Side;
            PossessionSide = shooter.Side;
            PendingOffside = false;

            if (shooter.Side == TeamSide.Home) Stats.HomeShots++;
            else Stats.AwayShots++;

            if (Vec3.Dot((goal - shooter.Position).Flat.Normalized, flatDir) > 0.45f)
            {
                if (shooter.Side == TeamSide.Home) Stats.HomeShotsOnTarget++;
                else Stats.AwayShotsOnTarget++;
            }

            DebugShootCalls++;
            PushEvent(MatchEventType.Shot, shooter.Side, $"{shooter.Name} shoots", shooter.Position);
            Presentation.ShakeCamera(0.04f, 0.25f);
        }

        public void ClearBall(PlayerAgent from, float dir)
        {
            Vec3 target = new Vec3(
                (float)(_rng.NextDouble() * 2.0 - 1.0) * PitchSpec.HalfWidth * 0.6f,
                0f,
                from.Position.Z + dir * 35f);
            ExecutePass(from, target, true, false);
        }

        public void TryTackle(PlayerAgent defender, PlayerAgent carrier)
        {
            if (defender == null || carrier == null || defender.ActionCooldown > 0f || carrier.ActionCooldown > 0f) return;
            defender.ActionCooldown = 0.85f;
            defender.State = PlayerState.Tackle;

            float fromBehind = Vec3.Dot(Vec3.FromYaw(carrier.Facing), (defender.Position - carrier.Position).Flat.Normalized);
            bool behind = fromBehind < -0.25f;
            float winChance = 0.44f + defender.Tackling * 0.42f - carrier.Control * 0.36f;
            if (behind) winChance -= 0.12f;
            bool won = _rng.NextDouble() < Math.Clamp(winChance, 0.12f, 0.88f);

            var decision = Referee.JudgeTackle(defender, carrier, won, behind,
                PitchSpec.IsInPenaltyArea(carrier.Position, PitchSpec.Opponent(carrier.Side)));

            if (decision.IsFoul)
            {
                AwardFoul(carrier, defender, decision);
                return;
            }

            if (won)
            {
                Vec3 away = (carrier.Position - defender.Position).Flat.Normalized;
                Ball.Position = carrier.Position + new Vec3(0f, 0.25f, 0f);
                Ball.Kick(away * 3.5f + new Vec3(0f, 1.1f, 0f), new Vec3(0f, (float)_rng.NextDouble() * 4f, 0f));
                carrier.ActionCooldown = 0.55f;
                carrier.State = PlayerState.Idle;
                Carrier = null;
                PendingReceiver = defender;
                LastTouchPlayer = defender;
                LastTouchSide = defender.Side;
                PossessionSide = defender.Side;
                PushEvent(MatchEventType.PossessionChange, defender.Side, $"{defender.Name} wins the ball", defender.Position);
            }
            else
            {
                carrier.Stamina = MathF.Max(0f, carrier.Stamina - 1.5f);
            }
        }

        private void AwardFoul(PlayerAgent victim, PlayerAgent fouler, FoulDecision decision)
        {
            Carrier = null;
            PendingOffside = false;
            if (fouler.Side == TeamSide.Home) Stats.HomeFouls++;
            else Stats.AwayFouls++;

            PushEvent(MatchEventType.Foul, victim.Side, $"{decision.Reason} by {fouler.Name}", victim.Position);

            if (decision.Card != CardColour.None)
            {
                ApplyCard(fouler, decision.Card);
            }

            if (decision.IsPenalty)
            {
                PushEvent(MatchEventType.Penalty, victim.Side, "Penalty awarded", victim.Position);
                BeginRestart(RestartType.Penalty, victim.Side, PitchSpec.PenaltySpotPosition(PitchSpec.Opponent(victim.Side)));
            }
            else
            {
                PushEvent(MatchEventType.FreeKick, victim.Side, "Free kick", victim.Position);
                BeginRestart(RestartType.FreeKick, victim.Side, victim.Position);
            }
        }

        private void ApplyCard(PlayerAgent player, CardColour card)
        {
            var team = Team(player.Side);
            if (card == CardColour.Yellow)
            {
                if (team.Booked.Contains(player.Number))
                {
                    card = CardColour.Red;
                }
                else
                {
                    team.Booked.Add(player.Number);
                }
            }

            if (card == CardColour.Red)
            {
                player.State = PlayerState.Down;
                player.ActionCooldown = 60f;
                team.SentOff.Add(player);
                PushEvent(MatchEventType.RedCard, player.Side, $"Red card: {player.Name}", player.Position);
            }
            else
            {
                PushEvent(MatchEventType.YellowCard, player.Side, $"Yellow card: {player.Name}", player.Position);
            }
        }

        // ------------------------------------------------------------------
        // Ball contact, goals, out of play
        // ------------------------------------------------------------------

        private void HandlePlayerBallContact(float dt)
        {
            if (GoalkeeperHasBall != null) return;
            if (Ball.Position.Y > 2.1f) return;

            foreach (var team in new[] { Home, Away })
            {
                foreach (var p in team.Players)
                {
                    if (p.State == PlayerState.Down) continue;
                    if (p == _ballKicker && _ballFreeTimer > 0f) continue;

                    float speed = Ball.Velocity.FlatLength;
                    bool intended = PendingReceiver == p;
                    float contactRadius = PitchSpec.PlayerRadius + PitchSpec.BallRadius + 0.22f;
                    if (intended) contactRadius = 1.55f;
                    else if (speed < 8f) contactRadius = 1.25f;
                    else if (speed < 17f) contactRadius = 0.75f;

                    float d = Vec3.FlatDistance(p.Position, Ball.Position);
                    if (d > contactRadius) continue;

                    if (p.Role == PlayerRole.Goalkeeper)
                    {
                        if (TryKeeperSave(p)) return;
                    }

                    float trapChance = 0.5f + p.Control * 0.42f - speed * 0.012f;
                    bool canTrap = speed < 7f
                        || (intended && speed < 30f && _rng.NextDouble() < trapChance)
                        || (speed < 17f && _rng.NextDouble() < trapChance * 0.35f);

                    if (canTrap && p != Carrier)
                    {
                        if (PendingOffside && intended && p.Side == PossessionSide)
                        {
                            callOffside(p);
                            return;
                        }

                        // First touch: the ball is cushioned, not teleported.
                        Carrier = p;
                        PossessionSide = p.Side;
                        LastTouchPlayer = p;
                        LastTouchSide = p.Side;
                        PendingOffside = false;
                        PendingReceiver = null;
                        GoalkeeperHasBall = null;
                        Ball.Velocity *= 0.18f;
                        p.DecisionTimer = 0.12f;
                        p.ActionCooldown = MathF.Max(p.ActionCooldown, 0.08f);
                        if (p.Side == TeamSide.Home) Stats.HomePossessionSeconds += dt;
                        else Stats.AwayPossessionSeconds += dt;
                        break;
                    }

                    if (speed > 11f && Carrier != p)
                    {
                        // Deflection off the body.
                        Vec3 normal = (Ball.Position - p.Position).Flat.Normalized;
                        Ball.Velocity = PitchMath.Reflect(Ball.Velocity, normal) * 0.32f;
                        Ball.Velocity += p.Velocity * 0.2f;
                        LastTouchPlayer = p;
                        LastTouchSide = p.Side;
                        PendingOffside = false;
                        PendingReceiver = null;
                        continue;
                    }
                }
            }

            if (Carrier != null && _ballFreeTimer <= 0f)
            {
                Vec3 ahead = Vec3.FromYaw(Carrier.Facing) * 0.52f;
                Ball.Position = Vec3.Lerp(Ball.Position, Carrier.Position + ahead + new Vec3(0f, PitchSpec.BallRadius, 0f), 0.5f);
                Ball.Velocity = Carrier.Velocity;
            }
        }

        private void callOffside(PlayerAgent receiver)
        {
            if (receiver.Side == TeamSide.Home) Stats.HomeOffsides++;
            else Stats.AwayOffsides++;
            PushEvent(MatchEventType.Offside, PitchSpec.Opponent(receiver.Side), $"Offside: {receiver.Name}", receiver.Position);
            PendingOffside = false;
            PendingReceiver = null;
            Carrier = null;
            BeginRestart(RestartType.FreeKick, PitchSpec.Opponent(receiver.Side), receiver.Position);
        }

        private bool TryKeeperSave(PlayerAgent keeper)
        {
            if (keeper.ActionCooldown > 0.2f) return false;
            float speed = Ball.Velocity.Length;
            bool towardGoal = Vec3.Dot((DefendingGoal(keeper.Side) - keeper.Position).Flat.Normalized, Ball.Velocity.Flat.Normalized) > 0.2f;
            if (!towardGoal && speed < 6f) return false;

            float reach = 1.7f + keeper.Control * 0.7f;
            if (Vec3.FlatDistance(keeper.Position, Ball.Position) > reach) return false;

            keeper.State = PlayerState.KeeperDive;
            keeper.ActionCooldown = 0.7f;
            LastTouchPlayer = keeper;
            LastTouchSide = keeper.Side;
            PendingOffside = false;

            if (keeper.Side == TeamSide.Home) Stats.HomeSaves++;
            else Stats.AwaySaves++;

            bool caught = _rng.NextDouble() < 0.62 + keeper.Control * 0.2;
            if (caught)
            {
                GoalkeeperHasBall = keeper;
                GoalkeeperHoldTime = 0f;
                Carrier = null;
                Ball.Velocity = Vec3.Zero;
                PushEvent(MatchEventType.Save, keeper.Side, $"{keeper.Name} saves", keeper.Position);
            }
            else
            {
                Vec3 away = (Ball.Position - DefendingGoal(keeper.Side)).Flat.Normalized;
                away = new Vec3(away.X + (float)(_rng.NextDouble() - 0.5) * 0.9f, 0f, away.Z).Normalized;
                Ball.Kick(away * (7f + (float)_rng.NextDouble() * 5f) + new Vec3(0f, 2.4f, 0f), Vec3.Zero);
                Carrier = null;
                PushEvent(MatchEventType.Save, keeper.Side, $"{keeper.Name} parries", keeper.Position);
            }
            return true;
        }

        public void DistributeFromKeeper(PlayerAgent keeper)
        {
            GoalkeeperHasBall = null;
            GoalkeeperHoldTime = 0f;
            PassToBest(keeper, true, false);
        }

        private void TryPickUpLooseBall()
        {
            PlayerAgent best = null;
            float bestDistance = 1.8f;
            foreach (var team in new[] { Home, Away })
            {
                foreach (var p in team.Players)
                {
                    if (p.State == PlayerState.Down) continue;
                    float d = Vec3.FlatDistance(p.Position, Ball.Position);
                    if (d < bestDistance && Ball.Velocity.FlatLength < 9f) { bestDistance = d; best = p; }
                }
            }
            if (best == null) return;
            Carrier = best;
            PossessionSide = best.Side;
            LastTouchPlayer = best;
            LastTouchSide = best.Side;
            if (best.Side == TeamSide.Home) Stats.HomePossessionSeconds += FixedDt;
            else Stats.AwayPossessionSeconds += FixedDt;
        }

        private void HandleGoalAndOut()
        {
            Vec3 p = Ball.Position;

            // Goal at the home end?
            if ((Ball.PreviousPosition.Z > -PitchSpec.HalfLength && p.Z <= -PitchSpec.HalfLength) && IsInsideMouth(p))
            {
                ScoreGoal(TeamSide.Away);
                return;
            }
            if ((Ball.PreviousPosition.Z < PitchSpec.HalfLength && p.Z >= PitchSpec.HalfLength) && IsInsideMouth(p))
            {
                ScoreGoal(TeamSide.Home);
                return;
            }

            if (p.Z < -PitchSpec.HalfLength - PitchSpec.BallRadius)
            {
                BallOutBehindGoalLine(defendingSide: TeamSide.Home);
                return;
            }
            if (p.Z > PitchSpec.HalfLength + PitchSpec.BallRadius)
            {
                BallOutBehindGoalLine(defendingSide: TeamSide.Away);
                return;
            }
            if (MathF.Abs(p.X) > PitchSpec.HalfWidth + PitchSpec.BallRadius)
            {
                Vec3 outPoint = new Vec3(MathF.Sign(p.X) * PitchSpec.HalfWidth, 0f, Math.Clamp(p.Z, -PitchSpec.HalfLength, PitchSpec.HalfLength));
                TeamSide throwSide = LastTouchSide.HasValue ? PitchSpec.Opponent(LastTouchSide.Value) : TeamSide.Home;
                PushEvent(MatchEventType.ThrowIn, throwSide, "Throw in", outPoint);
                BeginRestart(RestartType.ThrowIn, throwSide, outPoint);
            }
        }

        private static bool IsInsideMouth(Vec3 p)
            => MathF.Abs(p.X) < PitchSpec.GoalHalfWidth - PitchSpec.BallRadius * 0.5f && p.Y < PitchSpec.GoalHeight;

        private void BallOutBehindGoalLine(TeamSide defendingSide)
        {
            Vec3 spot = new Vec3(
                Math.Clamp(Ball.Position.X, -PitchSpec.GoalHalfWidth - 0.5f, PitchSpec.GoalHalfWidth + 0.5f),
                0f,
                PitchSpec.GoalLineZ(defendingSide));
            bool lastTouchDefender = LastTouchSide == defendingSide;
            if (lastTouchDefender)
            {
                Vec3 corner = new Vec3(MathF.Sign(Ball.Position.X) * PitchSpec.HalfWidth, 0f, PitchSpec.GoalLineZ(defendingSide));
                if (defendingSide == TeamSide.Away) Stats.HomeCorners++;
                else Stats.AwayCorners++;
                PushEvent(MatchEventType.Corner, PitchSpec.Opponent(defendingSide), "Corner", corner);
                BeginRestart(RestartType.Corner, PitchSpec.Opponent(defendingSide), corner);
            }
            else
            {
                PushEvent(MatchEventType.GoalKick, defendingSide, "Goal kick", spot);
                BeginRestart(RestartType.GoalKick, defendingSide, spot);
            }
        }

        private void ScoreGoal(TeamSide scoringSide)
        {
            var team = Team(scoringSide);
            var scorer = LastTouchPlayer;
            bool ownGoal = LastTouchSide.HasValue && LastTouchSide.Value != scoringSide;

            team.Score++;
            Presentation.HomeScore = Home.Score;
            Presentation.AwayScore = Away.Score;
            Presentation.Celebration = scorer?.Celebration ?? CelebrateStyle.TeamHuddle;
            Presentation.CelebrationTimer = Config.GoalCelebrationSeconds;
            Presentation.ShakeCamera(0.12f, 0.9f);
            Presentation.PushLowerThird("GOAL", $"{scorer?.Name ?? "Unknown"}  {Presentation.ClockLine}", 5f);
            Presentation.LastEvent = $"GOAL {team.Name}";

            PushEvent(ownGoal ? MatchEventType.OwnGoal : MatchEventType.Goal, scoringSide,
                $"{(ownGoal ? "Own goal" : "Goal")} {scorer?.Name}", Ball.Position);
            PushEvent(MatchEventType.Celebration, scoringSide, "Celebration", scorer?.Position ?? Ball.Position);

            if (scorer != null)
            {
                scorer.State = PlayerState.Celebrate;
                scorer.ActionCooldown = 2.5f;
            }

            Phase = MatchPhase.GoalCelebration;
            PhaseTime = 0f;
            Carrier = null;
            GoalkeeperHasBall = null;
            Ball.Velocity = Vec3.Zero;
            _kickOffHome = scoringSide == TeamSide.Away;
        }

        private void StepCelebration(float dt)
        {
            if (PhaseTime >= Config.GoalCelebrationSeconds)
            {
                foreach (var team in new[] { Home, Away })
                    foreach (var p in team.Players)
                        if (p.State == PlayerState.Celebrate) p.State = PlayerState.Idle;
                Presentation.Celebration = CelebrateStyle.None;
                BeginKickOff(_kickOffHome ? TeamSide.Home : TeamSide.Away);
            }
        }

        // ------------------------------------------------------------------
        // Restarts
        // ------------------------------------------------------------------

        private void BeginKickOff(TeamSide side, bool ceremony = false)
        {
            ResetForKickOff(side);
            Phase = MatchPhase.KickOff;
            Restart = new RestartState
            {
                Type = RestartType.KickOff,
                Side = side,
                Position = Vec3.Zero,
                Taker = Team(side).Players.Find(p => p.Role == PlayerRole.Striker) ?? Team(side).Players[9],
                Ready = false,
                Timer = Config.RestartDelay,
                WaitTimer = 0f,
            };
            Ball.Reset(new Vec3(0f, PitchSpec.BallRadius, 0f));
            Presentation.PhaseLabel = Half == 1 ? "KICK OFF" : "SECOND HALF";
            if (ceremony)
            {
                Presentation.Tifo = TifoPhase.Unfurl;
                Presentation.TifoProgress = 0f;
                PushEvent(MatchEventType.Tifo, TeamSide.Home, "Tifo unfurls", Vec3.Zero);
            }
            PushEvent(MatchEventType.KickOff, side,
                Half == 1 ? "Kick off" : (ceremony ? "Second half" : "Restart"), Vec3.Zero);
        }

        private void ResetForKickOff(TeamSide kickingSide)
        {
            Home.RefreshHomePositions();
            Away.RefreshHomePositions();

            foreach (var team in new[] { Home, Away })
            {
                Vec3 ownGoal = DefendingGoal(team.Side);
                foreach (var p in team.Players)
                {
                    Vec3 pos = p.HomePosition;
                    if (p.Role == PlayerRole.Goalkeeper)
                        pos = ownGoal + new Vec3(0f, 0f, team.Side == TeamSide.Home ? 2.4f : -2.4f);
                    if (PitchSpec.DistanceFromGoalLine(pos, team.AttackDir) > PitchSpec.HalfLength - 1f)
                        pos = new Vec3(pos.X, 0f, ownGoal.Z + team.AttackDir * (PitchSpec.HalfLength - 2f));
                    pos = PitchSpec.ClampToPitch(pos, 1.5f);
                    // Every player must be in his own half at kick off.
                    if (pos.Z * team.AttackDir > -0.4f)
                        pos.Z = -team.AttackDir * 0.6f;
                    p.ResetForKickOff(pos);
                    p.IsUserControlled = false;
                }
            }

            // Kicking team has two players in the circle, the other team stays out.
            var takers = Team(kickingSide);
            var striker = takers.Players.Find(p => p.Role == PlayerRole.Striker) ?? takers.Players[9];
            striker.Position = new Vec3(-0.8f, 0f, -0.6f * AttackDir(kickingSide));
            var support = takers.Players.Find(p => p.Role == PlayerRole.AttackingMid || p.Role == PlayerRole.CentreMid) ?? takers.Players[7];
            support.Position = new Vec3(1.4f, 0f, -2.2f * AttackDir(kickingSide));
            foreach (var p in Opponent(kickingSide).Players)
            {
                if (p.Role == PlayerRole.Goalkeeper) continue;
                if (Vec3.FlatDistance(p.Position, Vec3.Zero) < PitchSpec.CentreCircleRadius + 0.5f)
                {
                    Vec3 away = p.Position.Flat.Normalized;
                    if (away.Length < 1e-3f) away = new Vec3(0f, 0f, -AttackDir(kickingSide));
                    p.Position = away * (PitchSpec.CentreCircleRadius + 1.2f);
                }
            }
        }

        public void BeginRestart(RestartType type, TeamSide side, Vec3 position)
        {
            Phase = type switch
            {
                RestartType.KickOff => MatchPhase.KickOff,
                RestartType.ThrowIn => MatchPhase.ThrowIn,
                RestartType.GoalKick => MatchPhase.GoalKick,
                RestartType.Corner => MatchPhase.Corner,
                RestartType.FreeKick => MatchPhase.FreeKick,
                RestartType.Penalty => MatchPhase.Penalty,
                _ => MatchPhase.InPlay,
            };

            position = PitchSpec.ClampToPitch(position, 0.5f);
            Ball.Reset(position.WithY(PitchSpec.BallRadius));
            Carrier = null;
            GoalkeeperHasBall = null;
            PendingReceiver = null;
            PendingOffside = false;
            PhaseTime = 0f;

            PlayerAgent taker = PickTaker(type, side, position);
            Restart = new RestartState
            {
                Type = type,
                Side = side,
                Position = position,
                Taker = taker,
                Timer = Config.RestartDelay,
                WaitTimer = 0f,
                Ready = false,
            };

            SetupRestartPositions(type, side, position, taker);
            Presentation.PhaseLabel = type switch
            {
                RestartType.ThrowIn => "THROW IN",
                RestartType.GoalKick => "GOAL KICK",
                RestartType.Corner => "CORNER",
                RestartType.FreeKick => "FREE KICK",
                RestartType.Penalty => "PENALTY",
                _ => "RESTART",
            };
            if (type == RestartType.Corner) Presentation.PushLowerThird("CORNER", Team(side).Name, 2.5f);
            if (type == RestartType.Penalty) Presentation.PushLowerThird("PENALTY", Team(side).Name, 3f);
        }

        private PlayerAgent PickTaker(RestartType type, TeamSide side, Vec3 position)
        {
            var team = Team(side);
            if (type == RestartType.GoalKick) return team.Goalkeeper;
            if (type == RestartType.Penalty)
            {
                // Best finisher takes it.
                PlayerAgent best = null;
                float bestShooting = -1f;
                foreach (var p in team.Players)
                {
                    if (p.Role == PlayerRole.Goalkeeper) continue;
                    if (p.Shooting > bestShooting) { bestShooting = p.Shooting; best = p; }
                }
                return best;
            }

            PlayerAgent nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var p in team.Players)
            {
                if (p.Role == PlayerRole.Goalkeeper) continue;
                float d = Vec3.FlatDistance(p.Position, position);
                if (d < nearestDistance) { nearestDistance = d; nearest = p; }
            }
            return nearest;
        }

        private void SetupRestartPositions(RestartType type, TeamSide side, Vec3 position, PlayerAgent taker)
        {
            float dir = AttackDir(side);
            var attacking = Team(side);
            var defending = Opponent(side);

            foreach (var p in attacking.Players)
            {
                if (p == taker) continue;
                Vec3 target = p.HomePosition;
                if (type == RestartType.GoalKick)
                {
                    target = new Vec3(p.HomePosition.X * 0.9f, 0f, DefendingGoal(side).Z + dir * (28f + p.SlotZ * 22f));
                }
                else if (type == RestartType.Corner)
                {
                    bool inBox = p.Role == PlayerRole.Striker || p.Role == PlayerRole.CentreBack || p.Role == PlayerRole.AttackingMid;
                    target = inBox
                        ? new Vec3((float)(_rng.NextDouble() * 2 - 1) * 8f, 0f, position.Z - dir * (6f + (float)_rng.NextDouble() * 6f))
                        : new Vec3(p.HomePosition.X * 0.55f, 0f, DefendingGoal(side).Z + dir * 32f);
                }
                else if (type == RestartType.Penalty)
                {
                    target = new Vec3(p.HomePosition.X * 0.7f, 0f, position.Z - dir * (9f + p.SlotZ * 8f));
                }
                else if (type == RestartType.KickOff)
                {
                    target = p.HomePosition;
                }
                p.Position = PitchSpec.ClampToPitch(target, 1f);
                p.Velocity = Vec3.Zero;
                p.State = PlayerState.Idle;
            }

            // Keep defenders the required distance from the ball.
            float exclusion = type == RestartType.KickOff ? PitchSpec.CentreCircleRadius
                : type == RestartType.Corner ? 9.15f
                : type == RestartType.Penalty ? 9.15f
                : 9.15f;
            foreach (var p in defending.Players)
            {
                Vec3 target = p.HomePosition;
                if (type == RestartType.GoalKick)
                    target = new Vec3(p.HomePosition.X, 0f, PitchSpec.GoalLineZ(p.Side) - dir * (35f + p.SlotZ * 20f));
                if (type == RestartType.Penalty)
                {
                    target = p.Role == PlayerRole.Goalkeeper
                        ? new Vec3(0f, 0f, PitchSpec.GoalLineZ(p.Side))
                        : new Vec3(p.HomePosition.X * 0.7f, 0f, position.Z - dir * (11f + p.SlotZ * 8f));
                }

                if (p.Role != PlayerRole.Goalkeeper && Vec3.FlatDistance(target, position) < exclusion)
                {
                    Vec3 away = (target - position).Flat;
                    if (away.Length < 1e-3f) away = new Vec3(0f, 0f, -dir);
                    target = position + away.Normalized * (exclusion + 0.5f);
                }
                p.Position = PitchSpec.ClampToPitch(target, 1f);
                p.Velocity = Vec3.Zero;
                p.State = PlayerState.Idle;
            }

            if (taker != null)
            {
                Vec3 behind = new Vec3(0f, 0f, -dir * (type == RestartType.Penalty ? 2.5f : 0.9f));
                if (type == RestartType.ThrowIn) behind = new Vec3(MathF.Sign(position.X) * 0.7f, 0f, 0f);
                taker.Position = PitchSpec.ClampToPitch(position + behind, 1f);
                taker.Velocity = Vec3.Zero;
                taker.FaceTowards(position, FixedDt, 30f);
                taker.State = PlayerState.Idle;
            }

            if (type == RestartType.KickOff)
            {
                Restart = new RestartState
                {
                    Type = type, Side = side, Position = position, Taker = taker,
                    Timer = Config.RestartDelay, WaitTimer = 0f, Ready = false,
                };
            }
        }

        private void StepRestart(float dt)
        {
            if (Restart.Taker == null)
            {
                Restart.Timer -= dt;
                if (Restart.Timer <= 0f) TakeRestart();
                return;
            }

            // Players settle into the restart shape, the taker walks to the ball.
            foreach (var team in new[] { Home, Away })
                foreach (var p in team.Players)
                {
                    if (p == Restart.Taker) continue;
                    p.SteerTowards(p.HomePosition, dt, 0.55f, false);
                    p.TickTimers(dt);
                }

            Restart.Taker.FaceTowards(Restart.Position, dt, 12f);
            Restart.Timer -= dt;

            bool userRestart = HomeUserControlled && Restart.Side == TeamSide.Home;
            if (userRestart && Restart.Side == TeamSide.Home)
            {
                // Give the user a moment to aim, then auto-take so the match never stalls.
                Restart.WaitTimer += dt;
                if (Restart.WaitTimer > Config.UserAutoRestartSeconds)
                    TakeRestart();
            }
            else if (Restart.Timer <= 0f)
            {
                TakeRestart();
            }

            if (OffsideTimer > 0f && PendingOffside)
            {
                OffsideTimer -= dt;
                if (OffsideTimer <= 0f) PendingOffside = false;
            }
        }

        private void TakeRestart()
        {
            if (Restart.Taken) return;
            Restart.Taken = true;
            var taker = Restart.Taker;
            var side = Restart.Side;
            float dir = AttackDir(side);
            Vec3 goal = AttackingGoal(side);

            if (taker == null)
            {
                BeginKickOff(side);
                return;
            }

            switch (Restart.Type)
            {
                case RestartType.KickOff:
                {
                    var mate = Team(side).Players.Find(p => p != taker && p.Role != PlayerRole.Goalkeeper);
                    ExecutePass(taker, mate.Position, false, false, mate);
                    break;
                }
                case RestartType.ThrowIn:
                {
                    var mate = NearestTeammate(taker, new Vec3(0f, 0f, taker.Position.Z + dir * 12f));
                    Vec3 target = mate != null ? mate.Position : new Vec3(0f, 0f, taker.Position.Z + dir * 15f);
                    ExecutePass(taker, target, true, false, mate);
                    break;
                }
                case RestartType.GoalKick:
                {
                    var mate = NearestTeammate(taker, new Vec3(0f, 0f, taker.Position.Z + dir * 40f));
                    Vec3 target = mate != null ? mate.Position : new Vec3(0f, 0f, taker.Position.Z + dir * 45f);
                    ExecutePass(taker, target, true, false, mate);
                    break;
                }
                case RestartType.Corner:
                {
                    var target = new Vec3(
                        (float)(_rng.NextDouble() * 2 - 1) * 6f,
                        0f,
                        goal.Z - dir * (6f + (float)_rng.NextDouble() * 4f));
                    ExecutePass(taker, target, true, false, NearestTeammate(taker, target));
                    break;
                }
                case RestartType.FreeKick:
                {
                    float distance = PitchSpec.DistanceFromGoalLine(taker.Position, dir);
                    if (distance < 26f && Vec3.Dot((goal - taker.Position).Flat.Normalized, Vec3.FromYaw(taker.Facing)) > 0.2f)
                    {
                        Shoot(taker, goal + new Vec3((float)(_rng.NextDouble() * 2 - 1) * 2.4f, 0.5f, 0f), 0.78f, (float)(_rng.NextDouble() - 0.5));
                    }
                    else
                    {
                        PassToBest(taker, distance > 35f, false);
                    }
                    break;
                }
                case RestartType.Penalty:
                    TakePenalty();
                    return;
            }

            Phase = MatchPhase.InPlay;
            PhaseTime = 0f;
        }

        private void TakePenalty()
        {
            var taker = Restart.Taker;
            if (taker == null) return;
            float side = AttackDir(taker.Side);
            Vec3 goal = AttackingGoal(taker.Side);
            Vec3 aim = goal + new Vec3(
                (float)(_rng.NextDouble() * 2 - 1) * (PitchSpec.GoalHalfWidth - 0.5f),
                0.7f,
                0f);

            // The keeper guesses a side.
            var keeper = Opponent(taker.Side).Goalkeeper;
            if (keeper != null)
            {
                float guess = (float)(_rng.NextDouble() * 2 - 1) * 2.8f;
                keeper.Position = new Vec3(guess, 0f, PitchSpec.GoalLineZ(PitchSpec.Opponent(taker.Side)));
                keeper.State = PlayerState.KeeperDive;
            }
            Shoot(taker, aim, 0.82f, 0f);
            Phase = MatchPhase.InPlay;
            PhaseTime = 0f;
        }

        // ------------------------------------------------------------------
        // Clock and halves
        // ------------------------------------------------------------------

        private void CheckHalfEnd(float dt)
        {
            float halfLength = Config.HalfRealSeconds;
            if (ClockSeconds < halfLength) return;

            if (_halfExtraRealSeconds <= 0f && StoppageMinutes == 0)
            {
                StoppageMinutes = 1 + _rng.Next(0, 4);
                _halfExtraRealSeconds = StoppageMinutes * (Config.HalfRealSeconds / MathF.Max(1f, Config.HalfDisplayMinutes));
                Presentation.ShowStoppage = true;
                Presentation.StoppageMinutes = StoppageMinutes;
            }

            if (ClockSeconds < halfLength + _halfExtraRealSeconds) return;
            if (Carrier != null && Phase == MatchPhase.InPlay)
            {
                // Let the current attack finish, but never more than 10 real seconds.
                if (PhaseTime < 10f) return;
            }
            EndHalf();
        }

        private void EndHalf()
        {
            if (Half == 1)
            {
                Phase = MatchPhase.HalfTime;
                PhaseTime = 0f;
                PushEvent(MatchEventType.HalfTime, TeamSide.Home, "Half time", Vec3.Zero);
                Presentation.PhaseLabel = "HALF TIME";
                Presentation.ShowStoppage = false;
            }
            else
            {
                Phase = MatchPhase.FullTime;
                PhaseTime = 0f;
                PushEvent(MatchEventType.FullTime, TeamSide.Home, "Full time", Vec3.Zero);
                Presentation.PhaseLabel = "FULL TIME";
                Presentation.ShowStoppage = false;
                Carrier = null;
                Ball.Velocity = Vec3.Zero;
            }
        }

        private void StartSecondHalf()
        {
            Half = 2;
            ClockSeconds = 0f;
            StoppageMinutes = 0;
            _halfExtraRealSeconds = 0f;
            Presentation.ShowStoppage = false;
            PhaseTime = 0f;

            if (Config.SwapEndsAtHalfTime)
            {
                Home.AttackDir *= -1f;
                Away.AttackDir *= -1f;
                foreach (var team in new[] { Home, Away })
                {
                    foreach (var p in team.Players)
                    {
                        p.Position = new Vec3(p.Position.X, 0f, -p.Position.Z);
                        p.Velocity = Vec3.Zero;
                    }
                    team.RefreshHomePositions();
                }
            }

            Replay.Clear();
            BeginKickOff(TeamSide.Away, ceremony: true);
            Presentation.PhaseLabel = "SECOND HALF";
            Presentation.Tifo = TifoPhase.Mosaic;
            Presentation.TifoProgress = 0f;
            PushEvent(MatchEventType.Tifo, TeamSide.Away, "Second half tifo", Vec3.Zero);
        }

        // ------------------------------------------------------------------
        // Substitutions and helpers
        // ------------------------------------------------------------------

        public bool Substitute(TeamSide side, int outNumber, int benchIndex)
        {
            var team = Team(side);
            if (team.SubstitutionsLeft <= 0 || benchIndex < 0 || benchIndex >= team.Bench.Count) return false;
            var off = team.Players.Find(p => p.Number == outNumber);
            if (off == null || off.Role == PlayerRole.Goalkeeper) return false;
            var on = team.Bench[benchIndex];
            team.Bench.RemoveAt(benchIndex);
            on.Position = off.Position;
            on.SlotX = off.SlotX;
            on.SlotZ = off.SlotZ;
            on.Role = off.Role;
            on.HomePosition = off.HomePosition;
            on.Side = side;
            on.IsUserControlled = false;
            int index = team.Players.IndexOf(off);
            team.Players[index] = on;
            team.SubstitutionsLeft--;
            if (Carrier == off) Carrier = on;
            if (UserPlayer == off) UserPlayer = on;
            PushEvent(MatchEventType.Substitution, side, $"{on.Name} on for {off.Name}", off.Position);
            Presentation.PushLowerThird("SUBSTITUTION", $"{on.Name}  for  {off.Name}", 3f);
            return true;
        }

        /// <summary>Test/dev hook: jump straight into open play from the centre spot.</summary>
        public void TestStartPlay()
        {
            Phase = MatchPhase.InPlay;
            PhaseTime = 0f;
            ClockSeconds = 0f;
            Ball.Reset(new Vec3(0f, PitchSpec.BallRadius, 0f));
            Carrier = null;
            GoalkeeperHasBall = null;
        }

        public void TestPlaceBall(Vec3 position, Vec3 velocity)
        {
            Ball.Position = position;
            Ball.PreviousPosition = position;
            Ball.Velocity = velocity;
            Ball.Spin = Vec3.Zero;
            Carrier = null;
            GoalkeeperHasBall = null;
        }

        public void TestSetLastTouch(TeamSide? side, PlayerAgent player = null)
        {
            LastTouchSide = side;
            LastTouchPlayer = player;
        }

        public void TestSetCarrier(PlayerAgent player)
        {
            Carrier = player;
            PossessionSide = player?.Side;
        }

        public void SkipIntro()
        {
            if (Phase != MatchPhase.Intro) return;
            Intro.Skip();
            BeginKickOff(_kickOffHome ? TeamSide.Home : TeamSide.Away);
        }

        public void SetUserControlled(bool home)
        {
            HomeUserControlled = home;
        }

        public void PushEvent(MatchEventType type, TeamSide side, string text, Vec3 position)
        {
            Events.Add(new MatchEvent
            {
                Type = type,
                Side = side,
                Minute = DisplayMinute,
                PlayerNumber = LastTouchPlayer?.Number ?? 0,
                Text = text,
                Position = position,
            });
        }

        public List<MatchEvent> DrainEvents()
        {
            var copy = new List<MatchEvent>(Events);
            Events.Clear();
            return copy;
        }

        public int DisplayMinute
        {
            get
            {
                float halfProgress = PitchMath.Clamp01(ClockSeconds / MathF.Max(1f, Config.HalfRealSeconds));
                int minute = (int)(halfProgress * Config.HalfDisplayMinutes);
                return (Half - 1) * Config.HalfDisplayMinutes + Math.Min(minute, Config.HalfDisplayMinutes);
            }
        }

        public int DisplaySecond
        {
            get
            {
                float halfProgress = PitchMath.Clamp01(ClockSeconds / MathF.Max(1f, Config.HalfRealSeconds));
                float inMinute = halfProgress * Config.HalfDisplayMinutes * 60f;
                return (int)(inMinute % 60f);
            }
        }

        private void UpdatePresentation()
        {
            Presentation.HomeScore = Home.Score;
            Presentation.AwayScore = Away.Score;
            Presentation.DisplayMinute = DisplayMinute;
            Presentation.DisplaySecond = DisplaySecond;
            Presentation.PossessionLabel = PossessionSide.HasValue ? Team(PossessionSide.Value).Name : "";

            if (Phase == MatchPhase.InPlay || Phase == MatchPhase.Intro)
            {
                if (Presentation.Tifo == TifoPhase.Unfurl && Presentation.TifoProgress >= 1f)
                    Presentation.Tifo = TifoPhase.Mosaic;
                if (Presentation.Tifo == TifoPhase.Mosaic && PhaseTime > 12f)
                    Presentation.Tifo = TifoPhase.Chant;
            }
            else if (Phase == MatchPhase.HalfTime || Phase == MatchPhase.FullTime)
            {
                Presentation.Tifo = TifoPhase.Idle;
            }
        }

        // ------------------------------------------------------------------
        // Teams
        // ------------------------------------------------------------------

        private TeamState BuildTeam(TeamSide side, string name, string abbreviation, string formation, int seed)
        {
            var random = new Random(Config.Seed + seed);
            var team = new TeamState { Side = side, Name = name, FormationName = formation };
            for (int i = 0; i < 11; i++)
            {
                int number = i + 1;
                var player = new PlayerAgent
                {
                    Id = (int)side * 100 + number,
                    Side = side,
                    Number = number,
                    Name = $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}",
                    Pace = 0.55f + (float)random.NextDouble() * 0.4f,
                    Acceleration = 0.55f + (float)random.NextDouble() * 0.4f,
                    Control = 0.55f + (float)random.NextDouble() * 0.4f,
                    Passing = 0.55f + (float)random.NextDouble() * 0.4f,
                    Shooting = 0.45f + (float)random.NextDouble() * 0.5f,
                    Tackling = 0.5f + (float)random.NextDouble() * 0.45f,
                    StaminaRating = 0.55f + (float)random.NextDouble() * 0.4f,
                    Aggression = 0.3f + (float)random.NextDouble() * 0.5f,
                    Celebration = (CelebrateStyle)(1 + random.Next(5)),
                };
                team.Players.Add(player);
            }

            team.Shape = Formations.Get(formation);
            for (int i = 0; i < 11; i++)
            {
                team.Players[i].Role = team.Shape[i].Role;
                team.Players[i].SlotX = team.Shape[i].X;
                team.Players[i].SlotZ = team.Shape[i].Z;
            }
            team.AttackDir = side == TeamSide.Home ? 1f : -1f;
            team.ApplyFormation(formation);

            // Bench of seven, original names and attributes.
            for (int i = 0; i < 7; i++)
            {
                team.Bench.Add(new PlayerAgent
                {
                    Id = (int)side * 100 + 20 + i,
                    Side = side,
                    Number = 12 + i,
                    Name = $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}",
                    Pace = 0.55f + (float)random.NextDouble() * 0.4f,
                    Acceleration = 0.55f + (float)random.NextDouble() * 0.4f,
                    Control = 0.55f + (float)random.NextDouble() * 0.4f,
                    Passing = 0.55f + (float)random.NextDouble() * 0.4f,
                    Shooting = 0.45f + (float)random.NextDouble() * 0.5f,
                    Tackling = 0.5f + (float)random.NextDouble() * 0.45f,
                    StaminaRating = 0.55f + (float)random.NextDouble() * 0.4f,
                    Celebration = (CelebrateStyle)(1 + random.Next(5)),
                });
            }
            return team;
        }
    }
}
