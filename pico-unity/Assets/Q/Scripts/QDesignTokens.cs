// ============================================================
// QDesignTokens.cs — 来自 q-pico-ui.json 的视觉 Token
// ============================================================

using UnityEngine;

namespace Q.Pico
{
    public static class QDesign
    {
        // Colors from meta.theme
        public static readonly Color Bg = Hex("#0A0E13");
        public static readonly Color Panel = HexA("#10161E", 0.80f);
        public static readonly Color PanelSolid = Hex("#10161E");
        public static readonly Color Line = HexA("#FFFFFF", 0.09f);
        public static readonly Color LineStrong = HexA("#FFFFFF", 0.16f);
        public static readonly Color Txt = Hex("#EDF3F8");
        public static readonly Color Txt2 = Hex("#93A1B0");
        public static readonly Color Txt3 = Hex("#5B6875");
        public static readonly Color Cyan = Hex("#3AE3D2");
        public static readonly Color CyanDim = HexA("#3AE3D2", 0.14f);
        public static readonly Color Amber = Hex("#FFB454");
        public static readonly Color AmberDim = HexA("#FFB454", 0.14f);
        public static readonly Color Violet = Hex("#9D8CFF");
        public static readonly Color VioletDim = HexA("#9D8CFF", 0.14f);
        public static readonly Color PrimaryOn = Hex("#05201D");
        public static readonly Color Danger = new Color(0.86f, 0.28f, 0.28f, 0.95f);
        public static readonly Color CardBg = HexA("#FFFFFF", 0.028f);
        public static readonly Color CardBorder = HexA("#FFFFFF", 0.09f);

        public const float RadiusPanel = 15f;
        public const float RadiusCard = 15f;
        public const float RadiusPill = 15f;
        public const float RadiusBtn = 15f;
        public const float SafePad = 24f;
        public const float DesignW = 372f;
        public const float DesignH = 768f;

        public static Color Accent(string name)
        {
            if (string.IsNullOrEmpty(name)) return Cyan;
            switch (name.ToLowerInvariant())
            {
                case "amber": return Amber;
                case "violet": return Violet;
                case "cyan": return Cyan;
                default: return Cyan;
            }
        }

        public static Color AccentDim(string name)
        {
            if (string.IsNullOrEmpty(name)) return CyanDim;
            switch (name.ToLowerInvariant())
            {
                case "amber": return AmberDim;
                case "violet": return VioletDim;
                case "cyan": return CyanDim;
                default: return CyanDim;
            }
        }

        public static Color Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return Color.white;
        }

        public static Color HexA(string hex, float a)
        {
            var c = Hex(hex);
            c.a = a;
            return c;
        }

        public static Sprite Round(float radiusDesignPx)
        {
            // 用较高分辨率 9-slice，radius 映射到 48 基准
            return RoundedSpriteCache.Get(Mathf.Clamp(Mathf.RoundToInt(radiusDesignPx * 2f), 16, 64));
        }
    }
}
