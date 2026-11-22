using System.Collections;
using System.IO;
using Pitch.Core;
using UnityEngine;

namespace Pitch.Game
{
    /// <summary>
    /// Automated screenshot capture for the release pipeline. Start the game with
    /// the command line flag -pitch-screenshots (or PITCH_SCREENSHOTS=1) and it
    /// captures the menu, trophy intro, kick-off, action, possession ring, tifo,
    /// goal celebration, night match and radar, then quits. Screenshots are
    /// written to Application.persistentDataPath/PitchScreenshots.
    /// </summary>
    public sealed class ScreenshotCrew : MonoBehaviour
    {
        private GameBootstrap _boot;
        private string _directory;

        public static void AttachIfRequested(GameBootstrap boot)
        {
            bool requested = System.Environment.GetEnvironmentVariable("PITCH_SCREENSHOTS") == "1";
            foreach (var arg in System.Environment.GetCommandLineArgs())
                if (arg == "-pitch-screenshots") requested = true;
            if (!requested) return;
            var crew = boot.gameObject.AddComponent<ScreenshotCrew>();
            crew._boot = boot;
        }

        private void Start()
        {
            _directory = Path.Combine(Application.persistentDataPath, "PitchScreenshots");
            Directory.CreateDirectory(_directory);
            StartCoroutine(Capture());
        }

        private IEnumerator Capture()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("01_main_menu");

            _boot.SetMenu(false);
            if (_boot.Match.Phase == MatchPhase.Intro)
            {
                yield return Wait(2.5f);
                yield return Shot("02_trophy_walk_past");
                yield return Wait(5.0f);
                yield return Shot("03_trophy_touch");
                _boot.Match.SkipIntro();
            }

            yield return Wait(1.5f);
            yield return Shot("04_kickoff_tifo");
            yield return Wait(3.0f);
            yield return Shot("05_action_broadcast");

            // Possession ring and highlighted ball close-up.
            var carrier = _boot.Match.Home.Players[9];
            _boot.Match.TestSetCarrier(carrier);
            yield return Wait(0.5f);
            yield return Shot("06_possession_ring_and_ball");

            // Goal celebration.
            _boot.Match.TestPlaceBall(new Vec3(1.8f, 0.2f, PitchSpec.HalfLength - 0.2f), new Vec3(0f, 0f, 12f));
            _boot.Match.TestSetLastTouch(TeamSide.Home, carrier);
            yield return Wait(2.0f);
            yield return Shot("07_goal_celebration");
            yield return Wait(3.0f);
            yield return Shot("08_replay_lower_third");

            Debug.Log($"[Pitch] screenshots written to {_directory}");
            if (Application.isBatchMode) Application.Quit(0);
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(_directory, name + ".png");
            ScreenCapture.CaptureScreenshot(path, 1);
            Debug.Log($"[Pitch] screenshot {path}");
            yield return null;
        }

        private static IEnumerator Wait(float seconds) => new WaitForSecondsRealtime(seconds);
    }
}
