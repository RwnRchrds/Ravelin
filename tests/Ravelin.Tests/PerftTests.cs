using Ravelin.Core;

namespace Ravelin.Tests;

/// <summary>
/// Reference perft values from the Chess Programming Wiki's standard test positions.
/// These are the correctness gate for move generation; nothing should be layered on top
/// until every one of them passes.
/// </summary>
public class PerftTests
{
    public const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";

    /// <summary>CPW position 3: sparse, rook-endgame-like, exercises en passant discovered checks.</summary>
    public const string Position3 = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";

    /// <summary>CPW position 4: heavy promotion and pin traffic.</summary>
    public const string Position4 = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1";

    /// <summary>CPW position 5: catches castling-rights and promotion bugs.</summary>
    public const string Position5 = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";

    /// <summary>CPW position 6: a dense middlegame, good general-purpose coverage.</summary>
    public const string Position6 = "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10";

    [Theory]
    [InlineData(1, 20L)]
    [InlineData(2, 400L)]
    [InlineData(3, 8_902L)]
    [InlineData(4, 197_281L)]
    [InlineData(5, 4_865_609L)]
    public void StartingPosition(int depth, long expected)
    {
        Assert.Equal(expected, Perft.Run(Position.StartFen, depth));
    }

    [Theory]
    [InlineData(1, 48L)]
    [InlineData(2, 2_039L)]
    [InlineData(3, 97_862L)]
    [InlineData(4, 4_085_603L)]
    public void KiwipetePosition(int depth, long expected)
    {
        Assert.Equal(expected, Perft.Run(Kiwipete, depth));
    }

    [Theory]
    [InlineData(1, 14L)]
    [InlineData(2, 191L)]
    [InlineData(3, 2_812L)]
    [InlineData(4, 43_238L)]
    [InlineData(5, 674_624L)]
    [InlineData(6, 11_030_083L)]
    public void CpwPosition3(int depth, long expected)
    {
        Assert.Equal(expected, Perft.Run(Position3, depth));
    }

    [Theory]
    [InlineData(1, 6L)]
    [InlineData(2, 264L)]
    [InlineData(3, 9_467L)]
    [InlineData(4, 422_333L)]
    public void CpwPosition4(int depth, long expected)
    {
        Assert.Equal(expected, Perft.Run(Position4, depth));
    }

    [Theory]
    [InlineData(1, 44L)]
    [InlineData(2, 1_486L)]
    [InlineData(3, 62_379L)]
    [InlineData(4, 2_103_487L)]
    public void CpwPosition5(int depth, long expected)
    {
        Assert.Equal(expected, Perft.Run(Position5, depth));
    }

    [Theory]
    [InlineData(1, 46L)]
    [InlineData(2, 2_079L)]
    [InlineData(3, 89_890L)]
    [InlineData(4, 3_894_594L)]
    public void CpwPosition6(int depth, long expected)
    {
        Assert.Equal(expected, Perft.Run(Position6, depth));
    }

    /// <summary>Position 4 mirrored: same tree, but generated from Black's side of the board.</summary>
    [Theory]
    [InlineData(1, 6L)]
    [InlineData(2, 264L)]
    [InlineData(3, 9_467L)]
    [InlineData(4, 422_333L)]
    public void CpwPosition4Mirrored(int depth, long expected)
    {
        Assert.Equal(expected, Perft.Run("r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1", depth));
    }

    /// <summary>
    /// Perft leaves the position untouched, which is the invariant make/unmake has to hold.
    /// A stale bitboard or a dropped castling right would show up here before it corrupts a search.
    /// </summary>
    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(Kiwipete)]
    [InlineData(Position3)]
    [InlineData(Position4)]
    [InlineData(Position5)]
    public void PerftRestoresThePositionExactly(string fen)
    {
        Position position = Position.FromFen(fen);
        Perft.Run(ref position, 3);
        Assert.Equal(fen, position.ToFen());
    }
}
