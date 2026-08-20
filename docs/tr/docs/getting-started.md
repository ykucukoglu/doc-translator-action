<!-- doc-translator: source-hash=4eaaa588658a073d2dfacbf7a3b3f33393faafc2b02dbde9e41d5e2082c32acd; source-path=docs/getting-started.md; target-lang=tr; generated=2026-08-20T17:13:31.6120663+00:00 -->

# Başlarken

Bu kılavuz, belgelerinizin otomatik olarak çevrilmesini sağlamak için `doc-translator-action`'ı bir depoya ekleme adımlarında size yol gösterir.

## Önkoşullar

- Markdown belgeleri içeren bir GitHub deposu (varsayılan olarak, `docs/` altındaki her şey).
- En az bir desteklenen LLM sağlayıcısı için bir API anahtarı: [Google Gemini](https://ai.google.dev/), [OpenAI](https://platform.openai.com/) veya [Anthropic Claude](https://console.anthropic.com/). Bunu bir depo sırrı olarak saklayın, örneğin `GEMINI_API_KEY`.

## Minimal iş akışı

`.github/workflows/translate-docs.yml` oluşturun:

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

İşte bu kadar. `docs/**`'a dokunan her gönderimde, eylem:

1. Hangi Markdown dosyalarının gerçekten değiştiğini bulmak için commit'i karşılaştırır.
2. Her birini [Markdig](https://github.com/xoofx/markdig) aracılığıyla bir AST'ye ayrıştırır ve yalnızca doğal dil metnini çıkarır - kod blokları, satır içi kod ve bağlantı/resim URL'leri hiçbir zaman LLM'e gönderilmez.
3. Çıkarılan metni `target-languages`'da listelenen her dile çevirir.
4. Çevirileri orijinal belge yapısına geri ekler ve sonucu `docs/{lang}/...` altına yazar (`output-path-template` aracılığıyla yapılandırılabilir).
5. Çevrilmiş dosyalarla bir değişiklik isteği açar, tetikleyici commit'e anahtarlanır, böylece yeniden çalıştırmalar tekrarlanabilir olur.

## Yerel deneme çalıştırması

Yerel olarak denemek için gerçek bir API anahtarına veya GitHub token'ına ihtiyacınız yok:

```bash
dotnet run --project src/DocTranslator.Cli -- \
  --pr-mode false --use-fake-llm \
  --target-languages tr,de \
  --source-path docs
```

`--pr-mode false` çevrilmiş dosyaları hiçbir şey göndermeden diske yazar ve `--use-fake-llm` önemsiz bir işaretçi sarmalayıcı çeviriciyi değiştirir, böylece API kredileri harcamadan çıktı yapısını inceleyebilirsiniz.

## Çıktı yolları

Varsayılan olarak, çevrilmiş dosyalar `docs/{lang}/{relativePath}` konumuna gelir - `docs/guide.md`'in Türkçe çevirisi `docs/tr/guide.md` olur. Kendi belge düzeninize uyması için `output-path-template`'ü (veya yapılandırma dosyasındaki `outputPathTemplate`'ü) `{lang}`, `{dir}`, `{filename}`, `{ext}` ve `{relativePath}`'un herhangi bir kombinasyonuyla geçersiz kılın, örneğin Docusaurus için `i18n/{lang}/docusaurus-plugin-content-docs/current/{relativePath}`.

## Gelişmiş yapılandırma

Her iş akışı çalıştırmasında tekrarlamak istemediğiniz ayarlar için, `config-path`'ı depodaki bir JSON dosyasına (örn. `.doc-translator.json`) işaret edin; bu dosya `sourcePath`, `includeGlob`, `outputPathTemplate`, `baseBranch`, `failOnStaleTranslations`, `maxParallelRequests`, `llmProvider` veya sağlayıcı başına model geçersiz kılmalarından herhangi birini içerebilir. Eylem girişleri her zaman yapılandırma dosyasından öncelikli olduğundan, yine de tek bir değeri her çalıştırmada düzenlemeden geçersiz kılabilirsiniz.

## Sonraki adımlar

- AST ayrıştırma/çevirme/yeniden oluşturma hattının gerçekte nasıl çalıştığını öğrenmek için [architecture.md](architecture.md) belgesini okuyun.
- Ürün adlarını ve teknik terimleri çevrilmeden bırakmak için bir [`.doc-terms.json`](../.doc-terms.json) sözlüğü ekleyin. `custom_mappings`'in kalite kontrolü, gerekli oluşturmanın çeviride bir kelimenin *başlangıcında* yer almasını gerektirir, tamamen tek başına durmasını değil - Türkçe gibi eklemeli dillerde kelimeye doğrudan durum ekleri eklenir (örn. "depo" yasal olarak "depoya"/"deposunu" olarak görünür), bu da doğru bir çevirinin sözlükte eksik olarak işaretlenmesini önler.
- `CHANGELOG.md` gibi dosyaları çeviriden hariç tutmak için bir [`.doc-ignore`](../.doc-ignore) dosyası ekleyin.
- Tüm girdi/çıktı referansı ve Docusaurus/MkDocs hızlı başlangıç kod parçacıkları için [README](../README.md) belgesine bakın.