# doc-translator-action

A GitHub Action that translates Markdown documentation into multiple target languages on push or pull request, without breaking code snippets, inline variables, HTML, or technical URLs.

Markdown is parsed into a real AST via [Markdig](https://github.com/xoofx/markdig); only natural-language text nodes (paragraphs, headings, table cells, list items, blockquotes) are extracted and sent to an LLM, while code blocks, inline code, raw HTML, and link/image targets are never touched. Translated text is spliced back into the exact AST position it came from and re-rendered to Markdown.

## Usage

```yaml
- uses: ./ # or owner/doc-translator-action@v1 once published
  with:
    github-token: ${{ secrets.GITHUB_TOKEN }}
    gemini-api-key: ${{ secrets.GEMINI_API_KEY }} # or openai-api-key / anthropic-api-key
    target-languages: de,fr,ja
```

Set exactly one of `gemini-api-key`, `openai-api-key`, `anthropic-api-key` (or set `llm-provider` explicitly if more than one is configured). See [action.yml](action.yml) for every input/output.

### Local dry run

No API keys or GitHub token needed:

```bash
dotnet run --project src/DocTranslator.Cli -- \
  --dry-run --use-fake-llm \
  --target-languages de,fr \
  --source-path docs \
  --glossary-path .doc-terms.json
```

## Observability & reliability

- **Job Summary**: a Markdown execution report (per-language chunk/cache counts, token usage, warnings) is appended to the run's GitHub Actions "Summary" tab via `GITHUB_STEP_SUMMARY`.
- **Log grouping & annotations**: each file's processing is collapsed into a `::group::`/`::endgroup::` section; glossary and reconstruction issues surface as `::warning file=...::`/`::error file=...::` PR-visible annotations, not just console lines.
- **Resilience**: transient HTTP failures (429 rate limits, 5xx) are retried with Polly v8 exponential backoff, independent of the semantic retry that repairs malformed translation responses.
- **Concurrency**: LLM batch requests for a file/language run concurrently, bounded by `max-parallel-requests` (default 4, via a `SemaphoreSlim`).
- **Token usage**: prompt/completion token totals are accumulated across the whole run and reported in both the console log and the Job Summary.

## Self-healing & production hardening

- **Self-healing AST reconstruction**: if a translated chunk drops a required placeholder (`⟦CODE0⟧`) or tag (`<em0>...</em0>`), that one chunk is re-translated with a repair prompt (up to 2 attempts) before falling back to leaving just that paragraph in the source language - one bad LLM response never corrupts the document or aborts the whole file.
- **`.doc-ignore`**: a `.gitignore`-style file at the repo root (one glob per line, `#` comments) excludes files from translation entirely, e.g. `CHANGELOG.md` or `DRAFT_*.md`. See the sample file in this repo.
- **Flexible output paths**: `output-path-template` supports `{lang}`, `{relativePath}`, `{dir}`, `{filename}`, `{ext}` - enough to express both the default per-language tree (`docs/{lang}/{relativePath}`) and co-located naming styles used by MkDocs/Docusaurus setups (`{dir}/{filename}.{lang}.{ext}` turns `docs/guide.md` into `docs/guide.de.md`).

## Glossary

`.doc-terms.json` at the repo root controls which terms are never translated (`dont_translate`) and per-language required renderings (`custom_mappings`). See the sample file in this repo.

## Solution layout

- `src/DocTranslator.Core` — Markdig AST parsing/reconstruction, glossary, drift detection (no external API deps)
- `src/DocTranslator.LLM` — multi-provider translation via `Microsoft.Extensions.AI`'s `IChatClient` (Gemini, OpenAI, Claude)
- `src/DocTranslator.GitHub` — git diff analysis, translation cache, PR creation via LibGit2Sharp + Octokit
- `src/DocTranslator.Cli` — DI wiring and orchestration pipeline
- `tests/` — xUnit test suites for `Core` and `LLM`

## Build & test

```bash
dotnet build
dotnet test
```
