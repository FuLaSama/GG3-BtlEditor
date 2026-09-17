/*
 * SyntheticBtl.cs
 *
 * 手写一份最小合法 BTL，给 Smoke 自检：不依赖真实关卡文件。
 * 内容：Root.version=1 + MapTerrain 宽 2 高 1，tiles=[9001,9001]。
 *
 * 布局必须和游戏一致才能被 BtlToFront 当表认出来：
 *   vtable 在对象前面（对象头 4 字节是指向 vtable 的负向 soffset）
 *   子对象（tiles 向量）在指针槽之后，uoffset 为正
 *   文件最前面 4 字节是 Root 的绝对偏移
 */
namespace BtlCore.Fb
{
    public static class SyntheticBtl
    {
        /// <summary>返回可被 BtlToFront 读入、再被 FrontToBtl 写回的最小战役缓冲。</summary>
        public static byte[] MinimalStage()
        {
            var b = new List<byte>(128);

            void U8(byte v) => b.Add(v);
            void U16(ushort v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
            void I32(int v)
            {
                b.Add((byte)(v & 0xff));
                b.Add((byte)((v >> 8) & 0xff));
                b.Add((byte)((v >> 16) & 0xff));
                b.Add((byte)((v >> 24) & 0xff));
            }
            void SetI32(int at, int v)
            {
                b[at] = (byte)(v & 0xff);
                b[at + 1] = (byte)((v >> 8) & 0xff);
                b[at + 2] = (byte)((v >> 16) & 0xff);
                b[at + 3] = (byte)((v >> 24) & 0xff);
            }

            I32(0); // [0..3] root uoffset placeholder

            // Root vtable（fields 0..1）在对象之前
            int rootVt = b.Count;
            U16(8);
            U16(12);
            U16(4);  // version
            U16(8);  // map

            int root = b.Count;
            I32(root - rootVt);
            U16(1);
            U16(0); // pad
            int rootMapField = b.Count;
            I32(0); // map 指针占位
            SetI32(0, root);

            // MapTerrain 写在 Root 之后 → 正 uoffset
            int mtVt = b.Count;
            U16(14);
            U16(28);
            U16(4);  // Size @+4
            U16(16); // u8 @+16
            U16(20); // vector @+20
            U16(0);
            U16(24); // bool @+24

            int mt = b.Count;
            I32(mt - mtVt);
            U16(2); U16(1);          // Size w,h
            U16(0); U16(0);          // margins
            U16(2); U16(1);          // playable
            U8(1);                   // field1
            U8(0); U8(0); U8(0);     // pad to +20
            int mtVecField = b.Count;
            I32(0);                  // tiles 指针占位
            U8(0);                   // fog bool
            U8(0); U8(0); U8(0);     // pad object to 28
            SetI32(rootMapField, mt - rootMapField);

            // tiles 向量写在 MapTerrain 之后
            int tilesVec = b.Count;
            I32(2);
            U16(9001);
            U16(9001);
            SetI32(mtVecField, tilesVec - mtVecField);

            return b.ToArray();
        }
    }
}
