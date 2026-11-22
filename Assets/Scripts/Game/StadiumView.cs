using System.Collections.Generic;
using Pitch.Core;
using UnityEngine;

namespace Pitch.Game
{
    /// <summary>
    /// Procedural stadium: pitch with mowing pattern, all markings, goals with
    /// nets, four raked stands with an instanced crowd, tifo mosaic, LED boards,
    /// floodlights and a sky. Nothing is imported from a real stadium.
    /// </summary>
    public sealed class StadiumView : MonoBehaviour
    {
        private MatchEngine _match;
        private Transform _root;
        private Mesh _linesMesh;
        private CrowdRenderer _crowd;
        private Material _grassMaterial;
        private Transform _trophyPlinth;

        public static StadiumView Create(Transform parent, MatchEngine match)
        {
            var go = new GameObject("Stadium");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<StadiumView>();
            view._root = go.transform;
            view.Build(match);
            return view;
        }

        public void Rebind(MatchEngine match)
        {
            _match = match;
            if (_crowd != null) _crowd.Rebind(match);
        }

        private void Build(MatchEngine match)
        {
            _match = match;
            BuildSky();
            BuildPitch();
            BuildMarkings();
            BuildGoals();
            BuildStands();
            BuildAds();
            BuildFloodlights();
            BuildTrophyPlinth();
        }

        private void BuildSky()
        {
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader == null)
            {
                Debug.LogWarning("[Pitch] procedural skybox shader missing; keeping the default skybox");
                return;
            }
            var sky = new Material(skyShader);
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", 0.045f);
            sky.SetFloat("_AtmosphereThickness", 1.05f);
            sky.SetColor("_SkyTint", new Color(0.52f, 0.68f, 0.9f));
            sky.SetColor("_GroundColor", new Color(0.24f, 0.27f, 0.24f));
            sky.SetFloat("_Exposure", 1.15f);
            RenderSettings.skybox = sky;
        }

        private void BuildPitch()
        {
            var pitch = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pitch.name = "Pitch";
            pitch.transform.SetParent(_root, false);
            pitch.transform.localScale = new Vector3(PitchSpec.Width / 10f, 1f, PitchSpec.Length / 10f);
            pitch.transform.localPosition = Vector3.zero;
            pitch.layer = Layers.Pitch;

            _grassMaterial = new Material(ShaderLibrary.Grass);
            var albedo = LoadTexture("grass_albedo", false);
            var normal = LoadTexture("grass_normal", true);
            if (albedo != null) _grassMaterial.SetTexture("_BaseMap", albedo);
            if (normal != null) _grassMaterial.SetTexture("_BumpMap", normal);
            _grassMaterial.SetFloat("_Smoothness", 0.18f);
            _grassMaterial.SetFloat("_StripeScale", 1.5f);
            _grassMaterial.enableInstancing = true;
            pitch.GetComponent<Renderer>().sharedMaterial = _grassMaterial;

            // Runoff / technical area.
            var runoff = GameObject.CreatePrimitive(PrimitiveType.Plane);
            runoff.name = "Runoff";
            runoff.transform.SetParent(_root, false);
            runoff.transform.localScale = new Vector3((PitchSpec.Width + 12f) / 10f, 1f, (PitchSpec.Length + 14f) / 10f);
            runoff.transform.localPosition = new Vector3(0f, -0.03f, 0f);
            var runoffMat = new Material(ShaderLibrary.PlayerLit);
            runoffMat.color = new Color(0.11f, 0.22f, 0.13f);
            runoffMat.SetColor("_BaseColor", new Color(0.11f, 0.22f, 0.13f));
            runoff.GetComponent<Renderer>().sharedMaterial = runoffMat;
        }

        private static Texture2D LoadTexture(string name, bool linear)
        {
            var texture = Resources.Load<Texture2D>("Generated/Textures/" + name);
            if (texture == null) return null;
            return texture;
        }

