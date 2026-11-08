using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

// Reads real RGBA8 PNG alpha samples so tests don't hard-code the mod's sprite shapes.
internal static class PngGridReader
{
    internal static GridShape Read(byte[] png, int gridSize)
    {
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        if (png[24] != 8 || png[25] != 6 || png[28] != 0) throw new Exception("PNG checks require non-interlaced RGBA8 assets.");
        using var compressed = new MemoryStream();
        for (int offset = 8; offset < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT") compressed.Write(png, offset + 8, length);
            offset += length + 12;
        }
        compressed.Position = 0;
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        byte[] row = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int filter = inflater.ReadByte();
            inflater.ReadExactly(row);
            for (int x = 0; x < stride; x++)
            {
                int left = x >= 4 ? pixels[y * stride + x - 4] : 0;
                int up = y > 0 ? pixels[(y - 1) * stride + x] : 0;
                int upperLeft = y > 0 && x >= 4 ? pixels[(y - 1) * stride + x - 4] : 0;
                int predictor = filter switch
                {
                    0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2,
                    4 => Paeth(left, up, upperLeft), _ => throw new Exception("Unknown PNG filter.")
                };
                pixels[y * stride + x] = unchecked((byte)(row[x] + predictor));
            }
        }
        int columns = width / gridSize, rows = height / gridSize;
        byte[] cells = new byte[columns * rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                int textureY = (rows - 1 - y) * gridSize + gridSize / 2;
                int imageY = height - 1 - textureY;
                int imageX = x * gridSize + gridSize / 2;
                cells[y * columns + x] = pixels[imageY * stride + imageX * 4 + 3] > 0 ? (byte)1 : (byte)0;
            }
        return new GridShapeBuilder().SetData(cells, columns);
    }
    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
