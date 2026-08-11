using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Perpetuum.IO
{
    /// <summary>
    /// Reads the 8-bit, non-interlaced RGB PNG used by the P31 gravel layer.
    /// This deliberately small decoder keeps terrain loading cross-platform
    /// without introducing a general-purpose image stack into the game runtime.
    /// </summary>
    internal static class PngRgb24Reader
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static PngRgb24Image Load(string path)
        {
            using var input = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[Signature.Length];
            input.ReadExactly(signature);
            if (!signature.SequenceEqual(Signature))
            {
                throw new InvalidDataException($"Not a PNG file: {path}");
            }

            int width = 0;
            int height = 0;
            bool foundHeader = false;
            using var compressed = new MemoryStream();
            Span<byte> chunkTypeBytes = stackalloc byte[4];

            while (input.Position < input.Length)
            {
                int chunkLength = ReadBigEndianInt32(input);
                if (chunkLength < 0)
                {
                    throw new InvalidDataException("PNG chunk length is invalid.");
                }

                input.ReadExactly(chunkTypeBytes);
                string chunkType = Encoding.ASCII.GetString(chunkTypeBytes);

                switch (chunkType)
                {
                    case "IHDR":
                    {
                        if (chunkLength != 13)
                        {
                            throw new InvalidDataException("PNG header length is invalid.");
                        }

                        Span<byte> header = stackalloc byte[13];
                        input.ReadExactly(header);
                        width = BinaryPrimitives.ReadInt32BigEndian(header);
                        height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(4));

                        const byte requiredBitDepth = 8;
                        const byte requiredColorType = 2;
                        if (width <= 0 || height <= 0 ||
                            header[8] != requiredBitDepth || header[9] != requiredColorType ||
                            header[10] != 0 || header[11] != 0 || header[12] != 0)
                        {
                            throw new InvalidDataException("The gravel layer must be an 8-bit, non-interlaced RGB PNG.");
                        }

                        foundHeader = true;
                        break;
                    }
                    case "IDAT":
                        CopyExactly(input, compressed, chunkLength);
                        break;
                    case "IEND":
                        input.Seek(chunkLength, SeekOrigin.Current);
                        input.Seek(4, SeekOrigin.Current);
                        if (!foundHeader || compressed.Length == 0)
                        {
                            throw new InvalidDataException("PNG is missing its header or image data.");
                        }

                        return Decode(width, height, compressed);
                    default:
                        input.Seek(chunkLength, SeekOrigin.Current);
                        break;
                }

                // Chunk CRC. The runtime data is trusted and integrity is
                // independently pinned, so decoding does not recalculate it.
                input.Seek(4, SeekOrigin.Current);
            }

            throw new InvalidDataException("PNG is missing its end chunk.");
        }

        private static PngRgb24Image Decode(int width, int height, MemoryStream compressed)
        {
            const int bytesPerPixel = 3;
            int stride = checked(width * bytesPerPixel);
            var pixels = new byte[checked(stride * height)];
            var previous = new byte[stride];
            var current = new byte[stride];

            compressed.Position = 0;
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            for (int y = 0; y < height; y++)
            {
                int filter = inflater.ReadByte();
                if (filter < 0)
                {
                    throw new InvalidDataException("PNG pixel data ended unexpectedly.");
                }

                inflater.ReadExactly(current);
                Unfilter(current, previous, bytesPerPixel, filter);
                Buffer.BlockCopy(current, 0, pixels, y * stride, stride);

                byte[] swap = previous;
                previous = current;
                current = swap;
            }

            return new PngRgb24Image(width, height, pixels);
        }

        private static void Unfilter(byte[] current, byte[] previous, int bytesPerPixel, int filter)
        {
            for (int index = 0; index < current.Length; index++)
            {
                int left = index >= bytesPerPixel ? current[index - bytesPerPixel] : 0;
                int above = previous[index];
                int upperLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : 0;

                current[index] = filter switch
                {
                    0 => current[index],
                    1 => unchecked((byte) (current[index] + left)),
                    2 => unchecked((byte) (current[index] + above)),
                    3 => unchecked((byte) (current[index] + ((left + above) / 2))),
                    4 => unchecked((byte) (current[index] + Paeth(left, above, upperLeft))),
                    _ => throw new InvalidDataException($"Unsupported PNG filter: {filter}")
                };
            }
        }

        private static int Paeth(int left, int above, int upperLeft)
        {
            int estimate = left + above - upperLeft;
            int leftDistance = Math.Abs(estimate - left);
            int aboveDistance = Math.Abs(estimate - above);
            int upperLeftDistance = Math.Abs(estimate - upperLeft);

            if (leftDistance <= aboveDistance && leftDistance <= upperLeftDistance)
                return left;
            return aboveDistance <= upperLeftDistance ? above : upperLeft;
        }

        private static int ReadBigEndianInt32(Stream stream)
        {
            Span<byte> bytes = stackalloc byte[4];
            stream.ReadExactly(bytes);
            return BinaryPrimitives.ReadInt32BigEndian(bytes);
        }

        private static void CopyExactly(Stream input, Stream output, int count)
        {
            var buffer = new byte[Math.Min(count, 81920)];
            int remaining = count;
            while (remaining > 0)
            {
                int read = input.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }
    }

    internal sealed class PngRgb24Image
    {
        private readonly byte[] _pixels;

        public PngRgb24Image(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            _pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }

        public float GetBrightness(int x, int y)
        {
            int offset = ((y * Width) + x) * 3;
            byte red = _pixels[offset];
            byte green = _pixels[offset + 1];
            byte blue = _pixels[offset + 2];
            int min = Math.Min(red, Math.Min(green, blue));
            int max = Math.Max(red, Math.Max(green, blue));
            return (max + min) / 510f;
        }
    }
}
