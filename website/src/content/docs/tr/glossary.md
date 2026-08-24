---
title: Glossary
description: How .doc-terms.json controls which terms are never translated and how per-language renderings are enforced.
order: 3
---

<!-- doc-translator: source-hash=b28134c06383b72ae7b8048d21a8dd80d1e5b4935a7a2c707d2817cf0282e517; source-path=website/src/content/docs/en/glossary.md; target-lang=tr; generated=2026-08-24T17:05:40.6508429+00:00 -->

Deponuzun kök dizinindeki `.doc-terms.json`, hangi terimlerin asla çevrilmeyeceğini, dile göre gerekli çevirileri,
ve isteğe bağlı genel bir tonu kontrol eder — hem çeviri öncesinde (LLM'e komut talimatları olarak) hem de sonrasında (çevrilen çıktı üzerinde bir QA
kontrolü olarak) denetlenir.

Bu, bu projenin kendi deposundan gerçek dosyadır:

```json
{
  "version": 1,
  "case_sensitive": false,
  "dont_translate": ["GitHub", "API", "npm", "Markdig", "Docker", "JSON", "SDK", "LLM"],
  "custom_mappings": {
    "de": { "repository": "Repository", "pull request": "Pull Request" },
    "fr": { "repository": "dépôt", "pull request": "requête de tirage" },
    "tr": { "repository": "depo", "pull request": "değişiklik isteği" }
  },
  "style_guide": "Use a neutral, professional tone. Write instructions directly to the reader."
}
```

## Alanlar

- **`version`** — şema sürümü (şu anda `1`).
- **`case_sensitive`** — terim eşleştirmenin (hem `dont_translate` hem de `custom_mappings`) büyük/küçük harfe duyarlı olup olmadığı.
- **`dont_translate`** — her çıktı dilinde, aynen, çevrilmeden görünmesi gereken terimler. Düz alt dize aramasıyla değil,
  kelime sınırı eşleştirmesiyle kontrol edilir, bu yüzden `API` gibi kısa bir terim, `CAPITAL` gibi daha uzun bir kelimenin içinde hatalı pozitif vermez.
- **`custom_mappings`** — belirli kaynak terimler için dile göre gerekli çeviriler, örn. her zaman çevirme
  "pull request" terimini Türkçede "değişiklik isteği" olarak, LLM'in tutarlı bir çeviri seçmesine güvenmek yerine.
  her seferinde.
- **`style_guide`** — sözlük terimleriyle birlikte LLM'e gönderilen serbest biçimli bir ton talimatı.

## Eklemeli Dil Uyarısı

`custom_mappings`'nın QA kontrolü, gerekli ifadenin çeviride bir kelimeyi **başlatmasını** gerektirir, tamamen tek başına durmasını değil.
Türkçe gibi eklemeli diller, hal eklerini bir kelimeye ayırıcı olmadan doğrudan ekler —
"depo" (repository) kelimesi, dilbilgisel duruma bağlı olarak "depoya", "deposunu" veya "depodan" şeklinde yasal olarak görünür. Bir
sondaki kelime-sınırı kontrolü, çeviri doğru olsa bile bunların her birini bir sözlük hatası olarak işaretleyecektir,
bu yüzden `custom_mappings` için yalnızca önde gelen bir sınır uygulanır. `dont_translate` terimleri her iki
tarafta da tam kelime olarak kalır, çünkü bunlar (`GitHub`, `API`, `SDK`, ...) tamamen değiştirilmeden kalmalıdır.