using System.Collections.Generic;
using Pitch.Core;
using UnityEngine;

namespace Pitch.Game
{
    /// <summary>
    /// Instanced crowd in the four stands. Per-instance colours carry the teams
    /// and, when the tifo is active, a mosaic pattern that flips across the home
    /// end. 12k instances are drawn in batched instanced calls, never one
    /// GameObject per spectator.
    /// </summary>
    public sealed class CrowdRenderer : MonoBehaviour
    {
        private MatchEngine _match;
        private Mesh _mesh;
        private Material _material;
        private Matrix4x4[][] _batches;
        private Vector4[][] _colourBatches;
        private MaterialPropertyBlock _block;
        private TifoPhase _lastPhase = TifoPhase.Idle;
        private int _count;
        private readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
        private readonly List<Vector4> _colours = new List<Vector4>();
        private System.Random _random = new System.Random(7);

        public static CrowdRenderer Create(Transform parent, MatchEngine match, int count)
        {
            var go = new GameObject("Crowd");
            go.transform.SetParent(parent, false);
            var crowd = go.AddComponent<CrowdRenderer>();
            crowd.Build(match, count);
            return crowd;
        }

        public void Rebind(MatchEngine match)
        {
            _match = match;
            _lastPhase = TifoPhase.Idle;
        }

        private void Build(MatchEngine match, int count)
        {
            _match = match;
            _count = Mathf.Clamp(count, 1000, 20000);
            _mesh = BuildPersonMesh();
            _material = new Material(ShaderLibrary.Crowd);
            _material.enableInstancing = true;
            _block = new MaterialPropertyBlock();
            BuildMatrices();
            BuildColours(TifoPhase.Idle);
        }

        private static Mesh BuildPersonMesh()
        {
            var mesh = new Mesh { name = "Crowd Person" };
            var vertices = new List<Vector3>
            {
                new Vector3(-0.22f, 0f, 0f), new Vector3(0.22f, 0f, 0f),
                new Vector3(0.22f, 0.9f, 0f), new Vector3(-0.22f, 0.9f, 0f),
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void BuildMatrices()
        {
            _matrices.Clear();
            _random = new System.Random(7);
            float hl = PitchSpec.HalfLength;
            float hw = PitchSpec.HalfWidth;

            int perStand = _count / 4;
            AddStand(perStand, new Vector3(0f, 0f, -hl - 12f), new Vector3(hw + 18f, 0f, 0f), 0f, 12);
            AddStand(perStand, new Vector3(0f, 0f, hl + 12f), new Vector3(-hw - 18f, 0f, 0f), 180f, 12);
            AddStand(perStand, new Vector3(-hw - 15f, 0f, 0f), new Vector3(0f, 0f, hl + 10f), 90f, 10);
            AddStand(perStand, new Vector3(hw + 15f, 0f, 0f), new Vector3(0f, 0f, -hl - 10f), -90f, 10);

            int batchCount = Mathf.CeilToInt(_matrices.Count / 1023f);
            _batches = new Matrix4x4[batchCount][];
            for (int b = 0; b < batchCount; b++)
            {
                int start = b * 1023;
                int length = Mathf.Min(1023, _matrices.Count - start);
                _batches[b] = new Matrix4x4[length];
                _matrices.CopyTo(start, _batches[b], 0, length);
            }
        }

        private void AddStand(int count, Vector3 start, Vector3 span, float yaw, int rows)
        {
            for (int i = 0; i < count; i++)
            {
                float u = (float)_random.NextDouble();
                float row = _random.Next(0, rows);
                Vector3 basePosition = start + span * u;
                // Rake the tiers upwards and away from the pitch.
                Vector3 outward = new Vector3(basePosition.x, 0f, basePosition.z).normalized;
                Vector3 position = basePosition + outward * (row * 0.7f) + Vector3.up * (1.1f + row * 0.62f);
                Quaternion rotation = Quaternion.Euler(0f, yaw + 180f + (float)(_random.NextDouble() * 8 - 4), 0f);
                _matrices.Add(Matrix4x4.TRS(position, rotation, Vector3.one * (0.85f + (float)_random.NextDouble() * 0.25f)));
            }
        }

        private void BuildColours(TifoPhase phase)
        {
            _colours.Clear();
            var rng = new System.Random(19);
            Color home = new Color(0.05f, 0.32f, 0.42f);
            Color away = new Color(0.55f, 0.07f, 0.12f);
            Color neutral = new Color(0.72f, 0.72f, 0.7f);
            Color mosaicA = new Color(0.95f, 0.83f, 0.35f);
            Color mosaicB = new Color(0.92f, 0.94f, 0.96f);

            for (int i = 0; i < _matrices.Count; i++)
            {
                Vector3 position = _matrices[i].GetColumn(3);
                bool homeEnd = position.z < -20f;
                bool awayEnd = position.z > 20f;

                Color colour;
                if (phase == TifoPhase.Mosaic && homeEnd)
                {
                    // Checkerboard mosaic with a lighter band across the middle.
                    int bx = Mathf.FloorToInt(position.x / 2.2f);
                    int by = Mathf.FloorToInt(position.y / 1.4f);
                    colour = ((bx + by) & 1) == 0 ? mosaicA : mosaicB;
                    if (Mathf.Abs(position.y - 6f) < 1.3f) colour = home;
                }
                else if (phase == TifoPhase.Unfurl && homeEnd)
                {
                    colour = Color.Lerp(home, mosaicA, Mathf.Clamp01(position.y / 10f));
                }
                else
                {
                    float r = (float)rng.NextDouble();
                    colour = homeEnd ? (r < 0.72f ? home : neutral)
                        : awayEnd ? (r < 0.6f ? away : neutral)
                        : (r < 0.2f ? home : r < 0.4f ? away : neutral);
                    float shade = (float)(rng.NextDouble() * 0.22 - 0.11);
                    colour = new Color(
                        Mathf.Clamp01(colour.r + shade),
                        Mathf.Clamp01(colour.g + shade),
                        Mathf.Clamp01(colour.b + shade), 1f);
                }
                _colours.Add(new Vector4(colour.r, colour.g, colour.b, 1f));
            }

            if (_colourBatches == null || _colourBatches.Length != _batches.Length)
                _colourBatches = new Vector4[_batches.Length][];
            int index = 0;
            for (int b = 0; b < _batches.Length; b++)
            {
                int length = _batches[b].Length;
                _colourBatches[b] = new Vector4[length];
                for (int i = 0; i < length; i++) _colourBatches[b][i] = _colours[index++];
            }
        }

        public void Render(MatchEngine match, float time)
        {
            _match = match;
            var phase = match.Presentation.Tifo;
            if (phase != _lastPhase)
            {
                _lastPhase = phase;
                BuildColours(phase);
            }

            var cam = Camera.main;
            if (cam == null) return;
            for (int b = 0; b < _batches.Length; b++)
            {
                _block.Clear();
                _block.SetVectorArray("_InstanceColor", _colourBatches[b]);
                Graphics.DrawMeshInstanced(_mesh, 0, _material, _batches[b], _batches[b].Length, _block,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false, Layers.Default, cam);
            }
        }
    }
}
