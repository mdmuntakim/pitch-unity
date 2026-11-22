using System;
using System.Collections.Generic;

namespace Pitch.Core
{
    public enum PlayerRole
    {
        Goalkeeper,
        CentreBack,
        FullBack,
        WingBack,
        DefensiveMid,
        CentreMid,
        AttackingMid,
        Winger,
        Striker,
    }

    public enum PlayerState
    {
        Idle,
        Jog,
        Sprint,
        Dribble,
        Pass,
        Shoot,
        Tackle,
        Celebrate,
        Down,
        KeeperDive,
    }

    public enum CelebrateStyle
    {
        None,
        ArmsOut,
        KneeSlide,
        JumpPunch,
        ShirtPull,
        TeamHuddle,
    }

    /// <summary>
    /// One footballer. Movement is driven by a steering target, never by
    /// teleporting, so the same agent works for the human team and the AI.
    /// </summary>
    public sealed class PlayerAgent
    {
        public int Id;
        public TeamSide Side;
        public int Number;
        public string Name;
        public PlayerRole Role;
        public PlayerState State = PlayerState.Idle;

        /// <summary>Normalised formation slot, x -1..1 across, z 0..1 own goal to opponent goal.</summary>
        public float SlotX;
        public float SlotZ;

        public Vec3 Position;
        public Vec3 Velocity;
        public float Facing;
        public Vec3 HomePosition;

        public float Stamina = 100f;
        public float KickCharge;
        public float ActionCooldown;
        public float DecisionTimer;

        // Attributes, 0..1.
        public float Pace = 0.65f;
        public float Acceleration = 0.65f;
        public float Control = 0.65f;
        public float Passing = 0.65f;
        public float Shooting = 0.6f;
        public float Tackling = 0.6f;
        public float StaminaRating = 0.65f;
        public float Aggression = 0.5f;

        public CelebrateStyle Celebration = CelebrateStyle.ArmsOut;
        public bool IsUserControlled;

        public float MaxSpeed => (5.1f + Pace * 2.7f) * StaminaFactor;
        public float SprintSpeed => MaxSpeed;
        public float JogSpeed => MaxSpeed * 0.58f;
        public float StaminaFactor => 0.72f + 0.28f * PitchMath.Clamp01(Stamina / 100f);
        public float BodyRadius => PitchSpec.PlayerRadius;

        public void ResetForKickOff(Vec3 position)
        {
            Position = position;
            HomePosition = position;
            Velocity = Vec3.Zero;
            State = PlayerState.Idle;
            Facing = Side == TeamSide.Home ? 0f : MathF.PI;
            KickCharge = 0f;
            ActionCooldown = 0f;
            DecisionTimer = 0f;
            Stamina = MathF.Max(Stamina, 55f);
        }

        /// <summary>Steer towards a point, staying inside the pitch.</summary>
        public void SteerTowards(Vec3 target, float dt, float speedMultiplier = 1f, bool sprint = false)
        {
            Vec3 delta = (target - Position).Flat;
            float distance = delta.Length;
            float desiredSpeed = 0f;
            if (distance > 0.12f)
            {
                desiredSpeed = (sprint ? SprintSpeed : JogSpeed) * speedMultiplier;
                desiredSpeed *= PitchMath.Clamp01(distance / 1.2f);
            }

            Vec3 desiredVelocity = distance > 1e-4f ? delta / distance * desiredSpeed : Vec3.Zero;
            float accel = (6.5f + Acceleration * 7.5f) * (sprint ? 1f : 0.8f);
            Vec3 deltaV = desiredVelocity - Velocity;
            float deltaLen = deltaV.FlatLength;
            if (deltaLen > accel * dt)
                deltaV = deltaV.Flat.Normalized * (accel * dt);
            Velocity += new Vec3(deltaV.X, 0f, deltaV.Z);
            Position += Velocity * dt;
            Position = PitchSpec.ClampToPitch(Position, 2.5f);

            if (Velocity.FlatLength > 0.05f)
            {
                float targetFacing = MathF.Atan2(Velocity.X, Velocity.Z);
                Facing = PitchMath.AngleLerp(Facing, targetFacing, 1f - MathF.Exp(-10f * dt));
            }

            UpdateStamina(dt, sprint || Velocity.FlatLength > JogSpeed * 0.95f, distance < 0.4f);
            UpdateStateFromMotion(sprint);
        }

