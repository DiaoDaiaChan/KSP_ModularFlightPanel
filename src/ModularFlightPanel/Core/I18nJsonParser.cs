using System;
using System.Collections.Generic;
using System.Text;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 轻量级零依赖 JSON 词典解析器 (Zero-Dependency I18n JSON Parser)
    /// 兼容纯 C#、Mono 2.0、.NET Framework 4.7.2 与 .NET 6+，杜绝外部三方库依赖与版本冲突。
    /// 支持单行/多行注释 (// 及 /* */)、转义字符、以及多层嵌套扁平化合并。
    ///
    /// 【为什么需要报警而不是"尽力而为"】
    /// 本解析器同时是运行时查表（I18nManager）与 [9/9] 国际化审计的**唯一基准**。
    /// 一旦它对结构性错误保持沉默，审计就会拿一份"少了若干词条"的基准去比代码，
    /// 结果是成片的假阳性 MissingDictionaryKey —— 或者反过来，放过了真正缺词的词典。
    /// 因此所有"能猜但不确定"的路径（未闭合、括号不配对、非字符串词条、重复键、
    /// 数组/标量值被跳过、\u 转义残缺、嵌套过深）都必须显式报警，
    /// 由调用方（[9/9] 审计）按失败处理。报警计数见 <see cref="LastWarningCount"/>。
    /// </summary>
    public static class I18nJsonParser
    {
        public static Action<string> OnLogWarning;

        /// <summary>最近一次 Parse 产生的报警条数（0 = 词典结构干净）。审计方据此把"静默降级"变成硬失败。</summary>
        public static int LastWarningCount { get; private set; }

        /// <summary>嵌套深度上限：畸形输入（例如上千层 `{`）不再让递归吃爆调用栈。</summary>
        private const int MaxNestingDepth = 32;

        /// <summary>报警条数上限：畸形词典可能触发海量报警，这里只截断"输出"，不截断计数。</summary>
        private const int MaxReportedWarnings = 20;

        private static void Warn(string message)
        {
            bool count = LastWarningCount < int.MaxValue;
            if (count) LastWarningCount++;
            if (count && LastWarningCount <= MaxReportedWarnings)
            {
                OnLogWarning?.Invoke("[I18nJsonParser] " + message);
            }
        }

        public static Dictionary<string, string> Parse(string json, out string langCode, out string displayName, out string nativeName)
        {
            langCode = "en-US";
            displayName = "English";
            nativeName = "English";
            LastWarningCount = 0;

            // 词条与根元数据分开收集，最后再合并：
            // 旧实现把 code/displayName/nativeName 直接写进同一个 dict，且短键别名带
            // `!dict.ContainsKey(key)` 守卫 —— 于是"词条名恰好等于元数据名"时，
            // 词条的短别名会被元数据占位吞掉（I18n.Tr("code") 取到语言码而不是译文）。
            // 分开收集 + 词条优先合并，顺序无关且不丢词条。
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(json)) return values;

            try
            {
                int index = 0;
                SkipWhitespaceAndComments(json, ref index);

                if (index >= json.Length) return values;

                if (json[index] != '{')
                {
                    Warn("根节点不是 JSON 对象，整个文档被忽略（词典必须是 { ... } 结构）");
                    return values;
                }

                index++;
                ParseObjectContent(json, ref index, values, meta, "", 1, ref langCode, ref displayName, ref nativeName);

                SkipWhitespaceAndComments(json, ref index);
                if (index < json.Length)
                {
                    Warn("根对象结束后仍有未消费内容（第 " + LineOf(json, index) + " 行起）：括号可能不配对，或存在第二个根对象");
                }
            }
            catch (Exception ex)
            {
                Warn("解析异常: " + ex.Message);
            }

            foreach (var kv in meta)
            {
                if (!values.ContainsKey(kv.Key)) values[kv.Key] = kv.Value;
            }

            return values;
        }

        private static int LineOf(string json, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < json.Length; i++)
                if (json[i] == '\n') line++;
            return line;
        }

        private static void ParseObjectContent(string json, ref int index, Dictionary<string, string> dict,
            Dictionary<string, string> meta, string prefix, int depth,
            ref string langCode, ref string displayName, ref string nativeName)
        {
            while (index < json.Length)
            {
                SkipWhitespaceAndComments(json, ref index);
                if (index >= json.Length || json[index] == '}')
                {
                    if (index < json.Length) index++;
                    else Warn("对象未闭合就到达文档末尾（缺少 '}'）：该层的后续词条会全部丢失");
                    break;
                }

                // 期望解析键 (String)
                if (json[index] != '"')
                {
                    // 对象内部出现非引号 token：结构已错乱（多逗号、缺逗号、值漏了引号等）。
                    // 旧实现静默 index++ 跳过 —— 结果是"少了多少词条都不知道"。
                    Warn("对象内出现非字符串键的 token '" + json[index] + "'（第 " + LineOf(json, index) + " 行），已跳过");
                    index++;
                    continue;
                }

                string key = ParseString(json, ref index);
                SkipWhitespaceAndComments(json, ref index);

                // 期望冒号
                if (index < json.Length && json[index] == ':')
                {
                    index++;
                }
                else
                {
                    Warn("键 '" + key + "' 后面缺少 ':'（第 " + LineOf(json, index) + " 行），该词条已跳过");
                }

                SkipWhitespaceAndComments(json, ref index);
                if (index >= json.Length)
                {
                    Warn("键 '" + key + "' 在文档末尾处没有值（缺少字符串值）");
                    break;
                }

                char c = json[index];
                if (c == '{')
                {
                    // 嵌套对象：递归解析
                    if (depth >= MaxNestingDepth)
                    {
                        Warn("嵌套深度超过上限 " + MaxNestingDepth + "，该子树被跳过（畸形词典）");
                        SkipValue(json, ref index);
                    }
                    else
                    {
                        index++;
                        string nextPrefix = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";
                        ParseObjectContent(json, ref index, dict, meta, nextPrefix, depth + 1, ref langCode, ref displayName, ref nativeName);
                    }
                }
                else if (c == '"')
                {
                    string value = ParseString(json, ref index);
                    string fullKey = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";

                    // 检查元数据标识（只认根层；元数据单独存放，不与词条抢键位）
                    if (string.IsNullOrEmpty(prefix))
                    {
                        bool isMetadata = true;
                        if (key.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("lang", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("language", StringComparison.OrdinalIgnoreCase))
                        {
                            langCode = value;
                            meta["code"] = value;
                            meta[key] = value;          // 保留原始键名（lang / language 别名），与旧版返回内容一致
                        }
                        else if (key.Equals("displayName", StringComparison.OrdinalIgnoreCase) ||
                                 key.Equals("name", StringComparison.OrdinalIgnoreCase))
                        {
                            displayName = value;
                            meta["displayName"] = value;
                            meta[key] = value;
                        }
                        else if (key.Equals("nativeName", StringComparison.OrdinalIgnoreCase))
                        {
                            nativeName = value;
                            meta["nativeName"] = value;
                            meta[key] = value;
                        }
                        else
                        {
                            isMetadata = false;
                        }

                        if (isMetadata)
                        {
                            // 元数据不进词条表；但必须照常消费分隔逗号，否则下一轮会把 ',' 当成错乱 token
                            SkipWhitespaceAndComments(json, ref index);
                            if (index < json.Length && json[index] == ',') index++;
                            continue;
                        }
                    }

                    if (dict.ContainsKey(fullKey))
                    {
                        Warn("重复词条键 '" + fullKey + "'（第 " + LineOf(json, index) + " 行），后者覆盖前者");
                    }
                    dict[fullKey] = value;

                    // 若处于子对象内（如 "translations.MY_KEY"），同时冗余写入短键 "MY_KEY" 以支持直达访问。
                    // 短键冲突时保持"先到者胜"（不同分组里同名词条的语义由调用方决定），但要报出来。
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        if (dict.ContainsKey(key))
                        {
                            Warn("短键别名冲突: '" + key + "' 已存在，来自 '" + fullKey + "' 的别名未生效");
                        }
                        else
                        {
                            dict[key] = value;
                        }
                    }
                }
                else
                {
                    // boolean / number / null / array：多语言词条必须全部是字符串，这里显式报警而不是静默跳过。
                    string skipKey = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";
                    Warn("词条 '" + skipKey + "' 的值不是字符串（第 " + LineOf(json, index) + " 行），已跳过整个值");
                    SkipValue(json, ref index);
                }

                SkipWhitespaceAndComments(json, ref index);
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                }
            }
        }

        /// <summary>
        /// 跳过任意一个 JSON 值（字符串 / 数字 / true / false / null / 数组 / 对象），
        /// 完整消费整棵子树并停在值的下一个字符处。
        ///
        /// 旧实现是"扫到 , 或 } 或 ] 就停"的 SkipSimpleValue：数组里的元素会被外层循环
        /// 当成"键"继续解析（把数组内容误读成词条 / 让后续真实词条错位），
        /// 属于结构性错读 —— 现在按花括号/方括号配对真正跳过。
        /// </summary>
        private static void SkipValue(string json, ref int index)
        {
            SkipWhitespaceAndComments(json, ref index);
            if (index >= json.Length) return;

            char c = json[index];

            if (c == '"')
            {
                ParseString(json, ref index);
                return;
            }

            if (c == '{' || c == '[')
            {
                char open = c;
                char close = c == '{' ? '}' : ']';
                int depth = 0;
                while (index < json.Length)
                {
                    char x = json[index];
                    if (x == '"') { ParseString(json, ref index); continue; }
                    if (x == open) depth++;
                    else if (x == close)
                    {
                        depth--;
                        index++;
                        if (depth == 0) return;
                        continue;
                    }
                    index++;
                }
                Warn("数组/对象未闭合就到文档末尾（缺少 '" + close + "'）");
                return;
            }

            // 裸标量：数字 / true / false / null
            while (index < json.Length)
            {
                char x = json[index];
                if (x == ',' || x == '}' || x == ']' || char.IsWhiteSpace(x)) return;
                index++;
            }
        }

        private static string ParseString(string json, ref int index)
        {
            if (index >= json.Length || json[index] != '"') return string.Empty;
            index++; // 跳过开头的 "

            var sb = new StringBuilder();
            bool closed = false;
            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"') { closed = true; break; }
                if (c == '\\' && index < json.Length)
                {
                    char esc = json[index++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 <= json.Length)
                            {
                                string hex = json.Substring(index, 4);
                                index += 4;
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int codePoint))
                                {
                                    sb.Append((char)codePoint);
                                }
                                else
                                {
                                    // 旧实现静默吞掉：'\uZZZZ' 会让该字符凭空消失，译文与源文不一致却无人知晓
                                    Warn("\\u 转义不是 4 位十六进制（'" + hex + "'），该转义已丢弃");
                                }
                            }
                            else
                            {
                                Warn("\\u 转义在文档末尾被截断，该转义已丢弃");
                            }
                            break;
                        default:
                            // 非法转义（如 \q）：JSON 规范不允许，旧实现静默保留 esc 字符，这里显式报警
                            Warn("非法字符串转义 '\\" + esc + "'，已按字面量处理");
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (!closed)
            {
                Warn("字符串字面量未闭合就到文档末尾（缺少收尾 '\"'）");
            }
            return sb.ToString();
        }

        private static void SkipWhitespaceAndComments(string json, ref int index)
        {
            while (index < json.Length)
            {
                char c = json[index];
                if (char.IsWhiteSpace(c))
                {
                    index++;
                    continue;
                }
                if (c == '/' && index + 1 < json.Length)
                {
                    if (json[index + 1] == '/')
                    {
                        index += 2;
                        while (index < json.Length && json[index] != '\n' && json[index] != '\r') index++;
                        continue;
                    }
                    if (json[index + 1] == '*')
                    {
                        index += 2;
                        int start = index;
                        bool closed = false;
                        while (index + 1 < json.Length)
                        {
                            if (json[index] == '*' && json[index + 1] == '/') { closed = true; index += 2; break; }
                            index++;
                        }
                        if (!closed)
                        {
                            // 旧实现无条件 index += 2 越过末尾：未闭合的块注释会把后续词条整段吃掉
                            Warn("块注释未闭合就到文档末尾（起始于第 " + LineOf(json, start) + " 行）");
                            index = json.Length;
                        }
                        continue;
                    }
                }
                break;
            }
        }
    }
}
