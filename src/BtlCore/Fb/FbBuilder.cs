/*
 * FbBuilder.cs
 *
 * 与 Google FlatBuffers 官方 C# FlatBufferBuilder 同一套算法，用来把 Front 树写成 .btl。
 *
 * 关键点（和「从前往后 memcpy」完全相反）：
 *   - 缓冲区从尾部往头部生长。Offset = 已用字节数，相对「当前文件末尾」。
 *   - 先写子对象（字符串、向量、子表），再 StartTable 写父表；因此子对象在文件里更靠后。
 *   - 字段值是相对指针 uoffset，指向「当前位置之后」的对象。
 *   - vtable 写在对象前面；相同布局的 vtable 会去重共用。
 *   - 默认 0 的槽可以不 Slot，vtable 里偏移为 0 表示缺省。
 *
 * Prep 对齐时 Grow 已经把 used 拷到新数组末尾，不能再给 _space 加长度差，否则会越界。
 * 本类不保留原文件偏移：每次 Build 都是全新缓冲区。
 */
namespace BtlCore.Fb
{
    /// <summary>从尾往头写的 FlatBuffers 构建器，仅供 FrontToBtl 使用。</summary>
    sealed class FbBuilder
    {
        byte[] _bb;
        /// <summary>下一个可写字节的下标。写入时先减小再填，数据堆在数组后半段。</summary>
        int _space;
        int _minAlign = 1;
        int[] _vtable = new int[16];
        int _vtableSize = -1;
        int _objectStart;
        int[] _vtables = new int[16];
        int _numVtables;
        int _vectorNumElems;

        public FbBuilder(int initialSize = 1024)
        {
            if (initialSize < 32) initialSize = 32;
            _bb = new byte[initialSize];
            _space = initialSize;
        }

        /// <summary>相对当前缓冲末尾的已写长度，也是「对象偏移」。</summary>
        public int Offset => _bb.Length - _space;

        /// <summary>裁掉前半空白，得到最终文件字节。</summary>
        public byte[] SizedByteArray()
        {
            int n = _bb.Length - _space;
            var a = new byte[n];
            Buffer.BlockCopy(_bb, _space, a, 0, n);
            return a;
        }

        /// <summary>
        /// 为即将写入的 size 字节腾出对齐空间。additionalBytes 是后面还要写的（例如向量元素）。
        /// 空间不够只调用 Grow：Grow 已经把 _space 调到新数组对应位置。
        /// </summary>
        public void Prep(int size, int additionalBytes)
        {
            if (size > _minAlign) _minAlign = size;
            int alignSize = ((~(_bb.Length - _space + additionalBytes)) + 1) & (size - 1);
            while (_space < alignSize + size + additionalBytes)
                Grow();
            if (alignSize > 0) Pad(alignSize);
        }

        /// <summary>容量翻倍，把已用尾部拷到新数组末尾。_space 指向新的空洞起点。</summary>
        void Grow()
        {
            int oldLen = _bb.Length;
            int used = oldLen - _space;
            var n = new byte[oldLen * 2];
            Buffer.BlockCopy(_bb, _space, n, n.Length - used, used);
            _bb = n;
            _space = n.Length - used;
        }

        void Pad(int size)
        {
            _space -= size;
            for (int i = 0; i < size; i++) _bb[_space + i] = 0;
        }

        public void PutByte(byte x) => _bb[--_space] = x;
        public void PutUshort(ushort x)
        {
            _space -= 2;
            _bb[_space] = (byte)x;
            _bb[_space + 1] = (byte)(x >> 8);
        }
        public void PutShort(short x) => PutUshort(unchecked((ushort)x));
        public void PutInt(int x)
        {
            _space -= 4;
            _bb[_space] = (byte)x;
            _bb[_space + 1] = (byte)(x >> 8);
            _bb[_space + 2] = (byte)(x >> 16);
            _bb[_space + 3] = (byte)(x >> 24);
        }
        public void PutUint(uint x) => PutInt(unchecked((int)x));
        public void PutLong(long x)
        {
            PutInt(unchecked((int)(x >> 32)));
            PutInt(unchecked((int)x));
        }
        public void PutFloat(float x) => PutInt(BitConverter.ToInt32(BitConverter.GetBytes(x), 0));
        public void PutDouble(double x) => PutLong(BitConverter.ToInt64(BitConverter.GetBytes(x), 0));

        short GetShort(int i) => (short)(_bb[i] | (_bb[i + 1] << 8));
        void PutIntAt(int i, int x)
        {
            _bb[i] = (byte)x;
            _bb[i + 1] = (byte)(x >> 8);
            _bb[i + 2] = (byte)(x >> 16);
            _bb[i + 3] = (byte)(x >> 24);
        }

