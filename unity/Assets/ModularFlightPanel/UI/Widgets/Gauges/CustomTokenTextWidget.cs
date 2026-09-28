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

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                Ch1Val.Text = "---";
                Ch2Val.Text = "---";
                Ch3Val.Text = "---";
                Ch4Val.Text = "---";
                Ch5Val.Text = "---";
                Ch6Val.Text = "---";
                return;
            }

            // 自定义标题更新
            string titleTpl = !string.IsNullOrEmpty(Config?.DisplayName) ? Config.DisplayName : I18n.Tr("WIDGET_GAUGE_TELEM_MATRIX", "遥测矩阵");
            Title.Text = EvalToken(titleTpl, telemetry, I18n.Tr("WIDGET_GAUGE_TELEM_MATRIX", "遥测矩阵"));

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
            Ch1Val.Text = spd >= 10000.0 ? (spd * 0.001).ToString("F1") + "k" : spd.ToString("F1");

            // 2. TWR: 实际可用推重比
            double twr = telemetry.TWR;
            Ch2Val.Text = twr.ToString("F2");
            Ch2Val.SetRole(twr > 0.05 && twr < 1.0 && telemetry.AltitudeAGL < 1000.0 ? TextStyleRole.Warning : TextStyleRole.PrimaryValue);

            // 3. RALT: 雷达真高 (AGL)
            double ralt = telemetry.AltitudeAGL;
            Ch3Val.Text = ralt >= 100000.0 ? (ralt * 0.001).ToString("F0") + "k" : ralt.ToString("F0");

            // 4. Q: 动压 (kPa)
            double q = telemetry.DynamicPressure;
            Ch4Val.Text = q.ToString("F1");
            Ch4Val.SetRole(q > 35.0 ? TextStyleRole.Danger : (q > 25.0 ? TextStyleRole.Warning : TextStyleRole.PrimaryValue));

            // 5. VSI: 垂直速度
            double vsi = telemetry.VerticalSpeed;
            Ch5Val.Text = (vsi >= 0.0 ? "+" : "") + vsi.ToString("F1");
            Ch5Val.SetRole(vsi < -50.0 && ralt < 3000.0 ? TextStyleRole.Danger : TextStyleRole.PrimaryValue);

            // 6. G: 当前加速度过载
            double g = telemetry.GForce;
            Ch6Val.Text = g.ToString("F2");
            Ch6Val.SetRole(g > 6.0 ? TextStyleRole.Danger : (g > 4.0 ? TextStyleRole.Warning : TextStyleRole.PrimaryValue));
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
                    case "CH1": Ch1Val.Text = EvalToken(token, telemetry, "---"); break;
                    case "CH2": Ch2Val.Text = EvalToken(token, telemetry, "---"); break;
                    case "CH3": Ch3Val.Text = EvalToken(token, telemetry, "---"); break;
                    case "CH4": Ch4Val.Text = EvalToken(token, telemetry, "---"); break;
                    case "CH5": Ch5Val.Text = EvalToken(token, telemetry, "---"); break;
                    case "CH6": Ch6Val.Text = EvalToken(token, telemetry, "---"); break;
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
                    case 0: Ch1Val.Text = res; break;
                    case 1: Ch2Val.Text = res; break;
                    case 2: Ch3Val.Text = res; break;
                    case 3: Ch4Val.Text = res; break;
                    case 4: Ch5Val.Text = res; break;
                    case 5: Ch6Val.Text = res; break;
                }
            }
        }
    }
}
