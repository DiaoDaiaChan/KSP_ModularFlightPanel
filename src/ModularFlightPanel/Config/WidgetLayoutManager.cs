using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ModularFlightPanel.Config
{
    [Serializable]
    public class WidgetLayoutData
    {
        public float GlobalScale = 1.0f;
        public List<WidgetConfig> Widgets = new List<WidgetConfig>();
    }

    public class WidgetLayoutManager
    {
        private static WidgetLayoutManager _instance;
        public static WidgetLayoutManager Instance => _instance ?? (_instance = new WidgetLayoutManager());

        public WidgetLayoutData CurrentLayout { get; private set; } = new WidgetLayoutData();

        private string ConfigPath => Path.Combine(KSPUtil.ApplicationRootPath, "GameData/ModularFlightPanel/PluginData/layout.json");

        public void Initialize()
        {
            LoadLayout();
        }

        public void LoadLayout()
        {
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    CurrentLayout = JsonUtility.FromJson<WidgetLayoutData>(json);
                    if (CurrentLayout != null && CurrentLayout.Widgets != null && CurrentLayout.Widgets.Count > 0)
                    {
                        Debug.Log($"[ModularFlightPanel] Successfully loaded layout with {CurrentLayout.Widgets.Count} widgets.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModularFlightPanel] Error reading layout config: {ex.Message}");
                }
            }

            // 初始化默认布局 (含预置核心组件及通配符示例组件)
            CreateDefaultLayout();
            SaveLayout();
        }

        public void SaveLayout()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonUtility.ToJson(CurrentLayout, true);
                File.WriteAllText(ConfigPath, json);
                Debug.Log("[ModularFlightPanel] Saved widget layout to layout.json");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModularFlightPanel] Failed to save layout: {ex.Message}");
            }
        }

        private void CreateDefaultLayout()
        {
            CurrentLayout = new WidgetLayoutData { GlobalScale = 1.0f };

            // 核心组件配置 (参考 ksp2_ref.png 空间布局，预留间隙避免互相遮挡)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.navball", "姿态球 (Navball)", 0f, 0f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.throttle", "油门推力弧形表", 0f, 0f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.vsi", "垂直速度弧形表", 0f, 0f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.propellant", "推进剂消耗弧形表", 0f, 0f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.speed_box", "速度读数盒", -165f, -10f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.alt_box", "高度读数盒", 165f, -10f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.bottom_controls", "RCS与SAS底控", 0f, -145f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.orbital_info", "轨道数据面板", 0f, -185f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.sas_dial", "环形 SAS 罗盘", 265f, -20f));

            // 通配符扩展小组件示例 (默认不遮挡核心飞行面板，用户可在设置界面自由添加开启并拖拽)
            CurrentLayout.Widgets.Add(new WidgetConfig("custom.aero", "大气与气动参数", -360f, 110f, 1.0f, "动压: {Q} | 马赫: {MACH} | G: {GFORCE}") { IsEnabled = false });
            CurrentLayout.Widgets.Add(new WidgetConfig("custom.power", "推重比与推进", 360f, 110f, 1.0f, "TWR: {TWR:F2} | 燃料: {PROP} | 油门: {THROTTLE}") { IsEnabled = false });
        }

        public WidgetConfig GetConfig(string widgetId)
        {
            return CurrentLayout.Widgets.Find(w => w.WidgetId == widgetId);
        }

        public void AddCustomWidget(string title, string template, Vector2 initialPos)
        {
            string id = "custom." + Guid.NewGuid().ToString().Substring(0, 8);
            CurrentLayout.Widgets.Add(new WidgetConfig(id, title, initialPos.x, initialPos.y, 1.0f, template));
            SaveLayout();
        }

        public void RemoveWidget(string widgetId)
        {
            CurrentLayout.Widgets.RemoveAll(w => w.WidgetId == widgetId);
            SaveLayout();
        }
    }
}
