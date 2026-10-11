namespace DungeonVisionBot;

// O = player, X = boss, . = observed empty; positions are row-major 0..8.
// A tic-tac-toe strategy model is only a planning aid. In-game turn order,
// floor hazards and player targeting must be confirmed separately.
internal static class WaterwayBoardPolicy
{
    internal sealed record Decision(int Cell, string Reason, int Score);

    private static readonly int[][] Lines =
    {
        new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
        new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
        new[] { 0, 4, 8 }, new[] { 2, 4, 6 }
    };

    // Retained to preserve the v1 regression contract.
    public static int FindWinningCell(string board)
    {
        if (!Valid(board)) return -1;
        foreach (int[] line in Lines)
        {
            if (line.Count(i => board[i] == 'O') == 2 &&
                line.Count(i => board[i] == '.') == 1)
                return line.First(i => board[i] == '.');
        }
        return -1;
    }

    /// <summary>
    /// Determine the next O tile, considering immediately winning, blocking
    /// an X line, and minimax against an optimal X response. Only supplied
    /// safe empty cells are eligible for OUR action. There is never a guessed
    /// move when the board is invalid, terminal, uncertain or all candidate
    /// lines are forced losses. Does not infer whose turn it is.
    /// </summary>
    internal static Decision? ChooseMove(string board, IReadOnlyCollection<int>? safeEmptyCells = null)
    {
        if (!Valid(board) || Winner(board, 'O') || Winner(board, 'X'))
            return null;

        int o = board.Count(c => c == 'O');
        int x = board.Count(c => c == 'X');
        // The game's exact turn order is not verified: accept either starter,
        // but reject obviously inconsistent snapshots.
        if (Math.Abs(o - x) > 1)
            return null;

        var allowed = Enumerable.Range(0, 9)
            .Where(i => board[i] == '.' &&
                (safeEmptyCells is null || safeEmptyCells.Contains(i)))
            .ToArray();
        if (allowed.Length == 0)
            return null;

        // An immediate win is always preferred to merely blocking the boss.
        int immediate = FindWinningCell(board);
        if (immediate >= 0 && allowed.Contains(immediate))
            return new Decision(immediate, "즉시 O 한 줄 완성", 100);

        var cache = new Dictionary<string, int>(StringComparer.Ordinal);
        var ranked = allowed
            .Select(i =>
            {
                char[] next = board.ToCharArray();
                next[i] = 'O';
                int score = Minimax(new string(next), oTurn: false, cache);
                return (Cell: i, Score: score);
            })
            .OrderByDescending(p => p.Score)
            .ThenBy(p => PositionRank(p.Cell))
            .ThenBy(p => p.Cell)
            .ToArray();

        var best = ranked[0];
        // Never pretend an unavoidable loss is a solved mechanic.
        if (best.Score < 0) return null;

        bool block = Lines.Any(line =>
            line.Count(i => board[i] == 'X') == 2 &&
            line.Count(i => board[i] == '.') == 1 &&
            line.Contains(best.Cell));
        string reason = block ? "X 완성 차단" :
            best.Score > 0 ? "O 승리 경로" : "패배 회피 수";

        return new Decision(best.Cell, reason, best.Score);
    }

    private static int Minimax(string board, bool oTurn, Dictionary<string, int> cache)
    {
        if (Winner(board, 'O')) return 10;
        if (Winner(board, 'X')) return -10;
        if (!board.Contains('.')) return 0;

        string key = (oTurn ? "O" : "X") + board;
        if (cache.TryGetValue(key, out int value))
            return value;

        int best = oTurn ? -100 : 100;
        for (int i = 0; i < board.Length; i++)
        {
            if (board[i] != '.') continue;
            char[] next = board.ToCharArray();
            next[i] = oTurn ? 'O' : 'X';
            int score = Minimax(new string(next), !oTurn, cache);
            // Faster wins, slower losses; tie/draw stay at 0.
            score += score > 0 ? -1 : score < 0 ? 1 : 0;
            best = oTurn ? Math.Max(best, score) : Math.Min(best, score);
        }

        cache[key] = best;
        return best;
    }

    private static int PositionRank(int cell) => cell == 4 ? 0 :
        cell is 0 or 2 or 6 or 8 ? 1 : 2;

    private static bool Winner(string board, char owner) =>
        Lines.Any(line => line.All(i => board[i] == owner));

    private static bool Valid(string? board) =>
        board is { Length: 9 } &&
        board.All(c => c is 'O' or 'X' or '.');
}
