using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 现代化遥测参数速查与智能填槽抽屉 (Telemetry Parameter Drawer & Fast Picker)
    /// 解决 736+ 参数难以检索、123页低效翻页与多通道手动输入的痛点。
    /// 特性：
    /// 1. 左侧结构化分类垂直导航 (带各类别参数计数徽章)。
    /// 2. 顶部实时模糊快搜 (支持多关键词、Token、显示名、说明过滤)。
    /// 3. 实机 4Hz 定频解算采样值实时呈现。
    /// 4. 一键回填到目标通道/槽位 (OnParamSelected 回调) 或复制到系统剪贴板。
    /// </summary>
    public static class TelemetryParamDrawer
    {
        private static bool _isOpen = false;
        public static bool IsOpen => _isOpen;

        private static string _targetSlotName = "";
        public static string TargetSlotName => _targetSlotName;

        private static Action<string> _onParamSelected = null;

        // 筛选与检索状态
        private static string _searchQuery = "";
        private static string _lastSearchQuery = null;
        private static int _selectedCategoryIndex = 0;
        private static int _lastSelectedCategoryIndex = -1;

        private static Vector2 _categoryScroll = Vector2.zero;
        private static Vector2 _listScroll = Vector2.zero;

        private const int PageSize = 14;
        private static int _currentPage = 0;

        private static readonly List<TelemetryParam> _filteredList = new List<TelemetryParam>();

        // 4Hz 定频遥测采样求值缓存
        private static float _lastEvalTime = 0f;
        private static readonly Dictionary<string, string> _evalCache = new Dictionary<string, string>();

        public static void Open(string targetSlotName, Action<string> onSelect)
        {
            _isOpen = true;
            _targetSlotName = targetSlotName ?? "";
            _onParamSelected = onSelect;
            _currentPage = 0;
            _lastSearchQuery = null; // 触发刷新
            UpdateFilter();
        }

        public static void Close()
        {
            _isOpen = false;
            _targetSlotName = "";
            _onParamSelected = null;
        }

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();
            UpdateFilter();

            MFPGuiSkin.BeginCard();

            // 1. 抽屉顶栏 (标题、当前目标插槽与退出按钮)
            GUILayout.BeginHorizontal();
            string slotDesc = string.IsNullOrEmpty(_targetSlotName) ? "" : $"<color=#{MFPGuiSkin.HexAccentAmber}>[{_targetSlotName}]</color>";
            MFPGuiSkin.DrawHeader(
                $"{I18n.Tr("DRAWER_TITLE", "📖 遥测字典速查与智能填槽")} {slotDesc}",
                I18n.Tr("DRAWER_SUBTITLE", "支持全量遥测快搜、实时采样读数。点击「✔ 选用」即可直接填入。")
            );

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(I18n.Tr("DRAWER_BTN_BACK", "✕ 返回组件配置"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(130f), GUILayout.Height(26f)))
            {
                Close();
            }
            GUILayout.EndHorizontal();

            // 2. 搜索框
            MFPGuiSkin.DrawSearchBar(ref _searchQuery, I18n.Tr("DRAWER_SEARCH_PLACEHOLDER", "快速搜索遥测参数名、Token 标识 (如 ALT, SPD, MACH, TWR, PROP)..."));

            GUILayout.Space(6f);

            // 3. 主体分栏：左侧垂直分类栏 (180px) + 右侧参数表格
            float drawerContentH = Mathf.Max(260f, SettingsGUI.ContentHeight - 110f);
            GUILayout.BeginHorizontal(GUILayout.Height(drawerContentH));

            // ==========================================
            // 左子栏：垂直分类导航
            // ==========================================
            GUILayout.BeginVertical(GUILayout.Width(185f), GUILayout.Height(drawerContentH));
            MFPGuiSkin.BeginInset();
            GUILayout.Label($"<b><size=11>{I18n.Tr("DRAWER_CAT_HEADER", "📂 参数分类导航")}</size></b>");
            GUILayout.Space(2f);

            _categoryScroll = GUILayout.BeginScrollView(_categoryScroll, GUILayout.Height(drawerContentH - 32f));
            for (int i = 0; i < TelemetryCatalog.Categories.Length; i++)
            {
                bool isSelected = (_selectedCategoryIndex == i);
                GUIStyle catBtnStyle = isSelected ? MFPGuiSkin.RowSelectedStyle : MFPGuiSkin.RowNormalStyle;
                string catName = TelemetryCatalog.Categories[i];

                int countInCat = GetCountForCategory(i);
                string catLabel = $"{catName} <color=#{MFPGuiSkin.HexAccentCyan}><size=10>({countInCat})</size></color>";

                if (GUILayout.Button(catLabel, catBtnStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    _selectedCategoryIndex = i;
                    _currentPage = 0;
                }
            }
            GUILayout.EndScrollView();
            MFPGuiSkin.EndInset();
            GUILayout.EndVertical();

            GUILayout.Space(6f);

            // ==========================================
            // 右子栏：参数列表与实时采样
            // ==========================================
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.Height(drawerContentH));
            MFPGuiSkin.BeginInset();

            int totalMatches = _filteredList.Count;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)totalMatches / PageSize));
            _currentPage = Mathf.Clamp(_currentPage, 0, totalPages - 1);

            int startIdx = _currentPage * PageSize;
            int endIdx = Mathf.Min(startIdx + PageSize, totalMatches);

            // 采样刷新 (4Hz)
            bool refreshSample = (Time.unscaledTime - _lastEvalTime > 0.25f);
            if (refreshSample)
            {
                _lastEvalTime = Time.unscaledTime;
            }

            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Height(drawerContentH - 44f));

            if (totalMatches == 0)
            {
                GUILayout.Space(20f);
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=12>{I18n.Tr("DRAWER_NO_MATCH", "未找到符合条件的遥测参数，请尝试其它关键词。")}</size></color>");
            }
            else
            {
                for (int i = startIdx; i < endIdx; i++)
                {
                    var param = _filteredList[i];
                    DrawParamCard(param, refreshSample);
                }
            }

            GUILayout.EndScrollView();

            // 底部翻页控制条
            GUILayout.BeginHorizontal();
            GUI.enabled = _currentPage > 0;
            if (GUILayout.Button(I18n.Tr("ASM_PAGE_PREV", "◀ 上页"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(75f), GUILayout.Height(22f)))
            {
                _currentPage--;
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format(I18n.Tr("DRAWER_PAGE_INFO", "第 {0}/{1} 页 (共 {2} 条匹配)"), _currentPage + 1, totalPages, totalMatches));
            GUILayout.FlexibleSpace();

            GUI.enabled = _currentPage < totalPages - 1;
            if (GUILayout.Button(I18n.Tr("ASM_PAGE_NEXT", "下页 ▶"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(75f), GUILayout.Height(22f)))
            {
                _currentPage++;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndInset();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        private static void DrawParamCard(TelemetryParam p, bool refreshSample)
        {
            if (p == null) return;

            GUILayout.BeginHorizontal(MFPGuiSkin.RowNormalStyle);

            // 左侧：名称、Token 徽章、当前实时采样
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();

            // 友好显示名
            GUILayout.Label($"<b>{p.DisplayName}</b>", GUILayout.Width(150f));

            // Token 徽章
            MFPGuiSkin.DrawBadge(p.Token, MFPGuiSkin.AccentCyan, new Color(0.00f, 0.25f, 0.40f, 0.9f));

            // 实时采样值
            string sampleVal;
            if (refreshSample || !_evalCache.TryGetValue(p.Token, out sampleVal))
            {
                try
                {
                    sampleVal = TelemetryTokenEngine.Evaluate(p.Token, TelemetryHub.Instance);
                    if (string.IsNullOrEmpty(sampleVal)) sampleVal = "---";
                }
                catch
                {
                    sampleVal = "ERR";
                }
                _evalCache[p.Token] = sampleVal;
            }

            GUILayout.Space(8f);
            string unitSuffix = string.IsNullOrEmpty(p.DefaultUnit) ? "" : $" <size=9>{p.DefaultUnit}</size>";
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentGreen}><b>{sampleVal}</b></color>{unitSuffix}", GUILayout.Width(130f));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 描述信息
            if (!string.IsNullOrEmpty(p.Description))
            {
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=10>{p.Description}</size></color>");
            }

            GUILayout.EndVertical();

            // 右侧操作按钮
            GUILayout.BeginHorizontal(GUILayout.Width(155f));

            // 一键选用填槽
            if (GUILayout.Button(I18n.Tr("DRAWER_BTN_APPLY", "✔ 选用"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(72f), GUILayout.Height(24f)))
            {
                if (_onParamSelected != null)
                {
                    _onParamSelected.Invoke(p.Token);
                    MFPGuiSkin.ShowToast(string.Format(I18n.Tr("DRAWER_TOAST_APPLIED", "已填入参数: {0}"), p.Token));
                }
                Close();
            }

            GUILayout.Space(4f);

            // 复制到剪贴板
            if (GUILayout.Button(I18n.Tr("DRAWER_BTN_COPY", "📋 复制"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(68f), GUILayout.Height(24f)))
            {
                GUIUtility.systemCopyBuffer = p.Token;
                MFPGuiSkin.ShowToast(string.Format(I18n.Tr("DRAWER_TOAST_COPIED", "已复制 {0} 到剪贴板"), p.Token));
            }

            GUILayout.EndHorizontal();

            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        private static int GetCountForCategory(int catIndex)
        {
            if (catIndex == 0) return TelemetryCatalog.Parameters.Count;
            string catName = TelemetryCatalog.Categories[catIndex];
            int count = 0;
            for (int i = 0; i < TelemetryCatalog.Parameters.Count; i++)
            {
                if (TelemetryCatalog.Parameters[i].Category == catName) count++;
            }
            return count;
        }

        private static void UpdateFilter()
        {
            if (_searchQuery == _lastSearchQuery && _selectedCategoryIndex == _lastSelectedCategoryIndex)
            {
                return;
            }

            _lastSearchQuery = _searchQuery;
            _lastSelectedCategoryIndex = _selectedCategoryIndex;
            _filteredList.Clear();

            string currentCategory = TelemetryCatalog.Categories[_selectedCategoryIndex];
            bool matchCat = (_selectedCategoryIndex != 0);
            bool hasQuery = !string.IsNullOrEmpty(_searchQuery);

            string[] terms = null;
            if (hasQuery)
            {
                terms = _searchQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }

            for (int i = 0; i < TelemetryCatalog.Parameters.Count; i++)
            {
                var p = TelemetryCatalog.Parameters[i];
                if (matchCat && p.Category != currentCategory) continue;

                if (terms != null && terms.Length > 0)
                {
                    bool matchAll = true;
                    for (int t = 0; t < terms.Length; t++)
                    {
                        string term = terms[t];
                        bool termMatch = (p.DisplayName != null && p.DisplayName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                                      || (p.Token != null && p.Token.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                                      || (p.Description != null && p.Description.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                                      || (p.Category != null && p.Category.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!termMatch)
                        {
                            matchAll = false;
                            break;
                        }
                    }
                    if (!matchAll) continue;
                }

                _filteredList.Add(p);
            }

            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)_filteredList.Count / PageSize));
            if (_currentPage >= totalPages) _currentPage = 0;
        }
    }
}
