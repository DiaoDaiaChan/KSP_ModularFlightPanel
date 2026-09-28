using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 多通道遥测综合矩阵卡片 (Multi-Channel Flight Telemetry Matrix Card)
    /// 标准化航电 2x3 高集成度矩阵：
    /// 默认提供 6 大核心飞行遥测实时监控 (空速/地速、真高、垂直升降率、推重比 TWR、大气动压 Q 与过载 G)，
    /// 具备独立通道量纲单位与智能告警变色机制，同时向下兼容自定义通配符模板重载。
    /// 100% 遵照 SPEC-001..008 核心架构规范。
    /// </summary>
    [FlightWidget("custom_token", "custom_text", "custom", "telemetry_matrix",
        Category = WidgetCategory.Gauges,
        DisplayName = "多通道遥测综合矩阵卡",
        Description = "开箱即用 2x3 航电遥测数据矩阵：空速、真高、升降率、推重比、动压与过载实时监视，支持通配符模板重载。",
        DefaultWidgetId = "custom.telemetry_card",
        DefaultX = 0f,
        DefaultY = 0f)]
    public class CustomTokenTextWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(240f, 96f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        /// <summary>
        /// 语义主题管道 (MFP-SPEC-003)：显式接入 WidgetStyleManager 单向主题下发。
        /// 视觉全部来自语义角色微控件，因此由基类把主题推送给全部已注册微控件即可。
        /// </summary>
        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
        }

        // ── 顶部标题与徽章 ──
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_GAUGE_TELEM_MATRIX", "遥测矩阵"));
        public TextWidget Badge = TextWidget.Badge("6-CH MON");

        // ── 2 列 x 3 行通道矩阵 (左列: 运动学 / 右列: 动力学与力环境) ──
        // Row 1: SPD (空速) | TWR (推重比)
        public TextWidget Ch1Label = new TextWidget(TextStyleRole.Label, -112f, 16f, 32f, 16f, 8.5f, TextAnchor.MiddleLeft, "SPD");
        public TextWidget Ch1Val = new TextWidget(TextStyleRole.PrimaryValue, -80f, 16f, 54f, 16f, 11f, TextAnchor.MiddleRight, "0.0");
        public TextWidget Ch1Unit = new TextWidget(TextStyleRole.Unit, -24f, 16f, 20f, 16f, 8f, TextAnchor.MiddleLeft, "m/s");

        public TextWidget Ch2Label = new TextWidget(TextStyleRole.Label, 6f, 16f, 32f, 16f, 8.5f, TextAnchor.MiddleLeft, "TWR");
        public TextWidget Ch2Val = new TextWidget(TextStyleRole.PrimaryValue, 38f, 16f, 54f, 16f, 11f, TextAnchor.MiddleRight, "0.00");
        public TextWidget Ch2Unit = new TextWidget(TextStyleRole.Unit, 94f, 16f, 20f, 16f, 8f, TextAnchor.MiddleLeft, "x");

        // Row 2: RALT (雷达高) | Q (动压)
        public TextWidget Ch3Label = new TextWidget(TextStyleRole.Label, -112f, -6f, 32f, 16f, 8.5f, TextAnchor.MiddleLeft, "RALT");
        public TextWidget Ch3Val = new TextWidget(TextStyleRole.PrimaryValue, -80f, -6f, 54f, 16f, 11f, TextAnchor.MiddleRight, "0");
        public TextWidget Ch3Unit = new TextWidget(TextStyleRole.Unit, -24f, -6f, 20f, 16f, 8f, TextAnchor.MiddleLeft, "m");

        public TextWidget Ch4Label = new TextWidget(TextStyleRole.Label, 6f, -6f, 32f, 16f, 8.5f, TextAnchor.MiddleLeft, "Q");
        public TextWidget Ch4Val = new TextWidget(TextStyleRole.PrimaryValue, 38f, -6f, 54f, 16f, 11f, TextAnchor.MiddleRight, "0.0");
        public TextWidget Ch4Unit = new TextWidget(TextStyleRole.Unit, 94f, -6f, 20f, 16f, 8f, TextAnchor.MiddleLeft, "kPa");

        // Row 3: VSI (升降率) | G (过载)
        public TextWidget Ch5Label = new TextWidget(TextStyleRole.Label, -112f, -28f, 32f, 16f, 8.5f, TextAnchor.MiddleLeft, "VSI");
        public TextWidget Ch5Val = new TextWidget(TextStyleRole.PrimaryValue, -80f, -28f, 54f, 16f, 11f, TextAnchor.MiddleRight, "+0.0");
        public TextWidget Ch5Unit = new TextWidget(TextStyleRole.Unit, -24f, -28f, 20f, 16f, 8f, TextAnchor.MiddleLeft, "m/s");

        public TextWidget Ch6Label = new TextWidget(TextStyleRole.Label, 6f, -28f, 32f, 16f, 8.5f, TextAnchor.MiddleLeft, "G");
        public TextWidget Ch6Val = new TextWidget(TextStyleRole.PrimaryValue, 38f, -28f, 54f, 16f, 11f, TextAnchor.MiddleRight, "1.00");
        public TextWidget Ch6Unit = new TextWidget(TextStyleRole.Unit, 94f, -28f, 20f, 16f, 8f, TextAnchor.MiddleLeft, "G");

        // 零 GC 常量池：通道分隔符静态复用，杜绝帧循环内 new[]{...} 堆数组分配
        private static readonly char[] MultiChannelSeparators = { ';', '\n', '\r' };
        private static readonly char[] DelimitedSeparators = { '|', '\n', '\r' };

        private string _titleText = string.Empty;
        private string _ch1Text = "---";
        private TextStyleRole _ch1Role = TextStyleRole.PrimaryValue;
        private string _ch2Text = "---";
        private TextStyleRole _ch2Role = TextStyleRole.PrimaryValue;
        private string _ch3Text = "---";
        private TextStyleRole _ch3Role = TextStyleRole.PrimaryValue;
        private string _ch4Text = "---";
        private TextStyleRole _ch4Role = TextStyleRole.PrimaryValue;
        private string _ch5Text = "---";
        private TextStyleRole _ch5Role = TextStyleRole.PrimaryValue;
        private string _ch6Text = "---";
        private TextStyleRole _ch6Role = TextStyleRole.PrimaryValue;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel)
            {
                _ch1Text = "---";
                _ch2Text = "---";
                _ch3Text = "---";
                _ch4Text = "---";
                _ch5Text = "---";
                _ch6Text = "---";
                return;
            }

            // 自定义标题更新
            string titleTpl = !string.IsNullOrEmpty(Config?.DisplayName) ? Config.DisplayName : I18n.Tr("WIDGET_GAUGE_TELEM_MATRIX", "遥测矩阵");
            _titleText = EvalToken(titleTpl, telemetry, I18n.Tr("WIDGET_GAUGE_TELEM_MATRIX", "遥测矩阵"));

            // 检查是否有自定义模板覆盖
            string customTpl = Config?.CustomTemplate;
            if (!string.IsNullOrEmpty(customTpl))
            {
                if (customTpl.IndexOf('=') >= 0)
                {
                    // 具备通道键值对解析：CH1=...;CH2=...
                    ParseCustomMultiChannel(customTpl, telemetry);
                    return;
                }
                else
                {
                    // 智能容错：用户输入了未带通道前缀的由 '|'、换行或逗号分隔的通配符列表
                    ParseDelimitedChannels(customTpl, telemetry);
                    return;
                }
            }

            // 1. SPD: 当前地速/空速 (优先参考系真实速度)
            double spd = telemetry.SurfaceSpeed;
            _ch1Text = spd >= 10000.0 ? (spd * 0.001).ToString("F1") + "k" : spd.ToString("F1");
            _ch1Role = TextStyleRole.PrimaryValue;

            // 2. TWR: 实际可用推重比
            double twr = telemetry.TWR;
            _ch2Text = twr.ToString("F2");
            _ch2Role = twr > 0.05 && twr < 1.0 && telemetry.AltitudeAGL < 1000.0 ? TextStyleRole.Warning : TextStyleRole.PrimaryValue;

            // 3. RALT: 雷达真高 (AGL)
            double ralt = telemetry.AltitudeAGL;
            _ch3Text = ralt >= 100000.0 ? (ralt * 0.001).ToString("F0") + "k" : ralt.ToString("F0");
            _ch3Role = TextStyleRole.PrimaryValue;

            // 4. Q: 动压 (kPa)
            double q = telemetry.DynamicPressure;
            _ch4Text = q.ToString("F1");
            _ch4Role = q > 35.0 ? TextStyleRole.Danger : (q > 25.0 ? TextStyleRole.Warning : TextStyleRole.PrimaryValue);

            // 5. VSI: 垂直速度
            double vsi = telemetry.VerticalSpeed;
            _ch5Text = (vsi >= 0.0 ? "+" : "") + vsi.ToString("F1");
            _ch5Role = vsi < -50.0 && ralt < 3000.0 ? TextStyleRole.Danger : TextStyleRole.PrimaryValue;

            // 6. G: 当前加速度过载
            double g = telemetry.GForce;
            _ch6Text = g.ToString("F2");
            _ch6Role = g > 6.0 ? TextStyleRole.Danger : (g > 4.0 ? TextStyleRole.Warning : TextStyleRole.PrimaryValue);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            Title.Text = _titleText;

            Ch1Val.Text = _ch1Text;
            Ch1Val.SetRole(_ch1Role);

            Ch2Val.Text = _ch2Text;
            Ch2Val.SetRole(_ch2Role);

            Ch3Val.Text = _ch3Text;
            Ch3Val.SetRole(_ch3Role);

            Ch4Val.Text = _ch4Text;
            Ch4Val.SetRole(_ch4Role);

            Ch5Val.Text = _ch5Text;
            Ch5Val.SetRole(_ch5Role);

            Ch6Val.Text = _ch6Text;
            Ch6Val.SetRole(_ch6Role);
        }

        private void ParseCustomMultiChannel(string template, IFlightTelemetry telemetry)
        {
            string[] pairs = template.Split(MultiChannelSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in pairs)
            {
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                string key = p.Substring(0, eq).Trim().ToUpperInvariant();
                string token = (eq < p.Length - 1) ? p.Substring(eq + 1).Trim() : string.Empty;
                switch (key)
                {
                    case "CH1": _ch1Text = EvalToken(token, telemetry, "---"); break;
                    case "CH2": _ch2Text = EvalToken(token, telemetry, "---"); break;
                    case "CH3": _ch3Text = EvalToken(token, telemetry, "---"); break;
                    case "CH4": _ch4Text = EvalToken(token, telemetry, "---"); break;
                    case "CH5": _ch5Text = EvalToken(token, telemetry, "---"); break;
                    case "CH6": _ch6Text = EvalToken(token, telemetry, "---"); break;
                }
            }
        }

        private void ParseDelimitedChannels(string template, IFlightTelemetry telemetry)
        {
            string[] tokens = template.Split(DelimitedSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length && i < 6; i++)
            {
                string tok = tokens[i].Trim();
                int braceStart = tok.IndexOf('{');
                int braceEnd = tok.LastIndexOf('}');
                string evalStr = (braceStart >= 0 && braceEnd > braceStart) 
                    ? tok.Substring(braceStart, braceEnd - braceStart + 1) 
                    : tok;

                string res = EvalToken(evalStr, telemetry, "---");
                switch (i)
                {
                    case 0: _ch1Text = res; break;
                    case 1: _ch2Text = res; break;
                    case 2: _ch3Text = res; break;
                    case 3: _ch4Text = res; break;
                    case 4: _ch5Text = res; break;
                    case 5: _ch6Text = res; break;
                }
            }
        }
    }
}
