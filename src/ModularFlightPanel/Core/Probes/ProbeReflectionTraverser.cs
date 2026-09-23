using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// 遍历反射出的成员元数据与取值委托
    /// </summary>
    public class TraversedMember
    {
        public string ModTag { get; set; }
        public string MemberName { get; set; }
        public string NormalizedKey { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public Type ValueType { get; set; }
        public Func<object> Getter { get; set; }

        public bool IsNumeric =>
            ValueType == typeof(double) ||
            ValueType == typeof(float) ||
            ValueType == typeof(int) ||
            ValueType == typeof(long) ||
            ValueType == typeof(short) ||
            ValueType == typeof(byte) ||
            ValueType == typeof(uint) ||
            ValueType == typeof(ulong) ||
            ValueType == typeof(ushort) ||
            ValueType == typeof(sbyte) ||
            ValueType == typeof(decimal);

        public bool IsVector =>
            ValueType == typeof(Vector3) ||
            ValueType == typeof(Vector3d) ||
            ValueType == typeof(Vector2) ||
            ValueType == typeof(Vector2d);

        public bool IsBoolean => ValueType == typeof(bool);
        public bool IsTimeSpan => ValueType == typeof(TimeSpan);
    }

    /// <summary>
    /// 外部模组公开 API 通用反射遍历引擎 (Zero-Omission Traversal Engine)
    /// 彻底遍历目标类中的全部公开属性、字段与无参方法，建立 O(1) 委托注册表，绝不漏掉任何遥测指标。
    /// </summary>
    public class ProbeReflectionTraverser
    {
        private readonly string _modTag;
        private readonly Dictionary<string, TraversedMember> _memberDict = new Dictionary<string, TraversedMember>(StringComparer.OrdinalIgnoreCase);
        private readonly List<TraversedMember> _allMembers = new List<TraversedMember>();

        public ProbeReflectionTraverser(string modTag)
        {
            _modTag = modTag;
        }

        public IReadOnlyList<TraversedMember> AllMembers => _allMembers;
        public int DiscoveredCount => _allMembers.Count;

        /// <summary>
        /// 将字符串键名标准化（转大写、剥离下划线和连字符）
        /// </summary>
        public static string NormalizeKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return raw.Replace("_", "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
        }

        /// <summary>
        /// 遍历指定类型的公开静态成员 (别名重载)
        /// </summary>
        public int TraverseType(Type type, string categoryPrefix = null) => TraverseStatic(type, categoryPrefix);

        /// <summary>
        /// 遍历指定类型的公开静态成员 (字段、属性、无参方法)
        /// </summary>
        public int TraverseStatic(Type type, string categoryPrefix = null)
        {
            if (type == null) return 0;
            int count = 0;
            string cat = string.IsNullOrEmpty(categoryPrefix) ? type.Name : categoryPrefix;

            // 1. 公开静态属性
            PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < props.Length; i++)
            {
                PropertyInfo pi = props[i];
                if (!pi.CanRead || pi.GetIndexParameters().Length > 0) continue;
                MethodInfo getter = pi.GetGetMethod();
                if (getter == null) continue;

                RegisterMember(new TraversedMember
                {
                    ModTag = _modTag,
                    MemberName = pi.Name,
                    NormalizedKey = NormalizeKey(pi.Name),
                    Category = cat,
                    Description = $"{type.Name}.{pi.Name} (Property)",
                    ValueType = pi.PropertyType,
                    Getter = () =>
                    {
                        try { return getter.Invoke(null, null); }
                        catch { return null; }
                    }
                });
                count++;
            }

            // 2. 公开静态字段
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo fi = fields[i];
                RegisterMember(new TraversedMember
                {
                    ModTag = _modTag,
                    MemberName = fi.Name,
                    NormalizedKey = NormalizeKey(fi.Name),
                    Category = cat,
                    Description = $"{type.Name}.{fi.Name} (Field)",
                    ValueType = fi.FieldType,
                    Getter = () =>
                    {
                        try { return fi.GetValue(null); }
                        catch { return null; }
                    }
                });
                count++;
            }

            // 3. 公开静态无参方法
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo mi = methods[i];
                if (mi.IsSpecialName) continue; // 排除属性的 get_/set_
                if (mi.ReturnType == typeof(void)) continue;
                if (mi.GetParameters().Length > 0) continue;

                RegisterMember(new TraversedMember
                {
                    ModTag = _modTag,
                    MemberName = mi.Name,
                    NormalizedKey = NormalizeKey(mi.Name),
                    Category = cat,
                    Description = $"{type.Name}.{mi.Name}() (Method)",
                    ValueType = mi.ReturnType,
                    Getter = () =>
                    {
                        try { return mi.Invoke(null, null); }
                        catch { return null; }
                    }
                });
                count++;
            }

            return count;
        }

        /// <summary>
        /// 遍历指定类型的公开实例成员 (字段、属性、无参方法)，每次求值时通过 instanceProvider 获得实例
        /// </summary>
        public int TraverseInstance(Type type, Func<object> instanceProvider, string categoryPrefix = null)
        {
            if (type == null || instanceProvider == null) return 0;
            int count = 0;
            string cat = string.IsNullOrEmpty(categoryPrefix) ? type.Name : categoryPrefix;

            // 1. 公开实例属性
            PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < props.Length; i++)
            {
                PropertyInfo pi = props[i];
                if (!pi.CanRead || pi.GetIndexParameters().Length > 0) continue;
                MethodInfo getter = pi.GetGetMethod();
                if (getter == null) continue;

                RegisterMember(new TraversedMember
                {
                    ModTag = _modTag,
                    MemberName = pi.Name,
                    NormalizedKey = NormalizeKey(pi.Name),
                    Category = cat,
                    Description = $"{type.Name}.{pi.Name} (Property)",
                    ValueType = pi.PropertyType,
                    Getter = () =>
                    {
                        object inst = instanceProvider();
                        if (inst == null) return null;
                        try { return getter.Invoke(inst, null); }
                        catch { return null; }
                    }
                });
                count++;
            }

            // 2. 公开实例字段
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo fi = fields[i];
                RegisterMember(new TraversedMember
                {
                    ModTag = _modTag,
                    MemberName = fi.Name,
                    NormalizedKey = NormalizeKey(fi.Name),
                    Category = cat,
                    Description = $"{type.Name}.{fi.Name} (Field)",
                    ValueType = fi.FieldType,
                    Getter = () =>
                    {
                        object inst = instanceProvider();
                        if (inst == null) return null;
                        try { return fi.GetValue(inst); }
                        catch { return null; }
                    }
                });
                count++;
            }

            // 3. 公开实例无参方法
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo mi = methods[i];
                if (mi.IsSpecialName) continue;
                if (mi.ReturnType == typeof(void)) continue;
                if (mi.GetParameters().Length > 0) continue;

                RegisterMember(new TraversedMember
                {
                    ModTag = _modTag,
                    MemberName = mi.Name,
                    NormalizedKey = NormalizeKey(mi.Name),
                    Category = cat,
                    Description = $"{type.Name}.{mi.Name}() (Method)",
                    ValueType = mi.ReturnType,
                    Getter = () =>
                    {
                        object inst = instanceProvider();
                        if (inst == null) return null;
                        try { return mi.Invoke(inst, null); }
                        catch { return null; }
                    }
                });
                count++;
            }

            return count;
        }

        /// <summary>
        /// 手动注册/注入自定义计算成员或补充成员
        /// </summary>
        public void RegisterCustom(string name, Type valueType, Func<object> getter, string category, string description = null, string[] aliases = null)
        {
            var member = new TraversedMember
            {
                ModTag = _modTag,
                MemberName = name,
                NormalizedKey = NormalizeKey(name),
                Category = category,
                Description = description ?? name,
                ValueType = valueType,
                Getter = getter
            };
            RegisterMember(member);
            if (aliases != null)
            {
                for (int i = 0; i < aliases.Length; i++)
                {
                    RegisterAlias(aliases[i], member);
                }
            }
        }

        /// <summary>
        /// 注册自定义数值成员便捷封装
        /// </summary>
        public void RegisterManualMember(string name, Func<double> getter, string description = null, params string[] aliases)
        {
            RegisterCustom(name, typeof(double), () =>
            {
                try { return (object)getter(); }
                catch { return double.NaN; }
            }, _modTag, description, aliases);
        }

        /// <summary>
        /// 注册自定义字符串成员便捷封装
        /// </summary>
        public void RegisterManualStringMember(string name, Func<string> getter, string description = null, params string[] aliases)
        {
            RegisterCustom(name, typeof(string), () =>
            {
                try { return getter(); }
                catch { return "---"; }
            }, _modTag, description, aliases);
        }

        /// <summary>
        /// 统一登记成员进主字典并建立归一化索引
        /// </summary>
        private void RegisterMember(TraversedMember member)
        {
            if (member == null || string.IsNullOrEmpty(member.MemberName)) return;

            string key = member.MemberName.ToUpperInvariant();
            if (!_memberDict.ContainsKey(key))
            {
                _memberDict[key] = member;
            }

            string norm = member.NormalizedKey;
            if (!string.IsNullOrEmpty(norm) && !_memberDict.ContainsKey(norm))
            {
                _memberDict[norm] = member;
            }

            _allMembers.Add(member);
        }

        /// <summary>
        /// 注册常用别名（如 DV -> deltaV, TWR -> actualThrustToWeight 等）
        /// </summary>
        public void RegisterAlias(string alias, string targetMemberName)
        {
            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(targetMemberName)) return;
            TraversedMember target = FindMember(targetMemberName);
            if (target != null)
            {
                RegisterAlias(alias, target);
            }
        }

        public void RegisterAlias(string alias, TraversedMember member)
        {
            if (string.IsNullOrEmpty(alias) || member == null) return;
            string aUpper = alias.ToUpperInvariant();
            _memberDict[aUpper] = member;
            string norm = NormalizeKey(alias);
            if (!string.IsNullOrEmpty(norm))
            {
                _memberDict[norm] = member;
            }
        }

        public TraversedMember FindMember(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_memberDict.TryGetValue(name, out var m)) return m;
            string norm = NormalizeKey(name);
            if (_memberDict.TryGetValue(norm, out m)) return m;
            return null;
        }

        /// <summary>
        /// 双精度浮点数值安全解算 (支持 :X, :Y, :Z, :MAG 向量分量与布尔值转换)
        /// </summary>
        public double ResolveNumeric(string subTag, string modifier = null)
        {
            TraversedMember m = FindMember(subTag);
            if (m == null || m.Getter == null) return double.NaN;

            object raw;
            try
            {
                raw = m.Getter();
            }
            catch
            {
                return double.NaN;
            }

            if (raw == null) return double.NaN;

            // 1. 浮点与数值类型
            if (raw is double d) return d;
            if (raw is float f) return f;
            if (raw is int i) return i;
            if (raw is long l) return l;
            if (raw is uint ui) return ui;
            if (raw is ulong ul) return ul;
            if (raw is short s) return s;
            if (raw is ushort us) return us;
            if (raw is byte b) return b;
            if (raw is sbyte sb) return sb;
            if (raw is decimal dec) return (double)dec;

            // 2. 布尔类型
            if (raw is bool bVal) return bVal ? 1.0 : 0.0;

            // 3. TimeSpan 类型
            if (raw is TimeSpan ts) return ts.TotalSeconds;

            // 4. 枚举类型
            if (raw is Enum enumVal)
            {
                try { return Convert.ToDouble(Convert.ToInt64(enumVal)); }
                catch { return double.NaN; }
            }

            // 5. 三维向量类型 (Vector3 / Vector3d)
            if (raw is Vector3 v3)
            {
                string mod = modifier?.ToUpperInvariant();
                if (mod == "X") return v3.x;
                if (mod == "Y") return v3.y;
                if (mod == "Z") return v3.z;
                return v3.magnitude;
            }
            if (raw is Vector3d v3d)
            {
                string mod = modifier?.ToUpperInvariant();
                if (mod == "X") return v3d.x;
                if (mod == "Y") return v3d.y;
                if (mod == "Z") return v3d.z;
                return v3d.magnitude;
            }
            if (raw is Vector2 v2)
            {
                string mod = modifier?.ToUpperInvariant();
                if (mod == "X") return v2.x;
                if (mod == "Y") return v2.y;
                return v2.magnitude;
            }

            // 6. 字符串尝试解析
            if (raw is string strVal)
            {
                if (double.TryParse(strVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                    return parsed;
            }

            return double.NaN;
        }

        /// <summary>
        /// 尝试解算数值型遥测参数
        /// </summary>
        public bool TryResolveNumeric(string subTag, out double value)
        {
            value = ResolveNumeric(subTag);
            return !double.IsNaN(value);
        }

        /// <summary>
        /// 字符串安全格式化解算
        /// </summary>
        public string ResolveString(string subTag, string format = null, string modifier = null)
        {
            TraversedMember m = FindMember(subTag);
            if (m == null || m.Getter == null) return "---";

            object raw;
            try
            {
                raw = m.Getter();
            }
            catch
            {
                return "ERR";
            }

            if (raw == null) return "---";

            // 1. 布尔
            if (raw is bool b) return b ? "TRUE" : "FALSE";

            // 2. 向量
            if (raw is Vector3 v3)
            {
                string mod = modifier?.ToUpperInvariant();
                if (mod == "X") return FormatDouble(v3.x, format, "F2");
                if (mod == "Y") return FormatDouble(v3.y, format, "F2");
                if (mod == "Z") return FormatDouble(v3.z, format, "F2");
                if (mod == "MAG" || mod == "MAGNITUDE") return FormatDouble(v3.magnitude, format, "F2");
                return $"({v3.x:F1}, {v3.y:F1}, {v3.z:F1})";
            }
            if (raw is Vector3d v3d)
            {
                string mod = modifier?.ToUpperInvariant();
                if (mod == "X") return FormatDouble(v3d.x, format, "F2");
                if (mod == "Y") return FormatDouble(v3d.y, format, "F2");
                if (mod == "Z") return FormatDouble(v3d.z, format, "F2");
                if (mod == "MAG" || mod == "MAGNITUDE") return FormatDouble(v3d.magnitude, format, "F2");
                return $"({v3d.x:F1}, {v3d.y:F1}, {v3d.z:F1})";
            }

            // 3. TimeSpan 格式化
            if (raw is TimeSpan ts)
            {
                if (ts.TotalHours >= 1.0)
                    return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            }

            // 4. 数值类型格式化
            if (raw is double dVal) return FormatDouble(dVal, format, "F1");
            if (raw is float fVal) return FormatDouble(fVal, format, "F1");
            if (raw is int iVal) return iVal.ToString(string.IsNullOrEmpty(format) ? "D" : format);
            if (raw is long lVal) return lVal.ToString(string.IsNullOrEmpty(format) ? "D" : format);

            // 5. 其它对象转换为字符串
            return raw.ToString();
        }

        /// <summary>
        /// 尝试解算字符串型遥测参数
        /// </summary>
        public bool TryResolveString(string subTag, out string text)
        {
            text = ResolveString(subTag);
            return text != "---" && text != "ERR";
        }

        private static string FormatDouble(double val, string format, string defaultFmt)
        {
            if (double.IsNaN(val) || double.IsInfinity(val)) return "---";
            string fmt = string.IsNullOrEmpty(format) ? defaultFmt : format;
            try
            {
                return val.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return val.ToString(defaultFmt, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
