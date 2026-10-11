using DungeonVisionBot;

int passed = 0;

static void Assert(bool ok, string reason)
{
    if (!ok) throw new Exception(reason);
}

static void ExpectWinning(string board, int expected)
{
    int actual = WaterwayBoardPolicy.FindWinningCell(board);
    Assert(actual == expected, $"legacy {board}: expected {expected}, got {actual}");
}

static void ExpectMove(string board, int? expected, int[]? allowed = null)
{
    var found = WaterwayBoardPolicy.ChooseMove(board, allowed);
    Assert(found?.Cell == expected,
        $"strategy {board}: expected {expected?.ToString() ?? "no move"}, got {found?.Cell.ToString() ?? "no move"}");
    if (found is not null)
        Assert(board[found.Cell] == '.', "planner selected nonempty tile");
}

foreach (var (board, winner) in new[]
{
    ("OO.XX....", 2), ("O..O..X..", -1), ("O..O..O..", -1),
    ("O..O.....", 6), ("O...O....", 8), ("..O.O....", 6),
    ("OOX......", -1), (".........", -1), ("OO?XX....", -1),
    ("OO.", -1)
})
{
    ExpectWinning(board, winner);
    passed++;
}

foreach (var (board, target, allowed) in new (string, int?, int[]?)[]
{
    (".........", 4, null),       // empty board: prefer center
    ("XX..O....", 2, null),       // must block immediate X victory
    ("X.X.O....", 1, null),       // second variant of imminent X
    ("OO.XX....", 2, null),      // winning now is better than blocking
    ("XX.OO....", 5, null),      // complete middle row
    ("O...X....", 2, null),      // safe minimax corner
    (".........", 0, new[] {0, 2}), // avoid unsafe center
    (".........", null, Array.Empty<int>()), // no verified safe tile
    ("OOOXX....", null, null),   // O game already over
    ("XXXOO....", null, null),   // X game already over
    ("XXOOOXXOO", null, null),   // full
    ("OO.......", null, null),   // impossible uneven snapshot
    ("OO?XX....", null, null),   // uncertainty: reject
    ("........", null, null),    // not 9 tiles
})
{
    ExpectMove(board, target, allowed);
    passed++;
}

// Every legal, ongoing and plausible observation must produce either a safe
// empty cell or no move. Never choose occupied/outside a restricted safe set.
char[] alphabet = { 'O', 'X', '.' };
for (int raw = 0; raw < 19683; raw++)
{
    int number = raw;
    var board = new char[9];
    for (int i = 0; i < 9; i++)
    {
        board[i] = alphabet[number % 3];
        number /= 3;
    }
    string position = new(board);
    var found = WaterwayBoardPolicy.ChooseMove(position);
    if (found is not null)
        Assert(found.Cell is >= 0 and < 9 && board[found.Cell] == '.',
            "exhaustive board produced invalid tile: " + position);
}
passed++;
Console.WriteLine($"WaterwayBoardPolicy: {passed} PASS + 19,683 board safety snapshots");
