# doc-translator-action

[![Build](https://github.com/ykucukoglu/doc-translator-action/actions/workflows/ci.yml/badge.svg)](https://github.com/ykucukoglu/doc-translator-action/actions/workflows/ci.yml)
[![CodeQL](https://github.com/ykucukoglu/doc-translator-action/actions/workflows/codeql.yml/badge.svg)](https://github.com/ykucukoglu/doc-translator-action/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/9.0)

A GitHub Action that translates Markdown documentation into multiple target languages on push or pull request, without breaking code snippets, inline variables, HTML, or technical URLs.

Markdown is parsed into a real AST via [Markdig](https://github.com/xoofx/markdig); only natural-language text nodes (paragraphs, headings, table cells, list items, blockquotes) are extracted and sent to an LLM, while code blocks, inline code, raw HTML, and link/image targets are never touched. Translated text is spliced back into the exact AST position it came from and re-rendered to Markdown. See [docs/architecture.md](docs/architecture.md) for the full pipeline.

## Core features

- 🌳 **AST-based parsing** — Markdig, not regex. Code fences, inline code, and link/image URLs are structurally impossible to mistranslate.
- 💰 **Zero-cost content-hash cache** — each paragraph's translation is cached by a hash of its own content, not git line numbers, so unrelated edits elsewhere in a file never trigger a re-translation.
- 🩹 **Self-healing reconstruction** — a translation that drops a required marker is repaired (up to 2 retries) before falling back to leaving just that paragraph untranslated; one bad LLM response never corrupts a document.
- 📊 **Job Summary** — a Markdown execution report (chunk/cache counts, token usage, warnings) on every run's GitHub Actions "Summary" tab.
- 🔌 **Three LLM providers** — Gemini, OpenAI, and Claude, all behind `Microsoft.Extensions.AI`'s `IChatClient`, via each vendor's official SDK.
- 🔁 **Resilient & concurrent** — Polly v8 exponential backoff on transient HTTP failures; batch requests run concurrently, bounded by `max-parallel-requests`.
- 🕵️ **Drift detection** — flags translated files whose source has changed since they were last translated.
- 🚫 **`.doc-ignore`** — exclude files like `CHANGELOG.md` or `DRAFT_*.md` from the pipeline entirely.

## Quick start

Set exactly one of `gemini-api-key`, `openai-api-key`, `anthropic-api-key` (or set `llm-provider` explicitly if more than one is configured).

### Standard Markdown (default layout)

Translations land under `docs/{lang}/...`, mirroring the source tree.

```yaml
name: Translate Docs
on:
  push:
    branches: [main]
    paths: ['docs/**']
jobs:
  translate:
    runs-on: ubuntu-latest
    permissions:
      contents: write
      pull-requests: write
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 2
      # Persists the content-hash translation cache across runs - without this, every run starts
      # from an empty cache and re-translates unrelated unchanged chunks needlessly.
      - uses: actions/cache@v4
        with:
          path: .doc-translator-cache
          key: doc-translator-cache-${{ github.run_id }}
          restore-keys: |
            doc-translator-cache-
      - uses: ykucukoglu/doc-translator-action@v1
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          gemini-api-key: ${{ secrets.GEMINI_API_KEY }}
          target-languages: tr,de,fr
```

Adding this to a repository with pre-existing docs? Nothing in them "changed" in any one commit, so the diff-only pipeline above won't pick them up on its own - add `backfill-missing-translations: true` for that first run (or after adding a new target language) to translate anything with no output yet, regardless of diff.

### Docusaurus

Docusaurus expects translated content under `i18n/<locale>/docusaurus-plugin-content-docs/current/...`:

```yaml
      - uses: ykucukoglu/doc-translator-action@v1
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          gemini-api-key: ${{ secrets.GEMINI_API_KEY }}
          target-languages: tr,de
          source-path: docs
          output-path-template: 'i18n/{lang}/docusaurus-plugin-content-docs/current/{relativePath}'
```

### MkDocs (co-located, e.g. mkdocs-static-i18n)

Translated files sit next to the source, differentiated by a locale suffix (`guide.md` → `guide.de.md`):

```yaml
      - uses: ykucukoglu/doc-translator-action@v1
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          gemini-api-key: ${{ secrets.GEMINI_API_KEY }}
          target-languages: tr,de
          source-path: docs
          output-path-template: '{dir}/{filename}.{lang}.{ext}'
```

### Local dry run

No API keys or GitHub token needed:

```bash
dotnet run --project src/DocTranslator.Cli -- \
  --pr-mode false --use-fake-llm \
  --target-languages tr,de \
  --source-path docs
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full local development setup, including testing the `Dockerfile` directly.

## Configuration reference

All inputs are optional except `github-token` (required unless `pr-mode`/`dry-run` disables PR creation).

| Input | Default | Description |
| --- | --- | --- |
| `github-token` | — | GitHub token used to push the translation branch and open the pull request. |
| `gemini-api-key` | — | Google Gemini API key. Set this, `openai-api-key`, or `anthropic-api-key`. |
| `openai-api-key` | — | OpenAI API key. |
| `anthropic-api-key` | — | Anthropic (Claude) API key. |
| `llm-provider` | `auto` | `auto`, `gemini`, `openai`, `claude`, or `fake`. Must be set explicitly if more than one `*-api-key` is configured. |
| `gemini-model` | `gemini-2.5-flash` | Gemini model id. |
| `openai-model` | `gpt-5-mini` | OpenAI model id. |
| `claude-model` | `claude-sonnet-5` | Claude model id. |
| `target-languages` | `tr` | Comma-separated target language codes, e.g. `tr,de,fr`. |
| `source-path` | `docs` | Root folder to scan for documentation. |
| `include-glob` | `**/*.md` | Glob (relative to `source-path`) selecting which files to translate. |
| `glossary-path` | `.doc-terms.json` | Path to the glossary file - see [Glossary](#glossary). |
| `config-path` | — | Path to a JSON file supplying any of `targetLanguages`, `sourcePath`, `includeGlob`, `glossaryPath`, `outputPathTemplate`, `baseBranch`, `failOnStaleTranslations`, `backfillMissingTranslations`, `maxParallelRequests`, `llmProvider`, `geminiModel`, `openAiModel`, `claudeModel` - lets advanced setups avoid a large `with:` block. Explicit inputs always win over the config file. |
| `output-path-template` | `docs/{lang}/{relativePath}` | Supports `{lang}`, `{relativePath}`, `{dir}`, `{filename}`, `{ext}` - see the Quick start snippets above. |
| `base-branch` | *(auto)* | Base branch to diff against and open the PR into. Defaults to `GITHUB_BASE_REF` on `pull_request` events, or the previous commit on `push`. |
| `pr-mode` | `true` | When `true`, pushes a branch and opens a PR. When `false`, writes translated files locally only - no `github-token` required. |
| `dry-run` | — | Explicit override for `pr-mode`; wins if both are set. |
| `fail-on-stale-translations` | `false` | Exit non-zero if any existing translated file is out of sync with its current source. |
| `backfill-missing-translations` | `false` | Also translates any source file/language pair with no output yet, regardless of this run's diff. The diff-only pipeline never picks up pre-existing docs on its own - use this on a first install, or after adding a new target language. |
| `max-parallel-requests` | `4` | Bounds concurrent LLM batch requests per file/language. |

**Outputs:** `pr-url`, `translated-files-count`, `stale-translations-count`.

### Environment variables (local CLI use)

Running `DocTranslator.Cli` directly (outside the Action) reads plain, unprefixed env vars instead of `INPUT_*`:

| Variable | Purpose |
| --- | --- |
| `GEMINI_API_KEY` / `OPENAI_API_KEY` / `ANTHROPIC_API_KEY` | Provider credentials. |
| `GITHUB_TOKEN` | Used when opening a PR (via `--github-token` or the `github-token` input outside a container). |
| `GITHUB_REPOSITORY` | `owner/repo`, required to open a PR (set automatically inside GitHub Actions). |
| `GITHUB_STEP_SUMMARY` | Job Summary file path (set automatically inside GitHub Actions). |
| `GITHUB_OUTPUT` | Action outputs file path (set automatically inside GitHub Actions). |

## Self-healing & production hardening

- **Self-healing AST reconstruction**: if a translated chunk drops a required placeholder (`⟦CODE0⟧`) or tag (`<em0>...</em0>`), that one chunk is re-translated with a repair prompt (up to 2 attempts) before falling back to leaving just that paragraph in the source language - one bad LLM response never corrupts the document or aborts the whole file.
- **`.doc-ignore`**: a `.gitignore`-style file at the repo root (one glob per line, `#` comments) excludes files from translation entirely, e.g. `CHANGELOG.md` or `DRAFT_*.md`. See the sample file in this repo.
- **Flexible output paths**: see the Docusaurus/MkDocs snippets above.

## Observability & reliability

- **Job Summary**: a Markdown execution report (per-language chunk/cache counts, token usage, warnings) is appended to the run's GitHub Actions "Summary" tab via `GITHUB_STEP_SUMMARY`.
- **Log grouping & annotations**: each file's processing is collapsed into a `::group::`/`::endgroup::` section; glossary and reconstruction issues surface as `::warning file=...::`/`::error file=...::` PR-visible annotations, not just console lines.
- **Resilience**: transient HTTP failures (429 rate limits, 5xx) are retried with Polly v8 exponential backoff, independent of the semantic retry that repairs malformed translation responses.
- **Concurrency**: LLM batch requests for a file/language run concurrently, bounded by `max-parallel-requests` (default 4, via a `SemaphoreSlim`).
- **Token usage**: prompt/completion token totals are accumulated across the whole run and reported in both the console log and the Job Summary.

## Glossary

`.doc-terms.json` at the repo root controls which terms are never translated (`dont_translate`) and per-language required renderings (`custom_mappings`). See the sample file in this repo.

## Solution layout

- `src/DocTranslator.Core` — Markdig AST parsing/reconstruction, glossary, `.doc-ignore`, drift detection (no external API deps)
- `src/DocTranslator.LLM` — multi-provider translation via `Microsoft.Extensions.AI`'s `IChatClient` (Gemini, OpenAI, Claude)
- `src/DocTranslator.GitHub` — git diff analysis, translation cache, PR creation via LibGit2Sharp + Octokit
- `src/DocTranslator.Cli` — DI wiring and orchestration pipeline
- `tests/` — xUnit test suites for `Core` and `LLM`
- `docs/` — this project's own documentation, also used as the [dogfooding](.github/workflows/dogfooding.yml) fixture

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for local setup, test guidelines, and the AST extraction/reconstruction rules. Bug reports and feature requests: use the templates under [.github/ISSUE_TEMPLATE](.github/ISSUE_TEMPLATE).

## License

[MIT](LICENSE)
