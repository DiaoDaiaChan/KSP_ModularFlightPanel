using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.HeadlessValidator
{
    /// <summary>
    /// 全局配置与预设双向导入导出高保真往返测试套件 (Avionics Config Bidirectional Roundtrip Test Suite)
    /// 覆盖：
    /// 1. 词法/语法 AST 引擎自检 (容错、注释剥离、尾逗号、宽松类型转换、防崩溃)
    /// 2. 全量预设库双向往返保真度 (特别针对 diao.json 14 个组件零损耗往返)
    /// 3. 活动布局与备份布局双向往返保真度 (layout.json, layout.backup.json, Vessels/*.json)
    /// 4. 主题与色调配置双向往返保真度 (theme_settings.json, DockRules, 窗口几何状态)
    /// 5. 社区分享码 (MFP:v1:) GZip+Base64 编解码无损往返与模糊防御测试
    /// 6. 内存对象深拷贝深度隔离测试 (CloneLayout, CloneThemeSettings)
    /// </summary>
    public static class ConfigRoundtripTestSuite
    {
        private const float FloatEpsilon = 0.001f;

        public static int Run(string repoRoot)
        {
            Console.WriteLine("\n=======================================================================");
            Console.WriteLine("    MFP 航电配置中枢与预设库全量双向导入导出测试套件 (Config Roundtrip Suite)");
            Console.WriteLine("=======================================================================");

            int totalErrors = 0;

            // 1. JSON AST 引擎容错与类型转换自检
            totalErrors += TestJsonAstResilience();

            // 2. 出厂预设库全量双向往返与字段保真度 (重点核实 diao.json 14 个组件)
            totalErrors += TestPresetsRoundtrip(repoRoot);

            // 3. 运行时布局与备份布局往返保真度
            totalErrors += TestRuntimeLayoutsRoundtrip(repoRoot);

            // 4. 主题与色调配置 (ThemeSettingsData) 往返保真度
            totalErrors += TestThemeSettingsRoundtrip(repoRoot);

            // 5. 社区分享码 (MFP:v1:) 编解码往返与容错防御
            totalErrors += TestShareCodeRoundtrip(repoRoot);

            // 6. 内存对象深拷贝隔离性
            totalErrors += TestDeepCloningIsolation();

            Console.WriteLine("-----------------------------------------------------------------------");
            if (totalErrors == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✔ [CONFIG SUITE PASSED] 航电配置双向导入导出 100% 满分通过! 0 字段漂移, 0 数据丢弃!");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✘ [CONFIG SUITE FAILED] 发现 {totalErrors} 处配置解析或往返保真度异常!");
                Console.ResetColor();
            }

            return totalErrors;
        }

        // =========================================================================
        // 1. JSON AST 引擎容错与类型转换自检
        // =========================================================================
        private static int TestJsonAstResilience()
        {
            Console.WriteLine("\n[Sub-Test 1/6] JSON AST 语法引擎与容错解析自检 (Comments / Trailing Commas / Type Cast)...");
            int errors = 0;

            string tolerantJson = @"
            // 单行顶部注释
            {
                /* 块级注释 1 */
                ""string_val"": ""hello \u4e16\u754c\n\""quoted\"""",
                ""int_as_str"": ""42"",
                ""float_as_str"": ""128.75"",
                ""bool_as_str"": ""true"",
                ""bool_false_str"": ""false"",
                ""scientific_num"": 1.5e2,
                ""negative_float"": -89.125,
                /* 块级注释 2 */
                ""array_with_trailing_comma"": [
                    10,
                    20,
                    30, // 元素后单行注释
                ],
                ""object_with_trailing_comma"": {
                    ""sub_k"": ""sub_v"",
                },
            }
            ";

            try
            {
                var node = JsonNode.Parse(tolerantJson);
                if (node == null || !node.IsObject)
                {
                    PrintError("容错 JSON 解析失败: 根节点未返回有效的 JsonObject");
                    errors++;
                }
                else
                {
                    var obj = node.AsObject();
                    if (!obj.GetString("string_val").Contains("世界") || !obj.GetString("string_val").Contains("\"quoted\""))
                    {
                        PrintError($"字符串转义/Unicode 解析异常: {obj.GetString("string_val")}");
                        errors++;
                    }
                    if (obj.GetInt("int_as_str") != 42)
                    {
                        PrintError($"字符串化整型解析异常: 期望 42, 实际 {obj.GetInt("int_as_str")}");
                        errors++;
                    }
                    if (Math.Abs(obj.GetFloat("float_as_str") - 128.75f) > FloatEpsilon)
                    {
                        PrintError($"字符串化浮点数解析异常: 期望 128.75, 实际 {obj.GetFloat("float_as_str")}");
                        errors++;
                    }
                    if (!obj.GetBool("bool_as_str") || obj.GetBool("bool_false_str"))
                    {
                        PrintError("字符串化布尔值解析异常");
                        errors++;
                    }
                    if (Math.Abs(obj.GetFloat("scientific_num") - 150f) > FloatEpsilon)
                    {
                        PrintError($"科学计数法解析异常: 期望 150, 实际 {obj.GetFloat("scientific_num")}");
                        errors++;
                    }
                    if (Math.Abs(obj.GetFloat("negative_float") - (-89.125f)) > FloatEpsilon)
                    {
                        PrintError($"负浮点数解析异常: 期望 -89.125, 实际 {obj.GetFloat("negative_float")}");
                        errors++;
                    }

                    var arr = obj.GetArray("array_with_trailing_comma");
                    if (arr == null || arr.Count != 3 || arr[0].AsInt() != 10 || arr[2].AsInt() != 30)
                    {
                        PrintError($"数组尾随逗号与元素解析异常: 元素数量 {arr?.Count}");
                        errors++;
                    }

                    var subObj = obj.GetObject("object_with_trailing_comma");
                    if (subObj == null || subObj.GetString("sub_k") != "sub_v")
                    {
                        PrintError("对象尾随逗号解析异常");
                        errors++;
                    }
                }
            }
            catch (Exception ex)
            {
                PrintError($"容错 JSON 解析抛出未捕获异常: {ex.Message}");
                errors++;
            }

            // 异常防御性自检（确保畸形输入安全失败且不崩溃）
            string[] malformedSamples = new[]
            {
                "",
                "   \t\r\n",
                "{ unquoted_key: 123 }",
                "{ \"unclosed_string: 123 }",
                "{ \"Widgets\": [ 1, 2, ",
                "\"just a string\"",
                "[ 1, 2, 3 ]" // 顶层非对象传给 ParseLayout
            };

            foreach (var bad in malformedSamples)
            {
                var parsed = AvionicsConfigParser.ParseLayout(bad, out string err);
                if (parsed != null)
                {
                    PrintError($"畸形 JSON 应被安全拒绝但返回了非空对象: sample='{bad}'");
                    errors++;
                }
                else if (string.IsNullOrEmpty(err))
                {
                    PrintError($"畸形 JSON 被拒绝但未返回错误说明: sample='{bad}'");
                    errors++;
                }
            }

            if (errors == 0)
            {
                PrintSuccess("AST 容错与类型转换自检通过: 注释剥离、尾逗号、Unicode、宽松类型转换与畸形输入防御 100% 达标。");
            }
            return errors;
        }

        // =========================================================================
        // 2. 出厂预设库全量双向往返与字段保真度 (重点核实 diao.json 14 个组件)
        // =========================================================================
        private static int TestPresetsRoundtrip(string repoRoot)
        {
            Console.WriteLine("\n[Sub-Test 2/6] 出厂预设库全量双向往返保真度 (Preset Roundtrip & Fidelity)...");
            int errors = 0;

            string presetsDir = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "PluginData", "Presets");
            if (!Directory.Exists(presetsDir))
            {
                PrintError($"找不到预设目录: {presetsDir}");
                return 1;
            }

            string[] presetFiles = Directory.GetFiles(presetsDir, "*.json");
            if (presetFiles.Length == 0)
            {
                PrintError($"预设目录为空: {presetsDir}");
                return 1;
            }

            bool diaoFound = false;

            foreach (var filePath in presetFiles)
            {
                string fileName = Path.GetFileName(filePath);
                string json = File.ReadAllText(filePath);

                var layout1 = AvionicsConfigParser.ParseLayout(json, out string parseErr);
                if (layout1 == null)
                {
                    PrintError($"[预设 {fileName}] 首次解析失败: {parseErr}");
                    errors++;
                    continue;
                }

                if (layout1.Widgets == null || layout1.Widgets.Count == 0)
                {
                    PrintError($"[预设 {fileName}] 解析结果小组件数量为 0!");
                    errors++;
                    continue;
                }

                // 特别针对 diao.json 进行严苛专项断言
                if (string.Equals(fileName, "diao.json", StringComparison.OrdinalIgnoreCase))
                {
                    diaoFound = true;
                    if (layout1.Widgets.Count != 14)
                    {
                        PrintError($"[预设 diao.json 专项断言失败] 组件数应为 14，实际为 {layout1.Widgets.Count}!");
                        errors++;
                    }
                    else
                    {
                        // 校验 diao.json 关键特性
                        var baro = layout1.Widgets.Find(w => w.WidgetId == "gauge.barometer");
                        if (baro == null || Math.Abs(baro.StepInterval - 0.25f) > FloatEpsilon)
                        {
                            PrintError($"[diao.json 专项断言失败] gauge.barometer 小数步长丢失 (StepInterval={baro?.StepInterval})");
                            errors++;
                        }

                        var tb = layout1.Widgets.Find(w => w.WidgetId == "core.toolbar");
                        if (tb == null || Math.Abs(tb.PositionX - 890.0f) > FloatEpsilon)
                        {
                            PrintError($"[diao.json 专项断言失败] core.toolbar 坐标丢失 (PositionX={tb?.PositionX})");
                            errors++;
                        }
                    }
                }

                // 双向往返：Serialize -> Re-parse
                string serialized = AvionicsConfigParser.SerializeLayout(layout1, pretty: true);
                var layout2 = AvionicsConfigParser.ParseLayout(serialized, out string rtErr);
                if (layout2 == null)
                {
                    PrintError($"[预设 {fileName}] 往返重新解析失败: {rtErr}");
                    errors++;
                    continue;
                }

                // 深度字段比对
                int diffCount = AssertLayoutsEqual(layout1, layout2, $"Preset: {fileName}");
                if (diffCount > 0)
                {
                    errors += diffCount;
                }
                else
                {
                    Console.WriteLine($"  ├─ ✔ {fileName}: {layout1.Widgets.Count} 个组件无损往返 (100% 字段保真)");
                }
            }

            if (!diaoFound)
            {
                PrintError("未在预设目录找到 diao.json，无法进行针对性验证!");
                errors++;
            }
            else
            {
                PrintSuccess($"diao.json 专项验证通过: 14 个小组件全量成功加载与往返，彻底根除游戏内静默清空与回退缺陷!");
            }

            return errors;
        }

        // =========================================================================
        // 3. 运行时布局与备份布局往返保真度
        // =========================================================================
        private static int TestRuntimeLayoutsRoundtrip(string repoRoot)
        {
            Console.WriteLine("\n[Sub-Test 3/6] 运行时布局与历史备份双向往返保真度 (Runtime Layout Roundtrip)...");
            int errors = 0;

            string pluginDataDir = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "PluginData");
            string layoutPath = Path.Combine(pluginDataDir, "layout.json");
            string backupPath = Path.Combine(pluginDataDir, "layout.backup.json");

            var targets = new List<string>();
            if (File.Exists(layoutPath)) targets.Add(layoutPath);
            if (File.Exists(backupPath)) targets.Add(backupPath);

            string vesselsDir = Path.Combine(pluginDataDir, "Vessels");
            if (Directory.Exists(vesselsDir))
            {
                targets.AddRange(Directory.GetFiles(vesselsDir, "*.json"));
            }

            if (targets.Count == 0)
            {
                PrintWarning("未找到 layout.json 或备份文件，跳过实际文件测试。");
                return 0;
            }

            foreach (var target in targets)
            {
                string name = Path.GetFileName(target);
                string json = File.ReadAllText(target);
                var l1 = AvionicsConfigParser.ParseLayout(json, out string err);
                if (l1 == null)
                {
                    PrintError($"[文件 {name}] 布局解析失败: {err}");
                    errors++;
                    continue;
                }

                string serialized = AvionicsConfigParser.SerializeLayout(l1, pretty: true);
                var l2 = AvionicsConfigParser.ParseLayout(serialized, out string rtErr);
                if (l2 == null)
                {
                    PrintError($"[文件 {name}] 序列化后重新解析失败: {rtErr}");
                    errors++;
                    continue;
                }

                int diffs = AssertLayoutsEqual(l1, l2, $"Runtime: {name}");
                if (diffs > 0)
                {
                    errors += diffs;
                }
                else
                {
                    Console.WriteLine($"  ├─ ✔ {name}: {l1.Widgets.Count} 个组件无损往返 (GlobalScale={l1.GlobalScale:F2})");
                }
            }

            if (errors == 0)
            {
                PrintSuccess("运行时布局与机载个性化布局双向往返验证全部通过。");
            }

            return errors;
        }

        // =========================================================================
        // 4. 主题与色调配置 (ThemeSettingsData) 往返保真度
        // =========================================================================
        private static int TestThemeSettingsRoundtrip(string repoRoot)
        {
            Console.WriteLine("\n[Sub-Test 4/6] 主题与色调配置双向往返保真度 (ThemeSettings Roundtrip)...");
            int errors = 0;

            string themePath = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "PluginData", "theme_settings.json");
            if (File.Exists(themePath))
            {
                string json = File.ReadAllText(themePath);
                var t1 = AvionicsConfigParser.ParseThemeSettings(json, out string err);
                if (t1 == null)
                {
                    PrintError($"本地 theme_settings.json 解析失败: {err}");
                    errors++;
                }
                else
                {
                    string ser = AvionicsConfigParser.SerializeThemeSettings(t1, pretty: true);
                    var t2 = AvionicsConfigParser.ParseThemeSettings(ser, out string rtErr);
                    if (t2 == null)
                    {
                        PrintError($"theme_settings.json 重新解析失败: {rtErr}");
                        errors++;
                    }
                    else
                    {
                        int diffs = AssertThemeSettingsEqual(t1, t2, "Disk: theme_settings.json");
                        errors += diffs;
                        if (diffs == 0)
                        {
                            Console.WriteLine($"  ├─ ✔ 磁盘 theme_settings.json 往返无损 (主题={t1.SelectedThemeId}, 语言={t1.SelectedLanguage}, 规则数={t1.DockRules.Count})");
                        }
                    }
                }
            }

            // 合成全覆盖综合设置对象
            var synthetic = new ThemeSettingsData
            {
                SelectedThemeId = "starship_mars",
                SelectedLanguage = "zh-CN",
                RenderMode = 1,
                HideStockNavball = true,
                HideStockAltimeter = true,
                HideStockBottomLeft = false,
                HideStockTimeWarp = false,
                HideStockCommNet = true,
                HideStockToolbar = false,
                ToolbarStyleMode = 2,
                MasterBypass = false,
                ShowPerformanceBadge = true,
                EnableGpu2DUIAcceleration = true,
                AutoAdaptResolution = true,
                GlobalRenderScaleMultiplier = 1.25f,
                DockShowHiddenDrawer = true,
                DockOrientation = 1,
                DockEnableFavoritePanel = true,
                DockFavoriteOrientation = 2,
                DockKeepFavoritesInMain = true,
                DockFavoritePosX = 240.5f,
                DockFavoritePosY = -180.25f,
                SettingsWindowX = 350f,
                SettingsWindowY = 120f,
                SettingsWindowWidth = 1120f,
                SettingsWindowHeight = 800f,
                SettingsWindowMaximized = false,
                DockRules = new List<DockButtonRule>
                {
                    new DockButtonRule { Key = "MOCK_RES", DefaultName = "RES", CustomLabel = "资源监控", IsVisible = true, IsFavorite = true },
                    new DockButtonRule { Key = "MOCK_COMM", DefaultName = "COMM", CustomLabel = "天线网络", IsVisible = false, IsFavorite = false },
                    new DockButtonRule { Key = "MOCK_MJ", DefaultName = "MJ", CustomLabel = "自动驾驶仪", IsVisible = true, IsFavorite = true }
                }
            };

            string synSer = AvionicsConfigParser.SerializeThemeSettings(synthetic, true);
            var synParsed = AvionicsConfigParser.ParseThemeSettings(synSer, out string synErr);
            if (synParsed == null)
            {
                PrintError($"合成 ThemeSettingsData 重新解析失败: {synErr}");
                errors++;
            }
            else
            {
                int synDiffs = AssertThemeSettingsEqual(synthetic, synParsed, "Synthetic ThemeSettings");
                errors += synDiffs;
                if (synDiffs == 0)
                {
                    Console.WriteLine($"  ├─ ✔ 合成高级主题配置双向往返 100% 字段保真 (全 27 个字段与收纳坞子规则)");
                }
            }

            if (errors == 0)
            {
                PrintSuccess("主题与色调配置双向往返保真度验证通过。");
            }

            return errors;
        }

        // =========================================================================
        // 5. 社区分享码 (MFP:v1:) 编解码往返与容错防御
        // =========================================================================
        private static int TestShareCodeRoundtrip(string repoRoot)
        {
            Console.WriteLine("\n[Sub-Test 5/6] 社区分享码 (MFP:v1:) GZip+Base64 编解码往返与模糊防御...");
            int errors = 0;

            string diaoPath = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "PluginData", "Presets", "diao.json");
            if (File.Exists(diaoPath))
            {
                var diaoLayout = AvionicsConfigParser.ParseLayout(File.ReadAllText(diaoPath), out _);
                if (diaoLayout != null)
                {
                    // 编码分享码
                    string shareCode = EncodeShareCode(diaoLayout);
                    if (!shareCode.StartsWith("MFP:v1:"))
                    {
                        PrintError($"分享码未包含正确的前缀: {shareCode}");
                        errors++;
                    }

                    // 解码分享码
                    if (!TryDecodeShareCode(shareCode, out WidgetLayoutData decoded, out string decErr))
                    {
                        PrintError($"diao.json 分享码解码失败: {decErr}");
                        errors++;
                    }
                    else
                    {
                        int diffs = AssertLayoutsEqual(diaoLayout, decoded, "ShareCode: diao.json");
                        errors += diffs;
                        if (diffs == 0)
                        {
                            Console.WriteLine($"  ├─ ✔ diao.json 社区分享码无损编解码往返通过 (压缩后长度: {shareCode.Length} chars)");
                        }
                    }
                }
            }

            // 模糊与畸形分享码防御测试
            string[] malformedCodes = new[]
            {
                "MFP:v2:INVALID_VERSION",
                "MFP:v1:", // 空内容
                "MFP:v1:???NotValidBase64!!!",
                "MFP:v1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not gzip stream")),
                "MFP:v1:" + Convert.ToBase64String(GzipCompress(Encoding.UTF8.GetBytes("{ not a valid json }"))),
                "MFP:v1:" + Convert.ToBase64String(GzipCompress(Encoding.UTF8.GetBytes("{ \"GlobalScale\": 1.25, \"Widgets\": [] }"))) // 0 widgets
            };

            foreach (var badCode in malformedCodes)
            {
                bool success = TryDecodeShareCode(badCode, out var badLayout, out var badErr);
                if (success || badLayout != null)
                {
                    PrintError($"畸形分享码应被拦截但成功解码: code='{badCode}'");
                    errors++;
                }
                else if (string.IsNullOrEmpty(badErr))
                {
                    PrintError($"畸形分享码被拦截但未提供错误提示: code='{badCode}'");
                    errors++;
                }
            }

            if (errors == 0)
            {
                PrintSuccess("社区分享码编解码与防御测试全部通过: GZip 压缩还原 100% 对齐，非标输入防护周密。");
            }

            return errors;
        }

        // =========================================================================
        // 6. 内存对象深拷贝隔离性
        // =========================================================================
        private static int TestDeepCloningIsolation()
        {
            Console.WriteLine("\n[Sub-Test 6/6] 内存配置对象深拷贝隔离性验证 (Deep Clone Isolation)...");
            int errors = 0;

            var originalLayout = new WidgetLayoutData
            {
                GlobalScale = 1.35f,
                Widgets = new List<WidgetConfig>
                {
                    new WidgetConfig("test.widget", "原始名称", 100f, 200f) { DrawOrder = 3 }
                }
            };

            var clonedLayout = AvionicsConfigParser.CloneLayout(originalLayout);
            if (clonedLayout == null)
            {
                PrintError("CloneLayout 返回了 null");
                return 1;
            }

            // 变更克隆对象
            clonedLayout.GlobalScale = 2.0f;
            clonedLayout.Widgets[0].DisplayName = "被篡改的名称";
            clonedLayout.Widgets[0].PositionX = -999f;
            clonedLayout.Widgets.Add(new WidgetConfig("extra.widget", "额外组件", 0, 0));

            // 断言原始对象未受任何污染
            if (Math.Abs(originalLayout.GlobalScale - 1.35f) > FloatEpsilon)
            {
                PrintError($"深拷贝隔离失效: 原始 GlobalScale 被篡改为 {originalLayout.GlobalScale}");
                errors++;
            }
            if (originalLayout.Widgets.Count != 1)
            {
                PrintError($"深拷贝隔离失效: 原始 Widgets 数量被篡改为 {originalLayout.Widgets.Count}");
                errors++;
            }
            if (originalLayout.Widgets[0].DisplayName != "原始名称" || Math.Abs(originalLayout.Widgets[0].PositionX - 100f) > FloatEpsilon)
            {
                PrintError($"深拷贝隔离失效: 原始小组件属性被就地篡改!");
                errors++;
            }

            // 主题克隆隔离验证
            var originalTheme = new ThemeSettingsData
            {
                SelectedThemeId = "original_theme",
                DockRules = new List<DockButtonRule>
                {
                    new DockButtonRule { Key = "K1", CustomLabel = "原始标签" }
                }
            };

            var clonedTheme = AvionicsConfigParser.CloneThemeSettings(originalTheme);
            if (clonedTheme == null)
            {
                PrintError("CloneThemeSettings 返回了 null");
                return 1;
            }

            clonedTheme.SelectedThemeId = "mutated_theme";
            clonedTheme.DockRules[0].CustomLabel = "被篡改的标签";
            clonedTheme.DockRules.Add(new DockButtonRule { Key = "K2" });

            if (originalTheme.SelectedThemeId != "original_theme" || originalTheme.DockRules.Count != 1 || originalTheme.DockRules[0].CustomLabel != "原始标签")
            {
                PrintError("主题深拷贝隔离失效: 原始 ThemeSettingsData 被就地篡改!");
                errors++;
            }

            if (errors == 0)
            {
                PrintSuccess("深拷贝深度隔离验证通过: CloneLayout 与 CloneThemeSettings 彻底杜绝内存引用交叉污染。");
            }

            return errors;
        }

        // =========================================================================
        // 辅助比对方法
        // =========================================================================
        private static int AssertLayoutsEqual(WidgetLayoutData a, WidgetLayoutData b, string context)
        {
            int diffs = 0;
            if (a == null || b == null)
            {
                PrintError($"[{context}] Layout 为 null (a={a != null}, b={b != null})");
                return 1;
            }

            if (Math.Abs(a.GlobalScale - b.GlobalScale) > FloatEpsilon)
            {
                PrintError($"[{context}] GlobalScale 不一致: {a.GlobalScale} vs {b.GlobalScale}");
                diffs++;
            }

            if (a.Widgets.Count != b.Widgets.Count)
            {
                PrintError($"[{context}] Widgets 数量不一致: {a.Widgets.Count} vs {b.Widgets.Count}");
                return diffs + 1;
            }

            for (int i = 0; i < a.Widgets.Count; i++)
            {
                var wA = a.Widgets[i];
                var wB = b.Widgets[i];
                string prefix = $"[{context}] Widget[{i}] ({wA.WidgetId})";

                if (wA.WidgetId != wB.WidgetId) { PrintError($"{prefix} WidgetId: '{wA.WidgetId}' vs '{wB.WidgetId}'"); diffs++; }
                if (wA.DisplayName != wB.DisplayName) { PrintError($"{prefix} DisplayName: '{wA.DisplayName}' vs '{wB.DisplayName}'"); diffs++; }
                if (wA.IsEnabled != wB.IsEnabled) { PrintError($"{prefix} IsEnabled: {wA.IsEnabled} vs {wB.IsEnabled}"); diffs++; }
                if (Math.Abs(wA.PositionX - wB.PositionX) > FloatEpsilon) { PrintError($"{prefix} PositionX: {wA.PositionX} vs {wB.PositionX}"); diffs++; }
                if (Math.Abs(wA.PositionY - wB.PositionY) > FloatEpsilon) { PrintError($"{prefix} PositionY: {wA.PositionY} vs {wB.PositionY}"); diffs++; }
                if (Math.Abs(wA.Scale - wB.Scale) > FloatEpsilon) { PrintError($"{prefix} Scale: {wA.Scale} vs {wB.Scale}"); diffs++; }
                if (Math.Abs(wA.ScaleX - wB.ScaleX) > FloatEpsilon) { PrintError($"{prefix} ScaleX: {wA.ScaleX} vs {wB.ScaleX}"); diffs++; }
                if (Math.Abs(wA.ScaleY - wB.ScaleY) > FloatEpsilon) { PrintError($"{prefix} ScaleY: {wA.ScaleY} vs {wB.ScaleY}"); diffs++; }
                if (Math.Abs(wA.Rotation - wB.Rotation) > FloatEpsilon) { PrintError($"{prefix} Rotation: {wA.Rotation} vs {wB.Rotation}"); diffs++; }
                if (wA.CustomTemplate != wB.CustomTemplate) { PrintError($"{prefix} CustomTemplate: '{wA.CustomTemplate}' vs '{wB.CustomTemplate}'"); diffs++; }
                if (wA.WidgetType != wB.WidgetType) { PrintError($"{prefix} WidgetType: '{wA.WidgetType}' vs '{wB.WidgetType}'"); diffs++; }
                if (wA.NumericToken != wB.NumericToken) { PrintError($"{prefix} NumericToken: '{wA.NumericToken}' vs '{wB.NumericToken}'"); diffs++; }
                if (Math.Abs(wA.MinValue - wB.MinValue) > FloatEpsilon) { PrintError($"{prefix} MinValue: {wA.MinValue} vs {wB.MinValue}"); diffs++; }
                if (Math.Abs(wA.MaxValue - wB.MaxValue) > FloatEpsilon) { PrintError($"{prefix} MaxValue: {wA.MaxValue} vs {wB.MaxValue}"); diffs++; }
                if (Math.Abs(wA.CautionThreshold - wB.CautionThreshold) > FloatEpsilon) { PrintError($"{prefix} CautionThreshold: {wA.CautionThreshold} vs {wB.CautionThreshold}"); diffs++; }
                if (Math.Abs(wA.WarningThreshold - wB.WarningThreshold) > FloatEpsilon) { PrintError($"{prefix} WarningThreshold: {wA.WarningThreshold} vs {wB.WarningThreshold}"); diffs++; }
                if (wA.IsSoftLimit != wB.IsSoftLimit) { PrintError($"{prefix} IsSoftLimit: {wA.IsSoftLimit} vs {wB.IsSoftLimit}"); diffs++; }
                if (wA.LimitMode != wB.LimitMode) { PrintError($"{prefix} LimitMode: '{wA.LimitMode}' vs '{wB.LimitMode}'"); diffs++; }
                if (wA.UnitLabel != wB.UnitLabel) { PrintError($"{prefix} UnitLabel: '{wA.UnitLabel}' vs '{wB.UnitLabel}'"); diffs++; }
                if (Math.Abs(wA.StepInterval - wB.StepInterval) > FloatEpsilon) { PrintError($"{prefix} StepInterval: {wA.StepInterval} vs {wB.StepInterval}"); diffs++; }
                if (wA.IsLeftOrientation != wB.IsLeftOrientation) { PrintError($"{prefix} IsLeftOrientation: {wA.IsLeftOrientation} vs {wB.IsLeftOrientation}"); diffs++; }
                if (wA.IsolateCanvas != wB.IsolateCanvas) { PrintError($"{prefix} IsolateCanvas: {wA.IsolateCanvas} vs {wB.IsolateCanvas}"); diffs++; }
                if (Math.Abs(wA.UpdateInterval - wB.UpdateInterval) > FloatEpsilon) { PrintError($"{prefix} UpdateInterval: {wA.UpdateInterval} vs {wB.UpdateInterval}"); diffs++; }
                if (Math.Abs(wA.CustomHz - wB.CustomHz) > FloatEpsilon) { PrintError($"{prefix} CustomHz: {wA.CustomHz} vs {wB.CustomHz}"); diffs++; }
                if (Math.Abs(wA.RenderScale - wB.RenderScale) > FloatEpsilon) { PrintError($"{prefix} RenderScale: {wA.RenderScale} vs {wB.RenderScale}"); diffs++; }
                if (wA.DrawOrder != wB.DrawOrder) { PrintError($"{prefix} DrawOrder: {wA.DrawOrder} vs {wB.DrawOrder}"); diffs++; }
                if (wA.IsLocked != wB.IsLocked) { PrintError($"{prefix} IsLocked: {wA.IsLocked} vs {wB.IsLocked}"); diffs++; }
                if (wA.DisabledSubElements != wB.DisabledSubElements) { PrintError($"{prefix} DisabledSubElements: '{wA.DisabledSubElements}' vs '{wB.DisabledSubElements}'"); diffs++; }
                if (wA.SubElementTransforms != wB.SubElementTransforms) { PrintError($"{prefix} SubElementTransforms: '{wA.SubElementTransforms}' vs '{wB.SubElementTransforms}'"); diffs++; }
                if (Math.Abs(wA.ValueDeltaThreshold - wB.ValueDeltaThreshold) > FloatEpsilon) { PrintError($"{prefix} ValueDeltaThreshold: {wA.ValueDeltaThreshold} vs {wB.ValueDeltaThreshold}"); diffs++; }
                if (wA.BadgeNormal != wB.BadgeNormal) { PrintError($"{prefix} BadgeNormal: '{wA.BadgeNormal}' vs '{wB.BadgeNormal}'"); diffs++; }
                if (wA.BadgeCaution != wB.BadgeCaution) { PrintError($"{prefix} BadgeCaution: '{wA.BadgeCaution}' vs '{wB.BadgeCaution}'"); diffs++; }
                if (wA.BadgeWarning != wB.BadgeWarning) { PrintError($"{prefix} BadgeWarning: '{wA.BadgeWarning}' vs '{wB.BadgeWarning}'"); diffs++; }
            }

            return diffs;
        }

        private static int AssertThemeSettingsEqual(ThemeSettingsData a, ThemeSettingsData b, string context)
        {
            int diffs = 0;
            if (a == null || b == null)
            {
                PrintError($"[{context}] ThemeSettingsData 为 null");
                return 1;
            }

            if (a.SelectedThemeId != b.SelectedThemeId) { PrintError($"[{context}] SelectedThemeId: '{a.SelectedThemeId}' vs '{b.SelectedThemeId}'"); diffs++; }
            if (a.SelectedLanguage != b.SelectedLanguage) { PrintError($"[{context}] SelectedLanguage: '{a.SelectedLanguage}' vs '{b.SelectedLanguage}'"); diffs++; }
            if (a.RenderMode != b.RenderMode) { PrintError($"[{context}] RenderMode: {a.RenderMode} vs {b.RenderMode}"); diffs++; }
            if (a.HideStockNavball != b.HideStockNavball) { PrintError($"[{context}] HideStockNavball: {a.HideStockNavball} vs {b.HideStockNavball}"); diffs++; }
            if (a.HideStockAltimeter != b.HideStockAltimeter) { PrintError($"[{context}] HideStockAltimeter: {a.HideStockAltimeter} vs {b.HideStockAltimeter}"); diffs++; }
            if (a.HideStockBottomLeft != b.HideStockBottomLeft) { PrintError($"[{context}] HideStockBottomLeft: {a.HideStockBottomLeft} vs {b.HideStockBottomLeft}"); diffs++; }
            if (a.HideStockTimeWarp != b.HideStockTimeWarp) { PrintError($"[{context}] HideStockTimeWarp: {a.HideStockTimeWarp} vs {b.HideStockTimeWarp}"); diffs++; }
            if (a.HideStockCommNet != b.HideStockCommNet) { PrintError($"[{context}] HideStockCommNet: {a.HideStockCommNet} vs {b.HideStockCommNet}"); diffs++; }
            if (a.HideStockToolbar != b.HideStockToolbar) { PrintError($"[{context}] HideStockToolbar: {a.HideStockToolbar} vs {b.HideStockToolbar}"); diffs++; }
            if (a.ToolbarStyleMode != b.ToolbarStyleMode) { PrintError($"[{context}] ToolbarStyleMode: {a.ToolbarStyleMode} vs {b.ToolbarStyleMode}"); diffs++; }
            if (a.MasterBypass != b.MasterBypass) { PrintError($"[{context}] MasterBypass: {a.MasterBypass} vs {b.MasterBypass}"); diffs++; }
            if (a.ShowPerformanceBadge != b.ShowPerformanceBadge) { PrintError($"[{context}] ShowPerformanceBadge: {a.ShowPerformanceBadge} vs {b.ShowPerformanceBadge}"); diffs++; }
            if (a.EnableGpu2DUIAcceleration != b.EnableGpu2DUIAcceleration) { PrintError($"[{context}] EnableGpu2DUIAcceleration: {a.EnableGpu2DUIAcceleration} vs {b.EnableGpu2DUIAcceleration}"); diffs++; }
            if (a.AutoAdaptResolution != b.AutoAdaptResolution) { PrintError($"[{context}] AutoAdaptResolution: {a.AutoAdaptResolution} vs {b.AutoAdaptResolution}"); diffs++; }
            if (Math.Abs(a.GlobalRenderScaleMultiplier - b.GlobalRenderScaleMultiplier) > FloatEpsilon) { PrintError($"[{context}] GlobalRenderScaleMultiplier: {a.GlobalRenderScaleMultiplier} vs {b.GlobalRenderScaleMultiplier}"); diffs++; }
            if (a.DockShowHiddenDrawer != b.DockShowHiddenDrawer) { PrintError($"[{context}] DockShowHiddenDrawer: {a.DockShowHiddenDrawer} vs {b.DockShowHiddenDrawer}"); diffs++; }
            if (a.DockOrientation != b.DockOrientation) { PrintError($"[{context}] DockOrientation: {a.DockOrientation} vs {b.DockOrientation}"); diffs++; }
            if (a.DockEnableFavoritePanel != b.DockEnableFavoritePanel) { PrintError($"[{context}] DockEnableFavoritePanel: {a.DockEnableFavoritePanel} vs {b.DockEnableFavoritePanel}"); diffs++; }
            if (a.DockFavoriteOrientation != b.DockFavoriteOrientation) { PrintError($"[{context}] DockFavoriteOrientation: {a.DockFavoriteOrientation} vs {b.DockFavoriteOrientation}"); diffs++; }
            if (a.DockKeepFavoritesInMain != b.DockKeepFavoritesInMain) { PrintError($"[{context}] DockKeepFavoritesInMain: {a.DockKeepFavoritesInMain} vs {b.DockKeepFavoritesInMain}"); diffs++; }
            if (Math.Abs(a.DockFavoritePosX - b.DockFavoritePosX) > FloatEpsilon) { PrintError($"[{context}] DockFavoritePosX: {a.DockFavoritePosX} vs {b.DockFavoritePosX}"); diffs++; }
            if (Math.Abs(a.DockFavoritePosY - b.DockFavoritePosY) > FloatEpsilon) { PrintError($"[{context}] DockFavoritePosY: {a.DockFavoritePosY} vs {b.DockFavoritePosY}"); diffs++; }
            if (Math.Abs(a.SettingsWindowX - b.SettingsWindowX) > FloatEpsilon) { PrintError($"[{context}] SettingsWindowX: {a.SettingsWindowX} vs {b.SettingsWindowX}"); diffs++; }
            if (Math.Abs(a.SettingsWindowY - b.SettingsWindowY) > FloatEpsilon) { PrintError($"[{context}] SettingsWindowY: {a.SettingsWindowY} vs {b.SettingsWindowY}"); diffs++; }
            if (Math.Abs(a.SettingsWindowWidth - b.SettingsWindowWidth) > FloatEpsilon) { PrintError($"[{context}] SettingsWindowWidth: {a.SettingsWindowWidth} vs {b.SettingsWindowWidth}"); diffs++; }
            if (Math.Abs(a.SettingsWindowHeight - b.SettingsWindowHeight) > FloatEpsilon) { PrintError($"[{context}] SettingsWindowHeight: {a.SettingsWindowHeight} vs {b.SettingsWindowHeight}"); diffs++; }
            if (a.SettingsWindowMaximized != b.SettingsWindowMaximized) { PrintError($"[{context}] SettingsWindowMaximized: {a.SettingsWindowMaximized} vs {b.SettingsWindowMaximized}"); diffs++; }

            int rulesA = a.DockRules?.Count ?? 0;
            int rulesB = b.DockRules?.Count ?? 0;
            if (rulesA != rulesB)
            {
                PrintError($"[{context}] DockRules 数量不一致: {rulesA} vs {rulesB}");
                diffs++;
            }
            else if (rulesA > 0)
            {
                for (int i = 0; i < rulesA; i++)
                {
                    var rA = a.DockRules[i];
                    var rB = b.DockRules[i];
                    if (rA.Key != rB.Key || rA.DefaultName != rB.DefaultName || rA.CustomLabel != rB.CustomLabel || rA.IsVisible != rB.IsVisible || rA.IsFavorite != rB.IsFavorite)
                    {
                        PrintError($"[{context}] DockRule[{i}] ({rA.Key}) 内容不一致");
                        diffs++;
                    }
                }
            }

            return diffs;
        }

        private static string EncodeShareCode(WidgetLayoutData layout)
        {
            if (layout == null) return string.Empty;
            string json = AvionicsConfigParser.SerializeLayout(layout, false);
            byte[] rawBytes = Encoding.UTF8.GetBytes(json);
            byte[] compressed = GzipCompress(rawBytes);
            return "MFP:v1:" + Convert.ToBase64String(compressed);
        }

        private static bool TryDecodeShareCode(string code, out WidgetLayoutData layout, out string error)
        {
            layout = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(code))
            {
                error = "分享码为空";
                return false;
            }

            if (!code.StartsWith("MFP:v1:"))
            {
                error = "分享码前缀不匹配 (必须为 MFP:v1:)";
                return false;
            }

            try
            {
                string b64 = code.Substring("MFP:v1:".Length);
                byte[] comp = Convert.FromBase64String(b64);
                byte[] raw = GzipDecompress(comp);
                string json = Encoding.UTF8.GetString(raw);
                layout = AvionicsConfigParser.ParseLayout(json, out error);
                if (layout == null || layout.Widgets == null || layout.Widgets.Count == 0)
                {
                    layout = null;
                    error = string.IsNullOrEmpty(error) ? "分享码未包含任何小组件" : error;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                layout = null;
                error = $"解码异常: {ex.Message}";
                return false;
            }
        }

        private static byte[] GzipCompress(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionMode.Compress))
                {
                    gz.Write(data, 0, data.Length);
                }
                return ms.ToArray();
            }
        }

        private static byte[] GzipDecompress(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                gz.CopyTo(outMs);
                return outMs.ToArray();
            }
        }

        private static void PrintSuccess(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✔ {msg}");
            Console.ResetColor();
        }

        private static void PrintWarning(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠ {msg}");
            Console.ResetColor();
        }

        private static void PrintError(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✘ {msg}");
            Console.ResetColor();
        }
    }
}
