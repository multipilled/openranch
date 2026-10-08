// Crunch texture decoder: a C# port of the decoding half of crunch/crnlib (crn_decomp.h,
// Unity-Technologies/crunch, "unity" branch), limited to the DXT1, DXT5, DXT5A and DXN formats.
//
// This is an altered version of the original software: it was translated from C++ to C#,
// restructured, and the ETC formats, allocator, debugging and file-validation code were removed.
//
// Original notice:
//
//   crunch/crnlib uses the ZLIB license: http://opensource.org/licenses/Zlib
//   Copyright (c) 2010-2016 Richard Geldreich, Jr. and Binomial LLC
//
//   This software is provided 'as-is', without any express or implied warranty. In no event will
//   the authors be held liable for any damages arising from the use of this software.
//
//   Permission is granted to anyone to use this software for any purpose, including commercial
//   applications, and to alter it and redistribute it freely, subject to the following restrictions:
//
//   1. The origin of this software must not be misrepresented; you must not claim that you wrote
//      the original software. If you use this software in a product, an acknowledgment in the
//      product documentation would be appreciated but is not required.
//   2. Altered source versions must be plainly marked as such, and must not be misrepresented as
//      being the original software.
//   3. This notice may not be removed or altered from any source distribution.

namespace OpenRanch.Formats.Unity;

/// <summary>Decodes Unity "crunched" textures (DXT1Crunched, DXT5Crunched) back into plain DXT blocks.</summary>
public static class Crunch
{
    public enum CrnFormat
    {
        Dxt1 = 0, Dxt3 = 1, Dxt5 = 2, Dxt5CCxY = 3, Dxt5xGxR = 4, Dxt5xGBR = 5, Dxt5AGBR = 6,
        DxnXY = 7, DxnYX = 8, Dxt5A = 9, Etc1 = 10, Etc2 = 11, Etc2A = 12, Etc1S = 13, Etc2AS = 14,
    }

    public sealed record Header(int Width, int Height, int Levels, int Faces, CrnFormat Format, uint DataSize, uint[] LevelOffsets,
        (uint Ofs, uint Size, uint Num) ColorEndpoints, (uint Ofs, uint Size, uint Num) ColorSelectors,
        (uint Ofs, uint Size, uint Num) AlphaEndpoints, (uint Ofs, uint Size, uint Num) AlphaSelectors,
        uint TablesSize, uint TablesOfs)
    {
        public int BlockSize => Format is CrnFormat.Dxt1 or CrnFormat.Dxt5A ? 8 : 16;
    }

    private const int HeaderMinSize = 74;

    private static uint Be(ReadOnlySpan<byte> d, int at, int n)
    {
        uint v = 0;
        for (var i = 0; i < n; i++)
            v = (v << 8) | d[at + i];
        return v;
    }

    public static Header ReadHeader(ReadOnlySpan<byte> d)
    {
        if (d.Length < HeaderMinSize || Be(d, 0, 2) != (('H' << 8) | 'x'))
            throw new InvalidDataException("Not a crunch texture.");
        var headerSize = Be(d, 2, 2);
        var dataSize = Be(d, 6, 4);
        if (headerSize < HeaderMinSize || d.Length < dataSize)
            throw new InvalidDataException("Truncated crunch texture.");
        var levels = (int)Be(d, 16, 1);
        var offsets = new uint[levels];
        for (var i = 0; i < levels; i++)
            offsets[i] = Be(d, 70 + i * 4, 4);
        return new Header((int)Be(d, 12, 2), (int)Be(d, 14, 2), levels, (int)Be(d, 17, 1), (CrnFormat)Be(d, 18, 1), dataSize, offsets,
            Palette(d, 33), Palette(d, 41), Palette(d, 49), Palette(d, 57), Be(d, 65, 2), Be(d, 67, 3));
    }

    private static (uint, uint, uint) Palette(ReadOnlySpan<byte> d, int at) => (Be(d, at, 3), Be(d, at + 3, 3), Be(d, at + 6, 2));

