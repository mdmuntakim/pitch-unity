using Pitch.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pitch.Game
{
    /// <summary>
    /// Keeps the frame inside the 16.6 ms budget: chooses a device tier, applies
    /// shadow/LOD/crowd budgets, drives dynamic resolution through the URP
    /// scalable buffer and logs measured frame time for the profiler capture.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class QualityDirector : MonoBehaviour
    {
        public readonly QualityModel Model = new QualityModel();
        private bool _auto;
        private float _logTimer;
        private float _windowTimer;
        private int _frames;
        private float _windowSum;

        public void Initialise(bool auto)
        {
            _auto = auto;
            Model.TargetFps = 60f;
            QualityTier tier = auto ? DetectTier() : QualityTier.High;
            Model.ApplyTier(tier);
            Apply();
            Debug.Log($"[Pitch] quality tier={tier} crowd={Model.CrowdInstances} shadow={Model.ShadowDistance} target=60fps");
        }

        private static QualityTier DetectTier()
        {
            int memory = SystemInfo.systemMemorySize;
            int cores = SystemInfo.processorCount;
            string gpu = SystemInfo.graphicsDeviceName ?? "";
            if (memory < 3500 || cores <= 4) return QualityTier.Low;
            if (memory < 6000 || gpu.Contains("Mali-G5") || gpu.Contains("Adreno 6")) return QualityTier.Medium;
            return QualityTier.High;
        }

        private void Apply()
        {
            QualitySettings.shadowDistance = Model.ShadowDistance;
            QualitySettings.lodBias = Model.LodBias;
            QualitySettings.shadowCascades = Model.Tier == QualityTier.High ? 3 : 2;
            QualitySettings.shadowResolution = Model.Tier == QualityTier.High ? ShadowResolution.High : ShadowResolution.Medium;
            Application.targetFrameRate = 60;
        }

        private void LateUpdate()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            Model.SampleFrame(ms);
            _frames++;
            _windowSum += ms;
            _windowTimer += Time.unscaledDeltaTime;
            _logTimer += Time.unscaledDeltaTime;

            if (_auto)
            {
                ScalableBufferManager.ResizeBuffers(Model.RenderScale, Model.RenderScale);
                QualitySettings.shadowDistance = Model.ShadowDistance;
            }

            if (_windowTimer >= 5f)
            {
                float average = _windowSum / Mathf.Max(1, _frames);
                Debug.Log($"[Pitch] perf avg={average:F2}ms ({1000f / Mathf.Max(0.1f, average):F1}fps) " +
                          $"scale={Model.RenderScale:F2} tier={Model.Tier}");
                _frames = 0;
                _windowSum = 0f;
                _windowTimer = 0f;
            }
        }

        public float MeasuredFrameMs => Model.AverageFrameMs;
    }
}
