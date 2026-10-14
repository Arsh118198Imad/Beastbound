using System;
using Beastbound.Core;
using Beastbound.Saving;
using UnityEngine;
using UnityEngine.UI;

namespace Beastbound.UI
{
    /// <summary>Shared helpers for building centred menu cards.</summary>
    internal static class MenuLayout
    {
        public static RectTransform Card(RectTransform root, string title, float width, float height, out RectTransform content)
        {
            var dim = UIFactory.Fill(root, "Dim", UITheme.Overlay);
            dim.raycastTarget = true;
            var card = UIFactory.Panel(root, "Card", UITheme.Panel);
            UIFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            var heading = UIFactory.Label(card.transform, "Heading", title, UITheme.HeadingSize, UITheme.Accent, TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(width - 60f, 56f));
            content = UIFactory.Rect(card.transform, "Content");
            UIFactory.Stretch(content);
            content.offsetMin = new Vector2(40f, 30f);
            content.offsetMax = new Vector2(-40f, -96f);
            return card.rectTransform;
        }
    }

    // ====================================================================== Title

    public class TitleScreen : MenuScreen
    {
        private Button _continue;
        private Button _newGame;
        private Text _status;

        protected override void Build()
        {
            // Left-side gradient so the title reads over the live vale behind it.
            var shade = UIFactory.Fill(Root, "Shade", new Color(0.02f, 0.05f, 0.1f, 0.55f));
            shade.rectTransform.anchorMax = new Vector2(0.48f, 1f);
            shade.raycastTarget = false;

            var title = UIFactory.Label(Root, "Logo", "BEASTBOUND", UITheme.TitleSize, UITheme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(110f, -150f), new Vector2(900f, 130f));
            var outline = title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.12f, 0f, 0.6f);
            outline.effectDistance = new Vector2(2f, -2f);
            var subtitle = UIFactory.Label(Root, "Subtitle", "Sunmeadow Vale  ·  a creature-bonding adventure", UITheme.BodySize + 4, UITheme.Text, TextAnchor.MiddleLeft);
            UIFactory.Place(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(116f, -250f), new Vector2(900f, 44f));

            var menu = UIFactory.Rect(Root, "Menu");
            UIFactory.Place(menu, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(110f, -60f), new Vector2(420f, 400f));
            UIFactory.Vertical(menu, 14f, 0);

            _continue = UIFactory.Button(menu, "Continue Journey", OnContinue, true);
            _newGame = UIFactory.Button(menu, "New Journey", OnNewGame);
            UIFactory.Button(menu, "Settings", () => Ui.OpenSettings());
            UIFactory.Button(menu, "Controls", () => Ui.OpenControls());
            UIFactory.Button(menu, "Quit", () => GameManager.Instance?.QuitGame());

            _status = UIFactory.Label(Root, "Status", "", UITheme.SmallSize, UITheme.HealthLow, TextAnchor.MiddleLeft);
            UIFactory.Place(_status.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110f, 140f), new Vector2(900f, 40f));

            var footer = UIFactory.Label(Root, "Footer",
                "Prototype v0.1  ·  Offline single-player  ·  No microtransactions  ·  Saves stay on your PC",
                UITheme.SmallSize, UITheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(footer.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110f, 60f), new Vector2(1200f, 40f));