    /// <summary>
    /// Decodes every mip level of the first face into consecutive DXT blocks, largest level first,
    /// the layout GPUs and Godot expect.
    /// </summary>
    public static byte[] DecodeAllLevels(byte[] data, out Header header)
    {
        header = ReadHeader(data);
        if (header.Format is CrnFormat.Etc1 or CrnFormat.Etc2 or CrnFormat.Etc2A or CrnFormat.Etc1S or CrnFormat.Etc2AS or CrnFormat.Dxt3)
            throw new NotSupportedException($"Crunch format {header.Format} isn't supported.");
        var unpacker = new Unpacker(data, header);
        var sizes = new int[header.Levels];
        var total = 0;
        for (var level = 0; level < header.Levels; level++)
        {
            var w = Math.Max(header.Width >> level, 1);
            var h = Math.Max(header.Height >> level, 1);
            sizes[level] = ((w + 3) >> 2) * ((h + 3) >> 2) * header.BlockSize;
            total += sizes[level];
        }
        var output = new byte[total];
        var offset = 0;
        for (var level = 0; level < header.Levels; level++)
        {
            unpacker.UnpackLevel(level, output.AsSpan(offset, sizes[level]));
            offset += sizes[level];
        }
        return output;
    }

    private sealed class DecoderTables
    {
        public const int MaxExpectedCodeSize = 16;
        public const int MaxTableBits = 11;

        public uint NumSyms;
        public uint TableBits;
        public uint TableMaxCode;
        public uint DecodeStartCodeSize;
        public readonly uint[] MaxCodes = new uint[MaxExpectedCodeSize + 1];
        public readonly int[] ValPtrs = new int[MaxExpectedCodeSize + 1];
        public uint[] Lookup = [];
        public ushort[] SortedSymbolOrder = [];

        public bool Init(uint numSyms, byte[] codeSizes, uint tableBits)
        {
            var minCodes = new uint[MaxExpectedCodeSize];
            if (numSyms == 0 || tableBits > MaxTableBits)
                return false;
            NumSyms = numSyms;
            var numCodes = new uint[MaxExpectedCodeSize + 1];
            for (var i = 0; i < numSyms; i++)
                if (codeSizes[i] != 0)
                    numCodes[codeSizes[i]]++;

            var sortedPositions = new uint[MaxExpectedCodeSize + 1];
            uint curCode = 0, totalUsed = 0, maxCodeSize = 0, minCodeSize = uint.MaxValue;
            for (var i = 1; i <= MaxExpectedCodeSize; i++)
            {
                var n = numCodes[i];
                if (n == 0)
                {
                    MaxCodes[i - 1] = 0;
                }
                else
                {
                    minCodeSize = Math.Min(minCodeSize, (uint)i);
                    maxCodeSize = Math.Max(maxCodeSize, (uint)i);
                    minCodes[i - 1] = curCode;
                    MaxCodes[i - 1] = curCode + n - 1;
                    MaxCodes[i - 1] = 1 + ((MaxCodes[i - 1] << (16 - i)) | (uint)((1 << (16 - i)) - 1));
                    ValPtrs[i - 1] = (int)totalUsed;
                    sortedPositions[i] = totalUsed;
                    curCode += n;
                    totalUsed += n;
                }
                curCode <<= 1;
            }

            if (totalUsed > SortedSymbolOrder.Length)
                SortedSymbolOrder = new ushort[totalUsed];
            for (var i = 0; i < numSyms; i++)
            {
                var c = codeSizes[i];
                if (c != 0)
                    SortedSymbolOrder[sortedPositions[c]++] = (ushort)i;
            }

            if (tableBits <= minCodeSize)
                tableBits = 0;
            TableBits = tableBits;

            if (tableBits != 0)
            {
                var tableSize = 1u << (int)tableBits;
                if (tableSize > Lookup.Length)
                    Lookup = new uint[tableSize];
                Array.Fill(Lookup, uint.MaxValue);
                for (var codeSize = 1; codeSize <= tableBits; codeSize++)
                {
                    if (numCodes[codeSize] == 0)
                        continue;
                    var fillSize = (int)tableBits - codeSize;
                    var fillNum = 1u << fillSize;
                    var minCode = minCodes[codeSize - 1];
                    var maxCode = UnshiftedMaxCode(codeSize);
                    var valPtr = ValPtrs[codeSize - 1];
                    for (var code = minCode; code <= maxCode; code++)
                    {
                        var symIndex = SortedSymbolOrder[valPtr + (int)(code - minCode)];
                        for (uint j = 0; j < fillNum; j++)
                            Lookup[j + (code << fillSize)] = symIndex | ((uint)codeSize << 16);
                    }
                }
            }

            for (var i = 0; i < MaxExpectedCodeSize; i++)
                ValPtrs[i] -= (int)minCodes[i];

            TableMaxCode = 0;
            DecodeStartCodeSize = minCodeSize;
            if (tableBits != 0)
            {
                uint i;
                for (i = tableBits; i >= 1; i--)
                {
                    if (numCodes[i] != 0)
                    {
                        TableMaxCode = MaxCodes[i - 1];
                        break;
                    }
                }
                if (i >= 1)
                {
                    DecodeStartCodeSize = tableBits + 1;
                    for (var j = tableBits + 1; j <= maxCodeSize; j++)
                    {
                        if (numCodes[j] != 0)
                        {
                            DecodeStartCodeSize = j;
                            break;
                        }
                    }
                }
            }

            MaxCodes[MaxExpectedCodeSize] = uint.MaxValue;
            ValPtrs[MaxExpectedCodeSize] = 0xFFFFF;
            return true;
        }