        public void MoveManual(Vec3 input, float dt, bool sprint)
        {
            Vec3 dir = input.Flat;
            float magnitude = MathF.Min(1f, dir.Length);
            if (magnitude > 1e-3f)
            {
                float speed = (sprint ? SprintSpeed : JogSpeed) * magnitude;
                Vec3 desired = dir.Normalized * speed;
                float accel = (7f + Acceleration * 9f) * dt;
                Vec3 deltaV = desired - Velocity;
                if (deltaV.FlatLength > accel) deltaV = deltaV.Flat.Normalized * accel;
                Velocity += new Vec3(deltaV.X, 0f, deltaV.Z);
                float targetFacing = MathF.Atan2(desired.X, desired.Z);
                Facing = PitchMath.AngleLerp(Facing, targetFacing, 1f - MathF.Exp(-14f * dt));
            }
            else
            {
                Velocity = PitchMath.MoveTowards(Velocity, Vec3.Zero, 18f * dt);
            }

            Position += Velocity * dt;
            Position = PitchSpec.ClampToPitch(Position, 2.5f);
            UpdateStamina(dt, sprint && magnitude > 0.5f, magnitude < 0.1f);
            UpdateStateFromMotion(sprint && magnitude > 0.5f);
        }

        private void UpdateStamina(float dt, bool sprinting, bool resting)
        {
            float drain = sprinting ? 0.85f : 0.28f;
            drain *= 1.25f - StaminaRating * 0.5f;
            float recovery = resting ? 1.15f : 0.42f;
            Stamina = Math.Clamp(Stamina + (recovery - drain) * dt, 0f, 100f);
        }

        private void UpdateStateFromMotion(bool sprint)
        {
            if (State == PlayerState.Celebrate || State == PlayerState.Down || State == PlayerState.KeeperDive)
                return;
            if (ActionCooldown > 0f) return;
            if (Velocity.FlatLength > JogSpeed * 0.92f) State = sprint ? PlayerState.Sprint : PlayerState.Jog;
            else if (Velocity.FlatLength > 0.2f) State = PlayerState.Jog;
            else State = PlayerState.Idle;
        }

        public void TickTimers(float dt)
        {
            ActionCooldown = MathF.Max(0f, ActionCooldown - dt);
            DecisionTimer = MathF.Max(0f, DecisionTimer - dt);
            if (State == PlayerState.Down && ActionCooldown <= 0f) State = PlayerState.Idle;
        }

        public bool CanAct => State != PlayerState.Down && ActionCooldown <= 0.05f;

        public float ControlOfBall(Vec3 ball) => PitchMath.Clamp01(1f - Vec3.FlatDistance(Position, ball) / 2.2f);

        public void FaceTowards(Vec3 target, float dt, float speed = 10f)
        {
            Vec3 d = target - Position;
            if (d.FlatLength < 1e-4f) return;
            Facing = PitchMath.AngleLerp(Facing, MathF.Atan2(d.X, d.Z), 1f - MathF.Exp(-speed * dt));
        }
    }

    /// <summary>Named formation table. Slots are normalised for the Home orientation.</summary>
    public static class Formations
    {
        public sealed class Slot
        {
            public PlayerRole Role;
            public float X;
            public float Z;
            public Slot(PlayerRole role, float x, float z) { Role = role; X = x; Z = z; }
        }

        public static readonly Dictionary<string, Slot[]> Table = new Dictionary<string, Slot[]>
        {
            ["4-4-2"] = new[]
            {
                new Slot(PlayerRole.Goalkeeper, 0.00f, 0.035f),
                new Slot(PlayerRole.FullBack, -0.86f, 0.20f),
                new Slot(PlayerRole.CentreBack, -0.32f, 0.16f),
                new Slot(PlayerRole.CentreBack, 0.32f, 0.16f),
                new Slot(PlayerRole.FullBack, 0.86f, 0.20f),
                new Slot(PlayerRole.Winger, -0.90f, 0.50f),
                new Slot(PlayerRole.CentreMid, -0.28f, 0.44f),
                new Slot(PlayerRole.CentreMid, 0.28f, 0.44f),
                new Slot(PlayerRole.Winger, 0.90f, 0.50f),
                new Slot(PlayerRole.Striker, -0.22f, 0.76f),
                new Slot(PlayerRole.Striker, 0.22f, 0.76f),
            },
            ["4-3-3"] = new[]
            {
                new Slot(PlayerRole.Goalkeeper, 0.00f, 0.035f),
                new Slot(PlayerRole.FullBack, -0.88f, 0.22f),
                new Slot(PlayerRole.CentreBack, -0.32f, 0.16f),
                new Slot(PlayerRole.CentreBack, 0.32f, 0.16f),
                new Slot(PlayerRole.FullBack, 0.88f, 0.22f),
                new Slot(PlayerRole.DefensiveMid, 0.00f, 0.36f),
                new Slot(PlayerRole.CentreMid, -0.36f, 0.52f),
                new Slot(PlayerRole.CentreMid, 0.36f, 0.52f),
                new Slot(PlayerRole.Winger, -0.86f, 0.74f),
                new Slot(PlayerRole.Striker, 0.00f, 0.82f),
                new Slot(PlayerRole.Winger, 0.86f, 0.74f),
            },
            ["3-5-2"] = new[]
            {
                new Slot(PlayerRole.Goalkeeper, 0.00f, 0.035f),
                new Slot(PlayerRole.CentreBack, -0.44f, 0.16f),
                new Slot(PlayerRole.CentreBack, 0.00f, 0.14f),
                new Slot(PlayerRole.CentreBack, 0.44f, 0.16f),
                new Slot(PlayerRole.WingBack, -0.92f, 0.48f),
                new Slot(PlayerRole.CentreMid, -0.30f, 0.44f),
                new Slot(PlayerRole.DefensiveMid, 0.00f, 0.36f),
                new Slot(PlayerRole.CentreMid, 0.30f, 0.44f),
                new Slot(PlayerRole.WingBack, 0.92f, 0.48f),
                new Slot(PlayerRole.Striker, -0.24f, 0.80f),
                new Slot(PlayerRole.Striker, 0.24f, 0.80f),
            },
        };

