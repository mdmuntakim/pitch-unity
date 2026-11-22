using Pitch.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Pitch.Game
{
    /// <summary>
    /// Broadcast presentation built at runtime with uGUI: scoreboard, clock,
    /// half and stoppage, lower thirds, goal banner, replay wipe, radar and the
    /// main menu with the control settings. No prefabs, no binary UI assets.
    /// </summary>
    public sealed class BroadcastHUD : MonoBehaviour
    {
        private MatchEngine _match;
        private GameBootstrap _boot;

        private GameObject _menuRoot;
        private GameObject _hudRoot;
        private GameObject _fullTimeRoot;
        private Text _scoreText;
        private Text _clockText;
        private Text _phaseText;
        private Text _eventText;
        private Text _possessionText;
        private Text _contextText;
        private Text _lowerTitle;
        private Text _lowerSubtitle;
        private Text _replayText;
        private Image _chargeFill;
        private Image _chargeBack;
        private CanvasGroup _lowerGroup;
        private CanvasGroup _replayGroup;
        private RawImage _radar;
        private Texture2D _radarTexture;
        private float _radarTimer;
        private float _lowerTimer;
        private Text _settingsLabel;
        private Text _ballHighlightLabel;

        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _font;
            }
        }

        public void Initialise(MatchEngine match, GameBootstrap boot)
        {
            _match = match;
            _boot = boot;
            BuildMenu();
            BuildHud();
            BuildFullTime();
            MatchEvents.Raised += OnMatchEvent;
        }

        public void Rebind(MatchEngine match)
        {
            _match = match;
            _fullTimeRoot.SetActive(false);
        }

        private void OnDestroy() => MatchEvents.Raised -= OnMatchEvent;

        private void OnMatchEvent(MatchEvent matchEvent)
        {
            if (_eventText == null) return;
            string text = matchEvent.Text;
            if (!string.IsNullOrEmpty(text))
            {
                _eventText.text = $"{matchEvent.Minute}'  {text}";
                _eventText.canvasRenderer.SetAlpha(1f);
            }
            if (matchEvent.Type == MatchEventType.FullTime)
            {
                if (_fullTimeRoot != null) _fullTimeRoot.SetActive(true);
            }
        }

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------

        private void BuildMenu()
        {
            _menuRoot = Panel("Main Menu", transform, new Color(0.02f, 0.04f, 0.06f, 0.88f), Vector2.zero, Vector2.one);

            var title = MakeText("Title", _menuRoot.transform, "MATCHDAY ELEVEN", 92, TextAnchor.MiddleCenter,
                new Color(0.92f, 0.97f, 1f));
            Anchor(title.rectTransform, new Vector2(0.5f, 0.82f), new Vector2(900f, 120f));

            var subtitle = MakeText("Subtitle", _menuRoot.transform, "ELEVEN A SIDE  ·  UNITY 6 URP  ·  ARM64", 30, TextAnchor.MiddleCenter,
                new Color(0.55f, 0.78f, 0.92f));
            Anchor(subtitle.rectTransform, new Vector2(0.5f, 0.74f), new Vector2(900f, 50f));

            var play = MakeButton("Play", _menuRoot.transform, "PLAY MATCH", new Color(0.1f, 0.55f, 0.72f),
                new Vector2(0.5f, 0.60f), new Vector2(420f, 110f));
            play.onClick.AddListener(() => _boot.StartMatch());

            var formation = MakeButton("Formation", _menuRoot.transform, "FORMATION  4-3-3", new Color(0.12f, 0.2f, 0.26f),
                new Vector2(0.5f, 0.49f), new Vector2(420f, 76f));
            formation.onClick.AddListener(() =>
            {
                string next = _boot.homeFormation == "4-3-3" ? "4-4-2" : _boot.homeFormation == "4-4-2" ? "3-5-2" : "4-3-3";
                _boot.ApplyFormation(next);
                SetButtonLabel(formation, $"FORMATION  {next}");
            });

            var length = MakeButton("Length", _menuRoot.transform, "HALF LENGTH  5 MIN", new Color(0.12f, 0.2f, 0.26f),
                new Vector2(0.5f, 0.405f), new Vector2(420f, 76f));
            length.onClick.AddListener(() =>
            {
                float next = _boot.halfLengthMinutes >= 10f ? 3f : _boot.halfLengthMinutes >= 5f ? 10f : 5f;
                _boot.ApplyHalfLength(next);
                SetButtonLabel(length, $"HALF LENGTH  {next:0} MIN");
            });

            var intro = MakeButton("Intro", _menuRoot.transform, "TROPHY INTRO  ON", new Color(0.12f, 0.2f, 0.26f),
                new Vector2(0.5f, 0.32f), new Vector2(420f, 76f));
            intro.onClick.AddListener(() =>
            {
                _boot.ApplyIntro(!_boot.introEnabled);
                SetButtonLabel(intro, _boot.introEnabled ? "TROPHY INTRO  ON" : "TROPHY INTRO  OFF");
            });

            var haptics = MakeButton("Haptics", _menuRoot.transform, "HAPTICS  ON", new Color(0.12f, 0.2f, 0.26f),
                new Vector2(0.39f, 0.245f), new Vector2(200f, 66f));
            haptics.onClick.AddListener(() =>
            {
                var settings = _boot.Touch.CurrentSettings;
                if (_boot.Touch != null) _boot.Touch.SetHaptics(!settings.Haptics);
                SetButtonLabel(haptics, _boot.Touch.CurrentSettings.Haptics ? "HAPTICS  ON" : "HAPTICS  OFF");
            });

            var glow = MakeButton("Ball Glow", _menuRoot.transform, "BALL GLOW  ON", new Color(0.12f, 0.2f, 0.26f),
                new Vector2(0.61f, 0.245f), new Vector2(200f, 66f));
            glow.onClick.AddListener(() =>
            {
                var settings = _boot.Touch.CurrentSettings;
                bool next = !settings.BallHighlight;
                _boot.Touch.SetBallHighlight(next);
                SetButtonLabel(glow, next ? "BALL GLOW  ON" : "BALL GLOW  OFF");
                SetBallHighlightLabel(next);
            });
            _ballHighlightLabel = glow.GetComponentInChildren<Text>();

            var settingsText = MakeText("Settings", _menuRoot.transform,
                "LEFT STICK MOVES  ·  BLUE RING SHOWS YOUR CARRIER  ·  BALL IS HIGHLIGHTED", 22,
                TextAnchor.MiddleCenter, new Color(0.62f, 0.68f, 0.72f));
            Anchor(settingsText.rectTransform, new Vector2(0.5f, 0.155f), new Vector2(1200f, 40f));

            var honest = MakeText("Honest", _menuRoot.transform,
                "Originals only: no club, league, kit, trophy or player likeness.", 20,
                TextAnchor.MiddleCenter, new Color(0.5f, 0.56f, 0.6f));
            Anchor(honest.rectTransform, new Vector2(0.5f, 0.095f), new Vector2(1200f, 36f));
        }

        private void BuildHud()
        {
            _hudRoot = new GameObject("HUD", typeof(RectTransform));
            _hudRoot.transform.SetParent(transform, false);
            Stretch(_hudRoot.GetComponent<RectTransform>());

            var scorePanel = Panel("Scoreboard", _hudRoot.transform, new Color(0.01f, 0.03f, 0.05f, 0.82f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(520f, 96f), new Vector2(0f, -12f));
            _scoreText = MakeText("Score", scorePanel.transform, "NPT 0 - 0 AUR", 44, TextAnchor.MiddleCenter, Color.white);
            Anchor(_scoreText.rectTransform, new Vector2(0.5f, 0.68f), new Vector2(500f, 50f));
            _clockText = MakeText("Clock", scorePanel.transform, "00:00", 26, TextAnchor.MiddleCenter,
                new Color(0.65f, 0.88f, 1f));
            Anchor(_clockText.rectTransform, new Vector2(0.5f, 0.26f), new Vector2(500f, 32f));

            _phaseText = MakeText("Phase", _hudRoot.transform, "KICK OFF", 24, TextAnchor.MiddleCenter,
                new Color(0.9f, 0.95f, 1f));
            Anchor(_phaseText.rectTransform, new Vector2(0.5f, 0.89f), new Vector2(600f, 34f));

            _possessionText = MakeText("Possession", _hudRoot.transform, "", 22, TextAnchor.MiddleCenter,
                new Color(0.55f, 0.8f, 0.95f));
            Anchor(_possessionText.rectTransform, new Vector2(0.5f, 0.855f), new Vector2(600f, 30f));

            _eventText = MakeText("Event", _hudRoot.transform, "", 24, TextAnchor.MiddleCenter,
                new Color(1f, 0.92f, 0.65f));
            Anchor(_eventText.rectTransform, new Vector2(0.5f, 0.80f), new Vector2(900f, 34f));
            _eventText.canvasRenderer.SetAlpha(0f);

            var radarPanel = Panel("Radar", _hudRoot.transform, new Color(0.02f, 0.05f, 0.07f, 0.78f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(330f, 230f), new Vector2(-18f, 18f));
            var radarGo = new GameObject("Radar Image", typeof(RectTransform));
            radarGo.transform.SetParent(radarPanel.transform, false);
            var radarRect = radarGo.GetComponent<RectTransform>();
            Stretch(radarRect);
            radarRect.offsetMin = new Vector2(8f, 8f);
            radarRect.offsetMax = new Vector2(-8f, -8f);
            _radarTexture = new Texture2D(88, 132, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            _radar = radarGo.AddComponent<RawImage>();
            _radar.texture = _radarTexture;
            _radar.color = Color.white;

            var chargePanel = Panel("Charge", _hudRoot.transform, new Color(0.02f, 0.05f, 0.07f, 0.7f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(360f, 26f), new Vector2(0f, 22f));
            _chargeBack = chargePanel.GetComponent<Image>();
            var fill = Panel("Fill", chargePanel.transform, new Color(0.2f, 0.8f, 1f, 0.95f),
                Vector2.zero, new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            _chargeFill = fill.GetComponent<Image>();
            _chargeFill.type = Image.Type.Filled;
            _chargeFill.fillMethod = Image.FillMethod.Horizontal;
            _chargeFill.fillAmount = 0f;
            Stretch(_chargeFill.rectTransform);

            _contextText = MakeText("Context", _hudRoot.transform, "", 22, TextAnchor.MiddleCenter,
                new Color(1f, 1f, 1f));
            Anchor(_contextText.rectTransform, new Vector2(0.5f, 0.06f), new Vector2(500f, 30f));

            var lower = Panel("Lower Third", _hudRoot.transform, new Color(0.02f, 0.05f, 0.09f, 0.92f),
                new Vector2(0f, 0.22f), new Vector2(0f, 0.22f), new Vector2(640f, 118f), new Vector2(34f, 0f));
            _lowerGroup = lower.AddComponent<CanvasGroup>();
            _lowerGroup.alpha = 0f;
            _lowerTitle = MakeText("Lower Title", lower.transform, "GOAL", 46, TextAnchor.MiddleLeft, Color.white);
            Anchor(_lowerTitle.rectTransform, new Vector2(0.5f, 0.68f), new Vector2(600f, 54f));
            _lowerSubtitle = MakeText("Lower Sub", lower.transform, "", 24, TextAnchor.MiddleLeft,
                new Color(0.7f, 0.88f, 1f));
            Anchor(_lowerSubtitle.rectTransform, new Vector2(0.5f, 0.28f), new Vector2(600f, 34f));

            var replay = Panel("Replay", _hudRoot.transform, new Color(0.55f, 0.07f, 0.12f, 0.9f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(260f, 90f), new Vector2(0f, 250f));
            _replayGroup = replay.AddComponent<CanvasGroup>();
            _replayGroup.alpha = 0f;
            _replayText = MakeText("Replay Text", replay.transform, "REPLAY", 42, TextAnchor.MiddleCenter, Color.white);
            Anchor(_replayText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(240f, 60f));
        }

        private void BuildFullTime()
        {
            _fullTimeRoot = Panel("Full Time", transform, new Color(0.01f, 0.03f, 0.05f, 0.9f),
                Vector2.zero, Vector2.one);
            var title = MakeText("FT Title", _fullTimeRoot.transform, "FULL TIME", 84, TextAnchor.MiddleCenter, Color.white);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.65f), new Vector2(900f, 110f));
            var score = MakeText("FT Score", _fullTimeRoot.transform, "0 - 0", 64, TextAnchor.MiddleCenter,
                new Color(0.7f, 0.9f, 1f));
            score.name = "FT Score Text";
            Anchor(score.rectTransform, new Vector2(0.5f, 0.52f), new Vector2(900f, 90f));

            var again = MakeButton("Again", _fullTimeRoot.transform, "PLAY AGAIN", new Color(0.1f, 0.55f, 0.72f),
                new Vector2(0.5f, 0.36f), new Vector2(380f, 96f));
            again.onClick.AddListener(() => { _fullTimeRoot.SetActive(false); _boot.RestartMatch(); });

            var menu = MakeButton("To Menu", _fullTimeRoot.transform, "MAIN MENU", new Color(0.12f, 0.2f, 0.26f),
                new Vector2(0.5f, 0.26f), new Vector2(380f, 76f));
            menu.onClick.AddListener(() => { _fullTimeRoot.SetActive(false); _boot.ReturnToMenu(); });
            _fullTimeRoot.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Per frame
        // ------------------------------------------------------------------

        public void Refresh(MatchEngine match)
        {
            _match = match;
            if (match == null || _scoreText == null) return;
            var p = match.Presentation;
            _scoreText.text = $"{p.HomeAbbreviation}  {p.HomeScore}  -  {p.AwayScore}  {p.AwayAbbreviation}";
            _clockText.text = $"{p.ClockLine}   {HalfLabel(match.Half)}";
            _phaseText.text = p.PhaseLabel;
            _possessionText.text = string.IsNullOrEmpty(p.PossessionLabel) ? "" : $"POSSESSION  {p.PossessionLabel}";

            if (_chargeFill != null)
            {
                _chargeFill.fillAmount = Mathf.Clamp01(match.Controls.Charge);
                _chargeBack.color = match.Controls.HeldButton == ActionButton.Shoot
                    ? new Color(0.12f, 0.3f, 0.4f, 0.85f)
                    : new Color(0.02f, 0.05f, 0.07f, 0.7f);
            }
            if (_contextText != null) _contextText.text = match.Controls.ContextualLabel;

            if (p.ActiveLowerThird.Duration > 0f)
            {
                _lowerGroup.alpha = Mathf.Min(1f, _lowerGroup.alpha + Time.deltaTime * 5f);
                _lowerTitle.text = p.ActiveLowerThird.Title;
                _lowerSubtitle.text = p.ActiveLowerThird.Subtitle;
            }
            else
            {
                _lowerGroup.alpha = Mathf.Max(0f, _lowerGroup.alpha - Time.deltaTime * 4f);
            }

            if (_replayGroup != null)
                _replayGroup.alpha = p.ShowReplay ? 1f : Mathf.Max(0f, _replayGroup.alpha - Time.deltaTime * 4f);

            if (_eventText != null)
            {
                var colour = _eventText.color;
                colour.a = Mathf.Max(0f, colour.a - Time.deltaTime * 0.12f);
                _eventText.color = colour;
                if (colour.a <= 0.01f) _eventText.canvasRenderer.SetAlpha(0f);
            }

            var ftScore = _fullTimeRoot != null ? _fullTimeRoot.transform.Find("FT Score Text") : null;
            if (ftScore != null)
            {
                var text = ftScore.GetComponent<Text>();
                if (text != null) text.text = $"{p.HomeName}  {p.HomeScore}  -  {p.AwayScore}  {p.AwayName}";
            }

            _radarTimer += Time.deltaTime;
            if (_radarTimer > 0.1f)
            {
                _radarTimer = 0f;
                DrawRadar(match);
            }
        }

        private static string HalfLabel(int half) => half == 1 ? "1ST HALF" : "2ND HALF";

        private void DrawRadar(MatchEngine match)
        {
            if (_radarTexture == null) return;
            int width = _radarTexture.width;
            int height = _radarTexture.height;
            var pixels = new Color32[width * height];
            var background = new Color32(8, 24, 32, 255);
            var line = new Color32(60, 120, 140, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;

            void Dot(float x, float z, Color32 colour, int radius)
            {
                int px = Mathf.RoundToInt((x / PitchSpec.Width + 0.5f) * (width - 1));
                int py = Mathf.RoundToInt((z / PitchSpec.Length + 0.5f) * (height - 1));
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int cx = px + dx;
                        int cy = py + dy;
                        if (cx < 0 || cx >= width || cy < 0 || cy >= height) continue;
                        if (dx * dx + dy * dy > radius * radius) continue;
                        pixels[cy * width + cx] = colour;
                    }
            }

            // Pitch outline and halfway line.
            for (int x = 0; x < width; x++)
            {
                pixels[2 * width + x] = line;
                pixels[(height - 3) * width + x] = line;
            }
            for (int y = 0; y < height; y++) pixels[y * width + width / 2] = line;

            foreach (var player in match.Home.Players)
                Dot(player.Position.X, player.Position.Z, new Color32(60, 190, 255, 255), 2);
            foreach (var player in match.Away.Players)
                Dot(player.Position.X, player.Position.Z, new Color32(240, 90, 100, 255), 2);
            Dot(match.Ball.Position.X, match.Ball.Position.Z, new Color32(255, 240, 120, 255), 1);
            if (match.Carrier != null)
                Dot(match.Carrier.Position.X, match.Carrier.Position.Z, new Color32(255, 255, 255, 255), 3);

            _radarTexture.SetPixels32(pixels);
            _radarTexture.Apply(false);
        }

        public void SetMenuVisible(bool menu)
        {
            if (_menuRoot != null) _menuRoot.SetActive(menu);
            if (_hudRoot != null) _hudRoot.SetActive(!menu);
            if (menu && _fullTimeRoot != null) _fullTimeRoot.SetActive(false);
        }

        public void SetBallHighlightLabel(bool on)
        {
            if (_ballHighlightLabel != null) _ballHighlightLabel.text = on ? "BALL HIGHLIGHT  ON" : "BALL HIGHLIGHT  OFF";
        }

        // ------------------------------------------------------------------
        // UI helpers
        // ------------------------------------------------------------------

        private static GameObject Panel(string name, Transform parent, Color colour,
            Vector2 anchorMin, Vector2 anchorMax, Vector2? size = null, Vector2? position = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            if (size.HasValue) rect.sizeDelta = size.Value;
            if (position.HasValue) rect.anchoredPosition = position.Value;
            if (anchorMin == Vector2.zero && anchorMax == Vector2.one)
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.color = colour;
            return go;
        }

        private static Text MakeText(string name, Transform parent, string content, int size, TextAnchor alignment, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = colour;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Button MakeButton(string name, Transform parent, string label, Color colour,
            Vector2 anchor, Vector2 size)
        {
            var go = Panel(name, parent, colour, anchor, anchor, size, Vector2.zero);
            var button = go.AddComponent<Button>();
            var image = go.GetComponent<Image>();
            button.targetGraphic = image;
            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colours.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colours;
            var text = MakeText("Label", go.transform, label, 28, TextAnchor.MiddleCenter, Color.white);
            Anchor(text.rectTransform, new Vector2(0.5f, 0.5f), size);
            return button;
        }

        private static void SetButtonLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.text = label;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
