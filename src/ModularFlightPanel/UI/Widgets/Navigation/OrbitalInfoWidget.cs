using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 轨道参数卡片 (Orbital Info Widget)
    /// 实时呈现远拱点 (AP) 与近拱点 (PE) 距离与倒计时。
    /// 100% 通配符数据驱动，支持自定义模板，统一样式管道。
    /// </summary>
    [FlightWidget("orbital_info", "orbit", "orbital", Category = WidgetCategory.Navigation, DisplayName = "ORBITAL 轨道动力学面板", Description = "轨道力学四项精简读数面板：远地点 (AP)、近地点 (PE)、到达时间与轨道偏心率。", DefaultWidgetId = "core.orbital_info", DefaultX = 0f, DefaultY = 180f, IsSingleton = true, ExactIds = new[] { "core.orbital_info" })]
    public class OrbitalInfoWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private Text _titleText;
        private Text _apText;
        private Text _peText;
        private Image _bgImage;
        private Outline _outline;

        private string _titleTemplate = "ORBITAL PARAMETERS";
        private string _apTemplate = "AP {AP:DIST} in T-{TAP}";
        private string _peTemplate = "PE {PE:DIST} in T-{TPE}";

        private string _lastTitleText = string.Empty;
        private string _lastApText = string.Empty;
        private string _lastPeText = string.Empty;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            Vector2 panelSize = new Vector2(260f * s, 54f * s);
            RectTransform.sizeDelta = panelSize;

            ParseCustomTemplate(config);

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = Color.clear;

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectDistance = new Vector2(1.5f * s, 1.5f * s);
            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _outline.effectColor, s);

            int infoFontSize = Mathf.RoundToInt(11f * s);
            int titleFontSize = Mathf.RoundToInt(9f * s);

            _titleText = UIFactory.CreateText(transform, "Title_Text", _titleTemplate, titleFontSize, TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform titRt = _titleText.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(240f * s, 14f * s);
            titRt.anchoredPosition = new Vector2(0f, 16f * s);

            _apText = UIFactory.CreateText(transform, "AP_Text", "AP --- in T---", infoFontSize, TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform apRt = _apText.GetComponent<RectTransform>();
            apRt.sizeDelta = new Vector2(240f * s, 16f * s);
            apRt.anchoredPosition = new Vector2(0f, 0f);

            _peText = UIFactory.CreateText(transform, "PE_Text", "PE --- in T---", infoFontSize, TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform peRt = _peText.GetComponent<RectTransform>();
            peRt.sizeDelta = new Vector2(240f * s, 16f * s);
            peRt.anchoredPosition = new Vector2(0f, -16f * s);

            // 注册子控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "卡片底板", gameObject, (t) => ApplyCard(_bgImage, _outline, CardStyleRole.Normal, t)));
            this.Controls.Register(new WidgetReadoutControl("title", "标题", _titleText != null ? _titleText.gameObject : null, null, _titleText, TextStyleRole.Label));
            this.Controls.Register(new WidgetReadoutControl("ap_readout", "远地点读数", _apText != null ? _apText.gameObject : null, _apText, null, TextStyleRole.PrimaryValue));
            this.Controls.Register(new WidgetReadoutControl("pe_readout", "近地点读数", _peText != null ? _peText.gameObject : null, _peText, null, TextStyleRole.PrimaryValue));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void ParseCustomTemplate(WidgetConfig config)
        {
            if (config != null && !string.IsNullOrEmpty(config.DisplayName))
            {
                _titleTemplate = config.DisplayName.ToUpperInvariant();
            }

            if (string.IsNullOrEmpty(config?.CustomTemplate)) return;

            var pairs = config.CustomTemplate.Split(';');
            foreach (var p in pairs)
            {
                var kv = p.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                switch (k)
                {
                    case "TITLE": _titleTemplate = v; break;
                    case "AP":
                    case "AP_TEMPLATE": _apTemplate = v; break;
                    case "PE":
                    case "PE_TEMPLATE": _peTemplate = v; break;
                }
            }
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            string newTitle = TelemetryTokenEngine.Evaluate(_titleTemplate, telemetry);
            if (newTitle != _lastTitleText)
            {
                _lastTitleText = newTitle;
                if (_titleText != null) _titleText.text = newTitle;
            }

            if (_apText != null)
            {
                string newAp = TelemetryTokenEngine.Evaluate(_apTemplate, telemetry);
                if (newAp != _lastApText)
                {
                    _lastApText = newAp;
                    _apText.text = newAp;
                }
            }
            if (_peText != null)
            {
                string newPe = TelemetryTokenEngine.Evaluate(_peTemplate, telemetry);
                if (newPe != _lastPeText)
                {
                    _lastPeText = newPe;
                    _peText.text = newPe;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);
            if (_titleText != null) ApplyText(_titleText, TextStyleRole.Label, theme);
            if (_apText != null) ApplyText(_apText, TextStyleRole.PrimaryValue, theme);
            if (_peText != null) ApplyText(_peText, TextStyleRole.PrimaryValue, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
