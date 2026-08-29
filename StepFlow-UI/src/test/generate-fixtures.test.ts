import { describe, it, expect } from 'vitest';
import { writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { FlowService } from '@services/flowService';
import { useNodeStore } from '@stores/useNodeStore';

/**
 * One-off generator (dropped after verification): instantiates each flow
 * template and writes its exact exported ASL to smoke/templates-export/*.json.
 */
const OUT_DIR = join(process.cwd(), '..', 'smoke', 'templates-export');
const TEMPLATE_IDS = [
  'tpl-eav-row-processing',
  'tpl-refund-review',
  'tpl-data-exchange-pipeline',
  'tpl-dynamic-api-roundtrip',
  'tpl-human-task-approval',
];

describe('fixture generator (one-off)', () => {
  it('exports the three new templates as ASL fixtures', async () => {
    mkdirSync(OUT_DIR, { recursive: true });
    for (const id of TEMPLATE_IDS) {
      useNodeStore.setState({ nodes: [] });
      const res = await FlowService.instantiateTemplate(id);
      expect(res.success).toBe(true);

      const def = FlowService.exportFlow();
      writeFileSync(join(OUT_DIR, `${id}.json`), JSON.stringify(def, null, 2));
    }
  });
});
