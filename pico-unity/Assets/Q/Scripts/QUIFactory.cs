// ============================================================
// QUIFactory.cs
// 按 PICO 空间设计字体规范使用系统内置 PICO Sans 字体族
// 文档: https://developer-cn.picoxr.com/document/spatial-design/typeface/
//
// PICO OS 6 内置 6 款字体（设备 /system/fonts）：
//   PICO Sans          西文/多语言（PICOSans.ttf）
//   PICO Sans SC       简体中文（PICOSansSC-{Thin,Light,Regular,Medium,Bold,Heavy}.ttf）
//   PICO Sans VFE SC   可变简体（PICOSansVFSC.ttf）
//   PICO Sans VF TC/JP/KR 繁/日/韩可变
//
// 授权：仅限 PICO 平台应用内使用。
// ============================================================

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Q.Pico
{
    /// <summary>PICO 空间设计字重（对齐官方 Thin~Heavy）。</summary>
    public enum PicoFontWeight
    {
        Thin = 100,
        Light = 300,
        Regular = 400,
        Medium = 500,
        Bold = 700,
        Heavy = 900
    }

    /// <summary>
    /// PICO 空间设计文字角色（对齐官方 Display / Headline / Title / Body / Label 层级）。
    /// 字号为空间 UI 常用参考值（可按面板缩放再调）。
    /// </summary>
    public enum PicoTypeRole
    {
        Display,   // 超大标题
        Headline,  // 主标题
        Title,     // 区块标题
        Body,      // 正文
        Label,     // 标签/辅助
        Caption    // 更小说明
    }

    public static class QUIFactory
    {
        // 官方字重文件名（/system/fonts）
        const string PicoSansLatin = "PICOSans";
        static readonly string[] PicoSansScByWeight =
        {
            "PICOSansSC-Thin",
            "PICOSansSC-Light",
            "PICOSansSC-Regular",
            "PICOSansSC-Medium",
            "PICOSansSC-Bold",
            "PICOSansSC-Heavy"
        };

        static readonly string[] AndroidFontPaths =
        {
            "/system/fonts/PICOSansSC-Regular.ttf",
            "/system/fonts/PICOSansSC-Medium.ttf",
            "/system/fonts/PICOSansSC-Bold.ttf",
            "/system/fonts/PICOSansSC-Light.ttf",
            "/system/fonts/PICOSansSC-Thin.ttf",
            "/system/fonts/PICOSansSC-Heavy.ttf",
            "/system/fonts/PICOSansVFSC.ttf",
            "/system/fonts/PICOSans.ttf",
            "/system/fonts/NotoSansCJK-Regular.ttc",
            "/system/fonts/DroidSansFallback.ttf"
        };

        static bool _ready;
        static Font _regular;
        static Font _medium;
        static Font _bold;
        static Font _light;
        static Font _thin;
        static Font _heavy;
        static Font _fallback;
        static TMP_FontAsset _tmpFont;
        static bool _preferTmp;
        static HashSet<Font> _charsRequestedFor = new HashSet<Font>();
        static bool _tmpAtlasValidated;

        public static Font CjkFont
        {
            get { EnsureFonts(); return _regular ?? _fallback; }
        }

        public static Font LegacyFont => CjkFont;

        public static bool IsTmpAvailable
        {
            get { EnsureFonts(); return _preferTmp; }
        }

        /// <summary>按角色取推荐字号（pt/UI 单位）。</summary>
        public static float SizeOf(PicoTypeRole role)
        {
            switch (role)
            {
                case PicoTypeRole.Display: return 48f;
                case PicoTypeRole.Headline: return 34f;
                case PicoTypeRole.Title: return 26f;
                case PicoTypeRole.Body: return 22f;
                case PicoTypeRole.Label: return 18f;
                case PicoTypeRole.Caption: return 15f;
                default: return 22f;
            }
        }

        public static PicoFontWeight WeightOf(PicoTypeRole role)
        {
            switch (role)
            {
                case PicoTypeRole.Display:
                case PicoTypeRole.Headline: return PicoFontWeight.Bold;
                case PicoTypeRole.Title: return PicoFontWeight.Medium;
                case PicoTypeRole.Body: return PicoFontWeight.Regular;
                case PicoTypeRole.Label: return PicoFontWeight.Medium;
                case PicoTypeRole.Caption: return PicoFontWeight.Regular;
                default: return PicoFontWeight.Regular;
            }
        }

        public static Font FontOf(PicoFontWeight weight)
        {
            EnsureFonts();
            switch (weight)
            {
                case PicoFontWeight.Thin: return _thin ?? _light ?? _regular ?? _fallback;
                case PicoFontWeight.Light: return _light ?? _regular ?? _fallback;
                case PicoFontWeight.Medium: return _medium ?? _regular ?? _fallback;
                case PicoFontWeight.Bold: return _bold ?? _medium ?? _regular ?? _fallback;
                case PicoFontWeight.Heavy: return _heavy ?? _bold ?? _regular ?? _fallback;
                default: return _regular ?? _fallback;
            }
        }

        static void EnsureFonts()
        {
            if (_ready) return;
            _ready = true;

            // 1) 优先 PICO OS 内置字体（设备真机/模拟器）
            // Regular 优先 VFSC（官方 fonts.xml 中 zh-Hans weight=400）
            _regular = LoadPico("PICOSansVFSC", PicoFontWeight.Regular)
                       ?? LoadPico("PICOSansSC-Regular", PicoFontWeight.Regular);
            _thin = LoadPico("PICOSansSC-Thin", PicoFontWeight.Thin);
            _light = LoadPico("PICOSansSC-Light", PicoFontWeight.Light);
            _medium = LoadPico("PICOSansSC-Medium", PicoFontWeight.Medium);
            _bold = LoadPico("PICOSansSC-Bold", PicoFontWeight.Bold);
            _heavy = LoadPico("PICOSansSC-Heavy", PicoFontWeight.Heavy);

            // 西文补充
            var latin = LoadByOsName("PICOSans", "PICO Sans", "sans-serif");
            if (_regular == null) _regular = latin;

            // 3) StreamingAssets/fonts 用户字体
            if (_regular == null)
                _regular = LoadFromStreamingAssets();

            // 4) 其它系统 CJK
            if (_regular == null)
            {
                _regular = LoadByOsName(
                    "NotoSansCJK-Regular", "Noto Sans CJK SC", "Noto Sans CJK",
                    "DroidSansFallback", "PingFang SC", "Microsoft YaHei", "sans-serif");
            }

            _fallback = _regular
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf")
                ?? Font.CreateDynamicFontFromOSFont("sans-serif", 32);

            if (_regular == null) _regular = _fallback;
            if (_medium == null) _medium = _regular;
            if (_bold == null) _bold = _medium;
            if (_light == null) _light = _regular;
            if (_thin == null) _thin = _light;
            if (_heavy == null) _heavy = _bold;

            Debug.Log($"[QUI] PICO 字体 ready regular={Name(_regular)} medium={Name(_medium)} bold={Name(_bold)}");

            // TMP：仅当提供中文 SDF 且 TMP_Settings 资源已加载时启用
            // （AddComponent<TextMeshProUGUI> 会访问 TMP_Settings.defaultFontAsset，
            //   设备上若 TMP Settings 资源缺失则 NRE；必须先验证。）
            try
            {
                bool settingsOk = TMP_Settings.instance != null;
                if (settingsOk)
                {
                    _tmpFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/PICOSansSC SDF");
                    if (_tmpFont == null)
                        _tmpFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Chinese SDF");
                    if (_tmpFont == null)
                        _tmpFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansSC SDF");
                    _preferTmp = _tmpFont != null;
                }
                else
                {
                    _preferTmp = false;
                    Debug.LogWarning("[QUI] TMP_Settings.instance 为 null，禁用 TMP 路径，走 uGUI Text");
                }
            }
            catch (System.Exception e)
            {
                _preferTmp = false;
                Debug.LogWarning($"[QUI] TMP 检测异常，禁用 TMP 路径: {e.Message}");
            }

            if (_preferTmp && _tmpFont != null)
            {
                ValidateTmpFont();
            }
        }

        static string Name(Font f) => f != null ? f.name : "null";

        /// <summary>验证 TMP 字体图集能否渲染中文，设置动态模式并预热常用字。</summary>
        static void ValidateTmpFont()
        {
            if (_tmpAtlasValidated || _tmpFont == null) return;
            _tmpAtlasValidated = true;

            try
            {
                // 强制启用动态图集模式，确保运行时新汉字可实时加入图集
                _tmpFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;

                // 预热一批常用中文字符，避免首帧卡顿与白块
                string prewarm = "的一是了我不人在他有这个上们来到时大地为子中你说生国年着就那和要她出也得里后自以会家可下过天去能对小多然于心学么之都好看起发当没成只如事把还用第样道想作种开美总从无情己面女但现前些所同日手又行意动方期它头经长儿回位分爱老因很给名法间斯知世什两次使身者被高已亲其进此话常与活正感见明问力理尔点文几定本公特做外孩相西果走将月十实向声车全信重三机工物气每并别真打太新比才便夫再书部水像眼等体却加电主界门利海受听表德少克代员许先口由死安写性马光白住难望教命花结乐色更拉东神记处让母父应直字场平报友关放至张认接告入笑内英军候民岁往何度山觉路带万男边风解叫任金快原吃妈变通师立象数四失满战远格士音轻目条呢病始达完今提求清王化空业思切怎非找片罗钱吗语元喜曾离飞科言干流欢约各即指合反题必该论交终林请医晚制球决传画保读运及则房早院量苦火布品近坐产答星精视五连司巴奇管类未朋且婚台夜青北队久乎越观落尽形影红爸百令周吧识步希亚术留市半热送兴支节安故深具流畅逻辑语速接收口头禅心率平静紧张恐慌开口思路衔接节奏确认忽略切换会话钱包递钩演示卡壳表达效果实时转写连接中已连接未连接开始结束恢复提交清除评分辅助演讲控制台等待报告设置演讲中遮脸减压冬瓜模型遮挡真实表情成长属性历次辅助";
                _tmpFont.TryAddCharacters(prewarm);

                Debug.Log($"[QUI] TMP 字体验证通过 dynamic={_tmpFont.atlasPopulationMode} name={_tmpFont.name} atlasSize={_tmpFont.atlasWidth}x{_tmpFont.atlasHeight}");
            }
            catch (System.Exception e)
            {
                // 动态模式或预热失败 → 降级为 uGUI Text，避免全局白块
                _preferTmp = false;
                _tmpFont = null;
                Debug.LogWarning($"[QUI] TMP 字体验证失败，降级为 uGUI Text: {e.Message}");
            }
        }

        /// <summary>确保 TMP 动态图集包含指定文本的所有字符。</summary>
        static void EnsureTmpCharacters(TMP_FontAsset font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text)) return;
            try
            {
                font.TryAddCharacters(text);
            }
            catch { }
        }

        static Font LoadPico(string fileBaseName, PicoFontWeight weight)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // PICO fonts.xml:
            //  - sans-serif -> PICOSans.ttf（西文）
            //  - lang=zh-Hans fallback -> PICOSansSC-* / PICOSansVFSC
            // Unity CreateDynamicFontFromOSFont 对 fallback 族名支持有限，
            // 优先用「文件名去扩展」；再回退 sans-serif（系统会按中文走 SC 回落）。
            string path = "/system/fonts/" + fileBaseName + ".ttf";
            if (File.Exists(path))
            {
                var byFile = LoadByOsName(fileBaseName);
                if (byFile != null)
                {
                    Debug.Log($"[QUI] PICO 字体文件: {path} -> {byFile.name}");
                    return byFile;
                }
            }
