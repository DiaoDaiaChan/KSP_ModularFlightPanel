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
        public float Rotation = 0f; // 旋转角度 (度, 0~360)
        public string CustomTemplate = "";

        // 航电套件元数据 (Kit Metadata)
        public string WidgetType = "custom"; // "core", "custom", "tape", "ecam_dial"
        public string NumericToken = "{SPD}";
        public double MinValue = 0.0;
        public double MaxValue = 100.0;
        public double CautionThreshold = 80.0;
        public double WarningThreshold = 95.0;
        public bool IsSoftLimit = false; // 旧配置兼容字段：true 等同于 LimitMode="soft"
        public string LimitMode = "hard"; // "hard"=硬上限, "soft"=软上限爆表, "none"=无上限读数
        public string UnitLabel = "";
        public float StepInterval = 10f;
        public bool IsLeftOrientation = true; // 标尺方向 (true=速度带左侧向读, false=高度带右侧向左读)

        // 渲染与性能单独优化 (Individual Render Optimization)
        public bool IsolateCanvas = true;     // 是否为此组件挂载独立 Sub-Canvas，隔离几何网格重建与 Draw Call 批处理
        public float UpdateInterval = 0f;     // 遥测求值与绘制刷新间隔 (秒, 0=每帧 60Hz+, 0.05=20Hz, 0.2=5Hz, 0.5=2Hz)

        public WidgetConfig() { }

        public WidgetConfig(string id, string name, float x, float y, float scale = 1.0f, string template = "", float rotation = 0f)
        {
            WidgetId = id;
            DisplayName = name;
            PositionX = x;
            PositionY = y;
            Scale = scale;
            CustomTemplate = template;
            Rotation = rotation;
            WidgetType = id.StartsWith("tape.") ? "tape" : (id.StartsWith("ecam.") ? "ecam_dial" : (id.StartsWith("custom.") ? "custom" : "core"));
        }
    }
}
