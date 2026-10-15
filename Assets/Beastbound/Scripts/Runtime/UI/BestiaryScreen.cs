using System.Collections.Generic;
using Beastbound.Controls;
using Beastbound.Core;
using Beastbound.Creatures;
using Beastbound.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace Beastbound.UI
{
    /// <summary>
    /// The creature collection screen: list of bound creatures, a live 3D portrait, stats, personality,
    /// lore and a "Set as Partner" action. Every number that affects a creature is visible here.
    /// </summary>
    public class BestiaryScreen : MenuScreen
    {
        private RectTransform _list;
        private Text _countText;
        private RawImage _portraitImage;
        private Text _name, _subtitle, _description, _personality, _empty;
        private readonly Dictionary<string, Image> _statFills = new Dictionary<string, Image>();
        private readonly Dictionary<string, Text> _statValues = new Dictionary<string, Text>();
        private Button _partnerButton;
        private Text _partnerLabel;
        private Button _close;
        private RectTransform _detail;
        private CreaturePortrait _portrait;
        private string _selectedUid;
        private readonly List<Button> _entries = new List<Button>();

        private static readonly string[] StatKeys = { "HP", "ATK", "DEF", "SPD" };

        protected override void Build()
        {
            MenuLayout.Card(Root, "Bestiary", 1480f, 900f, out var content);

            _countText = UIFactory.Label(content, "Count", "", UITheme.SmallSize, UITheme.TextDim, TextAnchor.UpperLeft);
            UIFactory.Place(_countText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(400f, 30f));

            // Left: list
            var listPanel = UIFactory.Panel(content, "ListPanel", UITheme.PanelLight);
            listPanel.rectTransform.anchorMin = new Vector2(0f, 0f);
            listPanel.rectTransform.anchorMax = new Vector2(0f, 1f);
            listPanel.rectTransform.pivot = new Vector2(0f, 0.5f);
            listPanel.rectTransform.offsetMin = new Vector2(0f, 80f);
            listPanel.rectTransform.offsetMax = new Vector2(400f, -40f);
            _list = UIFactory.Rect(listPanel.transform, "List");
            UIFactory.Stretch(_list, 12f);
            UIFactory.Vertical(_list, 8f, 0, TextAnchor.UpperCenter);

            // Right: details
            _detail = UIFactory.Rect(content, "Detail");
            UIFactory.Stretch(_detail);
            _detail.offsetMin = new Vector2(430f, 80f);
            _detail.offsetMax = new Vector2(0f, 0f);

            var portraitFrame = UIFactory.Panel(_detail, "PortraitFrame", UITheme.PanelLight);
            UIFactory.Place(portraitFrame.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(420f, 420f));
            var portraitRt = UIFactory.Rect(portraitFrame.transform, "Portrait");
            UIFactory.Stretch(portraitRt, 10f);
            _portraitImage = portraitRt.gameObject.AddComponent<RawImage>();
            _portraitImage.raycastTarget = false;

            _name = UIFactory.Label(_detail, "Name", "", UITheme.HeadingSize, UITheme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(_name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(450f, 0f), new Vector2(510f, 54f));
            _subtitle = UIFactory.Label(_detail, "Subtitle", "", UITheme.BodySize - 2, UITheme.Nature, TextAnchor.UpperLeft);
            UIFactory.Place(_subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(450f, -56f), new Vector2(510f, 34f));

            float y = -110f;
            foreach (var key in StatKeys)
            {
                var label = UIFactory.Label(_detail, key + "Label", key, UITheme.SmallSize, UITheme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(450f, y), new Vector2(70f, 30f));
                var fill = UIFactory.Bar(_detail, key + "Bar", key == "HP" ? UITheme.Health : UITheme.Accent, out var bg);
                UIFactory.Place(bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(520f, y - 4f), new Vector2(340f, 22f));
                var value = UIFactory.Label(_detail, key + "Value", "", UITheme.SmallSize, UITheme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
                UIFactory.Place(value.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(870f, y), new Vector2(90f, 30f));
                _statFills[key] = fill;
                _statValues[key] = value;
                y -= 40f;
            }

            _personality = UIFactory.Label(_detail, "Personality", "", UITheme.SmallSize + 1, UITheme.Text, TextAnchor.UpperLeft);
            UIFactory.Place(_personality.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(450f, y - 10f), new Vector2(510f, 110f));

            _description = UIFactory.Label(_detail, "Description", "", UITheme.SmallSize + 1, UITheme.TextDim, TextAnchor.UpperLeft);
            UIFactory.Place(_description.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -440f), new Vector2(960f, 180f));

            var buttons = UIFactory.Rect(_detail, "Buttons");
            UIFactory.Place(buttons, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(960f, 58f));
            _partnerButton = UIFactory.Button(buttons, "Set as Partner", OnSetPartner, true);
            UIFactory.Place((RectTransform)_partnerButton.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(320f, 58f));
            _partnerLabel = _partnerButton.GetComponentInChildren<Text>();
            _close = UIFactory.Button(buttons, "Close", () => Ui.Pop());
            UIFactory.Place((RectTransform)_close.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(240f, 58f));

            _empty = UIFactory.Label(content, "Empty", "", UITheme.BodySize, UITheme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_empty.rectTransform);
            _empty.rectTransform.offsetMin = new Vector2(430f, 80f);
        }

        public override void OnShown()
        {
            if (_portrait == null) _portrait = CreaturePortrait.Create();
            RebuildList();
        }

        private void OnEnable()
        {
            if (_portrait != null) _portrait.SetActive(true);
        }

        private void OnDisable()
        {
            if (_portrait != null) _portrait.SetActive(false);
        }

        public override Selectable FirstSelectable => _entries.Count > 0 ? _entries[0] : (Selectable)_close;

        private void RebuildList()
        {
            foreach (var b in _entries) Destroy(b.gameObject);
            _entries.Clear();

            var gm = GameManager.Instance;
            var collection = gm != null ? gm.Collection : null;
            int count = collection != null ? collection.Count : 0;
            int nests = gm != null && gm.World != null ? gm.World.Nests.Count : 0;
            int species = gm != null && gm.Catalog != null ? gm.Catalog.Count : 1;
            int seen = 0;
            if (collection != null) foreach (var _ in collection.SeenSpecies) seen++;
            _countText.text = $"Bound: {count}/{nests} in this vale   ·   Species discovered: {seen}/{species}";

            if (count == 0)
            {
                _detail.gameObject.SetActive(false);
                _empty.gameObject.SetActive(true);
                _empty.text = "No creatures bound yet.\n\nFind a wild creature, sneak up or weaken it,\n" +
                              $"then press [{GameInput.Prompt("Bind")}] to throw a Bind Orb.";
                _portrait?.Show(null, null);
                return;
            }

            _empty.gameObject.SetActive(false);
            _detail.gameObject.SetActive(true);
            foreach (var record in collection.Records)
            {
                var r = record;
                var sp = gm.Catalog.Get(r.speciesId);
                string partnerMark = r.uid == collection.PartnerUid ? "  <color=#8BE3B4>(Partner)</color>" : "";
                var button = UIFactory.Button(_list, $"{r.DisplayName(sp)}  Lv {r.level}{partnerMark}", () => Select(r.uid), false, UITheme.SmallSize + 1);
                _entries.Add(button);
            }
            if (string.IsNullOrEmpty(_selectedUid) || collection.Find(_selectedUid) == null)
                _selectedUid = collection.PartnerUid ?? collection.Records[0].uid;
            Select(_selectedUid);
        }

        private void Select(string uid)
        {
            var gm = GameManager.Instance;
            var record = gm != null && gm.Collection != null ? gm.Collection.Find(uid) : null;
            if (record == null) return;
            _selectedUid = uid;
            var species = gm.Catalog.Get(record.speciesId);
            var stats = record.ComputeStats(species);

            _name.text = record.DisplayName(species);
            int stars = CreatureStatRules.VigorStars(record.vigor);
            string starText = new string('*', stars) + new string('-', 3 - stars);
            _subtitle.text = $"{(species != null ? species.displayName : record.speciesId)}  ·  {(species != null ? species.element.ToString() : "?")}  ·  Level {record.level}  ·  Vigor [{starText}]";

            SetStat("HP", stats.maxHealth, 150f);
            SetStat("ATK", stats.attack, 40f);
            SetStat("DEF", stats.defense, 40f);
            SetStat("SPD", stats.speed, 40f);

            _personality.text = $"<b>{record.personality}</b>\n{CreatureStatRules.Describe(record.personality)}";
            string lore = species != null ? $"{species.description}\n\n<i>Habitat:</i> {species.habitat}\n<i>Lore:</i> {species.lore}" : "";
            string attacks = "";
            if (species != null && species.attacks != null)
            {
                var names = new List<string>();
                foreach (var a in species.attacks) if (a != null) names.Add($"{a.displayName} ({a.element}, power {a.power:0})");
                attacks = "\n<i>Moves:</i> " + string.Join(", ", names);
            }
            _description.text = lore + attacks;

            bool isPartner = gm.Collection.PartnerUid == uid;
            _partnerButton.interactable = !isPartner;
            _partnerLabel.text = isPartner ? "Current Partner" : "Set as Partner";
            _portrait?.Show(species, record);
            if (_portrait != null) _portraitImage.texture = _portrait.Texture;
        }

        private void SetStat(string key, float value, float max)
        {
            _statFills[key].fillAmount = Mathf.Clamp01(value / max);
            _statValues[key].text = Mathf.RoundToInt(value).ToString();
        }

        private void OnSetPartner()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.Collection == null || string.IsNullOrEmpty(_selectedUid)) return;
            gm.Collection.SetPartner(_selectedUid);
            RebuildList();
        }

        private void OnDestroy()
        {
            if (_portrait != null) _portrait.Dispose();
        }
    }

    /// <summary>
    /// Renders a slowly turning creature model into a texture for the Bestiary, using a hidden stage on
    /// its own layer so it never appears in the main camera.
    /// </summary>
    public class CreaturePortrait
    {
        private GameObject _stage;
        private Camera _camera;
        private Transform _turntable;
        private GameObject _model;
        private string _shownUid;
        public RenderTexture Texture { get; private set; }

        public static CreaturePortrait Create()
        {
            var p = new CreaturePortrait();
            p._stage = new GameObject("BestiaryPortraitStage");
            p._stage.layer = Layers.Preview;
            p._stage.transform.position = new Vector3(0f, -600f, 0f);

            p.Texture = new RenderTexture(512, 512, 24) { name = "BestiaryPortrait", antiAliasing = 4 };

            var camGo = new GameObject("PortraitCamera");
            camGo.layer = Layers.Preview;
            camGo.transform.SetParent(p._stage.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.9f, 2.6f);
            camGo.transform.localRotation = Quaternion.Euler(10f, 180f, 0f);
            p._camera = camGo.AddComponent<Camera>();
            p._camera.cullingMask = Layers.PreviewMask;
            p._camera.clearFlags = CameraClearFlags.SolidColor;
            p._camera.backgroundColor = new Color(0.1f, 0.16f, 0.24f, 1f);
            p._camera.fieldOfView = 32f;
            p._camera.nearClipPlane = 0.1f;
            p._camera.farClipPlane = 20f;
            p._camera.targetTexture = p.Texture;
            p._camera.enabled = false;

            var key = new GameObject("PortraitKeyLight");
            key.layer = Layers.Preview;
            key.transform.SetParent(p._stage.transform, false);
            key.transform.localPosition = new Vector3(1.5f, 2.5f, 2.5f);
            var light = key.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = 2.2f;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.cullingMask = Layers.PreviewMask;
            light.shadows = LightShadows.None;

            var rim = new GameObject("PortraitRimLight");
            rim.layer = Layers.Preview;
            rim.transform.SetParent(p._stage.transform, false);
            rim.transform.localPosition = new Vector3(-1.8f, 1.6f, -1.5f);
            var rimLight = rim.AddComponent<Light>();
            rimLight.type = LightType.Point;
            rimLight.range = 8f;
            rimLight.intensity = 1.6f;
            rimLight.color = new Color(0.5f, 0.9f, 1f);
            rimLight.cullingMask = Layers.PreviewMask;
            rimLight.shadows = LightShadows.None;

            p._turntable = new GameObject("Turntable").transform;
            p._turntable.gameObject.layer = Layers.Preview;
            p._turntable.SetParent(p._stage.transform, false);
            p._turntable.gameObject.AddComponent<PortraitSpin>();
            p.SetActive(true);
            return p;
        }

        public void SetActive(bool active)
        {
            if (_camera != null) _camera.enabled = active;
        }

        public void Show(CreatureSpecies species, CreatureRecord record)
        {
            if (record != null && record.uid == _shownUid && _model != null) return;
            if (_model != null) Object.Destroy(_model);
            _model = null;
            _shownUid = record != null ? record.uid : null;
            if (species == null || record == null) return;

            var holder = new GameObject("Model");
            holder.layer = Layers.Preview;
            holder.transform.SetParent(_turntable, false);
            // The large guardian is shrunk so it fits the same framing as a Pebbloom.
            if (species.model == CreatureModelKind.Thalorix) holder.transform.localScale = Vector3.one * 0.55f;
            var anim = CreatureVisuals.Attach(holder, species, record.uid);
            anim.UseUnscaledTime = true; // the game is paused while browsing
            Layers.SetLayerRecursively(holder, Layers.Preview);
            _model = holder;
        }

        public void Dispose()
        {
            if (_stage != null) Object.Destroy(_stage);
            if (Texture != null)
            {
                Texture.Release();
                Object.Destroy(Texture);
            }
        }
    }

    /// <summary>Rotates the Bestiary turntable using unscaled time (the game is paused while browsing).</summary>
    public class PortraitSpin : MonoBehaviour
    {
        private void Update() => transform.Rotate(Vector3.up, 30f * Time.unscaledDeltaTime, Space.World);
    }
}
