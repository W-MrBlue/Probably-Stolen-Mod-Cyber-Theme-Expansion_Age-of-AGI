namespace AgeOfAgi.Items.Logic.Compiler
{
    internal static class CompilerResidualBridge
    {
        // Determine the opening from the transformed occupied mask, including flips.
        // A C occupies a 3x2 or 2x3 rectangle except the middle cell of one long edge.
        internal static bool TryGetPorts(GridShape shape, out (int X, int Y) first, out (int X, int Y) second)
        {
            first = second = default;
            int x = shape.minX, y = shape.minY;
            int missingX, missingY;
            if (shape.globalWidth == 3 && shape.globalHeight == 2)
            {
                missingX = x + 1;
                missingY = shape.Get(missingX, y) == 0 ? y : y + 1;
                int outsideY = missingY + (missingY == y ? -1 : 1);
                first = (x, outsideY);
                second = (x + 2, outsideY);
            }
            else if (shape.globalWidth == 2 && shape.globalHeight == 3)
            {
                missingY = y + 1;
                missingX = shape.Get(x, missingY) == 0 ? x : x + 1;
                int outsideX = missingX + (missingX == x ? -1 : 1);
                first = (outsideX, y);
                second = (outsideX, y + 2);
            }
            else return false;

            for (int row = y; row < y + shape.globalHeight; row++)
                for (int column = x; column < x + shape.globalWidth; column++)
                    if (shape.Get(column, row) != (column == missingX && row == missingY ? 0 : 1)) return false;
            return true;
        }

        internal static bool Connects(GridShape bridge, GridShape encoder, GridShape decoder)
        {
            if (!TryGetPorts(bridge, out var first, out var second)) return false;
            return (Contains(encoder, first) && Contains(decoder, second)) ||
                (Contains(decoder, first) && Contains(encoder, second));
        }

        private static bool Contains(GridShape shape, (int X, int Y) point) =>
            point.X >= shape.minX && point.X < shape.minX + shape.globalWidth &&
            point.Y >= shape.minY && point.Y < shape.minY + shape.globalHeight && shape.Get(point.X, point.Y) == 1;
    }
}