        private void BuildMarkings()
        {
            var material = new Material(ShaderLibrary.PlayerLit);
            material.color = new Color(0.93f, 0.95f, 0.93f);
            material.SetColor("_BaseColor", new Color(0.93f, 0.95f, 0.93f));
            material.SetFloat("_Smoothness", 0.1f);

            var go = new GameObject("Pitch Markings");
            go.transform.SetParent(_root, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            _linesMesh = BuildLineMesh();
            filter.sharedMesh = _linesMesh;
            go.transform.localPosition = new Vector3(0f, 0.012f, 0f);
        }

        private static Mesh BuildLineMesh()
        {
            var mesh = new Mesh { name = "Pitch Lines" };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            const float w = 0.12f;

            void Segment(float x1, float z1, float x2, float z2)
            {
                Vector3 a = new Vector3(x1, 0f, z1);
                Vector3 b = new Vector3(x2, 0f, z2);
                Vector3 dir = (b - a).normalized;
                Vector3 normal = new Vector3(-dir.z, 0f, dir.x) * (w * 0.5f);
                int index = vertices.Count;
                vertices.Add(a - normal); vertices.Add(a + normal);
                vertices.Add(b - normal); vertices.Add(b + normal);
                triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 1);
                triangles.Add(index + 1); triangles.Add(index + 2); triangles.Add(index + 3);
            }

            void Arc(float cx, float cz, float radius, float startDeg, float endDeg, int steps = 32)
            {
                float start = startDeg * Mathf.Deg2Rad;
                float end = endDeg * Mathf.Deg2Rad;
                Vector3 prev = new Vector3(cx + Mathf.Cos(start) * radius, 0f, cz + Mathf.Sin(start) * radius);
                for (int i = 1; i <= steps; i++)
                {
                    float t = Mathf.Lerp(start, end, i / (float)steps);
                    Vector3 next = new Vector3(cx + Mathf.Cos(t) * radius, 0f, cz + Mathf.Sin(t) * radius);
                    Segment(prev.x, prev.z, next.x, next.z);
                    prev = next;
                }
            }

            float hl = PitchSpec.HalfLength;
            float hw = PitchSpec.HalfWidth;

            // Touchlines and goal lines.
            Segment(-hw, -hl, hw, -hl);
            Segment(-hw, hl, hw, hl);
            Segment(-hw, -hl, -hw, hl);
            Segment(hw, -hl, hw, hl);

            // Halfway line across the width, centre circle and centre spot.
            Segment(-hw, 0f, hw, 0f);
            Arc(0f, 0f, PitchSpec.CentreCircleRadius, 0f, 360f, 64);
            AddSpot(vertices, triangles, 0f, 0f, w);

            // Penalty and goal areas at both ends.
            for (int side = -1; side <= 1; side += 2)
            {
                float goalLine = side * hl;
                float pa = -side * PitchSpec.PenaltyAreaLength;
                float ga = -side * PitchSpec.GoalAreaLength;
                Segment(-PitchSpec.PenaltyAreaHalfWidth, goalLine, -PitchSpec.PenaltyAreaHalfWidth, goalLine + pa);
                Segment(PitchSpec.PenaltyAreaHalfWidth, goalLine, PitchSpec.PenaltyAreaHalfWidth, goalLine + pa);
                Segment(-PitchSpec.PenaltyAreaHalfWidth, goalLine + pa, PitchSpec.PenaltyAreaHalfWidth, goalLine + pa);
                Segment(-PitchSpec.GoalAreaHalfWidth, goalLine, -PitchSpec.GoalAreaHalfWidth, goalLine + ga);
                Segment(PitchSpec.GoalAreaHalfWidth, goalLine, PitchSpec.GoalAreaHalfWidth, goalLine + ga);
                Segment(-PitchSpec.GoalAreaHalfWidth, goalLine + ga, PitchSpec.GoalAreaHalfWidth, goalLine + ga);
                AddSpot(vertices, triangles, 0f, goalLine - side * PitchSpec.PenaltySpot, w);
                // The penalty arc only shows the part outside the penalty area:
                // for the +z goal that is 217..323 degrees, for the -z goal 37..143.
                Arc(0f, goalLine - side * PitchSpec.PenaltySpot, PitchSpec.PenaltyArcRadius,
                    side > 0 ? 217f : 37f, side > 0 ? 323f : 143f, 24);
                // Corner arcs, each a quarter circle inside the pitch.
                Arc(-hw, goalLine, PitchSpec.CornerArcRadius, side > 0 ? 270f : 0f, side > 0 ? 360f : 90f, 12);
                Arc(hw, goalLine, PitchSpec.CornerArcRadius, side > 0 ? 180f : 90f, side > 0 ? 270f : 180f, 12);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddSpot(List<Vector3> vertices, List<int> triangles, float x, float z, float radius)
        {
            int index = vertices.Count;
            vertices.Add(new Vector3(x - radius, 0f, z - radius));
            vertices.Add(new Vector3(x + radius, 0f, z - radius));
            vertices.Add(new Vector3(x + radius, 0f, z + radius));
            vertices.Add(new Vector3(x - radius, 0f, z + radius));
            triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
            triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 3);
        }

        private void BuildGoals()
        {
            var postMaterial = new Material(ShaderLibrary.PlayerLit);
            postMaterial.color = new Color(0.96f, 0.97f, 0.98f);
            postMaterial.SetColor("_BaseColor", new Color(0.96f, 0.97f, 0.98f));
            postMaterial.SetFloat("_Metallic", 0.65f);
            postMaterial.SetFloat("_Smoothness", 0.55f);

            var netShader = Shader.Find("Pitch/Net");
            var netMaterial = netShader != null ? new Material(netShader) : new Material(ShaderLibrary.Trail);
            netMaterial.color = new Color(1f, 1f, 1f, 0.35f);

            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * PitchSpec.HalfLength;
                var goal = new GameObject(side > 0 ? "Away Goal" : "Home Goal");
                goal.transform.SetParent(_root, false);
                foreach (float x in new[] { -PitchSpec.GoalHalfWidth, PitchSpec.GoalHalfWidth })
                    AddPost(goal.transform, new Vector3(x, PitchSpec.GoalHeight * 0.5f, 0f),
                        new Vector3(0.12f, PitchSpec.GoalHeight, 0.12f), postMaterial);
                AddPost(goal.transform, new Vector3(0f, PitchSpec.GoalHeight, 0f),
                    new Vector3(PitchSpec.GoalWidth + 0.12f, 0.12f, 0.12f), postMaterial);

                // Net: three translucent panels.
                AddPanel(goal.transform, new Vector3(0f, PitchSpec.GoalHeight * 0.5f, side * PitchSpec.GoalDepth),
                    new Vector3(PitchSpec.GoalWidth, PitchSpec.GoalHeight, 0.02f), netMaterial);
                AddPanel(goal.transform, new Vector3(-PitchSpec.GoalHalfWidth, PitchSpec.GoalHeight * 0.5f, side * PitchSpec.GoalDepth * 0.5f),
                    new Vector3(0.02f, PitchSpec.GoalHeight, PitchSpec.GoalDepth), netMaterial);
                AddPanel(goal.transform, new Vector3(PitchSpec.GoalHalfWidth, PitchSpec.GoalHeight * 0.5f, side * PitchSpec.GoalDepth * 0.5f),
                    new Vector3(0.02f, PitchSpec.GoalHeight, PitchSpec.GoalDepth), netMaterial);
                AddPanel(goal.transform, new Vector3(0f, PitchSpec.GoalHeight, side * PitchSpec.GoalDepth * 0.5f),
                    new Vector3(PitchSpec.GoalWidth, 0.02f, PitchSpec.GoalDepth), netMaterial);
                goal.transform.localPosition = new Vector3(0f, 0f, z);
            }
        }

