using System.Collections.Generic;
using Pitch.Core;
using UnityEngine;

namespace Pitch.Game
{
    /// <summary>
    /// Renders the eleven-a-side match: original procedural player bodies, the
    /// highlighted match ball and the blue possession ring that only ever appears
    /// under the home team's ball carrier.
    /// </summary>
    public sealed class MatchView : MonoBehaviour
    {
        private MatchEngine _match;
        private StadiumView _stadium;
        private readonly List<PlayerVisual> _players = new List<PlayerVisual>();
        private BallView _ball;
        private PossessionRing _ring;
        private Transform _root;

        public static MatchView Create(Transform parent, MatchEngine match, StadiumView stadium)
        {
            var go = new GameObject("Match View");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MatchView>();
            view._root = go.transform;
            view.Bind(match, stadium);
            return view;
        }

        private void Bind(MatchEngine match, StadiumView stadium)
        {
            _match = match;
            _stadium = stadium;
            Rebuild();
        }

        public void Rebind(MatchEngine match)
        {
            _match = match;
            foreach (var p in _players) Destroy(p.gameObject);
            _players.Clear();
            if (_ball != null) Destroy(_ball.gameObject);
            if (_ring != null) Destroy(_ring.gameObject);
            Rebuild();
        }

        private void Rebuild()
        {
            if (_match == null) return;
            var homeKit = KitPalette.Home;
            var awayKit = KitPalette.Away;
            foreach (var team in new[] { _match.Home, _match.Away })
            {
                var kit = team.Side == TeamSide.Home ? homeKit : awayKit;
                foreach (var player in team.Players)
                {
                    var visual = PlayerVisual.Create(_root, player, kit);
                    _players.Add(visual);
                }
            }

            _ball = BallView.Create(_root);
            _ring = PossessionRing.Create(_root);
        }

        private void Update()
        {
            if (_match == null) return;
            float alpha = _match.InterpolationAlpha;

            foreach (var visual in _players)
            {
                if (visual == null) continue;
                visual.Render(alpha, Time.time);
            }

            if (_ball != null)
            {
                _ball.Render(_match, alpha, Time.time);
            }

            if (_ring != null)
            {
                _ring.Render(_match, alpha, Time.time);
            }
        }

        private void OnDestroy()
        {
            foreach (var visual in _players) if (visual != null) Destroy(visual.gameObject);
            if (_ball != null) Destroy(_ball.gameObject);
            if (_ring != null) Destroy(_ring.gameObject);
        }
    }

    public static class KitPalette
    {
        public struct Kit
        {
            public Color Shirt;
            public Color Shorts;
            public Color Socks;
            public Color Accent;
            public Kit(Color shirt, Color shorts, Color socks, Color accent)
            {
                Shirt = shirt; Shorts = shorts; Socks = socks; Accent = accent;
            }
        }

        // Original club identities, no licensed kit or badge.
        public static readonly Kit Home = new Kit(
            new Color(0.05f, 0.32f, 0.42f), new Color(0.92f, 0.93f, 0.95f),
            new Color(0.05f, 0.32f, 0.42f), new Color(0.55f, 0.86f, 0.92f));
        public static readonly Kit Away = new Kit(
            new Color(0.55f, 0.07f, 0.12f), new Color(0.12f, 0.11f, 0.12f),
            new Color(0.55f, 0.07f, 0.12f), new Color(0.95f, 0.78f, 0.42f));
    }

    /// <summary>
    /// Procedural, instanced player. The body is built from shared low-poly
    /// primitives with PBR materials; the run cycle, lean, kick and celebration
    /// poses are procedural. This is not a scanned human and the documentation
    /// says so.
    /// </summary>
    public sealed class PlayerVisual : MonoBehaviour
    {
        private PlayerAgent _agent;
        private Transform _body;
        private Transform _head;
        private Transform _leftArm;
        private Transform _rightArm;
        private Transform _leftLeg;
        private Transform _rightLeg;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private float _bobPhase;
        private Vector3 _lastPosition;

