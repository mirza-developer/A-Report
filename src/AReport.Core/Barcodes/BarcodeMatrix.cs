namespace AReport.Barcodes;

/// <summary>A barcode as a grid of dark/light modules. One-dimensional barcodes have a single row.</summary>
public sealed class BarcodeMatrix(int columns, int rows)
{
    private readonly bool[] _modules = new bool[columns * rows];

    public int Columns { get; } = columns;
    public int Rows { get; } = rows;

    public bool this[int column, int row]
    {
        get => _modules[row * Columns + column];
        set => _modules[row * Columns + column] = value;
    }

    /// <summary>Horizontal runs of dark modules as (row, startColumn, length): what a renderer draws as rectangles.</summary>
    public IEnumerable<(int Row, int Start, int Length)> DarkRuns()
    {
        for (int r = 0; r < Rows; r++)
        {
            int c = 0;
            while (c < Columns)
            {
                if (!this[c, r]) { c++; continue; }
                int start = c;
                while (c < Columns && this[c, r]) c++;
                yield return (r, start, c - start);
            }
        }
    }
}
