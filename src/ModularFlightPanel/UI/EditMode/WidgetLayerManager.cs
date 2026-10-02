using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 专业图形软件级图层堆叠与绘制顺序控制中枢 (Photoshop/Figma-Grade Layer & Drawing Order Engine)
    /// 核心架构职责：
    /// 1. 严格维系 UGUI Hierarchy 绘制契约：
    ///    - Sibling Index 0: CanvasBlueprintGrid (蓝图背景网格)
    ///    - Sibling Index 1: MarqueeSelectionCatcher (全屏框选捕获器)
    ///    - Sibling Index 2 ~ 2+N-1: 各飞行仪表小组件 (按照 DrawOrder 升序排列，0为最底层，N-1为最顶层)
    ///    - Sibling Index 2+N: WidgetSmartGuides (智能磁吸参考线)
    ///    - Sibling Index 2+N+1: WidgetTransformGizmo (8点变换包围盒与旋转操纵器)
    /// 2. 毫秒级多选图层跃迁：置顶 (BringToFront)、置底 (SendToBack)、上移一层 (BringForward)、下移一层 (SendBackward)。
    /// 3. 图层安全锁定 (Layer Lock)：锁定后穿透点击、禁止意外位移与形变，完美保护大型仪表底盘与框体。
    /// 4. 浮动图层堆叠抽屉 (Floating Layer Panel)：仿 Photoshop/Figma 倒序堆叠视图（顶层在最上，底层在最下）。
    /// 5. 与多级撤销/重做栈 (WidgetEditHistory) 100% 双向联动。
    /// </summary>
    public static class WidgetLayerManager
    {
        public const int WidgetBaseOffset = 2;

        public static event Action OnLayersChanged;

        // 浮动图层面板交互状态
        public static bool IsLayerPanelOpen = false;
        private static Rect _layerPanelRect = new Rect(0, 0, 260f, 380f);
        private static Vector2 _panelScrollPos = Vector2.zero;
        private static bool _rectInitialized = false;

        public static int TotalLayers
        {
            get
            {
                if (FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null && FlightHUDManager.Instance.ModularWidgets.Count > 0)
                {
                    return FlightHUDManager.Instance.ModularWidgets.Count;
                }
                return WidgetLayoutManager.Instance != null && WidgetLayoutManager.Instance.CurrentLayout != null && WidgetLayoutManager.Instance.CurrentLayout.Widgets != null
                    ? WidgetLayoutManager.Instance.CurrentLayout.Widgets.Count
                    : 0;
            }
        }

        public static void ToggleLayerPanel()
        {
            IsLayerPanelOpen = !IsLayerPanelOpen;
            if (IsLayerPanelOpen)
            {
                MFPToastBridge.Show(I18n.Tr("TOAST_LAYER_PANEL_OPEN", "📑 图层面板已开启 (按 L 可快速关闭)"));
            }
        }

        public static int GetLayerIndex(BaseFlightWidget w)
        {
            if (w == null || w.Config == null) return 0;
            return Mathf.Clamp(w.Config.DrawOrder, 0, Math.Max(0, TotalLayers - 1));
        }

        public static int GetLayerNumber(BaseFlightWidget w)
        {
            return GetLayerIndex(w) + 1;
        }

        public static int GetLayerNumber(WidgetConfig cfg)
        {
            if (cfg == null) return 1;
            return Mathf.Clamp(cfg.DrawOrder + 1, 1, Math.Max(1, TotalLayers));
        }

        public static List<BaseFlightWidget> GetWidgetsBottomToTop()
        {
            if (FlightHUDManager.Instance == null || FlightHUDManager.Instance.ModularWidgets == null) return new List<BaseFlightWidget>();
            return FlightHUDManager.Instance.ModularWidgets
                .Where(w => w != null && w.Config != null)
                .OrderBy(w => w.Config.DrawOrder)
                .ToList();
        }

        public static List<BaseFlightWidget> GetWidgetsTopToBottom()
        {
            if (FlightHUDManager.Instance == null || FlightHUDManager.Instance.ModularWidgets == null) return new List<BaseFlightWidget>();
            return FlightHUDManager.Instance.ModularWidgets
                .Where(w => w != null && w.Config != null)
                .OrderByDescending(w => w.Config.DrawOrder)
                .ToList();
        }

        public static List<WidgetConfig> GetConfigsBottomToTop()
        {
            if (WidgetLayoutManager.Instance == null || WidgetLayoutManager.Instance.CurrentLayout == null || WidgetLayoutManager.Instance.CurrentLayout.Widgets == null) return new List<WidgetConfig>();
            return WidgetLayoutManager.Instance.CurrentLayout.Widgets
                .OrderBy(c => c.DrawOrder)
                .ToList();
        }

        public static List<WidgetConfig> GetConfigsTopToBottom()
        {
            if (WidgetLayoutManager.Instance == null || WidgetLayoutManager.Instance.CurrentLayout == null || WidgetLayoutManager.Instance.CurrentLayout.Widgets == null) return new List<WidgetConfig>();
            return WidgetLayoutManager.Instance.CurrentLayout.Widgets
                .OrderByDescending(c => c.DrawOrder)
                .ToList();
        }

        #region Layer Reorder Operations (Runtime Widgets)

        /// <summary>
        /// 将指定组件集合移至最顶层 (DrawOrder 最大)
        /// </summary>
        public static void BringToFront(IEnumerable<BaseFlightWidget> targetWidgets)
        {
            if (targetWidgets == null) return;
            var targets = targetWidgets.Where(w => w != null && w.Config != null).ToList();
            if (targets.Count == 0) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_BRING_FRONT", "置于顶层"), () =>
            {
                var all = GetWidgetsBottomToTop();
                var nonTargets = all.Except(targets).ToList();

                int order = 0;
                foreach (var w in nonTargets) w.Config.DrawOrder = order++;
                foreach (var w in targets) w.Config.DrawOrder = order++;

                NormalizeAndSyncLayers(false);
            });

            MFPToastBridge.Show(I18n.Tr("TOAST_BRING_FRONT", "⤒ 已置于顶层"));
        }

        public static void BringToFront(BaseFlightWidget widget)
        {
            if (widget != null) BringToFront(new[] { widget });
        }

        /// <summary>
        /// 将指定组件集合移至最底层 (DrawOrder 最小)
        /// </summary>
        public static void SendToBack(IEnumerable<BaseFlightWidget> targetWidgets)
        {
            if (targetWidgets == null) return;
            var targets = targetWidgets.Where(w => w != null && w.Config != null).ToList();
            if (targets.Count == 0) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_SEND_BACK", "置于底层"), () =>
            {
                var all = GetWidgetsBottomToTop();
                var nonTargets = all.Except(targets).ToList();

                int order = 0;
                foreach (var w in targets) w.Config.DrawOrder = order++;
                foreach (var w in nonTargets) w.Config.DrawOrder = order++;

                NormalizeAndSyncLayers(false);
            });

            MFPToastBridge.Show(I18n.Tr("TOAST_SEND_BACK", "⤓ 已置于底层"));
        }

        public static void SendToBack(BaseFlightWidget widget)
        {
            if (widget != null) SendToBack(new[] { widget });
        }

        /// <summary>
        /// 将指定组件集合逐级上移一层 (Bring Forward)
        /// </summary>
        public static void BringForward(IEnumerable<BaseFlightWidget> targetWidgets)
        {
            if (targetWidgets == null) return;
            var targets = targetWidgets.Where(w => w != null && w.Config != null).ToList();
            if (targets.Count == 0) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_BRING_FORWARD", "上移一层"), () =>
            {
                var all = GetWidgetsBottomToTop();
                for (int i = all.Count - 2; i >= 0; i--)
                {
                    if (targets.Contains(all[i]) && !targets.Contains(all[i + 1]))
                    {
                        var temp = all[i];
                        all[i] = all[i + 1];
                        all[i + 1] = temp;
                    }
                }

                for (int i = 0; i < all.Count; i++)
                {
                    all[i].Config.DrawOrder = i;
                }

                NormalizeAndSyncLayers(false);
            });

            MFPToastBridge.Show(I18n.Tr("TOAST_BRING_FORWARD", "▲ 已上移一层"));
        }

        public static void BringForward(BaseFlightWidget widget)
        {
            if (widget != null) BringForward(new[] { widget });
        }

        /// <summary>
        /// 将指定组件集合逐级下移一层 (Send Backward)
        /// </summary>
        public static void SendBackward(IEnumerable<BaseFlightWidget> targetWidgets)
        {
            if (targetWidgets == null) return;
            var targets = targetWidgets.Where(w => w != null && w.Config != null).ToList();
            if (targets.Count == 0) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_SEND_BACKWARD", "下移一层"), () =>
            {
                var all = GetWidgetsBottomToTop();
                for (int i = 1; i < all.Count; i++)
                {
                    if (targets.Contains(all[i]) && !targets.Contains(all[i - 1]))
                    {
                        var temp = all[i];
                        all[i] = all[i - 1];
                        all[i - 1] = temp;
                    }
                }

                for (int i = 0; i < all.Count; i++)
                {
                    all[i].Config.DrawOrder = i;
                }

                NormalizeAndSyncLayers(false);
            });

            MFPToastBridge.Show(I18n.Tr("TOAST_SEND_BACKWARD", "▼ 已下移一层"));
        }

        public static void SendBackward(BaseFlightWidget widget)
        {
            if (widget != null) SendBackward(new[] { widget });
        }

        #endregion

        #region Layer Reorder Operations (Config Level Fallback)

        public static void BringToFront(WidgetConfig cfg)
        {
            if (cfg == null) return;
            var widget = FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null
                ? FlightHUDManager.Instance.ModularWidgets.FirstOrDefault(w => w.Config == cfg || w.WidgetId == cfg.WidgetId)
                : null;
            if (widget != null) { BringToFront(widget); return; }

            var list = GetConfigsBottomToTop();
            list.Remove(cfg);
            list.Add(cfg);
            for (int i = 0; i < list.Count; i++) list[i].DrawOrder = i;
            WidgetLayoutManager.Instance?.SaveLayout();
            OnLayersChanged?.Invoke();
        }

        public static void SendToBack(WidgetConfig cfg)
        {
            if (cfg == null) return;
            var widget = FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null
                ? FlightHUDManager.Instance.ModularWidgets.FirstOrDefault(w => w.Config == cfg || w.WidgetId == cfg.WidgetId)
                : null;
            if (widget != null) { SendToBack(widget); return; }

            var list = GetConfigsBottomToTop();
            list.Remove(cfg);
            list.Insert(0, cfg);
            for (int i = 0; i < list.Count; i++) list[i].DrawOrder = i;
            WidgetLayoutManager.Instance?.SaveLayout();
            OnLayersChanged?.Invoke();
        }

        public static void BringForward(WidgetConfig cfg)
        {
            if (cfg == null) return;
            var widget = FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null
                ? FlightHUDManager.Instance.ModularWidgets.FirstOrDefault(w => w.Config == cfg || w.WidgetId == cfg.WidgetId)
                : null;
            if (widget != null) { BringForward(widget); return; }

            var list = GetConfigsBottomToTop();
            int idx = list.IndexOf(cfg);
            if (idx >= 0 && idx < list.Count - 1)
            {
                var next = list[idx + 1];
                list[idx + 1] = cfg;
                list[idx] = next;
                for (int i = 0; i < list.Count; i++) list[i].DrawOrder = i;
                WidgetLayoutManager.Instance?.SaveLayout();
                OnLayersChanged?.Invoke();
            }
        }

        public static void SendBackward(WidgetConfig cfg)
        {
            if (cfg == null) return;
            var widget = FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null
                ? FlightHUDManager.Instance.ModularWidgets.FirstOrDefault(w => w.Config == cfg || w.WidgetId == cfg.WidgetId)
                : null;
            if (widget != null) { SendBackward(widget); return; }

            var list = GetConfigsBottomToTop();
            int idx = list.IndexOf(cfg);
            if (idx > 0)
            {
                var prev = list[idx - 1];
                list[idx - 1] = cfg;
                list[idx] = prev;
                for (int i = 0; i < list.Count; i++) list[i].DrawOrder = i;
                WidgetLayoutManager.Instance?.SaveLayout();
                OnLayersChanged?.Invoke();
            }
        }

        #endregion

        #region Layer Lock / Visibility Controls

        public static void ToggleLock(BaseFlightWidget widget)
        {
            if (widget?.Config == null) return;
            SetLock(widget, !widget.Config.IsLocked);
        }

        public static void SetLock(BaseFlightWidget widget, bool locked)
        {
            if (widget?.Config == null) return;
            widget.Config.IsLocked = locked;
            widget.DragHandler?.UpdateSelectionAppearance();

            string toastText = locked
                ? I18n.TrFormat("TOAST_LAYER_LOCKED_FMT", widget.DisplayName)
                : I18n.TrFormat("TOAST_LAYER_UNLOCKED_FMT", widget.DisplayName);
            MFPToastBridge.Show(toastText);

            WidgetLayoutManager.Instance?.SaveLayout();
            WidgetTransformGizmo.Instance?.UpdateGizmoPosition();
            OnLayersChanged?.Invoke();
        }

        public static void ToggleLock(WidgetConfig cfg)
        {
            if (cfg == null) return;
            var widget = FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null
                ? FlightHUDManager.Instance.ModularWidgets.FirstOrDefault(w => w.Config == cfg || w.WidgetId == cfg.WidgetId)
                : null;
            if (widget != null)
            {
                ToggleLock(widget);
            }
            else
            {
                cfg.IsLocked = !cfg.IsLocked;
                WidgetLayoutManager.Instance?.SaveLayout();
                OnLayersChanged?.Invoke();
            }
        }

        public static void ToggleVisibility(BaseFlightWidget widget)
        {
            if (widget?.Config == null) return;
            bool newState = !widget.Config.IsEnabled;
            widget.Config.IsEnabled = newState;
            widget.gameObject.SetActive(newState);
            if (!newState)
            {
                WidgetSelectionManager.Deselect(widget);
            }
            WidgetLayoutManager.Instance?.SaveLayout();
            OnLayersChanged?.Invoke();
        }

        public static void ToggleVisibility(WidgetConfig cfg)
        {
            if (cfg == null) return;
            var widget = FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null
                ? FlightHUDManager.Instance.ModularWidgets.FirstOrDefault(w => w.Config == cfg || w.WidgetId == cfg.WidgetId)
                : null;
            if (widget != null)
            {
                ToggleVisibility(widget);
            }
            else
            {
                cfg.IsEnabled = !cfg.IsEnabled;
                WidgetLayoutManager.Instance?.SaveLayout();
                OnLayersChanged?.Invoke();
            }
        }

        #endregion

        #region Layer Normalization & Sibling Hierarchy Synchronization

        /// <summary>
        /// 全量标准化所有图层绘制层级并同步至 UGUI Hierarchy 与 layout.json
        /// </summary>
        public static void NormalizeAndSyncLayers(bool recordHistory = false, string actionDesc = null)
        {
            void PerformSync()
            {
                var hudRoot = FlightHUDManager.Instance != null ? FlightHUDManager.Instance.HUDRoot : null;
                var widgets = FlightHUDManager.Instance != null ? FlightHUDManager.Instance.ModularWidgets : null;

                if (widgets != null && widgets.Count > 0)
                {
                    // 1. 按照当前 DrawOrder 稳定排序
                    var sorted = widgets.Where(w => w != null && w.Config != null)
                                        .OrderBy(w => w.Config.DrawOrder)
                                        .ToList();

                    // 2. 连续离散化赋值 0 .. N-1
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        sorted[i].Config.DrawOrder = i;
                    }

                    // 3. 同步至 UGUI Transform Hierarchy (保证 Sibling 顺序精确吻合)
                    if (hudRoot != null)
                    {
                        var grid = hudRoot.transform.Find("CanvasBlueprintGrid");
                        if (grid != null) grid.SetSiblingIndex(0);

                        var marquee = hudRoot.transform.Find("MarqueeSelectionCatcher");
                        if (marquee != null) marquee.SetSiblingIndex(1);

                        for (int i = 0; i < sorted.Count; i++)
                        {
                            sorted[i].transform.SetSiblingIndex(WidgetBaseOffset + i);
                        }

                        var guides = hudRoot.transform.Find("SmartGuides");
                        if (guides != null) guides.SetAsLastSibling();

                        var gizmo = hudRoot.transform.Find("TransformGizmo");
                        if (gizmo != null) gizmo.SetAsLastSibling();
                    }

                    // 4. 同步更新 CurrentLayout.Widgets 内部顺序
                    if (WidgetLayoutManager.Instance != null && WidgetLayoutManager.Instance.CurrentLayout != null && WidgetLayoutManager.Instance.CurrentLayout.Widgets != null)
                    {
                        WidgetLayoutManager.Instance.CurrentLayout.Widgets =
                            WidgetLayoutManager.Instance.CurrentLayout.Widgets
                            .OrderBy(c => c.DrawOrder)
                            .ToList();
                    }
                }
                else if (WidgetLayoutManager.Instance != null && WidgetLayoutManager.Instance.CurrentLayout != null && WidgetLayoutManager.Instance.CurrentLayout.Widgets != null)
                {
                    var cfgs = WidgetLayoutManager.Instance.CurrentLayout.Widgets
                        .OrderBy(c => c.DrawOrder)
                        .ToList();
                    for (int i = 0; i < cfgs.Count; i++)
                    {
                        cfgs[i].DrawOrder = i;
                    }
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets = cfgs;
                }

                WidgetLayoutManager.Instance?.SaveLayout();
                WidgetTransformGizmo.Instance?.UpdateGizmoPosition();
                OnLayersChanged?.Invoke();
            }

            if (recordHistory && !string.IsNullOrEmpty(actionDesc))
            {
                WidgetEditHistory.RecordInstantAction(actionDesc, PerformSync);
            }
            else
            {
                PerformSync();
            }
        }

        #endregion

        #region Floating Layer Panel GUI (Photoshop/Figma Style)

        /// <summary>
        /// 在屏幕右上方绘制悬浮图层管理器抽屉
        /// </summary>
        public static void DrawLayerPanel(int selCount)
        {
            if (!IsLayerPanelOpen || !WidgetDragHandler.IsEditModeActive) return;

            if (!_rectInitialized)
            {
                _layerPanelRect = new Rect(Screen.width - 275f, 100f, 265f, 380f);
                _rectInitialized = true;
            }

            _layerPanelRect.x = Mathf.Clamp(_layerPanelRect.x, 10f, Screen.width - _layerPanelRect.width - 10f);
            _layerPanelRect.y = Mathf.Clamp(_layerPanelRect.y, 10f, Screen.height - _layerPanelRect.height - 10f);

            if (_layerPanelRect.Contains(Event.current.mousePosition))
            {
                FlightHUDManager.IsMouseOverFloatingToolbar = true;
            }

            GUILayout.BeginArea(_layerPanelRect, GUI.skin.box);

            // 1. 顶栏标题 + 关闭按钮
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("LAYER_PANEL_TITLE", "📑 图层管理")}</b> <color=#94A3B8>({TotalLayers})</color>", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("✕", GUI.skin.button, GUILayout.Width(22f), GUILayout.Height(20f)))
            {
                IsLayerPanelOpen = false;
            }
            GUILayout.EndHorizontal();

            // 2. 快捷层级控制按钮条
            GUILayout.BeginHorizontal();
            GUI.enabled = selCount > 0;
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_TOP", "⤒ 顶"), GUI.skin.button, GUILayout.Height(22f)))
            {
                BringToFront(WidgetSelectionManager.SelectedWidgets);
            }
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_UP", "▲ 升"), GUI.skin.button, GUILayout.Height(22f)))
            {
                BringForward(WidgetSelectionManager.SelectedWidgets);
            }
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_DOWN", "▼ 降"), GUI.skin.button, GUILayout.Height(22f)))
            {
                SendBackward(WidgetSelectionManager.SelectedWidgets);
            }
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_BOTTOM", "⤓ 底"), GUI.skin.button, GUILayout.Height(22f)))
            {
                SendToBack(WidgetSelectionManager.SelectedWidgets);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 3. Photoshop 倒序图层堆叠列表 (顶层在上方，底层在下方)
            var widgetsTopDown = GetWidgetsTopToBottom();
            _panelScrollPos = GUILayout.BeginScrollView(_panelScrollPos, GUILayout.Height(290f));

            if (widgetsTopDown.Count == 0)
            {
                GUILayout.Label($"<color=#7088A8><size=11>{I18n.Tr("LAYER_NO_WIDGETS", "当前画布无加载的组件")}</size></color>");
            }
            else
            {
                for (int i = 0; i < widgetsTopDown.Count; i++)
                {
                    var w = widgetsTopDown[i];
                    if (w == null || w.Config == null) continue;

                    bool isSelected = WidgetSelectionManager.IsSelected(w);
                    bool isLocked = w.Config.IsLocked;
                    bool isVisible = w.Config.IsEnabled;

                    Color prevBg = GUI.backgroundColor;
                    if (isSelected)
                    {
                        GUI.backgroundColor = new Color(0.2f, 0.7f, 1f, 1f);
                    }
                    else if (isLocked)
                    {
                        GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                    }

                    GUILayout.BeginHorizontal("box");

                    // 显隐按钮 (Eye)
                    string eyeIcon = isVisible ? "<color=#00FF88>👁</color>" : "<color=#64748B>○</color>";
                    if (GUILayout.Button(eyeIcon, GUI.skin.button, GUILayout.Width(22f), GUILayout.Height(22f)))
                    {
                        ToggleVisibility(w);
                    }

                    // 锁定按钮 (Lock)
                    string lockIcon = isLocked ? "<color=#FFB703>🔒</color>" : "<color=#64748B>🔓</color>";
                    if (GUILayout.Button(lockIcon, GUI.skin.button, GUILayout.Width(22f), GUILayout.Height(22f)))
                    {
                        ToggleLock(w);
                    }

                    // 层级徽章
                    int layerNum = w.Config.DrawOrder + 1;
                    GUILayout.Label($"<color=#38BDF8><b>#{layerNum}</b></color>", GUILayout.Width(28f));

                    // 组件名称 (点击单选/多选)
                    string displayName = w.DisplayName;
                    if (displayName.Length > 9) displayName = displayName.Substring(0, 8) + "..";
                    string labelText = isLocked ? $"<color=#94A3B8>{displayName}</color>" : (isSelected ? $"<b>{displayName}</b>" : displayName);

                    if (GUILayout.Button(labelText, "label", GUILayout.ExpandWidth(true), GUILayout.Height(22f)))
                    {
                        bool isAdditive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                                          Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                        if (isAdditive)
                        {
                            WidgetSelectionManager.ToggleSelect(w);
                        }
                        else
                        {
                            WidgetSelectionManager.Select(w);
                        }
                    }

                    GUILayout.EndHorizontal();
                    GUI.backgroundColor = prevBg;
                }
            }

            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        #endregion
    }
}
