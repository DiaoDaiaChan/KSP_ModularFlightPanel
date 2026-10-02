using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.HUD;

namespace ModularFlightPanel.UI.Workbench.Tabs
{
    /// <summary>
    /// 现代航电遥测探针沙盒、性能监视与诊断视图 (WorkbenchTabDiagnostics)
    /// </summary>
    public class WorkbenchTabDiagnostics : IWorkbenchTabView
    {
        private RectTransform _container;
        private RectTransform _contentRt;

        private Text _fpsReadout;
        private Text _cpuReadout;
        private string _tokenSearch = "";
        private float _lastUpdateTimer = 0f;

        private readonly string[] _commonTelemetryTokens = new string[]
        {
            "{SPD}", "{SURFSPD}", "{ORBSPD}", "{MACH}", "{ALT}", "{RADARALT}",
            "{APO}", "{PER}", "{TIME_APO}", "{TIME_PER}", "{INC}", "{ECC}",
            "{THR}", "{TWR}", "{MAXTWR}", "{DV}", "{STGDV}", "{TOTALDV}",
            "{G}", "{VERTSPD}", "{HORIZSPD}", "{COMM}", "{EC}", "{EC_RATE}",
            "{MONO}", "{ORE}", "{MASS}", "{PARTS}", "{STAGE}", "{FRAME}"
        };

        private readonly List<Text> _liveTokenValues = new List<Text>();
        private readonly List<string> _activeTokens = new List<string>();

        public void Build(RectTransform container)
        {
            _container = container;

            for (int i = _container.childCount - 1; i >= 0; i--)
            {
                var c = _container.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }

            GameObject card = WorkbenchControls.CreatePanel(_container, "DiagCard", Vector2.zero);
            RectTransform cardRt = card.GetComponent<RectTransform>();
            cardRt.anchorMin = Vector2.zero;
            cardRt.anchorMax = Vector2.one;
            cardRt.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 10f;
            vlg.padding = new RectOffset(16, 16, 16, 16);

            // 标题
            GameObject titleObj = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleObj.transform.SetParent(card.transform, false);
            Text titleTxt = titleObj.GetComponent<Text>();
            titleTxt.font = WorkbenchControls.MainFont;
            titleTxt.fontSize = 15;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = WorkbenchStyleEngine.ColorTextPrimary;
            titleTxt.text = "🚀 航电实时遥测沙盒、性能监视与熔断排障";

            _contentRt = WorkbenchControls.CreateScrollView(card.transform, "DiagScrollView", new Vector2(0f, 480f), out GameObject scrollObj);
            var le = scrollObj.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;

            Refresh();
        }

