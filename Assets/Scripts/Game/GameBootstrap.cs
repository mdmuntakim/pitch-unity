using System;
using Pitch.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Pitch.Game
{
    /// <summary>
    /// Entry point. Builds the match, the stadium, the broadcast camera, the HUD,
    /// the touch controls and the audio at runtime, so the repository carries no
    /// binary scenes or prefabs and a clean clone always produces the same game.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        [Header("Match")]
        public float halfLengthMinutes = 5f;
        public bool introEnabled = true;
        public string homeFormation = "4-3-3";
        public string awayFormation = "4-4-2";
        public int seed = 20261122;
        public bool qualityAuto = true;

        public MatchEngine Match { get; private set; }
        public MatchRunner Runner { get; private set; }
        public BroadcastCamera CameraRig { get; private set; }
        public BroadcastHUD Hud { get; private set; }
        public TouchControlsUI Touch { get; private set; }
        public StadiumView Stadium { get; private set; }
        public MatchView View { get; private set; }
        public AudioDirector Audio { get; private set; }
        public QualityDirector Quality { get; private set; }
        public TrophyIntroView IntroView { get; private set; }
        public ReplayDirector Replay { get; private set; }
        public bool InMenu { get; private set; } = true;

        private const string PrefHalfLength = "pitch.halfLength";
        private const string PrefIntro = "pitch.intro";
        private const string PrefFormation = "pitch.formation";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (FindAnyObjectByType<GameBootstrap>() != null) return;
            var go = new GameObject("Matchday Eleven");
            go.AddComponent<GameBootstrap>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadSettings();

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            BuildMatch();
            BuildWorld();
            SetMenu(true);
            ScreenshotCrew.AttachIfRequested(this);
        }

        private void LoadSettings()
        {
            halfLengthMinutes = PlayerPrefs.GetFloat(PrefHalfLength, halfLengthMinutes);
            introEnabled = PlayerPrefs.GetInt(PrefIntro, introEnabled ? 1 : 0) == 1;
            homeFormation = PlayerPrefs.GetString(PrefFormation, homeFormation);
        }

        public void SaveSettings()
        {
            PlayerPrefs.SetFloat(PrefHalfLength, halfLengthMinutes);
            PlayerPrefs.SetInt(PrefIntro, introEnabled ? 1 : 0);
            PlayerPrefs.SetString(PrefFormation, homeFormation);
            PlayerPrefs.Save();
        }

        private void BuildMatch()
        {
            var config = new MatchConfig
            {
                HalfRealSeconds = Mathf.Clamp(halfLengthMinutes * 60f, 60f, 900f),
                HalfDisplayMinutes = 45,
                IntroEnabled = introEnabled,
                HomeFormation = homeFormation,
                AwayFormation = awayFormation,
                Seed = seed,
                GoalCelebrationSeconds = 8f,
                HalfTimeSeconds = 6f,
            };
            Match = new MatchEngine(config);
            Match.SetUserControlled(true);
            Match.Paused = true;
        }

        private void BuildWorld()
        {
            // Sun and ambient.
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            sun.shadowBias = 0.05f;
            sun.transform.rotation = Quaternion.Euler(48f, 38f, 0f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.62f, 0.72f, 0.82f);
            RenderSettings.fogStartDistance = 140f;
            RenderSettings.fogEndDistance = 320f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.44f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.16f, 0.12f);

            Stadium = StadiumView.Create(transform, Match);
            View = MatchView.Create(transform, Match, Stadium);
            IntroView = TrophyIntroView.Create(transform, Match);

            var camGo = new GameObject("Broadcast Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 400f;
            cam.fieldOfView = 42f;
            cam.allowHDR = true;
            cam.allowMSAA = false;
            camGo.AddComponent<AudioListener>();
            CameraRig = camGo.AddComponent<BroadcastCamera>();
            CameraRig.Initialise(Match, View);

            Audio = gameObject.AddComponent<AudioDirector>();
            Audio.Initialise(Match);

            Quality = gameObject.AddComponent<QualityDirector>();
            Quality.Initialise(qualityAuto);

            Replay = gameObject.AddComponent<ReplayDirector>();
            Replay.Initialise(Match, CameraRig);

            Runner = gameObject.AddComponent<MatchRunner>();
            Runner.Initialise(Match);

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("Event System");
                eventSystem.transform.SetParent(transform, false);
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }

            // uGUI overlay: menu, HUD and touch controls.
            var canvasGo = new GameObject("Broadcast UI");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            Hud = canvasGo.AddComponent<BroadcastHUD>();
            Hud.Initialise(Match, this);
            Touch = canvasGo.AddComponent<TouchControlsUI>();
            Touch.Initialise(Match, Hud);
        }

        public void SetMenu(bool menu)
        {
            InMenu = menu;
            Match.Paused = menu;
            if (Hud != null) Hud.SetMenuVisible(menu);
            if (Touch != null) Touch.SetVisible(!menu);
            if (IntroView != null) IntroView.gameObject.SetActive(!menu);
        }

        public void StartMatch()
        {
            if (Match == null) return;
            SetMenu(false);
            if (Match.Phase == MatchPhase.PreMatch || Match.Phase == MatchPhase.FullTime)
            {
                RestartMatch();
                return;
            }
            Match.Paused = false;
            if (Touch != null) Touch.SetVisible(true);
        }

        public void RestartMatch()
        {
            if (Match != null)
            {
                Match.DrainEvents();
            }
            BuildMatch();
            Match.Paused = true;
            if (Stadium != null) Stadium.Rebind(Match);
            if (View != null) View.Rebind(Match);
            if (IntroView != null) IntroView.Rebind(Match);
            if (CameraRig != null) CameraRig.Rebind(Match);
            if (Hud != null) Hud.Rebind(Match);
            if (Touch != null) Touch.Rebind(Match);
            if (Runner != null) Runner.Rebind(Match);
            if (Audio != null) Audio.Rebind(Match);
            if (Replay != null) Replay.Rebind(Match);
            SetMenu(false);
            Match.Paused = false;
        }

        public void ReturnToMenu()
        {
            SetMenu(true);
            if (Match != null) Match.Paused = true;
        }

        public void ApplyHalfLength(float minutes)
        {
            halfLengthMinutes = Mathf.Clamp(minutes, 1f, 15f);
            SaveSettings();
        }

        public void ApplyIntro(bool enabled)
        {
            introEnabled = enabled;
            SaveSettings();
        }

        public void ApplyFormation(string formation)
        {
            homeFormation = formation;
            SaveSettings();
        }

        private void OnApplicationPause(bool pause)
        {
            if (Match != null) Match.Paused = pause || InMenu;
        }

        private void OnApplicationFocus(bool focus)
        {
            if (Match != null && !focus) Match.Paused = true;
            else if (Match != null && !InMenu) Match.Paused = false;
        }
    }
}
