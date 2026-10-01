using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ModularFlightPanel.Config
{
    // =========================================================================
    // 1. 零依赖轻量级纯 C# JSON AST 引擎 (Pure C# Zero-Dependency JSON AST)
    // =========================================================================

    public abstract class JsonNode
    {
        public virtual bool IsObject => false;
        public virtual bool IsArray => false;
        public virtual bool IsValue => false;

        public JsonObject AsObject() => this as JsonObject;
        public JsonArray AsArray() => this as JsonArray;
        public JsonValue AsValue() => this as JsonValue;

        public string AsString(string fallback = "") => AsValue()?.AsString(fallback) ?? fallback;
        public float AsFloat(float fallback = 0f) => AsValue()?.AsFloat(fallback) ?? fallback;
        public double AsDouble(double fallback = 0.0) => AsValue()?.AsDouble(fallback) ?? fallback;
        public int AsInt(int fallback = 0) => AsValue()?.AsInt(fallback) ?? fallback;
        public bool AsBool(bool fallback = false) => AsValue()?.AsBool(fallback) ?? fallback;
        public bool IsNull => AsValue()?.IsNull ?? false;

        public abstract void Write(StringBuilder sb, bool pretty, int indentLevel);

        public override string ToString() => ToString(false);

        public string ToString(bool pretty)
        {
            var sb = new StringBuilder();
            Write(sb, pretty, 0);
            return sb.ToString();
        }

        public static JsonNode Parse(string json)
        {
            return JsonParser.Parse(json);
        }

        internal static string EscapeString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            var sb = new StringBuilder(str.Length + 4);
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    public class JsonObject : JsonNode, IEnumerable<KeyValuePair<string, JsonNode>>
    {
        private readonly Dictionary<string, JsonNode> _fields = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

        public override bool IsObject => true;
        public int Count => _fields.Count;
        public IEnumerable<string> Keys => _fields.Keys;

        public JsonNode this[string key]
        {
            get => _fields.TryGetValue(key, out var node) ? node : null;
            set => _fields[key] = value ?? JsonValue.Null;
        }

        public bool ContainsKey(string key) => _fields.ContainsKey(key);

        public void Add(string key, JsonNode node) => _fields[key] = node ?? JsonValue.Null;
        public void Add(string key, string val) => _fields[key] = new JsonValue(val);
        public void Add(string key, float val) => _fields[key] = new JsonValue(val);
        public void Add(string key, double val) => _fields[key] = new JsonValue(val);
        public void Add(string key, int val) => _fields[key] = new JsonValue(val);
        public void Add(string key, bool val) => _fields[key] = new JsonValue(val);

        public string GetString(string key, string fallback = "") => this[key]?.AsString(fallback) ?? fallback;
        public float GetFloat(string key, float fallback = 0f) => this[key]?.AsFloat(fallback) ?? fallback;
        public double GetDouble(string key, double fallback = 0.0) => this[key]?.AsDouble(fallback) ?? fallback;
        public int GetInt(string key, int fallback = 0) => this[key]?.AsInt(fallback) ?? fallback;
        public bool GetBool(string key, bool fallback = false) => this[key]?.AsBool(fallback) ?? fallback;
        public JsonObject GetObject(string key) => this[key]?.AsObject();
        public JsonArray GetArray(string key) => this[key]?.AsArray();

        public IEnumerator<KeyValuePair<string, JsonNode>> GetEnumerator() => _fields.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _fields.GetEnumerator();

        public override void Write(StringBuilder sb, bool pretty, int indentLevel)
        {
            sb.Append('{');
            if (_fields.Count == 0)
            {
                sb.Append('}');
                return;
            }

            if (pretty) sb.AppendLine();
            int idx = 0;
            string indent = pretty ? new string(' ', (indentLevel + 1) * 2) : "";
            foreach (var kv in _fields)
            {
                if (pretty) sb.Append(indent);
                sb.Append('"').Append(EscapeString(kv.Key)).Append(pretty ? "\": " : "\":");
                kv.Value.Write(sb, pretty, indentLevel + 1);
                if (++idx < _fields.Count) sb.Append(',');
                if (pretty) sb.AppendLine();
            }
            if (pretty) sb.Append(new string(' ', indentLevel * 2));
            sb.Append('}');
        }
    }

    public class JsonArray : JsonNode, IEnumerable<JsonNode>
    {
        private readonly List<JsonNode> _elements = new List<JsonNode>();

        public override bool IsArray => true;
        public int Count => _elements.Count;

        public JsonNode this[int index]
        {
            get => (index >= 0 && index < _elements.Count) ? _elements[index] : null;
            set
            {
                if (index >= 0 && index < _elements.Count)
                    _elements[index] = value ?? JsonValue.Null;
            }
        }

        public void Add(JsonNode node) => _elements.Add(node ?? JsonValue.Null);
        public void Add(string val) => _elements.Add(new JsonValue(val));
        public void Add(float val) => _elements.Add(new JsonValue(val));
        public void Add(double val) => _elements.Add(new JsonValue(val));
        public void Add(int val) => _elements.Add(new JsonValue(val));
        public void Add(bool val) => _elements.Add(new JsonValue(val));

        public IEnumerator<JsonNode> GetEnumerator() => _elements.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _elements.GetEnumerator();

        public override void Write(StringBuilder sb, bool pretty, int indentLevel)
        {
            sb.Append('[');
            if (_elements.Count == 0)
            {
                sb.Append(']');
                return;
            }

            if (pretty) sb.AppendLine();
            string indent = pretty ? new string(' ', (indentLevel + 1) * 2) : "";
            for (int i = 0; i < _elements.Count; i++)
            {
                if (pretty) sb.Append(indent);
                _elements[i].Write(sb, pretty, indentLevel + 1);
                if (i < _elements.Count - 1) sb.Append(',');
                if (pretty) sb.AppendLine();
            }
            if (pretty) sb.Append(new string(' ', indentLevel * 2));
            sb.Append(']');
        }
    }

    public class JsonValue : JsonNode
    {
        public static readonly JsonValue Null = new JsonValue();

        public object Raw { get; }
        public override bool IsValue => true;
        public new bool IsNull => Raw == null;

        public JsonValue() { Raw = null; }
        public JsonValue(string val) { Raw = val; }
        public JsonValue(double val) { Raw = val; }
        public JsonValue(float val) { Raw = (double)val; }
        public JsonValue(int val) { Raw = (long)val; }
        public JsonValue(long val) { Raw = val; }
        public JsonValue(bool val) { Raw = val; }

        public new string AsString(string fallback = "") => Raw?.ToString() ?? fallback;

        public new float AsFloat(float fallback = 0f)
        {
            if (Raw == null) return fallback;
            if (Raw is double d) return (float)d;
            if (Raw is float f) return f;
            if (Raw is long l) return (float)l;
            if (Raw is int i) return (float)i;
            if (float.TryParse(Raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result)) return result;
            return fallback;
        }

        public new double AsDouble(double fallback = 0.0)
        {
            if (Raw == null) return fallback;
            if (Raw is double d) return d;
            if (Raw is float f) return (double)f;
            if (Raw is long l) return (double)l;
            if (Raw is int i) return (double)i;
            if (double.TryParse(Raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result)) return result;
            return fallback;
        }

        public new int AsInt(int fallback = 0)
        {
            if (Raw == null) return fallback;
            if (Raw is long l) return (int)l;
            if (Raw is int i) return i;
            if (Raw is double d) return (int)Math.Round(d);
            if (Raw is float f) return (int)Math.Round(f);
            if (int.TryParse(Raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)) return result;
            return fallback;
        }

        public new bool AsBool(bool fallback = false)
        {
            if (Raw == null) return fallback;
            if (Raw is bool b) return b;
            if (Raw is long l) return l != 0;
            if (Raw is int i) return i != 0;
            if (bool.TryParse(Raw.ToString(), out bool result)) return result;
            return fallback;
        }

        public override void Write(StringBuilder sb, bool pretty, int indentLevel)
        {
            if (Raw == null) { sb.Append("null"); return; }
            if (Raw is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (Raw is string s) { sb.Append('"').Append(EscapeString(s)).Append('"'); return; }
            if (Raw is double d)
            {
                // 确保浮点数在 JSON 中包含小数点或有效数字
                string dStr = d.ToString("R", CultureInfo.InvariantCulture);
                if (dStr.IndexOf('.') < 0 && dStr.IndexOf('E') < 0 && dStr.IndexOf('e') < 0) dStr += ".0";
                sb.Append(dStr);
                return;
            }
            if (Raw is long l) { sb.Append(l.ToString(CultureInfo.InvariantCulture)); return; }
            sb.Append(Raw.ToString());
        }
    }

    // =========================================================================
    // 2. 词法器与语法分析器 (Parser: 注释剥离 / 尾逗号容错 / 宽容语法)
    // =========================================================================

    public static class JsonParser
    {
        public static JsonNode Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            // 剥离 BOM 字符
            if (json.Length > 0 && json[0] == '\uFEFF')
            {
                json = json.Substring(1);
            }

            int pos = 0;
            var node = ParseValue(json, ref pos);
            if (node == null) return null;

            SkipWhitespaceAndComments(json, ref pos);
            if (pos < json.Length)
            {
                throw new FormatException($"JSON 语法错误: 未能识别的尾随字符位于第 {pos} 个字符处");
            }
            return node;
        }

        private static void SkipWhitespaceAndComments(string s, ref int pos)
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (char.IsWhiteSpace(c))
                {
                    pos++;
                    continue;
                }
                // 单行注释 //
                if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
                {
                    pos += 2;
                    while (pos < s.Length && s[pos] != '\n' && s[pos] != '\r') pos++;
                    continue;
                }
                // 多行注释 /* */
                if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '*')
                {
                    pos += 2;
                    while (pos + 1 < s.Length && !(s[pos] == '*' && s[pos + 1] == '/')) pos++;
                    if (pos + 1 < s.Length) pos += 2;
                    continue;
                }
                break;
            }
        }

        private static JsonNode ParseValue(string s, ref int pos)
        {
            SkipWhitespaceAndComments(s, ref pos);
            if (pos >= s.Length) return null;

            char c = s[pos];
            if (c == '{') return ParseObject(s, ref pos);
            if (c == '[') return ParseArray(s, ref pos);
            if (c == '"' || c == '\'') return new JsonValue(ParseString(s, ref pos));
            if (char.IsDigit(c) || c == '-' || c == '+') return ParseNumber(s, ref pos);
            if (char.IsLetter(c)) return ParseLiteral(s, ref pos);

            return null;
        }

        private static JsonObject ParseObject(string s, ref int pos)
        {
            var obj = new JsonObject();
            pos++; // skip '{'

            while (pos < s.Length)
            {
                SkipWhitespaceAndComments(s, ref pos);
                if (pos >= s.Length) break;
                if (s[pos] == '}') { pos++; return obj; }

                // 解析键 (支持引号键和普通标识符键)
                string key;
                if (s[pos] == '"' || s[pos] == '\'')
                {
                    key = ParseString(s, ref pos);
                }
                else
                {
                    int start = pos;
                    while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_' || s[pos] == '-' || s[pos] == '.')) pos++;
                    key = s.Substring(start, pos - start);
                }

                SkipWhitespaceAndComments(s, ref pos);
                if (pos < s.Length && s[pos] == ':') pos++; // skip ':'
                else throw new FormatException($"JSON 语法错误: 键 '{key}' 后缺少冒号 ':'");
                SkipWhitespaceAndComments(s, ref pos);

                var val = ParseValue(s, ref pos);
                if (key != null)
                {
                    obj[key] = val;
                }

                SkipWhitespaceAndComments(s, ref pos);
                if (pos < s.Length && s[pos] == ',')
                {
                    pos++; // skip ','
                    continue;
                }
                if (pos < s.Length && s[pos] == '}')
                {
                    pos++;
                    return obj;
                }
            }

            throw new FormatException("JSON 语法错误: 对象缺少闭合花括号 '}'");
        }

        private static JsonArray ParseArray(string s, ref int pos)
        {
            var arr = new JsonArray();
            pos++; // skip '['

            while (pos < s.Length)
            {
                SkipWhitespaceAndComments(s, ref pos);
                if (pos >= s.Length) break;
                if (s[pos] == ']') { pos++; return arr; }

                var val = ParseValue(s, ref pos);
                arr.Add(val);

                SkipWhitespaceAndComments(s, ref pos);
                if (pos < s.Length && s[pos] == ',')
                {
                    pos++; // skip ','
                    continue;
                }
                if (pos < s.Length && s[pos] == ']')
                {
                    pos++;
                    return arr;
                }
            }

            throw new FormatException("JSON 语法错误: 数组缺少闭合方括号 ']'");
        }

        private static string ParseString(string s, ref int pos)
        {
            char quote = s[pos++];
            var sb = new StringBuilder();

            while (pos < s.Length)
            {
                char c = s[pos++];
                if (c == quote) return sb.ToString();
                if (c == '\\' && pos < s.Length)
                {
                    char esc = s[pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 <= s.Length)
                            {
                                string hex = s.Substring(pos, 4);
                                pos += 4;
                                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int unicode))
                                {
                                    sb.Append((char)unicode);
                                }
                            }
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            throw new FormatException($"JSON 语法错误: 字符串缺少闭合引号 '{quote}'");
        }

        private static JsonValue ParseNumber(string s, ref int pos)
        {
            int start = pos;
            bool isFloat = false;

            if (s[pos] == '-' || s[pos] == '+') pos++;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E' || s[pos] == '+' || s[pos] == '-'))
            {
                if (s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E') isFloat = true;
                pos++;
            }

            string numStr = s.Substring(start, pos - start);
            if (isFloat && double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double dVal))
            {
                return new JsonValue(dVal);
            }
            if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lVal))
            {
                return new JsonValue(lVal);
            }
            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double dFallback))
            {
                return new JsonValue(dFallback);
            }

            return new JsonValue(0L);
        }

        private static JsonValue ParseLiteral(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length && char.IsLetter(s[pos])) pos++;
            string lit = s.Substring(start, pos - start);

            if (string.Equals(lit, "true", StringComparison.OrdinalIgnoreCase)) return new JsonValue(true);
            if (string.Equals(lit, "false", StringComparison.OrdinalIgnoreCase)) return new JsonValue(false);
            if (string.Equals(lit, "null", StringComparison.OrdinalIgnoreCase)) return JsonValue.Null;

            return new JsonValue(lit);
        }
    }

    // =========================================================================
    // 3. 航电业务模型解析与序列化中枢 (Avionics Configuration Hub)
    // =========================================================================

    public static class AvionicsConfigParser
    {
        // ---------------------------------------------------------------------
        // A. 页面布局配置 (WidgetLayoutData) 双向解析与导出
        // ---------------------------------------------------------------------

        /// <summary>
        /// 从 JSON 字符串解析完整航电布局。具备自动容错、类型转换、注释剥离与默认值补齐能力。
        /// </summary>
        public static WidgetLayoutData ParseLayout(string json, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Input JSON is empty";
                return null;
            }

            try
            {
                var root = JsonNode.Parse(json)?.AsObject();
                if (root == null)
                {
                    error = "JSON root node must be an object ({ ... })";
                    return null;
                }

                if (!root.ContainsKey("Widgets"))
                {
                    error = "JSON missing 'Widgets' array";
                    return null;
                }

                var layout = new WidgetLayoutData();
                layout.GlobalScale = root.GetFloat("GlobalScale", 1.25f);
                if (layout.GlobalScale <= 0.05f) layout.GlobalScale = 1.25f;

                var widgetsArray = root.GetArray("Widgets");
                if (widgetsArray != null)
                {
                    for (int i = 0; i < widgetsArray.Count; i++)
                    {
                        var wObj = widgetsArray[i]?.AsObject();
                        if (wObj == null) continue;

                        var cfg = new WidgetConfig();
                        cfg.WidgetId = wObj.GetString("WidgetId", "unnamed_widget");
                        cfg.DisplayName = wObj.GetString("DisplayName", cfg.WidgetId);
                        cfg.IsEnabled = wObj.GetBool("IsEnabled", true);
                        cfg.PositionX = wObj.GetFloat("PositionX", 0f);
                        cfg.PositionY = wObj.GetFloat("PositionY", 0f);
                        cfg.Scale = wObj.GetFloat("Scale", 1.0f);
                        cfg.ScaleX = wObj.GetFloat("ScaleX", cfg.Scale > 0.01f ? cfg.Scale : 1.0f);
                        cfg.ScaleY = wObj.GetFloat("ScaleY", cfg.Scale > 0.01f ? cfg.Scale : 1.0f);
                        cfg.Rotation = wObj.GetFloat("Rotation", 0f);
                        cfg.CustomTemplate = wObj.GetString("CustomTemplate", "");

                        cfg.WidgetType = wObj.GetString("WidgetType", "custom");
                        cfg.NumericToken = wObj.GetString("NumericToken", "{SPD}");
                        cfg.MinValue = wObj.GetFloat("MinValue", 0f);
                        cfg.MaxValue = wObj.GetFloat("MaxValue", 100f);
                        cfg.CautionThreshold = wObj.GetFloat("CautionThreshold", 80f);
                        cfg.WarningThreshold = wObj.GetFloat("WarningThreshold", 95f);
                        cfg.IsSoftLimit = wObj.GetBool("IsSoftLimit", false);
                        cfg.LimitMode = wObj.GetString("LimitMode", "hard");
                        cfg.UnitLabel = wObj.GetString("UnitLabel", "");
                        cfg.StepInterval = wObj.GetFloat("StepInterval", 10f);
                        cfg.IsLeftOrientation = wObj.GetBool("IsLeftOrientation", true);

                        cfg.IsolateCanvas = wObj.GetBool("IsolateCanvas", true);
                        cfg.UpdateInterval = wObj.GetFloat("UpdateInterval", 0f);
                        cfg.CustomHz = wObj.GetFloat("CustomHz", 0f);
                        cfg.HeartBeatInterval = wObj.GetFloat("HeartBeatInterval", -1f);
                        cfg.HeartBeatHz = wObj.GetFloat("HeartBeatHz", 0f);
                        cfg.RenderScale = wObj.GetFloat("RenderScale", 1.0f);
                        cfg.DrawOrder = wObj.GetInt("DrawOrder", i);
                        cfg.IsLocked = wObj.GetBool("IsLocked", false);
                        cfg.DisabledSubElements = wObj.GetString("DisabledSubElements", "");
                        cfg.SubElementTransforms = wObj.GetString("SubElementTransforms", "");

                        cfg.ValueDeltaThreshold = wObj.GetFloat("ValueDeltaThreshold", 0.05f);
                        cfg.BadgeNormal = wObj.GetString("BadgeNormal", "NORM");
                        cfg.BadgeCaution = wObj.GetString("BadgeCaution", "CAUT");
                        cfg.BadgeWarning = wObj.GetString("BadgeWarning", "WARN");

                        layout.Widgets.Add(cfg);
                    }
                }

                return layout;
            }
            catch (Exception ex)
            {
                error = $"Layout JSON parse exception: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// 将航电布局数据序列化为标准化 JSON 字符串。
        /// </summary>
        public static string SerializeLayout(WidgetLayoutData layout, bool pretty = true)
        {
            if (layout == null) return "{}";

            var root = new JsonObject();
            root.Add("GlobalScale", layout.GlobalScale);

            var widgetsArray = new JsonArray();
            if (layout.Widgets != null)
            {
                for (int i = 0; i < layout.Widgets.Count; i++)
                {
                    var cfg = layout.Widgets[i];
                    if (cfg == null) continue;

                    var wObj = new JsonObject();
                    wObj.Add("WidgetId", cfg.WidgetId);
                    wObj.Add("DisplayName", cfg.DisplayName);
                    wObj.Add("IsEnabled", cfg.IsEnabled);
                    wObj.Add("PositionX", cfg.PositionX);
                    wObj.Add("PositionY", cfg.PositionY);
                    wObj.Add("Scale", cfg.Scale);
                    wObj.Add("ScaleX", cfg.ScaleX);
                    wObj.Add("ScaleY", cfg.ScaleY);
                    wObj.Add("Rotation", cfg.Rotation);
                    wObj.Add("CustomTemplate", cfg.CustomTemplate ?? "");
                    wObj.Add("WidgetType", cfg.WidgetType ?? "custom");
                    wObj.Add("NumericToken", cfg.NumericToken ?? "");
                    wObj.Add("MinValue", cfg.MinValue);
                    wObj.Add("MaxValue", cfg.MaxValue);
                    wObj.Add("CautionThreshold", cfg.CautionThreshold);
                    wObj.Add("WarningThreshold", cfg.WarningThreshold);
                    wObj.Add("IsSoftLimit", cfg.IsSoftLimit);
                    wObj.Add("LimitMode", cfg.LimitMode ?? "hard");
                    wObj.Add("UnitLabel", cfg.UnitLabel ?? "");
                    wObj.Add("StepInterval", cfg.StepInterval);
                    wObj.Add("IsLeftOrientation", cfg.IsLeftOrientation);
                    wObj.Add("IsolateCanvas", cfg.IsolateCanvas);
                    wObj.Add("UpdateInterval", cfg.UpdateInterval);
                    wObj.Add("CustomHz", cfg.CustomHz);
                    wObj.Add("HeartBeatInterval", cfg.HeartBeatInterval);
                    wObj.Add("HeartBeatHz", cfg.HeartBeatHz);
                    wObj.Add("RenderScale", cfg.RenderScale);
                    wObj.Add("DrawOrder", cfg.DrawOrder);
                    wObj.Add("IsLocked", cfg.IsLocked);
                    wObj.Add("DisabledSubElements", cfg.DisabledSubElements ?? "");
                    wObj.Add("SubElementTransforms", cfg.SubElementTransforms ?? "");
                    wObj.Add("ValueDeltaThreshold", cfg.ValueDeltaThreshold);
                    wObj.Add("BadgeNormal", cfg.BadgeNormal ?? "NORM");
                    wObj.Add("BadgeCaution", cfg.BadgeCaution ?? "CAUT");
                    wObj.Add("BadgeWarning", cfg.BadgeWarning ?? "WARN");

                    widgetsArray.Add(wObj);
                }
            }
            root.Add("Widgets", widgetsArray);

            return root.ToString(pretty);
        }

        // ---------------------------------------------------------------------
        // B. 主题与色调配置 (ThemeSettingsData) 双向解析与导出
        // ---------------------------------------------------------------------

        /// <summary>
        /// 解析 theme_settings.json。
        /// </summary>
        public static ThemeSettingsData ParseThemeSettings(string json, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Input theme settings JSON is empty";
                return null;
            }

            try
            {
                var root = JsonNode.Parse(json)?.AsObject();
                if (root == null)
                {
                    error = "JSON root node must be an object ({ ... })";
                    return null;
                }

                var data = new ThemeSettingsData();
                data.SelectedThemeId = root.GetString("SelectedThemeId", "modern_aero");
                data.SelectedLanguage = root.GetString("SelectedLanguage", "auto");
                data.RenderMode = root.GetInt("RenderMode", 1);
                data.HideStockNavball = root.GetBool("HideStockNavball", true);
                data.HideStockAltimeter = root.GetBool("HideStockAltimeter", false);
                data.HideStockBottomLeft = root.GetBool("HideStockBottomLeft", false);
                data.HideStockTimeWarp = root.GetBool("HideStockTimeWarp", false);
                data.HideStockCommNet = root.GetBool("HideStockCommNet", false);
                data.HideStockToolbar = root.GetBool("HideStockToolbar", false);
                data.ToolbarStyleMode = root.GetInt("ToolbarStyleMode", 1);
                data.NonFlightToolbarMode = root.GetInt("NonFlightToolbarMode", 1);
                data.MasterBypass = root.GetBool("MasterBypass", false);
                data.ShowPerformanceBadge = root.GetBool("ShowPerformanceBadge", false);
                data.EnableGpu2DUIAcceleration = root.GetBool("EnableGpu2DUIAcceleration", true);
                data.AutoAdaptResolution = root.GetBool("AutoAdaptResolution", true);
                data.GlobalRenderScaleMultiplier = root.GetFloat("GlobalRenderScaleMultiplier", 1.0f);
                data.GlobalRefreshProfile = root.GetInt("GlobalRefreshProfile", 1);
                data.RefreshControlMode = root.GetInt("RefreshControlMode", 0);
                data.GlobalStandardHz = root.GetFloat("GlobalStandardHz", 60.0f);
                data.GlobalSlowHz = root.GetFloat("GlobalSlowHz", 30.0f);
                data.GlobalRelaxedHz = root.GetFloat("GlobalRelaxedHz", 10.0f);
                data.GlobalUltraLowHz = root.GetFloat("GlobalUltraLowHz", 2.0f);
                data.GlobalDataHeartbeatHz = root.GetFloat("GlobalDataHeartbeatHz", 0f);

                // Dock 规则列表
                var rulesArr = root.GetArray("DockRules");
                if (rulesArr != null)
                {
                    data.DockRules = new List<DockButtonRule>();
                    for (int i = 0; i < rulesArr.Count; i++)
                    {
                        var rObj = rulesArr[i]?.AsObject();
                        if (rObj == null) continue;
                        var rule = new DockButtonRule
                        {
                            Key = rObj.GetString("Key", ""),
                            DefaultName = rObj.GetString("DefaultName", ""),
                            CustomLabel = rObj.GetString("CustomLabel", ""),
                            IsVisible = rObj.GetBool("IsVisible", true),
                            IsFavorite = rObj.GetBool("IsFavorite", false)
                        };
                        data.DockRules.Add(rule);
                    }
                }

                data.DockShowHiddenDrawer = root.GetBool("DockShowHiddenDrawer", false);
                data.DockOrientation = root.GetInt("DockOrientation", 0);
                data.DockEnableFavoritePanel = root.GetBool("DockEnableFavoritePanel", true);
                data.DockFavoriteOrientation = root.GetInt("DockFavoriteOrientation", 1);
                data.DockKeepFavoritesInMain = root.GetBool("DockKeepFavoritesInMain", false);
                data.DockFavoritePosX = root.GetFloat("DockFavoritePosX", 0f);
                data.DockFavoritePosY = root.GetFloat("DockFavoritePosY", -380f);

                data.SettingsWindowX = root.GetFloat("SettingsWindowX", -1f);
                data.SettingsWindowY = root.GetFloat("SettingsWindowY", -1f);
                data.SettingsWindowWidth = root.GetFloat("SettingsWindowWidth", 1040f);
                data.SettingsWindowHeight = root.GetFloat("SettingsWindowHeight", 740f);
                data.SettingsWindowMaximized = root.GetBool("SettingsWindowMaximized", false);

                return data;
            }
            catch (Exception ex)
            {
                error = $"Theme preferences JSON parse exception: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// 将主题设置序列化为标准化 JSON。
        /// </summary>
        public static string SerializeThemeSettings(ThemeSettingsData data, bool pretty = true)
        {
            if (data == null) return "{}";

            var root = new JsonObject();
            root.Add("SelectedThemeId", data.SelectedThemeId ?? "modern_aero");
            root.Add("SelectedLanguage", data.SelectedLanguage ?? "auto");
            root.Add("RenderMode", data.RenderMode);
            root.Add("HideStockNavball", data.HideStockNavball);
            root.Add("HideStockAltimeter", data.HideStockAltimeter);
            root.Add("HideStockBottomLeft", data.HideStockBottomLeft);
            root.Add("HideStockTimeWarp", data.HideStockTimeWarp);
            root.Add("HideStockCommNet", data.HideStockCommNet);
            root.Add("HideStockToolbar", data.HideStockToolbar);
            root.Add("ToolbarStyleMode", data.ToolbarStyleMode);
            root.Add("NonFlightToolbarMode", data.NonFlightToolbarMode);
            root.Add("MasterBypass", data.MasterBypass);
            root.Add("ShowPerformanceBadge", data.ShowPerformanceBadge);
            root.Add("EnableGpu2DUIAcceleration", data.EnableGpu2DUIAcceleration);
            root.Add("AutoAdaptResolution", data.AutoAdaptResolution);
            root.Add("GlobalRenderScaleMultiplier", data.GlobalRenderScaleMultiplier);
            root.Add("GlobalRefreshProfile", data.GlobalRefreshProfile);
            root.Add("RefreshControlMode", data.RefreshControlMode);
            root.Add("GlobalStandardHz", data.GlobalStandardHz);
            root.Add("GlobalSlowHz", data.GlobalSlowHz);
            root.Add("GlobalRelaxedHz", data.GlobalRelaxedHz);
            root.Add("GlobalUltraLowHz", data.GlobalUltraLowHz);
            root.Add("GlobalDataHeartbeatHz", data.GlobalDataHeartbeatHz);

            var rulesArr = new JsonArray();
            if (data.DockRules != null)
            {
                for (int i = 0; i < data.DockRules.Count; i++)
                {
                    var r = data.DockRules[i];
                    if (r == null) continue;
                    var rObj = new JsonObject();
                    rObj.Add("Key", r.Key ?? "");
                    rObj.Add("DefaultName", r.DefaultName ?? "");
                    rObj.Add("CustomLabel", r.CustomLabel ?? "");
                    rObj.Add("IsVisible", r.IsVisible);
                    rObj.Add("IsFavorite", r.IsFavorite);
                    rulesArr.Add(rObj);
                }
            }
            root.Add("DockRules", rulesArr);

            root.Add("DockShowHiddenDrawer", data.DockShowHiddenDrawer);
            root.Add("DockOrientation", data.DockOrientation);
            root.Add("DockEnableFavoritePanel", data.DockEnableFavoritePanel);
            root.Add("DockFavoriteOrientation", data.DockFavoriteOrientation);
            root.Add("DockKeepFavoritesInMain", data.DockKeepFavoritesInMain);
            root.Add("DockFavoritePosX", data.DockFavoritePosX);
            root.Add("DockFavoritePosY", data.DockFavoritePosY);

            root.Add("SettingsWindowX", data.SettingsWindowX);
            root.Add("SettingsWindowY", data.SettingsWindowY);
            root.Add("SettingsWindowWidth", data.SettingsWindowWidth);
            root.Add("SettingsWindowHeight", data.SettingsWindowHeight);
            root.Add("SettingsWindowMaximized", data.SettingsWindowMaximized);

            return root.ToString(pretty);
        }

        // ---------------------------------------------------------------------
        // C. 深拷贝工具辅助方法 (Deep Clone)
        // ---------------------------------------------------------------------

        public static WidgetLayoutData CloneLayout(WidgetLayoutData source)
        {
            if (source == null) return null;
            string json = SerializeLayout(source, false);
            return ParseLayout(json, out _);
        }

        public static ThemeSettingsData CloneThemeSettings(ThemeSettingsData source)
        {
            if (source == null) return null;
            string json = SerializeThemeSettings(source, false);
            return ParseThemeSettings(json, out _);
        }
    }
}
