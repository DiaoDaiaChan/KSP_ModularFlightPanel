using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 全局航电组件控件注册与装配管理器 (Avionics Widget Control Manager)
    /// 核心职责：
    /// 1. 集中管理所有组件内部的子控件 (Sub-Elements / Micro-Controls)；
    /// 2. 为所有 44 款组件提供标准化、零重复造轮子的通用控件装配能力；
    /// 3. 打通编辑模式 (Edit Mode) 的动态检查器 (Inspector) 与子部件按需裁剪屏蔽 (Sub-Element Masking)。
    /// </summary>
    public static class WidgetControlManager
    {
        private static readonly Dictionary<BaseFlightWidget, List<IWidgetControl>> _widgetControls =
            new Dictionary<BaseFlightWidget, List<IWidgetControl>>();

        public static void Register(BaseFlightWidget widget, IWidgetControl control)
        {
            if (widget == null || control == null) return;
            if (!_widgetControls.TryGetValue(widget, out var list))
            {
                list = new List<IWidgetControl>();
                _widgetControls[widget] = list;
            }
            if (!list.Contains(control))
            {
                list.Add(control);
            }
            if (control is BaseWidgetControl bwc)
            {
                bwc.SetParentWidget(widget);
            }
            widget.Controls?.AddDirectControl(control);
        }

        public static IReadOnlyList<IWidgetControl> GetControls(BaseFlightWidget widget)
        {
            if (widget != null && _widgetControls.TryGetValue(widget, out var list))
            {
                return list;
            }
            return Array.Empty<IWidgetControl>();
        }

        public static T GetControl<T>(BaseFlightWidget widget, string controlId) where T : class, IWidgetControl
        {
            if (widget == null || string.IsNullOrEmpty(controlId)) return null;
            if (_widgetControls.TryGetValue(widget, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is T typed && string.Equals(list[i].Id, controlId, StringComparison.OrdinalIgnoreCase))
                    {
                        return typed;
                    }
                }
            }
            return null;
        }

        public static void UpdateControls(BaseFlightWidget widget, IFlightTelemetry telemetry)
        {
            if (widget == null || telemetry == null) return;
            if (_widgetControls.TryGetValue(widget, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].UpdateTelemetry(telemetry);
                }
            }
        }

        public static void ApplyThemeToControls(BaseFlightWidget widget, ThemeConfig theme)
        {
            if (widget == null) return;
            if (_widgetControls.TryGetValue(widget, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].ApplyTheme(theme);
                }
            }
        }

        public static void BindConfigToControls(BaseFlightWidget widget, WidgetConfig config)
        {
            if (widget == null || config == null) return;
            if (_widgetControls.TryGetValue(widget, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].BindConfig(config);
                }
            }
        }

        public static void UnregisterAll(BaseFlightWidget widget)
        {
            if (widget != null)
            {
                _widgetControls.Remove(widget);
            }
        }

        #region Factory Methods

        /// <summary>
        /// 创建标准航电标题栏控件
        /// </summary>
        public static WidgetHeaderControl CreateHeader(BaseFlightWidget parent, string id, string title, string subtitle = "",
            Vector2? pos = null, Vector2? size = null, string badge = "")
        {
            float s = parent.CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;

            Vector2 actualSize = size ?? new Vector2(parent.RectTransform.sizeDelta.x - 16f * s, 20f * s);
            Vector2 actualPos = pos ?? new Vector2(0f, parent.RectTransform.sizeDelta.y * 0.5f - actualSize.y * 0.5f - 4f * s);

            GameObject headerGo = new GameObject(id + "_Header", typeof(RectTransform));
            headerGo.transform.SetParent(parent.transform, false);
            RectTransform headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.sizeDelta = actualSize;
            headerRt.anchoredPosition = actualPos;

            // 标题
            Text titleTxt = UIFactory.CreateText(headerGo.transform, "TitleText", title, Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform tRt = titleTxt.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 0.2f);
            tRt.anchorMax = new Vector2(0.6f, 1f);
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;

            // 副标题
            Text subTxt = null;
            if (!string.IsNullOrEmpty(subtitle))
            {
                subTxt = UIFactory.CreateText(headerGo.transform, "SubtitleText", subtitle, Mathf.RoundToInt(7.5f * s),
                    TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
                RectTransform sRt = subTxt.GetComponent<RectTransform>();
                sRt.anchorMin = new Vector2(0f, 0f);
                sRt.anchorMax = new Vector2(0.6f, 0.45f);
                sRt.offsetMin = Vector2.zero;
                sRt.offsetMax = Vector2.zero;
            }

            // 状态徽标
            Text badgeTxt = null;
            if (!string.IsNullOrEmpty(badge))
            {
                badgeTxt = UIFactory.CreateText(headerGo.transform, "BadgeText", badge, Mathf.RoundToInt(8f * s),
                    TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Accent, theme));
                RectTransform bRt = badgeTxt.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0.6f, 0.2f);
                bRt.anchorMax = new Vector2(1f, 1f);
                bRt.offsetMin = Vector2.zero;
                bRt.offsetMax = Vector2.zero;
            }

            // 装饰细线
            GameObject divGo = UIFactory.CreatePanel(headerGo.transform, "Divider", new Vector2(actualSize.x, 1f * s),
                new Vector2(0f, -actualSize.y * 0.5f), style.GetLineColor(LineWeight.Faint, theme));
            Image divImg = divGo.GetComponent<Image>();

            var ctrl = new WidgetHeaderControl(parent, id, I18n.Tr("CTL_HEADER_BAR", "标题栏"), headerGo, titleTxt, subTxt, badgeTxt, divImg, title, subtitle, badge);
            Register(parent, ctrl);
            return ctrl;
        }

        /// <summary>
        /// 创建标准数显读数盒控件
        /// </summary>
        public static WidgetReadoutControl CreateReadout(BaseFlightWidget parent, string id, string displayName,
            Vector2 size, Vector2 pos, string token, string title = "", string unit = "")
        {
            float s = parent.CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;

            GameObject boxGo = UIFactory.CreatePanel(parent.transform, id + "_Readout", size, pos,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);

            Image bg = boxGo.GetComponent<Image>();
            Outline outline = boxGo.GetComponent<Outline>();

            // 标题
            Text titleTxt = null;
            if (!string.IsNullOrEmpty(title))
            {
                titleTxt = UIFactory.CreateText(boxGo.transform, "Title", title, Mathf.Max(6, Mathf.RoundToInt(size.y * 0.28f)),
                    TextAnchor.UpperLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                RectTransform tRt = titleTxt.GetComponent<RectTransform>();
                tRt.anchorMin = new Vector2(0f, 0.5f);
                tRt.anchorMax = new Vector2(1f, 1f);
                tRt.offsetMin = new Vector2(4f * s, 0f);
                tRt.offsetMax = new Vector2(-4f * s, -2f * s);
            }

            // 主数显
            int valFontSize = Mathf.Max(8, Mathf.RoundToInt(size.y * 0.48f));
            Text valTxt = UIFactory.CreateText(boxGo.transform, "Value", "---", valFontSize,
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            valTxt.fontStyle = FontStyle.Bold;
            RectTransform vRt = valTxt.GetComponent<RectTransform>();
            vRt.anchorMin = new Vector2(0f, 0f);
            vRt.anchorMax = new Vector2(0.78f, titleTxt != null ? 0.65f : 1f);
            vRt.offsetMin = new Vector2(4f * s, 2f * s);
            vRt.offsetMax = Vector2.zero;

            // 单位
            Text unitTxt = null;
            if (!string.IsNullOrEmpty(unit))
            {
                unitTxt = UIFactory.CreateText(boxGo.transform, "Unit", unit, Mathf.Max(6, Mathf.RoundToInt(size.y * 0.26f)),
                    TextAnchor.LowerRight, style.GetTextColor(TextStyleRole.Unit, theme));
                RectTransform uRt = unitTxt.GetComponent<RectTransform>();
                uRt.anchorMin = new Vector2(0.72f, 0f);
                uRt.anchorMax = new Vector2(1f, 0.65f);
                uRt.offsetMin = new Vector2(0f, 2f * s);
                uRt.offsetMax = new Vector2(-4f * s, 0f);
            }

            var ctrl = new WidgetReadoutControl(parent, id, displayName, boxGo, bg, outline, titleTxt, valTxt, unitTxt, token, title, unit);
            Register(parent, ctrl);
            return ctrl;
        }

        /// <summary>
        /// 创建标准线性柱状表控件
        /// </summary>
        public static WidgetLinearBarControl CreateLinearBar(BaseFlightWidget parent, string id, string displayName,
            Vector2 size, Vector2 pos, string token, double min = 0.0, double max = 100.0, bool isVertical = false)
        {
            float s = parent.CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;

            GameObject trackGo = UIFactory.CreatePanel(parent.transform, id + "_Track", size, pos,
                style.GetMeterColor(MeterStyleRole.Track, theme));
            Image trackImg = trackGo.GetComponent<Image>();

            Vector2 fillSize = isVertical ? new Vector2(size.x, 0f) : new Vector2(0f, size.y);
            Vector2 fillPos = isVertical ? new Vector2(0f, -size.y * 0.5f) : new Vector2(-size.x * 0.5f, 0f);

            GameObject fillGo = UIFactory.CreatePanel(trackGo.transform, id + "_Fill", fillSize, fillPos,
                style.GetMeterColor(MeterStyleRole.Primary, theme));
            Image fillImg = fillGo.GetComponent<Image>();
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();

            if (isVertical)
            {
                fillRt.pivot = new Vector2(0.5f, 0f);
                fillRt.anchoredPosition = new Vector2(0f, -size.y * 0.5f);
            }
            else
            {
                fillRt.pivot = new Vector2(0f, 0.5f);
                fillRt.anchoredPosition = new Vector2(-size.x * 0.5f, 0f);
            }

            float span = isVertical ? size.y / s : size.x / s;
            var ctrl = new WidgetLinearBarControl(parent, id, displayName, trackGo, trackImg, fillImg, token, min, max, span, isVertical);
            Register(parent, ctrl);
            return ctrl;
        }

        /// <summary>
        /// 创建标准交互按键控件
        /// </summary>
        public static WidgetActionButtonControl CreateButton(BaseFlightWidget parent, string id, string displayName,
            Vector2 size, Vector2 pos, string label, Action onClick, bool isToggle = false)
        {
            float s = parent.CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;

            Button btn = UIFactory.CreateButton(parent.transform, id + "_Btn", size, pos, onClick != null ? new UnityEngine.Events.UnityAction(onClick) : null);
            Image bg = btn.GetComponent<Image>();
            Outline outline = btn.GetComponent<Outline>();

            Text txt = UIFactory.CreateText(btn.transform, "Label", label, Mathf.Max(8, Mathf.RoundToInt(size.y * 0.45f)),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            txt.fontStyle = FontStyle.Bold;
            RectTransform tRt = txt.GetComponent<RectTransform>();
            tRt.sizeDelta = size;
            tRt.anchoredPosition = Vector2.zero;

            var ctrl = new WidgetActionButtonControl(parent, id, displayName, btn.gameObject, btn, bg, outline, txt, null, label, onClick, isToggle);
            Register(parent, ctrl);
            return ctrl;
        }

        /// <summary>
        /// 将已有子物体封装注册为标准化子部件 (支持遮罩、显隐与样式更新)
        /// </summary>
        public static WidgetGenericSubElementControl WrapElement(BaseFlightWidget parent, string id, string displayName,
            GameObject rootGo, Action<ThemeConfig> onApplyTheme = null, Action<IFlightTelemetry> onUpdateTelemetry = null)
        {
            var ctrl = new WidgetGenericSubElementControl(parent, id, displayName, rootGo, onApplyTheme, onUpdateTelemetry);
            Register(parent, ctrl);
            return ctrl;
        }

        public static WidgetGenericSubElementControl WrapElement(BaseFlightWidget parent, string id, string displayName,
            GameObject rootGo, string description, Action<ThemeConfig> onApplyTheme = null, Action<IFlightTelemetry> onUpdateTelemetry = null)
        {
            var ctrl = new WidgetGenericSubElementControl(parent, id, displayName, rootGo, onApplyTheme, onUpdateTelemetry);
            Register(parent, ctrl);
            return ctrl;
        }

        public static WidgetGenericSubElementControl WrapElement(string id, string displayName,
            GameObject rootGo, Action<ThemeConfig> onApplyTheme = null, Action<IFlightTelemetry> onUpdateTelemetry = null)
        {
            return new WidgetGenericSubElementControl(null, id, displayName, rootGo, onApplyTheme, onUpdateTelemetry);
        }

        #endregion
    }

    /// <summary>
    /// 挂载在每个小组件上的微控件容器适配器，提供流式链式与声明式微控件生命周期访问
    /// 优化：通过实例级直接列表驱动 UpdateControls 与 Get，消除高频静态全局字典哈希查询
    /// </summary>
    public class WidgetControlContainer
    {
        private readonly BaseFlightWidget _owner;
        private readonly List<IWidgetControl> _directList = new List<IWidgetControl>();
        private readonly List<IWidgetControl> _activeTelemetryControls = new List<IWidgetControl>();
        private bool _activeControlsDirty = true;

        public WidgetControlContainer(BaseFlightWidget owner)
        {
            _owner = owner;
        }

        internal void AddDirectControl(IWidgetControl control)
        {
            if (control != null && !_directList.Contains(control))
            {
                _directList.Add(control);
                _activeControlsDirty = true;
            }
        }

        public void Register(IWidgetControl control)
        {
            AddDirectControl(control);
            WidgetControlManager.Register(_owner, control);
        }

        public void ApplyThemeToControls(ThemeConfig theme) => WidgetControlManager.ApplyThemeToControls(_owner, theme);
        public void BindConfigToControls(WidgetConfig config)
        {
            WidgetControlManager.BindConfigToControls(_owner, config);
            _activeControlsDirty = true;
        }

        public void InvalidateActiveControls()
        {
            _activeControlsDirty = true;
        }

        private void EnsureActiveControlsCached()
        {
            if (!_activeControlsDirty) return;
            _activeTelemetryControls.Clear();
            for (int i = 0; i < _directList.Count; i++)
            {
                var c = _directList[i];
                if (c != null && c.NeedsTelemetryUpdate)
                {
                    _activeTelemetryControls.Add(c);
                }
            }
            _activeControlsDirty = false;
        }

        public void UpdateControls(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;
            EnsureActiveControlsCached();
            int count = _activeTelemetryControls.Count;
            for (int i = 0; i < count; i++)
            {
                _activeTelemetryControls[i].UpdateTelemetry(telemetry);
            }
        }

        public void UnregisterAll()
        {
            _directList.Clear();
            _activeTelemetryControls.Clear();
            _activeControlsDirty = false;
            WidgetControlManager.UnregisterAll(_owner);
        }

        public IReadOnlyList<IWidgetControl> All => _directList.Count > 0 ? _directList : WidgetControlManager.GetControls(_owner);

        public T Get<T>(string id) where T : class, IWidgetControl
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _directList.Count; i++)
            {
                if (_directList[i] is T typed && string.Equals(_directList[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return typed;
                }
            }
            return WidgetControlManager.GetControl<T>(_owner, id);
        }

        public void SetControlVisibility(string id, bool visible)
        {
            var ctrl = Get<IWidgetControl>(id);
            if (ctrl != null)
            {
                ctrl.IsVisible = visible;
            }
            if (_owner?.Config != null)
            {
                _owner.Config.SetSubElementDisabled(id, !visible);
            }
        }

        public void SetControlOffset(string id, Vector2 offset)
        {
            var ctrl = Get<IWidgetControl>(id);
            if (ctrl != null)
            {
                ctrl.ApplyOffset(offset);
            }
            if (_owner?.Config != null)
            {
                _owner.Config.SetSubElementOffset(id, offset);
            }
        }

        public void ResetAllOffsets()
        {
            var list = All;
            for (int i = 0; i < list.Count; i++)
            {
                list[i].ResetOffset();
            }
            if (_owner?.Config != null)
            {
                _owner.Config.ResetSubElementOffsets();
            }
        }

        public void RecaptureDefaultPositions()
        {
            var list = All;
            for (int i = 0; i < list.Count; i++)
            {
                list[i].RecaptureDefaultPosition();
            }
        }

        public WidgetGenericSubElementControl Wrap(string id, string displayName, GameObject rootGo, Action<ThemeConfig> onApplyTheme = null, Action<IFlightTelemetry> onUpdateTelemetry = null)
            => WidgetControlManager.WrapElement(_owner, id, displayName, rootGo, onApplyTheme, onUpdateTelemetry);

        public WidgetHeaderControl AddHeader(string id, string title, string subtitle = "", Vector2? pos = null, Vector2? size = null, string badge = "")
            => WidgetControlManager.CreateHeader(_owner, id, title, subtitle, pos, size, badge);

        public WidgetReadoutControl AddReadout(string id, string displayName, Vector2 size, Vector2 pos, string token, string title = "", string unit = "")
            => WidgetControlManager.CreateReadout(_owner, id, displayName, size, pos, token, title, unit);

        public WidgetLinearBarControl AddLinearBar(string id, string displayName, Vector2 size, Vector2 pos, string token, double min = 0.0, double max = 100.0, bool isVertical = false)
            => WidgetControlManager.CreateLinearBar(_owner, id, displayName, size, pos, token, min, max, isVertical);

        public WidgetActionButtonControl AddButton(string id, string displayName, Vector2 size, Vector2 pos, string label, Action onClick, bool isToggle = false)
            => WidgetControlManager.CreateButton(_owner, id, displayName, size, pos, label, onClick, isToggle);
    }
}
