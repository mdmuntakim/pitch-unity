using System;

namespace Pitch.Core
{
    public enum ActionButton
    {
        None,
        Pass,
        Shoot,
        ThroughBall,
        LobCross,
        Sprint,
        PressureTackle,
        Contextual,
    }

    public enum ContextualAction
    {
        None,
        SlideTackle,
        Clear,
        SwitchPlayer,
        FinesseShot,
        Header,
        Throw,
    }

    /// <summary>
    /// Persistent touch control settings, mirrored into PlayerPrefs by the Unity
    /// adapter. Lives in Core so the layout maths and the persistence tests are
    /// engine independent.
    /// </summary>
    [Serializable]
    public sealed class ControlSettings
    {
        public float ButtonScale = 1f;
        public float ButtonOpacity = 0.72f;
        public bool LeftHanded;
        public bool Haptics = true;
        public bool FloatingJoystick = true;
        public float JoystickScale = 1f;
        public bool BallHighlight = true;
        public bool CameraShake = true;
        public bool ShowRadar = true;

        public void Sanitise()
        {
            ButtonScale = Math.Clamp(ButtonScale, 0.6f, 1.6f);
            ButtonOpacity = Math.Clamp(ButtonOpacity, 0.25f, 1f);
            JoystickScale = Math.Clamp(JoystickScale, 0.6f, 1.6f);
        }
    }

    public struct VirtualStick
    {
        public bool Active;
        public Vec3 Origin;
        public Vec3 Current;
        public Vec3 Value;
        public float Magnitude;

        public void Begin(Vec3 at)
        {
            Active = true;
            Origin = at;
            Current = at;
            Value = Vec3.Zero;
            Magnitude = 0f;
        }

        public void Drag(Vec3 to, float radius)
        {
            Current = to;
            Vec3 delta = Current - Origin;
            float length = VectorLength(delta);
            if (length > radius) delta = delta / length * radius;
            Magnitude = PitchMath.Clamp01(length / MathF.Max(0.001f, radius));
            Value = new Vec3(delta.X / MathF.Max(0.001f, radius), 0f, delta.Z / MathF.Max(0.001f, radius));
        }

        public void End()
        {
            Active = false;
            Value = Vec3.Zero;
            Magnitude = 0f;
        }

        private static float VectorLength(Vec3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
    }

    /// <summary>
    /// Multi-touch control model: a floating left stick, a right action cluster,
    /// hold-to-charge shooting and a contextual button that changes with
    /// possession. Input is event driven (press/release), never polled, so no
    /// touch is dropped while another finger is dragging the stick.
    /// </summary>
    public sealed class ControlModel
    {
        public ControlSettings Settings = new ControlSettings();
        public VirtualStick Stick = new VirtualStick();
        public ActionButton LastPressed { get; private set; }
        public ActionButton HeldButton { get; private set; }
        public float Charge { get; private set; }
        public float ChargeTime { get; private set; }
        public bool SprintHeld { get; private set; }
        public ContextualAction Contextual { get; private set; }
        public bool HasPossession { get; private set; }
        public float Radius = 0.17f;

        public Vec3 MoveInput => Stick.Value;
        public float InputLatencyBudgetMs = 50f;

        /// <summary>Contextual button changes with possession and position on the pitch.</summary>
        public void SetContext(bool hasPossession, bool canShoot, bool nearTouchline, bool defending)
        {
            HasPossession = hasPossession;
            if (hasPossession)
                Contextual = canShoot ? ContextualAction.FinesseShot : (nearTouchline ? ContextualAction.Header : ContextualAction.SwitchPlayer);
            else
                Contextual = defending ? ContextualAction.SlideTackle : ContextualAction.Clear;
        }

        public void StickDown(Vec3 screenPoint)
        {
            if (Settings.FloatingJoystick)
            {
                Stick.Begin(screenPoint);
            }
            else
            {
                Stick.Begin(Stick.Origin);
            }
        }

        public void StickMove(Vec3 screenPoint) => Stick.Drag(screenPoint, Radius * Settings.JoystickScale);

