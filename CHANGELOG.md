# Changelog

All notable changes to this project are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

Everything below is pending the first tagged release (`v1.0.0`).

### Added

- AST-based Markdown translation pipeline (Markdig parse → chunk extraction → LLM translate → self-healing reconstruction), preserving code fences, inline code, raw HTML, and link/image URLs byte-for-byte.
- Multi-provider LLM support (Gemini, OpenAI, Claude) behind a single `Microsoft.Extensions.AI` `IChatClient` abstraction, plus a `fake` provider for local/CI smoke testing with no API key.
- Content-hash translation cache, keyed per source file/language/chunk - immune to line-number drift from unrelated edits.
- Glossary support (`.doc-terms.json`): `dont_translate` terms, per-language `custom_mappings`, and an optional `style_guide` tone instruction, validated post-translation with word-boundary matching (word-start-only for `custom_mappings`, to tolerate agglutinative-language suffixes).
- `.doc-ignore` file exclusion (`.gitignore`-style globs).
- Drift detection: translated files carry a provenance header (source hash + timestamp); a later run flags translations that have gone stale, and the same header prevents a file this action generated from ever being picked up as new source on a later run.
- Self-healing reconstruction: a translation that drops a required placeholder/tag marker is retried with a repair prompt (up to 2 attempts) before falling back to leaving that one paragraph untranslated.
- Flexible output paths via `output-path-template` (`{lang}`, `{relativePath}`, `{dir}`, `{filename}`, `{ext}`), supporting both tree-per-language and co-located naming conventions.
- GitHub Actions observability: Job Summary report, `::group::`/`::warning::`/`::error::` log annotations, PR summary comment, token usage tracking.
- Resilience: Polly v8 retry with exponential backoff (plus `Retry-After`-aware delay where the provider exposes it) for transient HTTP failures, independent of the semantic retry that repairs malformed LLM responses; `max-parallel-requests` concurrency bound.
- `config-path` optional JSON config file for repo-wide defaults, with explicit inputs always taking priority.
- `backfill-missing-translations` input: translates any source file/language pair with no output yet regardless of this run's diff, for a first install against pre-existing docs or after adding a new target language.
- `DocTranslator.Cli.Tests`: end-to-end orchestrator tests against a real throwaway git repo, with only the LLM call and the GitHub PR API faked.
- `DocTranslator.GitHub.Tests`: diff analyzer and git-write mechanics tests against real throwaway repos.
- `estimate-cost-only` input: reports an estimated input-token count for the run (skipping anything already cached) and exits, with no LLM call and no git/GitHub access.
- Fork `pull_request_target` safety: automatically forces a dry run when triggered by that event/fork-PR combination, regardless of `pr-mode`, unless the `allow-fork-pull-request-target: true` action input is set explicitly (deliberately not readable from `config-path`, which can be fork-controlled content in this exact scenario).

### Fixed

- The content-hash translation cache is now persisted across CI runs via `actions/cache` in the example workflows - previously it was rebuilt from scratch and discarded every run, so it never produced any real cross-run savings.
- `allow-fork-pull-request-target` could no longer be set from `config-path` - that file is read from the job's working directory, which under `pull_request_target` with a fork-head checkout is the fork PR's own content, so honoring it from there defeated the safety net it was meant to enforce.
- The `OpenAI` package was pinned back to `2.12.0` - a Dependabot PR had bumped it to `2.13.0`, which builds with only a `NU1608` warning but is outside the `[2.12.0, 2.13.0)` range `Microsoft.Extensions.AI.OpenAI` actually declares support for.

[Unreleased]: https://github.com/ykucukoglu/doc-translator-action/commits/main
