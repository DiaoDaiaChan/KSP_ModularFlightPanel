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
        public static Shader DotMatrixShader { get; private set; }
        public static Shader PhosphorHoloShader { get; private set; }
        public static Shader DigitalSegmentShader { get; private set; }
        public static Shader Vessel3DShader { get; private set; }
        public static Shader MinimalistAttitudeShader { get; private set; }
        public static Shader CrispAvionicsTextShader { get; private set; }
        public static Shader RaymarchShader { get; private set; }
        public static Shader AvionicsProceduralShader { get; private set; }
        public static Shader ModernWorkbenchShader { get; private set; }
        public static Shader TapeGaugeShader => AvionicsProceduralShader;

        public static void LoadBundle()
        {
            if (_attemptedLoad) return;
            _attemptedLoad = true;

#if UNITY_EDITOR
            // 在 Unity 编辑器与无头渲染环境下，优先加载当前工程源码着色器，保证修改即时热生效
            RaymarchShader = Shader.Find("ModularFlightPanel/NavballRaymarch");
            ProceduralShader = RaymarchShader ?? Shader.Find("ModularFlightPanel/NavballRaymarch");
            EnhancedShader = Shader.Find("ModularFlightPanel/NavballEnhanced");
            HalftoneShader = Shader.Find("ModularFlightPanel/NavballHalftone");
            ModernShader = Shader.Find("ModularFlightPanel/NavballModern");
            RadialMeterShader = Shader.Find("ModularFlightPanel/RadialSegmentedMeter");
            NeonGlowShader = Shader.Find("ModularFlightPanel/NeonGlowUI");
            GlassCockpitShader = Shader.Find("ModularFlightPanel/GlassCockpitUI");
            DotMatrixShader = Shader.Find("ModularFlightPanel/DotMatrixUI");
            PhosphorHoloShader = Shader.Find("ModularFlightPanel/PhosphorHoloUI");
            DigitalSegmentShader = Shader.Find("ModularFlightPanel/DigitalSegmentUI");
            Vessel3DShader = Shader.Find("ModularFlightPanel/Vessel3DTechnical");
            MinimalistAttitudeShader = Shader.Find("ModularFlightPanel/MinimalistAttitudeSphere");
            CrispAvionicsTextShader = Shader.Find("ModularFlightPanel/CrispAvionicsText");
            AvionicsProceduralShader = Shader.Find("ModularFlightPanel/AvionicsProceduralUI") ?? Shader.Find("ModularFlightPanel/AvionicsTapeGauge");
            ModernWorkbenchShader = Shader.Find("ModularFlightPanel/ModernWorkbenchGlass") ?? GlassCockpitShader;
            if (RaymarchShader != null && EnhancedShader != null)
            {
                MFPLogger.Info(MFPLogger.CatRender, "In Editor: Loaded live project shaders successfully!");
                return;
            }
#endif

            string bundlePath = Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/AssetBundles/modularflightpanel.ksp");
            if (File.Exists(bundlePath))
            {
                try
                {
                    _bundle = AssetBundle.LoadFromFile(bundlePath);
                    if (_bundle != null)
                    {
                        EnhancedShader = _bundle.LoadAsset<Shader>("Assets/Shaders/NavballEnhanced.shader");
                        RaymarchShader = _bundle.LoadAsset<Shader>("Assets/Shaders/NavballRaymarch.shader");
                        ProceduralShader = RaymarchShader;
                        HalftoneShader = _bundle.LoadAsset<Shader>("Assets/Shaders/NavballHalftone.shader");
                        ModernShader = _bundle.LoadAsset<Shader>("Assets/Shaders/NavballModern.shader");
                        RadialMeterShader = _bundle.LoadAsset<Shader>("Assets/Shaders/RadialSegmentedMeter.shader");
                        NeonGlowShader = _bundle.LoadAsset<Shader>("Assets/Shaders/NeonGlowUI.shader");
                        GlassCockpitShader = _bundle.LoadAsset<Shader>("Assets/Shaders/GlassCockpitUI.shader");
                        DotMatrixShader = _bundle.LoadAsset<Shader>("Assets/Shaders/DotMatrixUI.shader");
                        PhosphorHoloShader = _bundle.LoadAsset<Shader>("Assets/Shaders/PhosphorHoloUI.shader");
                        DigitalSegmentShader = _bundle.LoadAsset<Shader>("Assets/Shaders/DigitalSegmentUI.shader");
                        CrispAvionicsTextShader = _bundle.LoadAsset<Shader>("Assets/Shaders/CrispAvionicsText.shader");
                        AvionicsProceduralShader = _bundle.LoadAsset<Shader>("Assets/Shaders/AvionicsProceduralUI.shader") ?? _bundle.LoadAsset<Shader>("Assets/Shaders/AvionicsTapeGauge.shader");
                        ModernWorkbenchShader = _bundle.LoadAsset<Shader>("Assets/Shaders/ModernWorkbenchGlass.shader");
                        Vessel3DShader = _bundle.LoadAsset<Shader>("Assets/Shaders/Vessel3DTechnical.shader");
                        MinimalistAttitudeShader = _bundle.LoadAsset<Shader>("Assets/Shaders/MinimalistAttitudeSphere.shader");
                        MFPLogger.Info(MFPLogger.CatRender, "Successfully loaded custom shaders from AssetBundle!");
                    }
                }
                catch (Exception ex)
                {
                    MFPLogger.Exception(MFPLogger.CatRender, ex, "Error loading AssetBundle");
                }
            }
            else
            {
                MFPLogger.Warn(MFPLogger.CatRender, $"AssetBundle not found at: {bundlePath}. Attempting Shader.Find fallbacks.");
            }

            // Fallbacks
            if (RaymarchShader == null) RaymarchShader = Shader.Find("ModularFlightPanel/NavballRaymarch") ?? Shader.Find("UI/Default");
            if (ProceduralShader == null) ProceduralShader = RaymarchShader ?? Shader.Find("UI/Default");
            if (AvionicsProceduralShader == null) AvionicsProceduralShader = Shader.Find("ModularFlightPanel/AvionicsProceduralUI") ?? Shader.Find("ModularFlightPanel/AvionicsTapeGauge") ?? Shader.Find("UI/Default");
            if (ModernWorkbenchShader == null) ModernWorkbenchShader = Shader.Find("ModularFlightPanel/ModernWorkbenchGlass") ?? GlassCockpitShader ?? AvionicsProceduralShader ?? Shader.Find("UI/Default");
            if (EnhancedShader == null) EnhancedShader = Shader.Find("ModularFlightPanel/NavballEnhanced") ?? Shader.Find("Unlit/Texture");
            if (HalftoneShader == null) HalftoneShader = Shader.Find("ModularFlightPanel/NavballHalftone") ?? Shader.Find("Unlit/Texture");
            if (ModernShader == null) ModernShader = Shader.Find("ModularFlightPanel/NavballModern") ?? Shader.Find("Unlit/Texture");
            if (RadialMeterShader == null) RadialMeterShader = Shader.Find("ModularFlightPanel/RadialSegmentedMeter") ?? Shader.Find("UI/Default");
            if (NeonGlowShader == null) NeonGlowShader = Shader.Find("ModularFlightPanel/NeonGlowUI") ?? Shader.Find("UI/Default");
            if (GlassCockpitShader == null) GlassCockpitShader = Shader.Find("ModularFlightPanel/GlassCockpitUI") ?? Shader.Find("UI/Default");
            if (DotMatrixShader == null) DotMatrixShader = Shader.Find("ModularFlightPanel/DotMatrixUI") ?? Shader.Find("UI/Default");
            if (PhosphorHoloShader == null) PhosphorHoloShader = Shader.Find("ModularFlightPanel/PhosphorHoloUI") ?? Shader.Find("UI/Default");
            if (DigitalSegmentShader == null) DigitalSegmentShader = Shader.Find("ModularFlightPanel/DigitalSegmentUI") ?? Shader.Find("UI/Default");
            if (CrispAvionicsTextShader == null) CrispAvionicsTextShader = Shader.Find("ModularFlightPanel/CrispAvionicsText") ?? Shader.Find("UI/Default");
            if (Vessel3DShader == null) Vessel3DShader = Shader.Find("ModularFlightPanel/Vessel3DTechnical") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Diffuse");
            if (MinimalistAttitudeShader == null) MinimalistAttitudeShader = Shader.Find("ModularFlightPanel/MinimalistAttitudeSphere") ?? RaymarchShader ?? ProceduralShader ?? Shader.Find("UI/Default");
        }

        public static void UnloadBundle()
        {
            if (_bundle != null)
            {
                _bundle.Unload(false);
                _bundle = null;
            }
            _attemptedLoad = false;
        }
    }
}
