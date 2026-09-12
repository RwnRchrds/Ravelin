using Ravelin.Core;

namespace Ravelin.Tests;

public class ZobristTests
{
    /// <summary>
    /// Walks the move tree comparing the incrementally maintained key against a full recompute at
    /// every node. Any XOR that make/unmake gets wrong shows up immediately, with the position and
    /// move that caused it.
    /// </summary>
    private static void ValidateKeys(ref Position position, int depth)
    {
        Assert.Equal(position.ComputeKey(), position.Key);
        if (depth == 0) return;

        foreach (Move move in MoveGenerator.GenerateLegalMoves(position))
        {
            string before = position.ToFen();
            ulong keyBefore = position.Key;

            Undo undo = position.MakeMove(move);
            Assert.True(position.ComputeKey() == position.Key,
                $"key drifted after {move} from {before}");

            ValidateKeys(ref position, depth - 1);

            position.UnmakeMove(move, undo);
            Assert.True(keyBefore == position.Key, $"key not restored after unmaking {move} from {before}");
        }
    }

    [Theory]
    [InlineData(Position.StartFen, 4)]
    [InlineData(PerftTests.Kiwipete, 3)]
    [InlineData(PerftTests.Position3, 4)]
    [InlineData(PerftTests.Position4, 3)]
    [InlineData(PerftTests.Position5, 3)]
    [InlineData("8/8/8/3pP3/8/8/8/K6k w - d6 0 1", 4)]
    [InlineData("8/P6k/8/8/8/8/8/K7 w - - 0 1", 4)]
    public void IncrementalKeysMatchAFullRecompute(string fen, int depth)
    {
        Position position = Position.FromFen(fen);
        ValidateKeys(ref position, depth);
    }

    [Fact]
    public void DistinctPositionsGetDistinctKeys()
    {
        var keys = new HashSet<ulong>();

        foreach (string fen in new[]
        {
            Position.StartFen,
            PerftTests.Kiwipete,
            PerftTests.Position3,
            PerftTests.Position4,
            PerftTests.Position5,
            PerftTests.Position6,
        })
        {
            Assert.True(keys.Add(Position.FromFen(fen).Key), $"key collision on {fen}");
        }
    }

    [Fact]
    public void SideToMoveChangesTheKey()
    {
        ulong white = Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").Key;
        ulong black = Position.FromFen("4k3/8/8/8/8/8/8/4K3 b - - 0 1").Key;

        Assert.NotEqual(white, black);
    }

    [Fact]
    public void CastlingRightsChangeTheKey()
    {
        ulong all = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1").Key;
        ulong none = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w - - 0 1").Key;

        Assert.NotEqual(all, none);
    }

    /// <summary>The counters are not part of the position's identity, so they must not be hashed.</summary>
    [Fact]
    public void MoveCountersDoNotAffectTheKey()
    {
        ulong fresh = Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").Key;
        ulong later = Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 42 99").Key;

        Assert.Equal(fresh, later);
    }

    [Fact]
    public void TranspositionsReachTheSameKey()
    {
        // 1.Nf3 Nf6 2.Ng1 Ng8 returns to the starting position by a different route.
        Position position = Position.StartingPosition();
        foreach (string uci in new[] { "g1f3", "g8f6", "f3g1", "f6g8" })
            position.MakeMove(MoveNotation.Parse(ref position, uci));

        Assert.Equal(Position.StartingPosition().Key, position.Key);
        Assert.NotEqual(Position.StartingPosition().ToFen(), position.ToFen()); // counters moved on
    }

    /// <summary>
    /// 1.d4 Nf6 2.c4 and 1.c4 Nf6 2.d4 reach the same board but record different en passant
    /// squares. Neither is capturable, so the keys must agree even though the FENs do not.
    /// </summary>
    [Fact]
    public void AnUncapturableEnPassantSquareDoesNotAffectTheKey()
    {
        Position first = Play("d2d4", "g8f6", "c2c4");
        Position second = Play("c2c4", "g8f6", "d2d4");

        Assert.NotEqual(first.ToFen(), second.ToFen());
        Assert.Equal(first.Key, second.Key);

        static Position Play(params string[] moves)
        {
            Position position = Position.StartingPosition();
            foreach (string uci in moves)
                position.MakeMove(MoveNotation.Parse(ref position, uci));
            return position;
        }
    }

    /// <summary>The mirror case: when the capture is actually available, the square must be hashed.</summary>
    [Fact]
    public void ACapturableEnPassantSquareDoesAffectTheKey()
    {
        ulong withCapture = Position.FromFen("8/8/8/3pP3/8/8/8/K6k w - d6 0 1").Key;
        ulong withoutCapture = Position.FromFen("8/8/8/3pP3/8/8/8/K6k w - - 0 1").Key;

        Assert.NotEqual(withCapture, withoutCapture);
    }
}
