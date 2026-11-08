namespace AgeOfAgi.Items
{
    internal static class ItemShapeHelper
    {
        internal static bool SameCells(GridShape first, GridShape second)
        {
            if (first.globalWidth != second.globalWidth || first.globalHeight != second.globalHeight) return false;
            for (int y = 0; y < first.globalHeight; y++)
            {
                for (int x = 0; x < first.globalWidth; x++)
                {
                    if (first.Get(first.minX + x, first.minY + y) != second.Get(second.minX + x, second.minY + y)) return false;
                }
            }
            return true;
        }
    }
}
