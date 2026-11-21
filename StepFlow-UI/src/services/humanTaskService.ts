// ═══════════════════════════════════════════════════════════
// Human Task Service — browser client for the backend human-task API.
// Human tasks are created by the engine when a flow reaches a
// stepflow:human:task state; completing one here resumes (or ends)
// the execution server-side.
// ═══════════════════════════════════════════════════════════

export interface HumanTask {
  taskId: string;
  status: 'Pending' | 'Completed' | string;
  executionId: string;
  stateMachineName: string;
  stateName: string;
  nextState?: string;
  isEnd?: boolean;
  title?: string;
  assignee?: string;
  /** e.g. 'form' (complete via the form-capture page) or 'json' (free-form result). */
  completionType?: string;
  createdAtUtc: string;
  completedAtUtc?: string;
  payload?: { input?: unknown; task?: unknown };
  /** Only present on single-task GET. */
  resultPath?: string;
  /** Only present on single-task GET, when the task is completed. */
  result?: unknown;
}

async function fail(res: Response): Promise<never> {
  let message = `Request failed with status ${res.status}`;
  try {
    const body = (await res.json()) as { error?: string };
    if (body && typeof body.error === 'string' && body.error.length > 0) message = body.error;
  } catch { /* non-JSON error body - keep the status message */ }
  throw new Error(message);
}

export const HumanTaskService = {
  /** List human tasks, optionally filtered by status ('Pending'|'Completed') and/or executionId. */
  async list(status?: string, executionId?: string): Promise<HumanTask[]> {
    const params = new URLSearchParams();
    if (status) params.set('status', status);
    if (executionId) params.set('executionId', executionId);
    const qs = params.toString();
    const res = await fetch(`/api/human-tasks${qs ? `?${qs}` : ''}`);
    if (!res.ok) return fail(res);
    return (await res.json()) as HumanTask[];
  },

  /** A single task with its full payload and (when completed) result. */
  async get(taskId: string): Promise<HumanTask> {
    const res = await fetch(`/api/human-tasks/${encodeURIComponent(taskId)}`);
    if (!res.ok) return fail(res);
    return (await res.json()) as HumanTask;
  },

  /**
   * Complete a pending task with the human's result. The backend accepts any JSON token;
   * pass null/undefined for an empty approval ({}) — e.g. a plain "approve" decision.
   * Throws with the backend message on 409 Conflict (already completed) or 404.
   */
  async complete(taskId: string, result: unknown): Promise<HumanTask> {
    const res = await fetch(`/api/human-tasks/${encodeURIComponent(taskId)}/complete`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(result ?? {}),
    });
    if (!res.ok) return fail(res);
    return (await res.json()) as HumanTask;
  },
};
