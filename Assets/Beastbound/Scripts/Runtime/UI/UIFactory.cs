using System;
using Beastbound.Audio;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Beastbound.UI
{
    /// <summary>Colour palette and sizes for the whole UI, in one place.</summary>
    public static class UITheme
    {
        public static readonly Color Panel = new Color(0.06f, 0.1f, 0.16f, 0.88f);
        public static readonly Color PanelLight = new Color(0.12f, 0.18f, 0.27f, 0.92f);
        public static readonly Color PanelBorder = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Overlay = new Color(0.02f, 0.04f, 0.08f, 0.6f);
        public static readonly Color Text = new Color(0.95f, 0.96f, 0.93f);
        public static readonly Color TextDim = new Color(0.72f, 0.77f, 0.8f);
        public static readonly Color Accent = new Color(0.98f, 0.78f, 0.32f);   // warm gold
        public static readonly Color Nature = new Color(0.5f, 0.9f, 0.72f);     // mint
        public static readonly Color Danger = new Color(0.95f, 0.38f, 0.35f);
        public static readonly Color Health = new Color(0.45f, 0.88f, 0.48f);
        public static readonly Color HealthLow = new Color(0.95f, 0.45f, 0.35f);
        public static readonly Color Button = new Color(0.16f, 0.25f, 0.36f, 0.95f);
        public static readonly Color ButtonHover = new Color(0.24f, 0.38f, 0.52f, 1f);
        public static readonly Color ButtonPressed = new Color(0.12f, 0.2f, 0.3f, 1f);
        public static readonly Color ButtonPrimary = new Color(0.86f, 0.62f, 0.2f, 1f);
        public static readonly Color ButtonPrimaryHover = new Color(0.98f, 0.76f, 0.32f, 1f);

        public const int TitleSize = 96;
        public const int HeadingSize = 40;
        public const int BodySize = 24;
        public const int SmallSize = 19;
    }

    /// <summary>
    /// Builds uGUI elements from code with a consistent style. Rounded corners and icons are generated
    /// textures, so the UI needs no imported assets (swap in artist sprites later if desired).
    /// </summary>
    public static class UIFactory
    {
        private static Font _font;
        private static Sprite _rounded;
        private static Sprite _roundedSmall;
        private static Sprite _circle;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Liberation Sans", "DejaVu Sans" }, 24);
                return _font;
            }
        }

        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = CreateRoundedSprite(64, 18));
        public static Sprite RoundedSmall => _roundedSmall != null ? _roundedSmall : (_roundedSmall = CreateRoundedSprite(32, 8));
        public static Sprite Circle => _circle != null ? _circle : (_circle = CreateRoundedSprite(64, 32));

        private static Sprite CreateRoundedSprite(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "UIRounded" + radius,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance to the rounded-rectangle edge, anti-aliased over one pixel.
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float border = Mathf.Min(radius, size / 2f - 1f);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        // ------------------------------------------------------------------ Layout helpers

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        /// <summary>Anchors at a normalised point with a fixed size and offset.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : Rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        public static Image Fill(Transform parent, string name, Color color)
        {
            var img = Panel(parent, name, color, null);
            img.sprite = null;
            img.type = Image.Type.Simple;
            Stretch(img.rectTransform);
            return img;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        public static VerticalLayoutGroup Vertical(RectTransform rt, float spacing, int padding, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static LayoutElement Size(Component c, float preferredHeight, float preferredWidth = -1f)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (preferredHeight >= 0f) le.preferredHeight = preferredHeight;
            if (preferredWidth >= 0f) le.preferredWidth = preferredWidth;
            return le;
        }

        // ------------------------------------------------------------------ Controls

        public static Button Button(Transform parent, string label, UnityAction onClick, bool primary = false, int fontSize = UITheme.BodySize)
        {
            var img = Panel(parent, "Button_" + label, Color.white, Rounded);
            var button = img.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = primary ? UITheme.ButtonPrimary : UITheme.Button;
            colors.highlightedColor = primary ? UITheme.ButtonPrimaryHover : UITheme.ButtonHover;
            colors.selectedColor = primary ? UITheme.ButtonPrimaryHover : UITheme.ButtonHover;
            colors.pressedColor = UITheme.ButtonPressed;
            colors.disabledColor = new Color(0.2f, 0.22f, 0.25f, 0.6f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.targetGraphic = img;

            var text = Label(img.transform, "Label", label, fontSize, primary ? new Color(0.12f, 0.08f, 0.02f) : UITheme.Text,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(text.rectTransform, 6f);
            if (primary) UnityEngine.Object.Destroy(text.GetComponent<Shadow>());

            if (onClick != null) button.onClick.AddListener(onClick);
            button.onClick.AddListener(() => AudioManager.PlayUi(SfxId.UiClick));
            AddHoverSound(button.gameObject);
            Size(img, 58f);
            return button;
        }

        public static void AddHoverSound(GameObject go)
        {
            var trigger = go.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            entry.callback.AddListener(_ => AudioManager.PlayUi(SfxId.UiMove, 0.4f));
            trigger.triggers.Add(entry);
            var hover = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            hover.callback.AddListener(_ =>
            {
                // Mouse hover also moves keyboard/gamepad selection so both stay in sync.
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(go);
            });
            trigger.triggers.Add(hover);
        }

        /// <summary>Horizontal bar (health, stats). Returns the fill image; set fillAmount 0..1.</summary>
        public static Image Bar(Transform parent, string name, Color fillColor, out Image background)
        {
            background = Panel(parent, name, new Color(0f, 0f, 0f, 0.45f), RoundedSmall);
            var fill = Panel(background.transform, "Fill", fillColor, null);
            fill.sprite = null;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 1f;
            // Filled images need a sprite to clip; a plain white texture is generated for that.
            fill.sprite = WhiteSprite;
            Stretch(fill.rectTransform, 3f);
            return fill;
        }

        private static Sprite _white;
        public static Sprite WhiteSprite
        {
            get
            {
                if (_white != null) return _white;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "UIWhite" };
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = Color.white;
                tex.SetPixels32(px);
                tex.Apply(false, true);
                _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                return _white;
            }
        }

        /// <summary>Labelled slider row. Returns the Slider.</summary>
        public static Slider SliderRow(Transform parent, string label, float min, float max, float value,
            Func<float, string> format, UnityAction<float> onChange)
        {
            var row = Rect(parent, "Row_" + label);
            Size(row, 54f);
            var text = Label(row, "Label", label, UITheme.BodySize, UITheme.Text);
            Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(300f, 40f));
            var valueText = Label(row, "Value", format(value), UITheme.BodySize, UITheme.Accent, TextAnchor.MiddleRight);
            Place(valueText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(110f, 40f));

            var sliderRt = Rect(row, "Slider");
            sliderRt.anchorMin = new Vector2(0f, 0.5f);
            sliderRt.anchorMax = new Vector2(1f, 0.5f);
            sliderRt.pivot = new Vector2(0.5f, 0.5f);
            sliderRt.offsetMin = new Vector2(320f, -12f);
            sliderRt.offsetMax = new Vector2(-130f, 12f);

            var bg = Panel(sliderRt, "Background", new Color(0f, 0f, 0f, 0.45f), RoundedSmall);
            Stretch(bg.rectTransform);
            var fillArea = Rect(sliderRt, "FillArea");
            Stretch(fillArea, 4f);
            var fill = Panel(fillArea, "Fill", UITheme.Nature, RoundedSmall);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = Rect(sliderRt, "HandleArea");
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(12f, 0f);
            handleArea.offsetMax = new Vector2(-12f, 0f);
            var handle = Panel(handleArea, "Handle", Color.white, Circle);
            handle.rectTransform.sizeDelta = new Vector2(30f, 30f);

            var slider = sliderRt.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            var colors = slider.colors;
            colors.highlightedColor = UITheme.Accent;
            colors.selectedColor = UITheme.Accent;
            slider.colors = colors;
            slider.onValueChanged.AddListener(v => valueText.text = format(v));
            if (onChange != null) slider.onValueChanged.AddListener(onChange);
            AddHoverSound(slider.gameObject);
            return slider;
        }

        /// <summary>A "&lt; value &gt;" selector, friendlier on gamepad than a dropdown.</summary>
        public static Cycler CyclerRow(Transform parent, string label, string[] options, int index, UnityAction<int> onChange)
        {
            var row = Rect(parent, "Row_" + label);
            Size(row, 54f);
            var text = Label(row, "Label", label, UITheme.BodySize, UITheme.Text);
            Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(300f, 40f));

            var box = Panel(row, "Box", Color.white, Rounded);
            box.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            box.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            box.rectTransform.offsetMin = new Vector2(320f, -22f);
            box.rectTransform.offsetMax = new Vector2(-8f, 22f);
            var button = box.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = UITheme.Button;
            colors.highlightedColor = UITheme.ButtonHover;
            colors.selectedColor = UITheme.ButtonHover;
            colors.pressedColor = UITheme.ButtonPressed;
            button.colors = colors;
            button.targetGraphic = box;
            // Left/right changes the value, so only allow vertical navigation away from this control.
            button.navigation = new Navigation { mode = Navigation.Mode.Vertical };
            var value = Label(box.transform, "Value", "", UITheme.BodySize, UITheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(value.rectTransform);
            var cycler = box.gameObject.AddComponent<Cycler>();
            cycler.Setup(options, index, value, onChange);
            button.onClick.AddListener(() => cycler.Step(1));
            AddHoverSound(box.gameObject);
            return cycler;
        }

        public static Toggle ToggleRow(Transform parent, string label, bool value, UnityAction<bool> onChange)
        {
            var row = Rect(parent, "Row_" + label);
            Size(row, 54f);
            var text = Label(row, "Label", label, UITheme.BodySize, UITheme.Text);
            Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(420f, 40f));

            var box = Panel(row, "Box", UITheme.Button, RoundedSmall);
            Place(box.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(44f, 44f));
            var check = Panel(box.transform, "Check", UITheme.Nature, RoundedSmall);
            Stretch(check.rectTransform, 8f);
            var toggle = box.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.isOn = value;
            var colors = toggle.colors;
            colors.highlightedColor = UITheme.ButtonHover;
            colors.selectedColor = UITheme.Accent;
            toggle.colors = colors;
            if (onChange != null) toggle.onValueChanged.AddListener(onChange);
            toggle.onValueChanged.AddListener(_ => AudioManager.PlayUi(SfxId.UiClick));
            AddHoverSound(box.gameObject);
            return toggle;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _font = null;
            _rounded = null;
            _roundedSmall = null;
            _circle = null;
            _white = null;
        }
    }

    /// <summary>Selector that cycles through options with click, or left/right on keyboard/gamepad.</summary>
    public class Cycler : MonoBehaviour, IMoveHandler
    {
        private string[] _options;
        private Text _label;
        private UnityAction<int> _onChange;

        public int Index { get; private set; }

        public void Setup(string[] options, int index, Text label, UnityAction<int> onChange)
        {
            _options = options ?? new string[0];
            _label = label;
            _onChange = onChange;
            Index = Mathf.Clamp(index, 0, Mathf.Max(0, _options.Length - 1));
            Refresh();
        }

        public void Step(int delta)
        {
            if (_options == null || _options.Length == 0) return;
            Index = (Index + delta + _options.Length) % _options.Length;
            Refresh();
            _onChange?.Invoke(Index);
        }

        private void Refresh()
        {
            if (_label != null && _options != null && _options.Length > 0) _label.text = "<   " + _options[Index] + "   >";
        }

        public void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left) { Step(-1); eventData.Use(); }
            else if (eventData.moveDir == MoveDirection.Right) { Step(1); eventData.Use(); }
        }
    }
}
