namespace DocTranslator.LLM.Exceptions;

/// <summary>
/// Thrown for any unrecoverable LLM translation failure: no/ambiguous provider configuration,
/// exhausted retries after malformed or ChunkId-mismatched responses, etc.
/// </summary>
public sealed class LlmTranslationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