        /// <summary>Keyboard/desktop fallback that feeds the same movement path as touch.</summary>
        public void SetStickManual(Vec3 value)
        {
            float magnitude = MathF.Min(1f, value.FlatLength);
            Stick.Active = magnitude > 0.01f;
            Stick.Value = magnitude > 0.01f ? value.Flat.Normalized * magnitude : Vec3.Zero;
            Stick.Magnitude = magnitude;
        }
        public void StickUp() => Stick.End();

        public void ButtonDown(ActionButton button)
        {
            LastPressed = button;
            HeldButton = button;
            if (button == ActionButton.Shoot)
            {
                Charge = 0f;
                ChargeTime = 0f;
            }
            if (button == ActionButton.Sprint) SprintHeld = true;
        }

        public void ButtonUp(ActionButton button)
        {
            if (HeldButton == button) HeldButton = ActionButton.None;
            if (button == ActionButton.Sprint) SprintHeld = false;
        }

        /// <summary>Charge grows in 0.85 s from tap to full power, then holds.</summary>
        public void Tick(float dt)
        {
            if (HeldButton == ActionButton.Shoot)
            {
                ChargeTime += dt;
                Charge = PitchMath.Clamp01(ChargeTime / 0.85f);
            }
            else
            {
                Charge = MathF.Max(0f, Charge - dt * 4f);
                if (Charge <= 0f) ChargeTime = 0f;
            }
        }

        public float ConsumeShotCharge()
        {
            float value = MathF.Max(0.18f, Charge);
            Charge = 0f;
            ChargeTime = 0f;
            HeldButton = ActionButton.None;
            return value;
        }

        public string ContextualLabel => Contextual switch
        {
            ContextualAction.None => "",
            ContextualAction.SlideTackle => "SLIDE",
            ContextualAction.Clear => "CLEAR",
            ContextualAction.SwitchPlayer => "SWITCH",
            ContextualAction.FinesseShot => "FINESSE",
            ContextualAction.Header => "HEADER",
            ContextualAction.Throw => "THROW",
            _ => "PRESS",
        };
    }

    public static class ContextualActionExtensions
    {
        /// <summary>Left/right mirror for the on-screen layout, used by the UI builder.</summary>
        public static float LayoutX(this ControlSettings settings, float normalisedX)
            => settings.LeftHanded ? 1f - normalisedX : normalisedX;
    }

    public enum QualityTier { Low, Medium, High }

    /// <summary>
    /// Frame budget and device tier model. The renderer asks this class how much
    /// it may spend, the class adapts resolution from the measured frame time.
    /// </summary>
    public sealed class QualityModel
    {
        public QualityTier Tier = QualityTier.High;
        public float TargetFps = 60f;
        public float FrameBudgetMs => 1000f / TargetFps;
        public float RenderScale = 1f;
        public float MinRenderScale = 0.7f;
        public float MaxRenderScale = 1f;
        public float AverageFrameMs = 16.6f;
        public bool DynamicResolution = true;
        public int CrowdInstances = 12000;
        public float ShadowDistance = 60f;
        public float LodBias = 1.4f;

        public void ApplyTier(QualityTier tier)
        {
            Tier = tier;
            switch (tier)
            {
                case QualityTier.Low:
                    CrowdInstances = 4000;
                    ShadowDistance = 32f;
                    MinRenderScale = 0.62f;
                    LodBias = 0.9f;
                    break;
                case QualityTier.Medium:
                    CrowdInstances = 8000;
                    ShadowDistance = 48f;
                    MinRenderScale = 0.7f;
                    LodBias = 1.15f;
                    break;
                default:
                    CrowdInstances = 16000;
                    ShadowDistance = 70f;
                    MinRenderScale = 0.75f;
                    LodBias = 1.5f;
                    break;
            }
        }

        public void SampleFrame(float frameMs)
        {
            AverageFrameMs = PitchMath.Lerp(AverageFrameMs, frameMs, 0.05f);
            if (!DynamicResolution) return;
            float budget = FrameBudgetMs;
            if (AverageFrameMs > budget * 1.06f)
                RenderScale = MathF.Max(MinRenderScale, RenderScale - 0.02f);
            else if (AverageFrameMs < budget * 0.82f)
                RenderScale = MathF.Min(MaxRenderScale, RenderScale + 0.01f);
        }

        public bool WithinBudget => AverageFrameMs <= FrameBudgetMs + 0.5f;
        public float MetresPerLod(int level) => (25f + level * 22f) * LodBias;
    }
}
