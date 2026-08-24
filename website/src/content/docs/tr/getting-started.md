---
title: Getting started
description: Add doc-translator-action to a repository so your documentation stays translated automatically.
order: 1
---

<!-- doc-translator: source-hash=f925d3ef7b90dfa1b41b1f2efdfbc9e87da7789e1565fbb54704bc29eb29fe04; source-path=website/src/content/docs/en/getting-started.md; target-lang=tr; generated=2026-08-24T17:06:35.6650207+00:00 -->

Bu kılavuz, dokümantasyonunuzun otomatik olarak çevrilmiş kalması için `doc-translator-action` öğesini bir depoya nasıl ekleyeceğinizi gösterir.

## Önkoşullar

- Markdown dokümantasyonu içeren bir GitHub deposu (varsayılan olarak, `docs/` altındaki her şey).
- Desteklenen en az bir LLM sağlayıcısı için bir API anahtarı: [Google Gemini](https://ai.google.dev/), [OpenAI](https://platform.openai.com/), [Anthropic Claude](https://console.anthropic.com/) veya Azure OpenAI. Bunu bir depo sırrı olarak saklayın, örneğin `GEMINI_API_KEY`.

## Minimal iş akışı

`.github/workflows/translate-docs.yml` oluşturun — veya ana sayfadaki [İş Akışı Oluşturucu](/#workflow-generator) ile kurulumunuza özel bu kodu oluşturun:

```yaml
name: Translate Docs
on:
  push:
    branches: [main]
    paths: ['docs/**']
jobs:
  translate:
    runs-on: ubuntu-latest
    timeout-minutes: 15
    permissions:
      contents: write
      pull-requests: write
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 2 # doc-translator-action diffs against the previous commit
      - uses: ykucukoglu/doc-translator-action@v1
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          gemini-api-key: ${{ secrets.GEMINI_API_KEY }}
          target-languages: tr,de
```

Bu kadar. `docs/**`'ı etkileyen her göndermede (push), eylem:

1. Değişen Markdown dosyalarını bulmak için commit'i karşılaştırır.
2. Her birini [Markdig](https://github.com/xoofx/markdig) aracılığıyla bir AST'ye ayrıştırır ve yalnızca doğal dildeki metni çıkarır — kod blokları, satır içi kod ve bağlantı/resim URL'leri asla LLM'ye gönderilmez.
3. Çıkarılan metni `target-languages`'da listelenen her dile çevirir.
4. Çevirileri orijinal belge yapısına geri ekler ve sonucu `docs/{lang}/...` altına yazar (`output-path-template` aracılığıyla yapılandırılabilir).
5. Çevrilmiş dosyalarla bir değişiklik isteği açar, tetikleyici commit'e anahtarlı olduğu için yeniden çalıştırmalar idempotent'tir.

## Yerel deneme çalıştırması

Yerel olarak denemek için gerçek bir API anahtarına veya GitHub token'ına ihtiyacınız yok:

```bash
dotnet run --project src/DocTranslator.Cli -- \
  --pr-mode false --use-fake-llm \
  --target-languages tr,de \
  --source-path docs
```

`--pr-mode false`, çevrilmiş dosyaları hiçbir şey göndermeden diske yazar ve `--use-fake-llm`, basit bir işaretçi sarıcı çevirici ile değiştirilir, böylece API kredisi harcamadan çıktı yapısını inceleyebilirsiniz.

## Çıktı yolları

Varsayılan olarak, çevrilmiş dosyalar `docs/{lang}/{relativePath}` konumuna gelir — `docs/guide.md`'in Türkçe çevirisi `docs/tr/guide.md` olur. Kendi belge düzeninize uyması için `output-path-template`'ü `{lang}`, `{dir}`, `{filename}`, `{ext}` ve `{relativePath}`'in herhangi bir kombinasyonu ile geçersiz kılın — her girdi için [Yapılandırma](/configuration)'ya veya hazır Docusaurus/Starlight/MkDocs tarifleri için İş Akışı Oluşturucu'ya bakın.

## Gelişmiş yapılandırma

Her iş akışı çalıştırmasında tekrarlamak istemediğiniz ayarlar için, `config-path`'ı, [Yapılandırma](/configuration) referansında listelenen gizli olmayan girdilerden herhangi birini içeren deponuzdaki bir JSON dosyasına yönlendirin. Eylem girdileri her zaman yapılandırma dosyasından öncelikli olacaktır, bu nedenle her çalıştırmada tek bir değeri düzenlemeden geçersiz kılabilirsiniz.

## Sonraki adımlar

- [Mimari](/architecture) sayfasını okuyarak AST ayrıştırma/çevirme/yeniden yapılandırma ardışık düzeninin gerçekte nasıl çalıştığını öğrenin.
- Ürün adlarını ve teknik terimleri çevrilmeden tutmak için [Sözlük](/glossary) sayfasını okuyun.
- Tüm girdi/çıktı listesi için [Yapılandırma](/configuration) referansına bakın.