        private uint UnshiftedMaxCode(int len)
        {
            var k = MaxCodes[len - 1];
            return k == 0 ? uint.MaxValue : (k - 1) >> (16 - len);
        }
    }

    private sealed class HuffmanModel
    {
        public uint TotalSyms;
        public byte[] CodeSizes = [];
        public DecoderTables? Tables;

        public bool PrepareDecoderTables()
        {
            TotalSyms = (uint)CodeSizes.Length;
            Tables ??= new DecoderTables();
            uint tableBits = 0;
            if (TotalSyms > 16)
                tableBits = Math.Min(1 + CeilLog2(TotalSyms), DecoderTables.MaxTableBits);
            return Tables.Init(TotalSyms, CodeSizes, tableBits);
        }

        private static uint CeilLog2(uint v)
        {
            uint l = 0;
            var x = v;
            while (x > 1)
            {
                x >>= 1;
                l++;
            }
            if (l != 32 && v > (1u << (int)l))
                l++;
            return l;
        }
    }

    private sealed class SymbolCodec
    {
        private byte[] _buf = [];
        private int _next, _end;
        private uint _bitBuf;
        private int _bitCount;

        private static readonly byte[] MostProbableCodelengthCodes = [17, 18, 19, 20, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15, 16];

        public void Start(byte[] buf, uint offset, uint size)
        {
            if (size == 0 || offset + size > buf.Length)
                throw new InvalidDataException("Crunch stream out of range.");
            _buf = buf;
            _next = (int)offset;
            _end = (int)(offset + size);
            _bitBuf = 0;
            _bitCount = 0;
        }

        public uint DecodeBits(int numBits)
        {
            if (numBits == 0)
                return 0;
            if (numBits > 16)
            {
                var a = GetBits(numBits - 16);
                var b = GetBits(16);
                return (a << 16) | b;
            }
            return GetBits(numBits);
        }

        private uint GetBits(int numBits)
        {
            while (_bitCount < numBits)
            {
                uint c = _next != _end ? _buf[_next++] : 0u;
                _bitCount += 8;
                _bitBuf |= c << (32 - _bitCount);
            }
            var result = _bitBuf >> (32 - numBits);
            _bitBuf <<= numBits;
            _bitCount -= numBits;
            return result;
        }

