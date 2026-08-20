# Contributing

Thanks for considering a contribution to `doc-translator-action`. This project is a .NET 9 solution with four layers (`Core`, `LLM`, `GitHub`, `Cli`) - see [docs/architecture.md](docs/architecture.md) before making structural changes.

## Local development

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Docker Desktop (only needed if you're changing the `Dockerfile` or `action.yml`'s execution model)

### Build & test

```bash
dotnet build DocTranslator.sln
dotnet test DocTranslator.sln
```

Every change must keep `dotnet test` green. The test projects cover different layers:

- `tests/DocTranslator.Core.Tests` - AST parsing/reconstruction, glossary, drift detection, `.doc-ignore`. This is the correctness gate for the whole system: `AstReconstructorRoundTripTests` and `AstReconstructorSelfHealingTests` prove that code blocks, inline code, and link/image URLs survive a full parse → translate → reconstruct cycle byte-for-byte.
- `tests/DocTranslator.LLM.Tests` - provider selection, prompt construction, batching, retry/resilience, structured-output parsing, mocked against `IChatClient` (no real API calls).
- `tests/DocTranslator.Cli.Tests` - `TranslationOrchestrator` wiring, against a real throwaway git repo with only the LLM call and the GitHub PR API faked. If you're touching `Orchestration/` or `Options/`, add a case here: this is the layer where output-path resolution, `.doc-ignore`/self-translation guards, and env-var/config-file precedence actually get exercised end to end - a change here that only compiles clean isn't the same as one that's been run.

### Running the CLI locally without any API keys

```bash
dotnet run --project src/DocTranslator.Cli -- \
  --pr-mode false --use-fake-llm \
  --target-languages tr,de \
  --source-path docs
```

`--use-fake-llm` swaps in a trivial marker-wrapping translator; `--pr-mode false` writes output locally instead of pushing a branch/opening a PR. Neither needs `GITHUB_TOKEN` or an LLM key.

### Testing the Dockerfile

```bash
docker build -t doc-translator-action:dev .
docker run --rm -v "$(pwd):/workspace" -w /workspace doc-translator-action:dev \
  --pr-mode false --use-fake-llm --target-languages tr --source-path docs
```

## AST guidelines

The core architectural rule of this project: **never use regex or raw string manipulation to find translation boundaries in Markdown.** Everything that decides what's translatable goes through Markdig's real AST.

If you're touching `DocTranslator.Core.Parsing` or `DocTranslator.Core.Reconstruction`:

- **Extraction** (`MarkdigParserService` / `InlineChunkExtractor`) walks the AST once per document. Code blocks, fenced code blocks, and raw HTML blocks are skipped entirely at the block level - they never become part of any chunk's text.
- **Chunk granularity** is one leaf block (paragraph, heading, table cell, list item, blockquote) - not the whole document and not individual inline runs. Whole-document chunking loses the ability to cache/repair at a useful granularity; per-inline-run chunking fragments sentences and breaks grammar in the target language.
- **Non-text inlines** (code spans, autolinks, raw inline HTML, line breaks) become atomic placeholder tokens (`⟦CODE0⟧`) - the original `Inline` object is stashed by reference and never leaves the process as text.
- **Inlines with translatable children but metadata-bearing wrappers** (emphasis, links/images) become paired synthetic tags (`<em0>...</em0>`) - only the tag name and index cross into LLM-visible text; URL/title/delimiter metadata is stashed separately.
- **Reconstruction** (`ReconstructionScanner` / `AstReconstructor`) parses the synthetic placeholder/tag mini-language with a hand-written recursive-descent scanner - this is a deliberate, narrow exception to the "no regex" rule: it's parsing a format this codebase invented and fully controls, not scanning arbitrary Markdown.
- Any change here needs new fixtures in `tests/DocTranslator.Core.Tests/Fixtures/` exercising the affected inline/block type, plus round-trip assertions that code/link/URL content stays byte-identical.

## Pull requests

- Keep commits scoped and the message in English, imperative mood (`Add X`, not `Added X`).
- Run `dotnet build` and `dotnet test` before pushing - CI (`.github/workflows/ci.yml`) will re-run both.
- If you change `action.yml`'s inputs/outputs, update the configuration table in [README.md](README.md) too.
