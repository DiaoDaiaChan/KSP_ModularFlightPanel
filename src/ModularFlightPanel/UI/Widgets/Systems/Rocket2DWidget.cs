using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 原生 UGUI 火箭姿态与分级动力学卡片 (Rocket 2D / Staging Monitor)
    /// 核心特性：
    /// 1. 左侧 2D 飞船剪影视窗 (Vehicle Silhouette Bay)：
    ///    联动 VesselSilhouetteBaker (IVesselSilhouetteProvider) 实时呈现 2D 正交俯视轮廓与发动机点火喷流；
    ///    离线或无活跃飞船时自适应切换至高精程序化矢量保底剪影。
    /// 2. 右侧推进堆叠结构 (Staging Stacks)：
    ///    直观呈现当前级推进剂耗尽进度条、下级预备状态与实时 dV / TWR。
    /// 3. 严格遵循 MFP 架构规范，零硬编码与零颜色字面量。
    /// </summary>
    public class Rocket2DWidget : BaseFlightWidget
    {
        private Image _bgImage;
        private Outline _outline;

        private Text _titleText;
        private Text _subTitleText;
        private Text _statusText;

        // 顶部简报
        private Text _summaryStageText;
        private Text _summaryTwrText;
        private Text _summaryDvText;

        // 左侧 2D 飞船剪影视窗 (Vehicle Silhouette Bay)
        private GameObject _silhouetteBayObj;
        private Image _silhouetteBayBg;
        private Outline _silhouetteBayOutline;
        private Text _silhouetteBayTitle;
        private RawImage _silhouetteRawImage;
        private static Texture2D _fallbackSilhouetteTexture;

        // 动态发动机喷流羽流 (Exhaust Plume)
        private GameObject _plumeObj;
        private RectTransform _plumeRt;
        private Image _plumeImg;

        // 3 级推进堆叠结构
        private struct StageRowUI
        {
            public GameObject RowObj;
            public Image LeftAccent;
            public Text StageIdText;
            public Text StageStatusText;
            public Text StageDetailText;
            public RectTransform FuelBarFill;
            public Text FuelPercentText;
        }

        private StageRowUI[] _stageRows = new StageRowUI[3];
        private float _barMaxWidth;
        private float _cachedScale = 1.0f;

        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            _cachedScale = s;
            Vector2 panelSize = new Vector2(270f * s, 168f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = theme.FrameBgColor;

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = theme.FrameBorderColor;
            _outline.effectDistance = new Vector2(1.5f * s, 1.5f * s);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _outline.effectColor, s);

            // 1. 顶部 Header
            _titleText = UIFactory.CreateText(transform, "Title", "ROCKET / 2D", Mathf.RoundToInt(12f * s), TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            RectTransform titRt = _titleText.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(95f * s, 18f * s);
            titRt.anchoredPosition = new Vector2(-75f * s, 68f * s);

            _subTitleText = UIFactory.CreateText(transform, "SubTitle", "STAGING TELEMETRY", Mathf.RoundToInt(8f * s), TextAnchor.MiddleLeft, theme.AccentSecondary);
            RectTransform subRt = _subTitleText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(100f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(15f * s, 68f * s);

            _statusText = UIFactory.CreateText(transform, "Status", "● LIVE", Mathf.RoundToInt(9f * s), TextAnchor.MiddleRight, theme.AccentPrimary);
            RectTransform statRt = _statusText.GetComponent<RectTransform>();
            statRt.sizeDelta = new Vector2(50f * s, 16f * s);
            statRt.anchoredPosition = new Vector2(100f * s, 68f * s);

            // 分割横线
            UIFactory.CreatePanel(transform, "Div1", new Vector2(panelSize.x - 16f * s, 1f * s), new Vector2(0f, 56f * s), theme.FrameBorderColor);

            // 2. 简报行 (STAGE, TWR, STAGE dV)
            _summaryStageText = UIFactory.CreateText(transform, "Sum_Stg", "STG --", Mathf.RoundToInt(9f * s), TextAnchor.MiddleLeft, theme.WarningColor);
            RectTransform sStgRt = _summaryStageText.GetComponent<RectTransform>();
            sStgRt.sizeDelta = new Vector2(75f * s, 16f * s);
            sStgRt.anchoredPosition = new Vector2(-85f * s, 44f * s);

            _summaryTwrText = UIFactory.CreateText(transform, "Sum_Twr", "TWR 0.00", Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform sTwrRt = _summaryTwrText.GetComponent<RectTransform>();
            sTwrRt.sizeDelta = new Vector2(75f * s, 16f * s);
            sTwrRt.anchoredPosition = new Vector2(0f, 44f * s);

            _summaryDvText = UIFactory.CreateText(transform, "Sum_Dv", "dV ---- m/s", Mathf.RoundToInt(9f * s), TextAnchor.MiddleRight, theme.AccentSecondary);
            RectTransform sDvRt = _summaryDvText.GetComponent<RectTransform>();
            sDvRt.sizeDelta = new Vector2(85f * s, 16f * s);
            sDvRt.anchoredPosition = new Vector2(82f * s, 44f * s);

            // 3. 左侧 2D 飞船剪影视窗 (Vehicle Silhouette Bay)
            BuildSilhouetteBay(s, theme);

            // 4. 构建右侧 3 个分级状态条 (当前活跃级、下一级、轨道入轨级)
            float rightAreaWidth = 190f * s;
            float rightAreaCenterX = 32f * s;
            _barMaxWidth = 96f * s;
            float[] yOffsets = new float[] { 14f * s, -22f * s, -58f * s };

            for (int i = 0; i < 3; i++)
            {
                _stageRows[i] = CreateStageRow(transform, $"StageRow_{i}", new Vector2(rightAreaWidth, 32f * s),
                    new Vector2(rightAreaCenterX, yOffsets[i]), i == 0, theme);
            }

            ApplyTheme(theme);
        }

        private void BuildSilhouetteBay(float s, ThemeConfig theme)
        {
            float bayW = 56f * s;
            float bayH = 112f * s;
            float bayX = -99f * s;
            float bayY = -22f * s;

            Color borderCol = theme.FrameBorderColor;
            Color primaryAccent = theme.AccentPrimary;
            Color secondaryAccent = theme.AccentSecondary;
            Color textPrimary = theme.TextPrimaryColor;

            _silhouetteBayObj = UIFactory.CreatePanel(transform, "SilhouetteBay", new Vector2(bayW, bayH),
                new Vector2(bayX, bayY), WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep),
                WidgetStyleManager.Weighted(borderCol, LineWeight.Strong), 1f * s);
            _silhouetteBayBg = _silhouetteBayObj.GetComponent<Image>();
            _silhouetteBayOutline = _silhouetteBayObj.GetComponent<Outline>();

            // 四角航电瞄准标线 (Corner Reticles)
            float retLen = 4f * s;
            float retW = 1f * s;
            Color retCol = WidgetStyleManager.Weighted(primaryAccent, LineWeight.Heavy);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTL_H", new Vector2(retLen, retW), new Vector2(-bayW * 0.5f + retLen * 0.5f, bayH * 0.5f - retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTL_V", new Vector2(retW, retLen), new Vector2(-bayW * 0.5f + retW * 0.5f, bayH * 0.5f - retLen * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTR_H", new Vector2(retLen, retW), new Vector2(bayW * 0.5f - retLen * 0.5f, bayH * 0.5f - retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTR_V", new Vector2(retW, retLen), new Vector2(bayW * 0.5f - retW * 0.5f, bayH * 0.5f - retLen * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBL_H", new Vector2(retLen, retW), new Vector2(-bayW * 0.5f + retLen * 0.5f, -bayH * 0.5f + retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBL_V", new Vector2(retW, retLen), new Vector2(-bayW * 0.5f + retW * 0.5f, -bayH * 0.5f + retLen * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBR_H", new Vector2(retLen, retW), new Vector2(bayW * 0.5f - retLen * 0.5f, -bayH * 0.5f + retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBR_V", new Vector2(retW, retLen), new Vector2(bayW * 0.5f - retW * 0.5f, -bayH * 0.5f + retLen * 0.5f), retCol);

            // 视窗顶部标牌
            _silhouetteBayTitle = UIFactory.CreateText(_silhouetteBayObj.transform, "BayTitle", "PROFILE",
                Mathf.RoundToInt(6.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Weighted(textPrimary, LineWeight.Heavy));
            _silhouetteBayTitle.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _silhouetteBayTitle.GetComponent<RectTransform>();
            titleRt.anchoredPosition = new Vector2(0f, bayH * 0.5f - 7f * s);
            titleRt.sizeDelta = new Vector2(bayW - 4f * s, 10f * s);

            // 2D 飞船剪影图元 (RawImage)
            GameObject rawObj = new GameObject("VesselSilhouette_RawImage", typeof(RectTransform), typeof(RawImage));
            rawObj.transform.SetParent(_silhouetteBayObj.transform, false);

            RectTransform rawRt = rawObj.GetComponent<RectTransform>();
            rawRt.anchoredPosition = new Vector2(0f, -2f * s);
            rawRt.sizeDelta = new Vector2(46f * s, 90f * s);

            _silhouetteRawImage = rawObj.GetComponent<RawImage>();
            _silhouetteRawImage.raycastTarget = false;
            _silhouetteRawImage.color = secondaryAccent;

            Texture tex = VesselSilhouetteService.Provider?.SilhouetteTexture;
            if (tex == null)
            {
                if (_fallbackSilhouetteTexture == null)
                {
                    _fallbackSilhouetteTexture = CreateProceduralRocketSilhouetteTexture();
                }
                tex = _fallbackSilhouetteTexture;
            }
            _silhouetteRawImage.texture = tex;

            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated += OnSilhouetteUpdated;
            }

            // 发动机点火羽流 (Exhaust Flare Plume)
            _plumeObj = UIFactory.CreatePanel(_silhouetteBayObj.transform, "EnginePlume", new Vector2(8f * s, 8f * s),
                new Vector2(0f, -bayH * 0.5f + 12f * s), primaryAccent);
            _plumeRt = _plumeObj.GetComponent<RectTransform>();
            _plumeImg = _plumeObj.GetComponent<Image>();
            _plumeObj.SetActive(false);
        }

        private void OnSilhouetteUpdated(Texture tex)
        {
            if (_silhouetteRawImage != null && tex != null)
            {
                _silhouetteRawImage.texture = tex;
            }
        }

        private StageRowUI CreateStageRow(Transform parent, string name, Vector2 size, Vector2 pos, bool isActive, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            StageRowUI row = new StageRowUI();

            Color rowBg = WidgetStyleManager.Surface(isActive ? SurfaceStyleRole.SlotActive : SurfaceStyleRole.Slot);
            Color borderCol = theme.FrameBorderColor;
            Color faintBorder = WidgetStyleManager.Weighted(borderCol, LineWeight.Faint);

            row.RowObj = UIFactory.CreatePanel(parent, name, size, pos, rowBg, faintBorder, 1f * s);

            // 左侧状态竖条
            GameObject accentObj = UIFactory.CreatePanel(row.RowObj.transform, "Accent", new Vector2(3f * s, size.y - 4f * s),
                new Vector2(-(size.x * 0.5f) + 2f * s, 0f), isActive ? theme.AccentPrimary : theme.FrameBorderColor);
            row.LeftAccent = accentObj.GetComponent<Image>();

            // 级数标签
            row.StageIdText = UIFactory.CreateText(row.RowObj.transform, "StgId", "STG 0", Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleLeft, isActive ? theme.WarningColor : theme.TextAccentColor);
            RectTransform idRt = row.StageIdText.GetComponent<RectTransform>();
            idRt.sizeDelta = new Vector2(38f * s, 14f * s);
            idRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 22f * s, 7f * s);

            // 状态标签 (ACTIVE / STANDBY)
            row.StageStatusText = UIFactory.CreateText(row.RowObj.transform, "StgStat", isActive ? "ACTIVE" : "STANDBY",
                Mathf.RoundToInt(6.5f * s), TextAnchor.MiddleLeft, isActive ? theme.AccentPrimary : theme.TextAccentColor);
            RectTransform statRt = row.StageStatusText.GetComponent<RectTransform>();
            statRt.sizeDelta = new Vector2(38f * s, 10f * s);
            statRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 22f * s, -7f * s);

            // 推进剂槽道
            GameObject barBg = UIFactory.CreatePanel(row.RowObj.transform, "FuelBar_Bg", new Vector2(_barMaxWidth, 7f * s),
                new Vector2(12f * s, -6f * s), theme.FrameBorderColor);

            // 推进剂填充条
            GameObject barFill = UIFactory.CreatePanel(barBg.transform, "FuelBar_Fill", new Vector2(_barMaxWidth, 7f * s),
                Vector2.zero, isActive ? theme.AccentPrimary : theme.AccentSecondary);
            row.FuelBarFill = barFill.GetComponent<RectTransform>();
            row.FuelBarFill.pivot = new Vector2(0f, 0.5f);
            row.FuelBarFill.anchoredPosition = new Vector2(-(_barMaxWidth * 0.5f), 0f);

            // 详细说明 (如 CORE LFO / SOLID BOOSTER)
            row.StageDetailText = UIFactory.CreateText(row.RowObj.transform, "Detail", "PROPULSION",
                Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft, theme.TextAccentColor);
            RectTransform detRt = row.StageDetailText.GetComponent<RectTransform>();
            detRt.sizeDelta = new Vector2(_barMaxWidth, 12f * s);
            detRt.anchoredPosition = new Vector2(12f * s, 7f * s);

            // 百分比读数
            row.FuelPercentText = UIFactory.CreateText(row.RowObj.transform, "Percent", "100%",
                Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight, theme.TextPrimaryColor);
            RectTransform pctRt = row.FuelPercentText.GetComponent<RectTransform>();
            pctRt.sizeDelta = new Vector2(34f * s, 14f * s);
            pctRt.anchoredPosition = new Vector2((size.x * 0.5f) - 18f * s, 0f);

            return row;
        }

        private string _lastSummaryStage;
        private string _lastSummaryTwr;
        private string _lastSummaryDv;
        private int _lastCurStage = -999;
        private int _lastActiveEngines = -999;
        private float _lastPropFraction = -1f;
        private bool _lastFiring = false;

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            string stageToken = GetTemplateChannel("SUM_STAGE", "{STAGE}");
            string twrToken = GetTemplateChannel("SUM_TWR", "TWR {TWR:F2}");
            string dvToken = GetTemplateChannel("SUM_DV", "dV {STAGE:DV}");
            string propToken = GetTemplateChannel("PROP_TOKEN", "{PROP}");

            int curStage = (int)TelemetryTokenEngine.EvaluateNumeric(stageToken, telemetry);
            float propFraction = (float)(TelemetryTokenEngine.EvaluateNumeric(propToken, telemetry) / 100.0);
            int activeEngines = (int)TelemetryTokenEngine.EvaluateNumeric("{ENG}", telemetry);
            double stageDv = TelemetryTokenEngine.EvaluateNumeric("{STAGE:DV}", telemetry);

            // 1. 确保剪影纹理最新
            if (_silhouetteRawImage != null && _silhouetteRawImage.texture == null)
            {
                Texture tex = VesselSilhouetteService.Provider?.SilhouetteTexture;
                if (tex != null) _silhouetteRawImage.texture = tex;
            }

            // 2. 动态点火羽流 (Exhaust Plume)
            bool isFiring = activeEngines > 0 || telemetry.Throttle > 0.01f;
            if (_plumeObj != null)
            {
                if (isFiring != _lastFiring)
                {
                    _lastFiring = isFiring;
                    _plumeObj.SetActive(isFiring);
                }
                if (isFiring && _plumeRt != null)
                {
                    float thr = Mathf.Clamp(telemetry.Throttle, 0.25f, 1.0f);
                    _plumeRt.sizeDelta = new Vector2(8f * _cachedScale, (5f + 7f * thr) * _cachedScale);
                }
            }

            // 3. 更新 Header 简报 (Dirty Checking)
            string stgStr = TelemetryTokenEngine.Evaluate(stageToken, telemetry);
            if (stgStr != _lastSummaryStage)
            {
                _lastSummaryStage = stgStr;
                _summaryStageText.text = stgStr;
            }

            string twrStr = TelemetryTokenEngine.Evaluate(twrToken, telemetry);
            if (twrStr != _lastSummaryTwr)
            {
                _lastSummaryTwr = twrStr;
                _summaryTwrText.text = twrStr;
            }

            string dvStr = stageDv > 0 ? TelemetryTokenEngine.Evaluate(dvToken, telemetry) : $"{activeEngines} ENG ON";
            if (dvStr != _lastSummaryDv)
            {
                _lastSummaryDv = dvStr;
                _summaryDvText.text = dvStr;
            }

            // 4. 更新 3 级推进条 (Dirty Checking)
            if (curStage != _lastCurStage || activeEngines != _lastActiveEngines || Math.Abs(propFraction - _lastPropFraction) > 0.005f)
            {
                _lastCurStage = curStage;
                _lastActiveEngines = activeEngines;
                _lastPropFraction = propFraction;

                UpdateRowDisplay(0, curStage, "ACTIVE", $"{activeEngines} ENGINES OPERATIONAL", propFraction, true);

                if (curStage > 0)
                {
                    _stageRows[1].RowObj.SetActive(true);
                    UpdateRowDisplay(1, curStage - 1, "STANDBY", "UPPER STAGE READY", 1.0f, false);
                }
                else
                {
                    _stageRows[1].RowObj.SetActive(false);
                }

                if (curStage > 1)
                {
                    _stageRows[2].RowObj.SetActive(true);
                    UpdateRowDisplay(2, 0, "FINAL", "PAYLOAD / ORBIT INSERTION", 1.0f, false);
                }
                else
                {
                    _stageRows[2].RowObj.SetActive(false);
                }
            }
        }

        private void UpdateRowDisplay(int rowIndex, int stageNumber, string statusTag, string detail, float fraction, bool isActive)
        {
            StageRowUI row = _stageRows[rowIndex];
            row.StageIdText.text = $"STG {stageNumber}";
            row.StageStatusText.text = statusTag;
            row.StageDetailText.text = detail;

            float clampedFraction = Mathf.Clamp01(fraction);
            row.FuelBarFill.sizeDelta = new Vector2(_barMaxWidth * clampedFraction, row.FuelBarFill.sizeDelta.y);
            row.FuelPercentText.text = $"{clampedFraction * 100f:F1}%";

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
            if (isActive && theme != null)
            {
                Image fillImg = row.FuelBarFill.GetComponent<Image>();
                if (fillImg != null)
                {
                    MeterStyleRole role = (clampedFraction <= 0.1f)
                        ? MeterStyleRole.Danger
                        : ((clampedFraction <= 0.25f) ? MeterStyleRole.Warning : MeterStyleRole.Primary);
                    fillImg.color = WidgetStyleManager.Meter(role, theme);
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);
            if (_titleText != null)
            {
                _titleText.text = GetTemplateChannel("TITLE", "ROCKET / 2D");
                ApplyText(_titleText, TextStyleRole.Label, theme);
            }
            if (_subTitleText != null)
            {
                _subTitleText.text = GetTemplateChannel("SUBTITLE", "STAGING TELEMETRY");
                ApplyText(_subTitleText, TextStyleRole.SecondaryValue, theme);
            }
            if (_statusText != null) ApplyText(_statusText, TextStyleRole.Accent, theme);
            if (_summaryStageText != null) ApplyText(_summaryStageText, TextStyleRole.Warning, theme);
            if (_summaryTwrText != null) ApplyText(_summaryTwrText, TextStyleRole.PrimaryValue, theme);
            if (_summaryDvText != null) ApplyText(_summaryDvText, TextStyleRole.SecondaryValue, theme);

            if (_silhouetteBayBg != null) _silhouetteBayBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);
            if (_silhouetteBayOutline != null) _silhouetteBayOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Strong);
            if (_silhouetteBayTitle != null) _silhouetteBayTitle.color = WidgetStyleManager.Weighted(theme.TextPrimaryColor, LineWeight.Heavy);
            if (_silhouetteRawImage != null) _silhouetteRawImage.color = theme.AccentSecondary;
            if (_plumeImg != null) _plumeImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
        }

        /// <summary>
        /// 程序化高精多级火箭矢量二维剪影纹理 (用于无头测试与原地沙盒无活跃飞船时的保底呈现)
        /// </summary>
        private static Texture2D CreateProceduralRocketSilhouetteTexture()
        {
            int w = 128;
            int h = 256;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] cols = new Color[w * h];
            float centerX = (w - 1) * 0.5f;

            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / (h - 1); // 0.0 (底部) .. 1.0 (顶部)

                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x - centerX) / centerX; // 0.0 (中轴线) .. 1.0 (侧边缘)
                    bool inside = false;
                    bool isLine = false;

                    // 1. 顶部载荷整流罩 / 头锥 (ny: 0.80 .. 0.96)
                    if (ny >= 0.80f && ny <= 0.96f)
                    {
                        float t = (ny - 0.80f) / 0.16f;
                        float fairingW = Mathf.Lerp(0.22f, 0.02f, Mathf.Pow(t, 0.75f));
                        if (dx <= fairingW) inside = true;
                    }
                    // 2. 上面级 (ny: 0.65 .. 0.795)
                    else if (ny >= 0.65f && ny < 0.795f)
                    {
                        if (dx <= 0.22f) inside = true;
                    }
                    // 3. 主芯级 (ny: 0.22 .. 0.645)
                    else if (ny >= 0.22f && ny < 0.645f)
                    {
                        if (dx <= 0.22f) inside = true;
                    }
                    // 4. 底部主发动机喷管 (ny: 0.14 .. 0.215)
                    else if (ny >= 0.14f && ny < 0.215f)
                    {
                        float t = (ny - 0.14f) / 0.075f;
                        float nozzleW = Mathf.Lerp(0.26f, 0.18f, t);
                        if (dx <= nozzleW) inside = true;
                    }

                    // 5. 两侧捆绑助推器 (ny: 0.24 .. 0.60)
                    if (ny >= 0.24f && ny <= 0.60f)
                    {
                        float boosterCenter = 0.38f;
                        float boosterHalfW = 0.09f;
                        float bstDx = Mathf.Abs(dx - boosterCenter);

                        if (ny > 0.54f)
                        {
                            float t = (ny - 0.54f) / 0.06f;
                            float curW = Mathf.Lerp(boosterHalfW, 0.01f, t);
                            if (bstDx <= curW) inside = true;
                        }
                        else
                        {
                            if (bstDx <= boosterHalfW) inside = true;
                        }
                    }

                    // 6. 助推器底部喷管 (ny: 0.17 .. 0.235)
                    if (ny >= 0.17f && ny < 0.235f)
                    {
                        float boosterCenter = 0.38f;
                        float bstDx = Mathf.Abs(dx - boosterCenter);
                        if (bstDx <= 0.06f) inside = true;
                    }

                    // 7. 级间隔框细线刻痕
                    if (inside && (Mathf.Abs(ny - 0.795f) < 0.005f || Mathf.Abs(ny - 0.645f) < 0.005f))
                    {
                        isLine = true;
                    }

                    // 8. 脊线高光
                    if (inside && dx <= 0.02f && ny >= 0.24f && ny <= 0.90f)
                    {
                        isLine = true;
                    }

                    Color c = Color.clear;
                    if (inside)
                    {
                        c = isLine
                            ? WidgetStyleManager.Weighted(WidgetStyleManager.NeutralOpaque, LineWeight.Strong)
                            : WidgetStyleManager.NeutralOpaque;
                    }

                    cols[y * w + x] = c;
                }
            }

            tex.SetPixels(cols);
            tex.Apply(false, true);
            return tex;
        }

        protected override void OnDestroy()
        {
            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated -= OnSilhouetteUpdated;
            }
            base.OnDestroy();
        }
    }
}