        public bool ReceiveModel(HuffmanModel model)
        {
            var totalUsedSyms = DecodeBits(14); // total_bits(8192 max symbols)
            if (totalUsedSyms == 0)
            {
                model.TotalSyms = 0;
                model.CodeSizes = [];
                model.Tables = null;
                return true;
            }
            model.CodeSizes = new byte[totalUsedSyms];

            var numCodelengthCodes = DecodeBits(5);
            if (numCodelengthCodes is < 1 or > 21)
                return false;
            var dm = new HuffmanModel { CodeSizes = new byte[21] };
            for (var i = 0; i < numCodelengthCodes; i++)
                dm.CodeSizes[MostProbableCodelengthCodes[i]] = (byte)DecodeBits(3);
            if (!dm.PrepareDecoderTables())
                return false;

            uint ofs = 0;
            while (ofs < totalUsedSyms)
            {
                var remaining = totalUsedSyms - ofs;
                var code = Decode(dm);
                if (code <= 16)
                {
                    model.CodeSizes[ofs++] = (byte)code;
                }
                else if (code == 17)
                {
                    var len = DecodeBits(3) + 3;
                    if (len > remaining)
                        return false;
                    ofs += len;
                }
                else if (code == 18)
                {
                    var len = DecodeBits(7) + 11;
                    if (len > remaining)
                        return false;
                    ofs += len;
                }
                else if (code is 19 or 20)
                {
                    var len = code == 19 ? DecodeBits(2) + 3 : DecodeBits(6) + 7;
                    if (ofs == 0 || len > remaining)
                        return false;
                    var prev = model.CodeSizes[ofs - 1];
                    if (prev == 0)
                        return false;
                    var end = ofs + len;
                    while (ofs < end)
                        model.CodeSizes[ofs++] = prev;
                }
                else
                {
                    return false;
                }
            }
            return ofs == totalUsedSyms && model.PrepareDecoderTables();
        }

        public uint Decode(HuffmanModel model)
        {
            var t = model.Tables!;
            if (_bitCount < 24)
            {
                if (_bitCount < 16)
                {
                    uint c0 = 0, c1 = 0;
                    if (_next < _end) c0 = _buf[_next++];
                    if (_next < _end) c1 = _buf[_next++];
                    _bitCount += 16;
                    _bitBuf |= ((c0 << 8) | c1) << (32 - _bitCount);
                }
                else
                {
                    uint c = _next < _end ? _buf[_next++] : 0u;
                    _bitCount += 8;
                    _bitBuf |= c << (32 - _bitCount);
                }
            }

            var k = (_bitBuf >> 16) + 1;
            uint sym, len;
            if (k <= t.TableMaxCode)
            {
                var entry = t.Lookup[_bitBuf >> (int)(32 - t.TableBits)];
                sym = entry & 0xFFFF;
                len = entry >> 16;
            }
            else
            {
                len = t.DecodeStartCodeSize;
                while (k > t.MaxCodes[len - 1])
                    len++;
                var valPtr = t.ValPtrs[len - 1] + (int)(_bitBuf >> (int)(32 - len));
                if ((uint)valPtr >= model.TotalSyms)
                    throw new InvalidDataException("Corrupt crunch stream.");
                sym = t.SortedSymbolOrder[valPtr];
            }
            _bitBuf <<= (int)len;
            _bitCount -= (int)len;
            return sym;
        }
    }

    private sealed class Unpacker
    {
        private static readonly byte[] Dxt5FromLinear = [0, 2, 3, 4, 5, 6, 7, 1];