        public static PlayerVisual Create(Transform parent, PlayerAgent agent, KitPalette.Kit kit)
        {
            var go = new GameObject($"Player {agent.Side} {agent.Number}");
            go.transform.SetParent(parent, false);
            var visual = go.AddComponent<PlayerVisual>();
            visual.Build(agent, kit);
            return visual;
        }

        private void Build(PlayerAgent agent, KitPalette.Kit kit)
        {
            _agent = agent;
            _block = new MaterialPropertyBlock();
            _bobPhase = agent.Number * 0.7f;

            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);

            var shirt = MakePart(PrimitiveType.Capsule, "Torso", _body, new Vector3(0f, 1.02f, 0f),
                new Vector3(0.42f, 0.34f, 0.26f), kit.Shirt);
            var shorts = MakePart(PrimitiveType.Cube, "Shorts", _body, new Vector3(0f, 0.78f, 0f),
                new Vector3(0.36f, 0.22f, 0.24f), kit.Shorts);
            _head = MakePart(PrimitiveType.Sphere, "Head", _body, new Vector3(0f, 1.52f, 0f),
                Vector3.one * 0.23f, new Color(0.72f, 0.55f, 0.42f)).transform;
            var hair = MakePart(PrimitiveType.Sphere, "Hair", _head, new Vector3(0f, 0.03f, -0.02f),
                new Vector3(0.24f, 0.16f, 0.24f), new Color(0.09f, 0.07f, 0.06f));

            _leftArm = MakePart(PrimitiveType.Capsule, "LeftArm", _body, new Vector3(-0.31f, 1.06f, 0f),
                new Vector3(0.11f, 0.3f, 0.11f), kit.Shirt).transform;
            _rightArm = MakePart(PrimitiveType.Capsule, "RightArm", _body, new Vector3(0.31f, 1.06f, 0f),
                new Vector3(0.11f, 0.3f, 0.11f), kit.Shirt).transform;
            _leftLeg = MakePart(PrimitiveType.Capsule, "LeftLeg", _body, new Vector3(-0.13f, 0.45f, 0f),
                new Vector3(0.13f, 0.34f, 0.13f), kit.Socks).transform;
            _rightLeg = MakePart(PrimitiveType.Capsule, "RightLeg", _body, new Vector3(0.13f, 0.45f, 0f),
                new Vector3(0.13f, 0.34f, 0.13f), kit.Socks).transform;
            MakePart(PrimitiveType.Cube, "LeftBoot", _leftLeg, new Vector3(0f, -0.32f, 0.06f),
                new Vector3(0.13f, 0.08f, 0.3f), new Color(0.05f, 0.05f, 0.05f));
            MakePart(PrimitiveType.Cube, "RightBoot", _rightLeg, new Vector3(0f, -0.32f, 0.06f),
                new Vector3(0.13f, 0.08f, 0.3f), new Color(0.05f, 0.05f, 0.05f));

