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
- `cleanup-stale-branches` input (default `true`): deletes this action's own `doc-translator/<sha>` branches once their pull request is closed (merged or declined), so re-runs don't leave an ever-growing pile of dead branches behind. Scoped to that name prefix and to branches with a known closed PR - a branch with no matching PR is left alone.
- `pr-was-created` output: `"true"` if this run opened a new pull request, `"false"` if it reused an already-open one.
- `source-language` input: explicitly tells the LLM the source language instead of leaving it unstated (default `auto`, unchanged behavior).
- `max-batch-tokens` input: exposes the per-batch token budget (previously hardcoded at 4000).
- `verbose` input: prints the full exception to stderr on failure (previously CLI-flag-only).
- Azure OpenAI as a fourth LLM provider (`azure-openai-api-key`, `azure-openai-endpoint`, `azure-openai-deployment`), for enterprise setups already using an Azure OpenAI resource.
- `llm-fallback-provider` input: retries once against a second, different provider if the primary exhausts its own retries (rate limit, outage, quota). Opt-in only - never inferred from which provider keys happen to be configured.

### Fixed

- The content-hash translation cache is now persisted across CI runs via `actions/cache` in the example workflows - previously it was rebuilt from scratch and discarded every run, so it never produced any real cross-run savings.
- `allow-fork-pull-request-target` could no longer be set from `config-path` - that file is read from the job's working directory, which under `pull_request_target` with a fork-head checkout is the fork PR's own content, so honoring it from there defeated the safety net it was meant to enforce.
- The `OpenAI` package was pinned back to `2.12.0` - a Dependabot PR had bumped it to `2.13.0`, which builds with only a `NU1608` warning but is outside the `[2.12.0, 2.13.0)` range `Microsoft.Extensions.AI.OpenAI` actually declares support for.
- `GitWriter` no longer leaves the job's working directory checked out on the pushed translation branch - any workflow step running after this action now still sees whatever ref the job actually checked out, not `doc-translator/<sha>`.
- YAML frontmatter (`---` ... `---` at the top of a file, used by Docusaurus, Jekyll, Hugo, Astro/Starlight, MkDocs, and others) is no longer sent to the LLM as ordinary text - it was previously parsed as a heading/paragraph and had its keys/values translated, corrupting the metadata block. It's now captured verbatim and spliced back onto the output ahead of everything else, including the provenance header (frontmatter must stay the file's first bytes). Drift detection and the self-translation-cascade guard were also fixed to find the provenance header when frontmatter precedes it.
- Docusaurus/MyST-style `::: note ... :::` admonitions are now recognized (Markdig's CustomContainers extension) - previously the fence lines weren't parsed as anything special and were swept into an ordinary paragraph, sending `note`/`tip` and the fence markers themselves to the LLM as translatable text. Only the content inside is translated now; a custom Markdown round-trip renderer re-emits the fence on output, since Markdig only ships one for HTML.
- Hugo's `+++`-delimited TOML frontmatter is now recognized too - Markdig has no native TOML frontmatter support at all (only YAML), so it's stripped from the raw text before Markdig ever parses it and spliced back verbatim ahead of the provenance header, the same way YAML frontmatter is handled.
- The PR summary comment now includes: a side-by-side original/translated preview (first 3 chunks per file, capped at 5 file/language pairs, in a collapsible `<details>` block); a "Preserved untouched" count of code blocks, inline code spans, links, and named `dont_translate` glossary terms; and a mention of the `GITHUB_ACTOR` who triggered the run.
- `openai-base-url` input: redirects the `openai` provider at any OpenAI-compatible endpoint (Ollama, LM Studio, vLLM, OpenRouter, ...) instead of api.openai.com, for running against a free/local model with no real OpenAI account. (GitHub Models was considered as a zero-key default too, but was fully retired on July 30, 2026 - no inference API is left to integrate with.)
- `translate-mermaid-diagrams` input (default `false`): translates node/edge/subgraph text labels inside ` ```mermaid ` `flowchart`/`graph` diagrams, leaving arrows, node ids, and every other structural token byte-for-byte untouched. Pattern-based extraction (`MermaidLabelExtractor`), not a full mermaid parser - scoped to that one diagram type and a fixed set of label shapes; anything it can't cleanly recognize is left alone rather than guessed at. Other diagram types and PlantUML are out of scope for now.
- `push-to-current-branch` input (default `false`): commits and pushes directly onto whatever branch is already checked out, instead of opening a new PR - meant for a workflow that already has a specific branch checked out for a reason of its own, e.g. a PR-comment-triggered `/translate` run pushing back onto that PR's own branch (see the README's new "Comment-triggered (ChatOps)" recipe). Requires a real branch checkout, not a detached HEAD; the push itself is never forced, since the target branch is real, possibly-shared work this action doesn't own the way it owns its own `doc-translator/<sha>` branches.
- Job Summary and console output now include execution time, the cache-hit rate, and the same "Preserved untouched" breakdown the PR comment already had (previously PR-comment-only, so a dry run or `push-to-current-branch` run never showed it anywhere) - all consolidated into one metrics table at the top of the Job Summary.

### Security

- `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1), completing GitHub's recommended community-health-file checklist alongside the existing `LICENSE`, `CONTRIBUTING.md`, `SECURITY.md`, and issue/PR templates.
- All third-party actions used in this repo's own workflows (`actions/checkout`, `actions/setup-dotnet`, `actions/cache`, `github/codeql-action/*`) are now pinned to a full commit SHA instead of a mutable version tag - a tag can be silently repointed (by a compromised maintainer account or a force-pushed release, both of which have happened to widely-used actions), a SHA cannot. Dependabot still proposes updates via the trailing `# vN` comment.

[Unreleased]: https://github.com/ykucukoglu/doc-translator-action/commits/main
