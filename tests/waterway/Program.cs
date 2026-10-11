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


{
    var marks = new WaterwayMarkPolicy();
    long t = 10000;

    // Six unique priest glyphs are cached before the player becomes targeted.
    foreach (var (symbol, x, y) in new (string, int, int)[]
    {
        ("fire", 110, 310), ("ice", 220, 340), ("dark", 330, 360),
        ("lightning", 440, 390), ("poison", 550, 410), ("unknown_sixth", 660, 440)
    })
        marks.ObservePriest(symbol, x, y, 0.96, t);

    marks.ObserveBoss("poison", t);
    Assert(marks.RecentPriestCount(t + 200) == 6, "six priests should be tracked");
    passed++;
    Assert(marks.Choose(null, t + 200) is null, "boss cue alone must not trigger");
    passed++;
    Assert(marks.Choose("fire", t + 200) is null, "player glyph must match boss");
    passed++;
    var target = marks.Choose("poison", t + 200);
    Assert(target?.MarkX == 550 && target.MarkY == 410 &&
        target.Symbol == "poison", "correct pre-located priest");
    passed++;

    // After camera movement the cached position is replaced, never fixed.
    marks.ObservePriest("poison", 305, 525, 0.99, t + 300);
    target = marks.Choose("poison", t + 400);
    Assert(target?.MarkX == 305 && target.MarkY == 525, "dynamic relocation");
    passed++;

    Assert(marks.Choose("poison", t + 1700) is null,
        "stale priest screen coordinates must expire");
    passed++;

    marks.ObservePriest("poison", 305, 525, 0.90, t + 1800);
    marks.ObserveBoss("ice", t + 1800);
    Assert(marks.Choose("poison", t + 1800) is null,
        "boss symbol change must invalidate old target");
    passed++;

    marks.ObservePriest("ice", -5, 400, 0.90, t + 1800);
    marks.Reset();
    Assert(marks.RecentPriestCount(t + 1800) == 0 &&
        marks.Choose("ice", t + 1800) is null,
        "new dungeon round must erase stale locations");
    passed++;

    marks.ObserveBoss("fire", t + 3000);
    marks.ObservePriest("fire", 420, 800, 0.94, t + 3000);
    Assert(marks.Choose("fire", t + 3000)?.MarkX == 420,
        "fresh encounter after reset");
    passed++;
}
Console.WriteLine("WaterwayMarkPolicy: 9 PASS (dynamic coordinates, mismatch, TTL, reset)");
