using UnityEngine;

namespace Beastbound.World
{
    /// <summary>
    /// A tiny colour-atlas texture. Batched props set their UVs to one cell so trees, rocks and flowers of
    /// many colours share a single material (= very few draw calls).
    /// </summary>
    public static class WorldPalette
    {
        public enum Swatch
        {
            Trunk, TrunkDark, Pine, PineDark, Leaf, LeafLight, LeafAutumn, Blossom, BlossomLight,
            Rock, RockDark, RockMoss, Stone, StoneMoss, FlowerYellow, FlowerViolet, FlowerWhite, FlowerRed,
            FlowerBlue, Stem, MushroomCap, MushroomStem, FarMountain, Reed, Sand
        }

        private const int Cells = 8; // 8x8 grid
        private static Texture2D _texture;
        private static Texture2D _grass;

        public static Color ColorOf(Swatch s)
        {
            switch (s)
            {
                case Swatch.Trunk: return new Color(0.42f, 0.29f, 0.19f);
                case Swatch.TrunkDark: return new Color(0.3f, 0.21f, 0.15f);
                case Swatch.Pine: return new Color(0.16f, 0.4f, 0.24f);
                case Swatch.PineDark: return new Color(0.11f, 0.3f, 0.2f);
                case Swatch.Leaf: return new Color(0.3f, 0.56f, 0.2f);
                case Swatch.LeafLight: return new Color(0.5f, 0.7f, 0.25f);
                case Swatch.LeafAutumn: return new Color(0.88f, 0.6f, 0.2f);
                case Swatch.Blossom: return new Color(0.98f, 0.62f, 0.74f);
                case Swatch.BlossomLight: return new Color(1f, 0.82f, 0.88f);
                case Swatch.Rock: return new Color(0.55f, 0.55f, 0.57f);
                case Swatch.RockDark: return new Color(0.4f, 0.4f, 0.44f);
                case Swatch.RockMoss: return new Color(0.42f, 0.52f, 0.33f);
                case Swatch.Stone: return new Color(0.7f, 0.66f, 0.58f);
                case Swatch.StoneMoss: return new Color(0.45f, 0.58f, 0.32f);
                case Swatch.FlowerYellow: return new Color(1f, 0.85f, 0.25f);
                case Swatch.FlowerViolet: return new Color(0.62f, 0.45f, 0.95f);
                case Swatch.FlowerWhite: return new Color(0.97f, 0.96f, 0.92f);
                case Swatch.FlowerRed: return new Color(0.95f, 0.32f, 0.3f);
                case Swatch.FlowerBlue: return new Color(0.35f, 0.62f, 1f);
                case Swatch.Stem: return new Color(0.3f, 0.5f, 0.2f);
                case Swatch.MushroomCap: return new Color(0.86f, 0.25f, 0.2f);
                case Swatch.MushroomStem: return new Color(0.95f, 0.9f, 0.8f);
                case Swatch.FarMountain: return new Color(0.46f, 0.53f, 0.64f);
                case Swatch.Reed: return new Color(0.55f, 0.6f, 0.3f);
                case Swatch.Sand: return new Color(0.86f, 0.79f, 0.6f);
                default: return Color.magenta;
            }
        }

        /// <summary>UV coordinate at the centre of a swatch's cell.</summary>
        public static Vector2 UV(Swatch s)
        {
            int i = (int)s;
            int cx = i % Cells;
            int cy = i / Cells;
            return new Vector2((cx + 0.5f) / Cells, (cy + 0.5f) / Cells);
        }

        public static Texture2D Texture
        {
            get
            {
                if (_texture != null) return _texture;
                const int cellPx = 4;
                int size = Cells * cellPx;
                _texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "WorldPalette",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                var pixels = new Color32[size * size];
                int swatchCount = System.Enum.GetValues(typeof(Swatch)).Length;
                for (int i = 0; i < Cells * Cells; i++)
                {
                    Color c = i < swatchCount ? ColorOf((Swatch)i) : Color.gray;
                    int cx = i % Cells, cy = i / Cells;
                    for (int y = 0; y < cellPx; y++)
                    for (int x = 0; x < cellPx; x++)
                        pixels[(cy * cellPx + y) * size + cx * cellPx + x] = c;
                }
                _texture.SetPixels32(pixels);
                _texture.Apply(false, true);
                return _texture;
            }
        }

        /// <summary>
        /// Grass gradient: 8 columns of colour variants, dark at the root (v=0) to sunlit at the tip (v=1).
        /// </summary>
        public static Texture2D GrassTexture
        {
            get
            {
                if (_grass != null) return _grass;
                const int w = 8, h = 32;
                _grass = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    name = "GrassGradient",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                Color[] roots =
                {
                    new Color(0.13f, 0.3f, 0.1f), new Color(0.16f, 0.33f, 0.1f), new Color(0.2f, 0.35f, 0.12f),
                    new Color(0.15f, 0.28f, 0.12f), new Color(0.22f, 0.36f, 0.1f), new Color(0.12f, 0.27f, 0.12f),
                    new Color(0.25f, 0.38f, 0.12f), new Color(0.18f, 0.31f, 0.12f)
                };
                Color[] tips =
                {
                    new Color(0.48f, 0.74f, 0.28f), new Color(0.6f, 0.78f, 0.3f), new Color(0.72f, 0.8f, 0.36f),
                    new Color(0.42f, 0.68f, 0.3f), new Color(0.78f, 0.78f, 0.4f), new Color(0.38f, 0.64f, 0.28f),
                    new Color(0.85f, 0.82f, 0.45f), new Color(0.55f, 0.75f, 0.32f)
                };
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float t = y / (float)(h - 1);
                    for (int x = 0; x < w; x++) pixels[y * w + x] = Color.Lerp(roots[x], tips[x], t * t * (3f - 2f * t));
                }
                _grass.SetPixels32(pixels);
                _grass.Apply(false, true);
                return _grass;
            }
        }

        public static Vector2 GrassUV(int variant, float t)
        {
            variant = Mathf.Abs(variant) % 8;
            return new Vector2((variant + 0.5f) / 8f, Mathf.Lerp(0.03f, 0.97f, Mathf.Clamp01(t)));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _texture = null;
            _grass = null;
        }
    }
}
