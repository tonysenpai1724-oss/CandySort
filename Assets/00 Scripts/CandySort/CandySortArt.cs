using System;
using System.Collections.Generic;
using UnityEngine;

namespace CandySort
{
    // Imported sprites and generated fallbacks share the same sheet-ordered catalog.
    public static class CandySortArt
    {
        public static readonly Color Ink = Hex("F0FAFF");
        public static readonly Color[] Colors = {
            Hex("F16C91"), Hex("9B63CF"), Hex("F6D64D"), Hex("E94D62"),
            Hex("ED6B74"), Hex("55C9B5"), Hex("EF8F40"), Hex("65AFE6"),
            Hex("754637"), Hex("FFF1DF"), Hex("DA82E8"), Hex("8DCB61"),
            Hex("946043"), Hex("B3DE45"), Hex("F29EC5"), Hex("F081AD"),
            Hex("EFD9A1"), Hex("EDB0D5"), Hex("D7A148"), Hex("45BE83"),
            Hex("B97A4C"), Hex("F35C50"), Hex("F078AE"), Hex("514055"),
            Hex("EFA466"), Hex("EFB476"), Hex("633C34"), Hex("F08557"),
            Hex("EF5260"), Hex("CE7ACB"), Hex("89D6D8"), Hex("FFE561")
        };
        public static readonly string[] Ids = {
            "swirl_lollipop", "purple_jellybean", "lemon_candy", "gummy_bear_red",
            "candy_cane", "peppermint_mint", "orange_wrapped_candy", "blueberry_candy",
            "chocolate_bar", "marshmallow", "rainbow_lollipop", "green_jellybean",
            "chocolate_tart", "gummy_worm", "rock_candy_stick", "strawberry_wrapped_candy",
            "sprinkle_candy_ball", "cotton_candy", "caramel_cube", "gummy_bear_green",
            "caramel_toffee", "peppermint_swirl", "pink_candy_ball", "black_licorice_twist",
            "orange_candy_blue_gum", "striped_lollipop", "chocolate_fudge", "gummy_fish",
            "heart_candy", "red_candy_ball", "wafer_bar", "lemon_wedge"
        };
        public static readonly string[] Names = {
            "Kẹo mút xoắn ốc", "Kẹo đậu thạch tím", "Kẹo chanh", "Kẹo dẻo gấu đỏ",
            "Kẹo gậy sọc đỏ trắng", "Kẹo bạc hà xoắn", "Kẹo cam gói giấy", "Kẹo viên xanh",
            "Thanh sô-cô-la", "Kẹo dẻo trắng", "Kẹo mút cầu vồng", "Kẹo đậu thạch xanh",
            "Bánh tart sô-cô-la", "Kẹo dẻo hình giun", "Kẹo đường kết tinh", "Kẹo dâu gói giấy",
            "Kẹo viên nhiều màu", "Kẹo bông gòn", "Kẹo caramel vuông", "Kẹo dẻo gấu xanh",
            "Kẹo caramel dạng thỏi", "Kẹo bạc hà xoáy", "Kẹo viên hồng", "Kẹo cam thảo xoắn đen",
            "Kẹo cam kèm viên xanh", "Kẹo mút sọc", "Kẹo sô-cô-la mềm", "Kẹo dẻo hình cá",
            "Kẹo hình trái tim", "Kẹo viên đỏ", "Bánh xốp wafer", "Kẹo chanh"
        };
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        private static Sprite[] customSprites;
        private static readonly string[] interfaceKeys = { "rounded", "circle", "ocean", "bubble", "ring" };
        private static Sprite[] interfaceSprites;

