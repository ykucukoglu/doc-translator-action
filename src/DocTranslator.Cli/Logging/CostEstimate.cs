namespace DocTranslator.Cli.Logging;

/// <summary>Result of an <c>estimate-cost-only</c> run - no LLM was called to produce these numbers.</summary>
public sealed record CostEstimate(int Files, int TotalPairs, int CachedPairs, int EstimatedTokens)
{
    public const string Note = "Rough char/4 heuristic; completion/output tokens are typically similar in magnitude to input for a translation task. No LLM was called.";
}
