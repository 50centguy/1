using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // The parts dealer's program, run on a physical screen (2077-style device use): opening it glides the view to the
    // intake terminal on the counter (or the bench CRT while working at the bench) and lays the program on that glass
    // as a world-space canvas the mouse works directly; closing glides back. Without a screen in the scene it falls
    // back to a modal overlay. Layout: section rail with the dealer's live portrait, product cards with schematic
    // glyphs, a totals strip and a stamp on accepted orders. The entry point is the system launcher's menu.
    public sealed class RepairPropShopPanel : MonoBehaviour
    {
        sealed class ProductRow
        {
            public ClinicSupplyProduct product;
            public Text stock, quantity, subtotal, compatible;
            public Button minus, plus, refund;
            public GameObject root;
            public Image frame;
        }
        static readonly Color Ink = RepairPropCyberStyle.Ink, Dim = RepairPropCyberStyle.Dim, Amber = RepairPropCyberStyle.Amber;
        static readonly Color Hot = RepairPropCyberStyle.Hot, Rule = RepairPropCyberStyle.AmberDim;
        static readonly Color ButtonBg = new Color(.02f, .015f, .01f, .6f), Selected = new Color(1f, .62f, .16f, .85f);
        static readonly Color RowA = new Color(1f, .62f, .16f, .045f), RowB = new Color(1f, .62f, .16f, .02f);
        RepairPropEconomyController owner;
        Font font, mono, kana;
        GameObject shade;
        RectTransform window, content, stamp;
        Text balance, total, message, context, vendorLine;
        Button purchase;
        Canvas canvas;
        CanvasScaler scaler;
        ClinicDeviceView device;
        RectTransform deviceScreen;
        UnityEngine.Rect screenRect;
        readonly List<ProductRow> rows = new List<ProductRow>();
        readonly List<Button> tabs = new List<Button>();
        int tab, ledgerCount = -1, receiptCount = -1;
        float stampAt = -10;
        public RectTransform Window => window;
        public ScrollRect Scroll { get; private set; }
        /// <summary>The launcher entry that opens this program (the launcher menu must be open to click it).</summary>
        public Button OpenButton => RepairPropLauncher.Current != null ? RepairPropLauncher.Current.ShopEntry : null;
        /// <summary>The camera showing the program (the device view while it runs on a screen), else null.</summary>
        public Camera DeviceCamera => device != null ? device.Camera : null;
        /// <summary>True once the view has settled on the screen (or when running as an overlay).</summary>
        public bool DeviceReady => device == null || (device.Arrived && OnScreen);
        public bool OnScreen => canvas != null && canvas.renderMode == RenderMode.WorldSpace;
        public Button PurchaseButton => purchase;
        /// <summary>Slot on the left rail for a live dealer portrait (filled by the call / NPC runtime when present).</summary>
        public RawImage VendorPortrait { get; private set; }
        public Button QuantityButton(string sku, bool add) => add ? rows.Find(r => r.product.Id == sku)?.plus : rows.Find(r => r.product.Id == sku)?.minus;
        public void Initialize(RepairPropEconomyController controller)
        {
            owner = controller;
            font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            mono = RepairPropCyberStyle.Mono != null ? RepairPropCyberStyle.Mono : font;
            kana = RepairPropCyberStyle.Kana != null ? RepairPropCyberStyle.Kana : font;
            var host = Rect("PartsShopCanvas", transform);
            canvas = host.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 90;
            scaler = host.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 1; scaler.dynamicPixelsPerUnit = 3;
            host.gameObject.AddComponent<GraphicRaycaster>();
            host.gameObject.AddComponent<HudLensExempt>();
            shade = Rect("PartsShopModal", host).gameObject; Stretch((RectTransform)shade.transform);
            shade.AddComponent<Image>().color = new Color(.02f, .012f, .005f, .96f);   // the screen's dark phosphor
            RepairPropCyberStyle.Texture(shade.transform, RepairPropCyberStyle.Scan, new Color(1f, .62f, .16f, .025f), "Scanlines");
            window = Rect("ProcurementTerminal", shade.transform);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
            window.sizeDelta = new Vector2(1060, 620);
            RepairPropCyberStyle.Framed(window.gameObject.AddComponent<Image>(), RepairPropCyberStyle.FieldDeep, Amber);
            RepairPropCyberStyle.Texture(window, RepairPropCyberStyle.Dots, new Color(1f, .62f, .16f, .09f), "DotGrid");
            RepairPropCyberStyle.Texture(window, RepairPropCyberStyle.Dither, new Color(1f, .42f, .10f, .05f), "Dither");
            HudMotion.Boot(window.gameObject);
            HudWindowDrag.Attach(RepairPropCyberStyle.TitleBar(window, "PARTS.EXE // 部品通販  —  NSP-LINE 03", font), window, "shop");

            // Header: dealer name in kana, line code, balance in big mono digits.
            Label(window, "部品通販", 26, 24, -12, 200, 40, Amber, kana);
            Label(window, "PARTS EXCHANGE // 岸壁ジャンク商会 // LINE 03", 11, 24, -50, 520, 18, Dim, kana);
            Label(window, "现金余额 · CR", 12, 640, -10, 300, 18, Dim).alignment = TextAnchor.MiddleRight;
            balance = Label(window, "", 28, 640, -26, 300, 38, Hot, mono); balance.alignment = TextAnchor.MiddleRight;
            Button(window, "×", owner.Close, 996, -18, 40, 36);
            Rule_(window, 24, -78, 1012);
            context = Label(window, "", 13, 240, -86, 796, 22, Dim);

            // Left rail: sections, then the dealer box.
            var names = new[] { "01  采购  BUY", "02  库存  STOCK", "03  账本  LEDGER" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var button = Button(window, names[i], () => ShowTab(index), 24, -114 - i * 50, 200, 42);
                var caption = button.GetComponentInChildren<Text>(); caption.alignment = TextAnchor.MiddleLeft;
                Position(caption.rectTransform, 14, 0, 186, 42);
                tabs.Add(button);
            }
            var dealer = Rect("Dealer", window); Position(dealer, 24, -272, 200, 224);
            RepairPropCyberStyle.Framed(dealer.gameObject.AddComponent<Image>(), new Color(1f, .62f, .16f, .04f), Rule, true);
            var portrait = Rect("Portrait", dealer); Position(portrait, 10, -10, 120, 150);
            VendorPortrait = portrait.gameObject.AddComponent<RawImage>(); VendorPortrait.color = new Color(1, 1, 1, 0); VendorPortrait.raycastTarget = false;
            var placeholder = Label(dealer, "NO\nFEED", 14, 10, -10, 120, 150, RepairPropCyberStyle.AmberFaint, mono); placeholder.alignment = TextAnchor.MiddleCenter;
            placeholder.name = "PortraitPlaceholder";
            Label(dealer, "ジ\nャ\nン\nク\n商\n会", 13, 140, -8, 50, 156, Rule, kana).alignment = TextAnchor.UpperCenter;
            vendorLine = Label(dealer, "“现货，现结，不问来路。”", 12, 10, -166, 180, 50, Dim);

            var viewport = Rect("Viewport", window); Position(viewport, 240, -114, 796, 382);
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .25f);
            viewport.gameObject.AddComponent<RectMask2D>();
            Scroll = viewport.gameObject.AddComponent<ScrollRect>(); Scroll.horizontal = false;
            Scroll.movementType = ScrollRect.MovementType.Clamped; Scroll.scrollSensitivity = 28;
            Scroll.viewport = viewport;
            content = Rect("Rows", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            Scroll.content = content;
            var track = Rect("Scrollbar", viewport); Position(track, 786, 0, 8, 382);
            track.gameObject.AddComponent<Image>().color = RepairPropCyberStyle.AmberFaint;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handle = Rect("Handle", track); Stretch(handle);
            var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = Rule;
            scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
            Scroll.verticalScrollbar = scrollbar; Scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            Rule_(window, 240, -506, 796);
            total = Label(window, "", 18, 240, -516, 540, 28, Ink);
            message = Label(window, "", 13, 240, -550, 540, 50, Dim);
            HudMotion.TypeOn(message, 110);
            purchase = Button(window, "确认采购 // CONFIRM", () => owner.Purchase(), 808, -516, 228, 62);
            purchase.GetComponentInChildren<Text>().fontSize = 17;
            Label(window, "ORD-" + RepairPropCyberStyle.Code(name, 5) + "  ·  CASH ONLY  ·  NO CREDIT", 10, 24, -592, 520, 16, RepairPropCyberStyle.AmberFaint, mono);

            stamp = Rect("Stamp", window); Position(stamp, 560, -470, 300, 80);
            stamp.localRotation = Quaternion.Euler(0, 0, 8);
            RepairPropCyberStyle.Framed(stamp.gameObject.AddComponent<Image>(), new Color(1f, .62f, .16f, .10f), Hot);
            Label(stamp, "受領 // ACCEPTED", 24, 0, -10, 300, 36, Hot, kana).alignment = TextAnchor.MiddleCenter;
            var stampCode = Label(stamp, "", 11, 0, -48, 300, 18, Amber, mono); stampCode.alignment = TextAnchor.MiddleCenter; stampCode.name = "StampCode";
            stamp.gameObject.SetActive(false);

            owner.Changed += Refresh;
            ShowTab(0); Refresh();
        }
        public void ShowTab(int value)
        {
            tab = Mathf.Clamp(value, 0, 2);
            BuildRows();
            Refresh();
        }
        void BuildRows()
        {
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            rows.Clear();
            if (tab == 2)
            {
                var transactions = owner.State.transactions;
                ledgerCount = transactions.Count;
                float y = 0;
                Label(content, "开业资金", 16, 16, -y - 12, 560, 28);
                Label(content, "+" + owner.State.startingCash, 18, 590, -y - 12, 170, 28, Ink, mono).alignment = TextAnchor.MiddleRight;
                y += 50;
                for (int i = transactions.Count - 1; i >= 0; i--)
                {
                    var t = transactions[i];
                    Label(content, "N" + t.night + " · " + RepairPropCyberStyle.Code(t.id, 4), 11, 16, -y - 2, 200, 16, RepairPropCyberStyle.AmberFaint, mono);
                    Label(content, t.label, 15, 16, -y - 16, 560, 26);
                    Label(content, (t.amount >= 0 ? "+" : "") + t.amount, 18, 590, -y - 12, 170, 30, t.amount >= 0 ? RepairPropCyberStyle.Ok : Amber, mono).alignment = TextAnchor.MiddleRight;
                    Rule_(content, 16, -y - 48, 744);
                    y += 52;
                }
                content.sizeDelta = new Vector2(0, Mathf.Max(382, y));
            }
            else
            {
                int i = 0;
                foreach (var p in ClinicSupplies.Products)
                {
                    var root = Rect(p.Id, content); Position(root, 0, -i * 90, 780, 84);
                    var rowFrame = RepairPropCyberStyle.Framed(root.gameObject.AddComponent<Image>(), i % 2 == 0 ? RowA : RowB, Rule);
                    HudMotion.Boot(root.gameObject, .18f + i * .06f);
                    Glyph(root, p.Id, 10, -10, 64);
                    Label(root, p.Name, 18, 88, -6, 250, 26);
                    Label(root, p.Id.ToUpperInvariant() + "  //  " + p.Category, 11, 88, -32, 250, 18, Dim, mono);
                    var row = new ProductRow { product = p, root = root.gameObject, frame = rowFrame };
                    row.compatible = Label(root, p.Compatibility, 12, 88, -54, 300, 22, Dim);
                    row.stock = Label(root, "", 12, 350, -14, 130, 54, Dim, mono);
                    Label(root, p.Price.ToString(), 22, 486, -12, 64, 32, Hot, mono).alignment = TextAnchor.MiddleRight;
                    Label(root, "CR/件", 11, 552, -20, 40, 18, Dim);
                    if (tab == 0)
                    {
                        row.minus = Button(root, "−", () => owner.SetQuantity(p.Id, Quantity(p.Id) - 1), 598, -24, 34, 34);
                        row.quantity = Label(root, "", 18, 634, -24, 40, 34, Ink, mono); row.quantity.alignment = TextAnchor.MiddleCenter;
                        row.plus = Button(root, "+", () => owner.SetQuantity(p.Id, Quantity(p.Id) + 1), 676, -24, 34, 34);
                        row.subtotal = Label(root, "", 16, 712, -24, 60, 34, Ink, mono); row.subtotal.alignment = TextAnchor.MiddleRight;
                    }
                    else row.refund = Button(root, "退回 1 件", () => owner.Refund(p.Id), 616, -22, 154, 40);
                    rows.Add(row); i++;
                }
                content.sizeDelta = new Vector2(0, i * 90);
            }
            Scroll.verticalNormalizedPosition = 1;
        }
        int Quantity(string id) => owner.Cart.TryGetValue(id, out int q) ? q : 0;
        public void Refresh()
        {
            if (owner == null || shade == null || owner.State == null) return;
            Device(owner.IsOpen);
            // Nothing on the glass while the view is still gliding toward the screen.
            shade.SetActive(owner.IsOpen && (device == null || OnScreen));
            if (!owner.IsOpen) { receiptCount = -1; return; }
            if (OnScreen) FitToScreen();
            else
            {
                // Fit the same table inside narrow Game views without changing its text scale or column spacing.
                var canvasRect = (RectTransform)window.parent.parent;
                float scale = Mathf.Min(1, (canvasRect.rect.width - 32) / 1060, (canvasRect.rect.height - 32) / 620);
                window.localScale = Vector3.one * Mathf.Max(.4f, scale);
            }
            balance.text = owner.State.Cash.ToString("N0");
            var recommended = ClinicSupplies.Find(owner.RecommendedSku);
            var trade = owner.State.ActiveTrade;
            context.text = recommended != null && trade != null ? "本单兼容：" + recommended.Name + "　配件 " + recommended.Price +
                " / 维修费 " + trade.income + " / 预计毛利 " + (trade.income - recommended.Price) : "城区配件行 · 即时入库 · 当面结清";
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].targetGraphic.color = tab == i ? Selected : ButtonBg;
                RepairPropCyberStyle.SetFrame(tabs[i], tab == i ? Hot : Rule);
                tabs[i].GetComponentInChildren<Text>().color = tab == i ? Color.black : Ink;
            }
            if (tab == 2 && ledgerCount != owner.State.transactions.Count) BuildRows();
            foreach (var row in rows)
            {
                string sku = row.product.Id;
                int available = ClinicSupplies.Available(owner.State, sku), allocated = ClinicSupplies.Allocated(owner.State, sku);
                row.stock.text = "AVL " + available.ToString("00") + "  INS " + allocated.ToString("00") + "\nOUT " + ClinicSupplies.Consumed(owner.State, sku).ToString("00");
                bool fit = sku == owner.RecommendedSku;
                row.compatible.color = fit ? Hot : Dim;
                row.compatible.text = (fit ? "▶ 本单兼容　" : "") + row.product.Compatibility;
                row.frame.color = fit ? Hot : Rule;
                if (row.quantity != null)
                {
                    int quantity = Quantity(sku);
                    row.quantity.text = quantity.ToString(); row.subtotal.text = (quantity * row.product.Price).ToString("N0");
                    row.minus.interactable = quantity > 0; row.plus.interactable = quantity < ClinicSupplies.MaxPerPurchase;
                }
                if (row.refund != null) row.refund.interactable = available > 0;
            }
            int sum = owner.Cart.Sum(e => ClinicSupplies.Find(e.Key).Price * e.Value);
            bool valid = ClinicSupplies.Quote(owner.State, owner.Cart, out _, out var reason);
            int partsSpend = -owner.State.transactions.Where(t => ClinicSupplies.IsReceipt(t.id)).Sum(t => t.amount);
            int income = owner.State.transactions.Where(t => !ClinicSupplies.IsReceipt(t.id) && t.amount > 0).Sum(t => t.amount);
            total.text = tab == 0 ? "采购合计 " + sum + "　→　采购后余额 " + (owner.State.Cash - sum) :
                "维修入账 " + income + "　配件净支出 " + partsSpend + "　本晚盈余 " + (owner.State.Cash - owner.State.startingCash);
            message.text = !string.IsNullOrEmpty(owner.Message) ? owner.Message : tab == 0 && owner.Cart.Count > 0 ? reason ?? "" :
                "房租目标 " + owner.State.rent + " / 第 " + owner.State.rentDueNight + " 晚到期 · 尚差 " + owner.State.RentShortfall;
            purchase.gameObject.SetActive(tab == 0);
            purchase.interactable = valid;
            purchase.targetGraphic.color = valid ? Selected : ButtonBg;
            purchase.GetComponentInChildren<Text>().color = valid ? Color.black : Dim;
            RepairPropCyberStyle.SetFrame(purchase, valid ? Hot : Rule);
            // A new receipt since the window opened: stamp it.
            int receipts = owner.State.transactions.Count(t => ClinicSupplies.IsReceipt(t.id));
            if (receiptCount >= 0 && receipts > receiptCount)
            {
                stampAt = Time.unscaledTime;
                stamp.Find("StampCode").GetComponent<Text>().text = "RCPT-" + RepairPropCyberStyle.Code(owner.State.transactions.Last(t => ClinicSupplies.IsReceipt(t.id)).id, 6);
            }
            receiptCount = receipts;
        }
        // Puts the program on (or takes it off) a physical screen. The view glides to the screen first; then the screen
        // "boots" and the program is drawn on a camera-space canvas right in front of the device camera, fitted to the
        // screen's projected rectangle, so a tilted glass or a deep bezel can never hide part of it.
        void Device(bool on)
        {
            if (on && device == null && !OnScreen)
            {
                var clinic = owner.Clinic;
                bool bench = clinic != null && clinic.ConsoleOpen;
                var screen = bench ? RepairBenchV2Runtime.Current?.CrtScreen : ClinicZonesV2Runtime.Current?.TerminalScreen;
                var from = clinic == null ? null : bench ? clinic.Station.Inspector.ViewCamera : clinic.Robot.Flow.Rig.Cam;
                if (screen == null || from == null) return;   // overlay fallback
                deviceScreen = screen;
                device = ClinicDeviceView.Begin(from, screen);
                return;
            }
            if (on && device != null && !OnScreen && device.Arrived)
            {
                var view = device.Camera;
                var corners = new Vector3[4];
                deviceScreen.GetWorldCorners(corners);
                var min = new Vector2(float.MaxValue, float.MaxValue); var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var c in corners) { Vector2 p = view.WorldToScreenPoint(c); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                deviceScreen.gameObject.SetActive(false);   // the program replaces the device's own readout
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = view;
                screenRect = UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                Textures(false);
                return;
            }
            if (!on && (device != null || OnScreen))
            {
                if (device != null) device.End();
                device = null;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                var overlayHost = (RectTransform)canvas.transform;
                overlayHost.localScale = Vector3.one; overlayHost.rotation = Quaternion.identity;
                var shadeRect = (RectTransform)shade.transform;
                shadeRect.anchorMin = Vector2.zero; shadeRect.anchorMax = Vector2.one; shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
                if (deviceScreen != null) deviceScreen.gameObject.SetActive(true);
                deviceScreen = null;
                Textures(true);
            }
        }

        // The canvas is a sheet 10 cm in front of the device camera covering its whole view (720 units high, like the
        // overlay), placed here every frame; shade = the screen's glass (its projected rectangle), window fitted inside.
        void FitToScreen()
        {
            var host = (RectTransform)canvas.transform;
            var eye = canvas.worldCamera;
            if (eye != null)
            {
                const float distance = .1f;
                float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
                float height = 2f * distance * Mathf.Tan(eye.fieldOfView * .5f * Mathf.Deg2Rad);
                host.sizeDelta = new Vector2(720f * aspect, 720f);
                host.localScale = Vector3.one * (height / 720f);
                host.SetPositionAndRotation(eye.transform.position + eye.transform.forward * distance, eye.transform.rotation);
            }
            float toCanvas = 720f / Mathf.Max(1, Screen.height);
            // Re-projected every frame: the game view (and so the projection) can change while the program is open.
            var view = canvas.worldCamera;
            if (view != null && deviceScreen != null)
            {
                var corners = new Vector3[4];
                deviceScreen.GetWorldCorners(corners);
                var min = new Vector2(float.MaxValue, float.MaxValue); var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var c in corners) { Vector2 p = view.WorldToScreenPoint(c); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                screenRect = UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
            var shadeRect = (RectTransform)shade.transform;
            shadeRect.anchorMin = shadeRect.anchorMax = Vector2.zero;
            shadeRect.pivot = new Vector2(.5f, .5f);
            shadeRect.anchoredPosition = screenRect.center * toCanvas;
            shadeRect.sizeDelta = screenRect.size * toCanvas;
            window.localScale = Vector3.one * Mathf.Min(shadeRect.sizeDelta.x / 1100f, shadeRect.sizeDelta.y / 660f);
        }

        // The visor's dot / dither fields read as noise on a real phosphor screen: off while the program is on glass.
        void Textures(bool on)
        {
            foreach (var image in canvas.GetComponentsInChildren<Image>(true))
                if (image.name == "Dither" || image.name == "DotGrid" || image.name == "Scanlines") image.enabled = on;
        }

        void Update()
        {
            if (owner != null && owner.IsOpen)
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) owner.Close();
            }
            if (stamp == null) return;
            // The dealer on the line while the terminal is open (live portrait from the NPC studio when present).
            var npcs = owner != null && owner.IsOpen ? ClinicNpcRuntime.Current : null;
            var feed = npcs != null ? npcs.Portrait("Gen") : null;
            VendorPortrait.texture = feed;
            VendorPortrait.color = feed != null ? new Color(1f, .93f, .82f, 1) : new Color(1, 1, 1, 0);
            var placeholder = VendorPortrait.transform.parent.Find("PortraitPlaceholder");
            if (placeholder != null && placeholder.gameObject.activeSelf != (feed == null)) placeholder.gameObject.SetActive(feed == null);
            float age = Time.unscaledTime - stampAt;
            bool show = owner != null && owner.IsOpen && age < 1.6f;
            if (stamp.gameObject.activeSelf != show) stamp.gameObject.SetActive(show);
            if (show) stamp.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, HudMotion.EaseOut(age / .12f));
        }
        // Schematic glyph for a product: board outline, a chip and pins, varied by SKU.
        void Glyph(Transform parent, string sku, float x, float y, float size)
        {
            var box = Rect("Glyph", parent); Position(box, x, y, size, size);
            RepairPropCyberStyle.Framed(box.gameObject.AddComponent<Image>(), new Color(0, 0, 0, .35f), Rule, true);
            int seed = int.Parse(RepairPropCyberStyle.Code(sku, 4));
            float inset = 10, w = size - inset * 2;
            Bar(box, inset, -inset, w, 1); Bar(box, inset, -size + inset, w, 1); Bar(box, inset, -inset, 1, w); Bar(box, size - inset, -inset, 1, w);
            float chip = 14 + seed % 8, cx = inset + 6 + seed % 10, cy = -inset - 6 - (seed / 10) % 10;
            var core = Rect("Chip", box); Position(core, cx, cy, chip, chip);
            var coreImage = core.gameObject.AddComponent<Image>(); coreImage.color = new Color(1f, .62f, .16f, .55f); coreImage.raycastTarget = false;
            for (int i = 0; i < 4; i++) Bar(box, cx + 2 + i * (chip - 4) / 3f, cy + 4, 1, 4);
            for (int i = 0; i < 3 + seed % 3; i++) Bar(box, size - inset - 4, -inset - 6 - i * 7, -(8 + (seed >> i) % 12), 1);
            Bar(box, inset + 4, -size + inset + 10, 6 + seed % 20, 1);
        }
        void Bar(Transform parent, float x, float y, float width, float height)
        {
            var r = Rect("Line", parent);
            if (width < 0) { x += width; width = -width; }
            Position(r, x, y, width, height);
            var image = r.gameObject.AddComponent<Image>(); image.color = Amber; image.raycastTarget = false;
        }
        void Rule_(Transform parent, float x, float y, float width) => Bar(parent, x, y, width, 1);
        RectTransform Rect(string name, Transform parent)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); return r;
        }
        static void Stretch(RectTransform r)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        static void Position(RectTransform r, float x, float y, float width, float height)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(width, height);
        }
        Text Label(Transform parent, string text, int size, float x, float y, float width, float height, Color? color = null, Font face = null)
        {
            var r = Rect("Label", parent); Position(r, x, y, width, height);
            var label = r.gameObject.AddComponent<Text>(); label.font = face != null ? face : font; label.fontSize = size; label.color = color ?? Ink;
            label.text = text; label.raycastTarget = false; label.supportRichText = false; label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
        Button Button(Transform parent, string text, Action action, float x, float y, float width, float height)
        {
            var r = Rect(text, parent); Position(r, x, y, width, height);
            var image = r.gameObject.AddComponent<Image>();
            RepairPropCyberStyle.Framed(image, ButtonBg, Rule);
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1.25f, 1.15f, 1f, 1); colors.disabledColor = new Color(.7f, .7f, .7f, .6f); button.colors = colors;
            button.onClick.AddListener(() => action());
            var label = Label(r, text, 16, 0, 0, width, height); label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        void OnDestroy()
        {
            if (owner != null) owner.Changed -= Refresh;
            if (device != null) Destroy(device.gameObject);
            if (deviceScreen != null) deviceScreen.gameObject.SetActive(true);
        }
    }
}
