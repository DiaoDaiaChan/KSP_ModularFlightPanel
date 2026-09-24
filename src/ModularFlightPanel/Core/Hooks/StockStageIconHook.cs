using System;
using UnityEngine;
using UnityEngine.UI;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// KSP 原版 StageManager 分级图标贴图与 UV 映射深度挂钩器 (Zero-Overhead Stock Stage Hook)
    /// 直接提取原版 defaultIconMap 图集与 StageIcon UV，实现与原版分级图标的无缝对齐
    /// </summary>
    public class StockStageIconHook : IStockStageIconProvider
    {
        private Texture2D _cachedAtlas;
        private float _lastAtlasCheckTime = -10f;

        public Texture StockAtlas
        {
            get
            {
                float now = Time.unscaledTime;
                if (_cachedAtlas == null || (now - _lastAtlasCheckTime > 3.0f))
                {
                    _lastAtlasCheckTime = now;
                    _cachedAtlas = FindStockAtlas();
                }
                return _cachedAtlas;
            }
        }

        public bool HasStockAtlas => StockAtlas != null;

        private static System.Reflection.FieldInfo _defaultIconMapField;
        private static System.Reflection.FieldInfo _iconImageField;
        private static bool _fieldsResolved = false;

        private static void EnsureFields()
        {
            if (_fieldsResolved) return;
            _fieldsResolved = true;
            try
            {
                var type = typeof(KSP.UI.Screens.StageIcon);
                _defaultIconMapField = type.GetField("defaultIconMap", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                _iconImageField = type.GetField("iconImage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }
            catch { }
        }

        private static Texture2D ExtractTextureFromIcon(KSP.UI.Screens.StageIcon icon)
        {
            if (icon == null) return null;
            EnsureFields();
            if (_defaultIconMapField != null)
            {
                try
                {
                    if (_defaultIconMapField.GetValue(icon) is Texture2D tDef) return tDef;
                }
                catch { }
            }
            if (_iconImageField != null)
            {
                try
                {
                    var img = _iconImageField.GetValue(icon) as RawImage;
                    if (img != null && img.texture is Texture2D tImg) return tImg;
                }
                catch { }
            }
            var raw = icon.GetComponentInChildren<RawImage>(true);
            if (raw != null && raw.texture is Texture2D t1) return t1;
            var uiImg = icon.GetComponentInChildren<Image>(true);
            if (uiImg != null && uiImg.sprite != null && uiImg.sprite.texture != null) return uiImg.sprite.texture;
            return null;
        }

        private Texture2D FindStockAtlas()
        {
            try
            {
                if (KSP.UI.Screens.StageManager.Instance != null)
                {
                    var mgr = KSP.UI.Screens.StageManager.Instance;
                    if (mgr.stageIconPrefab != null)
                    {
                        var tex = ExtractTextureFromIcon(mgr.stageIconPrefab);
                        if (tex != null) return tex;
                    }

                    // 从当前激活级或任一已实例化图标获取
                    var stages = mgr.Stages;
                    if (stages != null)
                    {
                        for (int i = 0; i < stages.Count; i++)
                        {
                            var stg = stages[i];
                            if (stg != null && stg.Icons != null)
                            {
                                for (int j = 0; j < stg.Icons.Count; j++)
                                {
                                    var tex = ExtractTextureFromIcon(stg.Icons[j]);
                                    if (tex != null) return tex;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] StockStageIconHook FindStockAtlas warning: {ex.Message}");
            }
            return null;
        }

        public Rect GetStockIconUv(string iconType)
        {
            int index = StageIconAtlasGenerator.GetIconIndex(iconType);
            return GetStockIconUv(index);
        }

        public Rect GetStockIconUv(int iconIndex)
        {
            Texture atlas = StockAtlas;
            if (atlas != null && atlas.width > 0)
            {
                // KSP 原版 StageIcon.SetIcon 权威 UV 布局算法
                int iconSize = 32;
                int cols = atlas.width / iconSize;
                if (cols > 0)
                {
                    int x = iconIndex % cols;
                    int y = iconIndex / cols;
                    float num = (float)atlas.width / (float)iconSize;
                    return new Rect((float)x / num, 1f - (float)(y + 1) / num, 1f / num, 1f / num);
                }
            }
            return StageIconAtlasGenerator.GetIconUv(iconIndex);
        }
    }
}
