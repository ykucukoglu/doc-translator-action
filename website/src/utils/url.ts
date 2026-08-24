/**
 * This site is published as a GitHub Pages *project* site (a non-root base path), and Astro
 * does not rewrite hand-written `<a href="/x">` for that case - every internal link must be
 * built through this helper instead. See the GH Pages base-path note in the website plan.
 */
export function withBase(pathname: string): string {
  const base = import.meta.env.BASE_URL;
  const normalizedBase = base.endsWith('/') ? base : `${base}/`;
  return normalizedBase + pathname.replace(/^\//, '');
}
