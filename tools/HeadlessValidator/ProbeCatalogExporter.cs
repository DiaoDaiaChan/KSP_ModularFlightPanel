using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mono.Cecil;

namespace ModularFlightPanel.HeadlessValidator
{
    public class ProbeCatalogModel
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "1.0";

        [JsonPropertyName("generatedAt")]
        public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        [JsonPropertyName("description")]
        public string Description { get; set; } = "Modular Flight Panel (MFP) 15 大航电外部探针遥测 API 全量查表字典";

        [JsonPropertyName("totalProbes")]
        public int TotalProbes { get; set; }

        [JsonPropertyName("totalParameters")]
        public int TotalParameters { get; set; }

        [JsonPropertyName("probes")]
        public List<ProbeDefinitionModel> Probes { get; set; } = new List<ProbeDefinitionModel>();
    }

    public class ProbeDefinitionModel
    {
        [JsonPropertyName("probeId")]
        public string ProbeId { get; set; }

        [JsonPropertyName("probeClassName")]
        public string ProbeClassName { get; set; }

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; }

        [JsonPropertyName("targetMod")]
        public string TargetMod { get; set; }

        [JsonPropertyName("targetAssembly")]
        public string TargetAssembly { get; set; }

        [JsonPropertyName("isAssemblyPresent")]
        public bool IsAssemblyPresent { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("modTagAliases")]
        public List<string> ModTagAliases { get; set; } = new List<string>();

        [JsonPropertyName("parameterCount")]
        public int ParameterCount => Parameters.Count;

        [JsonPropertyName("parameters")]
        public List<TelemetryParameterModel> Parameters { get; set; } = new List<TelemetryParameterModel>();
    }

    public class TelemetryParameterModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; }

        [JsonPropertyName("accessType")]
        public string AccessType { get; set; }

        [JsonPropertyName("sourceType")]
        public string SourceType { get; set; }

        [JsonPropertyName("descriptionZh")]
        public string DescriptionZh { get; set; }

        [JsonPropertyName("descriptionEn")]
        public string DescriptionEn { get; set; }

        [JsonPropertyName("aliases")]
        public List<string> Aliases { get; set; } = new List<string>();

        [JsonPropertyName("subModifiers")]
        public List<string> SubModifiers { get; set; } = new List<string>();
    }

    public static class ProbeCatalogExporter
    {
        public static int ExportCatalog(string repoRoot)
        {
            Console.WriteLine("[CatalogExporter] 正在初始化 15 大模组遥测探针元数据反射与代码提取器...");
            string kspGameData = @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData";
            
            var catalog = new ProbeCatalogModel();
            
            // 构建 15 大探针目录
            var builder = new ProbeCatalogBuilder(repoRoot, kspGameData);
            catalog.Probes = builder.BuildAllProbes();
            catalog.TotalProbes = catalog.Probes.Count;
            catalog.TotalParameters = catalog.Probes.Sum(p => p.Parameters.Count);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string jsonOutput = JsonSerializer.Serialize(catalog, options);

            // 输出到两处：docs 与 GameData/ModularFlightPanel/PluginData
            string docsPath = Path.Combine(repoRoot, "docs", "PROBE_TELEMETRY_API_CATALOG.json");
            string pluginDataDir = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "PluginData");
            string pluginDataPath = Path.Combine(pluginDataDir, "probe_telemetry_catalog.json");

            Directory.CreateDirectory(Path.GetDirectoryName(docsPath));
            Directory.CreateDirectory(pluginDataDir);

            File.WriteAllText(docsPath, jsonOutput);
            File.WriteAllText(pluginDataPath, jsonOutput);

            Console.WriteLine($"[CatalogExporter] 成功提取并输出 15 大探针共 {catalog.TotalParameters} 个可用遥测参数！");
            Console.WriteLine($"[CatalogExporter] 文档输出: {docsPath}");
            Console.WriteLine($"[CatalogExporter] 游戏内插件数据: {pluginDataPath}");

            return 0;
        }
    }
}
