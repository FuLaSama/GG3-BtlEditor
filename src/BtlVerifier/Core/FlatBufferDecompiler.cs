/*
 * FlatBufferDecompiler.cs
 * 
 * 本文件实现了一个只读的、专为 BtlVerifier 定制的 FlatBuffers 二进制反 decompiler：
 * - 物理结构健壮性验证 (ValidateTableStructure): 强力检查 Table 指针越界、VTable 相对偏移、VTable 字节对齐（2 字节对齐）以及合理的物理尺寸范围，确保不会因二进制格式被外部工具破坏而引发内存读取异常。
 * - 字典树转换: 将底层的二进制 BTL 字节序列，通过嵌套的解析逻辑解析成标准的属性字典树，并在解析过程中记录并公开关键向量属性（如 Tiles 和 Attributes）在物理文件中的绝对起始偏移量，供后续 Hex 查看器实现位置映射。
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace BtlVerifier
{
    // C# 原生 FlatBuffers 二进制反编译器 (只读版本，增加了物理文件地址偏移跟踪)
    public class FlatBufferDecompiler
    {
        private static readonly HashSet<string> CoreTables = new HashSet<string>
        {
            "Root",
            "MapTerrain",
            "StageMetadata",
            "BattleInfo",
            "FactionInfo",
            "TriggerInfo",
            "AIInfo",
            "RegionInfo",
            "StageConfig",
            "DecalInfo",
            "ViewBoundary",
            "AIAgent",
            "AIAgentBehavior",
            "AIAgentTable8",
            "AIAgentTable10",
            "AIAgentTable11",
            "AIAgentTable12",
            "Faction",
            "FactionLimits",
            "FactionCards",
            "TriggerEvent",
            "TriggerEventBldg",
            "TriggerEventFort",
            "ReinforcePoint",
            "Table8Entry",
            "FactionCardsRelation",
            "FactionLimitsRelation"
        };

        private readonly byte[] _data;
        private readonly Dictionary<int, object> _visitedTables = new Dictionary<int, object>();
        private readonly Stack<string> _parsePath = new Stack<string>();

        // 公开物理向量文件的起始偏移量，方便外部高亮追踪
        public int TilesOffset { get; private set; }
        public int AttributesOffset { get; private set; }
        public int ReinforcePointsOffset { get; private set; }

        public FlatBufferDecompiler(byte[] data)
        {
            _data = data;
        }

        public Dictionary<string, object> Decompile()
        {
            if (_data == null || _data.Length < 4)
                return null;

            int rootOffset = BitConverter.ToInt32(_data, 0);
            try
            {
                _parsePath.Clear();
                _parsePath.Push($"Root Table (Offset: 0x{rootOffset:X})");
                return ParseTable(rootOffset, "Root");
            }
            catch (Exception ex) when (!(ex is BtlDecompileException))
            {
                var pathArray = _parsePath.ToArray();
                Array.Reverse(pathArray);
                string pathTrace = string.Join("\n", pathArray);
                throw new BtlDecompileException(ex.Message, pathTrace, ex);
            }
        }

        private int GetFieldOffset(int tablePos, int vtableOffset)
        {
            if (tablePos < 0 || tablePos + 4 > _data.Length)
                return 0;

            int vtableRel = BitConverter.ToInt32(_data, tablePos);
            int vtablePos = tablePos - vtableRel;
            if (vtablePos < 0 || vtablePos + 2 > _data.Length)
                return 0;

            ushort vtableSize = BitConverter.ToUInt16(_data, vtablePos);
            if (vtableOffset >= vtableSize || vtablePos + vtableOffset + 2 > _data.Length)
                return 0;

            return BitConverter.ToUInt16(_data, vtablePos + vtableOffset);
        }

        private int GetFieldPointer(int tablePos, int vtableOffset)
        {
            int off = GetFieldOffset(tablePos, vtableOffset);
            if (off == 0)
                return 0;

            int fieldPos = tablePos + off;
            if (fieldPos < 0 || fieldPos + 4 > _data.Length)
                return 0;

            int val32 = BitConverter.ToInt32(_data, fieldPos);
            if (val32 == 0)
                return 0;
            return fieldPos + val32;
        }

        private Dictionary<string, object> ParseTable(int pos, string tableType)
        {
            if (pos <= 0 || pos >= _data.Length)
                return null;

            if (CoreTables.Contains(tableType))
            {
                if (!ValidateTableStructure(pos, tableType, out string reason))
                {
                    throw new Exception($"FlatBuffers structure corruption: core table '{tableType}' at 0x{pos:X} failed validation. Reason: {reason}");
                }
            }

            if (_visitedTables.ContainsKey(pos))
            {
                return new Dictionary<string, object> { { "_ref", $"0x{pos:X}" } };
            }

            var res = new Dictionary<string, object>();
            _visitedTables[pos] = res;

            int vtableRel = BitConverter.ToInt32(_data, pos);
            if (vtableRel == 0)
            {
                res["_type"] = tableType;
                res["_pos"] = $"0x{pos:X}";
                return res;
            }
            int vtablePos = pos - vtableRel;
            if (vtablePos < 0 || vtablePos + 4 > _data.Length)
                return res;

            ushort vtableSize = BitConverter.ToUInt16(_data, vtablePos);
            int numFields = (vtableSize - 4) / 2;

            res["_type"] = tableType;
            res["_pos"] = $"0x{pos:X}";

            var schema = BtlSchema.GetSchema(tableType);

            for (int i = 0; i < numFields; i++)
            {
                int vtableFieldOffset = 4 + i * 2;
                int off = GetFieldOffset(pos, vtableFieldOffset);
                if (off == 0)
                    continue;

                int absPos = pos + off;
                string fieldName = $"field_{i}";
                string fieldType = "unknown";

                if (schema.ContainsKey(i))
                {
                    fieldName = schema[i].Name;
                    fieldType = schema[i].Type;
                }

                // 物理向量地址追踪
                if (tableType == "MapTerrain")
                {
                    if (i == 2) TilesOffset = GetFieldPointer(pos, vtableFieldOffset);       // tiles 偏移
                    if (i == 3) AttributesOffset = GetFieldPointer(pos, vtableFieldOffset);  // attributes 偏移
                }
                if (tableType == "BattleInfo")
                {
                    if (i == 1) ReinforcePointsOffset = GetFieldPointer(pos, vtableFieldOffset);       // reinforce_points 偏移
                }

                if (BtlSchema.IsTableType(fieldType))
                {
                    int targetPos = GetFieldPointer(pos, vtableFieldOffset);
                    _parsePath.Push($"-> Field '{fieldName}' (Table {fieldType}, Offset: 0x{targetPos:X})");
                    res[fieldName] = ParseTable(targetPos, fieldType);
                    _parsePath.Pop();
                }
                else if (BtlSchema.IsStructType(fieldType))
                {
                    _parsePath.Push($"-> Field '{fieldName}' (Struct {fieldType}, Offset: 0x{absPos:X})");
                    res[fieldName] = ParseStruct(absPos, fieldType);
                    _parsePath.Pop();
                }
                else if (fieldType.StartsWith("vector_"))
                {
                    string elemType = fieldType.Substring(7);
                    int targetPos = GetFieldPointer(pos, vtableFieldOffset);
                    _parsePath.Push($"-> Field '{fieldName}' (Vector of {elemType}, Offset: 0x{targetPos:X})");
                    res[fieldName] = ParseVector(targetPos, elemType);
                    _parsePath.Pop();
                }
                else if (fieldType == "uint16")
                {
                    if (absPos + 2 <= _data.Length)
                        res[fieldName] = BitConverter.ToUInt16(_data, absPos);
                }
                else if (fieldType == "int16")
                {
                    if (absPos + 2 <= _data.Length)
                        res[fieldName] = BitConverter.ToInt16(_data, absPos);
                }
                else if (fieldType == "uint32")
                {
                    if (absPos + 4 <= _data.Length)
                        res[fieldName] = BitConverter.ToUInt32(_data, absPos);
                }
                else if (fieldType == "int32")
                {
                    if (absPos + 4 <= _data.Length)
                        res[fieldName] = BitConverter.ToInt32(_data, absPos);
                }
                else if (fieldType == "uint8")
                {
                    if (absPos < _data.Length)
                        res[fieldName] = _data[absPos];
                }
                else if (fieldType == "int8")
                {
                    if (absPos < _data.Length)
                        res[fieldName] = (sbyte)_data[absPos];
                }
                else if (fieldType == "bool")
                {
                    if (absPos < _data.Length)
                        res[fieldName] = _data[absPos] != 0;
                }
                else if (fieldType == "float")
                {
                    if (absPos + 4 <= _data.Length)
                        res[fieldName] = BitConverter.ToSingle(_data, absPos);
                }
                else if (fieldType == "double")
                {
                    if (absPos + 8 <= _data.Length)
                        res[fieldName] = BitConverter.ToDouble(_data, absPos);
                }
                else if (fieldType == "string")
                {
                    int targetPos = GetFieldPointer(pos, vtableFieldOffset);
                    res[fieldName] = ReadString(targetPos);
                }
                else // 备用回退逻辑
                {
                    if (absPos + 4 <= _data.Length)
                    {
                        int val32 = BitConverter.ToInt32(_data, absPos);
                        int targetPos = absPos + val32;

                        if (IsValidTable(targetPos))
                        {
                            _parsePath.Push($"-> Field '{fieldName}' (Table Table_Field_{i}, Offset: 0x{targetPos:X})");
                            res[fieldName] = ParseTable(targetPos, $"Table_Field_{i}");
                            _parsePath.Pop();
                        }
                        else if (IsValidString(targetPos))
                        {
                            res[fieldName] = ReadString(targetPos);
                        }
                        else
                        {
                            res[fieldName] = val32;
                        }
                    }
                    else if (absPos < _data.Length)
                    {
                        res[fieldName] = _data[absPos];
                    }
                }
            }

            return res;
        }

        private Dictionary<string, object> ParseStruct(int absPos, string structType)
        {
            var schema = BtlSchema.GetSchema(structType);
            if (schema == null || schema.Count == 0)
                return null;

            var res = new Dictionary<string, object>();
            int currentOffset = 0;

            var sortedIds = new List<int>(schema.Keys);
            sortedIds.Sort();

            foreach (int fid in sortedIds)
            {
                var field = schema[fid];
                string fieldName = field.Name;
                string fieldType = field.Type;

                int size = GetTypeSize(fieldType);
                currentOffset = ((currentOffset + size - 1) / size) * size;

                int fieldAbsPos = absPos + currentOffset;
                if (fieldAbsPos + size > _data.Length)
                    return res;

                object val = ReadScalar(fieldAbsPos, fieldType);
                    
                if (structType == "AgentInfo" && fieldName == "stack_count" && val is ushort)
                {
                    val = (ushort)((ushort)val >> 8);
                }

                res[fieldName] = val;
                currentOffset += size;
            }

            return res;
        }

        private object ParseVector(int pos, string elementType)
        {
            if (pos <= 0)
                return new List<object>();

            if (pos + 4 > _data.Length)
            {
                throw new Exception($"FlatBuffers structure corruption: vector of '{elementType}' pointer 0x{pos:X} length header exceeds file boundary.");
            }

            int count = BitConverter.ToInt32(_data, pos);
            if (count < 0 || count > 30000)
            {
                throw new Exception($"FlatBuffers structure corruption: vector of '{elementType}' at 0x{pos:X} has invalid length {count} (outside valid range [0, 30000]).");
            }

            int elemPos = pos + 4;
            int elemSize = GetElemSize(elementType);
            if (elemPos + count * elemSize > _data.Length)
            {
                throw new Exception($"FlatBuffers structure corruption: vector of '{elementType}' data at 0x{elemPos:X} (count={count}, element size={elemSize} bytes) exceeds file boundary.");
            }

            if (elementType == "uint8")
            {
                var list = new List<byte>();
                if (elemPos + count <= _data.Length)
                {
                    for (int i = 0; i < count; i++)
                        list.Add(_data[elemPos + i]);
                }
                return list;
            }
            else if (elementType == "uint16")
            {
                var list = new List<ushort>();
                if (elemPos + count * 2 <= _data.Length)
                {
                    for (int i = 0; i < count; i++)
                        list.Add(BitConverter.ToUInt16(_data, elemPos + i * 2));
                }
                return list;
            }
            else if (elementType == "int16")
            {
                var list = new List<short>();
                if (elemPos + count * 2 <= _data.Length)
                {
                    for (int i = 0; i < count; i++)
                        list.Add(BitConverter.ToInt16(_data, elemPos + i * 2));
                }
                return list;
            }
            else if (elementType == "uint32")
            {
                var list = new List<uint>();
                if (elemPos + count * 4 <= _data.Length)
                {
                    for (int i = 0; i < count; i++)
                        list.Add(BitConverter.ToUInt32(_data, elemPos + i * 4));
                }
                return list;
            }
            else if (elementType == "int32")
            {
                var list = new List<int>();
                if (elemPos + count * 4 <= _data.Length)
                {
                    for (int i = 0; i < count; i++)
                        list.Add(BitConverter.ToInt32(_data, elemPos + i * 4));
                }
                return list;
            }
            else if (elementType == "attr" || elementType == "TileAttr")
            {
                var list = new List<List<byte>>();
                if (elemPos + count * 4 <= _data.Length)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var attr = new List<byte>
                        {
                            _data[elemPos + i * 4],
                            _data[elemPos + i * 4 + 1],
                            _data[elemPos + i * 4 + 2],
                            _data[elemPos + i * 4 + 3]
                        };
                        list.Add(attr);
                    }
                }
                return list;
            }
            else // 表向量 (即 vector of tables)
            {
                var list = new List<object>();
                if (elemPos + count * 4 <= _data.Length)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int relOffset = BitConverter.ToInt32(_data, elemPos + i * 4);
                        int targetPos = elemPos + i * 4 + relOffset;

                        _parsePath.Push($"  -> [{i}] (Table {elementType}, Offset: 0x{targetPos:X})");
                        var tbl = ParseTable(targetPos, elementType);
                        _parsePath.Pop();

                        if (tbl != null)
                        {
                            // 附加物理表绝对地址偏移信息以供查找
                            tbl["_pos"] = $"0x{targetPos:X}";
                        }
                        list.Add(tbl);
                    }
                }
                return list;
            }
        }

        private bool ValidateTableStructure(int pos, string tableType, out string reason)
        {
            reason = "";
            if (pos < 4 || pos + 4 > _data.Length)
            {
                reason = $"Table pointer 0x{pos:X} is out of file boundary [0, 0x{_data.Length:X}].";
                return false;
            }

            int vtableRel = BitConverter.ToInt32(_data, pos);
            if (vtableRel == 0)
            {
                return true; // self-referencing empty table
            }
            int vtablePos = pos - vtableRel;
            if (vtablePos < 0 || vtablePos + 4 > _data.Length)
            {
                reason = $"Vtable position 0x{vtablePos:X} (computed from table pointer 0x{pos:X} minus relation offset {vtableRel}) is out of file boundary.";
                return false;
            }

            ushort vtableSize = BitConverter.ToUInt16(_data, vtablePos);
            ushort tableSize = BitConverter.ToUInt16(_data, vtablePos + 2);

            if (vtableSize % 2 != 0)
            {
                reason = $"Vtable size ({vtableSize} bytes) at 0x{vtablePos:X} is not 2-byte aligned.";
                return false;
            }
            if (tableSize % 2 != 0)
            {
                reason = $"Table inline size ({tableSize} bytes) at 0x{vtablePos+2:X} is not 2-byte aligned.";
                return false;
            }

            if (vtableSize < 4 || vtableSize > 100)
            {
                reason = $"Vtable size ({vtableSize} bytes) is outside valid range [4, 100].";
                return false;
            }
            if (tableSize < 4 || tableSize > 1000)
            {
                reason = $"Table inline size ({tableSize} bytes) is outside valid range [4, 1000].";
                return false;
            }

            if (vtablePos + vtableSize > _data.Length)
            {
                reason = $"Vtable end position 0x{vtablePos + vtableSize:X} exceeds file boundary.";
                return false;
            }

            return true;
        }

        private bool IsValidTable(int pos)
        {
            return ValidateTableStructure(pos, "", out _);
        }

        private bool IsValidString(int pos)
        {
            if (pos < 0 || pos + 4 > _data.Length)
                return false;

            int length = BitConverter.ToInt32(_data, pos);
            if (length <= 0 || length > 10000)
                return false;

            if (pos + 4 + length + 1 > _data.Length)
                return false;

            return _data[pos + 4 + length] == 0;
        }

        private string ReadString(int pos)
        {
            if (pos <= 0)
                return "";

            if (pos + 4 > _data.Length)
            {
                throw new Exception($"FlatBuffers structure corruption: string pointer 0x{pos:X} length header exceeds file boundary.");
            }

            int length = BitConverter.ToInt32(_data, pos);
            if (length < 0 || length > 10000)
            {
                throw new Exception($"FlatBuffers structure corruption: string at 0x{pos:X} has invalid length {length} (outside valid range [0, 10000]).");
            }

            if (pos + 4 + length > _data.Length)
            {
                throw new Exception($"FlatBuffers structure corruption: string data at 0x{pos+4:X} (length={length} bytes) exceeds file boundary.");
            }

            return Encoding.UTF8.GetString(_data, pos + 4, length);
        }

        private int GetElemSize(string type)
        {
            switch (type)
            {
                case "uint8":
                case "int8":
                case "bool":
                case "ubyte":
                case "byte":
                    return 1;
                case "uint16":
                case "int16":
                case "ushort":
                case "short":
                    return 2;
                case "uint32":
                case "int32":
                case "uint":
                case "int":
                case "float":
                    return 4;
                case "ulong":
                case "long":
                case "double":
                    return 8;
                case "attr":
                case "TileAttr":
                    return 4;
                default:
                    return 4; // vectors of tables/strings store 32-bit offsets
            }
        }

        private int GetTypeSize(string type)
        {
            if (type == "uint8" || type == "int8" || type == "bool" || type == "ubyte" || type == "byte")
                return 1;
            if (type == "uint16" || type == "int16" || type == "ushort" || type == "short")
                return 2;
            if (type == "uint32" || type == "int32" || type == "uint" || type == "int" || type == "float")
                return 4;
            if (type == "uint64" || type == "int64" || type == "double")
                return 8;
            return 1;
        }

        private object ReadScalar(int pos, string type)
        {
            if (pos < 0 || pos >= _data.Length)
                return null;

            if (type == "uint8" || type == "ubyte")
                return _data[pos];
            if (type == "int8" || type == "byte")
                return (sbyte)_data[pos];
            if (type == "bool")
                return _data[pos] != 0;

            if (type == "uint16" || type == "ushort")
            {
                if (pos + 2 <= _data.Length)
                    return BitConverter.ToUInt16(_data, pos);
            }
            if (type == "int16" || type == "short")
            {
                if (pos + 2 <= _data.Length)
                    return BitConverter.ToInt16(_data, pos);
            }

            if (type == "uint32" || type == "uint")
            {
                if (pos + 4 <= _data.Length)
                    return BitConverter.ToUInt32(_data, pos);
            }
            if (type == "int32" || type == "int")
            {
                if (pos + 4 <= _data.Length)
                    return BitConverter.ToInt32(_data, pos);
            }
            if (type == "float")
            {
                if (pos + 4 <= _data.Length)
                    return BitConverter.ToSingle(_data, pos);
            }

            if (type == "uint64")
            {
                if (pos + 8 <= _data.Length)
                    return BitConverter.ToUInt64(_data, pos);
            }
            if (type == "int64")
            {
                if (pos + 8 <= _data.Length)
                    return BitConverter.ToInt64(_data, pos);
            }
            if (type == "double")
            {
                if (pos + 8 <= _data.Length)
                    return BitConverter.ToDouble(_data, pos);
            }

            return null;
        }
    }

    public class BtlDecompileException : Exception
    {
        public string BtlPathTrace { get; }

        public BtlDecompileException(string message, string btlPathTrace, Exception innerException)
            : base(message, innerException)
        {
            BtlPathTrace = btlPathTrace;
        }
    }
}