        private static void AddPost(Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Goal Frame";
            post.transform.SetParent(parent, false);
            post.transform.localPosition = position;
            post.transform.localScale = scale;
            Object.Destroy(post.GetComponent<Collider>());
            post.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void AddPanel(Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Net";
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = position;
            panel.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            if (Mathf.Abs(scale.z) > 0.05f)
                panel.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Object.Destroy(panel.GetComponent<Collider>());
            panel.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void BuildStands()
        {
            var concrete = new Material(ShaderLibrary.PlayerLit);
            concrete.color = new Color(0.22f, 0.24f, 0.27f);
            concrete.SetColor("_BaseColor", new Color(0.22f, 0.24f, 0.27f));
            concrete.SetFloat("_Smoothness", 0.12f);

            // Four raked concrete wedges around the pitch.
            BuildStand(new Vector3(0f, 0f, -PitchSpec.HalfLength - 18f), new Vector3(PitchSpec.Width + 44f, 13f, 20f), concrete, 0f);
            BuildStand(new Vector3(0f, 0f, PitchSpec.HalfLength + 18f), new Vector3(PitchSpec.Width + 44f, 13f, 20f), concrete, 180f);
            BuildStand(new Vector3(-PitchSpec.HalfWidth - 22f, 0f, 0f), new Vector3(PitchSpec.Length + 30f, 11f, 15f), concrete, 90f);
            BuildStand(new Vector3(PitchSpec.HalfWidth + 22f, 0f, 0f), new Vector3(PitchSpec.Length + 30f, 11f, 15f), concrete, -90f);

            _crowd = CrowdRenderer.Create(_root, _match, 12000);
        }

        private void BuildStand(Vector3 centre, Vector3 size, Material material, float yaw)
        {
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stand.name = "Stand";
            stand.transform.SetParent(_root, false);
            stand.transform.localPosition = centre + Vector3.up * (size.y * 0.5f);
            stand.transform.localScale = size;
            stand.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Object.Destroy(stand.GetComponent<Collider>());
            stand.GetComponent<Renderer>().sharedMaterial = material;

            // Roof.
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "Roof";
            roof.transform.SetParent(_root, false);
            roof.transform.localPosition = centre + Vector3.up * (size.y + 0.4f);
            roof.transform.localScale = new Vector3(size.x, 0.4f, size.z * 1.1f);
            roof.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Object.Destroy(roof.GetComponent<Collider>());
            roof.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void BuildAds()
        {
            string[] slogans = { "MATCHDAY ELEVEN", "PLAY FAIR", "NORTHPORT", "AURELIA", "TOUCH. PASS. SCORE." };
            Color[] colours =
            {
                new Color(0.05f, 0.32f, 0.42f), new Color(0.85f, 0.72f, 0.25f),
                new Color(0.55f, 0.07f, 0.12f), new Color(0.12f, 0.35f, 0.18f),
                new Color(0.12f, 0.12f, 0.16f),
            };

            for (int i = 0; i < slogans.Length; i++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    board.name = "LED Board";
                    board.transform.SetParent(_root, false);
                    board.transform.localPosition = new Vector3(side * (PitchSpec.HalfWidth + 5f), 0.55f, -PitchSpec.Length * 0.35f + i * 14f);
                    board.transform.localScale = new Vector3(0.25f, 1.1f, 8f);
                    Object.Destroy(board.GetComponent<Collider>());
                    var material = new Material(ShaderLibrary.PlayerLit);
                    material.color = colours[i];
                    material.SetColor("_BaseColor", colours[i]);
                    material.SetColor("_EmissionColor", colours[i] * 1.4f);
                    material.EnableKeyword("_EMISSION");
                    board.GetComponent<Renderer>().sharedMaterial = material;

                    var label = new GameObject("Board Text");
                    label.transform.SetParent(board.transform, false);
                    label.transform.localPosition = new Vector3(-0.55f, 0f, 0f);
                    label.transform.localRotation = Quaternion.Euler(0f, side > 0 ? -90f : 90f, 0f);
                    var text = label.AddComponent<TextMesh>();
                    text.text = slogans[i];
                    text.font = BroadcastHUD.Font;
                    text.fontSize = 64;
                    text.characterSize = 0.08f;
                    text.anchor = TextAnchor.MiddleCenter;
                    text.color = new Color(0.95f, 0.95f, 0.95f);
                }
            }
        }

        private void BuildFloodlights()
        {
            var pole = new Material(ShaderLibrary.PlayerLit);
            pole.color = new Color(0.3f, 0.32f, 0.35f);
            pole.SetColor("_BaseColor", new Color(0.3f, 0.32f, 0.35f));
            var lamp = new Material(ShaderLibrary.PlayerLit);
            lamp.color = new Color(1f, 0.97f, 0.86f);
            lamp.SetColor("_BaseColor", new Color(1f, 0.97f, 0.86f));
            lamp.EnableKeyword("_EMISSION");
            lamp.SetColor("_EmissionColor", new Color(2.2f, 2f, 1.6f));

            foreach (var corner in new[]
                     {
                         new Vector3(-PitchSpec.HalfWidth - 20f, 0f, -PitchSpec.HalfLength - 20f),
                         new Vector3(PitchSpec.HalfWidth + 20f, 0f, -PitchSpec.HalfLength - 20f),
                         new Vector3(-PitchSpec.HalfWidth - 20f, 0f, PitchSpec.HalfLength + 20f),
                         new Vector3(PitchSpec.HalfWidth + 20f, 0f, PitchSpec.HalfLength + 20f),
                     })
            {
                var tower = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tower.name = "Floodlight Tower";
                tower.transform.SetParent(_root, false);
                tower.transform.localPosition = corner + Vector3.up * 18f;
                tower.transform.localScale = new Vector3(0.9f, 18f, 0.9f);
                Object.Destroy(tower.GetComponent<Collider>());
                tower.GetComponent<Renderer>().sharedMaterial = pole;

                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "Floodlight Panel";
                panel.transform.SetParent(tower.transform, false);
                panel.transform.localPosition = new Vector3(0f, 1.02f, 0f);
                panel.transform.localScale = new Vector3(4.2f, 0.9f, 1.2f);
                Object.Destroy(panel.GetComponent<Collider>());
                panel.GetComponent<Renderer>().sharedMaterial = lamp;
            }
        }

        private void BuildTrophyPlinth()
        {
            var plinth = new GameObject("Trophy Plinth");
            plinth.transform.SetParent(_root, false);
            plinth.transform.localPosition = new Vector3(0f, 0f, 0f);
            _trophyPlinth = plinth.transform;

            var baseMat = new Material(ShaderLibrary.PlayerLit);
            baseMat.color = new Color(0.16f, 0.14f, 0.12f);
            baseMat.SetColor("_BaseColor", new Color(0.16f, 0.14f, 0.12f));
            var gold = new Material(ShaderLibrary.PlayerLit);
            gold.color = new Color(0.95f, 0.74f, 0.28f);
            gold.SetColor("_BaseColor", new Color(0.95f, 0.74f, 0.28f));
            gold.SetFloat("_Metallic", 0.9f);
            gold.SetFloat("_Smoothness", 0.8f);

            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "Pedestal";
            pedestal.transform.SetParent(plinth.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            pedestal.transform.localScale = new Vector3(1.4f, 0.35f, 1.4f);
            Object.Destroy(pedestal.GetComponent<Collider>());
            pedestal.GetComponent<Renderer>().sharedMaterial = baseMat;

            // Original trophy: bowl, stem, foot, two curved handles.
            var cup = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cup.name = "Trophy Cup";
            cup.transform.SetParent(plinth.transform, false);
            cup.transform.localPosition = new Vector3(0f, 1.28f, 0f);
            cup.transform.localScale = new Vector3(0.42f, 0.36f, 0.42f);
            Object.Destroy(cup.GetComponent<Collider>());
            cup.GetComponent<Renderer>().sharedMaterial = gold;

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Trophy Stem";
            stem.transform.SetParent(plinth.transform, false);
            stem.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            stem.transform.localScale = new Vector3(0.1f, 0.22f, 0.1f);
            Object.Destroy(stem.GetComponent<Collider>());
            stem.GetComponent<Renderer>().sharedMaterial = gold;

            for (int side = -1; side <= 1; side += 2)
            {
                var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                handle.name = "Trophy Handle";
                handle.transform.SetParent(plinth.transform, false);
                handle.transform.localPosition = new Vector3(side * 0.31f, 1.32f, 0f);
                handle.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                handle.transform.localScale = new Vector3(0.07f, 0.16f, 0.07f);
                Object.Destroy(handle.GetComponent<Collider>());
                handle.GetComponent<Renderer>().sharedMaterial = gold;
            }

            var spot = new GameObject("Trophy Light").AddComponent<Light>();
            spot.transform.SetParent(plinth.transform, false);
            spot.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            spot.type = LightType.Point;
            spot.color = new Color(1f, 0.88f, 0.55f);
            spot.intensity = 3.2f;
            spot.range = 7f;
            spot.shadows = LightShadows.None;

            plinth.SetActive(false);
        }

        public void SetTrophyVisible(bool visible)
        {
            if (_trophyPlinth != null) _trophyPlinth.gameObject.SetActive(visible);
        }

        private void Update()
        {
            if (_crowd != null && _match != null) _crowd.Render(_match, Time.time);
        }
    }
}
