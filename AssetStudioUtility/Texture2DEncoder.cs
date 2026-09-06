using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetStudio
{
    /// <summary>
    /// Encodes a decoded (Bgra32) image back into raw Unity Texture2D pixel data.
    /// This mirrors the decoding logic in <see cref="Texture2DConverter"/> for the handful of
    /// formats it supports, plus a straightforward (non-optimal but fully standard) BC1/BC3
    /// block compressor for DXT1/DXT5.
    ///
    /// Only formats listed in <see cref="SupportedFormats"/> can be encoded. Anything else
    /// (ETC/ETC2, PVRTC, ASTC, BC4-7, crunched formats, float formats, etc.) is intentionally
    /// not supported - writing a correct encoder for those needs a lot more code than is
    /// reasonable to bolt on here, and a wrong encoder is worse than a clear "not supported".
    /// </summary>
    public static class Texture2DEncoder
    {
        public static readonly TextureFormat[] SupportedFormats =
        {
            TextureFormat.Alpha8,
            TextureFormat.R8,
            TextureFormat.RG16,
            TextureFormat.RGB24,
            TextureFormat.RGBA32,
            TextureFormat.ARGB32,
            TextureFormat.BGRA32,
            TextureFormat.RGB565,
            TextureFormat.ARGB4444,
            TextureFormat.RGBA4444,
            TextureFormat.DXT1,
            TextureFormat.DXT5,
        };

        public static bool IsFormatSupported(TextureFormat format) => Array.IndexOf(SupportedFormats, format) >= 0;

        /// <summary>
        /// Encodes <paramref name="image"/> (already sized to width x height, already flipped
        /// to Unity's bottom-up raw layout by the caller) into raw bytes for <paramref name="format"/>.
        /// </summary>
        public static bool TryEncode(Image<Bgra32> image, TextureFormat format, out byte[] data, out string error)
        {
            data = null;
            error = null;

            if (!IsFormatSupported(format))
            {
                error = $"Encoding to {format} isn't supported. Supported formats: {string.Join(", ", (IEnumerable<TextureFormat>)SupportedFormats)}.";
                return false;
            }

            var width = image.Width;
            var height = image.Height;

            // Grab a flat Bgra32 pixel buffer (row-major, top row first in the buffer -
            // the caller is responsible for flipping beforehand so row 0 here is Unity's bottom row).
            var pixels = new Bgra32[width * height];
            image.CopyPixelDataTo(pixels);

            switch (format)
            {
                case TextureFormat.Alpha8:
                    data = EncodeAlpha8(pixels);
                    break;
                case TextureFormat.R8:
                    data = EncodeR8(pixels);
                    break;
                case TextureFormat.RG16:
                    data = EncodeRG16(pixels);
                    break;
                case TextureFormat.RGB24:
                    data = EncodeRGB24(pixels);
                    break;
                case TextureFormat.RGBA32:
                    data = EncodeRGBA32(pixels);
                    break;
                case TextureFormat.ARGB32:
                    data = EncodeARGB32(pixels);
                    break;
                case TextureFormat.BGRA32:
                    data = EncodeBGRA32(pixels);
                    break;
                case TextureFormat.RGB565:
                    data = EncodeRGB565(pixels);
                    break;
                case TextureFormat.ARGB4444:
                    data = EncodeARGB4444(pixels);
                    break;
                case TextureFormat.RGBA4444:
                    data = EncodeRGBA4444(pixels);
                    break;
                case TextureFormat.DXT1:
                    data = EncodeDXT1(pixels, width, height);
                    break;
                case TextureFormat.DXT5:
                    data = EncodeDXT5(pixels, width, height);
                    break;
                default:
                    error = $"Encoding to {format} isn't implemented.";
                    return false;
            }

            return true;
        }

        // ---------------------------------------------------------------
        // Uncompressed formats - exact inverses of the matching DecodeXxx
        // methods in Texture2DConverter.cs (which decode into Bgra32).
        // ---------------------------------------------------------------

        private static byte[] EncodeAlpha8(Bgra32[] pixels)
        {
            var data = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                data[i] = pixels[i].A;
            }
            return data;
        }

        private static byte[] EncodeR8(Bgra32[] pixels)
        {
            var data = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                data[i] = pixels[i].R;
            }
            return data;
        }

        private static byte[] EncodeRG16(Bgra32[] pixels)
        {
            var data = new byte[pixels.Length * 2];
            for (var i = 0; i < pixels.Length; i++)
            {
                data[i * 2] = pixels[i].R;
                data[i * 2 + 1] = pixels[i].G;
            }
            return data;
        }

        private static byte[] EncodeRGB24(Bgra32[] pixels)
        {
            var data = new byte[pixels.Length * 3];
            for (var i = 0; i < pixels.Length; i++)
            {
                data[i * 3] = pixels[i].R;
                data[i * 3 + 1] = pixels[i].G;
                data[i * 3 + 2] = pixels[i].B;
            }
            return data;
        }

        private static byte[] EncodeRGBA32(Bgra32[] pixels)
        {
            var data = new byte[pixels.Length * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                data[i * 4] = p.R;
                data[i * 4 + 1] = p.G;
                data[i * 4 + 2] = p.B;
                data[i * 4 + 3] = p.A;
            }
            return data;
        }

        private static byte[] EncodeARGB32(Bgra32[] pixels)
        {
            var data = new byte[pixels.Length * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                data[i * 4] = p.A;
                data[i * 4 + 1] = p.R;
                data[i * 4 + 2] = p.G;
                data[i * 4 + 3] = p.B;
            }
            return data;
        }

        private static byte[] EncodeBGRA32(Bgra32[] pixels)
        {
            var data = new byte[pixels.Length * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                data[i * 4] = p.B;
                data[i * 4 + 1] = p.G;
                data[i * 4 + 2] = p.R;
                data[i * 4 + 3] = p.A;
            }
            return data;
        }

        private static byte[] EncodeRGB565(Bgra32[] pixels)
        {
            var data = new byte[pixels.Length * 2];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var value = Pack565(p.R, p.G, p.B);
                data[i * 2] = (byte)(value & 0xFF);
                data[i * 2 + 1] = (byte)(value >> 8);
            }
            return data;
        }

        private static byte[] EncodeARGB4444(Bgra32[] pixels)
        {
            var data = new byte[pixels.Length * 2];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                // bit layout (LE short): bits0-3=B, bits4-7=G, bits8-11=R, bits12-15=A
                var value = (ushort)((p.B >> 4) | ((p.G >> 4) << 4) | ((p.R >> 4) << 8) | ((p.A >> 4) << 12));
                data[i * 2] = (byte)(value & 0xFF);
                data[i * 2 + 1] = (byte)(value >> 8);
            }
            return data;
        }

        private static byte[] EncodeRGBA4444(Bgra32[] pixels)
        {
            var data = new byte[pixels.Length * 2];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                // bit layout (LE short): bits0-3=A, bits4-7=B, bits8-11=G, bits12-15=R
                var value = (ushort)((p.A >> 4) | ((p.B >> 4) << 4) | ((p.G >> 4) << 8) | ((p.R >> 4) << 12));
                data[i * 2] = (byte)(value & 0xFF);
                data[i * 2 + 1] = (byte)(value >> 8);
            }
            return data;
        }

        private static ushort Pack565(byte r, byte g, byte b)
        {
            var r5 = r >> 3;
            var g6 = g >> 2;
            var b5 = b >> 3;
            return (ushort)((r5 << 11) | (g6 << 5) | b5);
        }

        private static void Unpack565(ushort value, out byte r, out byte g, out byte b)
        {
            b = (byte)((value << 3) | (value >> 2 & 7));
            g = (byte)((value >> 3 & 0xfc) | (value >> 9 & 3));
            r = (byte)((value >> 8 & 0xf8) | (value >> 13));
        }

        // ---------------------------------------------------------------
        // DXT1 (BC1) / DXT5 (BC3) - simple range-fit block compressor.
        // Not as high quality as a proper optimal-fit encoder, but produces
        // spec-correct, widely compatible output.
        // ---------------------------------------------------------------

        private static byte[] EncodeDXT1(Bgra32[] pixels, int width, int height)
        {
            var blocksX = (width + 3) / 4;
            var blocksY = (height + 3) / 4;
            var data = new byte[blocksX * blocksY * 8];
            var block = new Bgra32[16];
            var offset = 0;

            for (var by = 0; by < blocksY; by++)
            {
                for (var bx = 0; bx < blocksX; bx++)
                {
                    GatherBlock(pixels, width, height, bx, by, block);
                    EncodeColorBlock(block, data, offset, forceOpaque: true);
                    offset += 8;
                }
            }
            return data;
        }

        private static byte[] EncodeDXT5(Bgra32[] pixels, int width, int height)
        {
            var blocksX = (width + 3) / 4;
            var blocksY = (height + 3) / 4;
            var data = new byte[blocksX * blocksY * 16];
            var block = new Bgra32[16];
            var offset = 0;

            for (var by = 0; by < blocksY; by++)
            {
                for (var bx = 0; bx < blocksX; bx++)
                {
                    GatherBlock(pixels, width, height, bx, by, block);
                    EncodeAlphaBlock(block, data, offset);
                    EncodeColorBlock(block, data, offset + 8, forceOpaque: true);
                    offset += 16;
                }
            }
            return data;
        }

        private static void GatherBlock(Bgra32[] pixels, int width, int height, int bx, int by, Bgra32[] block)
        {
            for (var y = 0; y < 4; y++)
            {
                var sy = Math.Min(by * 4 + y, height - 1);
                for (var x = 0; x < 4; x++)
                {
                    var sx = Math.Min(bx * 4 + x, width - 1);
                    block[y * 4 + x] = pixels[sy * width + sx];
                }
            }
        }

        private static void EncodeColorBlock(Bgra32[] block, byte[] output, int offset, bool forceOpaque)
        {
            // Bounding-box range fit: use the min/max corner of the block's RGB cube as the
            // two endpoint colors. Simple, fast, and good enough for a "replace this texture" tool.
            byte minR = 255, minG = 255, minB = 255;
            byte maxR = 0, maxG = 0, maxB = 0;
            for (var i = 0; i < 16; i++)
            {
                var p = block[i];
                if (p.R < minR) minR = p.R;
                if (p.G < minG) minG = p.G;
                if (p.B < minB) minB = p.B;
                if (p.R > maxR) maxR = p.R;
                if (p.G > maxG) maxG = p.G;
                if (p.B > maxB) maxB = p.B;
            }

            var c0 = Pack565(maxR, maxG, maxB);
            var c1 = Pack565(minR, minG, minB);

            if (forceOpaque && c0 <= c1)
            {
                // Force the 4-color interpolation mode (c0 > c1) rather than the
                // punch-through-alpha 3-color mode, since we're not using 1-bit alpha here.
                if (c1 == 0)
                {
                    c0 = 1;
                }
                else
                {
                    c0 = c1;
                    c1 = (ushort)(c1 - 1);
                }
            }

            Unpack565(c0, out var r0, out var g0, out var b0);
            Unpack565(c1, out var r1, out var g1, out var b1);

            // Palette: 0=c0, 1=c1, 2=2/3*c0+1/3*c1, 3=1/3*c0+2/3*c1
            Span<int> palR = stackalloc int[4] { r0, r1, (2 * r0 + r1) / 3, (r0 + 2 * r1) / 3 };
            Span<int> palG = stackalloc int[4] { g0, g1, (2 * g0 + g1) / 3, (g0 + 2 * g1) / 3 };
            Span<int> palB = stackalloc int[4] { b0, b1, (2 * b0 + b1) / 3, (b0 + 2 * b1) / 3 };

            uint indices = 0;
            for (var i = 0; i < 16; i++)
            {
                var p = block[i];
                var best = 0;
                var bestDist = int.MaxValue;
                for (var j = 0; j < 4; j++)
                {
                    var dr = p.R - palR[j];
                    var dg = p.G - palG[j];
                    var db = p.B - palB[j];
                    var dist = dr * dr + dg * dg + db * db;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = j;
                    }
                }
                indices |= (uint)(best << (i * 2));
            }

            output[offset] = (byte)(c0 & 0xFF);
            output[offset + 1] = (byte)(c0 >> 8);
            output[offset + 2] = (byte)(c1 & 0xFF);
            output[offset + 3] = (byte)(c1 >> 8);
            output[offset + 4] = (byte)(indices & 0xFF);
            output[offset + 5] = (byte)((indices >> 8) & 0xFF);
            output[offset + 6] = (byte)((indices >> 16) & 0xFF);
            output[offset + 7] = (byte)((indices >> 24) & 0xFF);
        }

        private static void EncodeAlphaBlock(Bgra32[] block, byte[] output, int offset)
        {
            byte a0 = 0, a1 = 255;
            for (var i = 0; i < 16; i++)
            {
                var a = block[i].A;
                if (a > a0) a0 = a;
                if (a < a1) a1 = a;
            }

            if (a0 == a1)
            {
                // Degenerate (flat alpha) block - nudge apart so the 8-value interpolation mode stays valid.
                if (a0 == 255) a1 = 254;
                else a0++;
            }

            Span<int> palette = stackalloc int[8];
            palette[0] = a0;
            palette[1] = a1;
            for (var i = 1; i <= 6; i++)
            {
                palette[1 + i] = ((7 - i) * a0 + i * a1) / 7;
            }

            output[offset] = a0;
            output[offset + 1] = a1;

            ulong indices = 0;
            for (var i = 0; i < 16; i++)
            {
                var a = block[i].A;
                var best = 0;
                var bestDist = int.MaxValue;
                for (var j = 0; j < 8; j++)
                {
                    var dist = Math.Abs(a - palette[j]);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = j;
                    }
                }
                indices |= (ulong)best << (i * 3);
            }

            for (var i = 0; i < 6; i++)
            {
                output[offset + 2 + i] = (byte)((indices >> (i * 8)) & 0xFF);
            }
        }
    }
}
