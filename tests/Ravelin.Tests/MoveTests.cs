using Ravelin.Core;

namespace Ravelin.Tests;

public class MoveTests
{
    [Fact]
    public void PacksAndUnpacksItsFields()
    {
        var move = new Move(Squares.E2, Squares.E4, MoveFlags.DoublePawnPush);

        Assert.Equal(Squares.E2, move.From);
        Assert.Equal(Squares.E4, move.To);
        Assert.Equal(MoveFlags.DoublePawnPush, move.Flags);
        Assert.True(move.IsDoublePawnPush);
        Assert.False(move.IsCapture);
        Assert.False(move.IsPromotion);
        Assert.Equal("e2e4", move.ToString());
    }

    /// <summary>Every from/to/flag combination has to survive the 16-bit round trip.</summary>
    [Fact]
    public void EveryEncodingRoundTrips()
    {
        for (int from = 0; from < 64; from++)
        {
            for (int to = 0; to < 64; to++)
            {
                for (int flags = 0; flags < 16; flags++)
                {
                    var move = new Move(from, to, flags);
                    Assert.Equal(from, move.From);
                    Assert.Equal(to, move.To);
                    Assert.Equal(flags, move.Flags);
                }
            }
        }
    }

    [Theory]
    [InlineData(MoveFlags.KnightPromotion, PieceType.Knight, "a7a8n")]
    [InlineData(MoveFlags.BishopPromotion, PieceType.Bishop, "a7a8b")]
    [InlineData(MoveFlags.RookPromotion, PieceType.Rook, "a7a8r")]
    [InlineData(MoveFlags.QueenPromotion, PieceType.Queen, "a7a8q")]
    [InlineData(MoveFlags.QueenPromotionCapture, PieceType.Queen, "a7a8q")]
    public void PromotionFlagsDecodeToTheRightPiece(int flags, PieceType expected, string uci)
    {
        var move = new Move(Squares.A7, Squares.A8, flags);

        Assert.True(move.IsPromotion);
        Assert.Equal(expected, move.PromotionPiece);
        Assert.Equal(uci, move.ToString());
    }

    [Theory]
    [InlineData(MoveFlags.Capture)]
    [InlineData(MoveFlags.EnPassant)]
    [InlineData(MoveFlags.KnightPromotionCapture)]
    [InlineData(MoveFlags.QueenPromotionCapture)]
    public void CaptureFlagsAreRecognised(int flags)
    {
        Assert.True(new Move(Squares.E2, Squares.D3, flags).IsCapture);
    }
}