        public static void SetCandySprites(Sprite[] sprites) => customSprites = sprites;
        public static void SetInterfaceSprites(Sprite[] sprites) => interfaceSprites = sprites;

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
            if (k < 0 || k >= CandySortModel.CandyKindCount) throw new ArgumentOutOfRangeException(nameof(kind));
            if (customSprites != null && k < customSprites.Length && customSprites[k] != null) return customSprites[k];
            return Get("candy" + k, (x, y) =>
            {
                float radius = Mathf.Sqrt(x * x + y * y);
                float d;
                switch (kind)
                {
                    case CandyKind.SwirlLollipop:
                    case CandyKind.RainbowLollipop:
                    case CandyKind.StripedLollipop:
                        d = Mathf.Min(new Vector2(x, y - .24f).magnitude - .59f,
                            Mathf.Max(Mathf.Abs(x) - .065f, Mathf.Abs(y + .37f) - .55f));
                        break;
                    case CandyKind.PurpleJellybean:
                    case CandyKind.GreenJellybean:
                        d = new Vector2(x * 1.08f, (y + Mathf.Sin(x * 3) * .12f) * 1.8f).magnitude - .81f;
                        break;
                    case CandyKind.GummyBearRed:
                    case CandyKind.GummyBearGreen:
                        d = Mathf.Min(new Vector2(x * 1.5f, y + .17f).magnitude - .77f,
                            new Vector2(x, y - .4f).magnitude - .42f);
                        d = Mathf.Min(d, new Vector2(Mathf.Abs(x) - .34f, y - .64f).magnitude - .19f);
                        break;
                    case CandyKind.CandyCane:
                        d = Mathf.Min(Mathf.Max(Mathf.Abs(x + .24f) - .13f, Mathf.Abs(y + .2f) - .68f),
                            Mathf.Max(Mathf.Abs(new Vector2(x - .02f, y - .43f).magnitude - .27f) - .13f, .43f - y));
                        break;
                    case CandyKind.OrangeWrappedCandy:
                    case CandyKind.StrawberryWrappedCandy:
                    case CandyKind.OrangeCandyBlueGum:
                        d = Mathf.Min(new Vector2(x * 1.25f, y * 1.5f).magnitude - .78f,
                            Mathf.Max(Mathf.Abs(x) - .94f, Mathf.Abs(y) - (.52f * Mathf.Abs(x))));
                        break;
                    case CandyKind.LemonCandy:
                        d = Mathf.Abs(x) * .65f + Mathf.Abs(y) - .86f;
                        break;
                    case CandyKind.ChocolateBar:
                    case CandyKind.CaramelToffee:
                    case CandyKind.WaferBar:
                        d = Mathf.Max(Mathf.Abs(x) - .84f, Mathf.Abs(y) - .46f);
                        break;
                    case CandyKind.Marshmallow:
                    case CandyKind.CaramelCube:
                    case CandyKind.ChocolateFudge:
                        d = Mathf.Pow(Mathf.Abs(x * 1.24f), 4) + Mathf.Pow(Mathf.Abs(y * 1.24f), 4) - .82f;
                        break;
                    case CandyKind.GummyWorm:
                    case CandyKind.BlackLicoriceTwist:
                        d = Mathf.Max(Mathf.Abs(y - Mathf.Sin(x * 5) * .24f) - .17f, Mathf.Abs(x) - .86f);
                        break;
                    case CandyKind.RockCandyStick:
                        d = Mathf.Max(Mathf.Abs(x) - .13f - .06f * Mathf.Cos(y * 25), Mathf.Abs(y) - .88f);
                        break;
                    case CandyKind.CottonCandy:
                    case CandyKind.RedCandyBall:
                        d = radius - (.72f + .1f * Mathf.Cos(Mathf.Atan2(y, x) * 7));
                        break;
                    case CandyKind.GummyFish:
                        d = Mathf.Min(new Vector2((x + .16f) * 1.4f, y * 2).magnitude - .78f,
                            Mathf.Max(Mathf.Abs(x - .58f) - .3f, Mathf.Abs(y) - (x - .3f) * .9f));
                        break;
                    case CandyKind.HeartCandy:
                        d = Mathf.Min(new Vector2(Mathf.Abs(x) - .32f, y - .25f).magnitude - .43f,
                            Mathf.Max(Mathf.Abs(x) + Mathf.Abs(y + .12f) - .82f, y - .25f));
                        break;
                    case CandyKind.LemonWedge:
                        d = Mathf.Max(radius - .88f, y - .12f);
                        break;
                    default:
                        d = radius - .81f;
                        break;
                }
                float alpha = Mathf.Clamp01(-d * 55);
                Color baseColor = Colors[k];
                float angle = Mathf.Atan2(y, x);
                if (kind == CandyKind.RainbowLollipop || kind == CandyKind.GummyWorm || kind == CandyKind.SprinkleCandyBall)
                    baseColor = Color.Lerp(baseColor, Color.HSVToRGB(Mathf.Repeat(x * .55f + y * .3f + .5f, 1), .65f, 1), .8f);
                float shine = Mathf.Exp(-((x + .28f) * (x + .28f) + (y - .4f) * (y - .4f)) * 16) * .65f;
                float stripe = Mathf.Pow(Mathf.Max(0, Mathf.Sin((x + y) * (10 + k % 5))), 8) * .25f;
                if (kind == CandyKind.SwirlLollipop || kind == CandyKind.PeppermintMint || kind == CandyKind.PeppermintSwirl)
                    stripe = Mathf.Pow(Mathf.Max(0, Mathf.Sin(angle * 3 + radius * 18)), 6) * .65f;
                if (kind == CandyKind.ChocolateBar || kind == CandyKind.CaramelCube || kind == CandyKind.WaferBar)
                    stripe = Mathf.Pow(Mathf.Max(0, Mathf.Cos(x * 14) * Mathf.Cos(y * 14)), 8) * .3f;
                Color color = Color.Lerp(baseColor, Color.white, shine + stripe);
                color = Color.Lerp(color, baseColor * .62f, Mathf.Clamp01(-y - .36f) * .35f);
                color.a = alpha;
                return color;
            });
        }

        private static Sprite Get(string key, Func<float, float, Color> sample, bool sliced = false)
        {
            int index = Array.IndexOf(interfaceKeys, key);
            if (interfaceSprites != null && index >= 0 && index < interfaceSprites.Length && interfaceSprites[index] != null)
                return interfaceSprites[index];
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