#endif
            // 名称候选：文件名、带空格、官方文档名
            return LoadByOsName(
                fileBaseName,
                fileBaseName.Replace('-', ' '),
                "PICO Sans SC",
                "PICOSans SC",
                "PICO Sans",
                "PICOSans",
                "sans-serif");
        }

        static Font LoadByOsName(params string[] names)
        {
            try
            {
                var f = Font.CreateDynamicFontFromOSFont(names, 32);
                if (f != null && !string.IsNullOrEmpty(f.name))
                    return f;
            }
            catch { }

            // 扫描已安装字体名
            try
            {
                var installed = Font.GetOSInstalledFontNames();
                if (installed != null)
                {
                    foreach (var want in names)
                    {
                        foreach (var n in installed)
                        {
                            if (n != null && n.IndexOf(want, System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var f = Font.CreateDynamicFontFromOSFont(n, 32);
                                if (f != null) return f;
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        static Font LoadFromStreamingAssets()
        {
            string sa = Application.streamingAssetsPath;
            if (string.IsNullOrEmpty(sa) || !Directory.Exists(sa)) return null;
            string dir = Path.Combine(sa, "fonts");
            if (!Directory.Exists(dir)) return null;

            string[] preferred =
            {
                "PICOSansSC-Regular.ttf",
                "PICOSansSC-Medium.ttf",
                "PICOSansSC-Bold.ttf",
                "PICOSansVFSC.ttf",
                "PICOSans.ttf",
                "NotoSansSC-Regular.otf",
                "NotoSansSC-Regular.ttf",
                "SourceHanSansSC-Regular.otf",
                "DroidSansFallback.ttf"
            };
            foreach (var name in preferred)
            {
                string path = Path.Combine(dir, name);
                if (!File.Exists(path)) continue;
                // 用文件名作为 OS 字体名通常无效；优先靠系统字体
                // 若用户把字体安装进系统，可被 GetOSInstalledFontNames 扫到
                var f = LoadByOsName(Path.GetFileNameWithoutExtension(name));
                if (f != null) return f;
            }
            return null;
        }

        public static Component CreateText(
            Transform parent,
            string name,
            string content,
            float size,
            TextAnchor legacyAlign,
            TextAlignmentOptions tmpAlign,
            Color? color = null,
            bool wrap = false)
        {
            return CreateText(parent, name, content, size, PicoFontWeight.Regular, legacyAlign, tmpAlign, color, wrap);
        }

        public static Component CreateText(
            Transform parent,
            string name,
            string content,
            PicoTypeRole role,
            TextAnchor legacyAlign,
            TextAlignmentOptions tmpAlign,
            Color? color = null,
            bool wrap = false)
        {
            return CreateText(parent, name, content, SizeOf(role), WeightOf(role), legacyAlign, tmpAlign, color, wrap);
        }

        public static Component CreateText(
            Transform parent,
            string name,
            string content,
            float size,
            PicoFontWeight weight,
            TextAnchor legacyAlign,
            TextAlignmentOptions tmpAlign,
            Color? color = null,
            bool wrap = false)
        {
            EnsureFonts();

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Color c = color ?? Color.white;

            if (_preferTmp && _tmpFont != null)
            {
                try
                {
                    var tmp = go.AddComponent<TextMeshProUGUI>();
                    tmp.font = _tmpFont;
                    // 确保初始内容中的字符已在动态图集中
                    if (!string.IsNullOrEmpty(content))
                        EnsureTmpCharacters(_tmpFont, content);
                    tmp.text = content ?? "";
                    tmp.fontSize = size;
                    tmp.alignment = tmpAlign;
                    tmp.color = c;
                    tmp.enableWordWrapping = wrap;
                    tmp.raycastTarget = false;
                    tmp.fontStyle = weight >= PicoFontWeight.Bold ? FontStyles.Bold : FontStyles.Normal;
                    if (!wrap) tmp.overflowMode = TextOverflowModes.Overflow;
                    return tmp;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[QUI] TMP 失败，回退 Text: {e.Message}");
                    var bad = go.GetComponent<TextMeshProUGUI>();
                    if (bad != null) Object.Destroy(bad);
                }
            }

            var text = go.AddComponent<Text>();
            text.font = FontOf(weight);
            text.text = content ?? "";
            text.fontSize = Mathf.Max(12, Mathf.RoundToInt(size));
            text.fontStyle = weight >= PicoFontWeight.Bold ? FontStyle.Bold
                : weight == PicoFontWeight.Medium ? FontStyle.Normal
                : FontStyle.Normal;
            text.alignment = legacyAlign;
            text.color = c;
            text.raycastTarget = false;
            text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.lineSpacing = 1.08f; // 略松行高，空间 UI 更易读
            RequestChineseCharacters(text.font);
            return text;
        }

        static void RequestChineseCharacters(Font font)
        {
            if (font == null) return;
            if (_charsRequestedFor.Contains(font)) return;
            _charsRequestedFor.Add(font);
            try
            {
                font.RequestCharactersInTexture(
                    "的一是了我不人在他有这个上们来到时大地为子中你说生国年着就那和要她出也得里后自以会家可下过天去能对小多然于心学么之都好看起发当没成只如事把还用第样道想作种开美总从无情己面女但现前些所同日手又行意动方期它头经长儿回位分爱老因很给名法间斯知世什两次使身者被高已亲其进此话常与活正感见明问力理尔点文几定本公特做外孩相西果走将月十实向声车全信重三机工物气每并别真打太新比才便夫再书部水像眼等体却加电主界门利海受听表德少克代员许先口由死安写性马光白住难望教命花结乐色更拉东神记处让母父应直字场平报友关放至张认接告入笑内英军候民岁往何度山觉路带万男边风解叫任金快原吃妈变通师立象数四失满战远格士音轻目条呢病始达完今提求清王化空业思切怎非找片罗钱吗语元喜曾离飞科言干流欢约各即指合反题必该论交终林请医晚制球决传画保读运及则房早院量苦火布品近坐产答星精视五连司巴奇管类未朋且婚台夜青北队久乎越观落尽形影红爸百令周吧识步希亚术留市半热送兴支节安故深具流畅逻辑语速接收口头禅心率平静紧张恐慌开口思路衔接节奏确认忽略切换会话钱包递钩演示卡壳表达效果实时转写连接中已连接未连接开始结束恢复提交清除评分辅助演讲控制台等待报告设置演讲中遮脸减压冬瓜模型遮挡真实表情成长属性历次辅助",
                    28,
                    FontStyle.Normal);
                font.RequestCharactersInTexture(
                    "流畅逻辑语速接收口头禅心率平静紧张恐慌开口思路衔接节奏确认忽略切换会话钱包递钩演示卡壳表达效果实时转写连接中已连接未连接开始结束恢复提交清除评分辅助演讲控制台等待报告设置演讲中遮脸减压冬瓜模型遮挡真实表情成长属性历次辅助",
                    28,
                    FontStyle.Bold);
            }
            catch { }
        }

        public static void SetText(Component textComp, string value)
        {
            if (textComp == null) return;
            string v = value ?? "";
            if (textComp is TMP_Text tmp)
            {
                // 动态文本（ASR 实时转写等）可能包含新汉字 → 先加入动态图集再渲染
                if (!string.IsNullOrEmpty(v) && tmp.font != null)
                    EnsureTmpCharacters(tmp.font, v);
                tmp.text = v;
                return;
            }
            if (textComp is Text t)
            {
                t.text = v;
                if (t.font == null)
                    t.font = CjkFont;
            }
        }

        public static void SetColor(Component textComp, Color color)
        {
            if (textComp == null) return;
            if (textComp is TMP_Text tmp) { tmp.color = color; return; }
            if (textComp is Text t) t.color = color;
        }

        public static string GetText(Component textComp)
        {
            if (textComp == null) return "";
            if (textComp is TMP_Text tmp) return tmp.text;
            if (textComp is Text t) return t.text;
            return "";
        }

        public static void SetFontStyleBold(Component textComp)
        {
            if (textComp is TMP_Text tmp)
            {
                tmp.fontStyle = FontStyles.Bold;
                return;
            }
            if (textComp is Text t)
            {
                // 仅改变 fontWeight/style，不切换底层字体对象。
                // 切换字体会导致"样式漂移"：Bold 字重文件若加载失败
                // 或回退到不含中文的字体 → 全文本白块。
                t.fontStyle = FontStyle.Bold;
            }
        }

        public static void SetWrap(Component textComp, bool wrap)
        {
            if (textComp is TMP_Text tmp)
            {
                tmp.enableWordWrapping = wrap;
                tmp.overflowMode = wrap ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
                return;
            }
            if (textComp is Text t)
                t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        }

        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Button CreateButton(Transform parent, string name, string label, Color bg)
        {
            var img = CreateImage(parent, name, bg);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(
                Mathf.Min(1f, bg.r + 0.1f),
                Mathf.Min(1f, bg.g + 0.1f),
                Mathf.Min(1f, bg.b + 0.1f), 1f);
            colors.pressedColor = bg * 0.8f;
            btn.colors = colors;

            // 按钮文案用 Label + Medium
            var textComp = CreateText(img.transform, "Label", label,
                SizeOf(PicoTypeRole.Label), PicoFontWeight.Medium,
                TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            var labelRt = textComp.transform as RectTransform;
            if (labelRt != null)
            {
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = new Vector2(4, 4);
                labelRt.offsetMax = new Vector2(-4, -4);
            }
            return btn;
        }

        public static void StretchFull(RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        public static void SetRect(RectTransform rt, float axMin, float axMax, float ayMin, float ayMax,
            float left, float bottom, float right, float top)
        {
            rt.anchorMin = new Vector2(axMin, ayMin);
            rt.anchorMax = new Vector2(axMax, ayMax);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(right, top);
        }
    }
}