        public void Refresh()
        {
            if (_contentRt == null) return;

            _liveTokenValues.Clear();
            _activeTokens.Clear();

            for (int i = _contentRt.childCount - 1; i >= 0; i--)
            {
                var c = _contentRt.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }

            // 1. 实时性能仪表盘 (Profiler Metrics)
            GameObject perfCard = WorkbenchControls.CreateCard(_contentRt, "PerfCard", new Vector2(0f, 60f));
            var pHlg = perfCard.AddComponent<HorizontalLayoutGroup>();
            pHlg.childForceExpandWidth = true;
            pHlg.childForceExpandHeight = true;
            pHlg.spacing = 16f;
            pHlg.padding = new RectOffset(14, 14, 8, 8);

            // FPS
            GameObject fpsBox = new GameObject("FpsBox", typeof(RectTransform), typeof(VerticalLayoutGroup));
            fpsBox.transform.SetParent(perfCard.transform, false);
            var fVlg = fpsBox.GetComponent<VerticalLayoutGroup>();
            fVlg.childForceExpandWidth = true;
            fVlg.childForceExpandHeight = false;

            GameObject fLbl = new GameObject("FLbl", typeof(RectTransform), typeof(Text));
            fLbl.transform.SetParent(fpsBox.transform, false);
            Text flTxt = fLbl.GetComponent<Text>();
            flTxt.font = WorkbenchControls.MainFont;
            flTxt.fontSize = 11;
            flTxt.color = WorkbenchStyleEngine.ColorTextMuted;
            flTxt.text = "视觉渲染刷新率 (FPS)";

            GameObject fVal = new GameObject("FVal", typeof(RectTransform), typeof(Text));
            fVal.transform.SetParent(fpsBox.transform, false);
            _fpsReadout = fVal.GetComponent<Text>();
            _fpsReadout.font = WorkbenchControls.MainFont;
            _fpsReadout.fontSize = 18;
            _fpsReadout.fontStyle = FontStyle.Bold;
            _fpsReadout.color = WorkbenchStyleEngine.ColorAccentPrimary;
            _fpsReadout.text = $"{MFPProfiler.CurrentFPS:F0} FPS";

            // CPU 耗时
            GameObject cpuBox = new GameObject("CpuBox", typeof(RectTransform), typeof(VerticalLayoutGroup));
            cpuBox.transform.SetParent(perfCard.transform, false);
            var cVlg = cpuBox.GetComponent<VerticalLayoutGroup>();
            cVlg.childForceExpandWidth = true;
            cVlg.childForceExpandHeight = false;

            GameObject cLbl = new GameObject("CLbl", typeof(RectTransform), typeof(Text));
            cLbl.transform.SetParent(cpuBox.transform, false);
            Text clTxt = cLbl.GetComponent<Text>();
            clTxt.font = WorkbenchControls.MainFont;
            clTxt.fontSize = 11;
            clTxt.color = WorkbenchStyleEngine.ColorTextMuted;
            clTxt.text = "MFP 单帧物理解算耗时";

            GameObject cVal = new GameObject("CVal", typeof(RectTransform), typeof(Text));
            cVal.transform.SetParent(cpuBox.transform, false);
            _cpuReadout = cVal.GetComponent<Text>();
            _cpuReadout.font = WorkbenchControls.MainFont;
            _cpuReadout.fontSize = 18;
            _cpuReadout.fontStyle = FontStyle.Bold;
            _cpuReadout.color = WorkbenchStyleEngine.ColorWarning;
            _cpuReadout.text = $"{MFPProfiler.AvgTotalMs:F2} ms";

            // 2. 安全熔断与紧急旁路
            if (MFPSafetyFallback.IsFaulted)
            {
                GameObject faultCard = WorkbenchControls.CreateCard(_contentRt, "FaultCard", new Vector2(0f, 44f));
                var fHlg = faultCard.AddComponent<HorizontalLayoutGroup>();
                fHlg.childForceExpandWidth = false;
                fHlg.childForceExpandHeight = true;
                fHlg.spacing = 10f;
                fHlg.padding = new RectOffset(12, 12, 6, 6);

                GameObject fText = new GameObject("FText", typeof(RectTransform), typeof(Text));
                fText.transform.SetParent(faultCard.transform, false);
                fText.GetComponent<RectTransform>().sizeDelta = new Vector2(400f, 28f);
                var fLe = fText.AddComponent<LayoutElement>();
                fLe.flexibleWidth = 1f;

                Text ft = fText.GetComponent<Text>();
                ft.font = WorkbenchControls.MainFont;
                ft.fontSize = 11;
                ft.color = WorkbenchStyleEngine.ColorDanger;
                ft.text = $"⚠ 航电安全熔断已激活: {MFPSafetyFallback.FaultReason}";

                WorkbenchControls.CreateButton(faultCard.transform, "RecoverBtn", "🔄 尝试恢复 MFP", new Vector2(130f, 26f), () =>
                {
                    MFPSafetyFallback.TryRecoverFromFault();
                    Refresh();
                }, false, 11);
            }

            // 3. 736+ 遥测参数动态探针检索沙盒
            GameObject subHeader = new GameObject("SubHeader", typeof(RectTransform), typeof(Text));
            subHeader.transform.SetParent(_contentRt, false);
            var shLe = subHeader.AddComponent<LayoutElement>();
            shLe.preferredHeight = 28f;
            shLe.minHeight = 28f;

            Text shTxt = subHeader.GetComponent<Text>();
            shTxt.font = WorkbenchControls.MainFont;
            shTxt.fontSize = 13;
            shTxt.fontStyle = FontStyle.Bold;
            shTxt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            shTxt.text = "\n📡 航电动态遥测参数沙盒 (Live Telemetry Probe)";

            WorkbenchControls.CreateTextField(_contentRt, "SearchToken", _tokenSearch, "🔍 检索参数 Token (如 SPD, AP, THR)...", new Vector2(0f, 28f), (v) =>
            {
                _tokenSearch = v;
                Refresh();
            });

            var tokens = _commonTelemetryTokens.Where(t =>
                string.IsNullOrEmpty(_tokenSearch) || t.IndexOf(_tokenSearch, StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList();

            foreach (var tok in tokens)
            {
                BuildTokenRow(tok);
            }
        }

        private void BuildTokenRow(string token)
        {
            GameObject row = WorkbenchControls.CreateCard(_contentRt, "Row_" + token, new Vector2(0f, 32f), true);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(12, 12, 4, 4);

            GameObject nameObj = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameObj.transform.SetParent(row.transform, false);
            nameObj.GetComponent<RectTransform>().sizeDelta = new Vector2(140f, 24f);
            var nLe = nameObj.AddComponent<LayoutElement>();
            nLe.preferredWidth = 140f;
            nLe.minWidth = 120f;

            Text nameTxt = nameObj.GetComponent<Text>();
            nameTxt.font = WorkbenchControls.MainFont;
            nameTxt.fontSize = 12;
            nameTxt.fontStyle = FontStyle.Bold;
            nameTxt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            nameTxt.text = token;

            GameObject valObj = new GameObject("Val", typeof(RectTransform), typeof(Text));
            valObj.transform.SetParent(row.transform, false);
            valObj.GetComponent<RectTransform>().sizeDelta = new Vector2(260f, 24f);
            var vLe = valObj.AddComponent<LayoutElement>();
            vLe.flexibleWidth = 1f;

            Text valTxt = valObj.GetComponent<Text>();
            valTxt.font = WorkbenchControls.MainFont;
            valTxt.fontSize = 12;
            valTxt.color = WorkbenchStyleEngine.ColorTextPrimary;
            string valStr = TelemetryTokenEngine.Evaluate(token, FlightTelemetryContext.Current);
            valTxt.text = string.IsNullOrEmpty(valStr) ? "---" : valStr;

            _activeTokens.Add(token);
            _liveTokenValues.Add(valTxt);

            WorkbenchControls.CreateButton(row.transform, "CopyBtn", "复制", new Vector2(50f, 24f), () =>
            {
                GUIUtility.systemCopyBuffer = token;
            }, false, 10);
        }

        public void OnUpdate()
        {
            _lastUpdateTimer += Time.unscaledDeltaTime;
            if (_lastUpdateTimer >= 0.25f) // 4Hz 节流平滑刷新，0 抖动
            {
                _lastUpdateTimer = 0f;

                if (_fpsReadout != null) _fpsReadout.text = $"{MFPProfiler.CurrentFPS:F0} FPS";
                if (_cpuReadout != null) _cpuReadout.text = $"{MFPProfiler.AvgTotalMs:F2} ms";

                var ctx = FlightTelemetryContext.Current;
                for (int i = 0; i < _liveTokenValues.Count; i++)
                {
                    if (_liveTokenValues[i] != null && i < _activeTokens.Count)
                    {
                        string eval = TelemetryTokenEngine.Evaluate(_activeTokens[i], ctx);
                        _liveTokenValues[i].text = string.IsNullOrEmpty(eval) ? "---" : eval;
                    }
                }
            }
        }
    }
}
