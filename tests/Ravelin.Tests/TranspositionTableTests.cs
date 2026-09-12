using Ravelin.Core;

namespace Ravelin.Tests;

public class TranspositionTableTests
{
    [Fact]
    public void StoredEntriesComeBack()
    {
        var table = new TranspositionTable(1);
        var move = new Move(Squares.E2, Squares.E4, MoveFlags.DoublePawnPush);

        table.Store(key: 0xDEADBEEF, ply: 0, depth: 7, score: 42, Bound.Exact, move);

        Assert.True(table.TryProbe(0xDEADBEEF, 0, out int depth, out int score, out Bound bound, out Move stored));
        Assert.Equal(7, depth);
        Assert.Equal(42, score);
        Assert.Equal(Bound.Exact, bound);
        Assert.Equal(move, stored);
    }

    [Fact]
    public void AnUnknownKeyMisses()
    {
        var table = new TranspositionTable(1);

        Assert.False(table.TryProbe(12345, 0, out _, out _, out Bound bound, out _));
        Assert.Equal(Bound.None, bound);
    }

    [Fact]
    public void ClearEmptiesTheTable()
    {
        var table = new TranspositionTable(1);
        table.Store(99, 0, 3, 10, Bound.Exact, Move.Null);

        table.Clear();

        Assert.False(table.TryProbe(99, 0, out _, out _, out _, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(64)]
    public void CapacityIsAPowerOfTwoWithinTheBudget(int megabytes)
    {
        var table = new TranspositionTable(megabytes);

        Assert.True(table.Capacity > 0);
        Assert.Equal(0, table.Capacity & (table.Capacity - 1));
        // 16 bytes an entry, so the table must not exceed the megabytes asked for.
        Assert.True((long)table.Capacity * 16 <= (long)megabytes * 1024 * 1024);
    }

    [Fact]
    public void RequestedSizeIsClampedToTheSupportedRange()
    {
        Assert.True(new TranspositionTable(0).Capacity > 0);
        Assert.True(new TranspositionTable(-5).Capacity > 0);
    }

    /// <summary>
    /// A mate score means "mate in N plies from here", so it cannot be stored verbatim: a node at a
    /// different depth reading the same entry would read the wrong distance. Storing makes the
    /// distance absolute and probing makes it relative again. Getting this backwards is the
    /// classic transposition table bug, and it shows up as nonsensical mate announcements.
    /// </summary>
    [Fact]
    public void MateScoresAreRebasedOntoTheProbingPly()
    {
        var table = new TranspositionTable(1);

        // Mate found six plies from the root, stored by a node five plies from the root:
        // one ply away from the storing node.
        int scoreAtStoringNode = Search.MateScore - 6;
        table.Store(key: 7, ply: 5, depth: 4, scoreAtStoringNode, Bound.Exact, Move.Null);

        // Read back at the same ply, it must be unchanged.
        Assert.True(table.TryProbe(7, 5, out _, out int samePly, out _, out _));
        Assert.Equal(scoreAtStoringNode, samePly);

        // Read back ten plies from the root, the same mate is ten plies further away.
        Assert.True(table.TryProbe(7, 10, out _, out int deeper, out _, out _));
        Assert.Equal(Search.MateScore - 11, deeper);
    }

    [Fact]
    public void BeingMatedIsRebasedTheOtherWay()
    {
        var table = new TranspositionTable(1);

        int scoreAtStoringNode = -Search.MateScore + 6;
        table.Store(key: 8, ply: 5, depth: 4, scoreAtStoringNode, Bound.Exact, Move.Null);

        Assert.True(table.TryProbe(8, 10, out _, out int deeper, out _, out _));
        Assert.Equal(-Search.MateScore + 11, deeper);
    }

    [Fact]
    public void OrdinaryScoresAreNotRebased()
    {
        var table = new TranspositionTable(1);
        table.Store(key: 9, ply: 3, depth: 4, score: -250, Bound.Upper, Move.Null);

        Assert.True(table.TryProbe(9, 40, out _, out int score, out _, out _));
        Assert.Equal(-250, score);
    }

    [Fact]
    public void ADeeperEntryIsNotReplacedByAShallowerOneFromTheSameSearch()
    {
        var table = new TranspositionTable(1);
        table.Store(key: 100, ply: 0, depth: 10, score: 500, Bound.Exact, Move.Null);

        table.Store(key: 100, ply: 0, depth: 2, score: -500, Bound.Exact, Move.Null);

        Assert.True(table.TryProbe(100, 0, out int depth, out int score, out _, out _));
        // Same position, so the newer search still wins: it knows at least as much.
        Assert.Equal(2, depth);
        Assert.Equal(-500, score);
    }

    [Fact]
    public void EntriesFromAnEarlierSearchAreReplaceable()
    {
        var table = new TranspositionTable(1);
        table.Store(key: 200, ply: 0, depth: 20, score: 1, Bound.Exact, Move.Null);

        table.NewSearch();
        // A different key landing on the same slot: with the old entry stale, the shallow
        // result from the current search takes the slot.
        table.Store(key: 200, ply: 0, depth: 1, score: 2, Bound.Exact, Move.Null);

        Assert.True(table.TryProbe(200, 0, out int depth, out _, out _, out _));
        Assert.Equal(1, depth);
    }

    [Fact]
    public void FillLevelTracksWhatHasBeenStored()
    {
        var table = new TranspositionTable(1);
        Assert.Equal(0, table.PermilleFull());

        for (ulong key = 1; key <= 2000; key++)
        {
            table.Store(key, 0, 1, 0, Bound.Exact, Move.Null);
        }

        Assert.True(table.PermilleFull() > 0);
    }
}