            GameEvents.Notification += OnNotification;
        }

        private void OnDestroy() => GameEvents.Notification -= OnNotification;

        private void OnNotification(string message, NotificationKind kind)
        {
            if (gameObject.activeInHierarchy && kind == NotificationKind.Warning && _status != null) _status.text = message;
        }

        public override void OnShown()
        {
            var gm = GameManager.Instance;
            bool hasSave = gm != null && gm.HasSave;
            _continue.gameObject.SetActive(hasSave);
            if (_status != null) _status.text = "";
        }

        public override Selectable FirstSelectable => _continue != null && _continue.gameObject.activeSelf ? _continue : _newGame;

        private void OnContinue()
        {
            GameManager.Instance?.ContinueGame();
        }

        private void OnNewGame()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            if (gm.HasSave)
            {
                Ui.AskConfirm("Start a new journey?",
                    "Your existing journey will be replaced the next time the game saves.",
                    "Start New Journey", gm.StartNewGame);
            }
            else
            {
                gm.StartNewGame();
            }
        }
    }

    // ====================================================================== Pause

    public class PauseMenu : MenuScreen
    {
        private Button _resume;

        protected override void Build()
        {
            MenuLayout.Card(Root, "Paused", 520f, 700f, out var content);
            UIFactory.Vertical(content, 12f, 0);
            _resume = UIFactory.Button(content, "Resume", () => Ui.Pop(), true);
            UIFactory.Button(content, "Bestiary", () => Ui.OpenBestiary());
            UIFactory.Button(content, "Settings", () => Ui.OpenSettings());
            UIFactory.Button(content, "Controls", () => Ui.OpenControls());
            UIFactory.Button(content, "Save Journey", () => GameManager.Instance?.SaveGame(false));
            UIFactory.Button(content, "Restart Journey", () =>
                Ui.AskConfirm("Restart from the beginning?",
                    "All bound creatures and progress in this journey will be reset.",
                    "Restart", () => GameManager.Instance?.StartNewGame()));
            UIFactory.Button(content, "Title Screen", () => GameManager.Instance?.ReturnToTitle());
            UIFactory.Button(content, "Save & Quit", () => GameManager.Instance?.QuitGame());
        }

        public override Selectable FirstSelectable => _resume;
    }

    // ====================================================================== Settings

    public class SettingsMenu : MenuScreen
    {
        private SettingsData _working;
        private RectTransform _content;
        private Selectable _first;

        protected override void Build()
        {
            MenuLayout.Card(Root, "Settings", 980f, 1000f, out _content);
            UIFactory.Vertical(_content, 4f, 0);
        }

        public override void OnShown()
        {
            var gm = GameManager.Instance;
            _working = gm != null && gm.Settings != null ? JsonUtility.FromJson<SettingsData>(JsonUtility.ToJson(gm.Settings)) : new SettingsData();
            Rebuild();
        }

        private void Apply(bool save = false) => GameManager.Instance?.ApplySettings(_working, save);

        private void Rebuild()
        {
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            var s = _working;

            _first = UIFactory.SliderRow(_content, "Mouse sensitivity", 0.2f, 8f, s.mouseSensitivity, v => v.ToString("0.0"),
                v => { s.mouseSensitivity = v; Apply(); });
            UIFactory.ToggleRow(_content, "Invert camera Y", s.invertY, v => { s.invertY = v; Apply(); });
            UIFactory.SliderRow(_content, "Field of view", 50f, 100f, s.fieldOfView, v => Mathf.RoundToInt(v) + "°",
                v => { s.fieldOfView = v; Apply(); });
            UIFactory.SliderRow(_content, "Master volume", 0f, 1f, s.masterVolume, Percent, v => { s.masterVolume = v; Apply(); });
            UIFactory.SliderRow(_content, "Effects & ambience", 0f, 1f, s.sfxVolume, Percent, v => { s.sfxVolume = v; Apply(); });
            UIFactory.SliderRow(_content, "Music", 0f, 1f, s.musicVolume, Percent, v => { s.musicVolume = v; Apply(); });
            UIFactory.CyclerRow(_content, "Graphics quality", SettingsData.QualityNames, s.quality, i => { s.quality = i; Apply(); });
            UIFactory.ToggleRow(_content, "Fullscreen (builds only)", s.fullscreen, v => { s.fullscreen = v; Apply(); });
            UIFactory.ToggleRow(_content, "VSync", s.vsync, v => { s.vsync = v; Apply(); });
            string[] caps = Array.ConvertAll(SettingsData.FrameCaps, c => c <= 0 ? "Unlimited" : c + " FPS");
            UIFactory.CyclerRow(_content, "Frame cap (VSync off)", caps, s.frameCapIndex, i => { s.frameCapIndex = i; Apply(); });
            UIFactory.SliderRow(_content, "Day length (minutes)", 2f, 120f, s.dayLengthMinutes, v => Mathf.RoundToInt(v).ToString(),
                v => { s.dayLengthMinutes = Mathf.Round(v); Apply(); });
            UIFactory.ToggleRow(_content, "Show FPS counter", s.showFps, v => { s.showFps = v; Apply(); });
            UIFactory.ToggleRow(_content, "Show controls hint", s.showControlsHint, v => { s.showControlsHint = v; Apply(); });

            var spacer = UIFactory.Rect(_content, "Spacer");
            UIFactory.Size(spacer, 10f);
            var back = UIFactory.Button(_content, "Save & Back", () => { Apply(true); Ui.Pop(); }, true);
            UIFactory.Size(back, 56f);
        }

        private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

        public override Selectable FirstSelectable => _first;

        private void OnDisable()
        {
            // Closing with Esc / B also keeps the changes.
            if (_working != null) Apply(true);
        }
    }

    // ====================================================================== Controls

    public class ControlsScreen : MenuScreen
    {
        private Button _back;

        protected override void Build()
        {
            MenuLayout.Card(Root, "Controls & Tips", 1180f, 860f, out var content);
            var keys = UIFactory.Label(content, "Keyboard", "", UITheme.BodySize - 1, UITheme.Text, TextAnchor.UpperLeft);
            keys.rectTransform.anchorMin = new Vector2(0f, 0f);
            keys.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            keys.rectTransform.offsetMin = new Vector2(0f, 80f);
            keys.rectTransform.offsetMax = new Vector2(-20f, 0f);
            keys.text =
                "<b><color=#FAC75A>KEYBOARD & MOUSE</color></b>\n" +
                "Move:  W A S D\n" +
                "Look:  Mouse\n" +
                "Run:  Hold Left Shift\n" +
                "Jump:  Space\n" +
                "Sneak:  Hold Left Ctrl, or press C\n" +
                "Staff Strike:  Left mouse (tap for combo)\n" +
                "Ember Spark:  Right mouse\n" +
                "Dodge:  Q\n" +
                "Bind creature:  E\n" +
                "Bestiary:  Tab\n" +
                "Pause:  Esc\n" +
                "Zoom camera:  Mouse wheel\n" +
                "Controls hint:  F1\n\n" +
                "<b><color=#FAC75A>GAMEPAD</color></b> (Input System package)\n" +
                "Move / Look:  Sticks     Run:  LB\n" +
                "Jump:  A          Dodge:  B\n" +
                "Strike:  X / RT     Spark:  Y / LT\n" +
                "Bind:  RB         Sneak:  R3\n" +
                "Bestiary:  View       Pause:  Start";

            var tips = UIFactory.Label(content, "Tips", "", UITheme.BodySize - 1, UITheme.Text, TextAnchor.UpperLeft);
            tips.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            tips.rectTransform.anchorMax = new Vector2(1f, 1f);
            tips.rectTransform.offsetMin = new Vector2(20f, 80f);
            tips.rectTransform.offsetMax = Vector2.zero;
            tips.text =
                "<b><color=#8BE3B4>HOW BINDING WORKS</color></b>\n" +
                "Face a wild creature and press Bind. The exact chance is always shown — what you see is what is rolled.\n\n" +
                "Raise your odds:\n" +
                "  •  Sneak up unnoticed (x1.5) or while it sleeps (x2)\n" +
                "  •  Weaken it first — lower health, higher chance\n" +
                "  •  Exhausted (0 HP) creatures are a guaranteed bind\n" +
                "  •  Every miss adds Resonance (+12% next time)\n\n" +
                "Bind Orbs are free and unlimited.\n\n" +
                "<b><color=#8BE3B4>STEALTH</color></b>\n" +
                "A ? above a creature means it is getting suspicious. ! means it noticed you. Running is loud; sneaking is quiet.\n\n" +
                "<b><color=#8BE3B4>BATTLE</color></b>\n" +
                "Watch for the crouch-and-glow telegraph, then Dodge. Ember Spark is extra effective against Verdant creatures.";
            tips.horizontalOverflow = HorizontalWrapMode.Wrap;

            var backRow = UIFactory.Rect(content, "BackRow");
            UIFactory.Place(backRow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(320f, 58f));
            _back = UIFactory.Button(backRow, "Back", () => Ui.Pop(), true);
            UIFactory.Stretch((RectTransform)_back.transform);
        }

        public override Selectable FirstSelectable => _back;
    }

    // ====================================================================== Confirm

    public class ConfirmDialog : MenuScreen
    {
        private Text _title;
        private Text _message;
        private Text _yesLabel;
        private Button _no;
        private Action _onYes;

        protected override void Build()
        {
            var card = MenuLayout.Card(Root, "", 760f, 380f, out var content);
            _title = card.Find("Heading").GetComponent<Text>();
            _message = UIFactory.Label(content, "Message", "", UITheme.BodySize, UITheme.Text, TextAnchor.UpperCenter);
            UIFactory.Stretch(_message.rectTransform);
            _message.rectTransform.offsetMin = new Vector2(0f, 80f);

            var row = UIFactory.Rect(content, "Buttons");
            UIFactory.Place(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(660f, 60f));
            var yes = UIFactory.Button(row, "Yes", () =>
            {
                var action = _onYes;
                Ui.Pop();
                action?.Invoke();
            }, true);
            UIFactory.Place((RectTransform)yes.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(320f, 58f));
            _yesLabel = yes.GetComponentInChildren<Text>();
            _no = UIFactory.Button(row, "Cancel", () => Ui.Pop());
            UIFactory.Place((RectTransform)_no.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(320f, 58f));
        }

        public void Setup(string title, string message, string yesLabel, Action onYes)
        {
            _title.text = title;
            _message.text = message;
            _yesLabel.text = yesLabel;
            _onYes = onYes;
        }

        public override Selectable FirstSelectable => _no; // safe default
    }

    // ====================================================================== Defeat

    public class DefeatScreen : MenuScreen
    {
        private Button _retry;
        private CanvasGroup _group;
        private float _shownAt;

        protected override void Build()
        {
            _group = gameObject.AddComponent<CanvasGroup>();
            MenuLayout.Card(Root, "You were overwhelmed", 760f, 420f, out var content);
            var msg = UIFactory.Label(content, "Message",
                "No progress is lost. Rest at the Waystone and try again — dodge the glowing wind-ups, " +
                "use Ember Spark from range, or sneak past.",
                UITheme.BodySize, UITheme.Text, TextAnchor.UpperCenter);
            UIFactory.Stretch(msg.rectTransform);
            msg.rectTransform.offsetMin = new Vector2(0f, 90f);

            var row = UIFactory.Rect(content, "Buttons");
            UIFactory.Place(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(660f, 60f));
            _retry = UIFactory.Button(row, "Try Again", () => GameManager.Instance?.RetryAfterDefeat(), true);
            UIFactory.Place((RectTransform)_retry.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(320f, 58f));
            var title = UIFactory.Button(row, "Title Screen", () => GameManager.Instance?.ReturnToTitle());
            UIFactory.Place((RectTransform)title.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(320f, 58f));
        }

        public override void OnShown()
        {
            _shownAt = Time.unscaledTime;
            _group.alpha = 0f;
        }

        private void Update()
        {
            _group.alpha = Mathf.Clamp01((Time.unscaledTime - _shownAt - 0.6f) * 2f);
            _group.interactable = _group.alpha > 0.5f;
        }

        public override bool CanCloseWithBack => false;
        public override Selectable FirstSelectable => _retry;
    }
}
