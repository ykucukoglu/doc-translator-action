---
title: Getting started
description: Add doc-translator-action to a repository so your documentation stays translated automatically.
order: 1
---

This guide walks you through adding `doc-translator-action` to a repository so your documentation stays translated automatically.

## Prerequisites

- A GitHub repository containing Markdown documentation (by default, anything under `docs/`).
- An API key for at least one supported LLM provider: [Google Gemini](https://ai.google.dev/), [OpenAI](https://platform.openai.com/), [Anthropic Claude](https://console.anthropic.com/), or Azure OpenAI. Store it as a repository secret, e.g. `GEMINI_API_KEY`.

## Minimal workflow

Create `.github/workflows/translate-docs.yml` — or generate this exact snippet, tailored to your setup, with the [Workflow Generator](/#workflow-generator) on the homepage:

```yaml
name: Translate Docs
on:
  push:
    branches: [main]
    paths: ['docs/**']
jobs:
  translate:
    runs-on: ubuntu-latest
    timeout-minutes: 15
    permissions:
      contents: write
      pull-requests: write
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 2 # doc-translator-action diffs against the previous commit
      - uses: ykucukoglu/doc-translator-action@v1
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          gemini-api-key: ${{ secrets.GEMINI_API_KEY }}
          target-languages: tr,de
```

That's it. On every push that touches `docs/**`, the action:

1. Diffs the commit to find which Markdown files actually changed.
2. Parses each one into an AST via [Markdig](https://github.com/xoofx/markdig) and extracts only the natural-language text — code blocks, inline code, and link/image URLs are never sent to the LLM.
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

## Output paths

By default, translated files land at `docs/{lang}/{relativePath}` — a Turkish translation of `docs/guide.md` becomes `docs/tr/guide.md`. Override `output-path-template` with any combination of `{lang}`, `{dir}`, `{filename}`, `{ext}`, and `{relativePath}` to match your own docs layout — see [Configuration](/configuration) for every input, or the Workflow Generator for ready-made Docusaurus/Starlight/MkDocs recipes.

## Advanced configuration

For settings you don't want to repeat across every workflow run, point `config-path` at a JSON file in your repository with any of the non-secret inputs listed on the [Configuration](/configuration) reference. Action inputs always take precedence over the config file, so you can still override a single value per-run without editing it.

## Next steps

- Read [Architecture](/architecture) for how the AST parse/translate/reconstruct pipeline actually works.
- Read the [Glossary](/glossary) page to keep product names and technical terms untranslated.
- See the [Configuration](/configuration) reference for the full input/output list.
