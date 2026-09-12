using Ravelin.Core;

namespace Ravelin.Tests;

/// <summary>
/// Deeper perft runs than the default gate. These take minutes rather than seconds, so they are
/// excluded from a plain <c>dotnet test</c> and run on demand:
/// <code>
/// dotnet test -c Release -p:DeepPerft=true
/// </code>
/// Run them after any change to move generation, make/unmake, or the attack tables.
/// </summary>
[Trait("Category", "Slow")]
public class DeepPerftTests
{
    [Theory]
    [InlineData(Position.StartFen, 6, 119_060_324L)]
    [InlineData(Position.StartFen, 7, 3_195_901_860L)]
    [InlineData(PerftTests.Kiwipete, 5, 193_690_690L)]
    [InlineData(PerftTests.Position3, 7, 178_633_661L)]
    [InlineData(PerftTests.Position4, 5, 15_833_292L)]
    [InlineData(PerftTests.Position4, 6, 706_045_033L)]
    [InlineData(PerftTests.Position5, 5, 89_941_194L)]
    [InlineData(PerftTests.Position6, 5, 164_075_551L)]
    public void MatchesReferenceNodeCounts(string fen, int depth, long expected)
    {
        Assert.Equal(expected, Perft.Run(fen, depth));
    }
}
