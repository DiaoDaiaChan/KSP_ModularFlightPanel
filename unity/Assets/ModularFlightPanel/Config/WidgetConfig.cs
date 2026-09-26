using System;

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
        public float ScaleX = 1.0f; // 独立水平缩放 (支持编辑模式自由调整细长/矮胖)
        public float ScaleY = 1.0f; // 独立垂直缩放 (支持编辑模式自由调整细长/矮胖)
        public float Rotation = 0f; // 旋转角度 (度, 0~360)
        public string CustomTemplate = "";

        public float EffectiveScaleX => ScaleX > 0.01f ? ScaleX : (Scale > 0.01f ? Scale : 1.0f);
        public float EffectiveScaleY => ScaleY > 0.01f ? ScaleY : (Scale > 0.01f ? Scale : 1.0f);

        // 航电套件元数据 (Kit Metadata)
        public string WidgetType = "custom"; // "core", "custom", "tape", "ecam_dial"
        public string NumericToken = "{SPD}";
        public float MinValue = 0.0f;
        public float MaxValue = 100.0f;
        public float CautionThreshold = 80.0f;
        public float WarningThreshold = 95.0f;
        public bool IsSoftLimit = false; // 旧配置兼容字段：true 等同于 LimitMode="soft"
        public string LimitMode = "hard"; // "hard"=硬上限, "soft"=软上限爆表, "none"=无上限读数
        public string UnitLabel = "";
        public float StepInterval = 10f;
        public bool IsLeftOrientation = true; // 标尺方向 (true=速度带左侧向读, false=高度带右侧向左读)

        // 渲染与性能单独优化 (Individual Render Optimization)
        public bool IsolateCanvas = true;     // 是否为此组件挂载独立 Sub-Canvas，隔离几何网格重建与 Draw Call 批处理
        public float UpdateInterval = 0f;     // 遥测求值与绘制刷新间隔 (秒, 0=每帧 60Hz+, 0.05=20Hz, 0.2=5Hz, 0.5=2Hz)
        public float CustomHz = 0f;           // 任意浮点数自定义刷新率 (Hz, 例如 11.2f)。0f 表示遵循全局阶梯或 UpdateInterval，>0f 表示独立精确定义
        public float RenderScale = 1.0f;      // 单组件渲染分辨率缩放倍率 (0.5x~2.0x, 缺省 1.0f，支持 0.8x 节能或 1.5x 超采样)

        // 图层与绘制顺序 (Layer & Drawing Order)
        public int DrawOrder = 0;             // 渲染图层层级 (0为最底层，数值越大越靠前/顶层)
        public bool IsLocked = false;         // 是否锁定图层 (锁定后禁止在画布中点击拖拽/变换，防止误触大背景面板)

        // 子部件屏蔽与定制 (Sub-Element Masking)
        public string DisabledSubElements = ""; // 逗号或分号分隔的已屏蔽子部件 ID (例如 "top_mode,bottom_sec,trend_bar")

        public bool IsSubElementDisabled(string controlId)
        {
            if (string.IsNullOrEmpty(DisabledSubElements) || string.IsNullOrEmpty(controlId)) return false;
            string[] parts = DisabledSubElements.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i].Trim(), controlId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public void SetSubElementDisabled(string controlId, bool disabled)
        {
            if (string.IsNullOrEmpty(controlId)) return;
            var set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(DisabledSubElements))
            {
                string[] parts = DisabledSubElements.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++) set.Add(parts[i].Trim());
            }

            if (disabled) set.Add(controlId.Trim());
            else set.Remove(controlId.Trim());

            DisabledSubElements = string.Join(",", set);
        }

        // ===== 视图策略 (原硬编码常量的配置化出口，可在 layout.json / 预设 JSON 中逐组件覆盖) =====
        /// <summary>脏标记阈值：读数变化量小于该值时不触发任何 UI 重绘 (避免每帧 UGUI 顶点全量重建)</summary>
        public float ValueDeltaThreshold = 0.05f;
        /// <summary>状态徽标文案 (正常 / 注意 / 告警)，可按语言或机型定制</summary>
        public string BadgeNormal = "NORM";
        public string BadgeCaution = "CAUT";
        public string BadgeWarning = "WARN";

        public float EffectiveUpdateInterval => CustomHz > 0.001f ? (1.0f / CustomHz) : UpdateInterval;

        public WidgetConfig() { }

        public WidgetConfig(string id, string name, float x, float y, float scale = 1.0f, string template = "", float rotation = 0f, float scaleX = 1.0f, float scaleY = 1.0f)
        {
            WidgetId = id;
            DisplayName = name;
            PositionX = x;
            PositionY = y;
            Scale = scale;
            ScaleX = scaleX > 0.01f ? scaleX : scale;
            ScaleY = scaleY > 0.01f ? scaleY : scale;
            CustomTemplate = template;
            Rotation = rotation;
            WidgetType = id.StartsWith("tape.") ? "tape" : (id.StartsWith("arc_tape.") ? "arc_tape" : (id.StartsWith("ecam.") ? "ecam_dial" : (id.StartsWith("custom.") ? "custom" : "core")));
        }
    }
}
