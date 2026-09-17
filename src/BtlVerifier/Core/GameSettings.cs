/*
 * GameSettings.cs
 * 
 * 本文件负责加载和管理游戏内的静态配置定义（数据库）：
 * - 反序列化载入 `BuildingSettings.json`、`FortificationSettings.json`、`ArmySettings.json`、`GeneralSettings.json` 和 `CountrySettings.json` 等配置文件。
 * - 提供了针对建筑类型 ID、军事工事特征属性（如战壕、雷达、地堡）的查询映射，主要供 BtlVerifier 对读取出的事件或属性字段进行游戏实体名翻译 and 合法性验证。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BtlVerifier
{
    public class BuildingSetting
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("Type")]
        public int Type { get; set; }

        [JsonPropertyName("Level")]
        public int Level { get; set; }

        [JsonPropertyName("ProduceResource")]
        public int ProduceResource { get; set; }

        [JsonPropertyName("ProduceTech")]
        public int ProduceTech { get; set; }

        [JsonPropertyName("AddDefense")]
        public int AddDefense { get; set; }

        [JsonPropertyName("Moralechange")]
        public int Moralechange { get; set; }

        [JsonPropertyName("MobilityCost")]
        public int MobilityCost { get; set; }
    }

    public class FortificationSetting
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("Feature")]
        public int Feature { get; set; }

        [JsonPropertyName("AddDefense")]
        public int AddDefense { get; set; }
    }

    public class SimpleSetting
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }
    }

    public static class GameSettings
    {
        public static List<BuildingSetting> Buildings { get; private set; } = new List<BuildingSetting>();
        public static List<FortificationSetting> Fortifications { get; private set; } = new List<FortificationSetting>();
        public static HashSet<int> ValidUnitIds { get; } = new HashSet<int>();
        public static HashSet<int> ValidGeneralIds { get; } = new HashSet<int>();
        public static HashSet<int> ValidCountryIds { get; } = new HashSet<int>();

        private static readonly Dictionary<int, BuildingSetting> BuildingMap = new Dictionary<int, BuildingSetting>();
        private static readonly Dictionary<int, FortificationSetting> FortificationMap = new Dictionary<int, FortificationSetting>();

        public static void LoadDatabases()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] buildingPaths = new string[]
                {
                    Path.Combine(baseDir, "BuildingSettings.json"),
                    Path.Combine(BtlSchema.WorkspacePath, "一些游戏内的定义文件", "BuildingSettings.json"),
                    Path.Combine(BtlSchema.WorkspacePath, "游戏包体", "resources", "assets", "json", "BuildingSettings.json"),
                };

                string[] fortificationPaths = new string[]
                {
                    Path.Combine(baseDir, "FortificationSettings.json"),
                    Path.Combine(BtlSchema.WorkspacePath, "一些游戏内的定义文件", "FortificationSettings.json"),
                    Path.Combine(BtlSchema.WorkspacePath, "游戏包体", "resources", "assets", "json", "FortificationSettings.json"),
                };

                string[] armyPaths = new string[]
                {
                    Path.Combine(baseDir, "ArmySettings.json"),
                    Path.Combine(BtlSchema.WorkspacePath, "一些游戏内的定义文件", "ArmySettings.json"),
                    Path.Combine(BtlSchema.WorkspacePath, "游戏包体", "resources", "assets", "json", "ArmySettings.json"),
                };

                string[] generalPaths = new string[]
                {
                    Path.Combine(baseDir, "GeneralSettings.json"),
                    Path.Combine(BtlSchema.WorkspacePath, "一些游戏内的定义文件", "GeneralSettings.json"),
                    Path.Combine(BtlSchema.WorkspacePath, "游戏包体", "resources", "assets", "json", "GeneralSettings.json"),
                };

                string[] countryPaths = new string[]
                {
                    Path.Combine(baseDir, "CountrySettings.json"),
                    Path.Combine(BtlSchema.WorkspacePath, "一些游戏内的定义文件", "CountrySettings.json"),
                    Path.Combine(BtlSchema.WorkspacePath, "游戏包体", "resources", "assets", "json", "CountrySettings.json"),
                };

                string bPath = FindFile(buildingPaths);
                if (bPath != null)
                {
                    string json = File.ReadAllText(bPath);
                    Buildings = JsonSerializer.Deserialize<List<BuildingSetting>>(json) ?? new List<BuildingSetting>();
                    BuildingMap.Clear();
                    foreach (var b in Buildings)
                    {
                        BuildingMap[b.Id] = b;
                    }
                }

                string fPath = FindFile(fortificationPaths);
                if (fPath != null)
                {
                    string json = File.ReadAllText(fPath);
                    Fortifications = JsonSerializer.Deserialize<List<FortificationSetting>>(json) ?? new List<FortificationSetting>();
                    FortificationMap.Clear();
                    foreach (var f in Fortifications)
                    {
                        FortificationMap[f.Feature] = f;
                    }
                }

                string aPath = FindFile(armyPaths);
                if (aPath != null)
                {
                    string json = File.ReadAllText(aPath);
                    var list = JsonSerializer.Deserialize<List<SimpleSetting>>(json);
                    ValidUnitIds.Clear();
                    if (list != null)
                    {
                        foreach (var item in list) ValidUnitIds.Add(item.Id);
                    }
                }

                string gPath = FindFile(generalPaths);
                if (gPath != null)
                {
                    string json = File.ReadAllText(gPath);
                    var list = JsonSerializer.Deserialize<List<SimpleSetting>>(json);
                    ValidGeneralIds.Clear();
                    if (list != null)
                    {
                        foreach (var item in list) ValidGeneralIds.Add(item.Id);
                    }
                }

                string cPath = FindFile(countryPaths);
                if (cPath != null)
                {
                    string json = File.ReadAllText(cPath);
                    var list = JsonSerializer.Deserialize<List<SimpleSetting>>(json);
                    ValidCountryIds.Clear();
                    if (list != null)
                    {
                        foreach (var item in list) ValidCountryIds.Add(item.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                // Silence exceptions in designer or startup but log them to Console
                Console.WriteLine("加载游戏设置数据库失败: " + ex.Message);
            }
        }

        private static string FindFile(string[] paths)
        {
            foreach (var path in paths)
            {
                bool exists = File.Exists(path);
                Console.WriteLine($"[FindFile] Checking: {path} -> Exists: {exists}");
                if (exists)
                    return path;
            }
            return null;
        }

        public static BuildingSetting GetBuilding(int bType)
        {
            // 直接通过 BuildingSettings.json 中的 Id 查找（101~116）
            // 注意：terrain ID（如 47河流、12沼泽、35矮山、53-56装饰）
            // 和 BuildingSettings ID 是完全不同的命名空间，不能混淆映射。
            if (BuildingMap.TryGetValue(bType, out var b))
                return b;

            return null;
        }

        public static FortificationSetting GetFortification(int fType)
        {
            // 如果是触发器中提取的原始 ID (1..5)，转换为 ID 映射区间 (201..205)
            if (fType >= 1 && fType <= 5)
            {
                fType = 200 + fType;
            }

            // fType 对应 FortificationSettings.json 里的 Feature 属性
            if (FortificationMap.TryGetValue(fType, out var f))
                return f;

            // 特殊情况：地堡有些地方使用 ID 或 Feature 映射
            if (fType >= 201 && fType <= 205)
            {
                int feature = 0;
                if (fType == 201) feature = 56; // 雷达
                else if (fType == 202) feature = 57; // 战壕
                else if (fType == 203) feature = 14; // 地堡
                else if (fType == 204) feature = 15; // 要塞炮
                else if (fType == 205) feature = 16; // 海岸炮

                if (feature != 0 && FortificationMap.TryGetValue(feature, out var mappedFort))
                    return mappedFort;
            }

            return null;
        }
    }
}
