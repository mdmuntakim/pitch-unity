using NUnit.Framework;
using Pitch.Core;

namespace Pitch.Tests
{
    /// <summary>
    /// In-editor checks. The full 136-check suite runs headless with dotnet
    /// (tests/CoreTests); these keep the same guarantees inside Unity.
    /// </summary>
    public sealed class CoreEditModeTests
    {
        [Test]
        public void BallSettlesOnTheGround()
        {
            var ball = new BallPhysics();
            ball.Kick(new Vec3(10f, 0.4f, 0f), Vec3.Zero);
            for (int i = 0; i < 60 * 20; i++) ball.Step(1f / 60f);
            Assert.Less(ball.Velocity.FlatLength, 0.5f);
            Assert.AreEqual(PitchSpec.BallRadius, ball.Position.Y, 0.03f);
        }

        [Test]
        public void SpinBendsTheBall()
        {
            var straight = new BallPhysics();
            var curled = new BallPhysics();
            straight.Kick(new Vec3(20f, 2f, 0f), Vec3.Zero);
            curled.Kick(new Vec3(20f, 2f, 0f), new Vec3(0f, 30f, 0f));
            for (int i = 0; i < 60; i++) { straight.Step(1f / 60f); curled.Step(1f / 60f); }
            Assert.Greater(System.Math.Abs(curled.Position.Z - straight.Position.Z), 0.05f);
        }

        [Test]
        public void GoalDetectionHonoursPostsAndBar()
        {
            Assert.IsTrue(PitchSpec.IsGoal(new Vec3(0f, 0.4f, PitchSpec.HalfLength + 0.2f), TeamSide.Away));
            Assert.IsFalse(PitchSpec.IsGoal(new Vec3(4.4f, 0.4f, PitchSpec.HalfLength + 0.2f), TeamSide.Away));
            Assert.IsFalse(PitchSpec.IsGoal(new Vec3(0f, 2.8f, PitchSpec.HalfLength + 0.2f), TeamSide.Away));
        }

        [Test]
        public void OffsideUsesSecondLastDefenderAndTheBall()
        {
            var striker = new PlayerAgent { Position = new Vec3(0f, 0f, 40f) };
            Assert.IsTrue(Referee.IsOffside(striker, new Vec3(0f, 0f, 0f), 20f, 1f));
            striker.Position = new Vec3(0f, 0f, 25f);
            Assert.IsFalse(Referee.IsOffside(striker, new Vec3(0f, 0f, 0f), 20f, 1f));
            striker.Position = new Vec3(0f, 0f, -10f);
            Assert.IsFalse(Referee.IsOffside(striker, new Vec3(0f, 0f, 0f), 20f, 1f));
        }

        [Test]
        public void ThreeFormationsHaveElevenRolesAndOneKeeper()
        {
            var team = new TeamState { Side = TeamSide.Home, AttackDir = 1f };
            for (int i = 0; i < 11; i++) team.Players.Add(new PlayerAgent { Number = i + 1 });
            foreach (var name in new[] { "4-4-2", "4-3-3", "3-5-2" })
            {
                team.ApplyFormation(name);
                Assert.AreEqual(11, team.Shape.Length, name);
                Assert.AreEqual(1, team.Players.FindAll(p => p.Role == PlayerRole.Goalkeeper).Count, name);
                Assert.IsTrue(team.Players.TrueForAll(p => p.HomePosition.Z < 0f), name);
            }
        }

        [Test]
        public void MatchReachesHalfTimeAndFullTime()
        {
            var match = new MatchEngine(new MatchConfig
            {
                HalfRealSeconds = 3f, IntroEnabled = false, GoalCelebrationSeconds = 0.5f,
                HalfTimeSeconds = 0.5f, RestartDelay = 0.2f,
            });
            match.SetUserControlled(false);
            for (int i = 0; i < 60 * 20; i++) match.StepFixed();
            Assert.AreEqual(MatchPhase.FullTime, match.Phase);
            Assert.AreEqual("FULL TIME", match.Presentation.PhaseLabel);
            Assert.IsFalse(float.IsNaN(match.Ball.Position.X));
        }

        [Test]
        public void PossessionRingOnlyForTheHomeCarrier()
        {
            var match = new MatchEngine(new MatchConfig { IntroEnabled = false });
            match.TestSetCarrier(match.Home.Players[9]);
            Assert.AreEqual(TeamSide.Home, match.RingOwnerSide);
            match.TestSetCarrier(match.Away.Players[9]);
            Assert.IsNull(match.RingOwnerSide);
            match.TestSetCarrier(null);
            Assert.IsNull(match.RingOwnerSide);
        }

        [Test]
        public void ShotChargeAndContextualButton()
        {
            var controls = new ControlModel();
            controls.ButtonDown(ActionButton.Shoot);
            for (int i = 0; i < 90; i++) controls.Tick(1f / 60f);
            Assert.Greater(controls.Charge, 0.9f);
            Assert.Greater(controls.ConsumeShotCharge(), 0.9f);
            controls.SetContext(false, false, false, true);
            Assert.AreEqual(ContextualAction.SlideTackle, controls.Contextual);
        }

        [Test]
        public void DestroyedOpponentKeeperCannotBeSubstituted()
        {
            var match = new MatchEngine(new MatchConfig { IntroEnabled = false });
            Assert.IsFalse(match.Substitute(TeamSide.Home, 1, 0));
            Assert.IsTrue(match.Substitute(TeamSide.Home, 9, 0));
        }

        [Test]
        public void IntroHasATrophyWalkPastAndIsSkippable()
        {
            var intro = new IntroDirector();
            for (int i = 0; i < 60 * 12; i++) intro.Tick(1f / 60f);
            Assert.GreaterOrEqual((int)intro.Phase, (int)IntroPhase.WalkPast);
            intro.Skip();
            Assert.IsTrue(intro.Finished);
            Assert.Greater(intro.CameraAt(10f).Position.Y, 0f);
        }
    }
}
