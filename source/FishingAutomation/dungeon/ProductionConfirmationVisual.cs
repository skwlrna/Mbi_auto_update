namespace DungeonVisionBot;

// Shared production evidence: green scenery is not a confirmation control.
// Only a bounded, filled horizontal component can be a candidate button.
// Callers still verify the expected screen/CLI state before any input.
internal static class ProductionConfirmationVisual
{
    internal static bool HasBoundedGreenButton(Bitmap frame, int minimumY)
    {
        if (frame.Width != 800 || frame.Height != 1000) return false;
        const int step = 3, left = 100, right = 700, bottom = 997;
        int rows = (bottom - minimumY) / step;
        const int columns = (right - left) / step;
        var green = new bool[rows * columns];
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            Color p = frame.GetPixel(left + column * step, minimumY + row * step);
            green[row * columns + column] =
                p.G >= 90 && p.G >= p.R + 40 && p.G >= p.B + 15;
        }

        var pending = new Queue<int>();
        for (int start = 0; start < green.Length; start++)
        {
            if (!green[start]) continue;
            green[start] = false;
            pending.Enqueue(start);
            int minX = columns, maxX = 0, minY = rows, maxY = 0, count = 0;
            while (pending.Count > 0)
            {
                int cell = pending.Dequeue(), x = cell % columns, y = cell / columns;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                count++;
                void Visit(int next)
                {
                    if (!green[next]) return;
                    green[next] = false;
                    pending.Enqueue(next);
                }
                if (x > 0) Visit(cell - 1);
                if (x + 1 < columns) Visit(cell + 1);
                if (y > 0) Visit(cell - columns);
                if (y + 1 < rows) Visit(cell + columns);
            }
            int width = (maxX - minX + 1) * step;
            int height = (maxY - minY + 1) * step;
            if (minX > 0 && maxX < columns - 1 && minY > 0 && maxY < rows - 1 &&
                width is >= 110 and <= 480 && height is >= 22 and <= 100 &&
                width >= height * 2 &&
                count * 100 >= (maxX - minX + 1) * (maxY - minY + 1) * 55)
                return true;
        }
        return false;
    }
}
