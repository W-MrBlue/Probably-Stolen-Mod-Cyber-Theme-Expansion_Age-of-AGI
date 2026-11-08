using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

// All exact numerical behavior examples run here, never against the editable mod tables.
internal sealed class BehaviorTestFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "aoa-behavior-" + Guid.NewGuid().ToString("N"));

    internal static BehaviorTestFixture Create()
    {
        var fixture = new BehaviorTestFixture();
        try
        {
            Assembly assembly = typeof(BehaviorTestFixture).Assembly;
            const string prefix = "BehaviorFixtures/";
            foreach (string resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix)))
            {
                string path = Path.Combine(fixture.Root, resource.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using var input = assembly.GetManifestResourceStream(resource);
                using var output = File.Create(path);
                input.CopyTo(output);
            }
            foreach (string line in File.ReadLines(Path.Combine(fixture.Root, "sprites.csv")).Skip(1))
            {
                string[] cells = line.Split(',');
                WriteSprite(Path.Combine(fixture.Root, cells[0]), int.Parse(cells[1]), int.Parse(cells[2]), cells[3]);
            }
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    public void Dispose()
    {
        // Root is generated once by this owner, never supplied by a caller.
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }

    // Synthetic white RGBA fixtures preserve only the test shape, with no dependency on mod artwork.
    private static void WriteSprite(string path, int width, int height, string mask)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < width; x++)
            {
                bool occupied = mask.Length == 0 || mask[((height - 1 - y) / 16) * (width / 16) + x / 16] == '1';
                raw.Write(new byte[] { 255, 255, 255, occupied ? (byte)255 : (byte)0 });
            }
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            zlib.Write(raw.ToArray());

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var png = File.Create(path);
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; header[9] = 6;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        byte[] name = Encoding.ASCII.GetBytes(type);
        output.Write(name); output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in name.Concat(data))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        output.Write(length);
    }
}
