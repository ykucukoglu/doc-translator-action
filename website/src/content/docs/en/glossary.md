---
title: Glossary
description: How .doc-terms.json controls which terms are never translated and how per-language renderings are enforced.
order: 3
---

`.doc-terms.json` at your repository root controls which terms are never translated, per-language required renderings,
and an optional overall tone — checked both before translation (as prompt instructions to the LLM) and after (as a QA
pass over the translated output).

This is the real file from this project's own repository:

```json
{
  "version": 1,
  "case_sensitive": false,
  "dont_translate": ["GitHub", "API", "npm", "Markdig", "Docker", "JSON", "SDK", "LLM"],
  "custom_mappings": {
    "de": { "repository": "Repository", "pull request": "Pull Request" },
    "fr": { "repository": "dépôt", "pull request": "requête de tirage" },
    "tr": { "repository": "depo", "pull request": "değişiklik isteği" }
  },
  "style_guide": "Use a neutral, professional tone. Write instructions directly to the reader."
}
```

## Fields

- **`version`** — schema version (currently `1`).
- **`case_sensitive`** — whether term matching (both `dont_translate` and `custom_mappings`) is case-sensitive.
- **`dont_translate`** — terms that must appear verbatim, untranslated, in every output language. Checked with
  word-boundary matching, not plain substring search, so a short term like `API` doesn't false-positive inside a
  longer word like `CAPITAL`.
- **`custom_mappings`** — per-language required renderings for specific source terms, e.g. always rendering
  "pull request" as "değişiklik isteği" in Turkish rather than trusting the LLM to pick a consistent translation
  every time.
- **`style_guide`** — a free-form tone instruction sent to the LLM alongside the glossary terms.

## The agglutinative-language caveat

`custom_mappings`'s QA check only requires the required rendering to **start** a word in the translation, not stand
entirely alone. Agglutinative languages like Turkish attach case suffixes directly onto a word with no separator —
"depo" (repository) legitimately appears as "depoya", "deposunu", or "depodan" depending on grammatical case. A
trailing word-boundary check would flag every one of those as a glossary miss even though the translation is
correct, so only a leading boundary is enforced for `custom_mappings`. `dont_translate` terms stay whole-word on both
sides, since those (`GitHub`, `API`, `SDK`, ...) are meant to survive completely unmodified.
