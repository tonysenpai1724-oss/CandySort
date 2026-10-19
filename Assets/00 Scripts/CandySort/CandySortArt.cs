using System;
using System.Collections.Generic;
using UnityEngine;

namespace CandySort
{
    // Replace the generated sprites here when the final candy artwork is ready.
    public static class CandySortArt
    {
        public static readonly Color Ink = Hex("F0FAFF");
        public static readonly Color[] Colors = {
            Hex("F16C91"), Hex("F6B64D"), Hex("9B83DF"),
            Hex("55C9B5"), Hex("65AFE6"), Hex("EF8F60")
        };
        public static readonly string[] Names = { "Dâu", "Chanh", "Nho", "Bạc hà", "Việt quất", "Cam" };
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        private static Sprite[] customSprites;

        public static void SetCandySprites(Sprite[] sprites) => customSprites = sprites;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color value);
            return value;
        }

        public static Sprite Rounded => Get("rounded", (x, y) =>
        {
            float d = new Vector2(Mathf.Max(Mathf.Abs(x) - .64f, 0), Mathf.Max(Mathf.Abs(y) - .64f, 0)).magnitude;
            return new Color(1, 1, 1, Mathf.Clamp01((.35f - d) * 80));
        }, true);
        public static Sprite Circle => Get("circle", (x, y) => new Color(1, 1, 1, Mathf.Clamp01((.98f - Mathf.Sqrt(x * x + y * y)) * 80)));
        public static Sprite Ocean => Get("ocean", (x, y) =>
        {
            float depth = Mathf.Clamp01(y * .5f + .5f);
            Color color = Color.Lerp(Hex("082743"), Hex("146A89"), depth);
            float ray = Mathf.Pow(Mathf.Max(0, Mathf.Sin(x * 9 + y * 2)), 16) * depth * .06f;
            return Color.Lerp(color, Hex("6ED8E7"), ray);
        });
        public static Sprite Bubble => Get("bubble", (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float edge = Mathf.Clamp01((.995f - r) * 100);
            float rim = Mathf.Exp(-Mathf.Pow((r - .955f) * 70, 2));
            float innerRim = Mathf.Exp(-Mathf.Pow((r - .91f) * 45, 2));
            float shine = Mathf.Exp(-Mathf.Pow((r - .89f) * 20, 2)) * Mathf.Clamp01((y - x - .3f) * 2);
            Color glass = Color.Lerp(Hex("5BC8F3"), Hex("F1FEFF"), Mathf.Clamp01(.3f + y * .4f + shine));
            glass.a = edge * Mathf.Clamp01(.025f + Mathf.Pow(r, 8) * .08f + rim * .9f + innerRim * .2f + shine * .58f);
            return glass;
        });
        public static Sprite Ring => Get("ring", (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return new Color(1, 1, 1, Mathf.Clamp01((.98f - r) * 80) * Mathf.Clamp01((r - .81f) * 80));
        });

        public static Sprite Candy(CandyKind kind)
        {
            int k = (int)kind;
            if (customSprites != null && k < customSprites.Length && customSprites[k] != null) return customSprites[k];
            return Get("candy" + k, (x, y) =>
            {
                float d;
                switch (k)
                {
                    case 0: // Wrapped sweet.
                        d = Mathf.Min(new Vector2(x * 1.25f, y * 1.5f).magnitude - .78f,
                            Mathf.Max(Mathf.Abs(x) - .94f, Mathf.Abs(y) - (.52f * Mathf.Abs(x))));
                        break;
                    case 1: d = Mathf.Pow(Mathf.Abs(x * 1.24f), 4) + Mathf.Pow(Mathf.Abs(y * 1.24f), 4) - .82f; break;
                    case 2: d = Mathf.Sqrt(x * x + y * y) - .85f; break;
                    case 3: d = Mathf.Pow(Mathf.Abs(x * 1.2f), 4) + Mathf.Pow(Mathf.Abs(y * 1.6f), 4) - .8f; break;
                    case 4: d = Mathf.Abs(x) * .65f + Mathf.Abs(y) - .86f; break;
                    default: d = Mathf.Sqrt(x * x + y * y) - .81f; break;
                }
                float alpha = Mathf.Clamp01(-d * 55);
                Color baseColor = Colors[k];
                float shine = Mathf.Exp(-((x + .28f) * (x + .28f) + (y - .4f) * (y - .4f)) * 16) * .65f;
                float stripe = 0;
                if (k == 0 || k == 3) stripe = Mathf.Pow(Mathf.Max(0, Mathf.Sin((x + y) * 14)), 8) * .28f;
                if (k == 2) stripe = Mathf.Pow(Mathf.Max(0, Mathf.Sin(Mathf.Atan2(y, x) * 3 + Mathf.Sqrt(x * x + y * y) * 18)), 6) * .38f;
                if (k == 1 || k == 4) stripe = Mathf.Clamp01(1 - Mathf.Abs(x + y - .15f) * 12) * .28f;
                Color color = Color.Lerp(baseColor, Color.white, shine + stripe);
                color = Color.Lerp(color, baseColor * .62f, Mathf.Clamp01(-y - .36f) * .35f);
                color.a = alpha;
                return color;
            });
        }

        private static Sprite Get(string key, Func<float, float, Color> sample, bool sliced = false)
        {
            if (cache.TryGetValue(key, out Sprite sprite)) return sprite;
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "CandySort_" + key, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) pixels[y * size + x] = sample((x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1);
            texture.SetPixels(pixels);
            texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect,
                sliced ? new Vector4(26, 26, 26, 26) : Vector4.zero);
            cache.Add(key, sprite);
            return sprite;
        }
    }
}
