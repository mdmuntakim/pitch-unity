using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pitch.EditorTools
{
    /// <summary>
    /// Captures the release screenshots from the real game inside the editor.
    /// The build machine has no Android device, so the editor opens the boot
    /// scene, enters play mode and lets <see cref="Pitch.Game.ScreenshotCrew"/>
    /// drive the match (menu, trophy intro, kick-off, action, possession ring,
    /// goal, replay) exactly as it does on device.
    ///
    ///   Unity -executeMethod Pitch.EditorTools.Screenshots.Capture
    ///
    /// Requires PITCH_SHOTS=1 (set by the workflow) so a normal build never
    /// starts an editor play session. Exits 0 with PNGs in docs/screenshots, 1
    /// when nothing usable was captured. `-quit` must NOT be passed: the editor
    /// is quit from the poll below once the capture is complete.
    /// </summary>
    public static class Screenshots
    {
        private const double TimeoutSeconds = 900;
        private const long MinimumPngBytes = 10_000; // a black frame compresses to almost nothing

        private static string _target;
        private static double _deadline;
        private static bool _stopping;

        public static void Capture()
        {
            if (Environment.GetEnvironmentVariable("PITCH_SHOTS") != "1")
            {
                Debug.Log("[Pitch] screenshots not requested (PITCH_SHOTS != 1); nothing to do");
                EditorApplication.Exit(0);
                return;
            }

            ProjectBootstrap.EnsureProject();
            EditorSceneManager.OpenScene(ProjectBootstrap.BootScenePath, OpenSceneMode.Single);
            _target = Path.Combine(Directory.GetCurrentDirectory(), "docs", "screenshots");
            Directory.CreateDirectory(_target);
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
            EditorApplication.isPlaying = true;
            Debug.Log($"[Pitch] screenshot capture started, target {_target}");
        }

        private static void Poll()
        {
            if (_stopping)
            {
                if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup < _deadline) return;
                EditorApplication.update -= Poll;
                CopyAndExit();
                return;
            }

            string source = Path.Combine(Application.persistentDataPath, "PitchScreenshots");
            int captured = Directory.Exists(source) ? Directory.GetFiles(source, "*.png").Length : 0;
            bool done = captured >= 8;
            bool expired = EditorApplication.timeSinceStartup > _deadline;

            if (!done && !expired) return;

            // Leave play mode first so the last frame is flushed, then copy.
            _stopping = true;
            _deadline = EditorApplication.timeSinceStartup + 5;
            EditorApplication.isPlaying = false;
        }

        private static void CopyAndExit()
        {
            string source = Path.Combine(Application.persistentDataPath, "PitchScreenshots");
            int kept = 0;
            int captured = 0;
            if (Directory.Exists(source))
            {
                foreach (string file in Directory.GetFiles(source, "*.png"))
                {
                    captured++;
                    long bytes = new FileInfo(file).Length;
                    if (bytes < MinimumPngBytes)
                    {
                        Debug.LogWarning($"[Pitch] dropping {Path.GetFileName(file)}: {bytes} bytes looks like an empty frame");
                        continue;
                    }
                    File.Copy(file, Path.Combine(_target, Path.GetFileName(file)), true);
                    kept++;
                }
            }

            Debug.Log($"[Pitch] screenshots: {kept} copied to {_target} (captured={captured})");
            EditorApplication.Exit(kept >= 6 ? 0 : 1);
        }
    }
}
