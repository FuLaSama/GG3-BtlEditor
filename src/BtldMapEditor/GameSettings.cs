/*
 * GameSettings.cs
 *
 * 编辑器「显示用」数据库，不参与 BTL 读写：
 *   def_terraintype.xml / def_mapterrain.xml → 气候、地表中文名
 *   Building/Fortification/Country/General/ArmySettings.json → 建筑工事国家将领兵种名
 *
 * 查找根目录：exe 旁 GameData，或 SetExternalDataDir 指定的外部目录。
 * 缺文件就空字典，UI 显示数字 ID，不要因此打不开编辑器。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace BtldMapEditor
{
    public class TerrainTypeSetting
    {
        public int Type { get; set; }
        public string Name { get; set; }
    }

    public class MapTerrainSetting
    {
        public int TerrainId { get; set; }
        public string Name { get; set; }
        public int Type { get; set; }
        public int Plants { get; set; }
        public List<string> Images { get; set; } = new List<string>();
    }

    public class BuildingSetting
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("Type")]
        public int Type { get; set; }
    }

    public class FortificationSetting
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("Feature")]
        public int Feature { get; set; }
    }

    public class CountrySetting
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; }
    }

    public class GeneralSetting
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; }
    }

    public class ArmySetting
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; }
    }

    /// <summary>兵种/地形/国家等 ID→名称。静态缓存，启动时 LoadAllSettings 一次。</summary>
    public static class GameSettings
    {
        // 数据库容器
        public static Dictionary<int, string> TerrainTypes { get; } = new Dictionary<int, string>();
        public static Dictionary<int, MapTerrainSetting> MapTerrains { get; } = new Dictionary<int, MapTerrainSetting>();
        public static Dictionary<int, BuildingSetting> Buildings { get; } = new Dictionary<int, BuildingSetting>();
        public static Dictionary<int, FortificationSetting> Fortifications { get; } = new Dictionary<int, FortificationSetting>();
        public static Dictionary<int, string> Countries { get; } = new Dictionary<int, string>();
        public static Dictionary<int, string> Generals { get; } = new Dictionary<int, string>();
        public static Dictionary<int, string> Units { get; } = new Dictionary<int, string>();

        static readonly string[] GameDataSubdirs = { "", "Terrain", "Buildings", "Entities", "Config", "Units", "EditorPalette" };

        /// <summary>exe 同目录下的 GameData（打包到 dist 时由 csproj 拷过去）。</summary>
        public static string GetGameDataRoot()
        {
            if (!string.IsNullOrEmpty(ExternalDataDir))
            {
                string external = Path.Combine(ExternalDataDir, "GameData");
                if (Directory.Exists(external))
                    return Path.GetFullPath(external);
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "GameData"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "GameData")),
            };

            foreach (string dir in candidates)
            {
                if (Directory.Exists(dir))
                    return Path.GetFullPath(dir);
            }

            return Path.GetFullPath(candidates[0]);
        }

        /// <summary>读 XML/JSON 填字典。单文件失败只打日志，继续加载其余。</summary>
        public static void LoadAllSettings()
        {
            try
            {
                // 1. 加载地形类型 (def_terraintype.xml)
                string terrainTypePath = FindFile("def_terraintype.xml");
                if (terrainTypePath != null)
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(terrainTypePath);
                    TerrainTypes.Clear();
                    foreach (XmlNode node in doc.SelectNodes("//terraintype"))
                    {
                        if (node.Attributes["type"] != null && node.Attributes["name"] != null)
                        {
                            int type = int.Parse(node.Attributes["type"].Value);
                            string name = node.Attributes["name"].Value;
                            TerrainTypes[type] = name;
                        }
                    }
                }

                // 2. 加载详细地形与地物 (def_mapterrain.xml 与 MapTerrainSettings.json / DoodadSettings.json)
                MapTerrains.Clear();
                string mapTerrainXmlPath = FindFile("def_mapterrain.xml");
                if (mapTerrainXmlPath != null)
                {
                    try
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(mapTerrainXmlPath);
                        foreach (XmlNode node in doc.SelectNodes("//terrain"))
                        {
                            if (node.Attributes["terrain"] != null && node.Attributes["name"] != null && node.Attributes["type"] != null)
                            {
                                int terrainId = int.Parse(node.Attributes["terrain"].Value);
                                string name = node.Attributes["name"].Value;
                                int type = int.Parse(node.Attributes["type"].Value);
                                var images = new List<string>();
                                foreach (XmlNode tile in node.SelectNodes("tile"))
                                {
                                    string image = tile.Attributes["image"]?.Value;
                                    images.Add(image ?? "");
                                }
                                MapTerrains[terrainId] = new MapTerrainSetting
                                {
                                    TerrainId = terrainId,
                                    Name = name,
                                    Type = type,
                                    Plants = node.Attributes["plants"] != null ? int.Parse(node.Attributes["plants"].Value) : 0,
                                    Images = images
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GameSettings] def_mapterrain.xml 加载出错: {ex.Message}");
                    }
                }

                // 支持从 JSON 文件加载地物 (MapTerrainSettings.json / DoodadSettings.json)
                string mapTerrainJsonPath = FindFile("MapTerrainSettings.json") ?? FindFile("DoodadSettings.json");
                if (mapTerrainJsonPath != null)
                {
                    try
                    {
                        string json = File.ReadAllText(mapTerrainJsonPath);
                        var list = JsonSerializer.Deserialize<List<MapTerrainSetting>>(json);
                        if (list != null)
                        {
                            foreach (var item in list)
                            {
                                MapTerrains[item.TerrainId] = item;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GameSettings] MapTerrainSettings.json 加载出错: {ex.Message}");
                    }
                }

                // 3. 加载建筑设定 (BuildingSettings.json)
                string buildingPath = FindFile("BuildingSettings.json");
                if (buildingPath != null)
                {
                    string json = File.ReadAllText(buildingPath);
                    var list = JsonSerializer.Deserialize<List<BuildingSetting>>(json) ?? new List<BuildingSetting>();
                    Buildings.Clear();
                    foreach (var b in list)
                    {
                        Buildings[b.Id] = b;
                    }
                }

                // 4. 加载工事设定 (FortificationSettings.json)
                string fortPath = FindFile("FortificationSettings.json");
                if (fortPath != null)
                {
                    string json = File.ReadAllText(fortPath);
                    var list = JsonSerializer.Deserialize<List<FortificationSetting>>(json) ?? new List<FortificationSetting>();
                    Fortifications.Clear();
                    foreach (var f in list)
                    {
                        Fortifications[f.Feature] = f; // 根据 Feature 属性映射，如雷达、战壕、地堡
                    }
                }

                // 5. 加载国家设定 (CountrySettings.json)
                string countryPath = FindFile("CountrySettings.json");
                if (countryPath != null)
                {
                    string json = File.ReadAllText(countryPath);
                    var list = JsonSerializer.Deserialize<List<CountrySetting>>(json) ?? new List<CountrySetting>();
                    Countries.Clear();
                    foreach (var c in list)
                    {
                        Countries[c.Id] = c.Name;
                    }
                }

                // 6. 加载武将设定 (GeneralSettings.json)
                string generalPath = FindFile("GeneralSettings.json");
                if (generalPath != null)
                {
                    string json = File.ReadAllText(generalPath);
                    var list = JsonSerializer.Deserialize<List<GeneralSetting>>(json) ?? new List<GeneralSetting>();
                    Generals.Clear();
                    foreach (var g in list)
                    {
                        Generals[g.Id] = g.Name;
                    }
                }

                // 7. 加载部队/兵种设定 (合并 ArmySettings.json 与 ExArmySettings.json)
                Units.Clear();
                string armyPath = FindFile("ArmySettings.json");
                if (armyPath != null)
                {
                    string json = File.ReadAllText(armyPath);
                    var list = JsonSerializer.Deserialize<List<ArmySetting>>(json) ?? new List<ArmySetting>();
                    foreach (var a in list)
                    {
                        Units[a.Id] = a.Name;
                    }
                }

                string exArmyPath = FindFile("ExArmySettings.json");
                if (exArmyPath != null)
                {
                    string json = File.ReadAllText(exArmyPath);
                    var list = JsonSerializer.Deserialize<List<ArmySetting>>(json) ?? new List<ArmySetting>();
                    foreach (var a in list)
                    {
                        Units[a.Id] = a.Name; // 覆盖或并入精英部队
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Settings Loader] 外部配置文件加载出错: {ex.Message}");
            }
        }

        public static string ExternalDataDir { get; set; }

        public static void SetExternalDataDir(string path)
        {
            ExternalDataDir = path;
        }

        /// <summary>在 GameData 及常见子目录里找文件。找不到返回 null。</summary>
        public static string FindFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            string root = GetGameDataRoot();
            foreach (var sub in GameDataSubdirs)
            {
                string path = string.IsNullOrEmpty(sub)
                    ? Path.Combine(root, filename)
                    : Path.Combine(root, sub, filename);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        public static string FindDirectory(string relative)
        {
            if (string.IsNullOrEmpty(relative))
                return null;

            string path = Path.Combine(GetGameDataRoot(), relative);
            if (Directory.Exists(path))
                return Path.GetFullPath(path);
            return null;
        }

        public static string GetTerrainName(int terrainId)
        {
            if (MapTerrains.TryGetValue(terrainId, out var terrain))
                return terrain.Name;
            
            // 如果在 def_mapterrain 中找不到，退化到基础地形类型
            if (TerrainTypes.TryGetValue(terrainId, out var name))
                return name;

            return $"未知地形({terrainId})";
        }

        /// <summary>兵种 ID → 中文名；没有配置时返回「未知兵种(id)」。</summary>
        public static string GetUnitName(int unitId)
        {
            if (Units.TryGetValue(unitId, out var name))
                return name;
            return $"未知兵种({unitId})";
        }

        public static string GetBuildingName(int buildingId)
        {
            if (Buildings.TryGetValue(buildingId, out var b))
                return b.Name;
            return $"未知建筑({buildingId})";
        }

        public static string GetFortName(int fortId)
        {
            // 原始触发器里工事 ID 为 1~5，需要映射到 Feature 或 id 寻找
            int mappedFeature = fortId;
            if (fortId == 1) mappedFeature = 56; // 雷达
            else if (fortId == 2) mappedFeature = 57; // 战壕
            else if (fortId == 3) mappedFeature = 14; // 地堡
            else if (fortId == 4) mappedFeature = 15; // 要塞炮
            else if (fortId == 5) mappedFeature = 16; // 海岸炮

            if (Fortifications.TryGetValue(mappedFeature, out var f))
                return f.Name;

            return $"未知工事({fortId})";
        }

        public static string GetCountryName(int countryId)
        {
            if (Countries.TryGetValue(countryId, out var name))
                return name;
            return $"国家({countryId})";
        }

        public static string GetGeneralName(int generalId)
        {
            if (generalId == 0) return "无将领";
            if (Generals.TryGetValue(generalId, out var name))
                return name;
            return $"将领({generalId})";
        }
    }
}
