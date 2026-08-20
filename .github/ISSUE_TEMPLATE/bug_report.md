---
name: Bug report
about: Something in the translation pipeline isn't working as expected
title: '[Bug] '
labels: bug
assignees: ''
---

## Describe the bug

A clear description of what went wrong.

## Reproduction

1. `action.yml` inputs used (redact API keys):
   ```yaml
   target-languages: ...
   llm-provider: ...
   # ...
   ```
2. Minimal Markdown snippet that triggers the issue, if applicable:
   ```markdown
   <!-- paste here -->
   ```
3. Steps to reproduce.

## Expected behavior

What you expected to happen instead.

## Actual behavior

What actually happened - paste the relevant console output, Job Summary section, or `::error::`/`::warning::` annotation.

## Environment

- doc-translator-action version/commit:
- LLM provider: (Gemini / OpenAI / Claude)
- Run mode: (GitHub Action / local CLI / Docker)
- .NET version (if running locally): `dotnet --version`

## Additional context

Anything else relevant - was this a fresh translation or a re-run, did it involve `.doc-ignore` / `config-path` / a custom `output-path-template`, etc.
