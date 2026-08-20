<!-- doc-translator: source-hash=4eaaa588658a073d2dfacbf7a3b3f33393faafc2b02dbde9e41d5e2082c32acd; source-path=docs/getting-started.md; target-lang=de; generated=2026-08-20T17:13:31.6120663+00:00 -->

# Erste Schritte

Diese Anleitung führt Sie durch das Hinzufügen von `doc-translator-action` zu einem Repository, damit Ihre Dokumentation automatisch übersetzt wird.

## Voraussetzungen

- Ein GitHub Repository mit Markdown-Dokumentation (standardmäßig alles unter `docs/`).
- Ein API-Schlüssel für mindestens einen unterstützten LLM-Anbieter: [Google Gemini](https://ai.google.dev/), [OpenAI](https://platform.openai.com/) oder [Anthropic Claude](https://console.anthropic.com/). Speichern Sie ihn als Repository-Secret, z.B. `GEMINI_API_KEY`.

## Minimaler Workflow

Erstellen Sie `.github/workflows/translate-docs.yml`:

```yaml
name: Translate Docs

on:
  push:
    branches: [main]
    paths: ['docs/**']

jobs:
  translate:
    runs-on: ubuntu-latest
    permissions:
      contents: write
      pull-requests: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 2 # doc-translator-action diffs against the previous commit

      - uses: your-org/doc-translator-action@v1
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          gemini-api-key: ${{ secrets.GEMINI_API_KEY }}
          target-languages: tr,de
```

Das war's. Bei jedem Push, der `docs/**` betrifft, führt die Aktion Folgendes aus:

1. Vergleicht den Commit, um herauszufinden, welche Markdown-Dateien tatsächlich geändert wurden.
2. Analysiert jede Datei mittels [Markdig](https://github.com/xoofx/markdig) in einen AST und extrahiert nur den natürlichsprachigen Text – Codeblöcke, Inline-Code und Link-/Bild-URLs werden niemals an den LLM gesendet.
3. Übersetzt den extrahierten Text in jede in `target-languages` aufgeführte Sprache.
4. Fügt die Übersetzungen wieder in die ursprüngliche Dokumentstruktur ein und schreibt das Ergebnis unter `docs/{lang}/...` (konfigurierbar über `output-path-template`).
5. Öffnet einen Pull Request mit den übersetzten Dateien, der mit dem auslösenden Commit verknüpft ist, sodass Wiederholungen idempotent sind.

## Lokaler Probelauf

Sie benötigen keinen echten API-Schlüssel oder GitHub-Token, um es lokal auszuprobieren:

```bash
dotnet run --project src/DocTranslator.Cli -- \
  --pr-mode false --use-fake-llm \
  --target-languages tr,de \
  --source-path docs
```

`--pr-mode false` schreibt die übersetzten Dateien auf die Festplatte, ohne etwas zu pushen, und `--use-fake-llm` tauscht einen trivialen, Marker umschließenden Übersetzer ein, sodass Sie die Ausgabestruktur überprüfen können, ohne API-Guthaben zu verbrauchen.

## Ausgabepfade

Standardmäßig landen übersetzte Dateien unter `docs/{lang}/{relativePath}` – eine türkische Übersetzung von `docs/guide.md` wird zu `docs/tr/guide.md`. Überschreiben Sie `output-path-template` (oder `outputPathTemplate` in der Konfigurationsdatei) mit einer beliebigen Kombination aus `{lang}`, `{dir}`, `{filename}`, `{ext}` und `{relativePath}`, um Ihr eigenes Dokumentlayout anzupassen, z. B. `i18n/{lang}/docusaurus-plugin-content-docs/current/{relativePath}` für Docusaurus.

## Erweiterte Konfiguration

Für Einstellungen, die Sie nicht bei jeder Workflow-Ausführung wiederholen möchten, verweisen Sie `config-path` auf eine JSON-Datei in Ihrem Repository (z. B. `.doc-translator.json`) mit einer der folgenden Optionen: `sourcePath`, `includeGlob`, `outputPathTemplate`, `baseBranch`, `failOnStaleTranslations`, `maxParallelRequests`, `llmProvider` oder die pro-Anbieter-Modellüberschreibungen. Aktionseingaben haben immer Vorrang vor der Konfigurationsdatei, sodass Sie weiterhin einen einzelnen Wert pro Ausführung überschreiben können, ohne diese bearbeiten zu müssen.

## Nächste Schritte

- Lesen Sie [architecture.md](architecture.md), um zu erfahren, wie die AST-Analyse/Übersetzungs-/Rekonstruktionspipeline tatsächlich funktioniert.
- Fügen Sie ein [`.doc-terms.json`](../.doc-terms.json)-Glossar hinzu, um Produktnamen und technische Begriffe unübersetzt zu lassen. Die QA-Prüfung von `custom_mappings` erfordert lediglich, dass die erforderliche Wiedergabe ein Wort in der Übersetzung *beginnt*, nicht vollständig alleine steht – agglutinierende Sprachen wie Türkisch hängen Fallendungen direkt an ein Wort an (z. B. erscheint „depo“ legitimerweise als „depoya“/„deposunu“), wodurch vermieden wird, eine korrekte Übersetzung als Glossarfehler zu kennzeichnen.
- Fügen Sie eine [`.doc-ignore`](../.doc-ignore)-Datei hinzu, um Dateien wie `CHANGELOG.md` von der Übersetzung auszuschließen.
- Im [README](../README.md) finden Sie die vollständige Referenz zu Eingabe/Ausgabe und Kurzanleitungen für Docusaurus/MkDocs.