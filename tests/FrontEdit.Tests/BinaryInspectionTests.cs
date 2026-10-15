using BtlCore.Fb;
using BtlCore.Front;
using System.Buffers.Binary;
using Xunit;

namespace BtlCore.Front.Tests;

public class BinaryInspectionTests
{
    static void I32(byte[] b, int p, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(p), v);
    static void U16(byte[] b, int p, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p), v);
    static FbsSchema Schema(string body) => FbsSchema.LoadFromText(body, "test.fbs");
    static byte[] ScalarFile(ushort value = 65535)
    {
        var b = new byte[20]; I32(b, 0, 12); U16(b, 4, 6); U16(b, 6, 8); U16(b, 8, 4); I32(b, 12, 8); U16(b, 16, value);
        return b;
    }

    [Fact]
    public void Alias_lookup_and_human_comment_are_separate()
    {
        var schema = Schema("table Root { renamed:ushort; // @alias(old_name) 中文说明\n } root_type Root;");
        Assert.True(schema.TryGetFieldByName("Root", "old_name", out var field));
        Assert.Equal(0, field.Id);
        Assert.Equal("renamed", field.Name);
        Assert.Equal("中文说明", FbsSchema.DisplayComment(field.Comment));
        Assert.Equal("前文  后文", FbsSchema.DisplayComment("前文 @alias(old_name) 后文"));
        Assert.Equal("联系 user@example.com", FbsSchema.DisplayComment("联系 user@example.com"));
        Assert.Equal("", FbsSchema.DisplayComment("@alias(old_name)"));
    }

    [Fact]
    public void Schema_reinterpretation_is_local_and_original_bytes_are_immutable()
    {
        byte[] bytes = ScalarFile();
        var original = (byte[])bytes.Clone();
        var global = SoftSchema.Schema;
        var unsigned = new BtlBinaryInspection(bytes, Schema("table Root { value:ushort; } root_type Root;"));
        bytes[16] = 0;
        var signed = unsigned.Reinterpret(Schema("table Other { renamed:short; } root_type Other;"));
        Assert.Equal((ushort)65535, unsigned.Find("/0").Value);
        Assert.Equal((short)-1, signed.Find("/0").Value);
        Assert.Equal(original, signed.CopyBytes());
        var copy = signed.CopyBytes(); copy[0] = 0;
        Assert.Equal(original, signed.CopyBytes());
        Assert.Same(global, SoftSchema.Schema);
        Assert.Equal(unsigned.Sha256, signed.Sha256);
    }

    [Fact]
    public void Physical_slots_survive_without_schema_and_unknown_values_are_not_guessed()
    {
        var b = new byte[36]; I32(b, 0, 24); U16(b, 4, 18); U16(b, 6, 12); U16(b, 8, 4); I32(b, 24, 20); I32(b, 28, 4);
        var view = new BtlBinaryInspection(b);
        Assert.Equal(7, view.Root.PhysicalSlotCount);
        Assert.Equal(7, view.Root.ChildCount);
        Assert.Equal(BinaryValueState.Unknown, view.Find("/0").State);
        Assert.Equal("raw", view.Find("/0").Kind);
        Assert.False(view.Find("/0").ExactLength);
        Assert.Equal(BinaryValueState.Absent, view.Find("/6").State);
    }

    [Fact]
    public void Unknown_scalar_fallback_is_reported_as_unresolved_in_editing_conversion()
    {
        var b = new byte[52]; I32(b, 0, 40); U16(b, 4, 30); U16(b, 6, 8); U16(b, 32, 4); I32(b, 40, 36); I32(b, 44, -1);
        var result = BtlToFront.FromBytes(b);
        Assert.True(result.Document.Root.F.ContainsKey(12));
        Assert.True(result.HasUnresolvedData);
        Assert.Equal(BinaryValueState.Unknown, new BtlBinaryInspection(b, SoftSchema.Schema).Find("/12").State);
    }

    [Fact]
    public void Absent_nonzero_default_explicit_zero_and_invalid_are_distinct()
    {
        var view = new BtlBinaryInspection(ScalarFile(0), Schema("table Root { present:ushort; absent:ushort = 7; } root_type Root;"));
        Assert.Equal(BinaryValueState.Present, view.Find("/0").State);
        Assert.Equal((ushort)0, view.Find("/0").Value);
        Assert.Equal(BinaryValueState.Absent, view.Find("/1").State);
        Assert.Contains("7", view.Find("/1").Value.ToString());
        var b = ScalarFile(); U16(b, 6, 4);
        var invalid = new BtlBinaryInspection(b, Schema("table Root { value:ushort; } root_type Root;")).Find("/0");
        Assert.Equal(BinaryValueState.Invalid, invalid.State);
        Assert.Equal(16, invalid.Offset);
        Assert.NotEqual((ushort)0, invalid.Value);
        Assert.NotEmpty(new BtlBinaryInspection(b).Hex(16, 2));
    }

    [Fact]
    public void Broken_pointer_keeps_the_actual_slot_and_bytes()
    {
        var b = ScalarFile(); I32(b, 16, 0x001e0001);
        var view = new BtlBinaryInspection(b, Schema("table Child {} table Root { child:Child; } root_type Root;"));
        var field = view.Find("/0");
        Assert.Equal(BinaryValueState.Invalid, field.State);
        Assert.Equal(16, field.Offset);
        Assert.Equal(4, field.Length);
        Assert.Contains("01 00 1E 00", view.Hex(16, 4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(16)]
    public void Empty_and_short_files_are_not_successful_conversions(int length)
    {
        var bytes = new byte[length];
        var r = BtlToFront.FromBytes(bytes);
        Assert.False(r.Ok);
        Assert.True(r.HasUnresolvedData);
        Assert.Equal(BinaryValueState.Invalid, new BtlBinaryInspection(bytes).Root.State);
    }

    [Fact]
    public void Backwards_pointer_cannot_create_a_cyclic_document()
    {
        var b = new byte[24]; I32(b, 0, 12); U16(b, 4, 8); U16(b, 6, 12); U16(b, 10, 8); I32(b, 12, 8); I32(b, 20, -8);
        var r = BtlToFront.FromBytes(b);
        Assert.False(r.Document.Root.F.ContainsKey(1));
        Assert.True(r.HasUnresolvedData);
        _ = BtlFrontJson.CloneDocument(r.Document);
        var view = new BtlBinaryInspection(b, Schema("table Root { missing:ushort; self:Root; } root_type Root;"));
        Assert.Equal(BinaryValueState.Invalid, view.Find("/1").State);
    }

    [Fact]
    public void Object_boundary_and_truncated_scalar_are_not_read_as_values()
    {
        var b = ScalarFile(); U16(b, 6, 4);
        var r = BtlToFront.FromBytes(b);
        Assert.False(r.Document.Root.F.ContainsKey(0));
        Assert.True(r.HasUnresolvedData);
        b = ScalarFile()[..17];
        r = BtlToFront.FromBytes(b);
        Assert.False(r.Document.Root.F.ContainsKey(0));
        Assert.True(r.HasUnresolvedData);
    }

    [Fact]
    public void Struct_alignment_nested_layout_and_enum_defaults_are_interpreted()
    {
        var schema = Schema("enum Mode:ubyte { Off, On = 3 } struct Inner { small:ubyte; wide:uint; } struct Outer { first:ubyte; nested:Inner; } table Root { data:Outer; mode:Mode = On; } root_type Root;");
        var b = new byte[36]; I32(b, 0, 12); U16(b, 4, 6); U16(b, 6, 24); U16(b, 8, 4); I32(b, 12, 8);
        b[16] = 1; b[20] = 2; I32(b, 24, 0x12345678);
        var view = new BtlBinaryInspection(b, schema);
        Assert.Equal(12, view.Find("/0").Length);
        Assert.Equal(20, view.Find("/0/1").Offset);
        Assert.Equal(0x12345678u, view.Find("/0/1/1").Value);
        Assert.Contains("3", view.Find("/1").Value.ToString());
    }

    [Fact]
    public void Invalid_strings_and_vectors_keep_error_nodes()
    {
        var b = new byte[28]; ScalarFile().CopyTo(b, 0); I32(b, 16, 4); I32(b, 20, 2); b[24] = 0xff; b[25] = 0xff;
        var view = new BtlBinaryInspection(b, Schema("table Root { text:string; } root_type Root;"));
        Assert.Equal(BinaryValueState.Invalid, view.Find("/0").State);
        I32(b, 20, int.MaxValue);
        view = new BtlBinaryInspection(b, Schema("table Root { values:[ushort]; } root_type Root;"));
        Assert.Equal(BinaryValueState.Invalid, view.Find("/0").State);
        Assert.Null(view.Find("/0/i0"));
    }

    [Fact]
    public void Unsupported_or_malformed_schema_is_explicitly_rejected()
    {
        Assert.Throws<InvalidDataException>(() => new BtlBinaryInspection(ScalarFile(), Schema("table Root { value:Undefined; } root_type Root;")));
        Assert.Throws<InvalidDataException>(() => Schema("table Root { broken declaration } root_type Root;"));
        Assert.Throws<InvalidDataException>(() => new BtlBinaryInspection(ScalarFile(), Schema("table Root { value:ubyte = 256; } root_type Root;")));
        Assert.Throws<InvalidDataException>(() => new BtlBinaryInspection(ScalarFile(), Schema("table Root { value:ushort (required); } root_type Root;")));
    }

    [Fact]
    public void Required_missing_field_is_an_error_and_sparse_schema_ids_are_preserved()
    {
        var view = new BtlBinaryInspection(ScalarFile(), Schema("table Root { value:ushort (id: 0); required_child:string (id: 12, required); } root_type Root;"));
        Assert.Equal(2, view.Root.ChildCount);
        Assert.Equal("/12", view.Root.GetChild(1).Path);
        Assert.Equal(BinaryValueState.Invalid, view.Find("/12").State);
        Assert.Null(view.Find("/12").Offset);
        Assert.Contains("required", view.Find("/12").Diagnostic);
    }

    [Fact]
    public void Packed_decoding_is_an_additional_view_and_raw_elements_remain()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var vector = BtlFrontJson.NewVector("u8"); vector.Enc = PackedJsonStream.EncName;
        vector.V.Add(System.Text.Json.Nodes.JsonNode.Parse("{\"btid\":7}")); doc.Root.F[10] = vector;
        byte[] bytes = FrontToBtl.Build(doc).Bytes;
        var view = new BtlBinaryInspection(bytes, SoftSchema.Schema);
        Assert.IsType<byte>(view.Find("/10/i0").Value);
        Assert.Equal(BinaryValueState.Decoded, view.Find("/10/decoded").State);
        Assert.NotNull(view.Find("/10/decoded/i0/0"));
        Assert.Equal(bytes, view.CopyBytes());
        Assert.Null(view.Find("/10/nonexistent"));
    }

    [Fact]
    public void Unsupported_packed_type_has_a_navigable_error_view_and_keeps_bytes()
    {
        byte[] packed = PackedJsonStream.Encode(new object[] { System.Text.Json.Nodes.JsonNode.Parse("{\"btid\":7}") });
        packed[^2] = 252;
        Assert.False(PackedJsonStream.TryDecode(packed, out _));
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var vector = BtlFrontJson.NewVector("u8"); vector.V.AddRange(packed.Select(b => (object)b)); doc.Root.F[10] = vector;
        var view = new BtlBinaryInspection(FrontToBtl.Build(doc).Bytes, SoftSchema.Schema);
        var failed = view.Find("/10/decoded");
        Assert.Equal(BinaryValueState.Invalid, failed.State);
        Assert.Equal(packed.Length, failed.Length);
        Assert.Contains("解码失败", failed.Diagnostic);
        Assert.IsType<byte>(view.Find("/10/i0").Value);
    }

    [Fact]
    public void Excessive_schema_depth_is_rejected_before_traversing_a_file()
    {
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < 80; i++) text.Append($"struct S{i} {{ next:S{i + 1}; }} ");
        text.Append("struct S80 { value:ubyte; } table Root { data:S0; } root_type Root;");
        Assert.Throws<InvalidDataException>(() => new BtlBinaryInspection(ScalarFile(), Schema(text.ToString())));
    }

    [Fact]
    public void Mutated_inputs_can_be_inspected_without_unhandled_range_exceptions()
    {
        var random = new Random(20261012);
        for (int iteration = 0; iteration < 600; iteration++)
        {
            byte[] bytes = SyntheticBtl.MinimalStage();
            for (int j = 0; j < 4; j++) bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
            var r = BtlToFront.FromBytes(bytes);
            var view = new BtlBinaryInspection(bytes, SoftSchema.Schema);
            var queue = new Queue<BinaryInspectionNode>(); queue.Enqueue(view.Root);
            int budget = 300;
            while (queue.Count > 0 && budget-- > 0)
            {
                var node = queue.Dequeue();
                for (int i = 0; i < Math.Min(node.ChildCount, 20); i++) queue.Enqueue(node.GetChild(i));
            }
            Assert.NotNull(r.Document);
        }
    }
}
