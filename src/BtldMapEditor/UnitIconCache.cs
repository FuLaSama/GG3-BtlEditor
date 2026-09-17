/*
 * UnitIconCache.cs
 *
 * 画布上的部队兵牌。文件名约定 icon_army_{side}_{unitId}.png：
 *   side 0 灰中立 / 1 蓝友军 / 2 红敌军
 * 玩家目前也用蓝（Units/Player），以后可换成绿。
 * 原图朝右；AgentInfo.val5 低字节 0 朝左（镜像绘制），1 朝右。
 * 找不到图时返回 null，画布改画色块+兵种名。
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;

namespace BtldMapEditor
{
    public enum UnitIconRole
    {
        Neutral,
        Player,
        Ally,
        Enemy
    }

    public static class UnitIconCache
    {
        static readonly Regex NameRegex = new Regex(
            @"icon_army_(?<side>player|[012])_(?<id>\d+|unknow)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex LooseIdRegex = new Regex(
            @"(?<id>\d{3,})",
            RegexOptions.Compiled);

        static readonly object Gate = new object();
        static bool _loaded;
        static readonly Dictionary<(int side, int unitId), Image> _bySideAndId = new Dictionary<(int, int), Image>();
        static readonly Dictionary<int, Image> _playerById = new Dictionary<int, Image>();
        static readonly Dictionary<int, Image> _byIdAny = new Dictionary<int, Image>();
        static Image _unknown;

        public static Image Get(int unitId, UnitIconRole role)
        {
            EnsureLoaded();
            Image img;
            if (role == UnitIconRole.Player)
            {
                if (_playerById.TryGetValue(unitId, out img))
                    return img;
                if (_bySideAndId.TryGetValue((1, unitId), out img))
                    return img;
            }
            else
            {
                int side = role == UnitIconRole.Enemy ? 2 : (role == UnitIconRole.Ally ? 1 : 0);
                if (_bySideAndId.TryGetValue((side, unitId), out img))
                    return img;
            }

            if (_byIdAny.TryGetValue(unitId, out img))
                return img;
            if (_bySideAndId.TryGetValue((0, unitId), out img))
                return img;
            if (_bySideAndId.TryGetValue((1, unitId), out img))
                return img;
            if (_bySideAndId.TryGetValue((2, unitId), out img))
                return img;
            return _unknown;
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (Gate)
            {
                if (_loaded) return;

                string unitsDir = GameSettings.FindDirectory("Units");
                if (!string.IsNullOrEmpty(unitsDir))
                {
                    LoadDirectory(unitsDir, playerSet: false);
                    string playerDir = Path.Combine(unitsDir, "Player");
                    if (Directory.Exists(playerDir))
                        LoadDirectory(playerDir, playerSet: true);
                }

                string editorIcons = GameSettings.FindDirectory(Path.Combine("EditorPalette", "UnitIcons"));
                if (!string.IsNullOrEmpty(editorIcons))
                    LoadDirectory(editorIcons, playerSet: false);

                string buildings = GameSettings.FindDirectory("Buildings");
                if (!string.IsNullOrEmpty(buildings))
                    LoadBattleresAtlas(buildings);

                _bySideAndId.TryGetValue((0, -1), out _unknown);
                if (_unknown == null)
                    _bySideAndId.TryGetValue((2, -1), out _unknown);
                _loaded = true;
            }
        }

        static void LoadDirectory(string dir, bool playerSet)
        {
            foreach (var file in Directory.GetFiles(dir, "*.png"))
                RegisterFile(Path.GetFileNameWithoutExtension(file), () => LoadPng(file), playerSet);
        }

        static void LoadBattleresAtlas(string folder)
        {
            string xml = Path.Combine(folder, "battleres.xml");
            if (!File.Exists(xml)) return;
            try
            {
                var atlas = new TerrainAtlas(folder);
                atlas.LoadXml(xml);
                foreach (var name in atlas.Slices.Keys)
                {
                    if (!NameRegex.IsMatch(name)) continue;
                    RegisterFile(Path.GetFileNameWithoutExtension(name), () =>
                    {
                        if (atlas.TryGet(name, out var crop, out _) && crop != null)
                            return crop.ToBitmap();
                        return null;
                    }, playerSet: false);
                }
            }
            catch
            {
            }
        }

        static void RegisterFile(string stem, Func<Image> loader, bool playerSet)
        {
            var m = NameRegex.Match(stem);
            int side = -1;
            int unitId = -1;
            bool namedPlayer = false;
            if (m.Success)
            {
                string sideText = m.Groups["side"].Value;
                if (sideText.Equals("player", StringComparison.OrdinalIgnoreCase))
                    namedPlayer = true;
                else
                    side = int.Parse(sideText);
                string idText = m.Groups["id"].Value;
                unitId = idText.Equals("unknow", StringComparison.OrdinalIgnoreCase) ? -1 : int.Parse(idText);
            }
            else
            {
                var loose = LooseIdRegex.Matches(stem);
                if (loose.Count == 0) return;
                unitId = int.Parse(loose[loose.Count - 1].Value);
            }

            bool toPlayer = playerSet || namedPlayer;
            if (toPlayer)
            {
                if (unitId >= 0 && _playerById.ContainsKey(unitId))
                    return;
            }
            else if (side >= 0 && _bySideAndId.ContainsKey((side, unitId)))
            {
                return;
            }
            else if (side < 0 && unitId >= 0 && _byIdAny.ContainsKey(unitId))
            {
                return;
            }

            Image img = loader();
            if (img == null) return;

            if (toPlayer)
            {
                if (unitId >= 0)
                    _playerById[unitId] = img;
                return;
            }

            if (side >= 0)
                _bySideAndId[(side, unitId)] = img;
            if (unitId >= 0 && !_byIdAny.ContainsKey(unitId))
                _byIdAny[unitId] = img;
            if (unitId < 0 && _unknown == null)
                _unknown = img;
        }

        static Image LoadPng(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var tmp = Image.FromStream(fs, false, false))
            {
                return new Bitmap(tmp);
            }
        }
    }
}
