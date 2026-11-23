using Pitch.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Pitch.Game
{
    /// <summary>
    /// eFootball style touch controls: a floating left thumbstick, a right
    /// action cluster with hold-to-charge shooting and a contextual button.
    /// Every finger is tracked independently through uGUI pointer events, so no
    /// touch is dropped while the stick is being dragged. Haptics on press.
    /// Settings (scale, opacity, handedness) are persisted in PlayerPrefs.
    /// </summary>
    public sealed class TouchControlsUI : MonoBehaviour
    {
        private MatchEngine _match;
        private BroadcastHUD _hud;
        private GameObject _root;
        private RectTransform _stickZone;
        private RectTransform _stickBase;
        private RectTransform _stickKnob;
        private Vector2 _stickOrigin;
        private int _stickPointer = -1;
        private float _radiusPixels = 170f;

        private Text _passLabel;
        private Text _throughLabel;
        private Text _lobLabel;
        private Text _tackleLabel;
        private Text _contextLabel;
        private Text _sprintLabel;
        private Text _shootLabel;

        private ControlSettings Settings => _match != null ? _match.Controls.Settings : new ControlSettings();

        /// <summary>Live settings object; the menu reads it to draw the toggles.</summary>
        public ControlSettings CurrentSettings => Settings;

        public void SetHaptics(bool enabled)
        {
            if (_match == null) return;
            Settings.Haptics = enabled;
            SaveSettings();
        }

        public void SetBallHighlight(bool enabled)
        {
            if (_match == null) return;
            Settings.BallHighlight = enabled;
            _match.Presentation.BallHighlight = enabled;
            SaveSettings();
        }

        public void Initialise(MatchEngine match, BroadcastHUD hud)
        {
            _match = match;
            _hud = hud;
            LoadSettings();
            Build();
        }

        public void Rebind(MatchEngine match) => _match = match;

        public void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        private void LoadSettings()
        {
            var s = _match.Controls.Settings;
            s.ButtonScale = PlayerPrefs.GetFloat("pitch.buttonScale", s.ButtonScale);
            s.ButtonOpacity = PlayerPrefs.GetFloat("pitch.buttonOpacity", s.ButtonOpacity);
            s.LeftHanded = PlayerPrefs.GetInt("pitch.leftHanded", s.LeftHanded ? 1 : 0) == 1;
            s.Haptics = PlayerPrefs.GetInt("pitch.haptics", s.Haptics ? 1 : 0) == 1;
            s.BallHighlight = PlayerPrefs.GetInt("pitch.ballHighlight", s.BallHighlight ? 1 : 0) == 1;
            s.Sanitise();
            _match.Presentation.BallHighlight = s.BallHighlight;
        }

        private void SaveSettings()
        {
            var s = _match.Controls.Settings;
            PlayerPrefs.SetFloat("pitch.buttonScale", s.ButtonScale);
            PlayerPrefs.SetFloat("pitch.buttonOpacity", s.ButtonOpacity);
            PlayerPrefs.SetInt("pitch.leftHanded", s.LeftHanded ? 1 : 0);
            PlayerPrefs.SetInt("pitch.haptics", s.Haptics ? 1 : 0);
            PlayerPrefs.SetInt("pitch.ballHighlight", s.BallHighlight ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void Build()
        {
            _root = new GameObject("Touch Controls", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            var rect = _root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            BuildStickZone();
            BuildButtons();
        }

        private void BuildStickZone()
        {
            var zone = new GameObject("Stick Zone", typeof(RectTransform));
            zone.transform.SetParent(_root.transform, false);
            _stickZone = zone.GetComponent<RectTransform>();
            _stickZone.anchorMin = new Vector2(0f, 0f);
            _stickZone.anchorMax = new Vector2(0.52f, 0.82f);
            _stickZone.offsetMin = Vector2.zero;
            _stickZone.offsetMax = Vector2.zero;
            var image = zone.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.001f);
            image.raycastTarget = true;
            zone.AddComponent<StickZone>();

            var baseGo = new GameObject("Stick Base", typeof(RectTransform));
            baseGo.transform.SetParent(_root.transform, false);
            _stickBase = baseGo.GetComponent<RectTransform>();
            _stickBase.anchorMin = new Vector2(0.14f, 0.18f);
            _stickBase.anchorMax = new Vector2(0.14f, 0.18f);
            _stickBase.sizeDelta = new Vector2(300f, 300f);
            var baseImage = baseGo.AddComponent<Image>();
            baseImage.color = new Color(1f, 1f, 1f, Settings.ButtonOpacity * 0.32f);
            baseImage.raycastTarget = false;
            var knobGo = new GameObject("Stick Knob", typeof(RectTransform));
            knobGo.transform.SetParent(baseGo.transform, false);
            _stickKnob = knobGo.GetComponent<RectTransform>();
            _stickKnob.anchorMin = _stickKnob.anchorMax = new Vector2(0.5f, 0.5f);
            _stickKnob.sizeDelta = new Vector2(150f, 150f);
            var knobImage = knobGo.AddComponent<Image>();
            knobImage.color = new Color(0.3f, 0.8f, 1f, Settings.ButtonOpacity * 0.6f);
            knobImage.raycastTarget = false;
            _stickBase.gameObject.SetActive(true);
        }

        private void BuildButtons()
        {
            float scale = Settings.ButtonScale;
            float opacity = Settings.ButtonOpacity;
            bool left = Settings.LeftHanded;
            float side = left ? -1f : 1f;

            _shootLabel = MakeButton("Shoot", ActionButton.Shoot, new Vector2(0.87f, 0.19f), 250f * scale,
                new Color(0.1f, 0.55f, 0.78f, opacity), "SHOOT", 40, true);
            _passLabel = MakeButton("Pass", ActionButton.Pass, new Vector2(0.73f, 0.25f), 150f * scale,
                new Color(0.1f, 0.4f, 0.55f, opacity), "PASS", 28);
            _throughLabel = MakeButton("Through", ActionButton.ThroughBall, new Vector2(0.76f, 0.48f), 135f * scale,
                new Color(0.1f, 0.3f, 0.42f, opacity), "THRU", 24);
            _lobLabel = MakeButton("Lob", ActionButton.LobCross, new Vector2(0.93f, 0.44f), 135f * scale,
                new Color(0.1f, 0.3f, 0.42f, opacity), "LOB", 24);
            _tackleLabel = MakeButton("Tackle", ActionButton.PressureTackle, new Vector2(0.94f, 0.70f), 150f * scale,
                new Color(0.55f, 0.22f, 0.18f, opacity), "TACKLE", 22);
            _contextLabel = MakeButton("Context", ActionButton.Contextual, new Vector2(0.72f, 0.69f), 145f * scale,
                new Color(0.35f, 0.3f, 0.1f, opacity), "CONTEXT", 20);
            _sprintLabel = MakeButton("Sprint", ActionButton.Sprint, new Vector2(0.73f, 0.07f), 130f * scale,
                new Color(0.2f, 0.5f, 0.25f, opacity), "SPRINT", 22);

            // Left handed layout mirrors the whole cluster around the screen centre.
            if (left)
            {
                foreach (var label in new[] { _shootLabel, _passLabel, _throughLabel, _lobLabel, _tackleLabel, _contextLabel, _sprintLabel })
                {
                    var rect = label.transform.parent.GetComponent<RectTransform>();
                    var anchor = rect.anchorMin;
                    anchor.x = 1f - anchor.x;
                    rect.anchorMin = rect.anchorMax = anchor;
                }
            }
        }

        private Text MakeButton(string name, ActionButton action, Vector2 anchor, float size, Color colour,
            string label, int fontSize, bool hold = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(size, size);
            var image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = true;
            var button = go.AddComponent<TouchActionButton>();
            button.Action = action;
            button.Hold = hold;
            button.Owner = this;

            var text = new GameObject("Label", typeof(RectTransform));
            text.transform.SetParent(go.transform, false);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var component = text.AddComponent<Text>();
            component.font = BroadcastHUD.Font;
            component.text = label;
            component.fontSize = fontSize;
            component.alignment = TextAnchor.MiddleCenter;
            component.color = new Color(1f, 1f, 1f, 0.92f);
            component.raycastTarget = false;
            return component;
        }

        internal void Press(ActionButton action)
        {
            if (_match == null) return;
            _match.Controls.ButtonDown(action);
            if (action != ActionButton.Shoot && action != ActionButton.Sprint)
                _match.SubmitAction(action);
            Haptic();
        }

        internal void Release(ActionButton action)
        {
            if (_match == null) return;
            if (action == ActionButton.Shoot)
            {
                _match.Controls.ButtonUp(action);
                _match.SubmitAction(ActionButton.Shoot);
            }
            else
            {
                _match.Controls.ButtonUp(action);
            }
        }

        internal void StickDown(Vector2 screenPosition)
        {
            if (_match == null) return;
            _stickOrigin = screenPosition;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _stickZone, screenPosition, null, out var local))
            {
                _stickBase.anchoredPosition = local;
            }
            _match.Controls.StickDown(new Vec3(0f, 0f, 0f));
            _stickPointer = -1;
        }

        internal void StickDrag(Vector2 screenPosition)
        {
            if (_match == null) return;
            var delta = (screenPosition - _stickOrigin) / _radiusPixels;
            _match.Controls.SetStickManual(new Vec3(delta.x, 0f, delta.y));
            _stickKnob.anchoredPosition = Vector2.ClampMagnitude(delta * _radiusPixels, _radiusPixels);
        }

        internal void StickUp()
        {
            if (_match == null) return;
            _match.Controls.SetStickManual(Vec3.Zero);
            _stickKnob.anchoredPosition = Vector2.zero;
            _stickPointer = -1;
        }

        private void Update()
        {
            if (_match == null) return;
            if (Input.GetKeyDown(KeyCode.J)) Press(ActionButton.Pass);
            if (Input.GetKeyUp(KeyCode.J)) Release(ActionButton.Pass);
            if (Input.GetKeyDown(KeyCode.K)) Press(ActionButton.Shoot);
            if (Input.GetKeyUp(KeyCode.K)) Release(ActionButton.Shoot);
            if (Input.GetKeyDown(KeyCode.L)) Press(ActionButton.ThroughBall);
            if (Input.GetKeyDown(KeyCode.I)) Press(ActionButton.LobCross);
            if (Input.GetKeyDown(KeyCode.U)) Press(ActionButton.Contextual);
            if (Input.GetKeyDown(KeyCode.Space)) Press(ActionButton.Sprint);
            if (Input.GetKeyUp(KeyCode.Space)) Release(ActionButton.Sprint);

            Vector3 keyboard = Vector3.zero;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) keyboard.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) keyboard.x += 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) keyboard.z += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) keyboard.z -= 1f;
            bool touchActive = _match.Controls.Stick.Active;
            if (keyboard.sqrMagnitude > 0.01f)
                _match.Controls.SetStickManual(keyboard.normalized.ToCore());
            else if (!touchActive && !Input.GetKey(KeyCode.Space))
                _match.Controls.SetStickManual(Vec3.Zero);

            if (_contextLabel != null) _contextLabel.text = _match.Controls.ContextualLabel;
        }

        private static void Haptic()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                vibrator?.Call("vibrate", 12L);
            }
            catch (System.Exception)
            {
                // Haptics are optional; a device without a vibrator must not fail.
            }
#endif
        }

        internal void SetScale(float scale)
        {
            Settings.ButtonScale = Mathf.Clamp(scale, 0.6f, 1.6f);
            SaveSettings();
        }

        internal void SetOpacity(float opacity)
        {
            Settings.ButtonOpacity = Mathf.Clamp(opacity, 0.25f, 1f);
            SaveSettings();
        }
    }

    public sealed class StickZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
            var controls = GetComponentInParent<TouchControlsUI>();
            controls?.StickDown(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            var controls = GetComponentInParent<TouchControlsUI>();
            controls?.StickDrag(eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            var controls = GetComponentInParent<TouchControlsUI>();
            controls?.StickUp();
        }
    }

    public sealed class TouchActionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public ActionButton Action;
        public bool Hold;
        public TouchControlsUI Owner;

        public void OnPointerDown(PointerEventData eventData)
        {
            Owner?.Press(Action);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (Hold) Owner?.Release(Action);
        }
    }
}
