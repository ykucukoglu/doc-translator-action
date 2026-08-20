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

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Absolute path, not "DocTranslator.Cli.dll" relative to WORKDIR: GitHub Actions runs Docker
# actions with the working directory overridden to the checked-out repo (GITHUB_WORKSPACE), so a
# relative entrypoint path would fail to locate the DLL at runtime.
ENTRYPOINT ["dotnet", "/app/DocTranslator.Cli.dll"]
