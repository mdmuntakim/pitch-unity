using System;

namespace Pitch.Core
{
    /// <summary>
    /// Laws-of-the-game pitch geometry, metres, origin at the centre spot.
    /// +Z points at the away goal, -Z at the home goal, X is across the pitch.
    /// </summary>
    public static class PitchSpec
    {
        public const float Length = 105f;
        public const float Width = 68f;
        public const float HalfLength = Length * 0.5f;
        public const float HalfWidth = Width * 0.5f;

        public const float GoalWidth = 7.32f;
        public const float GoalHalfWidth = GoalWidth * 0.5f;
        public const float GoalHeight = 2.44f;
        public const float GoalDepth = 2.0f;
        public const float PostRadius = 0.06f;

        public const float PenaltyAreaLength = 16.5f;
        public const float PenaltyAreaHalfWidth = 20.16f;
        public const float GoalAreaLength = 5.5f;
        public const float GoalAreaHalfWidth = 9.16f;
        public const float PenaltySpot = 11f;
        public const float CentreCircleRadius = 9.15f;
        public const float CornerArcRadius = 1f;

        public const float BallRadius = 0.11f;
        public const float BallMass = 0.43f;
        public const float Gravity = -9.81f;

        public const float PlayerRadius = 0.34f;

        /// <summary>Outer playing surface including a small runoff.</summary>
        public const float SurfaceLength = Length + 10f;
        public const float SurfaceWidth = Width + 8f;

        public static Vec3 HomeGoalCentre => new Vec3(0f, 0f, -HalfLength);
        public static Vec3 AwayGoalCentre => new Vec3(0f, 0f, HalfLength);

        public static Vec3 GoalCentre(TeamSide side) => side == TeamSide.Home ? HomeGoalCentre : AwayGoalCentre;

        /// <summary>The goal a team attacks.</summary>
        public static Vec3 AttackingGoal(TeamSide side) => side == TeamSide.Home ? AwayGoalCentre : HomeGoalCentre;

        /// <summary>The goal a team defends.</summary>
        public static Vec3 DefendingGoal(TeamSide side) => side == TeamSide.Home ? HomeGoalCentre : AwayGoalCentre;

        /// <summary>+1 when the side attacks towards +Z, -1 otherwise.</summary>
        public static float AttackDirection(TeamSide side) => side == TeamSide.Home ? 1f : -1f;

        public static float GoalLineZ(TeamSide defendingSide) => defendingSide == TeamSide.Home ? -HalfLength : HalfLength;

        public static TeamSide Opponent(TeamSide side) => side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;

        /// <summary>Metres from the goal line a team defends. 0 on its own goal line, 105 at the far goal.</summary>
        public static float DistanceFromOwnGoal(Vec3 p, TeamSide side)
            => side == TeamSide.Home ? p.Z + HalfLength : HalfLength - p.Z;

        /// <summary>Metres from the goal line a team attacks.</summary>
        public static float DistanceToAttackingGoal(Vec3 p, TeamSide side)
            => DistanceFromOwnGoal(p, Opponent(side));

        /// <summary>Metres from the goal line a team attacks, given its attack direction.</summary>
        public static float DistanceFromGoalLine(Vec3 p, float attackDir)
            => attackDir > 0f ? HalfLength - p.Z : p.Z + HalfLength;

        /// <summary>Goal centre a team attacks, given its attack direction.</summary>
        public static Vec3 AttackingGoalFor(float attackDir)
            => attackDir > 0f ? AwayGoalCentre : HomeGoalCentre;

        /// <summary>Goal centre a team defends, given its attack direction.</summary>
        public static Vec3 DefendingGoalFor(float attackDir)
            => attackDir > 0f ? HomeGoalCentre : AwayGoalCentre;

        public static Vec3 ClampToPitch(Vec3 p, float margin = 0f)
        {
            return Vec3.ClampFlat(p, -HalfWidth - margin, HalfWidth + margin, -HalfLength - margin, HalfLength + margin);
        }

        public static bool IsInsidePitch(Vec3 p, float margin = 0f)
            => PitchMath.InRect(p, -HalfWidth - margin, HalfWidth + margin, -HalfLength - margin, HalfLength + margin);

        /// <summary>Crossed the goal line of the given defending side and is inside the goal mouth.</summary>
        public static bool IsGoal(Vec3 ballPosition, TeamSide defendingSide)
        {
            float line = GoalLineZ(defendingSide);
            bool crossed = defendingSide == TeamSide.Home
                ? ballPosition.Z < line - BallRadius * 0.5f
                : ballPosition.Z > line + BallRadius * 0.5f;
            return crossed
                && MathF.Abs(ballPosition.X) < GoalHalfWidth
                && ballPosition.Y < GoalHeight;
        }

        public static bool IsBehindGoalLine(Vec3 p, TeamSide defendingSide)
        {
            return defendingSide == TeamSide.Home
                ? p.Z < -HalfLength
                : p.Z > HalfLength;
        }

        public static bool IsInPenaltyArea(Vec3 p, TeamSide defendingSide)
        {
            float line = GoalLineZ(defendingSide);
            float minZ = defendingSide == TeamSide.Home ? line : line - PenaltyAreaLength;
            float maxZ = defendingSide == TeamSide.Home ? line + PenaltyAreaLength : line;
            return PitchMath.InRect(p, -PenaltyAreaHalfWidth, PenaltyAreaHalfWidth, minZ, maxZ);
        }

        public static bool IsInGoalArea(Vec3 p, TeamSide defendingSide)
        {
            float line = GoalLineZ(defendingSide);
            float minZ = defendingSide == TeamSide.Home ? line : line - GoalAreaLength;
            float maxZ = defendingSide == TeamSide.Home ? line + GoalAreaLength : line;
            return PitchMath.InRect(p, -GoalAreaHalfWidth, GoalAreaHalfWidth, minZ, maxZ);
        }

        public static Vec3 PenaltySpotPosition(TeamSide defendingSide)
        {
            float line = GoalLineZ(defendingSide);
            return new Vec3(0f, 0f, line + (defendingSide == TeamSide.Home ? PenaltySpot : -PenaltySpot));
        }

        public static Vec3 CornerPosition(float sideX, TeamSide defendingSide)
        {
            float z = defendingSide == TeamSide.Home ? -HalfLength : HalfLength;
            return new Vec3(sideX * HalfWidth, 0f, z);
        }

        /// <summary>Nearest point on the defending team's goal line for a throw-in/corner decision.</summary>
        public static Vec3 GoalLinePoint(Vec3 p, TeamSide defendingSide)
            => new Vec3(p.X, 0f, GoalLineZ(defendingSide));

        /// <summary>
        /// True when the ball is in the defending team's goal mouth framework area,
        /// used to pick goal kick vs corner after the ball goes out.
        /// </summary>
        public static bool LastTouchByAttacker(TeamSide defendingSide, TeamSide lastTouch)
            => lastTouch != defendingSide;
    }

    public enum TeamSide
    {
        Home = 0,
        Away = 1,
    }
}
