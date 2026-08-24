// @ts-check
import { defineConfig } from 'astro/config';
import sitemap from '@astrojs/sitemap';

// Published as a GitHub Pages *project* site (ykucukoglu.github.io/doc-translator-action/),
// not a root domain - every internal link must go through src/utils/url.ts's withBase()
// helper, since Astro does not rewrite hand-written <a href="/x"> for a non-root base.
export default defineConfig({
  site: 'https://ykucukoglu.github.io',
  base: '/doc-translator-action',
  i18n: {
    defaultLocale: 'en',
    // 'tr' is the first real dogfooded locale (see .github/workflows/website-dogfooding.yml) -
    // [...slug].astro's routing already derives locale/path generically from each content entry's
    // id, so this is the only config change adding it needs; it's harmless to list here even
    // before any tr/*.md file exists (no content -> no routes for it yet).
    locales: ['en', 'tr'],
    routing: {
      prefixDefaultLocale: false,
    },
  },
  // Reads `site`/`base` above automatically - emits dist/sitemap-index.xml + dist/sitemap-0.xml
  // at build time, one <url> per static route, already base-path-correct with no extra config.
  integrations: [sitemap()],
});
