using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Purchasing;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && IAP_SUPPORT_INPUTSYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace RiseOn.Iap {
    /// <summary>
    /// The fake store's payment sheet, drawn as the store the build buys from draws its own in the device's light or dark appearance, so a tester sees what a player will.<br/>
    /// Both stand in for the stores' test sheets, the ones a tester meets on a test account, measured on the devices.<br/>
    /// Google Play: the store's name, the product with its price and tax, the payment method, whose page in the sheet lists Play's test cards, the test-order notice,
    /// the terms and 1-tap buy.
    /// The App Store: the floating glass sheet of a test purchase, the product, its price and the testing notice in a card, and Buy; Apple's sheet has no way to
    /// decline or ask to buy, so those wait in a strip above it marked as the fake store's.<br/>
    /// After the choice the sheet shows the outcome, as the stores do, then leaves. Built in code over every canvas, laid out in a phone's points, 400 on the screen's
    /// short side, and shrunk where it would not fit;
    /// its clock is unscaled, as games often pause under a shop.
    /// </summary>
    internal sealed class IapFakeStoreSheet : MonoBehaviour {
        /// <summary>How the purchase ends: bought, canceled, declined, or held and later approved or declined.</summary>
        public enum Answer { Buy, Cancel, Decline, ApproveLater, DeclineLater }

        /// <summary>Which store's sheet stands in, and in which appearance.</summary>
        internal readonly struct Look {
            public readonly bool Apple;
            public readonly bool Dark;

            public Look(bool apple, bool dark) {
                Apple = apple;
                Dark  = dark;
            }

            /// <summary>The store the build buys from and the appearance it shows in: in the Editor, the active build target and the Editor's theme.</summary>
            public static Look Current => new(AppleStore(), DarkAppearance());
        }

        private sealed class Palette {
            public Color Surface, Card, Ink, Muted, Rule, Accent, OnAccent, Good, Bad, Chip, ChipInk, Dim;
        }

        private enum Glyphs { Check, Cross, Clock }

        // Play's test cards, in the order its Payment methods page lists them, and how each ends the purchase.
        private static readonly (string Label, Answer Answer)[] testCards = {
            ("Test card, always approves", Answer.Buy),
            ("Test card, always declines", Answer.Decline),
            ("Slow test card, approves after a few seconds", Answer.ApproveLater),
            ("Slow test card, declines after a few seconds", Answer.DeclineLater)
        };

        private static readonly Vector2 layout = new(400, 720);

        private const float OpenTime   = 0.28f;
        private const float CloseTime  = 0.18f;
        private const float ResultTime = 1.4f;

        // The rounded sprite's corner in its own pixels; each image scales it to its radius.
        private const float Corner = 96;

        // Play's sheet in a wide window.
        private const float WideWidth = 560;

        // Apple's sheet floats this far from the screen's edges; its top corners have one radius, its bottom ones follow the screen's rounded corners on a phone
        // that has them. Its shape is drawn at three pixels a point, as a phone shows it.
        private const float AppleInset  = 6;
        private const float AppleTop    = 34;
        private const float AppleBottom = 58;
        private const float Density     = 3;

        private static readonly Palette playLight = new() {
            Surface = Hex(0xFFFFFF), Ink = Hex(0x1F1F1F), Muted = Hex(0x444746), Rule = Hex(0xC4C7C5), Accent = Hex(0x0B57D0), OnAccent = Color.white,
            Good = Hex(0x1E8E3E), Bad = Hex(0xD93025), Chip = Hex(0xE9EEF6), ChipInk = Hex(0x1F1F1F), Dim = new Color(0, 0, 0, .32f)
        };

        private static readonly Palette playDark = new() {
            Surface = Hex(0x131314), Ink = Hex(0xE3E3E3), Muted = Hex(0xC4C7C5), Rule = Hex(0x444746), Accent = Hex(0xA8C7FA), OnAccent = Hex(0x062E6F),
            Good = Hex(0x6DD58C), Bad = Hex(0xF2B8B5), Chip = Hex(0x282A2C), ChipInk = Hex(0xE3E3E3), Dim = new Color(0, 0, 0, .6f)
        };

        // Apple's glass lets the game through, and nothing dims the game behind it.
        private static readonly Palette appleLight = new() {
            Surface = new Color(1, 1, 1, .74f), Card = Color.white, Ink = Color.black, Muted = Hex(0x8A8A8E), Rule = Hex(0xC6C6C8), Accent = Hex(0x0088FF),
            OnAccent = Color.white, Good = Hex(0x0088FF), Bad = Hex(0xFF383C), Chip = new Color(0, 0, 0, .06f), ChipInk = Color.black, Dim = Color.clear
        };

        private static readonly Palette appleDark = new() {
            Surface = new Color(.11f, .11f, .12f, .78f), Card = Hex(0x2C2C2E), Ink = Color.white, Muted = Hex(0x8D8D93), Rule = Hex(0x38383A), Accent = Hex(0x0091FF),
            OnAccent = Color.white, Good = Hex(0x0091FF), Bad = Hex(0xFF4245), Chip = new Color(1, 1, 1, .1f), ChipInk = Color.white, Dim = Color.clear
        };

        /// <summary>The screen the sheet lays out for when set; a batchmode test sets it, as its own screen has one fixed size.</summary>
        internal static Vector2? ScreenSize;

        private static Sprite rounded;
        private static Font font;
        private static IapFakeStoreSheet current;

        private CanvasGroup group;
        private GameObject strip;
        private Sprite shape;
        private Image dim;
        private RectTransform sheet;
        private RectTransform main;
        private RectTransform cards;
        private Action<Answer> answered;
        private ProductMetadata product;
        private IapProductType type;
        private Palette colors;
        private bool apple;
        private bool wide;
        private float rest;
        private int bottom;
        private int card;
        private float clock;
        private bool closing;
        private float resultLeft = -1;
        private Answer resultAnswer;

        /// <summary>Opens the sheet; <paramref name="answered"/> runs once, as the sheet starts to leave.</summary>
        public static void Show(ProductMetadata product, IapProductType type, Action<Answer> answered) => Show(product, type, Look.Current, answered);

        internal static void Show(ProductMetadata product, IapProductType type, Look look, Action<Answer> answered) {
            // The pack runs one purchase at a time, so a second open sheet is the caller's bug; it ends as canceled.
            if (current != null) {
                Debug.LogWarning($"{nameof(IapFakeStore)}: a purchase sheet is open already, so this one ends as canceled");
                answered(Answer.Cancel);
                return;
            }

            var root = new GameObject(nameof(IapFakeStoreSheet), typeof(RectTransform));
            DontDestroyOnLoad(root);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            // Its scale is set as the sheet is laid out.
            root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            root.AddComponent<GraphicRaycaster>();

            current          = root.AddComponent<IapFakeStoreSheet>();
            current.group    = root.AddComponent<CanvasGroup>();
            current.answered = answered;
            current.product  = product;
            current.type     = type;
            current.Build(look);

            if (FindAnyObjectByType<EventSystem>() == null) {
                var events = new GameObject(nameof(EventSystem), typeof(EventSystem));
                events.transform.SetParent(root.transform, false);
#if ENABLE_INPUT_SYSTEM && IAP_SUPPORT_INPUTSYSTEM
                events.AddComponent<InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
            }
        }

        private void Build(Look look) {
            apple  = look.Apple;
            colors = apple ? look.Dark ? appleDark : appleLight : look.Dark ? playDark : playLight;
            var screen = ScreenSize ?? new Vector2(Screen.width, Screen.height);
            wide = screen.x > screen.y;

            // A phone's points: its short side 400, held either way, a tablet held to 720 on its long side.
            var scale  = Mathf.Min(Mathf.Min(screen.x, screen.y) / layout.x, Mathf.Max(screen.x, screen.y) / layout.y);
            var canvas = screen / scale;
            bottom = ScreenSize == null ? Mathf.RoundToInt(Screen.safeArea.yMin / scale) : 0;

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
            sheet.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Column(sheet, 0);
            // Over the sheet, so a press on it never falls through to the shade.
            sheet.gameObject.AddComponent<Image>().color = Color.clear;

            if (apple) {
                // Floating clear of the screen's edges, as iOS draws it; never wider than a phone's. The Editor stands in for a phone with rounded corners.
                sheet.sizeDelta = new Vector2(Mathf.Min(canvas.x, layout.x) - AppleInset * 2, 0);
                rest            = AppleInset;
                var concentric = Application.isEditor || Screen.safeArea.yMin > 0;
                shape = CreateRounded(AppleTop * Density, (concentric ? AppleBottom : AppleTop) * Density);
                var glass = Backdrop(sheet, colors.Surface, 0).GetComponent<Image>();
                glass.sprite                  = shape;
                glass.type                    = Image.Type.Sliced;
                glass.pixelsPerUnitMultiplier = Density;
                main = BuildApple();
                Strip();
            } else {
                // Across a phone held upright; a wide screen centres it, as Play does.
                sheet.sizeDelta = new Vector2(wide ? Mathf.Min(canvas.x, WideWidth) : canvas.x, 0);
                Backdrop(sheet, colors.Surface, 0);
                main  = BuildPlay();
                cards = BuildCards();
                cards.gameObject.SetActive(false);
            }

            // Laid out now, so the first frame already starts below the screen; shrunk where a screen on its side is too short for it.
            LayoutRebuilder.ForceRebuildLayoutImmediate(sheet);
            var need = sheet.rect.height + rest + (apple ? 64 : 24);
            if (need > canvas.y) scale *= canvas.y / need;
            GetComponent<CanvasScaler>().scaleFactor = scale;
            Place(0);
        }

        // Google Play's billing sheet as Play 54 draws it for a test account: the store's name over a rule, the product with its price and tax, the payment
        // method that opens the test cards, the test-order notice, the terms and the buy button; X only on a wide screen, as Play shows it there.
        // A tap beside it cancels.
        private RectTransform BuildPlay() {
            var page         = Page("Purchase");
            var subscription = type is IapProductType.Subscription;
            var buy          = subscription ? "Subscribe" : "1-tap buy";

            var head = Row(Child("Header", page), TextAnchor.MiddleLeft, 8);
            head.padding = new RectOffset(24, wide ? 12 : 24, 10, 10);
            Label(head.transform, "Google Play", 17, FontStyle.Normal, colors.Muted, TextAnchor.MiddleLeft);
            Spacer(head.transform);
            if (wide) CloseButton(head.transform, 32, false);
            Rule(page);

            var item = Row(Child("Product", page), TextAnchor.MiddleLeft, 19);
            item.padding = new RectOffset(27, 24, 27, 27);
            Icon(item.transform, 34, 8);
            var names = Names(item.transform);
            Column(names, 4);
            Label(names, product.localizedTitle, 16, FontStyle.Normal, colors.Ink, TextAnchor.UpperLeft);
            Label(names, Application.productName, 14, FontStyle.Normal, colors.Muted, TextAnchor.UpperLeft);
            if (!subscription) {
                var price = Child("Price", item.transform);
                Column(price, 4).childAlignment = TextAnchor.UpperRight;
                Label(price, product.localizedPriceString, 16, FontStyle.Normal, colors.Ink, TextAnchor.UpperRight);
                var tax = Row(Child("Tax", price), TextAnchor.MiddleRight, 5);
                Label(tax.transform, "+ tax", 12, FontStyle.Normal, colors.Muted, TextAnchor.MiddleRight);
                InfoGlyph(tax.transform);
            }

            if (subscription) {
                var terms = Child("Terms", page);
                Column(terms, 6).padding = new RectOffset(24, 24, 0, 16);
                var starting = Row(Child("Starting", terms), TextAnchor.MiddleLeft, 8);
                Label(starting.transform, "Starting today", 14, FontStyle.Normal, colors.Ink, TextAnchor.MiddleLeft);
                Spacer(starting.transform);
                Label(starting.transform, product.localizedPriceString, 14, FontStyle.Normal, colors.Ink, TextAnchor.MiddleRight);
                Label(terms, "Cancel anytime in Subscriptions on Google Play", 13, FontStyle.Normal, colors.Muted, TextAnchor.UpperLeft);
            }

            Rule(page);
            var method = Tappable(Row(Child("Payment Method", page), TextAnchor.MiddleLeft, 24), () => Open(cards));
            method.padding = new RectOffset(30, 28, 17, 17);
            CardGlyph(method.transform, card);
            var chosen = Label(method.transform, testCards[card].Label, 15, FontStyle.Normal, colors.Ink, TextAnchor.MiddleLeft);
            chosen.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Chevron(method.transform);
            Rule(page);

            var notice = subscription ? "This is a test subscription. You will not be charged." : "This is a test order, you will not be charged.";
            var about  = Child("Notice", page);
            Column(about, 10).padding = new RectOffset(24, 24, 16, 0);
            Label(about, notice, 14, FontStyle.Normal, colors.Muted, TextAnchor.UpperLeft);
            var agree = Label(about, $"By tapping \"{buy}\", you agree to the Google Play Terms of Service, Privacy Notice. <color=#{ColorUtility.ToHtmlStringRGB(colors.Accent)}>More</color>",
                14, FontStyle.Normal, colors.Muted, TextAnchor.UpperLeft);
            agree.supportRichText = true;

            var action = Child("Action", page);
            Column(action, 0).padding = new RectOffset(24, 24, 28, 16 + bottom);
            Pill(action, buy, 35, 15, FontStyle.Normal, false, () => Commit(testCards[card].Answer));
            return page;
        }

        // Play's Payment methods page, kept in the sheet: the account, the test cards, the chosen one ticked; a pick goes back to the purchase with that card.
        private RectTransform BuildCards() {
            var page = Page("Payment Methods");

            var head = Row(Child("Header", page), TextAnchor.UpperLeft, 12);
            head.padding = new RectOffset(4, 24, 8, 12);
            var back = Tappable(Row(Child("Back", head.transform), TextAnchor.MiddleCenter, 0), () => Open(main));
            back.padding = new RectOffset(12, 12, 12, 12);
            BackArrow(back.transform);
            var titles = Names(head.transform);
            Column(titles, 6).padding = new RectOffset(0, 0, 10, 0);
            Label(titles, "Payment methods", 20, FontStyle.Normal, colors.Ink, TextAnchor.UpperLeft);
            Label(titles, "Fake store test account", 14, FontStyle.Normal, colors.Muted, TextAnchor.UpperLeft);

            for (var i = 0; i < testCards.Length; i++) {
                var index = i;
                var row   = Tappable(Row(Child(testCards[i].Label, page), TextAnchor.MiddleLeft, 20), () => Pick(index));
                row.padding = new RectOffset(26, 26, 22, 22);
                CardGlyph(row.transform, i);
                var label = Label(row.transform, testCards[i].Label, 15, FontStyle.Normal, colors.Ink, TextAnchor.MiddleLeft);
                label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                var mark = Child("Chosen", row.transform);
                var size = mark.gameObject.AddComponent<LayoutElement>();
                size.preferredWidth = size.preferredHeight = size.minWidth = 20;
                if (i == card) Glyph(mark, Glyphs.Check, 20, colors.Accent, colors.Surface);
            }

            Rule(page);
            Fixed(Child("Foot", page), 16 + bottom);
            return page;
        }

        // The App Store's sheet as iOS 26 floats it for a test purchase: the environment's name and X, a card of the product, its price and the testing notice,
        // and Buy.
        private RectTransform BuildApple() {
            var page         = Page("Purchase");
            var subscription = type is IapProductType.Subscription;
            Column(page, 0).padding = new RectOffset(15, 15, 17, 30);

            var head = Row(Child("Header", page), TextAnchor.MiddleLeft, 0);
            head.padding = new RectOffset(5, 0, 0, 0);
            Label(head.transform, "Fake Store", 22, FontStyle.Bold, colors.Ink, TextAnchor.MiddleLeft);
            Spacer(head.transform);
            CloseButton(head.transform, 42, true);
            Fixed(Child("Gap", page), 10);

            var box = Child("Card", page);
            Column(box, 0).padding = new RectOffset(16, 16, 0, 0);
            Backdrop(box, colors.Card, 20);

            var names = Section(box, 18, 16, 2);
            Label(names, product.localizedTitle, 16, FontStyle.Normal, colors.Ink, TextAnchor.UpperLeft);
            Label(names, Application.productName, 14, FontStyle.Normal, colors.Muted, TextAnchor.UpperLeft);
            Label(names, subscription ? "Subscription" : "In-App Purchase", 14, FontStyle.Normal, colors.Muted, TextAnchor.UpperLeft);
            Rule(box);

            var price = Section(box, 15, 16, 3);
            Label(price, product.localizedPriceString, 19, FontStyle.Bold, colors.Ink, TextAnchor.UpperLeft);
            Label(price, subscription ? "Cancel anytime in Settings > Apple Account at least a day before each renewal date. Plan automatically renews until canceled."
                : "One-time charge", 14, FontStyle.Normal, colors.Muted, TextAnchor.UpperLeft);
            Rule(box);

            var note = Section(box, 14, 18, 0);
            Label(note, "For testing purposes only. You will not be charged for confirming this purchase.", 14, FontStyle.Normal, colors.Ink, TextAnchor.UpperLeft);

            Fixed(Child("Gap", page), 31);
            Pill(page, subscription ? "Subscribe" : "Buy", 34, 17, FontStyle.Normal, true, () => Commit(Answer.Buy));
            return page;
        }

        // A block of the App Store card's lines between its rules.
        private static RectTransform Section(RectTransform card, int top, int bottom, float spacing) {
            var section = Child("Section", card);
            Column(section, spacing).padding = new RectOffset(0, 0, top, bottom);
            return section;
        }

        // Apple's sheet has no way to decline or to ask to buy: those wait above it, marked as the fake store's own.
        private void Strip() {
            var rect = Child("Fake Store", sheet);
            strip = rect.gameObject;
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            rect.anchorMin        = rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot            = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, 10);
            var fit = rect.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Row(rect, TextAnchor.MiddleCenter, 6).padding = new RectOffset(12, 12, 2, 2);
            Backdrop(rect, new Color(0, 0, 0, .6f), 15);

            var ink = new Color(1, 1, 1, .92f);
            Label(rect, "Fake Store:", 12, FontStyle.Bold, ink, TextAnchor.MiddleLeft);
            Link(rect, "Decline", ink, () => Commit(Answer.Decline));
            Label(rect, "·", 12, FontStyle.Normal, ink, TextAnchor.MiddleCenter);
            Link(rect, "Ask to Buy: yes", ink, () => Commit(Answer.ApproveLater));
            Label(rect, "·", 12, FontStyle.Normal, ink, TextAnchor.MiddleCenter);
            Link(rect, "Ask to Buy: no", ink, () => Commit(Answer.DeclineLater));
        }

        // Ends the purchase as the store does: its outcome on the sheet for a moment, then away.
        private void Commit(Answer answer) {
            if (closing || resultLeft >= 0) return;

            var height = sheet.rect.height;
            main.gameObject.SetActive(false);
            if (cards) cards.gameObject.SetActive(false);
            if (strip) strip.SetActive(false);
            var page   = Page("Result");
            var column = page.GetComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.MiddleCenter;
            column.spacing        = 10;
            column.padding        = new RectOffset(32, 32, 24, 24 + (apple ? 0 : bottom));
            page.gameObject.AddComponent<LayoutElement>().minHeight = height;

            var (glyph, tint, title, line) = answer switch {
                Answer.Buy when apple     => (Glyphs.Check, colors.Good, "Done", ""),
                Answer.Buy                => (Glyphs.Check, colors.Good, type is IapProductType.Subscription ? "Subscribed" : "Payment successful", ""),
                Answer.Decline when apple => (Glyphs.Cross, colors.Bad, "Payment Declined", "Your payment method was declined."),
                Answer.Decline            => (Glyphs.Cross, colors.Bad, "Your transaction cannot be completed", testCards[1].Label),
                _ when apple              => (Glyphs.Clock, colors.Accent, "Request Sent", "The purchase finishes once it is answered."),
                _                         => (Glyphs.Clock, colors.Accent, "Payment pending", "The purchase finishes once the payment goes through.")
            };

            var mark = Row(Child("Mark", page), TextAnchor.MiddleCenter, 0);
            var slot = Child("Glyph", mark.transform);
            var size = slot.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = size.preferredHeight = 48;
            Glyph(slot, glyph, 48, tint, apple ? colors.Card : colors.Surface);
            Label(page, title, 17, apple ? FontStyle.Bold : FontStyle.Normal, colors.Ink, TextAnchor.UpperCenter);
            if (line.Length > 0) Label(page, line, 13, FontStyle.Normal, colors.Muted, TextAnchor.UpperCenter);

            group.interactable = false;
            resultAnswer       = answer;
            resultLeft         = answer is Answer.Buy ? ResultTime : ResultTime + 0.6f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(sheet);
        }

        private void Pick(int index) {
            card = index;
            main.gameObject.SetActive(false);
            cards.gameObject.SetActive(false);
            Destroy(main.gameObject);
            Destroy(cards.gameObject);
            main  = BuildPlay();
            cards = BuildCards();
            cards.gameObject.SetActive(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(sheet);
        }

        private void Open(RectTransform page) {
            main.gameObject.SetActive(page == main);
            cards.gameObject.SetActive(page == cards);
            LayoutRebuilder.ForceRebuildLayoutImmediate(sheet);
        }

        private void Update() {
            clock += Time.unscaledDeltaTime;
            if (resultLeft >= 0 && !closing) {
                resultLeft -= Time.unscaledDeltaTime;
                if (resultLeft < 0) Close(resultAnswer);
            }

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
            sheet.anchoredPosition = new Vector2(0, rest - (sheet.rect.height + rest + 64) * (1 - shown));
            dim.color              = new Color(colors.Dim.r, colors.Dim.g, colors.Dim.b, colors.Dim.a * shown);
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
            if (shape) {
                Destroy(shape.texture);
                Destroy(shape);
            }

            Send(Answer.Cancel);
        }

        // One page of the sheet: the sheet shows one at a time.
        private RectTransform Page(string name) {
            var page = Child(name, sheet);
            Column(page, 0);
            return page;
        }

        private void Rule(RectTransform parent) => Fixed(Child("Rule", parent), 1).gameObject.AddComponent<Image>().color = colors.Rule;

        // The column of a product's names, taking the row's width its icon and price leave, its lines wrapping there.
        private static RectTransform Names(Transform parent) {
            var names   = Child("Names", parent);
            var element = names.gameObject.AddComponent<LayoutElement>();
            element.minWidth       = 0;
            element.preferredWidth = 1;
            element.flexibleWidth  = 1;
            return names;
        }

        // A laid out row made a button over its whole area.
        private static HorizontalLayoutGroup Tappable(HorizontalLayoutGroup row, Action onClick) {
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition    = Selectable.Transition.None;
            button.navigation    = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onClick());
            return row;
        }

        private static void Link(Transform parent, string text, Color color, Action onClick) {
            var row = Tappable(Row(Child(text, parent), TextAnchor.MiddleCenter, 0), onClick);
            row.padding = new RectOffset(2, 2, 7, 7);
            Label(row.transform, text, 12, FontStyle.Normal, color, TextAnchor.MiddleCenter);
        }

        // The stores' filled buy button: Play's across the sheet, Apple's centred and as wide as its word needs, never under Apple's own width.
        private void Pill(RectTransform parent, string text, float height, int size, FontStyle style, bool fit, Action onClick) {
            var holder = fit ? Row(Child(text + " Row", parent), TextAnchor.MiddleCenter, 0).transform : parent;
            var rect   = Fixed(Child(text, holder), height);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = Rounded(rect, colors.Accent, height / 2);
            var tint = button.colors;
            tint.highlightedColor = new Color(0.94f, 0.94f, 0.94f);
            tint.pressedColor     = new Color(0.8f, 0.8f, 0.8f);
            tint.selectedColor    = Color.white;
            tint.fadeDuration     = 0.06f;
            button.colors         = tint;
            button.navigation     = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onClick());
            var label = Label(rect, text, size, style, colors.OnAccent, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            if (fit) rect.GetComponent<LayoutElement>().preferredWidth = Mathf.Max(103, label.preferredWidth + 48);
        }

        // The X: Apple's on a glass disc, Play's bare.
        private void CloseButton(Transform parent, float size, bool disc) {
            var rect    = Child("Close", parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = element.minWidth = size;
            var hit    = Rounded(rect, disc ? colors.Chip : Color.clear, size / 2);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition    = Selectable.Transition.None;
            button.navigation    = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => Close(Answer.Cancel));
            Bars(rect, size * (disc ? .52f : .5f), 2, disc ? colors.ChipInk : colors.Muted, 45, -45);
        }

        // Play's test card mark: a card, or an hourglass for a slow card, with a ringed tick or cross at its corner.
        private void CardGlyph(Transform parent, int index) {
            var slot    = Child("Card Glyph", parent);
            var element = slot.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth  = element.minWidth = 26;
            element.preferredHeight = 22;

            if (index < 2) {
                var body = Child("Body", slot);
                body.anchorMin        = body.anchorMax = new Vector2(0.5f, 0.5f);
                body.sizeDelta        = new Vector2(20, 16);
                body.anchoredPosition = new Vector2(-2, 1);
                Outline(body, colors.Ink, colors.Surface, 2.5f, 2.2f);
                var stripe = Child("Stripe", body);
                stripe.anchorMin        = new Vector2(0, 1);
                stripe.anchorMax        = Vector2.one;
                stripe.pivot            = new Vector2(0.5f, 1);
                stripe.sizeDelta        = new Vector2(0, 4.5f);
                stripe.anchoredPosition = Vector2.zero;
                Rounded(stripe, colors.Ink, 2.5f);
            } else {
                var glass = Child("Hourglass", slot);
                glass.anchorMin        = glass.anchorMax = new Vector2(0.5f, 0.5f);
                glass.sizeDelta        = new Vector2(13, 20);
                glass.anchoredPosition = new Vector2(-2, 1);
                Bar(glass, new Vector2(-6.5f, 9), 13, 2.6f, 0, colors.Ink);
                Bar(glass, new Vector2(-6.5f, -9), 13, 2.6f, 0, colors.Ink);
                Bars(glass, 20, 2.2f, colors.Ink, 54, -54);
            }

            var ring = Child("Badge", slot);
            ring.anchorMin        = ring.anchorMax = new Vector2(0.5f, 0.5f);
            ring.sizeDelta        = new Vector2(14, 14);
            ring.anchoredPosition = new Vector2(8, -6);
            Rounded(ring, colors.Surface, 7);
            var approves = index is 0 or 2;
            Glyph(ring, approves ? Glyphs.Check : Glyphs.Cross, 11, approves ? colors.Good : colors.Bad, colors.Surface);
        }

        // Play's way into the payment methods: a chevron on a tinted disc.
        private void Chevron(Transform parent) {
            var disc    = Child("Chevron", parent);
            var element = disc.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = element.minWidth = 31;
            Rounded(disc, colors.Chip, 15.5f);
            Bar(disc, new Vector2(3, 0), 7, 2, 135, colors.ChipInk);
            Bar(disc, new Vector2(3, 0), 7, 2, -135, colors.ChipInk);
        }

        // Play's back arrow, drawn from bars as the default font's arrow sits off its line.
        private void BackArrow(Transform parent) {
            var slot    = Child("Arrow", parent);
            var element = slot.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = element.minWidth = 24;
            Bar(slot, new Vector2(-8, 0), 16, 2, 0, colors.Muted);
            Bar(slot, new Vector2(-8.5f, 0), 8, 2, 45, colors.Muted);
            Bar(slot, new Vector2(-8.5f, 0), 8, 2, -45, colors.Muted);
        }

        // The ringed i beside Play's tax line.
        private void InfoGlyph(Transform parent) {
            var slot    = Child("Info", parent);
            var element = slot.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = element.minWidth = 13;
            Outline(slot, colors.Muted, colors.Surface, 6.5f, 1.3f);
            Bar(slot, new Vector2(0, -3.5f), 5, 1.4f, 90, colors.Muted);
            Bar(slot, new Vector2(0, 2.6f), 1.4f, 1.4f, 90, colors.Muted);
        }

        // A filled disc with a tick, a cross or a clock's hands in the background's color, drawn from bars as the default font has no such signs.
        private static void Glyph(RectTransform slot, Glyphs glyph, float size, Color disc, Color ink) {
            var holder = Child(glyph.ToString(), slot);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
            holder.sizeDelta = new Vector2(size, size);
            Rounded(holder, disc, size / 2);
            var thick = Mathf.Max(1.4f, size * .09f);
            switch (glyph) {
                case Glyphs.Check:
                    Bar(holder, new Vector2(-.26f, .02f) * size, size * .24f, thick, -45, ink);
                    Bar(holder, new Vector2(-.1f, -.15f) * size, size * .5f, thick, 48, ink);
                    break;
                case Glyphs.Cross:
                    Bars(holder, size * .44f, thick, ink, 45, -45);
                    break;
                default:
                    Bar(holder, Vector2.zero, size * .26f, thick, 90, ink);
                    Bar(holder, Vector2.zero, size * .2f, thick, 0, ink);
                    break;
            }
        }

        // Bars of one length crossing at the middle, at the given angles.
        private static void Bars(RectTransform parent, float length, float thick, Color color, params float[] angles) {
            foreach (var angle in angles) {
                var bar = Child("Bar", parent);
                bar.anchorMin        = bar.anchorMax = new Vector2(0.5f, 0.5f);
                bar.sizeDelta        = new Vector2(length, thick);
                bar.localEulerAngles = new Vector3(0, 0, angle);
                bar.gameObject.AddComponent<Image>().color = color;
            }
        }

        // A bar from a point, at an angle.
        private static void Bar(RectTransform parent, Vector2 from, float length, float thick, float angle, Color color) {
            var bar = Child("Bar", parent);
            bar.anchorMin        = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot            = new Vector2(0, 0.5f);
            bar.sizeDelta        = new Vector2(length, thick);
            bar.anchoredPosition = from;
            bar.localEulerAngles = new Vector3(0, 0, angle);
            bar.gameObject.AddComponent<Image>().color = color;
        }

        // A rounded outline: the line's color behind, the fill inset by its width.
        private static void Outline(RectTransform rect, Color line, Color fill, float radius, float width) {
            var inner = Backdrop(rect, fill, Mathf.Max(0.5f, radius - width));
            inner.offsetMin = new Vector2(width, width);
            inner.offsetMax = new Vector2(-width, -width);
            Backdrop(rect, line, radius);
        }

        // The game's icon on a rounded tile: in the Editor Player Settings' Default Icon; elsewhere, or without one, the game's initial on Play's green.
        private void Icon(Transform parent, float size, float radius) {
            var tile    = Child("Icon", parent);
            var element = tile.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = size;
            element.minWidth       = size;
            var shape = Rounded(tile, Color.white, radius);
            tile.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var icon = GameIcon();
            if (icon != null) {
                var image = Child("Image", tile).gameObject.AddComponent<RawImage>();
                image.texture       = icon;
                image.raycastTarget = false;
                Stretch(image.rectTransform);
                return;
            }

            shape.color = Hex(0x01875F);
            var name    = Application.productName;
            var initial = Label(tile, name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "", Mathf.RoundToInt(size * .45f), FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Stretch(initial.rectTransform);
        }

        private static Texture GameIcon() {
#if UNITY_EDITOR
            foreach (var icon in UnityEditor.PlayerSettings.GetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, UnityEditor.IconKind.Application)) {
                if (icon != null) return icon;
            }
#endif
            return null;
        }

        // Which store the build buys from: in the Editor, the active build target's.
        private static bool AppleStore() {
#if UNITY_EDITOR
            return UnityEditor.EditorUserBuildSettings.activeBuildTarget is UnityEditor.BuildTarget.iOS;
#else
            return Application.platform is RuntimePlatform.IPhonePlayer;
#endif
        }

        // The device's dark appearance: in the Editor, the Editor's theme, as the pack's other Editor stand-ins take it.
        private static bool DarkAppearance() {
#if UNITY_EDITOR
            return UnityEditor.EditorGUIUtility.isProSkin;
#elif UNITY_IOS
            return RiseOnIapDarkAppearance();
#else
            if (Application.platform is not RuntimePlatform.Android) return false;

            // Configuration.UI_MODE_NIGHT_MASK and UI_MODE_NIGHT_YES.
            using var player        = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity      = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var resources     = activity.Call<AndroidJavaObject>("getResources");
            using var configuration = resources.Call<AndroidJavaObject>("getConfiguration");
            return (configuration.Get<int>("uiMode") & 0x30) == 0x20;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern bool RiseOnIapDarkAppearance();
#endif

        private static Color Hex(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private static RectTransform Child(string name, Transform parent) {
            var rect = (RectTransform) new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Spacer(Transform parent) => Child("Spacer", parent).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

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
            if (!rect.TryGetComponent<VerticalLayoutGroup>(out var column)) column = rect.gameObject.AddComponent<VerticalLayoutGroup>();
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
            rect.SetAsFirstSibling();
            Stretch(rect);
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            if (radius <= 0) {
                rect.gameObject.AddComponent<Image>().color = color;
            } else {
                Rounded(rect, color, radius);
            }

            return rect;
        }

        private static Image Rounded(RectTransform rect, Color color, float radius) {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite                  = rounded ??= CreateRounded(Corner, Corner);
            image.type                    = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = Corner / radius;
            image.color                   = color;
            return image;
        }

        // A white rounded box cut in nine around a one-pixel middle, its top corners of one radius and its bottom ones of another, in pixels: every rounded
        // box of the sheet slices it to its own radius.
        private static Sprite CreateRounded(float top, float bottom) {
            var side   = Mathf.Max(top, bottom);
            var width  = Mathf.CeilToInt(side) * 2 + 1;
            var height = Mathf.CeilToInt(top) + Mathf.CeilToInt(bottom) + 1;
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++) {
                var upper  = y > Mathf.CeilToInt(bottom);
                var radius = upper ? top : bottom;
                var py     = y + 0.5f;
                var dy     = upper ? py - (height - radius) : radius - py;
                for (var x = 0; x < width; x++) {
                    var px    = x + 0.5f;
                    var dx    = Mathf.Max(radius - px, px - (width - radius));
                    var alpha = dx > 0 && dy > 0 ? Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f) : 1;
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte) Mathf.RoundToInt(alpha * 255));
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var border = new Vector4(Mathf.CeilToInt(side), Mathf.CeilToInt(bottom), Mathf.CeilToInt(side), Mathf.CeilToInt(top));
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}