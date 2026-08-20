using DocTranslator.Core.Models;

namespace DocTranslator.Core.Provenance;

/// <summary>Outcome of comparing an existing translated file's provenance header against its current source.</summary>
/// <param name="IsStale">True if the source has moved on since this translation was generated, or the header is missing/unparseable.</param>
/// <param name="Reason">Human-readable explanation, for the console summary and PR comment.</param>
/// <param name="ExistingProvenance">The parsed header, if one was found (even if stale).</param>
public sealed record DriftCheckResult(bool IsStale, string Reason, TranslationProvenance? ExistingProvenance);
