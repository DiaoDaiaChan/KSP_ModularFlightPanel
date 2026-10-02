using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Workbench
{
    /// <summary>
    /// 现代航电工作台可复用交互微控件套件 (WorkbenchControls)
    /// 包含：现代暗晶卡片、动态辉光按钮、iOS/Fluent 质感平滑开关、交互滑块、胶囊徽标与文本框。
    /// </summary>
    public static class WorkbenchControls
    {
        public static Font MainFont => UIFactory.GetActiveFont();

        #region Modern Button

        /// <summary>
        /// 创建带有动态 GPU 悬浮辉光与点击反馈的现代化航电按钮
        /// </summary>
        public static GameObject CreateButton(Transform parent, string name, string labelText, Vector2 size, Action onClick, bool isPrimary = false, int fontSize = 13)
        {
            GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(ModernButtonEffect));
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            if (size.x > 0 || size.y > 0)
            {
                var le = btnObj.AddComponent<LayoutElement>();
                if (size.x > 0) { le.preferredWidth = size.x; le.minWidth = size.x; }
                if (size.y > 0) { le.preferredHeight = size.y; le.minHeight = size.y; }
            }

            Image img = btnObj.GetComponent<Image>();
            img.type = Image.Type.Simple;
            img.material = WorkbenchStyleEngine.GetButtonMaterial(isPrimary, false);
            img.color = isPrimary ? WorkbenchStyleEngine.ColorBtnPrimaryBg : WorkbenchStyleEngine.ColorBtnSecondaryBg;

            Button btn = btnObj.GetComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }

            // 文本子节点
            GameObject txtObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtObj.transform.SetParent(btnObj.transform, false);

            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;
            txtRt.offsetMin = new Vector2(4f, 2f);
            txtRt.offsetMax = new Vector2(-4f, -2f);

            Text txt = txtObj.GetComponent<Text>();
            txt.font = MainFont;
            txt.fontSize = fontSize;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = isPrimary ? WorkbenchStyleEngine.ColorTextPrimary : WorkbenchStyleEngine.ColorTextAccent;
            txt.material = WorkbenchStyleEngine.GetCrispTextMaterial();
            txt.text = labelText;
            txt.raycastTarget = false;

            ModernButtonEffect effect = btnObj.GetComponent<ModernButtonEffect>();
            effect.IsPrimary = isPrimary;
            effect.ButtonImage = img;
            effect.LabelText = txt;

            return btnObj;
        }

        #endregion

        #region Modern Card

        /// <summary>
        /// 创建标准暗晶磨砂玻璃卡片容器
        /// </summary>
        public static GameObject CreateCard(Transform parent, string name, Vector2 size, bool isInteractive = false)
        {
            GameObject cardObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            cardObj.transform.SetParent(parent, false);

            RectTransform rt = cardObj.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            if (size.x > 0 || size.y > 0)
            {
                var le = cardObj.AddComponent<LayoutElement>();
                if (size.x > 0) { le.preferredWidth = size.x; le.minWidth = size.x; }
                if (size.y > 0) { le.preferredHeight = size.y; le.minHeight = size.y; }
            }

            Image img = cardObj.GetComponent<Image>();
            img.material = WorkbenchStyleEngine.GetCardMaterial(false);
            img.color = WorkbenchStyleEngine.ColorCardBg;

            if (isInteractive)
            {
                var hover = cardObj.AddComponent<ModernHoverEffect>();
                hover.CardImage = img;
            }

            return cardObj;
        }

        #endregion

        #region Modern Pill Badge

        /// <summary>
        /// 创建带有强调色或状态色的紧凑胶囊药丸徽章
        /// </summary>
        public static GameObject CreatePill(Transform parent, string name, string labelText, Color accentColor, int fontSize = 11)
        {
            GameObject pillObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            pillObj.transform.SetParent(parent, false);

            float pillWidth = Mathf.Max(48f, labelText.Length * 8f + 20f);
            RectTransform rt = pillObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(pillWidth, 22f);

            var le = pillObj.AddComponent<LayoutElement>();
            le.preferredWidth = pillWidth;
            le.minWidth = pillWidth;
            le.preferredHeight = 22f;
            le.minHeight = 22f;
            le.flexibleWidth = 0f;

            Image img = pillObj.GetComponent<Image>();
            img.material = WorkbenchStyleEngine.GetPillDockMaterial(true);
            img.color = WorkbenchStyleEngine.ColorPillDarkBg;

            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtObj.transform.SetParent(pillObj.transform, false);

            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;

            Text txt = txtObj.GetComponent<Text>();
            txt.font = MainFont;
            txt.fontSize = fontSize;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = accentColor;
            txt.material = WorkbenchStyleEngine.GetCrispTextMaterial();
            txt.text = labelText;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.raycastTarget = false;

            return pillObj;
        }

        #endregion

        #region Modern Toggle Switch

        /// <summary>
        /// 创建现代平滑滑动开关 (Track + Sliding Thumb)
        /// </summary>
        public static GameObject CreateToggle(Transform parent, string name, string title, bool initialValue, Action<bool> onValueChanged)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);

            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(240f, 26f);

            var rootLe = root.AddComponent<LayoutElement>();
            rootLe.preferredHeight = 28f;
            rootLe.minHeight = 28f;

            // 标题文本
            GameObject titleObj = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            titleObj.transform.SetParent(root.transform, false);
            RectTransform titleRt = titleObj.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(0.7f, 1f);
            titleRt.sizeDelta = Vector2.zero;
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;

            Text titleTxt = titleObj.GetComponent<Text>();
            titleTxt.font = MainFont;
            titleTxt.fontSize = 12;
            titleTxt.alignment = TextAnchor.MiddleLeft;
            titleTxt.color = WorkbenchStyleEngine.ColorTextPrimary;
            titleTxt.text = title;
            titleTxt.raycastTarget = false;

            // 开关轨道 (Track)
            GameObject trackObj = new GameObject("Track", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            trackObj.transform.SetParent(root.transform, false);
            RectTransform trackRt = trackObj.GetComponent<RectTransform>();
            trackRt.anchorMin = new Vector2(1f, 0.5f);
            trackRt.anchorMax = new Vector2(1f, 0.5f);
            trackRt.anchoredPosition = new Vector2(-22f, 0f);
            trackRt.sizeDelta = new Vector2(44f, 20f);

            Image trackImg = trackObj.GetComponent<Image>();
            trackImg.material = WorkbenchStyleEngine.GetPillDockMaterial(initialValue);
            trackImg.color = initialValue ? WorkbenchStyleEngine.ColorPillAccentBg : WorkbenchStyleEngine.ColorPillDarkBg;

            // 开关圆形滑块 (Thumb)
            GameObject thumbObj = new GameObject("Thumb", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            thumbObj.transform.SetParent(trackObj.transform, false);
            RectTransform thumbRt = thumbObj.GetComponent<RectTransform>();
            thumbRt.sizeDelta = new Vector2(16f, 16f);
            thumbRt.anchoredPosition = new Vector2(initialValue ? 12f : -12f, 0f);

            Image thumbImg = thumbObj.GetComponent<Image>();
            thumbImg.color = WorkbenchStyleEngine.ColorTextPrimary;
            thumbImg.raycastTarget = false;

            Button btn = trackObj.GetComponent<Button>();
            bool curVal = initialValue;
            btn.onClick.AddListener(() =>
            {
                curVal = !curVal;
                thumbRt.anchoredPosition = new Vector2(curVal ? 12f : -12f, 0f);
                trackImg.material = WorkbenchStyleEngine.GetPillDockMaterial(curVal);
                trackImg.color = curVal ? WorkbenchStyleEngine.ColorPillAccentBg : WorkbenchStyleEngine.ColorPillDarkBg;
                onValueChanged?.Invoke(curVal);
            });

            return root;
        }

        #endregion

        #region Modern Slider

        /// <summary>
        /// 创建带有实时数值反馈的航电滑块组件
        /// </summary>
        public static GameObject CreateSlider(Transform parent, string name, string label, float min, float max, float current, Action<float> onValueChanged, string format = "F2")
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);

            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(300f, 32f);

            var rootLe = root.AddComponent<LayoutElement>();
            rootLe.preferredHeight = 32f;
            rootLe.minHeight = 32f;

            // 标签
            GameObject lblObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            lblObj.transform.SetParent(root.transform, false);
            RectTransform lblRt = lblObj.GetComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0f, 0.5f);
            lblRt.anchorMax = new Vector2(0f, 0.5f);
            lblRt.anchoredPosition = new Vector2(40f, 0f);
            lblRt.sizeDelta = new Vector2(80f, 24f);

            Text lblTxt = lblObj.GetComponent<Text>();
            lblTxt.font = MainFont;
            lblTxt.fontSize = 12;
            lblTxt.alignment = TextAnchor.MiddleLeft;
            lblTxt.color = WorkbenchStyleEngine.ColorTextAccent;
            lblTxt.text = label;
            lblTxt.raycastTarget = false;

            // 数值显示
            GameObject valObj = new GameObject("Value", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            valObj.transform.SetParent(root.transform, false);
            RectTransform valRt = valObj.GetComponent<RectTransform>();
            valRt.anchorMin = new Vector2(1f, 0.5f);
            valRt.anchorMax = new Vector2(1f, 0.5f);
            valRt.anchoredPosition = new Vector2(-25f, 0f);
            valRt.sizeDelta = new Vector2(50f, 24f);

            Text valTxt = valObj.GetComponent<Text>();
            valTxt.font = MainFont;
            valTxt.fontSize = 12;
            valTxt.alignment = TextAnchor.MiddleRight;
            valTxt.color = WorkbenchStyleEngine.ColorTextPrimary;
            valTxt.text = current.ToString(format);
            valTxt.raycastTarget = false;

            // 滑条组件 (Slider)
            GameObject sldObj = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sldObj.transform.SetParent(root.transform, false);
            RectTransform sldRt = sldObj.GetComponent<RectTransform>();
            sldRt.anchorMin = new Vector2(0f, 0.5f);
            sldRt.anchorMax = new Vector2(1f, 0.5f);
            sldRt.offsetMin = new Vector2(85f, -8f);
            sldRt.offsetMax = new Vector2(-55f, 8f);

            Slider slider = sldObj.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = current;

            // 背景槽
            GameObject bgObj = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgObj.transform.SetParent(sldObj.transform, false);
            RectTransform bgRt = bgObj.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            Image bgImg = bgObj.GetComponent<Image>();
            bgImg.color = WorkbenchStyleEngine.ColorCardBg;

            // 填充槽
            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sldObj.transform, false);
            RectTransform fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.sizeDelta = Vector2.zero;

            GameObject fillObj = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillObj.transform.SetParent(fillArea.transform, false);
            RectTransform fillRt = fillObj.GetComponent<RectTransform>();
            fillRt.sizeDelta = Vector2.zero;
            Image fillImg = fillObj.GetComponent<Image>();
            fillImg.color = WorkbenchStyleEngine.ColorAccentPrimary;
            slider.fillRect = fillRt;

            slider.onValueChanged.AddListener((v) =>
            {
                valTxt.text = v.ToString(format);
                onValueChanged?.Invoke(v);
            });

            return root;
        }

        #endregion

        #region Modern Text Field

        /// <summary>
        /// 创建带有高亮聚焦与提示文字的输入框
        /// </summary>
        public static GameObject CreateTextField(Transform parent, string name, string initialText, string placeholder, Vector2 size, Action<string> onEndEdit)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
            root.transform.SetParent(parent, false);

            RectTransform rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            if (size.x > 0 || size.y > 0)
            {
                var le = root.AddComponent<LayoutElement>();
                if (size.x > 0) { le.preferredWidth = size.x; le.minWidth = size.x; }
                if (size.y > 0) { le.preferredHeight = size.y; le.minHeight = size.y; }
            }

            Image bg = root.GetComponent<Image>();
            bg.material = WorkbenchStyleEngine.GetCardMaterial(false);
            bg.color = WorkbenchStyleEngine.ColorCardBg;

            InputField input = root.GetComponent<InputField>();

            // 占位符
            GameObject phObj = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            phObj.transform.SetParent(root.transform, false);
            RectTransform phRt = phObj.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(8f, 2f);
            phRt.offsetMax = new Vector2(-8f, -2f);

            Text phTxt = phObj.GetComponent<Text>();
            phTxt.font = MainFont;
            phTxt.fontSize = 12;
            phTxt.fontStyle = FontStyle.Italic;
            phTxt.color = WorkbenchStyleEngine.ColorTextMuted;
            phTxt.text = placeholder;
            phTxt.alignment = TextAnchor.MiddleLeft;

            // 文本节点
            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtObj.transform.SetParent(root.transform, false);
            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(8f, 2f);
            txtRt.offsetMax = new Vector2(-8f, -2f);

            Text txt = txtObj.GetComponent<Text>();
            txt.font = MainFont;
            txt.fontSize = 12;
            txt.color = WorkbenchStyleEngine.ColorTextPrimary;
            txt.alignment = TextAnchor.MiddleLeft;

            input.textComponent = txt;
            input.placeholder = phTxt;
            input.text = initialText;

            if (onEndEdit != null)
            {
                input.onEndEdit.AddListener((s) => onEndEdit(s));
            }

            return root;
        }

        #endregion

        #region Modern ScrollView

        /// <summary>
        /// 创建平滑滚动视口，返回 content 容器的 RectTransform
        /// </summary>
        public static RectTransform CreateScrollView(Transform parent, string name, Vector2 size, out GameObject scrollRoot)
        {
            scrollRoot = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(ScrollRect), typeof(Image), typeof(Mask));
            scrollRoot.transform.SetParent(parent, false);

            RectTransform rootRt = scrollRoot.GetComponent<RectTransform>();
            rootRt.sizeDelta = size;

            if (size.x > 0 || size.y > 0)
            {
                var le = scrollRoot.AddComponent<LayoutElement>();
                if (size.x > 0) { le.preferredWidth = size.x; le.minWidth = size.x; }
                if (size.y > 0) { le.preferredHeight = size.y; le.minHeight = size.y; }
            }

            Image rootImg = scrollRoot.GetComponent<Image>();
            rootImg.color = new Color(0f, 0f, 0f, 0.05f); // 微透明遮罩背景

            Mask mask = scrollRoot.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            ScrollRect sr = scrollRoot.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 25f;

            // Content
            GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObj.transform.SetParent(scrollRoot.transform, false);

            RectTransform contentRt = contentObj.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup vlg = contentObj.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(6, 6, 6, 6);

            ContentSizeFitter csf = contentObj.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.content = contentRt;

            return contentRt;
        }

        #endregion
    }

    #region Helper Hover / Click Feedback Components

    public class ModernButtonEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public bool IsPrimary;
        public Image ButtonImage;
        public Text LabelText;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (ButtonImage != null)
            {
                ButtonImage.material = WorkbenchStyleEngine.GetButtonMaterial(IsPrimary, true);
                ButtonImage.color = WorkbenchStyleEngine.ColorBtnHoverBg;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (ButtonImage != null)
            {
                ButtonImage.material = WorkbenchStyleEngine.GetButtonMaterial(IsPrimary, false);
                ButtonImage.color = IsPrimary ? WorkbenchStyleEngine.ColorBtnPrimaryBg : WorkbenchStyleEngine.ColorBtnSecondaryBg;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            transform.localScale = new Vector3(0.97f, 0.97f, 1f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            transform.localScale = Vector3.one;
        }
    }

    public class ModernHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image CardImage;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (CardImage != null)
            {
                CardImage.material = WorkbenchStyleEngine.GetCardMaterial(true);
                CardImage.color = WorkbenchStyleEngine.ColorCardBgHover;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (CardImage != null)
            {
                CardImage.material = WorkbenchStyleEngine.GetCardMaterial(false);
                CardImage.color = WorkbenchStyleEngine.ColorCardBg;
            }
        }
    }

    #endregion
}