        private readonly byte[] _data;
        private readonly Header _header;
        private readonly SymbolCodec _codec = new();
        private readonly HuffmanModel _referenceEncoding = new();
        private readonly HuffmanModel[] _endpointDelta = [new(), new()];
        private readonly HuffmanModel[] _selectorDelta = [new(), new()];
        private uint[] _colorEndpoints = [];
        private uint[] _colorSelectors = [];
        private ushort[] _alphaEndpoints = [];
        private ushort[] _alphaSelectors = [];

        private struct BlockBufferElement
        {
            public ushort EndpointReference;
            public ushort ColorEndpointIndex;
            public ushort Alpha0EndpointIndex;
            public ushort Alpha1EndpointIndex;
        }

        private BlockBufferElement[] _blockBuffer = [];

        public Unpacker(byte[] data, Header header)
        {
            _data = data;
            _header = header;
            InitTables();
            DecodePalettes();
        }

        private static void Check(bool ok)
        {
            if (!ok)
                throw new InvalidDataException("Corrupt crunch tables.");
        }

        private void InitTables()
        {
            _codec.Start(_data, _header.TablesOfs, _header.TablesSize);
            Check(_codec.ReceiveModel(_referenceEncoding));
            Check(_header.ColorEndpoints.Num != 0 || _header.AlphaEndpoints.Num != 0);
            if (_header.ColorEndpoints.Num != 0)
            {
                Check(_codec.ReceiveModel(_endpointDelta[0]));
                Check(_codec.ReceiveModel(_selectorDelta[0]));
            }
            if (_header.AlphaEndpoints.Num != 0)
            {
                Check(_codec.ReceiveModel(_endpointDelta[1]));
                Check(_codec.ReceiveModel(_selectorDelta[1]));
            }
        }

        private void DecodePalettes()
        {
            if (_header.ColorEndpoints.Num != 0)
            {
                DecodeColorEndpoints();
                DecodeColorSelectors();
            }
            if (_header.AlphaEndpoints.Num != 0)
            {
                DecodeAlphaEndpoints();
                DecodeAlphaSelectors();
            }
        }

        private void DecodeColorEndpoints()
        {
            var num = _header.ColorEndpoints.Num;
            _colorEndpoints = new uint[num];
            _codec.Start(_data, _header.ColorEndpoints.Ofs, _header.ColorEndpoints.Size);
            var dm0 = new HuffmanModel();
            var dm1 = new HuffmanModel();
            Check(_codec.ReceiveModel(dm0));
            Check(_codec.ReceiveModel(dm1));
            uint a = 0, b = 0, c = 0, d = 0, e = 0, f = 0;
            for (var i = 0; i < num; i++)
            {
                a = (a + _codec.Decode(dm0)) & 31;
                b = (b + _codec.Decode(dm1)) & 63;
                c = (c + _codec.Decode(dm0)) & 31;
                d = (d + _codec.Decode(dm0)) & 31;
                e = (e + _codec.Decode(dm1)) & 63;
                f = (f + _codec.Decode(dm0)) & 31;
                _colorEndpoints[i] = c | (b << 5) | (a << 11) | (f << 16) | (e << 21) | (d << 27);
            }
        }

        private void DecodeColorSelectors()
        {
            _codec.Start(_data, _header.ColorSelectors.Ofs, _header.ColorSelectors.Size);
            var dm = new HuffmanModel();
            Check(_codec.ReceiveModel(dm));
            var num = _header.ColorSelectors.Num;
            _colorSelectors = new uint[num];
            uint s = 0;
            for (var i = 0; i < num; i++)
            {
                for (var j = 0; j < 32; j += 4)
                    s ^= _codec.Decode(dm) << j;
                _colorSelectors[i] = ((s ^ (s << 1)) & 0xAAAAAAAA) | ((s >> 1) & 0x55555555);
            }
        }

        private void DecodeAlphaEndpoints()
        {
            var num = _header.AlphaEndpoints.Num;
            _codec.Start(_data, _header.AlphaEndpoints.Ofs, _header.AlphaEndpoints.Size);
            var dm = new HuffmanModel();
            Check(_codec.ReceiveModel(dm));
            _alphaEndpoints = new ushort[num];
            uint a = 0, b = 0;
            for (var i = 0; i < num; i++)
            {
                a = (a + _codec.Decode(dm)) & 255;
                b = (b + _codec.Decode(dm)) & 255;
                _alphaEndpoints[i] = (ushort)(a | (b << 8));
            }
        }

