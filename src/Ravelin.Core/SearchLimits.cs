namespace Ravelin.Core;

/// <summary>
/// What the search is allowed to spend, mapped from the arguments of a UCI <c>go</c> command.
/// </summary>
public sealed class SearchLimits
{
    /// <summary>Hard depth ceiling in plies.</summary>
    public int Depth { get; init; } = Search.MaxPly - 1;

    /// <summary>Fixed thinking time in milliseconds, overriding any clock calculation.</summary>
    public int MoveTimeMs { get; init; }

    public int WhiteTimeMs { get; init; }
    public int BlackTimeMs { get; init; }
    public int WhiteIncrementMs { get; init; }
    public int BlackIncrementMs { get; init; }

    /// <summary>Moves remaining until the next time control, or 0 when the clock is sudden death.</summary>
    public int MovesToGo { get; init; }

    /// <summary>Node ceiling, or 0 for unlimited.</summary>
    public long MaxNodes { get; init; }

    /// <summary>Search until told to stop.</summary>
    public bool Infinite { get; init; }
}

/// <summary>The result of one completed iteration of the search.</summary>
public sealed record SearchInfo(
    int Depth,
    int Score,
    long Nodes,
    TimeSpan Elapsed,
    IReadOnlyList<Move> PrincipalVariation)
{
    public Move BestMove => PrincipalVariation.Count > 0 ? PrincipalVariation[0] : Move.Null;

    public long NodesPerSecond => Elapsed.TotalSeconds > 0 ? (long)(Nodes / Elapsed.TotalSeconds) : 0;
}