            _renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in _renderers)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.gameObject.layer = Layers.Players;
            }
            transform.localScale = Vector3.one;
            _lastPosition = agent.Position.ToUnity();
        }

        // 22 players x 10 body parts would otherwise allocate 220 materials and
        // defeat batching. Parts share one material per colour.
        private static readonly Dictionary<int, Material> PartMaterials = new Dictionary<int, Material>();

        private static Material PartMaterial(Color color)
        {
            int key = (Mathf.RoundToInt(color.r * 255f) << 16)
                      | (Mathf.RoundToInt(color.g * 255f) << 8)
                      | Mathf.RoundToInt(color.b * 255f);
            if (PartMaterials.TryGetValue(key, out var cached) && cached != null) return cached;
            var material = new Material(ShaderLibrary.PlayerLit)
            {
                color = color,
                enableInstancing = true,
            };
            material.SetColor("_BaseColor", color);
            PartMaterials[key] = material;
            return material;
        }

        private static GameObject MakePart(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
            Vector3 scale, Color color)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = PartMaterial(color);
            return part;
        }

        public void Render(float alpha, float time)
        {
            if (_agent == null) return;
            Vector3 previous = _agent.PreviousPosition.ToUnity();
            Vector3 current = _agent.Position.ToUnity();
            Vector3 position = Vector3.Lerp(previous, current, alpha);
            transform.position = position;

            float speed = _agent.Velocity.FlatLength;
            Quaternion facing = Quaternion.Euler(0f, _agent.Facing * Mathf.Rad2Deg, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, facing, 0.35f);

            bool moving = speed > 0.2f;
            _bobPhase += Time.deltaTime * (moving ? 9f + speed : 1.5f);
            float bob = moving ? Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.055f : 0f;
            float lean = moving ? Mathf.Clamp(speed / 8f, 0f, 0.22f) : 0f;
            _body.localPosition = new Vector3(0f, bob, 0f);
            _body.localRotation = Quaternion.Euler(lean * 52f, 0f, 0f);

            float swing = moving ? Mathf.Sin(_bobPhase) * (18f + speed * 4f) : 0f;
            _leftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
            _rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            _leftArm.localRotation = Quaternion.Euler(-swing * 0.7f, 0f, 8f);
            _rightArm.localRotation = Quaternion.Euler(swing * 0.7f, 0f, -8f);

            switch (_agent.State)
            {
                case PlayerState.Shoot:
                case PlayerState.Pass:
                    _rightLeg.localRotation = Quaternion.Euler(-55f, 0f, 0f);
                    _leftArm.localRotation = Quaternion.Euler(25f, 0f, 30f);
                    break;
                case PlayerState.Tackle:
                    _body.localRotation = Quaternion.Euler(35f, 0f, 0f);
                    break;
                case PlayerState.Celebrate:
                    Celebrate(time);
                    break;
                case PlayerState.KeeperDive:
                    _body.localRotation = Quaternion.Euler(0f, 0f, 72f);
                    break;
                case PlayerState.Down:
                    _body.localRotation = Quaternion.Euler(88f, 0f, 0f);
                    _body.localPosition = new Vector3(0f, 0.25f, 0f);
                    break;
            }
        }

        private void Celebrate(float time)
        {
            switch (_agent.Celebration)
            {
                case CelebrateStyle.KneeSlide:
                    _body.localPosition = new Vector3(0f, -0.3f, 0f);
                    _body.localRotation = Quaternion.Euler(-16f, 0f, 0f);
                    _leftArm.localRotation = Quaternion.Euler(0f, 0f, 65f);
                    _rightArm.localRotation = Quaternion.Euler(0f, 0f, -65f);
                    break;
                case CelebrateStyle.JumpPunch:
                    _body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(time * 7f)) * 0.3f, 0f);
                    _leftArm.localRotation = Quaternion.Euler(160f, 0f, 0f);
                    _rightArm.localRotation = Quaternion.Euler(160f, 0f, 0f);
                    break;
                case CelebrateStyle.ShirtPull:
                    _body.localRotation = Quaternion.Euler(-8f, 0f, 0f);
                    _leftArm.localRotation = Quaternion.Euler(90f, 0f, -40f);
                    _rightArm.localRotation = Quaternion.Euler(90f, 0f, 40f);
                    break;
                case CelebrateStyle.TeamHuddle:
                    _leftArm.localRotation = Quaternion.Euler(0f, 0f, 55f);
                    _rightArm.localRotation = Quaternion.Euler(0f, 0f, -55f);
                    break;
                default:
                    _leftArm.localRotation = Quaternion.Euler(0f, 0f, 78f);
                    _rightArm.localRotation = Quaternion.Euler(0f, 0f, -78f);
                    break;
            }
        }
    }

    /// <summary>
    /// The match ball with a rim light, an additive glow shell and a motion
    /// trail so it stays readable against grass, kits and crowd.
    /// </summary>
    public sealed class BallView : MonoBehaviour
    {
        private MatchEngine _match;
        private Transform _ball;
        private Transform _glow;
        private TrailRenderer _trail;
        private Vector3 _lastPosition;
        private bool _highlight = true;

        public static BallView Create(Transform parent)
        {
            var go = new GameObject("Match Ball");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<BallView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Ball";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * (PitchSpec.BallRadius * 2f);
            Object.Destroy(sphere.GetComponent<Collider>());
            var renderer = sphere.GetComponent<Renderer>();
            var material = new Material(ShaderLibrary.BallLit);
            material.color = new Color(0.97f, 0.97f, 0.92f);
            material.enableInstancing = true;
            renderer.sharedMaterial = material;
            _ball = sphere.transform;

            var glowSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glowSphere.name = "Ball Glow";
            glowSphere.transform.SetParent(_ball, false);
            glowSphere.transform.localScale = Vector3.one * 1.45f;
            Object.Destroy(glowSphere.GetComponent<Collider>());
            var glowRenderer = glowSphere.GetComponent<Renderer>();
            var glowMaterial = new Material(ShaderLibrary.BallHighlight);
            glowMaterial.SetColor("_Color", new Color(1f, 0.95f, 0.55f, 0.55f));
            glowRenderer.sharedMaterial = glowMaterial;
            glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _glow = glowSphere.transform;

            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = 0.28f;
            _trail.startWidth = 0.12f;
            _trail.endWidth = 0.0f;
            _trail.material = new Material(ShaderLibrary.Trail);
            _trail.startColor = new Color(1f, 0.95f, 0.6f, 0.7f);
            _trail.endColor = new Color(1f, 0.95f, 0.6f, 0f);
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lastPosition = transform.position;
        }

        public void Render(MatchEngine match, float alpha, float time)
        {
            _match = match;
            Vector3 position = match.Ball.Interpolated(alpha).ToUnity();
            transform.position = position;

            float speed = match.Ball.Velocity.Length;
            _ball.Rotate(Vector3.right, speed * Time.deltaTime * 35f, Space.World);
            _ball.Rotate(Vector3.up, match.Ball.Spin.Y * Time.deltaTime, Space.World);
            bool highlight = _highlight && match.Presentation.BallHighlight;
            if (_glow != null)
            {
                float pulse = 1f + Mathf.Sin(time * 6f) * 0.06f;
                _glow.localScale = Vector3.one * (1.45f * pulse);
                _glow.gameObject.SetActive(highlight);
            }
            if (_trail != null)
            {
                bool fast = speed > 5f;
                _trail.emitting = fast && highlight;
                _trail.time = Mathf.Clamp(speed * 0.04f, 0.08f, 0.5f);
            }

            // Rotation from the engine spin gives a believable rolling ball.
            Vector3 delta = position - _lastPosition;
            if (delta.sqrMagnitude > 1e-5f && delta.magnitude < 5f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, delta.normalized);
                _ball.Rotate(axis, delta.magnitude / PitchSpec.BallRadius * Mathf.Rad2Deg, Space.World);
            }
            _lastPosition = position;
        }

        public void SetHighlight(bool enabled)
        {
            _highlight = enabled;
            if (_glow != null) _glow.gameObject.SetActive(enabled);
        }
    }

    /// <summary>
    /// Blue ground ring under the home team's ball carrier only. It never
    /// appears for the opposition, fades under the ball and sits 2 cm above the
    /// pitch to avoid z-fighting.
    /// </summary>
    public sealed class PossessionRing : MonoBehaviour
    {
        private MatchEngine _match;
        private GameObject _ring;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private Transform _transform;
        private Vector3 _currentPosition;
        private float _opacity;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static PossessionRing Create(Transform parent)
        {
            var go = new GameObject("Possession Ring");
            go.transform.SetParent(parent, false);
            var ring = go.AddComponent<PossessionRing>();
            ring.Build();
            return ring;
        }

        private void Build()
        {
            _transform = transform;
            _ring = new GameObject("Blue Ring");
            _ring.transform.SetParent(transform, false);
            var meshFilter = _ring.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = BuildRingMesh(0.42f, 0.62f, 64);
            var renderer = _ring.AddComponent<MeshRenderer>();
            var material = new Material(ShaderLibrary.Ring);
            material.SetColor("_Color", new Color(0.16f, 0.62f, 1f, 0.92f));
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _renderer = renderer;
            _block = new MaterialPropertyBlock();
            _ring.SetActive(false);
        }

        private static Mesh BuildRingMesh(float inner, float outer, int segments)
        {
            var mesh = new Mesh { name = "Possession Ring Mesh" };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);
                vertices.Add(new Vector3(sin * inner, 0f, cos * inner));
                vertices.Add(new Vector3(sin * outer, 0f, cos * outer));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1);
                triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 3);
            }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Render(MatchEngine match, float alpha, float time)
        {
            _match = match;
            bool visible = match.RingOwnerSide == TeamSide.Home && match.Carrier != null;
            if (_ring == null) return;
            if (!visible)
            {
                _opacity = Mathf.MoveTowards(_opacity, 0f, Time.deltaTime * 5f);
                _ring.SetActive(_opacity > 0.01f);
                if (_ring.activeSelf) ApplyOpacity(_opacity);
                return;
            }

            var carrier = match.Carrier;
            Vector3 target = Vector3.Lerp(carrier.PreviousPosition.ToUnity(), carrier.Position.ToUnity(), alpha);
            // Smooth follow with a short lag, never clipping into the pitch.
            _currentPosition = Vector3.Lerp(_currentPosition, target, 1f - Mathf.Exp(-22f * Time.deltaTime));
            float height = Mathf.Max(0.02f, carrier.Position.Y + 0.02f);
            _transform.position = new Vector3(_currentPosition.x, height, _currentPosition.z);
            float scale = 1f + Mathf.Clamp01(carrier.Velocity.FlatLength / 9f) * 0.12f;
            _transform.localScale = new Vector3(scale, 1f, scale);
            _opacity = Mathf.MoveTowards(_opacity, 1f, Time.deltaTime * 7f);
            _ring.SetActive(true);
            ApplyOpacity(_opacity);
        }

        private void ApplyOpacity(float opacity)
        {
            if (_renderer == null) return;
            _block.SetColor(ColorId, new Color(0.16f, 0.62f, 1f, opacity * 0.92f));
            _renderer.SetPropertyBlock(_block);
        }
    }

    /// <summary>Shader references resolved by name so a missing shader degrades to a lit material.</summary>
    public static class ShaderLibrary
    {
        private static Shader _playerLit;
        private static Shader _ballLit;
        private static Shader _ballHighlight;
        private static Shader _ring;
        private static Shader _trail;
        private static Shader _grass;
        private static Shader _crowd;

        public static Shader PlayerLit => _playerLit ??= Find("Universal Render Pipeline/Lit", "Standard");
        public static Shader BallLit => _ballLit ??= Find("Universal Render Pipeline/Lit", "Standard");
        public static Shader BallHighlight => _ballHighlight ??= Find("Pitch/BallHighlight", "Universal Render Pipeline/Unlit", "Unlit/Color");
        public static Shader Ring => _ring ??= Find("Pitch/PossessionRing", "Universal Render Pipeline/Unlit", "Unlit/Transparent");
        public static Shader Trail => _trail ??= Find("Universal Render Pipeline/Particles/Unlit", "Sprites/Default");
        public static Shader Grass => _grass ??= Find("Pitch/Grass", "Universal Render Pipeline/Lit", "Standard");
        public static Shader Crowd => _crowd ??= Find("Pitch/Crowd", "Universal Render Pipeline/Lit", "Standard");

        private static Shader Find(params string[] names)
        {
            foreach (var name in names)
            {
                var shader = Shader.Find(name);
                if (shader != null) return shader;
            }
            return Shader.Find("Standard");
        }
    }
}