        public static Slot[] Get(string name)
            => Table.TryGetValue(name, out var slots) ? slots : Table["4-4-2"];

        /// <summary>
        /// World position of a formation slot, shifted by tactic width and line
        /// height. <paramref name="attackDir"/> is +1 when the team attacks +Z.
        /// </summary>
        public static Vec3 SlotPosition(TeamSide side, float attackDir, Slot slot, float lineHeight, float width)
        {
            float x = slot.X * PitchSpec.HalfWidth * (0.72f + width * 0.24f);
            float zFraction = slot.Z + lineHeight * 0.07f;
            float z = -attackDir * PitchSpec.HalfLength + attackDir * zFraction * PitchSpec.Length;
            if (side == TeamSide.Away) x = -x;
            return new Vec3(x, 0f, z);
        }
    }

    /// <summary>Team tactics and the eleven agents, substitutions and cards.</summary>
    public sealed class TeamState
    {
        public TeamSide Side;
        public string Name;
        public string FormationName;
        public Formations.Slot[] Shape;
        public List<PlayerAgent> Players = new List<PlayerAgent>();
        public int Score;
        public int SubstitutionsLeft = 5;
        public bool HasPossession;
        public float PressIntensity = 0.6f;
        public float LineHeight = 0.5f;
        public float Width = 0.55f;
        public float Tempo = 0.6f;
        /// <summary>+1 when this team attacks towards +Z. Flipped at half time.</summary>
        public float AttackDir = 1f;
        public List<int> Booked = new List<int>();
        public List<PlayerAgent> SentOff = new List<PlayerAgent>();
        public List<PlayerAgent> Bench = new List<PlayerAgent>();

        public PlayerAgent Goalkeeper => Players.Find(p => p.Role == PlayerRole.Goalkeeper);

        public void ApplyFormation(string formation)
        {
            FormationName = formation;
            Shape = Formations.Get(formation);
            for (int i = 0; i < Players.Count && i < Shape.Length; i++)
            {
                Players[i].Role = Shape[i].Role;
                Players[i].SlotX = Shape[i].X;
                Players[i].SlotZ = Shape[i].Z;
                Players[i].State = PlayerState.Idle;
            }
            RefreshHomePositions();
        }

        public void RefreshHomePositions()
        {
            if (Shape == null) return;
            for (int i = 0; i < Players.Count && i < Shape.Length; i++)
            {
                Players[i].HomePosition = Formations.SlotPosition(Side, AttackDir, Shape[i], LineHeight, Width);
            }
        }

        /// <summary>
        /// Offside line for this team, in metres from the goal line it attacks.
        /// The second last opponent from that goal line, never deeper than the ball.
        /// </summary>
        public float OffsideLineDistance(TeamState opponent, Vec3 ball)
        {
            var depths = new List<float>();
            foreach (var p in opponent.Players)
            {
                if (p.State == PlayerState.Down) continue;
                depths.Add(PitchSpec.DistanceFromGoalLine(p.Position, opponent.AttackDir));
            }
            depths.Sort();
            float secondLast = depths.Count >= 2 ? depths[1] : PitchSpec.HalfLength;
            return MathF.Min(PitchSpec.HalfLength, MathF.Min(secondLast, PitchSpec.DistanceFromGoalLine(ball, AttackDir)));
        }
    }
}
