import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../bridge/Bridge';
import type { BatchMode, BatchResult, GenerateParams, JobInfo, JobStateName } from '../bridge/types';

export interface JobsStoreState {
  /** Jobs by id. */
  jobs: Record<string, JobInfo>;

  /** Reloads every job from the host (after a reconnect). */
  load(): Promise<void>;
  submit(params: GenerateParams): Promise<JobInfo>;
  submitBatch(items: GenerateParams[], mode: BatchMode): Promise<BatchResult>;
  cancel(jobId: string): Promise<void>;
  cancelGroup(groupId: string): Promise<void>;
}

const stateRank: Record<JobStateName, number> = {
  queued: 0,
  running: 1,
  succeeded: 2,
  failed: 2,
  cancelled: 2,
};

/** True when the job can no longer change. */
export function isFinished(job: JobInfo | undefined): boolean {
  return !!job && stateRank[job.state] === 2;
}

/**
 * Keeps the newer of two snapshots. Events and RPC answers travel on different connections, so
 * a "queued" answer can arrive after the "running" event of the same job.
 */
export function newerJob(current: JobInfo | undefined, incoming: JobInfo): JobInfo {
  if (!current || stateRank[incoming.state] >= stateRank[current.state]) {
    return incoming;
  }
  return current;
}

/** Creates the job store and keeps it in sync with host events. */
export function createJobsStore(bridge: Bridge): StoreApi<JobsStoreState> {
  const store: StoreApi<JobsStoreState> = createStore<JobsStoreState>()((set, get) => ({
    jobs: {},

    async load() {
      const list = await bridge.call('jobs.list');
      set((state) => {
        const jobs = { ...state.jobs };
        for (const job of list) {
          jobs[job.id] = newerJob(jobs[job.id], job);
        }
        return { jobs };
      });
    },

    async submit(params) {
      const job = await bridge.call('jobs.submit', params);
      upsert(job);
      return get().jobs[job.id] ?? job;
    },

    async submitBatch(items, mode) {
      const result = await bridge.call('jobs.submitBatch', { items, mode });
      for (const job of result.jobs) {
        upsert(job);
      }
      return result;
    },

    async cancel(jobId) {
      await bridge.call('jobs.cancel', { jobId });
    },

    async cancelGroup(groupId) {
      await bridge.call('jobs.cancel', { groupId });
    },
  }));

  function upsert(job: JobInfo): void {
    store.setState((state) => ({ jobs: { ...state.jobs, [job.id]: newerJob(state.jobs[job.id], job) } }));
  }

  bridge.on('job.updated', upsert);
  bridge.onConnectionChange((connection) => {
    if (connection === 'connected') {
      void store
        .getState()
        .load()
        .catch(() => undefined);
    }
  });

  return store;
}
