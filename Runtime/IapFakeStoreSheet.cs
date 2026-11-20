using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Purchasing;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && RISEON_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace RiseOn.Iap {
    /// <summary>
    /// The fake store's payment sheet: the product as the catalog describes it, and how the purchase ends.<br/>
    /// Built in code over every canvas and laid out on a 400 x 720 screen shrunk to fit, so it reads alike on a phone and in the Game view;
    /// its clock is unscaled, as games often pause under a shop.
    /// </summary>
    internal sealed class IapFakeStoreSheet : MonoBehaviour {
        public enum Answer { Buy, Cancel, Decline, Defer }

        private static readonly Vector2 Layout = new(400, 720);

        private const float OpenTime  = 0.28f;
        private const float CloseTime = 0.18f;

        // The rounded sprite's corner in its own pixels; each image scales it to its radius.
        private const float Corner = 32;

        // The background runs this far past the screen's bottom, so only its top corners show round.
        private const float Overhang = 24;

        private static readonly Color Dim      = new(0, 0, 0, 0.45f);
        private static readonly Color Surface  = Color.white;
        private static readonly Color Ink      = new(0.11f, 0.11f, 0.12f);
        private static readonly Color Muted    = new(0.42f, 0.42f, 0.45f);
        private static readonly Color Rule     = new(0.89f, 0.89f, 0.91f);
        private static readonly Color Accent   = new(0, 0.48f, 1);
        private static readonly Color Quiet    = new(0.94f, 0.94f, 0.96f);
        private static readonly Color Badge    = new(1, 0.62f, 0.04f, 0.18f);
        private static readonly Color BadgeInk = new(0.6f, 0.34f, 0);

        private static Sprite rounded;
        private static Font font;
        private static IapFakeStoreSheet current;

        private CanvasGroup group;
        private Image dim;
        private RectTransform sheet;
        private Action<Answer> answered;
        private float clock;
        private bool closing;

        /// <summary>Opens the sheet; <paramref name="answered"/> runs once, as the sheet starts to leave.</summary>
        public static void Show(ProductMetadata product, IapProductType type, Action<Answer> answered) {
            // The pack runs one purchase at a time, so a second open sheet is the caller's bug; it ends as cancelled.
            if (current != null) {
                Debug.LogWarning($"{nameof(IapFakeStore)}: a purchase sheet is open already, so this one ends as cancelled");
                answered(Answer.Cancel);
                return;
            }

            var root = new GameObject(nameof(IapFakeStoreSheet), typeof(RectTransform));
            DontDestroyOnLoad(root);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Layout;
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Shrink;
            root.AddComponent<GraphicRaycaster>();

            current          = root.AddComponent<IapFakeStoreSheet>();
            current.group    = root.AddComponent<CanvasGroup>();
            current.answered = answered;
            current.Build(product, type);

            if (FindAnyObjectByType<EventSystem>() == null) {
                var events = new GameObject(nameof(EventSystem), typeof(EventSystem));
                events.transform.SetParent(root.transform, false);
#if ENABLE_INPUT_SYSTEM && RISEON_INPUT_SYSTEM
                events.AddComponent<InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
            }
        }

        private void Build(ProductMetadata product, IapProductType type) {
            // A tap beside the sheet cancels, as on the stores' own sheets.
            var shade = Child("Dim", transform);
            Stretch(shade);
            dim       = shade.gameObject.AddComponent<Image>();
            dim.color = Color.clear;
            var outside = shade.gameObject.AddComponent<Button>();
            outside.transition = Selectable.Transition.None;
            outside.onClick.AddListener(() => Close(Answer.Cancel));

            sheet           = Child("Sheet", transform);
            sheet.anchorMin = sheet.anchorMax = sheet.pivot = new Vector2(0.5f, 0);
            sheet.sizeDelta = new Vector2(Layout.x, 0);

            var scale  = Mathf.Min(Screen.width / Layout.x, Screen.height / Layout.y);
            var column = Column(sheet, 12);
            column.padding = new RectOffset(20, 20, 8, 20 + Mathf.RoundToInt(Screen.safeArea.yMin / scale));
            sheet.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Backdrop(sheet, Surface, 20).offsetMin = new Vector2(0, -Overhang);

            var grip = Fixed(Child("Grip", sheet), 5);
            var pill = Child("Pill", grip);
            pill.anchorMin = pill.anchorMax = new Vector2(0.5f, 0.5f);
            pill.sizeDelta = new Vector2(36, 5);
            Rounded(pill, Rule, 2.5f);

            // The badge keeps its text's width, on the left.
            var badge = Row(Child("Badge", Row(Child("Badge Row", sheet), TextAnchor.MiddleLeft, 0).transform), TextAnchor.MiddleCenter, 0);
            badge.padding = new RectOffset(8, 8, 3, 3);
            Backdrop((RectTransform) badge.transform, Badge, 6);
            Label(badge.transform, "TEST STORE  ·  NO CHARGE", 11, FontStyle.Bold, BadgeInk, TextAnchor.MiddleLeft);

            var about = Child("Product", sheet);
            Column(about, 4);
            Label(about, product.localizedTitle, 20, FontStyle.Bold, Ink, TextAnchor.UpperLeft);
            Label(about, type switch { IapProductType.NonConsumable => "Non-Consumable", IapProductType.Subscription => "Subscription", _ => "Consumable" }, 13, FontStyle.Normal, Muted, TextAnchor.UpperLeft);
            if (!string.IsNullOrEmpty(product.localizedDescription)) Label(about, product.localizedDescription, 14, FontStyle.Normal, Muted, TextAnchor.UpperLeft);

            Fixed(Child("Rule", sheet), 1).gameObject.AddComponent<Image>().color = Rule;

            var price = Row(Child("Price", sheet), TextAnchor.MiddleLeft, 8);
            price.childForceExpandWidth = true;
            Label(price.transform, "Price", 15, FontStyle.Normal, Muted, TextAnchor.MiddleLeft);
            Label(price.transform, product.localizedPriceString, 15, FontStyle.Bold, Ink, TextAnchor.MiddleRight);

            Action(sheet, $"Buy for {product.localizedPriceString}", Accent, Color.white, 48, 17, FontStyle.Bold, Answer.Buy);
            Action(sheet, "Cancel", Quiet, Accent, 44, 16, FontStyle.Normal, Answer.Cancel);

            // The endings a tester cannot reach at will on a real store.
            var endings = Row(Child("Endings", sheet), TextAnchor.MiddleCenter, 8);
            Link(endings.transform, "Payment declined", Answer.Decline);
            Label(endings.transform, "·", 13, FontStyle.Normal, Muted, TextAnchor.MiddleCenter);
            Link(endings.transform, "Deferred (Ask to Buy)", Answer.Defer);

            // Laid out now, so the first frame already starts below the screen.
            LayoutRebuilder.ForceRebuildLayoutImmediate(sheet);
            Place(0);
        }

        private void Update() {
            clock += Time.unscaledDeltaTime;
            if (!closing) {
                var rise = 1 - Mathf.Clamp01(clock / OpenTime);
                Place(1 - rise * rise * rise);
                return;
            }

            var fall = Mathf.Clamp01(clock / CloseTime);
            Place(1 - fall * fall);
            if (fall >= 1) Destroy(gameObject);
        }

        // 0: below the screen with no shade; 1: up, with the full shade.
        private void Place(float shown) {
            sheet.anchoredPosition = new Vector2(0, -(sheet.rect.height + Overhang) * (1 - shown));
            dim.color              = new Color(Dim.r, Dim.g, Dim.b, Dim.a * shown);
        }

        private void Close(Answer answer) {
            if (closing) return;

            closing            = true;
            clock              = 0;
            group.interactable = false;

            // The purchase ends now, so the next one may open its sheet while this one leaves.
            if (current == this) current = null;
            Send(answer);
        }

        private void Send(Answer answer) {
            var call = answered;
            answered = null;
            try {
                call?.Invoke(answer);
            } catch (Exception exception) {
                Debug.LogException(exception);
            }
        }

        // Removed before an answer, by the end of Play mode or anything else: the purchase must still end.
        private void OnDestroy() {
            if (current == this) current = null;
            Send(Answer.Cancel);
        }

        private void Action(Transform parent, string text, Color fill, Color ink, float height, int size, FontStyle style, Answer answer) {
            var rect   = Fixed(Child(text, parent), height);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = Rounded(rect, fill, 12);
            Press(button, answer);
            Stretch(Label(rect, text, size, style, ink, TextAnchor.MiddleCenter).rectTransform);
        }

        private void Link(Transform parent, string text, Answer answer) {
            var row = Row(Child(text, parent), TextAnchor.MiddleCenter, 0);
            row.padding = new RectOffset(4, 4, 8, 8);
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            Press(button, answer);
            Label(row.transform, text, 13, FontStyle.Normal, Accent, TextAnchor.MiddleCenter);
        }

        private void Press(Button button, Answer answer) {
            var colors = button.colors;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f);
            colors.pressedColor     = new Color(0.78f, 0.78f, 0.78f);
            colors.selectedColor    = Color.white;
            colors.fadeDuration     = 0.06f;
            button.colors           = colors;
            button.navigation       = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => Close(answer));
        }

        private static RectTransform Child(string name, Transform parent) {
            var rect = (RectTransform) new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Fixed(RectTransform rect, float height) {
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return rect;
        }

        private static VerticalLayoutGroup Column(RectTransform rect, float spacing) {
            var column = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing                = spacing;
            column.childControlWidth      = true;
            column.childControlHeight     = true;
            column.childForceExpandWidth  = true;
            column.childForceExpandHeight = false;
            return column;
        }

        private static HorizontalLayoutGroup Row(RectTransform rect, TextAnchor alignment, float spacing) {
            var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment         = alignment;
            row.spacing                = spacing;
            row.childControlWidth      = true;
            row.childControlHeight     = true;
            row.childForceExpandWidth  = false;
            row.childForceExpandHeight = false;
            return row;
        }

        private static Text Label(Transform parent, string value, int size, FontStyle style, Color color, TextAnchor alignment) {
            var text = Child("Text", parent).gameObject.AddComponent<Text>();
            text.font               = font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text               = value;
            text.fontSize           = size;
            text.fontStyle          = style;
            text.color              = color;
            text.alignment          = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow   = VerticalWrapMode.Overflow;
            text.supportRichText    = false;
            text.raycastTarget      = false;
            return text;
        }

        // Behind a laid out box and out of its layout: a sliced image asks for its sprite's whole border as its size, unscaled.
        private static RectTransform Backdrop(RectTransform parent, Color color, float radius) {
            var rect = Child("Backdrop", parent);
            Stretch(rect);
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Rounded(rect, color, radius);
            return rect;
        }

        private static Image Rounded(RectTransform rect, Color color, float radius) {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite                  = rounded ??= CreateRounded();
            image.type                    = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = Corner / radius;
            image.color                   = color;
            return image;
        }

        // A white disc cut in nine around a one-pixel middle: every rounded box of the sheet slices it to its own radius.
        private static Sprite CreateRounded() {
            var size   = (int) Corner * 2 + 1;
            var middle = size / 2f;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++) {
                for (var x = 0; x < size; x++) {
                    var dx    = Mathf.Max(0, Mathf.Abs(x + 0.5f - middle) - 0.5f);
                    var dy    = Mathf.Max(0, Mathf.Abs(y + 0.5f - middle) - 0.5f);
                    var alpha = Mathf.Clamp01(Corner - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte) Mathf.RoundToInt(alpha * 255));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(Corner, Corner, Corner, Corner));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}