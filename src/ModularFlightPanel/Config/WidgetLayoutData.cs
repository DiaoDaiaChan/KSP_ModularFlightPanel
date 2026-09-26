using System;
using System.Collections.Generic;

namespace ModularFlightPanel.Config
{
    /// <summary>
    /// 航电仪表全盘布局数据契约 (Avionics Layout Data Contract)
    /// 纯 C# 解耦数据模型，无 UnityEngine 依赖，用于持久化、无头门禁验证与跨版本双向往返。
    /// </summary>
    [Serializable]
    public class WidgetLayoutData
    {
        public float GlobalScale = 1.25f;
        public List<WidgetConfig> Widgets = new List<WidgetConfig>();

        public WidgetLayoutData() { }

        public WidgetLayoutData(float globalScale)
        {
            GlobalScale = globalScale > 0.05f ? globalScale : 1.25f;
        }
    }
}
