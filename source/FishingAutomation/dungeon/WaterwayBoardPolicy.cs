namespace DungeonVisionBot;

// Board positions are row-major 0..8.  O = friendly, X = blocked/opponent,
// . = explicitly observed empty.  Unknown cells must never be guessed empty.
internal static class WaterwayBoardPolicy
{
    private static readonly int[][] Lines =
    {
        new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
        new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
        new[] { 0, 4, 8 }, new[] { 2, 4, 6 }
    };

    // Only commit an immediately completing O-O-.  No speculative moves.
    public static int FindWinningCell(string board)
    {
        if (board is null || board.Length != 9 ||
            board.Any(c => c != 'O' && c != 'X' && c != '.'))
            return -1;

        foreach (int[] line in Lines)
        {
            int o = line.Count(i => board[i] == 'O');
            int empty = line.Count(i => board[i] == '.');
            if (o == 2 && empty == 1)
                return line.First(i => board[i] == '.');
        }

        return -1;
    }
}
