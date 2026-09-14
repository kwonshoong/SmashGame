using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace SmashGame
{
    /// <summary>uGUI를 코드로 조립하기 위한 최소 도구. 기준 해상도 1080x1920 세로.</summary>
    public static class UIKit
    {
        static Font font;

        public static Font DefaultFont
        {
            get
            {
                if (font != null) return font;
                // 한글 표기를 위해 OS 폰트 우선
                string[] candidates = { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" };
                foreach (var n in candidates)
                {
                    try { font = UnityEngine.Font.CreateDynamicFontFromOSFont(n, 40); } catch { font = null; }
                    if (font != null) break;
                }
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            var img = go.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        public static RectTransform FullPanel(Transform parent, string name, Color color)
            => Panel(parent, name, color, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        /// <summary>앵커 기준 위치/크기 지정 박스</summary>
        public static RectTransform Box(Transform parent, string name, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        public static Text Label(Transform parent, string text, int size, Color color, Vector2 anchor, Vector2 pos, Vector2 boxSize, TextAnchor align = TextAnchor.MiddleCenter, bool bold = false)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = boxSize;
            var t = go.AddComponent<Text>();
            t.font = DefaultFont;
            t.fontSize = size;
            t.color = color;
            t.text = text;
            t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.5f);
            outline.effectDistance = new Vector2(2, -2);
            return t;
        }

        public static Text FillLabel(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, bool bold = false)
        {
            var t = Label(parent, text, size, color, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, align, bold);
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(16, 8); rt.offsetMax = new Vector2(-16, -8);
            return t;
        }

        public static Button Button(Transform parent, string label, Color color, Vector2 anchor, Vector2 pos, Vector2 size, System.Action onClick, int fontSize = 40)
        {
            var rt = Box(parent, "Btn_" + label, color, anchor, pos, size);
            var btn = rt.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = color * 1.1f;
            colors.pressedColor = color * 0.8f;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick?.Invoke());
            FillLabel(rt, label, fontSize, Color.white, TextAnchor.MiddleCenter, true);
            return btn;
        }

        public static void SetButtonLabel(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static readonly Color Green = new Color(0.35f, 0.75f, 0.25f);
        public static readonly Color Blue = new Color(0.2f, 0.45f, 0.85f);
        public static readonly Color Purple = new Color(0.45f, 0.25f, 0.65f);
        public static readonly Color Orange = new Color(0.95f, 0.55f, 0.15f);
        public static readonly Color Red = new Color(0.85f, 0.25f, 0.3f);
        public static readonly Color Gold = new Color(1f, 0.8f, 0.2f);
        public static readonly Color PanelDark = new Color(0.12f, 0.08f, 0.2f, 1f);
        public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.65f);
        public static readonly Color Bar = new Color(0.55f, 0.12f, 0.35f, 0.9f);
    }
}
