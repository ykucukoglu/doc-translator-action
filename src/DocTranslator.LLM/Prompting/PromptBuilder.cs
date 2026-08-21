using System.Globalization;
using System.Text;
using DocTranslator.Core.Glossary;
using DocTranslator.Core.Models;
using Microsoft.Extensions.AI;

namespace DocTranslator.LLM.Prompting;

public interface IPromptBuilder
{
    IReadOnlyList<ChatMessage> Build(IReadOnlyList<TranslationChunk> batch, string targetLanguage, GlossaryContext glossary, string sourceLanguage = "auto");
}

/// <summary>
/// Builds the system/user messages sent to <c>IChatClient</c>. Provider-agnostic - it only ever
/// produces <see cref="ChatMessage"/>s, so both providers share identical prompt semantics.
/// </summary>
public sealed class PromptBuilder(IGlossaryService glossaryService) : IPromptBuilder
{
    public IReadOnlyList<ChatMessage> Build(IReadOnlyList<TranslationChunk> batch, string targetLanguage, GlossaryContext glossary, string sourceLanguage = "auto")
    {
        var sourceClause = string.IsNullOrWhiteSpace(sourceLanguage) || sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" from the language with code '{sourceLanguage}'";

        var systemPrompt = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"You are a professional technical documentation translator. Translate every given chunk{sourceClause} into the language with code '{targetLanguage}'.")
            .AppendLine("Each chunk's text may contain special markers that you MUST preserve EXACTLY, character-for-character, in their original relative position:")
            .AppendLine("- Placeholder tokens such as ⟦CODE0⟧, ⟦AUTOLINK1⟧, ⟦HTML2⟧, ⟦BR3⟧ - copy them verbatim; never translate, reorder, or alter their digits.")
            .AppendLine("- Paired tags such as <em0>...</em0>, <strong0>...</strong0>, <link0>...</link0> - translate ONLY the text between the tags, and keep the tag name and number exactly as given.")
            .AppendLine("Do not translate the placeholder or tag markers themselves under any circumstances.")
            .AppendLine("Return exactly one translation per chunk id given, and no other chunk ids.");

        var glossaryHint = glossaryService.BuildPromptHint(glossary, targetLanguage);
        if (!string.IsNullOrEmpty(glossaryHint))
        {
            systemPrompt.AppendLine(glossaryHint);
        }

        var userPrompt = new StringBuilder("Translate the following chunks. Each line is \"[chunkId]: text\".\n\n");
        foreach (var chunk in batch)
        {
            userPrompt.Append('[').Append(chunk.ChunkId).Append("]: ").AppendLine(chunk.SourceText);
        }

        return
        [
            new ChatMessage(ChatRole.System, systemPrompt.ToString()),
            new ChatMessage(ChatRole.User, userPrompt.ToString()),
        ];
    }
}
