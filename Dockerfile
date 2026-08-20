# syntax=docker/dockerfile:1

# Framework-dependent (not Native AOT) by design for v1: Octokit's reflection-heavy REST
# (de)serialization and the LLM SDKs' JSON model binding have unverified AOT/trimming
# compatibility, and container cold-start is negligible next to LLM network latency anyway.
# See the implementation plan's Dockerfile section for the full rationale.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first, from just the project files, so dependency layers are cached across builds
# that only change source code.
COPY DocTranslator.sln Directory.Build.props ./
COPY src/DocTranslator.Core/DocTranslator.Core.csproj src/DocTranslator.Core/
COPY src/DocTranslator.LLM/DocTranslator.LLM.csproj src/DocTranslator.LLM/
COPY src/DocTranslator.GitHub/DocTranslator.GitHub.csproj src/DocTranslator.GitHub/
COPY src/DocTranslator.Cli/DocTranslator.Cli.csproj src/DocTranslator.Cli/
RUN dotnet restore src/DocTranslator.Cli/DocTranslator.Cli.csproj -r linux-x64

COPY src/ src/

# -r linux-x64 ensures LibGit2Sharp.NativeBinaries publishes the correct native libgit2 shared
# library for the container's platform.
RUN dotnet publish src/DocTranslator.Cli/DocTranslator.Cli.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /app \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# GitHub Actions checks out the repo as the runner's own user but runs Docker actions as root,
# which trips libgit2's "dubious ownership" safety check (the same CVE-2022-24765 fix git itself
# has) unless the workspace is explicitly marked safe. Written into the image at build time,
# rather than by a runtime entrypoint shell script, because GitHub Actions passes hyphenated
# INPUT_* env vars (e.g. INPUT_PR-MODE) that /bin/sh silently drops when it execs a child process
# - POSIX shells only carry forward environment entries whose names are valid shell identifiers.
# An exec-form ENTRYPOINT with no intermediate shell is required for those inputs to survive.
#
# Goes into the system-wide gitconfig (/etc/gitconfig), not $HOME/.gitconfig: GitHub Actions also
# passes through the runner's own HOME (e.g. /home/runner) via `-e "HOME"` with no value, which
# overrides whatever HOME this image sets - a per-user config file would silently end up at the
# wrong path. /etc/gitconfig isn't keyed to HOME at all, so it's read regardless.
RUN printf '[safe]\n\tdirectory = *\n' >> /etc/gitconfig

ENTRYPOINT ["dotnet", "/app/DocTranslator.Cli.dll"]
