using System.Collections.Generic;
using Beastbound.Audio;
using Beastbound.Controls;
using Beastbound.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Beastbound.UI
{
    /// <summary>Base class for full-screen menus managed by <see cref="UIManager"/>'s menu stack.</summary>
    public abstract class MenuScreen : MonoBehaviour
    {
        protected UIManager Ui { get; private set; }
        public RectTransform Root => (RectTransform)transform;

        public void Attach(UIManager ui)
        {
            Ui = ui;
            Build();
            gameObject.SetActive(false);
        }

        protected abstract void Build();

        /// <summary>Called each time the screen becomes the top of the stack.</summary>
        public virtual void OnShown() { }

        /// <summary>The control that gets keyboard/gamepad focus when this screen opens.</summary>
        public virtual Selectable FirstSelectable => GetComponentInChildren<Selectable>();

        /// <summary>Return false to block closing with Esc / B (e.g. while a choice is required).</summary>
        public virtual bool CanCloseWithBack => true;
    }

    /// <summary>
    /// Builds the canvas and every screen, owns the menu stack (pause > settings > ...), keeps
    /// keyboard/gamepad focus valid, and reacts to game state changes.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        private readonly List<MenuScreen> _stack = new List<MenuScreen>();
        private Canvas _canvas;

        public TitleScreen Title { get; private set; }
        public HudScreen Hud { get; private set; }
        public PauseMenu Pause { get; private set; }
        public SettingsMenu Settings { get; private set; }
        public ControlsScreen Controls { get; private set; }
        public BestiaryScreen Bestiary { get; private set; }
        public ConfirmDialog Confirm { get; private set; }
        public DefeatScreen Defeat { get; private set; }

        /// <summary>Frame on which a menu last opened/closed (prevents one key press doing two things).</summary>
        public int MenuChangedFrame { get; private set; } = -1;
        public bool IsMenuOpen => _stack.Count > 0;

        public static UIManager Create(Transform parent)
        {
            var go = new GameObject("UI");
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var ui = go.AddComponent<UIManager>();
            ui.Build();
            return ui;
        }

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            Hud = CreateScreen<HudScreen>("HUD", false);
            Hud.Initialize(this);
            Title = CreateScreen<TitleScreen>("Title", true);
            Pause = CreateScreen<PauseMenu>("PauseMenu", true);
            Settings = CreateScreen<SettingsMenu>("Settings", true);
            Controls = CreateScreen<ControlsScreen>("Controls", true);
            Bestiary = CreateScreen<BestiaryScreen>("Bestiary", true);
            Defeat = CreateScreen<DefeatScreen>("Defeat", true);
            Confirm = CreateScreen<ConfirmDialog>("Confirm", true);
        }

        private T CreateScreen<T>(string name, bool isMenu) where T : MonoBehaviour
        {
            var rt = UIFactory.Rect(transform, name);
            UIFactory.Stretch(rt);
            var screen = rt.gameObject.AddComponent<T>();
            if (screen is MenuScreen menu) menu.Attach(this);
            return screen;
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if BB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        // ------------------------------------------------------------------ Stack

        public void Push(MenuScreen screen)
        {
            if (screen == null) return;
            if (_stack.Count > 0) _stack[_stack.Count - 1].gameObject.SetActive(false);
            _stack.Remove(screen);
            _stack.Add(screen);
            screen.gameObject.SetActive(true);
            screen.transform.SetAsLastSibling();
            screen.OnShown();
            FocusTop();
            MenuChangedFrame = Time.frameCount;
            AudioManager.PlayUi(SfxId.UiOpen, 0.5f);
        }

        public void Pop()
        {
            if (_stack.Count == 0) return;
            var top = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            top.gameObject.SetActive(false);
            MenuChangedFrame = Time.frameCount;
            AudioManager.PlayUi(SfxId.UiBack, 0.5f);
            if (_stack.Count > 0)
            {
                var next = _stack[_stack.Count - 1];
                next.gameObject.SetActive(true);
                next.OnShown();
                FocusTop();
            }
            else
            {
                OnStackEmptied();
            }
        }

        public void CloseAll()
        {
            foreach (var s in _stack) s.gameObject.SetActive(false);
            _stack.Clear();
            MenuChangedFrame = Time.frameCount;
        }

        private void OnStackEmptied()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            if (gm.State == GameState.Paused) gm.Resume();
            else if (gm.State == GameState.Title) FocusTitle();
        }

        private void FocusTop()
        {
            if (_stack.Count == 0) return;
            var first = _stack[_stack.Count - 1].FirstSelectable;
            Select(first != null ? first.gameObject : null);
        }

        private void FocusTitle()
        {
            if (Title != null && Title.gameObject.activeSelf)
            {
                var first = Title.FirstSelectable;
                Select(first != null ? first.gameObject : null);
            }
        }

        private static void Select(GameObject go)
        {
            if (EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            if (go != null) EventSystem.current.SetSelectedGameObject(go);
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (_stack.Count > 0 && MenuChangedFrame != Time.frameCount)
            {
                var top = _stack[_stack.Count - 1];
                bool closeAll = (top == Bestiary && GameInput.BestiaryPressed) || (gm.State == GameState.Paused && GameInput.PausePressed && !GameInput.CancelPressed);
                if (closeAll && gm.State == GameState.Paused)
                {
                    CloseAll();
                    gm.Resume();
                }
                else if (GameInput.CancelPressed && top.CanCloseWithBack)
                {
                    Pop();
                }
            }

            // Keep a valid selection for keyboard/gamepad users (mouse clicks on empty space clear it).
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null
                && GameInput.LastDevice == InputDeviceKind.Gamepad)
            {
                if (_stack.Count > 0) FocusTop();
                else if (gm.State == GameState.Title) FocusTitle();
                else if (gm.State == GameState.Defeat && Defeat.gameObject.activeSelf)
                {
                    var f = Defeat.FirstSelectable;
                    Select(f != null ? f.gameObject : null);
                }
            }
        }

        // ------------------------------------------------------------------ Game hooks

        public void OnGameStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.Title:
                    CloseAll();
                    Hud.gameObject.SetActive(false);
                    Defeat.gameObject.SetActive(false);
                    Title.gameObject.SetActive(true);
                    Title.OnShown();
                    FocusTitle();
                    break;
                case GameState.Playing:
                    CloseAll();
                    Title.gameObject.SetActive(false);
                    Defeat.gameObject.SetActive(false);
                    Hud.gameObject.SetActive(true);
                    Select(null);
                    break;
                case GameState.Paused:
                    Hud.gameObject.SetActive(false);
                    break;
                case GameState.Defeat:
                    CloseAll();
                    Hud.gameObject.SetActive(false);
                    Defeat.gameObject.SetActive(true);
                    Defeat.OnShown();
                    var f = Defeat.FirstSelectable;
                    Select(f != null ? f.gameObject : null);
                    break;
            }
        }

        public void OpenPauseMenu() => Push(Pause);

        public void OpenBestiary()
        {
            Push(Bestiary);
            GameEvents.RaiseBestiaryOpened();
        }

        public void OpenSettings() => Push(Settings);
        public void OpenControls() => Push(Controls);

        public void AskConfirm(string title, string message, string yes, System.Action onYes)
        {
            Confirm.Setup(title, message, yes, onYes);
            Push(Confirm);
        }

        public void ToggleControlsOverlay() => Hud?.ToggleControlsHint();

        public void ShowBanner(string title, string subtitle) => Hud?.ShowBanner(title, subtitle);
    }
}
