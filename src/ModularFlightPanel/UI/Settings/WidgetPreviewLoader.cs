using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 航电组件预览图智能全自动映射与显存缓存中枢 (Avionics Widget Preview Texture Loader)
    /// 核心架构革新：
    /// 1. 彻底解耦硬编码字典：全自动动态扫描 PluginData 目录下的真实渲染图 (isolated_*.png)；
    /// 2. 具备元数据全自动匹配：基于 WidgetDescriptor 的 DefaultWidgetId、TypeName、ExactIds、Aliases 与词根归一化；
    /// 3. 实机无头渲染联动 (In-Game Live Bake)：若缺失离线切片，可联动 WidgetLiveBaker 在游戏内实机渲染一次并写入磁盘；
    /// 4. 单例显存常驻缓存 (Zero Per-Frame GC)：单次解码，逐帧 0 GC 极速复用；
    /// 5. 科技蓝图程序化保底 (Procedural Blueprint Fallback)：未烘焙组件优雅呈现暗晶座舱线框预览。
    /// </summary>
    public static class WidgetPreviewLoader
    {
        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _fileIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> _allFileKeys = new List<string>();
        private static bool _isIndexed = false;
        private static Texture2D _defaultBlueprintTex = null;

        /// <summary>
        /// 确保磁盘 isolated_*.png 文件全量自动索引
        /// </summary>
        public static void EnsureIndexed()
        {
            if (_isIndexed) return;
            _isIndexed = true;
            RefreshIndex();
        }

        /// <summary>
        /// 重新扫描并构建 PluginData 真实渲染图索引
        /// </summary>
        public static void RefreshIndex()
        {
            _fileIndex.Clear();
            _allFileKeys.Clear();

            string[] searchDirs = new string[]
            {
                Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData"),
                Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/Textures"),
                Path.Combine(Directory.GetCurrentDirectory(), "GameData/ModularFlightPanel/PluginData")
            };

            for (int d = 0; d < searchDirs.Length; d++)
            {
                string dir = searchDirs[d];
                if (!Directory.Exists(dir)) continue;

                try
                {
                    string[] pngFiles = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly);
                    for (int f = 0; f < pngFiles.Length; f++)
                    {
                        string filePath = pngFiles[f];
                        string fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();

                        // 1. 注册完整文件名 (例: isolated_core_navball)
                        IndexKey(fileName, filePath);

                        // 2. 剥离 isolated_ 前缀注册 (例: core_navball)
                        if (fileName.StartsWith("isolated_"))
                        {
                            string strippedIsolated = fileName.Substring(9);
                            IndexKey(strippedIsolated, filePath);

                            // 3. 剥离次级命名空间前缀 (例: core_navball -> navball, custom_b747_eicas -> b747_eicas)
                            int firstUnder = strippedIsolated.IndexOf('_');
                            if (firstUnder >= 0 && firstUnder < strippedIsolated.Length - 1)
                            {
                                string stem = strippedIsolated.Substring(firstUnder + 1);
                                IndexKey(stem, filePath);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MFPLogger.Warn("WidgetPreviewLoader", $"Error indexing directory '{dir}': {ex.Message}");
                }
            }

            MFPLogger.Info("WidgetPreviewLoader", $"Automatic file indexing completed: {_fileIndex.Count} indexed keys pointing to isolated renders.");
        }

        private static void IndexKey(string key, string path)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(path)) return;
            if (!_fileIndex.ContainsKey(key))
            {
                _fileIndex[key] = path;
                _allFileKeys.Add(key);
            }
        }

        /// <summary>
        /// 基于 WidgetDescriptor 动态元数据全自动推导最佳匹配渲染图路径
        /// </summary>
        public static string ResolvePreviewPath(WidgetDescriptor desc)
        {
            if (desc == null) return null;
            EnsureIndexed();

            // 1. 优先尝试 DefaultWidgetId 映射 (例: core.navball -> core_navball / isolated_core_navball)
            if (!string.IsNullOrEmpty(desc.DefaultWidgetId))
            {
                string safeId = desc.DefaultWidgetId.Replace('.', '_').ToLowerInvariant();
                if (_fileIndex.TryGetValue(safeId, out string p)) return p;
                if (_fileIndex.TryGetValue("isolated_" + safeId, out p)) return p;

                int dotIdx = desc.DefaultWidgetId.LastIndexOf('.');
                if (dotIdx >= 0 && dotIdx < desc.DefaultWidgetId.Length - 1)
                {
                    string idStem = desc.DefaultWidgetId.Substring(dotIdx + 1).ToLowerInvariant();
                    if (_fileIndex.TryGetValue(idStem, out p)) return p;
                    if (_fileIndex.TryGetValue("isolated_" + idStem, out p)) return p;
                }
            }

            // 2. 尝试 ExactIds 确切标识列表
            if (desc.ExactIds != null)
            {
                for (int i = 0; i < desc.ExactIds.Length; i++)
                {
                    string exact = desc.ExactIds[i];
                    if (string.IsNullOrEmpty(exact)) continue;
                    string safeExact = exact.Replace('.', '_').ToLowerInvariant();
                    if (_fileIndex.TryGetValue(safeExact, out string p)) return p;
                    if (_fileIndex.TryGetValue("isolated_" + safeExact, out p)) return p;
                }
            }

            // 3. 尝试 TypeName 与蛇形命名 (例: VesselAttitudeSphereWidget -> vessel_attitude_sphere)
            if (!string.IsNullOrEmpty(desc.TypeName))
            {
                string cleanType = desc.TypeName;
                if (cleanType.EndsWith("Widget", StringComparison.OrdinalIgnoreCase))
                {
                    cleanType = cleanType.Substring(0, cleanType.Length - 6);
                }
                string snakeType = ToSnakeCase(cleanType).ToLowerInvariant();
                if (_fileIndex.TryGetValue(snakeType, out string p)) return p;
                if (_fileIndex.TryGetValue("isolated_" + snakeType, out p)) return p;
                if (_fileIndex.TryGetValue(cleanType.ToLowerInvariant(), out p)) return p;
            }

            // 4. 尝试 Aliases 别名列表
            if (desc.Aliases != null)
            {
                for (int i = 0; i < desc.Aliases.Length; i++)
                {
                    string alias = desc.Aliases[i];
                    if (string.IsNullOrEmpty(alias)) continue;
                    string safeAlias = alias.Replace('.', '_').ToLowerInvariant();
                    if (_fileIndex.TryGetValue(safeAlias, out string p)) return p;
                    if (_fileIndex.TryGetValue("isolated_" + safeAlias, out p)) return p;
                }
            }

            // 5. 智能词元重合度模糊推导 (Token Overlap Fallback)
            string bestPath = FindBestTokenMatch(desc.TypeName + " " + desc.DefaultWidgetId);
            return bestPath;
        }

        /// <summary>
        /// 基于文本查询自动推导最佳匹配路径 (供参数化预设生成器使用)
        /// </summary>
        public static string ResolvePreviewPath(string rawKey)
        {
            if (string.IsNullOrEmpty(rawKey)) return null;
            EnsureIndexed();

            string clean = rawKey.Replace('.', '_').Replace('/', '_').Replace('\\', '_').ToLowerInvariant();

            // 直接匹配
            if (_fileIndex.TryGetValue(clean, out string path)) return path;
            if (_fileIndex.TryGetValue("isolated_" + clean, out path)) return path;

            // 去前缀匹配
            int underIdx = clean.IndexOf('_');
            if (underIdx >= 0 && underIdx < clean.Length - 1)
            {
                string stem = clean.Substring(underIdx + 1);
                if (_fileIndex.TryGetValue(stem, out path)) return path;
                if (_fileIndex.TryGetValue("isolated_" + stem, out path)) return path;
            }

            // 词元模糊匹配
            return FindBestTokenMatch(rawKey);
        }

        private static string FindBestTokenMatch(string query)
        {
            if (string.IsNullOrEmpty(query)) return null;
            string[] queryTokens = query.ToLowerInvariant().Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (queryTokens.Length == 0) return null;

            string bestMatchPath = null;
            int maxOverlap = 0;

            for (int i = 0; i < _allFileKeys.Count; i++)
            {
                string fileKey = _allFileKeys[i];
                int overlap = 0;
                for (int t = 0; t < queryTokens.Length; t++)
                {
                    string tok = queryTokens[t];
                    if (tok.Length >= 3 && fileKey.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        overlap += tok.Length;
                    }
                }

                if (overlap > maxOverlap && overlap >= 4)
                {
                    maxOverlap = overlap;
                    bestMatchPath = _fileIndex[fileKey];
                }
            }

            return bestMatchPath;
        }

        /// <summary>
        /// 获取组件元数据描述符的缩略预览纹理 (自动推导 + 实机渲染兜底 + 显存缓存)
        /// </summary>
        public static Texture2D GetPreviewTexture(WidgetDescriptor desc)
        {
            if (desc == null) return GetDefaultBlueprintTexture();

            string cacheKey = desc.TypeName ?? desc.DefaultWidgetId ?? "unknown";

            // 1. 显存缓存快速命中 (0 GC)
            if (_cache.TryGetValue(cacheKey, out Texture2D cachedTex) && cachedTex != null)
            {
                return cachedTex;
            }

            // 2. 自动磁盘路径推导
            string filePath = ResolvePreviewPath(desc);
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                Texture2D loaded = LoadTextureFromFile(filePath);
                if (loaded != null)
                {
                    _cache[cacheKey] = loaded;
                    return loaded;
                }
            }

            // 3. 实机无头渲染兜底 (在游戏运行时实机渲染一次)
            if (Application.isPlaying)
            {
                try
                {
                    Texture2D liveTex = WidgetLiveBaker.BakeWidget(desc);
                    if (liveTex != null)
                    {
                        RegisterBakedTexture(desc, liveTex);
                        return liveTex;
                    }
                }
                catch (Exception ex)
                {
                    MFPLogger.Warn("WidgetPreviewLoader", $"Live bake fallback failed for {desc.TypeName}: {ex.Message}");
                }
            }

            // 4. 科技蓝图程序化保底
            return GetDefaultBlueprintTexture();
        }

        /// <summary>
        /// 基于 Key 字符串获取预览贴图 (供 6 大参数化生成器条目调用)
        /// </summary>
        public static Texture2D GetPreviewTexture(string key1, string key2 = null, string key3 = null)
        {
            string primaryKey = key1 ?? key2 ?? key3;
            if (string.IsNullOrEmpty(primaryKey)) return GetDefaultBlueprintTexture();

            if (_cache.TryGetValue(primaryKey, out Texture2D cachedTex) && cachedTex != null)
            {
                return cachedTex;
            }

            string[] keys = new string[] { key1, key2, key3 };
            for (int k = 0; k < keys.Length; k++)
            {
                string queryKey = keys[k];
                if (string.IsNullOrEmpty(queryKey)) continue;

                string filePath = ResolvePreviewPath(queryKey);
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    Texture2D loaded = LoadTextureFromFile(filePath);
                    if (loaded != null)
                    {
                        _cache[primaryKey] = loaded;
                        return loaded;
                    }
                }
            }

            return GetDefaultBlueprintTexture();
        }

        /// <summary>
        /// 注册由实机离屏烘焙器生成的纹理至显存缓存
        /// </summary>
        public static void RegisterBakedTexture(WidgetDescriptor desc, Texture2D tex)
        {
            if (desc == null || tex == null) return;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            if (!string.IsNullOrEmpty(desc.TypeName)) _cache[desc.TypeName] = tex;
            if (!string.IsNullOrEmpty(desc.DefaultWidgetId)) _cache[desc.DefaultWidgetId] = tex;
        }

        private static Texture2D LoadTextureFromFile(string fullPath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes))
                {
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    return tex;
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Warn("WidgetPreviewLoader", $"Failed reading image '{fullPath}': {ex.Message}");
            }
            return null;
        }

        private static string ToSnakeCase(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && !char.IsUpper(str[i - 1]))
                    {
                        sb.Append('_');
                    }
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 程序化生成精致的暗晶座舱科技蓝图占位底图 (Procedural Dark Glass Avionics Blueprint)
        /// </summary>
        public static Texture2D GetDefaultBlueprintTexture()
        {
            if (_defaultBlueprintTex != null) return _defaultBlueprintTex;

            int w = 128;
            int h = 76;
            _defaultBlueprintTex = new Texture2D(w, h, TextureFormat.ARGB32, false);

            Color bg = new Color(0.04f, 0.07f, 0.11f, 0.95f);
            Color gridLine = new Color(0.00f, 0.88f, 1.00f, 0.09f);
            Color crosshair = new Color(0.00f, 0.88f, 1.00f, 0.25f);
            Color border = new Color(0.18f, 0.26f, 0.38f, 0.65f);

            int cx = w / 2;
            int cy = h / 2;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool isBorder = (x == 0 || x == w - 1 || y == 0 || y == h - 1);
                    if (isBorder)
                    {
                        _defaultBlueprintTex.SetPixel(x, y, border);
                        continue;
                    }

                    bool isGrid = (x % 16 == 0 || y % 16 == 0);
                    Color col = isGrid ? gridLine : bg;

                    if ((Math.Abs(x - cx) <= 12 && y == cy) || (Math.Abs(y - cy) <= 10 && x == cx))
                    {
                        col = crosshair;
                    }
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (Mathf.Abs(dist - 14f) < 1.0f)
                    {
                        col = crosshair;
                    }

                    _defaultBlueprintTex.SetPixel(x, y, col);
                }
            }

            _defaultBlueprintTex.Apply();
            _defaultBlueprintTex.wrapMode = TextureWrapMode.Clamp;
            _defaultBlueprintTex.filterMode = FilterMode.Bilinear;
            return _defaultBlueprintTex;
        }

        /// <summary>
        /// 清空显存缓存并重新索引
        /// </summary>
        public static void ClearCache()
        {
            foreach (var kvp in _cache)
            {
                if (kvp.Value != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value);
                }
            }
            _cache.Clear();
            _isIndexed = false;
        }
    }
}
