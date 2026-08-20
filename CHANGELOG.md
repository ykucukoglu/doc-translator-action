# Changelog

All notable changes to this project are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

Everything below is pending the first tagged release (`v1.0.0`).

### Added

- AST-based Markdown translation pipeline (Markdig parse → chunk extraction → LLM translate → self-healing reconstruction), preserving code fences, inline code, raw HTML, and link/image URLs byte-for-byte.
- Multi-provider LLM support (Gemini, OpenAI, Claude) behind a single `Microsoft.Extensions.AI` `IChatClient` abstraction, plus a `fake` provider for local/CI smoke testing with no API key.
- Content-hash translation cache, keyed per source file/language/chunk - immune to line-number drift from unrelated edits.
- Glossary support (`.doc-terms.json`): `dont_translate` terms and per-language `custom_mappings`, validated post-translation with word-boundary matching (word-start-only for `custom_mappings`, to tolerate agglutinative-language suffixes).
- `.doc-ignore` file exclusion (`.gitignore`-style globs).
- Drift detection: translated files carry a provenance header (source hash + timestamp); a later run flags translations that have gone stale, and the same header prevents a file this action generated from ever being picked up as new source on a later run.
- Self-healing reconstruction: a translation that drops a required placeholder/tag marker is retried with a repair prompt (up to 2 attempts) before falling back to leaving that one paragraph untranslated.
- Flexible output paths via `output-path-template` (`{lang}`, `{relativePath}`, `{dir}`, `{filename}`, `{ext}`), supporting both tree-per-language and co-located naming conventions.
- GitHub Actions observability: Job Summary report, `::group::`/`::warning::`/`::error::` log annotations, PR summary comment, token usage tracking.
- Resilience: Polly v8 retry with exponential backoff (plus `Retry-After`-aware delay where the provider exposes it) for transient HTTP failures, independent of the semantic retry that repairs malformed LLM responses; `max-parallel-requests` concurrency bound.
- `config-path` optional JSON config file for repo-wide defaults, with explicit inputs always taking priority.

[Unreleased]: https://github.com/ykucukoglu/doc-translator-action/commits/main
