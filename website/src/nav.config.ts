export interface NavEntry {
  label: string;
  href: string;
}

export const nav: NavEntry[] = [
  { label: 'Getting started', href: '/getting-started' },
  { label: 'Architecture', href: '/architecture' },
  { label: 'Configuration', href: '/configuration' },
  { label: 'Glossary', href: '/glossary' },
  // Phase 2: add { label: 'AST Explorer', href: '/ast-explorer' } here
];
