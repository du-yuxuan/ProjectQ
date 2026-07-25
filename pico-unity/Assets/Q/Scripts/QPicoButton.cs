// ============================================================
// QPicoButton.cs
// 对齐 PICO 空间设计 Button 规范：
// https://developer-cn.picoxr.com/document/spatial-design/button/
//
// 角色：Primary / Secondary / Passable / Other / Ghost / Danger
// 尺寸：Min / Small / Regular / Max
// 状态：Normal / Hover / Pressed / Disabled
// 全圆角 + PICO Sans 中文字体
// ============================================================

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Q.Pico
{
    public enum PicoButtonRole
    {
        Primary,    // 主要按钮：每区仅一个
        Secondary,  // 辅助按钮
        Passable,   // 可通行/开始玩（强调绿色）
        Other,      // 叠在内容上，带模糊感
        Ghost,      // 无边框
        Danger      // 危险操作
    }

    public enum PicoButtonSize
    {
        Min,
        Small,
        Regular,
        Max
    }

    public class QPicoButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Button button;
        public Image background;
        public Component label;
        public PicoButtonRole role = PicoButtonRole.Primary;
        public PicoButtonSize size = PicoButtonSize.Regular;
        public bool interactable = true;

        bool _hover;
        bool _pressed;

        // 规范色：优先 q-pico-ui.json 主题
        static readonly Color PrimaryNormal = QDesign.Cyan;
        static readonly Color PrimaryHover = new Color(0.35f, 0.92f, 0.86f, 1f);
        static readonly Color PrimaryPressed = new Color(0.18f, 0.72f, 0.66f, 1f);

        static readonly Color SecondaryNormal = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color SecondaryHover = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color SecondaryPressed = new Color(1f, 1f, 1f, 0.24f);

        // Passable = design primary cyan
        static readonly Color PassableNormal = QDesign.Cyan;
        static readonly Color PassableHover = new Color(0.35f, 0.92f, 0.86f, 1f);
        static readonly Color PassablePressed = new Color(0.18f, 0.72f, 0.66f, 1f);

        static readonly Color OtherNormal = new Color(0.08f, 0.09f, 0.12f, 0.55f);
        static readonly Color OtherHover = new Color(0.12f, 0.14f, 0.18f, 0.70f);
        static readonly Color OtherPressed = new Color(0.06f, 0.07f, 0.10f, 0.80f);

        static readonly Color GhostNormal = new Color(1f, 1f, 1f, 0.00f);
        static readonly Color GhostHover = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color GhostPressed = new Color(1f, 1f, 1f, 0.16f);

        static readonly Color DangerNormal = QDesign.Danger;
        static readonly Color DangerHover = new Color(0.95f, 0.35f, 0.35f, 1.00f);
        static readonly Color DangerPressed = new Color(0.72f, 0.18f, 0.18f, 1.00f);

        static readonly Color DisabledBg = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color TextOnDark = Color.white;
        static readonly Color TextOnPrimary = QDesign.PrimaryOn;
        static readonly Color TextMuted = new Color(1f, 1f, 1f, 0.45f);

        public static QPicoButton Create(
            Transform parent,
            string name,
            string text,
            PicoButtonRole role = PicoButtonRole.Primary,
            PicoButtonSize size = PicoButtonSize.Regular,
            UnityEngine.Events.UnityAction onClick = null)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);

            var img = root.AddComponent<Image>();
            img.raycastTarget = true;

            var btn = root.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None; // 自己管状态色

            // 全圆角：用简单圆角 sprite 若无则纯色块 + 高高度近似胶囊
            // 这里用运行时生成的圆角 sprite
            img.sprite = QDesign.Round(QDesign.RadiusBtn);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;

            var label = QUIFactory.CreateText(
                root.transform, "Label", text,
                SizeToFont(size),
                size >= PicoButtonSize.Regular ? PicoFontWeight.Medium : PicoFontWeight.Regular,
                TextAnchor.MiddleCenter, TMPro.TextAlignmentOptions.Center);
            var lrt = label.transform as RectTransform;
            if (lrt != null)
            {
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(12, 4);
                lrt.offsetMax = new Vector2(-12, -4);
            }

            var comp = root.AddComponent<QPicoButton>();
            comp.button = btn;
            comp.background = img;
            comp.label = label;
            comp.role = role;
            comp.size = size;
            comp.ApplySize();
            comp.ApplyVisual();

            if (onClick != null)
                btn.onClick.AddListener(onClick);

            return comp;
        }

        public void SetText(string text) => QUIFactory.SetText(label, text);

        public void SetInteractable(bool value)
        {
            interactable = value;
            if (button != null) button.interactable = value;
            ApplyVisual();
        }

        public void SetRole(PicoButtonRole r)
        {
            role = r;
            ApplyVisual();
        }

        void ApplySize()
        {
            var rt = transform as RectTransform;
            if (rt == null) return;
            // 规范四档高度 + 最小宽度（全圆角胶囊）
            float h, minW, padX;
            switch (size)
            {
                case PicoButtonSize.Min:
                    h = 36f; minW = 72f; padX = 14f; break;
                case PicoButtonSize.Small:
                    h = 44f; minW = 96f; padX = 18f; break;
                case PicoButtonSize.Max:
                    h = 72f; minW = 180f; padX = 28f; break;
                default:
                    h = 56f; minW = 128f; padX = 22f; break;
            }
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
            // 不强制宽度，由 LayoutElement 保底
            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            le.minHeight = h;
            le.preferredHeight = h;
            le.minWidth = minW;
            // 圆角半径 = 高度/2 → 全圆角
            if (background != null)
                background.pixelsPerUnitMultiplier = 64f / h;
        }

        static float SizeToFont(PicoButtonSize size)
        {
            switch (size)
            {
                case PicoButtonSize.Min: return 15f;
                case PicoButtonSize.Small: return 17f;
                case PicoButtonSize.Max: return 24f;
                default: return 20f;
            }
        }

        public void ApplyVisual()
        {
            if (background == null) return;
            Color bg, fg;
            if (!interactable)
            {
                bg = DisabledBg;
                fg = TextMuted;
            }
            else
            {
                GetRoleColors(out var n, out var h, out var p);
                if (_pressed) bg = p;
                else if (_hover) bg = h;
                else bg = n;
                // primary / passable 用深色字
                fg = (role == PicoButtonRole.Primary || role == PicoButtonRole.Passable)
                    ? TextOnPrimary
                    : TextOnDark;
            }
            background.color = bg;
            QUIFactory.SetColor(label, fg);
        }

        void GetRoleColors(out Color normal, out Color hover, out Color pressed)
        {
            switch (role)
            {
                case PicoButtonRole.Secondary:
                    normal = SecondaryNormal; hover = SecondaryHover; pressed = SecondaryPressed; break;
                case PicoButtonRole.Passable:
                    normal = PassableNormal; hover = PassableHover; pressed = PassablePressed; break;
                case PicoButtonRole.Other:
                    normal = OtherNormal; hover = OtherHover; pressed = OtherPressed; break;
                case PicoButtonRole.Ghost:
                    normal = GhostNormal; hover = GhostHover; pressed = GhostPressed; break;
                case PicoButtonRole.Danger:
                    normal = DangerNormal; hover = DangerHover; pressed = DangerPressed; break;
                default:
                    normal = PrimaryNormal; hover = PrimaryHover; pressed = PrimaryPressed; break;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true; ApplyVisual();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false; _pressed = false; ApplyVisual();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!interactable) return;
            _pressed = true; ApplyVisual();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false; ApplyVisual();
        }
    }

    /// <summary>运行时生成 9-slice 圆角 sprite，用于全圆角按钮。</summary>
    public static class RoundedSpriteCache
    {
        static Sprite _sprite;

        public static Sprite Get(int radiusPx = 32)
        {
            if (_sprite != null) return _sprite;
            int size = radiusPx * 2 + 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float r = radiusPx;
            float cx = size / 2f;
            float cy = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - cx) - (size / 2f - r), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - cy) - (size / 2f - r), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d + 0.5f);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply(false, true);
            // 9-slice border = radius
            _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return _sprite;
        }
    }
}
