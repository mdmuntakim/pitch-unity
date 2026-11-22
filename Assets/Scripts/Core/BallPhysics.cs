using System;

namespace Pitch.Core
{
    /// <summary>
    /// Continuous ball simulation with a fixed timestep. No UnityEngine types, so
    /// the same code runs in the editor, in a headless test and on device.
    ///
    /// Model: gravity, quadratic air drag, Magnus force from spin, rolling
    /// resistance, bounce restitution, spin decay and post/crossbar collisions.
    /// </summary>
    public sealed class BallPhysics
    {
        public Vec3 Position;
        public Vec3 Velocity;
        /// <summary>Angular velocity in rad/s. Y is the vertical axis (curve), X is topspin/backspin.</summary>
        public Vec3 Spin;
        public Vec3 PreviousPosition;
        public bool IsGrounded;

        public float Restitution = 0.62f;
        public float RollingResistance = 0.42f;
        public float DragCoefficient = 0.0032f;
        public float MagnusCoefficient = 0.00055f;
        public float SpinDecay = 1.1f;

        public BallPhysics()
        {
            Reset(new Vec3(0f, PitchSpec.BallRadius, 0f));
        }

        public void Reset(Vec3 position)
        {
            Position = position;
            PreviousPosition = position;
            Velocity = Vec3.Zero;
            Spin = Vec3.Zero;
            IsGrounded = true;
        }

        public void Kick(Vec3 velocity, Vec3 spin)
        {
            Velocity = velocity;
            Spin = spin;
            IsGrounded = false;
        }

        /// <summary>
        /// Advance the simulation. <paramref name="dt"/> is normally 1/60.
        /// Returns flags describing what happened during the step.
        /// </summary>
        public BallStepResult Step(float dt)
        {
            if (dt <= 0f) return default;

            PreviousPosition = Position;
            BallStepResult result = default;

            Vec3 acceleration = new Vec3(0f, PitchSpec.Gravity, 0f);

            float speed = Velocity.Length;
            if (speed > 0.01f)
            {
                acceleration += -Velocity.Normalized * (DragCoefficient * speed * speed);
                // Magnus: a spinning ball curves perpendicular to its velocity.
                acceleration += Vec3.Cross(Spin, Velocity) * MagnusCoefficient;
            }

            Velocity += acceleration * dt;
            Position += Velocity * dt;

            // Ground contact.
            if (Position.Y <= PitchSpec.BallRadius)
            {
                Position = Position.WithY(PitchSpec.BallRadius);
                if (Velocity.Y < -0.35f)
                {
                    result.Bounced = true;
                    Velocity = new Vec3(Velocity.X, -Velocity.Y * Restitution, Velocity.Z);
                    // Bounce converts horizontal speed into spin and loses energy.
                    float horizontal = Velocity.FlatLength;
                    if (horizontal > 0.1f)
                    {
                        Vec3 dir = Velocity.Flat.Normalized;
                        Spin += new Vec3(dir.Z, 0f, -dir.X) * (horizontal * 0.35f);
                        Velocity = Velocity.Flat * 0.86f + new Vec3(0f, Velocity.Y, 0f);
                    }
                }
                else
                {
                    Velocity = new Vec3(Velocity.X, 0f, Velocity.Z);
                    IsGrounded = MathF.Abs(Velocity.Y) < 0.01f;
                }

                if (IsGrounded)
                {
                    // Rolling resistance is a deceleration plus a small viscous term.
                    float flat = Velocity.FlatLength;
                    if (flat > 0.001f)
                    {
                        float decel = (RollingResistance + flat * 0.055f) * dt;
                        float newFlat = MathF.Max(0f, flat - decel);
                        Velocity = Velocity.Flat.Normalized * newFlat;
                    }
                    else
                    {
                        Velocity = Vec3.Zero;
                    }

                    // Rolling spin is bound to the surface: the ball rolls without slipping.
                    if (Velocity.FlatLength > 0.01f)
                    {
                        Vec3 dir = Velocity.Flat.Normalized;
                        Spin = Vec3.Lerp(Spin, new Vec3(dir.Z, 0f, -dir.X) * (Velocity.FlatLength / PitchSpec.BallRadius), 0.35f);
                    }
                }
            }
            else
            {
                IsGrounded = false;
            }

            // Spin decays exponentially (air and contact friction).
            Spin *= MathF.Max(0f, 1f - SpinDecay * dt);

            result.HitPost = HandleGoalFrame(ref result);
            result.Position = Position;
            result.Speed = Velocity.Length;
            return result;
        }

