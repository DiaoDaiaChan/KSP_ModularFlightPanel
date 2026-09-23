using System;
using System.IO;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    public static class AssetLoader
    {
        private static AssetBundle _bundle;
        private static bool _attemptedLoad = false;

        public static Shader EnhancedShader { get; private set; }
        public static Shader ProceduralShader { get; private set; }
        public static Shader HalftoneShader { get; private set; }
        public static Shader ModernShader { get; private set; }
        public static Shader RadialMeterShader { get; private set; }
        public static Shader NeonGlowShader { get; private set; }
        public static Shader GlassCockpitShader { get; private set; }

        public static void LoadBundle()
        {
            if (_attemptedLoad) return;
            _attemptedLoad = true;

            string bundlePath = Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/AssetBundles/modularflightpanel.ksp");
            if (File.Exists(bundlePath))
            {
                try
                {
                    _bundle = AssetBundle.LoadFromFile(bundlePath);
                    if (_bundle != null)
                    {
                        EnhancedShader = _bundle.LoadAsset<Shader>("Assets/Shaders/NavballEnhanced.shader");
                        ProceduralShader = _bundle.LoadAsset<Shader>("Assets/Shaders/NavballProcedural.shader");
                        HalftoneShader = _bundle.LoadAsset<Shader>("Assets/Shaders/NavballHalftone.shader");
                        ModernShader = _bundle.LoadAsset<Shader>("Assets/Shaders/NavballModern.shader");
                        RadialMeterShader = _bundle.LoadAsset<Shader>("Assets/Shaders/RadialSegmentedMeter.shader");
                        NeonGlowShader = _bundle.LoadAsset<Shader>("Assets/Shaders/NeonGlowUI.shader");
                        GlassCockpitShader = _bundle.LoadAsset<Shader>("Assets/Shaders/GlassCockpitUI.shader");
                        Debug.Log("[ModularFlightPanel] Successfully loaded custom shaders from AssetBundle!");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModularFlightPanel] Error loading AssetBundle: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[ModularFlightPanel] AssetBundle not found at: {bundlePath}. Attempting Shader.Find fallbacks.");
            }

            // Fallbacks
            if (EnhancedShader == null) EnhancedShader = Shader.Find("ModularFlightPanel/NavballEnhanced") ?? Shader.Find("Unlit/Texture");
            if (ProceduralShader == null) ProceduralShader = Shader.Find("ModularFlightPanel/NavballProcedural") ?? Shader.Find("Unlit/Texture");
            if (HalftoneShader == null) HalftoneShader = Shader.Find("ModularFlightPanel/NavballHalftone") ?? Shader.Find("Unlit/Texture");
            if (ModernShader == null) ModernShader = Shader.Find("ModularFlightPanel/NavballModern") ?? Shader.Find("Unlit/Texture");
            if (RadialMeterShader == null) RadialMeterShader = Shader.Find("ModularFlightPanel/RadialSegmentedMeter") ?? Shader.Find("UI/Default");
            if (NeonGlowShader == null) NeonGlowShader = Shader.Find("ModularFlightPanel/NeonGlowUI") ?? Shader.Find("UI/Default");
            if (GlassCockpitShader == null) GlassCockpitShader = Shader.Find("ModularFlightPanel/GlassCockpitUI") ?? Shader.Find("UI/Default");
        }

        public static void UnloadBundle()
        {
            if (_bundle != null)
            {
                _bundle.Unload(true);
                _bundle = null;
            }
            _attemptedLoad = false;
        }
    }
}
