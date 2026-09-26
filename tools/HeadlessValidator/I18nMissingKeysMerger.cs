using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ModularFlightPanel.HeadlessValidator
{
    /// <summary>
    /// 外部多语言词条模板合并工具 (从 external JSON 动态合并待补充词条至 zh-CN.json 与 en-US.json)
    /// </summary>
    public static class I18nMissingKeysMerger
    {
        public static int Merge(string repoRoot)
        {
            string locDir = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "Localization");
            string zhPath = Path.Combine(locDir, "zh-CN.json");
            string enPath = Path.Combine(locDir, "en-US.json");
            string templatePath = Path.Combine(repoRoot, "tools", "HeadlessValidator", "missing_keys_template.json");

            if (!File.Exists(zhPath) || !File.Exists(enPath))
            {
                Console.WriteLine("Localization files missing!");
                return 1;
            }

            if (!File.Exists(templatePath))
            {
                Console.WriteLine($"✔ 未找到外部 JSON 词条模板文件 ({templatePath})，词典当前处于最新状态。");
                return 0;
            }

            var zhDict = LoadDictionary(zhPath, out string zhCode, out string zhDisp, out string zhNat);
            var enDict = LoadDictionary(enPath, out string enCode, out string enDisp, out string enNat);
            var keyMap = LoadKeyMap(templatePath);

            int mergedCount = 0;
            foreach (var kvp in keyMap)
            {
                string key = kvp.Key;
                var (cnVal, enVal) = kvp.Value;

                if (!zhDict.ContainsKey(key))
                {
                    zhDict[key] = cnVal;
                    enDict[key] = enVal;
                    mergedCount++;
                }
            }

            if (mergedCount > 0)
            {
                SaveDictionary(zhPath, zhCode, zhDisp, zhNat, zhDict);
                SaveDictionary(enPath, enCode, enDisp, enNat, enDict);
                Console.WriteLine($"✔ 成功从外部 JSON 合并 {mergedCount} 个遗漏词条到 zh-CN.json 和 en-US.json! (当前总词条数: {zhDict.Count})");
            }
            else
            {
                Console.WriteLine($"✔ 外部 JSON 中所有词条已全部收录于 zh-CN.json 和 en-US.json 中 (共 {zhDict.Count} 词条)，无需重复合并。");
            }

            return 0;
        }

        private static Dictionary<string, (string cn, string en)> LoadKeyMap(string path)
        {
            var result = new Dictionary<string, (string cn, string en)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string cn = prop.Value.TryGetProperty("cn", out var pCn) ? pCn.GetString() : prop.Name;
                    string en = prop.Value.TryGetProperty("en", out var pEn) ? pEn.GetString() : prop.Name;
                    result[prop.Name] = (cn, en);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] 读取外部词条模板 JSON 异常: {ex.Message}");
            }
            return result;
        }

        private static Dictionary<string, string> LoadDictionary(string path, out string code, out string display, out string native)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            code = root.GetProperty("code").GetString();
            display = root.GetProperty("displayName").GetString();
            native = root.GetProperty("nativeName").GetString();

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in root.GetProperty("translations").EnumerateObject())
            {
                dict[prop.Name] = prop.Value.GetString();
            }
            return dict;
        }

        private static void SaveDictionary(string path, string code, string display, string native, Dictionary<string, string> dict)
        {
            using var stream = new MemoryStream();
            var options = new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            using (var writer = new Utf8JsonWriter(stream, options))
            {
                writer.WriteStartObject();
                writer.WriteString("code", code);
                writer.WriteString("displayName", display);
                writer.WriteString("nativeName", native);
                writer.WriteStartObject("translations");

                foreach (var kvp in dict)
                {
                    writer.WriteString(kvp.Key, kvp.Value);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            File.WriteAllBytes(path, stream.ToArray());
        }
    }
}
