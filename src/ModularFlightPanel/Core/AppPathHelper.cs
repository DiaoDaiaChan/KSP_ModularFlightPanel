using System;
using System.IO;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 跨平台与跨宿主路径解析中枢 (Decoupled Application Root Helper)
    /// 在 KSP 环境下优先动态反射 KSPUtil.ApplicationRootPath；
    /// 在独立 Unity 编辑器、批处理无头渲染或脱机测试环境中，自动回退到项目或当前工作目录。
    /// </summary>
    public static class AppPathHelper
    {
        private static string _overridePath = null;
        public static void SetRootPath(string path) => _overridePath = path;

        public static string RootPath
        {
            get
            {
                if (!string.IsNullOrEmpty(_overridePath)) return _overridePath;

                try
                {
                    Type kspUtilType = Type.GetType("KSPUtil, Assembly-CSharp");
                    if (kspUtilType != null)
                    {
                        var prop = kspUtilType.GetProperty("ApplicationRootPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (prop != null)
                        {
                            string val = prop.GetValue(null, null) as string;
                            if (!string.IsNullOrEmpty(val)) return val;
                        }
                    }
                }
                catch { }

                try
                {
                    return Directory.GetCurrentDirectory();
                }
                catch
                {
                    return ".";
                }
            }
        }
    }
}
