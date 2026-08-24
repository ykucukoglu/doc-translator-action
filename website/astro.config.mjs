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
    // Deliberately just 'en' for now - website-dogfooding.yml (kept, dormant) already proved a
    // real 'tr' locale works end to end (translated content, routing, a language switcher), but
    // shipping only 2 of the site's ~6 pages in a second language read as more unfinished than
    // useful. [...slug].astro's routing derives locale/path generically from each content entry's
    // id already, so re-adding 'tr' here (and nothing else) is the only step needed if that
    // changes later.
    locales: ['en'],
    routing: {
      prefixDefaultLocale: false,
    },
  },
  // Reads `site`/`base` above automatically - emits dist/sitemap-index.xml + dist/sitemap-0.xml
  // at build time, one <url> per static route, already base-path-correct with no extra config.
  integrations: [sitemap()],
});
