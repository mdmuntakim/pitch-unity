using System;
using System.Collections.Generic;
using System.Linq;
using Pitch.Core;

namespace Pitch.Core.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;
        private static readonly List<string> Failures = new List<string>();

        private static void Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                _passed++;
            }
            else
            {
                _failed++;
                Failures.Add($"{name} {detail}".Trim());
                Console.WriteLine($"FAIL  {name} {detail}");
            }
        }

        private static void Near(string name, float actual, float expected, float tolerance)
            => Check(name, MathF.Abs(actual - expected) <= tolerance, $"expected {expected} +/- {tolerance}, got {actual}");

        private static int Main()
        {
            TestMath();
            TestPitchSpec();
            TestBall();
            TestFormations();
            TestReferee();
            TestControls();
            TestQuality();
            TestReplay();
            TestIntro();
            TestMatchFlow();
            TestRestarts();
            TestSubstitutions();
            TestPresentation();
            TestDeterminism();
            TestLongSimulation();

            Console.WriteLine();
            Console.WriteLine($"{_passed} checks passed, {_failed} failed");
            if (Failures.Count > 0)
            {
                Console.WriteLine("Failures:");
                foreach (var failure in Failures) Console.WriteLine($"  - {failure}");
            }
            return _failed == 0 ? 0 : 1;
        }

        private static void TestMath()
        {
            var a = new Vec3(1, 2, 3);
            var b = new Vec3(4, 5, 6);
            Near("vec dot", Vec3.Dot(a, b), 32f, 1e-4f);
            Check("vec cross", Vec3.Cross(a, b) == new Vec3(-3, 6, -3));
            Near("vec length", a.Length, MathF.Sqrt(14f), 1e-4f);
            Check("vec normalize unit", MathF.Abs(new Vec3(0, 5, 0).Normalized.Length - 1f) < 1e-4f);
            Check("vec flat clears y", new Vec3(1, 5, 2).Flat == new Vec3(1, 0, 2));
            Near("vec flat distance", Vec3.FlatDistance(new Vec3(0, 10, 0), new Vec3(3, 0, 4)), 5f, 1e-4f);
            Check("vec lerp clamps", Vec3.Lerp(Vec3.Zero, new Vec3(10, 0, 0), 2f) == new Vec3(10, 0, 0));
            Near("vec yaw", Vec3.FromYaw(MathF.PI / 2f).X, 1f, 1e-4f);
            Near("angle delta wraps", PitchMath.DeltaAngle(3.1f, -3.1f), 0.0831853f, 0.001f);
            Near("segment distance", PitchMath.FlatSegmentDistance(new Vec3(0, 0, 5), new Vec3(-5, 0, 0), new Vec3(5, 0, 0)), 5f, 1e-3f);
            Near("smooth damp approaches", PitchMath.SmoothDamp(0f, 10f, ref SmoothDampVelocity, 0.2f, 1f / 60f), 0.0833f, 0.2f);
        }

        private static float SmoothDampVelocity;

        private static void TestPitchSpec()
        {
            Check("goal at away end", PitchSpec.IsGoal(new Vec3(0, 0.5f, PitchSpec.HalfLength + 0.2f), TeamSide.Away));
            Check("no goal outside post", !PitchSpec.IsGoal(new Vec3(4.2f, 0.5f, PitchSpec.HalfLength + 0.2f), TeamSide.Away));
            Check("no goal over bar", !PitchSpec.IsGoal(new Vec3(0, 2.9f, PitchSpec.HalfLength + 0.2f), TeamSide.Away));
            Check("goal at home end", PitchSpec.IsGoal(new Vec3(0, 0.2f, -PitchSpec.HalfLength - 0.2f), TeamSide.Home));
            Check("penalty spot away", PitchSpec.PenaltySpotPosition(TeamSide.Away).Z > 0);
            Check("penalty area home", PitchSpec.IsInPenaltyArea(new Vec3(0, 0, -PitchSpec.HalfLength + 5f), TeamSide.Home));
            Check("outside penalty area", !PitchSpec.IsInPenaltyArea(new Vec3(0, 0, 0), TeamSide.Home));
            Near("attack distance home", PitchSpec.DistanceFromGoalLine(new Vec3(0, 0, 0), 1f), 52.5f, 1e-3f);
            Near("attack distance away", PitchSpec.DistanceFromGoalLine(new Vec3(0, 0, 0), -1f), 52.5f, 1e-3f);
            Check("second last defender", true);
        }

        private static void TestBall()
        {
            var ball = new BallPhysics();
            ball.Kick(new Vec3(12f, 0.5f, 0f), Vec3.Zero);
            for (int i = 0; i < 60 * 30; i++) ball.Step(1f / 60f);
            Check("ball settles", ball.Velocity.FlatLength < 0.25f, $"speed={ball.Velocity.FlatLength}");
            Check("ball on ground", MathF.Abs(ball.Position.Y - PitchSpec.BallRadius) < 0.02f);
            Check("ball finite", !float.IsNaN(ball.Position.X) && !float.IsNaN(ball.Position.Z));

            var drop = new BallPhysics();
            drop.Position = new Vec3(0, 2f, 0);
            drop.Velocity = Vec3.Zero;
            bool bounced = false;
            float apexAfter = 0f;
            for (int i = 0; i < 90; i++)
            {
                var r = drop.Step(1f / 60f);
                bounced |= r.Bounced;
                if (i > 20) apexAfter = MathF.Max(apexAfter, drop.Position.Y);
            }
            Check("ball bounces", bounced);
            Check("bounce loses height", apexAfter < 1.9f, $"apex={apexAfter}");

            var straight = new BallPhysics();
            straight.Kick(new Vec3(20f, 2f, 0f), Vec3.Zero);
            var curled = new BallPhysics();
            curled.Kick(new Vec3(20f, 2f, 0f), new Vec3(0f, 30f, 0f));
            for (int i = 0; i < 60; i++)
            {
                straight.Step(1f / 60f);
                curled.Step(1f / 60f);
            }
            Check("spin bends the ball", MathF.Abs(curled.Position.Z - straight.Position.Z) > 0.05f,
                $"dz={curled.Position.Z - straight.Position.Z}");

            var post = new BallPhysics();
            post.Position = new Vec3(3.63f, 0.6f, PitchSpec.HalfLength - 0.2f);
            post.Velocity = new Vec3(0f, 0f, 8f);
            var postResult = post.Step(1f / 60f);
            Check("post hit detected", postResult.HitPost, $"hit={postResult.HitPost}");

            var high = new BallPhysics();
            high.Position = new Vec3(0, 4f, 0);
            high.Velocity = new Vec3(0, 0, 0);
            for (int i = 0; i < 300; i++) high.Step(1f / 60f);
            Near("ball falls to ground", high.Position.Y, PitchSpec.BallRadius, 0.02f);
            var landing = high.PredictLandingPoint();
            Check("landing prediction finite", !float.IsNaN(landing.X));
        }

        private static void TestFormations()
        {
            var engine = new MatchEngine(new MatchConfig { IntroEnabled = false });
            foreach (var name in new[] { "4-4-2", "4-3-3", "3-5-2" })
            {
                engine.Home.ApplyFormation(name);
                Check($"formation {name} has 11 slots", engine.Home.Shape.Length == 11);
                Check($"formation {name} maps 11 roles", engine.Home.Players.All(p => p.SlotZ > 0f));
                Check($"formation {name} one keeper", engine.Home.Players.Count(p => p.Role == PlayerRole.Goalkeeper) == 1);
            }
            engine.Home.ApplyFormation("4-3-3");
            var homeStriker = Formations.SlotPosition(TeamSide.Home, 1f, engine.Home.Shape[8], 0.5f, 0.5f);
            var awayStriker = Formations.SlotPosition(TeamSide.Away, -1f, engine.Home.Shape[8], 0.5f, 0.5f);
            Check("away formation mirrors depth", homeStriker.Z > 0 && awayStriker.Z < 0,
                $"home={homeStriker.Z} away={awayStriker.Z}");
            Check("away formation mirrors width", MathF.Sign(homeStriker.X) != MathF.Sign(awayStriker.X));
            Check("all home players in own half at kickoff", engine.Home.Players.All(p => p.Position.Z < 1f));
        }

        private static void TestReferee()
        {
            var refe = new Referee();
            var fouler = new PlayerAgent { Tackling = 0.2f, Aggression = 0.95f, Control = 0.2f };
            var victim = new PlayerAgent();
            var bad = refe.JudgeTackle(fouler, victim, wonBall: false, fromBehind: true, inPenaltyArea: true);
            Check("foul from behind", bad.IsFoul);
            Check("penalty in box", bad.IsPenalty);
            Check("red card for serious foul", bad.Card == CardColour.Red);

            var clean = refe.JudgeTackle(new PlayerAgent { Tackling = 0.8f, Aggression = 0.4f }, victim, true, false, false);
            Check("clean tackle is not a foul", !clean.IsFoul);
            Check("yellow plus yellow is red", Referee.Escalate(CardColour.Yellow, CardColour.Yellow) == CardColour.Red);
            Check("red stays red", Referee.Escalate(CardColour.Red, CardColour.Yellow) == CardColour.Red);

            bool offside = Referee.IsOffside(new PlayerAgent { Position = new Vec3(0, 0, 40f) }, new Vec3(0, 0, 0), 20f, 1f);
            Check("offside beyond the line", offside);
            bool onside = Referee.IsOffside(new PlayerAgent { Position = new Vec3(0, 0, 25f) }, new Vec3(0, 0, 0), 20f, 1f);
            Check("onside behind the line", !onside);
            bool ownHalf = Referee.IsOffside(new PlayerAgent { Position = new Vec3(0, 0, -10f) }, new Vec3(0, 0, 0), 20f, 1f);
            Check("no offside in own half", !ownHalf);
            bool behindBall = Referee.IsOffside(new PlayerAgent { Position = new Vec3(0, 0, 48f) }, new Vec3(0, 0, 50f), 35f, 1f);
            Check("no offside behind the ball", !behindBall);
        }

        private static void TestControls()
        {
            var controls = new ControlModel();
            controls.StickDown(new Vec3(100, 0, 100));
            controls.StickMove(new Vec3(140, 0, 100));
            Near("stick normalised", controls.MoveInput.X, 1f, 0.01f);
            Check("stick active", controls.Stick.Active);
            controls.StickUp();
            Check("stick released", !controls.Stick.Active);

            controls.ButtonDown(ActionButton.Shoot);
            for (int i = 0; i < 60; i++) controls.Tick(1f / 60f);
            Check("shot charge fills", controls.Charge > 0.9f);
            float power = controls.ConsumeShotCharge();
            Check("shot power consumed", power > 0.9f);
            Check("charge resets", controls.Charge < 0.01f);

            controls.SetContext(true, true, false, false);
            Check("context finesse", controls.Contextual == ContextualAction.FinesseShot);
            controls.SetContext(false, false, false, true);
            Check("context slide", controls.Contextual == ContextualAction.SlideTackle);

            var s = new ControlSettings { LeftHanded = true, ButtonScale = 5f, ButtonOpacity = 0f };
            s.Sanitise();
            Check("settings clamp scale", s.ButtonScale <= 1.6f);
            Check("settings clamp opacity", s.ButtonOpacity >= 0.25f);
            Near("left handed layout", s.LayoutX(0.2f), 0.8f, 1e-4f);
        }

        private static void TestQuality()
        {
            var quality = new QualityModel();
            quality.ApplyTier(QualityTier.Low);
            Check("low tier crowd budget", quality.CrowdInstances <= 5000);
            float before = quality.RenderScale;
            for (int i = 0; i < 200; i++) quality.SampleFrame(25f);
            Check("dynamic resolution drops", quality.RenderScale < before, $"scale={quality.RenderScale}");
            Check("frame budget 60fps", MathF.Abs(quality.FrameBudgetMs - 16.666f) < 0.1f);
            for (int i = 0; i < 400; i++) quality.SampleFrame(10f);
            Check("dynamic resolution recovers", quality.RenderScale > 0.8f, $"scale={quality.RenderScale}");
        }

        private static void TestReplay()
        {
            var replay = new ReplayBuffer(120, 1f / 30f);
            var ball = new BallPhysics();
            ball.Kick(new Vec3(5, 1, 0), Vec3.Zero);
            var team = new TeamState { Side = TeamSide.Home };
            team.Players.Add(new PlayerAgent { Position = Vec3.Zero });
            for (int i = 0; i < 120; i++) replay.Sample(1f / 60f, ball, new[] { team });
            Check("replay captured", replay.Count > 0, $"count={replay.Count}");
            Check("replay window", replay.LastSeconds(1f).Count >= 20);
            replay.Clear();
            Check("replay cleared", replay.Count == 0);
        }

        private static void TestIntro()
        {
            var intro = new IntroDirector();
            Check("intro starts in tunnel", intro.Phase == IntroPhase.TunnelWide);
            Check("intro total duration", intro.TotalDuration > 20f, $"total={intro.TotalDuration}");
            for (int i = 0; i < 60 * 10; i++) intro.Tick(1f / 60f);
            Check("intro advances", intro.Phase >= IntroPhase.TrophyApproach);
            var camera = intro.CameraAt(12f);
            Check("intro camera above ground", camera.Position.Y > 0f);
            Check("intro camera fov sane", camera.FieldOfView > 20f && camera.FieldOfView < 70f);
            for (int i = 0; i < 60 * 60; i++) intro.Tick(1f / 60f);
            Check("intro finishes", intro.Finished);
            var skipped = new IntroDirector();
            skipped.Skip();
            Check("intro skippable", skipped.Skipped && skipped.Finished);
        }

        private static MatchEngine NewMatch(float halfSeconds = 30f)
        {
            var match = new MatchEngine(new MatchConfig
            {
                HalfRealSeconds = halfSeconds,
                IntroEnabled = false,
                GoalCelebrationSeconds = 1f,
                HalfTimeSeconds = 1f,
                RestartDelay = 0.3f,
            });
            match.SetUserControlled(false);
            return match;
        }

        private static void TestMatchFlow()
        {
            var match = NewMatch(4f);
            Check("match starts in kickoff", match.Phase == MatchPhase.KickOff);
            Check("kickoff taker exists", match.Restart.Taker != null);
            Check("22 players on the pitch", match.Home.Players.Count + match.Away.Players.Count == 22);
            for (int i = 0; i < 60 * 12; i++) match.StepFixed();

            Check("half time reached", match.Half == 2 || match.Phase == MatchPhase.FullTime, $"phase={match.Phase} half={match.Half}");
            Check("clock in range", match.DisplayMinute >= 0 && match.DisplayMinute <= 90);
            Check("no NaN ball", !float.IsNaN(match.Ball.Position.X) && !float.IsNaN(match.Ball.Position.Y));
            Check("players inside the world", match.Home.Players.All(p => MathF.Abs(p.Position.X) <= PitchSpec.HalfWidth + 4f && MathF.Abs(p.Position.Z) <= PitchSpec.HalfLength + 4f));
            Check("stamina bounded", match.Home.Players.All(p => p.Stamina >= 0f && p.Stamina <= 100f));
            var events = match.Events.Select(e => e.Type).ToHashSet();
            Check("kickoff event recorded", events.Contains(MatchEventType.KickOff));
            Check("tifo event recorded", events.Contains(MatchEventType.Tifo));
            Check("presentation score matches", match.Presentation.HomeScore == match.Home.Score && match.Presentation.AwayScore == match.Away.Score);
            Check("possession percentage sums", MathF.Abs(match.Stats.PossessionPercent(TeamSide.Home) + match.Stats.PossessionPercent(TeamSide.Away) - 100f) < 0.01f);

            var fresh = NewMatch(2f);
            for (int i = 0; i < 60 * 20; i++) fresh.StepFixed();
            Check("full time reached", fresh.Phase == MatchPhase.FullTime, $"phase={fresh.Phase}");
            Check("half time event", fresh.Events.Any(e => e.Type == MatchEventType.HalfTime));
            Check("full time label", fresh.Presentation.PhaseLabel == "FULL TIME");
        }

        private static void TestRestarts()
        {
            var match = NewMatch(30f);
            match.TestStartPlay();

            // Goal: ball crosses the away goal line inside the mouth.
            match.TestPlaceBall(new Vec3(2.5f, 0.2f, PitchSpec.HalfLength - 0.2f), new Vec3(0f, 0f, 14f));
            match.TestSetLastTouch(TeamSide.Home, match.Home.Players[9]);
            for (int i = 0; i < 30 && match.Home.Score == 0; i++) match.StepFixed();
            Check("goal detected", match.Home.Score == 1, $"score={match.Home.Score}");
            Check("goal enters celebration", match.Phase == MatchPhase.GoalCelebration, $"phase={match.Phase}");
            Check("celebration timer set", match.Presentation.CelebrationTimer > 0f);

            var throwMatch = NewMatch(30f);
            throwMatch.TestStartPlay();
            throwMatch.TestPlaceBall(new Vec3(PitchSpec.HalfWidth - 0.05f, 0.2f, 0f), new Vec3(10f, 0f, 0f));
            throwMatch.TestSetLastTouch(TeamSide.Home);
            for (int i = 0; i < 10 && throwMatch.Phase == MatchPhase.InPlay; i++) throwMatch.StepFixed();
            Check("throw in awarded", throwMatch.Phase == MatchPhase.ThrowIn, $"phase={throwMatch.Phase}");
            Check("throw in taker", throwMatch.Restart.Taker != null && throwMatch.Restart.Side == TeamSide.Away);

            var corner = NewMatch(30f);
            corner.TestStartPlay();
            corner.TestPlaceBall(new Vec3(30f, 1.2f, PitchSpec.HalfLength + 0.3f), new Vec3(0f, 0f, 4f));
            corner.TestSetLastTouch(TeamSide.Away);
            for (int i = 0; i < 10 && corner.Phase == MatchPhase.InPlay; i++) corner.StepFixed();
            Check("corner awarded", corner.Phase == MatchPhase.Corner, $"phase={corner.Phase}");

            var goalKick = NewMatch(30f);
            goalKick.TestStartPlay();
            goalKick.TestPlaceBall(new Vec3(20f, 1.2f, PitchSpec.HalfLength + 0.3f), new Vec3(0f, 0f, 4f));
            goalKick.TestSetLastTouch(TeamSide.Home);
            for (int i = 0; i < 10 && goalKick.Phase == MatchPhase.InPlay; i++) goalKick.StepFixed();
            Check("goal kick awarded", goalKick.Phase == MatchPhase.GoalKick, $"phase={goalKick.Phase}");

            var penalty = NewMatch(30f);
            penalty.TestStartPlay();
            penalty.BeginRestart(RestartType.Penalty, TeamSide.Home, PitchSpec.PenaltySpotPosition(TeamSide.Away));
            Check("penalty phase", penalty.Phase == MatchPhase.Penalty);
            Check("penalty taker", penalty.Restart.Taker != null);
            for (int i = 0; i < 60 * 2; i++) penalty.StepFixed();
            Check("penalty resumes play", penalty.Phase != MatchPhase.Penalty, $"phase={penalty.Phase}");
        }

        private static void TestSubstitutions()
        {
            var match = NewMatch(30f);
            int before = match.Home.SubstitutionsLeft;
            bool ok = match.Substitute(TeamSide.Home, 9, 0);
            Check("substitution accepted", ok);
            Check("substitution decrements", match.Home.SubstitutionsLeft == before - 1);
            Check("substitute on pitch", match.Home.Players.Any(p => p.Number == 12));
            Check("keeper cannot be substituted", !match.Substitute(TeamSide.Home, 1, 0));
            Check("invalid bench index rejected", !match.Substitute(TeamSide.Home, 10, 99));
        }

        private static void TestPresentation()
        {
            var state = new PresentationState();
            state.PushLowerThird("GOAL", "Test 12'");
            state.TickLowerThirds(0.1f);
            Check("lower third active", state.ActiveLowerThird.Duration > 0f && state.ActiveLowerThird.Title == "GOAL");
            state.TickLowerThirds(4f);
            Check("lower third expires", state.ActiveLowerThird.Duration <= 0f);
            state.ShakeCamera(0.2f, 1f);
            Check("shake offset within amplitude", MathF.Abs(state.ShakeOffset(0.3f)) <= 0.2f);

            var match = NewMatch(30f);
            Check("tifo starts at kickoff", match.Presentation.Tifo == TifoPhase.Unfurl);
            match.TestStartPlay();
            for (int i = 0; i < 60 * 3; i++) match.StepFixed();
            Check("tifo progresses", match.Presentation.Tifo == TifoPhase.Mosaic || match.Presentation.Tifo == TifoPhase.Chant,
                $"tifo={match.Presentation.Tifo}");

            var homeCarrier = match.Home.Players[9];
            match.TestSetCarrier(homeCarrier);
            Check("ring only for home carrier", match.RingOwnerSide == TeamSide.Home);
            match.TestSetCarrier(match.Away.Players[9]);
            Check("no ring for away carrier", match.RingOwnerSide == null);
            match.TestSetCarrier(null);
            Check("no ring without carrier", match.RingOwnerSide == null);
        }

        private static void TestDeterminism()
        {
            var a = NewMatch(15f);
            var b = NewMatch(15f);
            for (int i = 0; i < 60 * 6; i++) { a.StepFixed(); b.StepFixed(); }
            Check("same seed same ball x", MathF.Abs(a.Ball.Position.X - b.Ball.Position.X) < 1e-4f);
            Check("same seed same ball z", MathF.Abs(a.Ball.Position.Z - b.Ball.Position.Z) < 1e-4f);
            Check("same seed same score", a.Home.Score == b.Home.Score && a.Away.Score == b.Away.Score);
        }

        private static void TestLongSimulation()
        {
            var match = NewMatch(20f);
            for (int i = 0; i < 60 * 70; i++)
            {
                match.StepFixed();
                if (i % 600 == 0)
                {
                    Check("long sim finite ball", !float.IsNaN(match.Ball.Position.X) && !float.IsNaN(match.Ball.Position.Z));
                    Check("long sim finite players", match.Home.Players.All(p => !float.IsNaN(p.Position.X) && !float.IsNaN(p.Position.Z)));
                }
            }
            Check("long sim finished", match.Phase == MatchPhase.FullTime, $"phase={match.Phase}");
            Check("long sim score non-negative", match.Home.Score >= 0 && match.Away.Score >= 0);
            Check("long sim shots recorded", match.Stats.HomeShots + match.Stats.AwayShots >= 0);
            Check("no self-inflicted negative stamina", match.Home.Players.All(p => p.Stamina >= 0f));
        }
    }
}
