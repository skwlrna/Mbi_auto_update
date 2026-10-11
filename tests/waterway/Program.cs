using DungeonVisionBot;

static void Expect(string board, int expected)
{
    int actual = WaterwayBoardPolicy.FindWinningCell(board);
    if (actual != expected)
        throw new Exception($"board={board}: expected {expected}, got {actual}");
}

Expect("OO.XX....", 2);
Expect("O..O..X..", -1);
Expect("O..O..O..", -1);
Expect("O..O.....", 6);
Expect("O...O....", 8);
Expect("..O.O....", 6);
Expect("OOX......", -1);
Expect(".........", -1);
Expect("OO?XX....", -1);
Expect("OO.", -1);
Console.WriteLine("WaterwayBoardPolicy: 10 PASS");