        /// <summary>
        /// Goal frame collision: two posts and the crossbar for both goals.
        /// Sets the HitPost flag on the result and reflects the velocity.
        /// </summary>
        private bool HandleGoalFrame(ref BallStepResult result)
        {
            bool hit = false;
            for (int i = 0; i < 2; i++)
            {
                float lineZ = i == 0 ? -PitchSpec.HalfLength : PitchSpec.HalfLength;
                if (MathF.Abs(Position.Z - lineZ) > PitchSpec.BallRadius + PitchSpec.PostRadius + 0.4f)
                    continue;

                for (int s = -1; s <= 1; s += 2)
                {
                    Vec3 post = new Vec3(s * PitchSpec.GoalHalfWidth, 0f, lineZ);
                    Vec3 flatDelta = new Vec3(Position.X - post.X, 0f, Position.Z - post.Z);
                    float dist = flatDelta.Length;
                    float combined = PitchSpec.BallRadius + PitchSpec.PostRadius;
                    if (dist < combined && Position.Y < PitchSpec.GoalHeight + PitchSpec.BallRadius)
                    {
                        Vec3 normal = dist > 1e-5f ? flatDelta / dist : new Vec3(0f, 0f, 1f);
                        Position = new Vec3(post.X + normal.X * combined, Position.Y, post.Z + normal.Z * combined);
                        Velocity = PitchMath.Reflect(Velocity, normal) * 0.74f;
                        Spin *= 0.7f;
                        hit = true;
                        result.HitLeftPost |= s < 0;
                        result.HitRightPost |= s > 0;
                    }

                    // Crossbar: horizontal cylinder at the goal height.
                    float dx = Position.X - post.X;
                    float dy = Position.Y - PitchSpec.GoalHeight;
                    float radial = MathF.Sqrt(dx * dx + dy * dy);
                    if (radial < combined && MathF.Abs(Position.Z - lineZ) < combined)
                    {
                        Vec3 normal = radial > 1e-5f ? new Vec3(dx / radial, dy / radial, 0f) : Vec3.Up;
                        Position = new Vec3(post.X + normal.X * combined, PitchSpec.GoalHeight + normal.Y * combined, Position.Z);
                        Velocity = PitchMath.Reflect(Velocity, normal) * 0.72f;
                        hit = true;
                        result.HitCrossbar = true;
                    }
                }
            }

            return hit;
        }

        /// <summary>
        /// Interpolated position for rendering. Rendering runs at the display rate,
        /// physics at a fixed 60 Hz, so the ball never stutters on a 90/120 Hz screen.
        /// </summary>
        public Vec3 Interpolated(float alpha) => Vec3.Lerp(PreviousPosition, Position, alpha);

        public float HeightAboveGround => Position.Y - PitchSpec.BallRadius;

        /// <summary>Predicted landing point for a lofted pass, used by the AI and the indicator.</summary>
        public Vec3 PredictLandingPoint(float maxTime = 3f)
        {
            Vec3 p = Position;
            Vec3 v = Velocity;
            float dt = 1f / 60f;
            for (float t = 0f; t < maxTime; t += dt)
            {
                v += new Vec3(0f, PitchSpec.Gravity, 0f) * dt;
                p += v * dt;
                if (p.Y <= PitchSpec.BallRadius) return p.WithY(0f);
            }
            return p.WithY(0f);
        }
    }

    public struct BallStepResult
    {
        public Vec3 Position;
        public float Speed;
        public bool Bounced;
        public bool HitPost;
        public bool HitLeftPost;
        public bool HitRightPost;
        public bool HitCrossbar;
    }
}
