using System;
using UnityEngine;

namespace ModularFlightPanel.Config
{
    [Serializable]
    public class WidgetConfig
    {
        public string WidgetId = "unnamed_widget";
        public string DisplayName = "未命名组件";
        public bool IsEnabled = true;
        public float PositionX = 0f;
        public float PositionY = 0f;
        public float Scale = 1.0f;
        public string CustomTemplate = "";

        // 航电套件元数据 (Kit Metadata)
        public string WidgetType = "custom"; // "core", "custom", "tape", "ecam_dial"
        public string NumericToken = "{SPD}";
        public double MinValue = 0.0;
        public double MaxValue = 100.0;
        public double CautionThreshold = 80.0;
        public double WarningThreshold = 95.0;
        public bool IsSoftLimit = false; // false = 有上限型(限幅截断), true = 软上限/无上限型(爆表卡上限并告警, 读数不截断)
        public string UnitLabel = "";
        public float StepInterval = 10f;
        public bool IsLeftOrientation = true; // 标尺方向 (true=速度带左侧向右读, false=高度带右侧向左读)

        public WidgetConfig() { }

        public WidgetConfig(string id, string name, float x, float y, float scale = 1.0f, string template = "")
        {
            WidgetId = id;
            DisplayName = name;
            PositionX = x;
            PositionY = y;
            Scale = scale;
            CustomTemplate = template;
            WidgetType = id.StartsWith("tape.") ? "tape" : (id.StartsWith("ecam.") ? "ecam_dial" : (id.StartsWith("custom.") ? "custom" : "core"));
        }
    }
}
