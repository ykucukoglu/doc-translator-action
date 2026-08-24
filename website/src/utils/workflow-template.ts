// Builds a real, ready-to-paste GitHub Actions workflow, mirroring the exact formatting
// conventions used in the README's own recipes (comment placement, step ordering, pinned
// @v1 action ref) - deliberately a template-literal string builder, not a generic YAML
// serializer, so the output reads exactly like hand-written README content, not machine YAML.

export type Provider = 'auto' | 'gemini' | 'openai' | 'claude' | 'azure-openai';
export type OutputRecipe = 'standard' | 'docusaurus' | 'starlight' | 'mkdocs' | 'custom';
export type RunMode = 'pr' | 'dry-run';

export interface WorkflowState {
  provider: Provider;
  targetLanguages: string;
  outputRecipe: OutputRecipe;
  customOutputPathTemplate: string;
  sourcePath: string;
  runMode: RunMode;
  persistCache: boolean;
  backfill: boolean;

  fallbackProvider: Provider | '';
  geminiModel: string;
  openaiModel: string;
  openaiBaseUrl: string;
  claudeModel: string;
  azureEndpoint: string;
  azureDeployment: string;
  maxParallelRequests: number;
  maxBatchTokens: number;
  sourceLanguage: string;
  translateMermaid: boolean;
  translateFrontmatter: boolean;
  pushToCurrentBranch: boolean;
  failOnStale: boolean;
  cleanupStaleBranches: boolean;
  baseBranch: string;
}

export const DEFAULT_WORKFLOW_STATE: WorkflowState = {
  provider: 'gemini',
  targetLanguages: 'tr',
  outputRecipe: 'standard',
  customOutputPathTemplate: '',
  sourcePath: 'docs',
  runMode: 'pr',
  persistCache: true,
  backfill: false,

  fallbackProvider: '',
  geminiModel: '',
  openaiModel: '',
  openaiBaseUrl: '',
  claudeModel: '',
  azureEndpoint: '',
  azureDeployment: '',
  maxParallelRequests: 4,
  maxBatchTokens: 4000,
  sourceLanguage: 'auto',
  translateMermaid: false,
  translateFrontmatter: false,
  pushToCurrentBranch: false,
  failOnStale: false,
  cleanupStaleBranches: true,
  baseBranch: '',
};

const RECIPE_OUTPUT_PATH: Record<Exclude<OutputRecipe, 'custom'>, string | null> = {
  standard: null, // matches the input's real default (docs/{lang}/{relativePath}) - omit
  docusaurus: 'i18n/{lang}/docusaurus-plugin-content-docs/current/{relativePath}',
  starlight: 'src/content/docs/{lang}/{relativePath}',
  mkdocs: '{dir}/{filename}.{lang}.{ext}',
};

const RECIPE_SOURCE_PATH: Record<Exclude<OutputRecipe, 'custom'>, string> = {
  standard: 'docs',
  docusaurus: 'docs',
  starlight: 'src/content/docs',
  mkdocs: 'docs',
};

const SECRET_NAME_FOR_PROVIDER: Record<Provider, string | null> = {
  auto: null,
  gemini: 'GEMINI_API_KEY',
  openai: 'OPENAI_API_KEY',
  claude: 'ANTHROPIC_API_KEY',
  'azure-openai': 'AZURE_OPENAI_API_KEY',
};

const KEY_INPUT_FOR_PROVIDER: Record<Provider, string | null> = {
  auto: null,
  gemini: 'gemini-api-key',
  openai: 'openai-api-key',
  claude: 'anthropic-api-key',
  'azure-openai': 'azure-openai-api-key',
};

/** Secret names the "Secrets you'll need" callout should list for the current selection. */
export function secretsNeeded(state: WorkflowState): string[] {
  const names: string[] = [];
  if (state.runMode === 'pr' && !state.pushToCurrentBranch) {
    names.push('GITHUB_TOKEN (usually already available - no setup needed)');
  }

  if (state.provider === 'auto') {
    names.push('exactly one of: GEMINI_API_KEY, OPENAI_API_KEY, ANTHROPIC_API_KEY, AZURE_OPENAI_API_KEY');
  } else {
    const secret = SECRET_NAME_FOR_PROVIDER[state.provider];
    if (secret) names.push(secret);
  }

  const fallbackSecret = state.fallbackProvider ? SECRET_NAME_FOR_PROVIDER[state.fallbackProvider] : null;
  if (fallbackSecret) names.push(fallbackSecret);

  return names;
}

function withLine(key: string, value: string | number | boolean): string {
  return `          ${key}: ${value}`;
}

