#!/bin/sh
# GitHub Actions runs Docker actions as root by default, while the checked-out repository at
# /github/workspace is owned by the runner's own user. libgit2 (used by LibGit2Sharp) enforces
# the same "dubious ownership" safety check git itself added for CVE-2022-24765 and refuses to
# open a repository it doesn't own unless that path is explicitly marked safe. Register a
# wildcard safe.directory entry in the global gitconfig before starting the CLI - this only
# relaxes the ownership check for git/libgit2 operations inside this ephemeral container, not on
# the host.
set -e

export HOME="${HOME:-/root}"
mkdir -p "$HOME"
cat >> "$HOME/.gitconfig" <<'EOF'
[safe]
	directory = *
EOF

exec dotnet /app/DocTranslator.Cli.dll "$@"
