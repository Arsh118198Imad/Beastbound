using System.Collections.Generic;
using Beastbound.Combat;
using Beastbound.Controls;
using Beastbound.Core;
using Beastbound.CreatureAI;
using Beastbound.Creatures;
using Beastbound.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace Beastbound.UI
{
    /// <summary>
    /// In-game HUD. Deliberately minimal and uncluttered: health and cooldowns bottom-left, one objective
    /// top-left, context prompt bottom-centre, enemy bar only during fights, and small markers above
    /// creatures that show exactly how aware they are of you.
    /// </summary>
    public class HudScreen : MonoBehaviour
    {
        private UIManager _ui;

        // Player
        private Image _hpFill;
        private Text _hpText;
        private Image _sparkFill, _dodgeFill;
        private Text _statusText;
        private Text _partnerText;
        private Image _lowHealthVignette;

        // Objective
        private Text _objectiveTitle, _objectiveBody;

        // Prompt
        private RectTransform _promptPanel;
        private Text _promptText, _promptDetail;

        // Enemy
        private RectTransform _enemyPanel;
        private Text _enemyName;
        private Image _enemyFill;

        // Toasts / banner
        private RectTransform _toastRoot;
        private readonly List<Toast> _toasts = new List<Toast>();
        private Text _bannerTitle, _bannerSubtitle;
        private CanvasGroup _bannerGroup;
        private float _bannerUntil;

        // Markers / damage numbers
        private RectTransform _markerRoot;
        private readonly List<Text> _markers = new List<Text>();
        private readonly List<DamageNumber> _damageNumbers = new List<DamageNumber>();

        // Misc
        private Text _fpsText;
        private float _fpsTimer;
        private int _fpsFrames;
        private RectTransform _controlsHint;
        private Text _controlsHintText;
        private bool _controlsExpanded;

        private class Toast
        {
            public RectTransform Rect;
            public Text Text;
            public CanvasGroup Group;
            public float Born;
        }

        private class DamageNumber
        {
            public Text Text;
            public Vector3 World;
            public float Born;
        }

        public void Initialize(UIManager ui)
        {
            _ui = ui;
            var root = (RectTransform)transform;

            _lowHealthVignette = UIFactory.Fill(root, "LowHealth", new Color(0.8f, 0.1f, 0.1f, 0f));
            _lowHealthVignette.raycastTarget = false;

            _markerRoot = UIFactory.Rect(root, "Markers");
            UIFactory.Stretch(_markerRoot);

            BuildPlayerPanel(root);
            BuildObjective(root);
            BuildPrompt(root);
            BuildEnemyPanel(root);
            BuildToasts(root);
            BuildBanner(root);
            BuildMisc(root);

            Health.AnyDamaged += OnAnyDamaged;
            GameEvents.Notification += OnNotification;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            Health.AnyDamaged -= OnAnyDamaged;
            GameEvents.Notification -= OnNotification;
        }

        // ------------------------------------------------------------------ Build

        private void BuildPlayerPanel(RectTransform root)
        {
            var panel = UIFactory.Panel(root, "PlayerPanel", UITheme.Panel);
            UIFactory.Place(panel.rectTransform, Vector2.zero, Vector2.zero, new Vector2(32f, 32f), new Vector2(430f, 150f));

            var name = UIFactory.Label(panel.transform, "Name", "Warden", UITheme.BodySize, UITheme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(200f, 32f));
            _statusText = UIFactory.Label(panel.transform, "Status", "", UITheme.SmallSize, UITheme.Nature, TextAnchor.UpperRight);
            UIFactory.Place(_statusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -14f), new Vector2(200f, 28f));

            _hpFill = UIFactory.Bar(panel.transform, "HP", UITheme.Health, out var hpBg);
            UIFactory.Place(hpBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -50f), new Vector2(390f, 26f));
            _hpText = UIFactory.Label(hpBg.transform, "Value", "", UITheme.SmallSize, UITheme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(_hpText.rectTransform);

            _sparkFill = CooldownPill(panel.transform, "Spark", new Vector2(20f, -90f), new Color(1f, 0.6f, 0.25f));
            _dodgeFill = CooldownPill(panel.transform, "Dodge", new Vector2(215f, -90f), UITheme.Nature);

            _partnerText = UIFactory.Label(panel.transform, "Partner", "", UITheme.SmallSize, UITheme.TextDim, TextAnchor.UpperLeft);
            UIFactory.Place(_partnerText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -122f), new Vector2(390f, 26f));
        }

        private Image CooldownPill(Transform parent, string label, Vector2 pos, Color color)
        {
            var fill = UIFactory.Bar(parent, label, color, out var bg);
            UIFactory.Place(bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(195f, 24f));
            var text = UIFactory.Label(bg.transform, "Label", label, UITheme.SmallSize - 2, UITheme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(text.rectTransform);
            return fill;
        }

        private void BuildObjective(RectTransform root)
        {
            var panel = UIFactory.Panel(root, "Objective", UITheme.Panel);
            UIFactory.Place(panel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -32f), new Vector2(500f, 150f));
            var tag = UIFactory.Label(panel.transform, "Tag", "OBJECTIVE", UITheme.SmallSize - 3, UITheme.TextDim, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(tag.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(300f, 22f));
            _objectiveTitle = UIFactory.Label(panel.transform, "Title", "", UITheme.BodySize + 2, UITheme.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(_objectiveTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -34f), new Vector2(460f, 34f));
            _objectiveBody = UIFactory.Label(panel.transform, "Body", "", UITheme.SmallSize, UITheme.Text, TextAnchor.UpperLeft);
            UIFactory.Place(_objectiveBody.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -70f), new Vector2(460f, 80f));
        }

        private void BuildPrompt(RectTransform root)
        {
            var panel = UIFactory.Panel(root, "Prompt", UITheme.Panel);
            _promptPanel = panel.rectTransform;
            UIFactory.Place(_promptPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(640f, 96f));
            _promptText = UIFactory.Label(panel.transform, "Text", "", UITheme.BodySize + 2, UITheme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_promptText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(620f, 40f));
            _promptDetail = UIFactory.Label(panel.transform, "Detail", "", UITheme.SmallSize, UITheme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_promptDetail.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(620f, 36f));
            _promptPanel.gameObject.SetActive(false);
        }

        private void BuildEnemyPanel(RectTransform root)
        {
            var panel = UIFactory.Panel(root, "Enemy", UITheme.Panel);
            _enemyPanel = panel.rectTransform;
            UIFactory.Place(_enemyPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(620f, 92f));
            _enemyName = UIFactory.Label(panel.transform, "Name", "", UITheme.BodySize, UITheme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Place(_enemyName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(600f, 32f));
            _enemyFill = UIFactory.Bar(panel.transform, "HP", UITheme.Danger, out var bg);
            UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(560f, 24f));
            _enemyPanel.gameObject.SetActive(false);
        }

        private void BuildToasts(RectTransform root)
        {
            _toastRoot = UIFactory.Rect(root, "Toasts");
            UIFactory.Place(_toastRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(760f, 300f));
        }

        private void BuildBanner(RectTransform root)
        {
            var rt = UIFactory.Rect(root, "Banner");
            UIFactory.Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(1000f, 160f));
            _bannerGroup = rt.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            _bannerGroup.blocksRaycasts = false;
            _bannerTitle = UIFactory.Label(rt, "Title", "", 72, UITheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_bannerTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1000f, 96f));
            _bannerSubtitle = UIFactory.Label(rt, "Subtitle", "", UITheme.BodySize, UITheme.Text, TextAnchor.MiddleCenter);
            UIFactory.Place(_bannerSubtitle.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1000f, 50f));
        }

        private void BuildMisc(RectTransform root)
        {
            _fpsText = UIFactory.Label(root, "FPS", "", UITheme.SmallSize, UITheme.TextDim, TextAnchor.UpperRight);
            UIFactory.Place(_fpsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -16f), new Vector2(200f, 30f));

            var panel = UIFactory.Panel(root, "ControlsHint", UITheme.Panel);
            _controlsHint = panel.rectTransform;
            UIFactory.Place(_controlsHint, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 32f), new Vector2(330f, 60f));
            _controlsHintText = UIFactory.Label(panel.transform, "Text", "", UITheme.SmallSize, UITheme.TextDim, TextAnchor.UpperLeft);
            UIFactory.Stretch(_controlsHintText.rectTransform, 14f);
        }

        // ------------------------------------------------------------------ Public API

        public void ToggleControlsHint()
        {
            _controlsExpanded = !_controlsExpanded;
        }

        public void ShowBanner(string title, string subtitle)
        {
            _bannerTitle.text = title;
            _bannerSubtitle.text = subtitle;
            _bannerUntil = Time.unscaledTime + 3f;
        }

        // ------------------------------------------------------------------ Events

        private void OnNotification(string message, NotificationKind kind)
        {
            if (_toastRoot == null) return;
            var panel = UIFactory.Panel(_toastRoot, "Toast", UITheme.PanelLight);
            var rt = panel.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(740f, 60f); // width first, so the text can measure its wrapped height
            Color accent;
            switch (kind)
            {
                case NotificationKind.Success: accent = UITheme.Nature; break;
                case NotificationKind.Warning: accent = UITheme.HealthLow; break;
                case NotificationKind.Discovery: accent = UITheme.Accent; break;
                default: accent = UITheme.Text; break;
            }
            var stripe = UIFactory.Panel(panel.transform, "Stripe", accent, UIFactory.RoundedSmall);
            stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
            stripe.rectTransform.anchorMax = new Vector2(0f, 1f);
            stripe.rectTransform.pivot = new Vector2(0f, 0.5f);
            stripe.rectTransform.offsetMin = new Vector2(8f, 10f);
            stripe.rectTransform.offsetMax = new Vector2(14f, -10f);
            var text = UIFactory.Label(panel.transform, "Text", message, UITheme.SmallSize + 1, UITheme.Text, TextAnchor.MiddleLeft);
            UIFactory.Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(28f, 8f);
            text.rectTransform.offsetMax = new Vector2(-18f, -8f);
            float height = Mathf.Max(52f, text.preferredHeight + 20f);
            rt.sizeDelta = new Vector2(740f, Mathf.Clamp(height, 52f, 120f));
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            _toasts.Add(new Toast { Rect = rt, Text = text, Group = group, Born = Time.unscaledTime });
            while (_toasts.Count > 4)
            {
                Destroy(_toasts[0].Rect.gameObject);
                _toasts.RemoveAt(0);
            }
        }

        private void OnAnyDamaged(Health target, DamageInfo info)
        {
            if (!isActiveAndEnabled || target == null) return;
            bool playerHurt = target.Faction == Faction.Player;
            string label = Mathf.RoundToInt(info.Amount).ToString();
            if (info.Effective) label += " <size=16>Effective!</size>";
            else if (info.Resisted) label += " <size=16>Resisted</size>";
            Color color = playerHurt ? UITheme.Danger : (info.Effective ? UITheme.Accent : UITheme.Text);

            DamageNumber dn = null;
            foreach (var d in _damageNumbers)
            {
                if (!d.Text.gameObject.activeSelf) { dn = d; break; }
            }
            if (dn == null)
            {
                var t = UIFactory.Label(_markerRoot, "Damage", "", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.rectTransform.sizeDelta = new Vector2(260f, 50f);
                dn = new DamageNumber { Text = t };
                _damageNumbers.Add(dn);
            }
            dn.Text.text = label;
            dn.Text.color = color;
            dn.World = target.transform.position + Vector3.up * 1.4f + Random.insideUnitSphere * 0.3f;
            dn.Born = Time.time;
            dn.Text.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------ Update

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.Player == null) return;
            var player = gm.Player;

            UpdatePlayer(gm, player);
            UpdateObjective(gm);
            UpdatePrompt(player);
            UpdateEnemy(gm);
            UpdateToasts();
            UpdateBanner();
            UpdateMarkers(gm);
            UpdateDamageNumbers();
            UpdateMisc(gm);
        }

        private void UpdatePlayer(GameManager gm, Player.PlayerController player)
        {
            var hp = player.Health;
            float frac = hp != null ? hp.Fraction : 1f;
            _hpFill.fillAmount = Mathf.Lerp(_hpFill.fillAmount, frac, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            _hpFill.color = Color.Lerp(UITheme.HealthLow, UITheme.Health, Mathf.InverseLerp(0.2f, 0.6f, frac));
            _hpText.text = hp != null ? $"{Mathf.CeilToInt(hp.Current)} / {Mathf.RoundToInt(hp.Max)}" : "";
            if (player.Combat != null)
            {
                _sparkFill.fillAmount = player.Combat.SparkCooldown01;
                _dodgeFill.fillAmount = player.Combat.DodgeCooldown01;
            }
            string status = player.IsSneaking ? "Sneaking" : (player.IsWading ? "Wading" : (player.IsRunning ? "Running" : ""));
            _statusText.text = status;

            var partner = gm.Collection != null ? gm.Collection.Partner : null;
            if (partner != null)
            {
                var species = gm.Catalog.Get(partner.speciesId);
                _partnerText.text = $"Partner: {partner.DisplayName(species)}  Lv {partner.level}  ({partner.personality})";
            }
            else
            {
                _partnerText.text = "No partner yet";
            }

            float vignette = frac < 0.35f ? (0.35f - frac) / 0.35f * 0.35f * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f)) : 0f;
            _lowHealthVignette.color = new Color(0.8f, 0.1f, 0.1f, vignette);
        }

        private void UpdateObjective(GameManager gm)
        {
            if (gm.Objectives == null) return;
            _objectiveTitle.text = gm.Objectives.Title;
            _objectiveBody.text = gm.Objectives.Description;
        }

        private void UpdatePrompt(Player.PlayerController player)
        {
            var binder = player.Binder;
            var target = binder != null ? binder.CurrentTarget : null;
            if (target == null || binder.IsBusy)
            {
                if (binder != null && binder.IsBusy)
                {
                    _promptPanel.gameObject.SetActive(true);
                    _promptText.text = "Binding...";
                    _promptDetail.text = "";
                }
                else
                {
                    _promptPanel.gameObject.SetActive(false);
                }
                return;
            }

            _promptPanel.gameObject.SetActive(true);
            float chance = target.CaptureChance;
            int pct = Mathf.RoundToInt(chance * 100f);
            string color = pct >= 70 ? "#8BE3B4" : (pct >= 40 ? "#FAC75A" : "#F2735E");
            _promptText.text = $"[{GameInput.Prompt("Bind")}] Bind {target.DisplayName}  <color={color}>{pct}%</color>";

            var awareness = target.CurrentAwareness;
            if (awareness == Awareness.Exhausted)
            {
                _promptDetail.text = "Exhausted — guaranteed bind";
                return;
            }
            string state;
            switch (awareness)
            {
                case Awareness.Asleep: state = "Asleep x2.0"; break;
                case Awareness.Unaware: state = "Unaware x1.5"; break;
                case Awareness.Hostile: state = "Hostile x0.85"; break;
                default: state = "Alert x1.0"; break;
            }
            int hpPct = Mathf.RoundToInt(target.Health.Fraction * 100f);
            string resonance = target.FailedBindAttempts > 0
                ? $"  ·  Resonance +{Mathf.RoundToInt(target.FailedBindAttempts * CaptureRules.ResonancePerFailure * 100f)}%"
                : "";
            _promptDetail.text = $"{state}  ·  HP {hpPct}% (lower = easier){resonance}";
        }

        private void UpdateEnemy(GameManager gm)
        {
            var enemy = gm.Battle != null ? gm.Battle.PrimaryEnemy : null;
            bool show = enemy != null && enemy.Health != null;
            if (_enemyPanel.gameObject.activeSelf != show) _enemyPanel.gameObject.SetActive(show);
            if (!show) return;
            _enemyName.text = $"{enemy.DisplayName}  <size=18><color=#B8C4CC>Lv {enemy.Record.level} · {enemy.Record.personality} · {enemy.Species.element}</color></size>";
            _enemyFill.fillAmount = Mathf.Lerp(_enemyFill.fillAmount, enemy.Health.Fraction, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        }

        private void UpdateToasts()
        {
            float y = 0f;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                float age = Time.unscaledTime - t.Born;
                const float life = 5f;
                if (age > life)
                {
                    Destroy(t.Rect.gameObject);
                    _toasts.RemoveAt(i);
                    continue;
                }
                t.Group.alpha = Mathf.Clamp01(age * 5f) * Mathf.Clamp01((life - age) * 1.5f);
            }
            // Newest at the top
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                Vector2 target = new Vector2(0f, -y);
                t.Rect.anchoredPosition = Vector2.Lerp(t.Rect.anchoredPosition, target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                y += t.Rect.sizeDelta.y + 8f;
            }
        }

        private void UpdateBanner()
        {
            float remaining = _bannerUntil - Time.unscaledTime;
            float target = remaining > 0f ? Mathf.Clamp01(remaining * 2f) : 0f;
            _bannerGroup.alpha = Mathf.MoveTowards(_bannerGroup.alpha, target, Time.unscaledDeltaTime * 4f);
        }

        private void UpdateMarkers(GameManager gm)
        {
            var cam = Camera.main;
            int used = 0;
            if (cam != null && gm.Spawner != null)
            {
                foreach (var c in gm.Spawner.WildCreatures)
                {
                    if (c == null || c.Brain == null) continue;
                    var indicator = c.Brain.Indicator;
                    if (indicator == CreatureIndicator.None) continue;
                    Vector3 world = c.MarkerPoint;
                    if ((world - cam.transform.position).sqrMagnitude > 45f * 45f) continue;
                    Vector3 screen = cam.WorldToScreenPoint(world);
                    if (screen.z <= 0f) continue;

                    var marker = GetMarker(used++);
                    marker.rectTransform.position = new Vector3(screen.x, screen.y, 0f);
                    float suspicion = c.Perception != null ? c.Perception.Suspicion : 0f;
                    switch (indicator)
                    {
                        case CreatureIndicator.Suspicious:
                            marker.text = "?";
                            marker.color = Color.Lerp(new Color(1f, 1f, 1f, 0.5f), UITheme.Accent, suspicion);
                            break;
                        case CreatureIndicator.Noticed:
                            marker.text = "!";
                            marker.color = UITheme.Accent;
                            break;
                        case CreatureIndicator.Hostile:
                            marker.text = "!!";
                            marker.color = UITheme.Danger;
                            break;
                        case CreatureIndicator.Sleeping:
                            marker.text = "Zz";
                            marker.color = new Color(0.7f, 0.85f, 1f);
                            break;
                        case CreatureIndicator.Exhausted:
                            marker.text = "100%";
                            marker.color = UITheme.Nature;
                            break;
                        default:
                            marker.text = "";
                            break;
                    }
                }
            }
            for (int i = used; i < _markers.Count; i++)
            {
                if (_markers[i].gameObject.activeSelf) _markers[i].gameObject.SetActive(false);
            }
        }

        private Text GetMarker(int index)
        {
            while (_markers.Count <= index)
            {
                var t = UIFactory.Label(_markerRoot, "Marker", "", 34, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.rectTransform.sizeDelta = new Vector2(120f, 50f);
                _markers.Add(t);
            }
            var m = _markers[index];
            if (!m.gameObject.activeSelf) m.gameObject.SetActive(true);
            return m;
        }

        private void UpdateDamageNumbers()
        {
            var cam = Camera.main;
            foreach (var d in _damageNumbers)
            {
                if (!d.Text.gameObject.activeSelf) continue;
                float age = Time.time - d.Born;
                if (age > 0.9f || cam == null)
                {
                    d.Text.gameObject.SetActive(false);
                    continue;
                }
                Vector3 screen = cam.WorldToScreenPoint(d.World + Vector3.up * age * 1.2f);
                if (screen.z <= 0f)
                {
                    d.Text.gameObject.SetActive(false);
                    continue;
                }
                d.Text.rectTransform.position = new Vector3(screen.x, screen.y, 0f);
                var c = d.Text.color;
                c.a = Mathf.Clamp01((0.9f - age) * 3f);
                d.Text.color = c;
                float s = 1f + Mathf.Max(0f, 0.15f - age) * 3f;
                d.Text.rectTransform.localScale = Vector3.one * s;
            }
        }

        private void UpdateMisc(GameManager gm)
        {
            bool showFps = gm.Settings != null && gm.Settings.showFps;
            if (_fpsText.gameObject.activeSelf != showFps) _fpsText.gameObject.SetActive(showFps);
            if (showFps)
            {
                _fpsFrames++;
                _fpsTimer += Time.unscaledDeltaTime;
                if (_fpsTimer >= 0.5f)
                {
                    _fpsText.text = $"{Mathf.RoundToInt(_fpsFrames / _fpsTimer)} FPS";
                    _fpsFrames = 0;
                    _fpsTimer = 0f;
                }
            }

            bool showHint = gm.Settings == null || gm.Settings.showControlsHint || _controlsExpanded;
            if (_controlsHint.gameObject.activeSelf != showHint) _controlsHint.gameObject.SetActive(showHint);
            if (!showHint) return;
            if (_controlsExpanded)
            {
                _controlsHintText.text =
                    $"Move  WASD / Left stick\nRun  {GameInput.Prompt("Run")}   Jump  {GameInput.Prompt("Jump")}\n" +
                    $"Sneak  {GameInput.Prompt("Sneak")}\nAttack  {GameInput.Prompt("Attack")}   Spark  {GameInput.Prompt("Spark")}\n" +
                    $"Dodge  {GameInput.Prompt("Dodge")}   Bind  {GameInput.Prompt("Bind")}\n" +
                    $"Bestiary  {GameInput.Prompt("Bestiary")}   Pause  {GameInput.Prompt("Pause")}\nF1  hide";
                _controlsHint.sizeDelta = new Vector2(380f, 230f);
            }
            else
            {
                _controlsHintText.text = $"F1  Controls     {GameInput.Prompt("Pause")}  Menu";
                _controlsHint.sizeDelta = new Vector2(330f, 54f);
            }
        }
    }
}
