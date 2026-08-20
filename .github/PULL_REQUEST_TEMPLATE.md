## What changed

<!-- One or two sentences. Link an issue if there is one. -->

## Why

<!-- What problem does this solve, or what does it enable? -->

## Testing

<!-- How did you verify this? `dotnet test`, a local dry run, a real dogfooding run, etc. -->

- [ ] `dotnet build DocTranslator.sln` and `dotnet test DocTranslator.sln` pass
- [ ] If this touches `Orchestration/`, `Options/`, or the GitHub layer, added/updated a case in `tests/DocTranslator.Cli.Tests`
- [ ] If this touches AST parsing/reconstruction, added a fixture + round-trip assertion in `tests/DocTranslator.Core.Tests`
- [ ] If this touches `action.yml`'s inputs/outputs, updated `README.md`'s configuration table
