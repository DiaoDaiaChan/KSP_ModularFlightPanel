using System;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// [已向后兼容重构] 原 TabSharePresets 已合并升级为 TabProfilesConfig (档案与配置管理中枢)
    /// </summary>
    [Obsolete("TabSharePresets 已合并升级为 TabProfilesConfig，请直接调用 TabProfilesConfig.Draw()")]
    public static class TabSharePresets
    {
        public static void Draw()
        {
            TabProfilesConfig.Draw();
        }
    }
}
