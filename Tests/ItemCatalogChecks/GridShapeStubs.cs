using System;

public interface GridShape
{
    int width { get; }
    int height { get; }
    int minX { get; }
    int minY { get; }
    int orientation { get; }
    bool flipped { get; }
    int globalWidth { get; }
    int globalHeight { get; }
    byte Get(int x, int y);
    byte GetLocal(int x, int y);
    GridShape Clone();
}

internal sealed class GridShapeBuilder : GridShape
{
    private byte[] cells = new byte[1];
    public int width { get; private set; } = 1;
    public int height { get; private set; } = 1;
    public int minX { get; private set; }
    public int minY { get; private set; }
    public int orientation { get; private set; }
    public bool flipped { get; private set; }
    public int globalWidth => orientation % 2 == 0 ? width : height;
    public int globalHeight => orientation % 2 == 0 ? height : width;
    internal GridShapeBuilder() { }
    internal GridShapeBuilder(GridShape source)
    {
        width = source.width; height = source.height;
        cells = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) cells[y * width + x] = source.GetLocal(x, y);
        SetTransform(source.minX, source.minY, source.flipped, source.orientation);
    }
    internal GridShapeBuilder SetData(byte[] data, int newWidth)
    {
        cells = (byte[])data.Clone(); width = newWidth; height = data.Length / newWidth;
        return this;
    }
    internal GridShapeBuilder SetPosition(int x, int y) { minX = x; minY = y; return this; }
    internal GridShapeBuilder SetOrientation(int next) { orientation = (next % 4 + 4) % 4; return this; }
    internal GridShapeBuilder SetTransform(int x, int y, bool flip, int rotation)
    {
        SetPosition(x, y); flipped = flip; return SetOrientation(rotation);
    }
    public byte GetLocal(int x, int y) => x >= 0 && y >= 0 && x < width && y < height ? cells[y * width + x] : (byte)0;
    public byte Get(int x, int y)
    {
        x -= minX; y -= minY;
        if (x < 0 || y < 0 || x >= globalWidth || y >= globalHeight) return 0;
        (x, y) = orientation switch
        {
            1 => (width - 1 - y, x),
            2 => (width - 1 - x, height - 1 - y),
            3 => (y, height - 1 - x),
            _ => (x, y)
        };
        if (flipped) x = width - 1 - x;
        return GetLocal(x, y);
    }
    public GridShape Clone() => new GridShapeBuilder(this);
}
