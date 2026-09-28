using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.HUD;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 编辑模式小组件剪贴板与复用中枢 (Widget Clipboard & Cloning Engine)
    /// 支持单选/多选组件的复制 (Ctrl+C)、粘贴 (Ctrl+V) 与快捷克隆 (Ctrl+D)。
    /// 自动分配全局唯一 WidgetId、智能递增偏移排版，并记录撤销历史。
    /// </summary>
    public static class WidgetClipboardManager
    {
        private static readonly List<WidgetConfig> _clipboard = new List<WidgetConfig>();
        private static int _consecutivePasteCount = 0;

        public static int ClipboardCount => _clipboard.Count;
        public static bool HasData => _clipboard.Count > 0;

        /// <summary>
        /// 复制指定或当前选中的小组件到剪贴板
        /// </summary>
        public static void CopySelected(IEnumerable<BaseFlightWidget> targets = null)
        {
            var list = (targets ?? WidgetSelectionManager.SelectedWidgets).Where(w => w != null && w.Config != null).ToList();
            if (list.Count == 0)
            {
                MFPToastBridge.Show(I18n.Tr("TOAST_CLIPBOARD_NO_SELECTION", "未选择可复制的小组件"));
                return;
            }

            _clipboard.Clear();
            foreach (var w in list)
            {
                _clipboard.Add(w.Config.Clone());
            }

            _consecutivePasteCount = 0;
            MFPToastBridge.Show(I18n.TrFormat("TOAST_CLIPBOARD_COPIED", _clipboard.Count));
        }

        /// <summary>
        /// 从剪贴板粘贴小组件实例
        /// </summary>
        public static void Paste()
        {
            if (_clipboard.Count == 0)
            {
                MFPToastBridge.Show(I18n.Tr("TOAST_CLIPBOARD_EMPTY", "剪贴板为空，请先按 Ctrl+C 复制组件"));
                return;
            }

            var layout = WidgetLayoutManager.Instance?.CurrentLayout;
            if (layout == null || layout.Widgets == null) return;

            _consecutivePasteCount++;
            Vector2 stepOffset = new Vector2(24f * _consecutivePasteCount, -24f * _consecutivePasteCount);

            var createdIds = new List<string>();

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_PASTE_WIDGETS", "粘贴小组件"), () =>
            {
                foreach (var srcCfg in _clipboard)
                {
                    string uniqueId = GenerateUniqueWidgetId(srcCfg.WidgetId, srcCfg.WidgetType, layout);
                    var newCfg = srcCfg.Clone(uniqueId, stepOffset.x, stepOffset.y);
                    newCfg.IsEnabled = true;

                    // 确保 WidgetType 绝不为空，方便泛型工厂精准实例化
                    if (string.IsNullOrEmpty(newCfg.WidgetType))
                    {
                        newCfg.WidgetType = srcCfg.WidgetType;
                    }

                    layout.Widgets.Add(newCfg);
                    createdIds.Add(uniqueId);
                }

                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();

                // 重建后自动框选新粘贴出的所有组件
                if (FlightHUDManager.Instance?.ModularWidgets != null)
                {
                    var newWidgets = FlightHUDManager.Instance.ModularWidgets
                        .Where(w => w != null && createdIds.Contains(w.WidgetId, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                    WidgetSelectionManager.SetSelection(newWidgets);
                }
            });

            MFPToastBridge.Show(I18n.TrFormat("TOAST_CLIPBOARD_PASTED", createdIds.Count));
        }

        /// <summary>
        /// 快捷克隆选中的组件 (Ctrl+D)
        /// </summary>
        public static void DuplicateSelected(IEnumerable<BaseFlightWidget> targets = null)
        {
            var list = (targets ?? WidgetSelectionManager.SelectedWidgets).Where(w => w != null && w.Config != null).ToList();
            if (list.Count == 0) return;

            CopySelected(list);
            Paste();
        }

        /// <summary>
        /// 为克隆或新建组件生成全局唯一的 WidgetId
        /// </summary>
        public static string GenerateUniqueWidgetId(string baseId, string typeName, WidgetLayoutData layout)
        {
            string prefix = baseId;
            if (string.IsNullOrEmpty(prefix))
            {
                prefix = !string.IsNullOrEmpty(typeName) ? typeName : "widget";
            }

            // 清理末尾现有的 _copy 或 _copyX
            int copyIdx = prefix.IndexOf("_copy", StringComparison.OrdinalIgnoreCase);
            if (copyIdx > 0)
            {
                prefix = prefix.Substring(0, copyIdx);
            }

            string candidate = $"{prefix}_copy";
            int counter = 1;
            while (layout != null && layout.Widgets != null && layout.Widgets.Exists(w => string.Equals(w.WidgetId, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{prefix}_copy{counter++}";
            }
            return candidate;
        }
    }
}
