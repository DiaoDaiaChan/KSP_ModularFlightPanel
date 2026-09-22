using System.IO;
using UnityEditor;
using UnityEngine;

namespace ModularFlightPanel.Editor
{
    public class BuildAssetBundles
    {
        [MenuItem("ModularFlightPanel/Build AssetBundles")]
        public static void BuildAllAssetBundles()
        {
            string assetBundleDirectory = Path.Combine(Application.dataPath, "../../GameData/ModularFlightPanel/AssetBundles");
            if (!Directory.Exists(assetBundleDirectory))
            {
                Directory.CreateDirectory(assetBundleDirectory);
            }

            AssetBundleBuild[] buildMap = new AssetBundleBuild[1];
            buildMap[0].assetBundleName = "modularflightpanel.ksp";

            string[] shaders = Directory.GetFiles("Assets/Shaders", "*.shader", SearchOption.AllDirectories);
            buildMap[0].assetNames = shaders;

            Debug.Log($"[ModularFlightPanel] Starting AssetBundle build for target: {BuildTarget.StandaloneWindows64}...");
            Debug.Log($"[ModularFlightPanel] Bundling {shaders.Length} shaders into {buildMap[0].assetBundleName}...");

            BuildPipeline.BuildAssetBundles(
                assetBundleDirectory,
                buildMap,
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64
            );

            // Copy to KSP instance if it exists
            string kspTarget = @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData\ModularFlightPanel\AssetBundles";
            if (Directory.Exists(@"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData"))
            {
                if (!Directory.Exists(kspTarget)) Directory.CreateDirectory(kspTarget);
                foreach (string file in Directory.GetFiles(assetBundleDirectory))
                {
                    string destFile = Path.Combine(kspTarget, Path.GetFileName(file));
                    File.Copy(file, destFile, true);
                }
                Debug.Log($"[ModularFlightPanel] Synced AssetBundles to: {kspTarget}");
            }

            Debug.Log($"[ModularFlightPanel] AssetBundle build finished successfully! Output: {assetBundleDirectory}");
        }
    }
}