        public void AddByte(byte x) { Prep(1, 0); PutByte(x); }
        public void AddSbyte(sbyte x) => AddByte(unchecked((byte)x));
        public void AddBool(bool x) => AddByte(x ? (byte)1 : (byte)0);
        public void AddUshort(ushort x) { Prep(2, 0); PutUshort(x); }
        public void AddShort(short x) => AddUshort(unchecked((ushort)x));
        public void AddInt(int x) { Prep(4, 0); PutInt(x); }
        public void AddUint(uint x) => AddInt(unchecked((int)x));
        public void AddFloat(float x) { Prep(4, 0); PutFloat(x); }
        public void AddLong(long x) { Prep(8, 0); PutLong(x); }
        public void AddDouble(double x) { Prep(8, 0); PutDouble(x); }

        /// <summary>写入相对当前 Offset 的 uoffset（指向已经写好的子对象）。</summary>
        public void AddOffset(int off)
        {
            Prep(4, 0);
            if (off > Offset) throw new ArgumentException("bad offset");
            PutInt(Offset - off + 4);
        }

        public void StartVector(int elemSize, int count, int alignment)
        {
            if (_vtableSize >= 0) throw new InvalidOperationException("nested");
            _vectorNumElems = count;
            Prep(4, elemSize * count);
            Prep(alignment, elemSize * count);
        }

        public int EndVector()
        {
            PutInt(_vectorNumElems);
            return Offset;
        }

        public int CreateString(string s)
        {
            s ??= "";
            AddByte(0);
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(s);
            StartVector(1, utf8.Length, 1);
            _space -= utf8.Length;
            Buffer.BlockCopy(utf8, 0, _bb, _space, utf8.Length);
            return EndVector();
        }

        public int CreateByteVector(byte[] data)
        {
            data ??= Array.Empty<byte>();
            StartVector(1, data.Length, 1);
            _space -= data.Length;
            if (data.Length > 0)
                Buffer.BlockCopy(data, 0, _bb, _space, data.Length);
            return EndVector();
        }

        /// <summary>开始一张表。numfields 是最大 field id + 1（vtable 槽数）。</summary>
        public void StartTable(int numfields)
        {
            if (numfields < 0) throw new ArgumentOutOfRangeException(nameof(numfields));
            if (_vtableSize >= 0) throw new InvalidOperationException("nested table");
            if (_vtable.Length < numfields) _vtable = new int[numfields];
            _vtableSize = numfields;
            _objectStart = Offset;
        }

        /// <summary>记下刚写入的字段在对象里的位置，供 EndTable 填 vtable。</summary>
        public void Slot(int voffset)
        {
            if (voffset < 0 || voffset >= _vtableSize)
                throw new IndexOutOfRangeException("slot");
            _vtable[voffset] = Offset;
        }

        /// <summary>
        /// 写出 vtable + soffset。从高 id 往低扫，尾部全 0 的槽可以剪掉。
        /// 若已有完全相同的 vtable，丢掉刚写的那份，改成指向旧的。
        /// 返回对象起始偏移（vtable 后面那四个字节 soffset 所在处）。
        /// </summary>
        public int EndTable()
        {
            if (_vtableSize < 0) throw new InvalidOperationException("EndTable without StartTable");
            AddInt(0);
            int vtableloc = Offset;
            int i = _vtableSize - 1;
            for (; i >= 0 && _vtable[i] == 0; i--) { }
            int trimmedSize = i + 1;
            for (; i >= 0; i--)
            {
                short off = (short)(_vtable[i] != 0 ? vtableloc - _vtable[i] : 0);
                AddShort(off);
                _vtable[i] = 0;
            }
            AddShort((short)(vtableloc - _objectStart));
            AddShort((short)((trimmedSize + 2) * 2));

            int existing = 0;
            for (int t = 0; t < _numVtables; t++)
            {
                int vt1 = _bb.Length - _vtables[t];
                int vt2 = _space;
                short len = GetShort(vt1);
                if (len != GetShort(vt2)) continue;
                bool same = true;
                for (int j = 2; j < len; j += 2)
                {
                    if (GetShort(vt1 + j) != GetShort(vt2 + j)) { same = false; break; }
                }
                if (same) { existing = _vtables[t]; break; }
            }

            if (existing != 0)
            {
                _space = _bb.Length - vtableloc;
                PutIntAt(_space, existing - vtableloc);
            }
            else
            {
                if (_numVtables == _vtables.Length)
                {
                    var nv = new int[_numVtables * 2];
                    Array.Copy(_vtables, nv, _vtables.Length);
                    _vtables = nv;
                }
                _vtables[_numVtables++] = Offset;
                PutIntAt(_bb.Length - vtableloc, Offset - vtableloc);
            }

            _vtableSize = -1;
            return vtableloc;
        }

        /// <summary>文件最前面写 root 的 uoffset，并按整文件最大对齐补齐。</summary>
        public void Finish(int rootTable)
        {
            Prep(_minAlign, 4);
            AddOffset(rootTable);
        }
    }
}
