import schema from '../data/action-schema.generated.json';

export interface ActionInput {
  name: string;
  description: string;
  required: boolean;
  default: string | null;
}

export interface ActionOutput {
  name: string;
  description: string;
}

export const actionInputs: ActionInput[] = schema.inputs;
export const actionOutputs: ActionOutput[] = schema.outputs;

/**
 * Inputs surfaced as top-level fields in the Workflow Generator and shown first on
 * /configuration. Everything else (secrets, power-user/CI-only inputs) still appears on
 * /configuration - generated from the same schema - just not as a form control.
 */
export const CORE_INPUT_NAMES = new Set([
  'llm-provider',
  'target-languages',
  'output-path-template',
  'source-path',
  'pr-mode',
  'backfill-missing-translations',
]);

/** *-api-key inputs are never rendered as text fields - see WorkflowGenerator.astro. */
export const SECRET_INPUT_NAMES = new Set([
  'github-token',
  'gemini-api-key',
  'openai-api-key',
  'anthropic-api-key',
  'azure-openai-api-key',
]);

export function getInput(name: string): ActionInput | undefined {
  return actionInputs.find((input) => input.name === name);
}