        private void DecodeAlphaSelectors()
        {
            _codec.Start(_data, _header.AlphaSelectors.Ofs, _header.AlphaSelectors.Size);
            var dm = new HuffmanModel();
            Check(_codec.ReceiveModel(dm));
            _alphaSelectors = new ushort[_header.AlphaSelectors.Num * 3];
            var fromLinear = new byte[64];
            for (var i = 0; i < 64; i++)
                fromLinear[i] = (byte)(Dxt5FromLinear[i & 7] | (Dxt5FromLinear[i >> 3] << 3));
            uint s0Linear = 0, s1Linear = 0;
            for (var i = 0; i < _alphaSelectors.Length;)
            {
                uint s0 = 0, s1 = 0;
                for (var j = 0; j < 24; j += 6)
                {
                    s0Linear ^= _codec.Decode(dm) << j;
                    s0 |= (uint)fromLinear[(s0Linear >> j) & 0x3F] << j;
                }
                for (var j = 0; j < 24; j += 6)
                {
                    s1Linear ^= _codec.Decode(dm) << j;
                    s1 |= (uint)fromLinear[(s1Linear >> j) & 0x3F] << j;
                }
                _alphaSelectors[i++] = (ushort)s0;
                _alphaSelectors[i++] = (ushort)((s0 >> 16) | (s1 << 8));
                _alphaSelectors[i++] = (ushort)(s1 >> 8);
            }
        }

        public void UnpackLevel(int level, Span<byte> dst)
        {
            var start = _header.LevelOffsets[level];
            var next = level + 1 < _header.Levels ? _header.LevelOffsets[level + 1] : _header.DataSize;
            if (next <= start)
                throw new InvalidDataException("Bad crunch level offsets.");
            var w = Math.Max(_header.Width >> level, 1);
            var h = Math.Max(_header.Height >> level, 1);
            var blocksX = (w + 3) >> 2;
            var blocksY = (h + 3) >> 2;
            _codec.Start(_data, start, next - start);
            switch (_header.Format)
            {
                case CrnFormat.Dxt1:
                    UnpackBlocks(dst, blocksX, blocksY, 8, color: true, alpha0: false, alpha1: false);
                    break;
                case CrnFormat.Dxt5 or CrnFormat.Dxt5CCxY or CrnFormat.Dxt5xGBR or CrnFormat.Dxt5AGBR or CrnFormat.Dxt5xGxR:
                    UnpackBlocks(dst, blocksX, blocksY, 16, color: true, alpha0: true, alpha1: false);
                    break;
                case CrnFormat.Dxt5A:
                    UnpackBlocks(dst, blocksX, blocksY, 8, color: false, alpha0: true, alpha1: false);
                    break;
                case CrnFormat.DxnXY or CrnFormat.DxnYX:
                    UnpackBlocks(dst, blocksX, blocksY, 16, color: false, alpha0: true, alpha1: true);
                    break;
                default:
                    throw new NotSupportedException($"Crunch format {_header.Format} isn't supported.");
            }
        }

