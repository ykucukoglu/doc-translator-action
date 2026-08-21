// @ts-check
import { defineConfig } from 'astro/config';

// Published as a GitHub Pages *project* site (ykucukoglu.github.io/doc-translator-action/),
// not a root domain - every internal link must go through src/utils/url.ts's withBase()
// helper, since Astro does not rewrite hand-written <a href="/x"> for a non-root base.
export default defineConfig({
  site: 'https://ykucukoglu.github.io',
  base: '/doc-translator-action',
  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
    routing: {
      prefixDefaultLocale: false,
    },
  },
});
