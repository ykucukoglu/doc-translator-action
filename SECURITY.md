# Security Policy

## Reporting a vulnerability

Please **do not** open a public issue for a suspected security vulnerability. Instead, use GitHub's [private vulnerability reporting](https://github.com/ykucukoglu/doc-translator-action/security/advisories/new) for this repository, or open a draft security advisory. Include:

- The affected version/commit.
- A description of the vulnerability and its impact.
- Steps to reproduce, if possible.

You should receive an acknowledgment within a few days. There's no bug-bounty program - this is a small open-source project - but reports are taken seriously and credited in the fix's release notes unless you ask otherwise.

## Scope

`doc-translator-action` runs as a Docker-based GitHub Action with `contents: write` and `pull-requests: write` permissions in whichever repository installs it. Relevant security surface includes:

- Handling of `github-token` / `*-api-key` inputs (never logged, never written to output files - see `.doc-ignore` and the `TokenUsageTracker`, which only ever aggregates counts).
- The `docker-entrypoint`/gitconfig setup that relaxes libgit2's ownership check inside the container (`/etc/gitconfig`, `safe.directory = *`) - this is scoped to the ephemeral container, not the host.
- Anything that could let content from a translated document (LLM output) influence what gets committed, pushed, or included in a PR outside the intended `output-path-template` location.

## Supported versions

Only the latest tagged release is supported. Since this action is consumed via `uses: ykucukoglu/doc-translator-action@v1`, pinning to a specific tag/SHA in your own workflow is the recommended way to control exactly which version you're running.
