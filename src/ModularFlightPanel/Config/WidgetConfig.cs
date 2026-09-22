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

        public WidgetConfig() { }

        public WidgetConfig(string id, string name, float x, float y, float scale = 1.0f, string template = "")
        {
            WidgetId = id;
            DisplayName = name;
            PositionX = x;
            PositionY = y;
            Scale = scale;
            CustomTemplate = template;
        }
    }
}