        // One loop for the DXT variants. Blocks are visited in pairs of rows and columns; each
        // 2x2 group shares a reference code saying whether each block reuses its neighbour's endpoints.
        private void UnpackBlocks(Span<byte> dst, int outWidth, int outHeight, int blockSize, bool color, bool alpha0, bool alpha1)
        {
            var width = (outWidth + 1) & ~1;
            var height = (outHeight + 1) & ~1;
            if (_blockBuffer.Length < width)
                _blockBuffer = new BlockBufferElement[width];
            var pitch = outWidth * blockSize;
            uint colorIndex = 0, alpha0Index = 0, alpha1Index = 0;
            uint numColor = (uint)_colorEndpoints.Length, numAlpha = (uint)_alphaEndpoints.Length;
            byte referenceGroup = 0;

            for (var y = 0; y < height; y++)
            {
                var rowVisible = y < outHeight;
                for (var x = 0; x < width; x++)
                {
                    var visible = rowVisible && x < outWidth;
                    if ((y & 1) == 0 && (x & 1) == 0)
                        referenceGroup = (byte)_codec.Decode(_referenceEncoding);
                    ref var buffer = ref _blockBuffer[x];
                    int endpointReference;
                    if ((y & 1) != 0)
                    {
                        endpointReference = buffer.EndpointReference;
                    }
                    else
                    {
                        endpointReference = referenceGroup & 3;
                        referenceGroup >>= 2;
                        buffer.EndpointReference = (ushort)(referenceGroup & 3);
                        referenceGroup >>= 2;
                    }

                    if (endpointReference == 0)
                    {
                        if (color)
                        {
                            colorIndex += _codec.Decode(_endpointDelta[0]);
                            if (colorIndex >= numColor)
                                colorIndex -= numColor;
                            buffer.ColorEndpointIndex = (ushort)colorIndex;
                        }
                        if (alpha0)
                        {
                            alpha0Index += _codec.Decode(_endpointDelta[1]);
                            if (alpha0Index >= numAlpha)
                                alpha0Index -= numAlpha;
                            buffer.Alpha0EndpointIndex = (ushort)alpha0Index;
                        }
                        if (alpha1)
                        {
                            alpha1Index += _codec.Decode(_endpointDelta[1]);
                            if (alpha1Index >= numAlpha)
                                alpha1Index -= numAlpha;
                            buffer.Alpha1EndpointIndex = (ushort)alpha1Index;
                        }
                    }
                    else if (endpointReference == 1)
                    {
                        buffer.ColorEndpointIndex = (ushort)colorIndex;
                        buffer.Alpha0EndpointIndex = (ushort)alpha0Index;
                        buffer.Alpha1EndpointIndex = (ushort)alpha1Index;
                    }
                    else
                    {
                        colorIndex = buffer.ColorEndpointIndex;
                        alpha0Index = buffer.Alpha0EndpointIndex;
                        alpha1Index = buffer.Alpha1EndpointIndex;
                    }

                    // Selector order matches the encoder: colour first, then alpha.
                    var colorSelector = color ? _codec.Decode(_selectorDelta[0]) : 0;
                    var alpha0Selector = alpha0 ? _codec.Decode(_selectorDelta[1]) : 0;
                    var alpha1Selector = alpha1 ? _codec.Decode(_selectorDelta[1]) : 0;
                    if (!visible)
                        continue;

                    var block = dst.Slice(y * pitch + x * blockSize, blockSize);
                    var at = 0;
                    if (alpha0)
                    {
                        WriteAlphaBlock(block, at, _alphaEndpoints[alpha0Index], alpha0Selector);
                        at += 8;
                    }
                    if (alpha1)
                    {
                        WriteAlphaBlock(block, at, _alphaEndpoints[alpha1Index], alpha1Selector);
                        at += 8;
                    }
                    if (color)
                    {
                        WriteUInt32(block, at, _colorEndpoints[colorIndex]);
                        WriteUInt32(block, at + 4, _colorSelectors[colorSelector]);
                    }
                }
            }
        }

        private void WriteAlphaBlock(Span<byte> block, int at, ushort endpoints, uint selectorIndex)
        {
            var s = _alphaSelectors.AsSpan((int)selectorIndex * 3, 3);
            WriteUInt32(block, at, endpoints | ((uint)s[0] << 16));
            WriteUInt32(block, at + 4, s[1] | ((uint)s[2] << 16));
        }

        private static void WriteUInt32(Span<byte> block, int at, uint value) =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(at, 4), value);
    }
}
