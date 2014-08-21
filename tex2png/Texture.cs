using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace tex2png
{
    enum Format
    {
        Unknown,
        Format4bppPvrtc,
        Format16bppRgba,
        Format32bppAbgr,
        Format32bppArgb,
    }

    static class FormatExtensions
    {
        private static Dictionary<Format, int> bitsPerPixel = new Dictionary<Format, int>
        {
            { Format.Format4bppPvrtc, 4 },
            { Format.Format16bppRgba, 16 },
            { Format.Format32bppAbgr, 32 },
            { Format.Format32bppArgb, 32 },
        };

        public static int GetBitsPerPixel(this Format format)
        {
            return bitsPerPixel[format];
        }
    }

    struct Header
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public Format Format { get; set; }

        public Header(Stream stream) : this()
        {
            var data = new byte[16];
            stream.Read(data, 0, data.Length);

            uint magic = BitConverter.ToUInt32(data, 0);
            ushort version = BitConverter.ToUInt16(data, 4);

            if (magic != 0x20584554) // "TEX "
                throw new InvalidDataException();

            if (version != 7)
                throw new ArgumentException();

            switch (BitConverter.ToUInt16(data, 7))
            {
                case 0x0120:
                    Format = Format.Format32bppAbgr;
                    break;
                case 0x1104: // "BM"
                    Format = Format.Format4bppPvrtc;
                    break;
                case 0x7110: // "LP4"
                    Format = Format.Format16bppRgba;
                    break;
                case 0x6204: // "CM"
                    Format = Format.Format4bppPvrtc;
                    break;
                default:
                    throw new ArgumentException();
            }

            Width = BitConverter.ToUInt16(data, 12);
            Height = data[14] * 8;
        }
    }

    class Texture
    {
        private Header header;
        private byte[] pixelData;

        public int Width { get { return header.Width; } }
        public int Height { get { return header.Height; } }
        public Format Format { get { return header.Format; } }

        public Header Header { get { return header; } }
        public byte[] PixelData { get { return pixelData; } }

        public Texture(Stream stream)
        {
            this.header = new Header(stream);
            this.pixelData = new byte[header.Width * header.Height * header.Format.GetBitsPerPixel() / 8];
            stream.Read(pixelData, 0, pixelData.Length);
        }

        public Texture(int width, int height, Format format, byte[] pixelData)
        {
            this.header = new Header { Width = width, Height = height, Format = format };
            this.pixelData = pixelData;
        }

        public void Convert(Format format)
        {
            var converted = Convert(this, format);
            this.header = converted.header;
            this.pixelData = converted.pixelData;
        }

        public void SaveBitmap(string path)
        {
            if (header.Format != Format.Format32bppArgb)
            {
                Convert(this, Format.Format32bppArgb).SaveBitmap(path);
                return;
            }

            var convertedHandle = GCHandle.Alloc(pixelData, GCHandleType.Pinned);

            try
            {
                using (var bitmap = new Bitmap(
                    header.Width,
                    header.Height,
                    header.Width * header.Format.GetBitsPerPixel() / 8,
                    PixelFormat.Format32bppArgb,
                    convertedHandle.AddrOfPinnedObject()))
                {
                    var fileInfo = new FileInfo(path);
                    fileInfo.Directory.Create();
                    bitmap.Save(fileInfo.FullName);
                }
            }
            finally
            {
                convertedHandle.Free();
            }
        }

        public static Texture Convert(Texture texture, Format format)
        {
            if (format != Format.Format32bppArgb)
                throw new ArgumentException();

            var src = texture.pixelData;
            var dst = new byte[texture.Width * texture.Height * format.GetBitsPerPixel() / 8];

            if (texture.Format == Format.Format4bppPvrtc)
            {
                var result = PVRTDecompressPVRTC(src, 0, texture.Width, texture.Height, dst);

                if (result != src.Length)
                    throw new ArgumentException();

                Convert32bppAbgrTo32bppArgb(dst, dst);
            }
            else if (texture.Format == Format.Format32bppAbgr)
            {
                Convert32bppAbgrTo32bppArgb(src, dst);
            }
            else if (texture.Format == Format.Format16bppRgba)
            {
                Convert16bppRgbaTo32bppArgb(src, dst);
            }
            else
            {
                throw new ArgumentException();
            }

            return new Texture(texture.Width, texture.Height, format, dst);
        }

        private static void Convert32bppAbgrTo32bppArgb(byte[] src, byte[] dst)
        {
            using (var reader = new BinaryReader(new MemoryStream(src)))
            using (var writer = new BinaryWriter(new MemoryStream(dst)))
            for (int i = 0; i < src.Length; i += 4)
            {
                uint abgr = reader.ReadUInt32();
                uint argb = (abgr & 0xff00ff00) |
                    ((abgr & 0x00ff0000) >> 16) |
                    ((abgr & 0x000000ff) << 16);
                writer.Write(argb);
            }
        }

        private static void Convert16bppRgbaTo32bppArgb(byte[] src, byte[] dst)
        {
            using (var reader = new BinaryReader(new MemoryStream(src)))
            using (var writer = new BinaryWriter(new MemoryStream(dst)))
            for (int i = 0; i < src.Length; i += 2)
            {
                uint rgba = reader.ReadUInt16();
                uint argb =
                    ((rgba & 0x000f) << 24) | ((rgba & 0x000f) << 28) |
                    ((rgba & 0x00f0) >> 0) | ((rgba & 0x00f0) >> 4) |
                    ((rgba & 0x0f00) << 0) | ((rgba & 0x0f00) << 4) |
                    ((rgba & 0xf000) << 4) | ((rgba & 0xf000) << 8);
                writer.Write(argb);
            }
        }

        [DllImport("PVRTDecompress.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int PVRTDecompressPVRTC(
            [MarshalAs(UnmanagedType.LPArray)] byte[] pCompressedData,
            int Do2bitMode,
            int XDim,
            int YDim,
            [MarshalAs(UnmanagedType.LPArray)] byte[] pResultImage);
    }
}
