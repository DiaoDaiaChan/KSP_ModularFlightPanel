using System;
using System.Collections.Generic;

namespace ModularFlightPanel.Config
{
    /// <summary>
    /// 收纳坞按钮自定义过滤与别名配置项 (Dock Button Rule)
    /// </summary>
    [Serializable]
    public class DockButtonRule
    {
        public string Key = "";
        public string DefaultName = "";
        public string CustomLabel = "";
        public bool IsVisible = true;
        public bool IsFavorite = false;

        public string ButtonKey { get => Key; set => Key = value; }
        public string DisplayName { get => string.IsNullOrEmpty(CustomLabel) ? DefaultName : CustomLabel; set => CustomLabel = value; }
    }

    /// <summary>
    /// 主题与全局航电工作台配置契约 (Theme & Global Avionics Settings Data Contract)
    /// 纯 C# 解耦数据模型，无 UnityEngine 依赖，用于持久化、无头门禁验证与跨版本双向往返。
    /// </summary>
    [Serializable]
    public class ThemeSettingsData
    {
        public string SelectedThemeId = "modern_aero";
        public string SelectedLanguage = "auto"; // "auto", "zh-CN", "en-US"
        public int RenderMode = 1; // 0 = StockTexture, 1 = ProceduralVector, 2 = ProceduralBake, 3 = StockDirect
        public bool HideStockNavball = true;
        public bool HideStockAltimeter = false;
        public bool HideStockBottomLeft = false;
        public bool HideStockTimeWarp = false;
        public bool HideStockCommNet = false;
        public bool HideStockToolbar = false;
        public int ToolbarStyleMode = 1; // 0 = Stock, 1 = Reskin, 2 = ModernWidget
        public bool MasterBypass = false;
        public bool ShowPerformanceBadge = false;

        // 自适应渲染分辨率与超采样倍率设置 (Smart Resolution & Supersampling)
        public bool AutoAdaptResolution = true;
        public float GlobalRenderScaleMultiplier = 1.0f;

        // 收纳坞按钮自定义过滤与别名配置
        public List<DockButtonRule> DockRules = new List<DockButtonRule>();
        public bool DockShowHiddenDrawer = false;
        public int DockOrientation = 0; // 0 = 纵向双列, 1 = 横向双行, 2 = 横向单行

        // 常用 MOD 独立快捷面板设置
        public bool DockEnableFavoritePanel = true;
        public int DockFavoriteOrientation = 1; // 0 = 纵向单列, 1 = 横向单行, 2 = 横向双行
        public bool DockKeepFavoritesInMain = false;
        public float DockFavoritePosX = 0f;
        public float DockFavoritePosY = -380f;

        // Alt+N 航电工作台窗口几何状态持久化 (Window Geometry Persistence)
        public float SettingsWindowX = -1f;
        public float SettingsWindowY = -1f;
        public float SettingsWindowWidth = 1040f;
        public float SettingsWindowHeight = 650f;
        public bool SettingsWindowMaximized = false;
    }
}
