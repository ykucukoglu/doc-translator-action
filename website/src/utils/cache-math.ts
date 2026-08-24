// Matches the real per-chunk-per-language content-hash cache mechanics documented in
// docs/architecture.md: each (chunk, target language) pair is cached independently, so cost
// scales with uncached pairs, not with file count alone.
export interface CacheInputs {
  fileCount: number;
  chunksPerFile: number;
  tokensPerChunk: number;
  languageCount: number;
  hitRatePercent: number;
  firstRun: boolean;
}

export interface CacheResult {
  pairs: number;
  uncachedPairs: number;
  tokensNoCache: number;
  tokensWithCache: number;
  tokensSaved: number;
  percentSaved: number;
  costNoCache: number;
  costWithCache: number;
}

// Illustrative-only reference rate ($ / 1M input tokens) - actual pricing varies by provider
// and model; this exists only to make "tokens saved" tangible, and is labeled as such in the UI.
export const ILLUSTRATIVE_RATE_PER_MILLION_TOKENS = 0.3;

export function computeCacheEconomics(inputs: CacheInputs): CacheResult {
  const { fileCount, chunksPerFile, tokensPerChunk, languageCount, hitRatePercent, firstRun } = inputs;

  const pairs = fileCount * chunksPerFile * languageCount;
  const hitRate = firstRun ? 0 : Math.min(100, Math.max(0, hitRatePercent)) / 100;
  const uncachedPairs = firstRun ? pairs : pairs * (1 - hitRate);

  const tokensNoCache = pairs * tokensPerChunk;
  const tokensWithCache = uncachedPairs * tokensPerChunk;
  const tokensSaved = tokensNoCache - tokensWithCache;
  const percentSaved = tokensNoCache > 0 ? (tokensSaved / tokensNoCache) * 100 : 0;

  const costNoCache = (tokensNoCache / 1_000_000) * ILLUSTRATIVE_RATE_PER_MILLION_TOKENS;
  const costWithCache = (tokensWithCache / 1_000_000) * ILLUSTRATIVE_RATE_PER_MILLION_TOKENS;

  return {
    pairs: Math.round(pairs),
    uncachedPairs: Math.round(uncachedPairs),
    tokensNoCache: Math.round(tokensNoCache),
    tokensWithCache: Math.round(tokensWithCache),
    tokensSaved: Math.round(tokensSaved),
    percentSaved,
    costNoCache,
    costWithCache,
  };
}
