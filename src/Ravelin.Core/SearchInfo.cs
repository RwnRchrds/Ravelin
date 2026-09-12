namespace Ravelin.Core;

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