export function buildWorkflowYaml(state: WorkflowState): string {
  const withLines: string[] = [];

  if (!(state.runMode === 'dry-run') || state.pushToCurrentBranch) {
    withLines.push(withLine('github-token', '${{ secrets.GITHUB_TOKEN }}'));
  }

  if (state.provider !== 'auto') {
    const keyInput = KEY_INPUT_FOR_PROVIDER[state.provider];
    const secret = SECRET_NAME_FOR_PROVIDER[state.provider];
    if (keyInput && secret) {
      withLines.push(withLine(keyInput, `\${{ secrets.${secret} }}`));
    }
    withLines.push(withLine('llm-provider', state.provider));
  } else {
    withLines.push(withLine('gemini-api-key', '${{ secrets.GEMINI_API_KEY }}'));
  }

  if (state.provider === 'gemini' && state.geminiModel) withLines.push(withLine('gemini-model', state.geminiModel));
  if (state.provider === 'openai' && state.openaiModel) withLines.push(withLine('openai-model', state.openaiModel));
  if (state.provider === 'openai' && state.openaiBaseUrl) withLines.push(withLine('openai-base-url', state.openaiBaseUrl));
  if (state.provider === 'claude' && state.claudeModel) withLines.push(withLine('claude-model', state.claudeModel));
  if (state.provider === 'azure-openai' && state.azureEndpoint) withLines.push(withLine('azure-openai-endpoint', state.azureEndpoint));
  if (state.provider === 'azure-openai' && state.azureDeployment) withLines.push(withLine('azure-openai-deployment', state.azureDeployment));

  if (state.fallbackProvider) {
    withLines.push(withLine('llm-fallback-provider', state.fallbackProvider));
    const fallbackKeyInput = KEY_INPUT_FOR_PROVIDER[state.fallbackProvider];
    const fallbackSecret = SECRET_NAME_FOR_PROVIDER[state.fallbackProvider];
    if (fallbackKeyInput && fallbackSecret) {
      withLines.push(withLine(fallbackKeyInput, `\${{ secrets.${fallbackSecret} }}`));
    }
  }

  withLines.push(withLine('target-languages', state.targetLanguages || 'tr'));

  const recipeSourcePath = state.outputRecipe === 'custom' ? state.sourcePath : RECIPE_SOURCE_PATH[state.outputRecipe];
  if (recipeSourcePath && recipeSourcePath !== 'docs') {
    withLines.push(withLine('source-path', recipeSourcePath));
  }

  const outputPath = state.outputRecipe === 'custom' ? state.customOutputPathTemplate : RECIPE_OUTPUT_PATH[state.outputRecipe];
  if (outputPath) {
    withLines.push(withLine('output-path-template', `'${outputPath}'`));
  }

  if (state.pushToCurrentBranch) {
    withLines.push(withLine('push-to-current-branch', true));
  } else if (state.runMode === 'dry-run') {
    withLines.push(withLine('dry-run', true));
  }

  if (state.backfill) withLines.push(withLine('backfill-missing-translations', true));
  if (state.sourceLanguage !== 'auto') withLines.push(withLine('source-language', state.sourceLanguage));
  if (state.maxParallelRequests !== 4) withLines.push(withLine('max-parallel-requests', state.maxParallelRequests));
  if (state.maxBatchTokens !== 4000) withLines.push(withLine('max-batch-tokens', state.maxBatchTokens));
  if (state.translateMermaid) withLines.push(withLine('translate-mermaid-diagrams', true));
  if (state.translateFrontmatter) withLines.push(withLine('translate-frontmatter-fields', true));
  if (state.failOnStale) withLines.push(withLine('fail-on-stale-translations', true));
  if (!state.cleanupStaleBranches) withLines.push(withLine('cleanup-stale-branches', false));
  if (state.baseBranch) withLines.push(withLine('base-branch', state.baseBranch));

  const cacheStep = state.persistCache
    ? `      # Persists the content-hash translation cache across runs - without this, every run
      # starts from an empty cache and re-translates unrelated unchanged chunks needlessly.
      - uses: actions/cache@v4
        with:
          path: .doc-translator-cache
          key: doc-translator-cache-\${{ github.run_id }}
          restore-keys: |
            doc-translator-cache-
`
    : '';

  return `name: Translate Docs
on:
  push:
    branches: [main]
    paths: ['${recipeSourcePath || 'docs'}/**']
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
          fetch-depth: 2
${cacheStep}      - uses: ykucukoglu/doc-translator-action@v1
        with:
${withLines.join('\n')}
`;
}
