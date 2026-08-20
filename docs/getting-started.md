# Getting Started

This guide walks you through adding `doc-translator-action` to a repository so your documentation stays translated automatically.

## Prerequisites

- A GitHub repository containing Markdown documentation (by default, anything under `docs/`).
- An API key for at least one supported LLM provider: [Google Gemini](https://ai.google.dev/), [OpenAI](https://platform.openai.com/), or [Anthropic Claude](https://console.anthropic.com/). Store it as a repository secret, e.g. `GEMINI_API_KEY`.

## Minimal workflow

Create `.github/workflows/translate-docs.yml`:

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
      - uses: actions/checkout@v4
        with:
          fetch-depth: 2 # doc-translator-action diffs against the previous commit

      - uses: your-org/doc-translator-action@v1
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          gemini-api-key: ${{ secrets.GEMINI_API_KEY }}
          target-languages: tr,de
```

That's it. On every push that touches `docs/**`, the action:

1. Diffs the commit to find which Markdown files actually changed.
2. Parses each one into an AST via [Markdig](https://github.com/xoofx/markdig) and extracts only the natural-language text - code blocks, inline code, and link/image URLs are never sent to the LLM.
3. Translates the extracted text into every language listed in `target-languages`.
4. Splices the translations back into the original document structure and writes the result under `docs/{lang}/...` (configurable via `output-path-template`).
5. Opens a pull request with the translated files, keyed to the triggering commit so re-runs are idempotent.

## Local dry run

You don't need a real API key or GitHub token to try it locally:

```bash
dotnet run --project src/DocTranslator.Cli -- \
  --pr-mode false --use-fake-llm \
  --target-languages tr,de \
  --source-path docs
```

`--pr-mode false` writes the translated files to disk without pushing anything, and `--use-fake-llm` swaps in a trivial marker-wrapping translator so you can inspect the output structure without spending API credits.

## Next steps

- Read [architecture.md](architecture.md) for how the AST parse/translate/reconstruct pipeline actually works.
- Add a [`.doc-terms.json`](../.doc-terms.json) glossary to keep product names and technical terms untranslated.
- Add a [`.doc-ignore`](../.doc-ignore) file to exclude files like `CHANGELOG.md` from translation.
- See the [README](../README.md) for the full input/output reference and Docusaurus/MkDocs quick-start snippets.
