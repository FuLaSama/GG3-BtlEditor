using System.Buffers.Binary;
using System.Globalization;
using BtlCore.Front;
using MoonSharp.Interpreter;

namespace BtlCore.Scripting;

public sealed partial class ScriptHost
{
    void InstallBinaryApi(Table editor)
    {
        var binary = new Table(_script);
        binary.Set("create", Api("editor.binary.create", (_, args) =>
        {
            if (args.Count != 1) throw new FrontEditException("需要字节数");
            int count = AsIndex(args[0]);
            if (count < 0 || count > MaxPayloadBytes) throw new FrontEditException("字节数超出预算");
            var bytes = new Table(_script);
            for (int i = 1; i <= count; i++) bytes.Set(i, DynValue.NewNumber(0));
            return DynValue.NewTable(bytes);
        }));
        binary.Set("read", Api("editor.binary.read", (_, args) =>
        {
            var (table, offset, type, size, big) = BinaryArguments(args, write: false);
            Span<byte> bytes = stackalloc byte[size];
            for (int i = 0; i < size; i++) bytes[i] = ByteValue(table.Get(offset + i + 1));
            if (big) bytes.Reverse();
            if (type == "u64") return DynValue.NewString(BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString(CultureInfo.InvariantCulture));
            if (type == "i64") return DynValue.NewString(BinaryPrimitives.ReadInt64LittleEndian(bytes).ToString(CultureInfo.InvariantCulture));
            double value = type switch
            {
                "u8" => bytes[0], "i8" => (sbyte)bytes[0],
                "u16" => BinaryPrimitives.ReadUInt16LittleEndian(bytes), "i16" => BinaryPrimitives.ReadInt16LittleEndian(bytes),
                "u32" => BinaryPrimitives.ReadUInt32LittleEndian(bytes), "i32" => BinaryPrimitives.ReadInt32LittleEndian(bytes),
                "f32" => BinaryPrimitives.ReadSingleLittleEndian(bytes), "f64" => BinaryPrimitives.ReadDoubleLittleEndian(bytes),
                _ => throw new FrontEditException("未知二进制类型")
            };
            if (!double.IsFinite(value)) throw new FrontEditException("浮点内容不是有限数值；保留原字节处理");
            return DynValue.NewNumber(value);
        }));
        binary.Set("write", Api("editor.binary.write", (_, args) =>
        {
            var (table, offset, type, size, big) = BinaryArguments(args, write: true);
            var input = args[3];
            if (type is "u64" or "i64")
            {
                if (input.Type != DataType.String) throw new FrontEditException("64 位整数请使用精确十进制字符串");
            }
            else if (input.Type != DataType.Number || !double.IsFinite(input.Number)
                || type is not ("f32" or "f64") && input.Number != Math.Truncate(input.Number))
                throw new FrontEditException("需要类型范围内的有限数值；整数不能有小数");
            object value = FrontPath.Coerce(type, FromDyn(input));
            Span<byte> bytes = stackalloc byte[size];
            switch (type)
            {
                case "u8": bytes[0] = (byte)value; break;
                case "i8": bytes[0] = unchecked((byte)(sbyte)value); break;
                case "u16": BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value); break;
                case "i16": BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)value); break;
                case "u32": BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value); break;
                case "i32": BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)value); break;
                case "u64": BinaryPrimitives.WriteUInt64LittleEndian(bytes, (ulong)value); break;
                case "i64": BinaryPrimitives.WriteInt64LittleEndian(bytes, (long)value); break;
                case "f32":
                    if (!float.IsFinite((float)value)) throw new FrontEditException("数值超出 f32 范围");
                    BinaryPrimitives.WriteSingleLittleEndian(bytes, (float)value); break;
                case "f64": BinaryPrimitives.WriteDoubleLittleEndian(bytes, (double)value); break;
            }
            if (big) bytes.Reverse();
            for (int i = 0; i < size; i++) table.Set(offset + i + 1, DynValue.NewNumber(bytes[i]));
            return DynValue.Nil;
        }));
        editor.Set("binary", DynValue.NewTable(binary));
    }

    static (Table Table, int Offset, string Type, int Size, bool Big) BinaryArguments(CallbackArguments args, bool write)
    {
        int required = write ? 4 : 3;
        if (args.Count < required || args.Count > required + 1 || args[0].Type != DataType.Table)
            throw new FrontEditException("需要字节数组、0 起偏移、类型" + (write ? "、值" : "") + "，以及可选的 le/be");
        int offset = AsIndex(args[1]);
        string type = ArgString(args, 2);
        int size = type switch
        {
            "u8" or "i8" => 1, "u16" or "i16" => 2, "u32" or "i32" or "f32" => 4,
            "u64" or "i64" or "f64" => 8, _ => throw new FrontEditException("不支持的二进制类型：" + type)
        };
        var table = args[0].Table;
        if (table.Length > MaxPayloadBytes || offset < 0 || (long)offset + size > table.Length)
            throw new FrontEditException("二进制操作越界；请先用 binary.create 创建足够大的数组");
        string order = args.Count == required + 1 ? ArgString(args, required) : "le";
        if (order is not ("le" or "be")) throw new FrontEditException("字节序必须是 le 或 be");
        return (table, offset, type, size, order == "be");
    }
}
