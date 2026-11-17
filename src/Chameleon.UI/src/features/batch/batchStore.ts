import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../../bridge/Bridge';
import type { AssetRef, JobInfo, RpcError } from '../../bridge/types';
import type { MessageKey } from '../../i18n';
import type { AppStoreState } from '../../state/appStore';
import { isFinished, type JobsStoreState } from '../../state/jobsStore';
import { status, type StatusMessage } from '../../state/status';
import { buildGenerateParams, type ParamsStoreState } from '../params/paramsStore';

/** One named view of the document. */
export interface BatchItem {
  viewName: string;
  selected: boolean;
  source: AssetRef | null;
  capturing: boolean;
  captureFailed: boolean;
  addonPrompt: string;
  /** Latest job for this view (batch run or single regeneration). */
  jobId: string | null;
  result: AssetRef | null;
  error: RpcError | null;
}

export interface BatchStoreState {
  items: BatchItem[];
  loading: boolean;
  /** Group of the latest batch run. */
  groupId: string | null;
  /** Jobs of the latest batch run, in render order. */
  runJobIds: string[];
  saving: boolean;
  status: StatusMessage | null;

  loadViews(): Promise<void>;
  recapture(viewName: string): Promise<void>;
  setSelected(viewName: string, selected: boolean): void;
  selectAll(selected: boolean): void;
  setAddonPrompt(viewName: string, text: string): void;
  render(): Promise<void>;
  regenerate(viewName: string): Promise<void>;
  cancel(): Promise<void>;
  saveItem(viewName: string): Promise<void>;
  saveAll(): Promise<void>;
  setStatus(message: StatusMessage | null): void;
}

export interface BatchStoreDeps {
  bridge: Bridge;
  app: StoreApi<AppStoreState>;
  jobs: StoreApi<JobsStoreState>;
  params: StoreApi<ParamsStoreState>;
}

/** The status key shown on a view's row. */
export function itemStatusKey(item: BatchItem, job: JobInfo | undefined): MessageKey {
  if (item.capturing) {
    return 'item.capturing';
  }
  if (job) {
    switch (job.state) {
      case 'queued':
        return 'item.queued';
      case 'running':
        return 'item.generating';
      case 'succeeded':
        return 'item.done';
      case 'cancelled':
        return 'item.cancelled';
      default:
        return 'item.failed';
    }
  }
  if (item.captureFailed) {
    return 'item.captureFailed';
  }
  return item.source ? 'item.ready' : 'item.waiting';
}

/** Progress of a batch run. */
export function runProgress(runJobIds: readonly string[], jobs: Record<string, JobInfo>) {
  const runJobs = runJobIds.map((id) => jobs[id]);
  return {
    total: runJobs.length,
    done: runJobs.filter(isFinished).length,
    succeeded: runJobs.filter((j) => j?.state === 'succeeded').length,
    failed: runJobs.filter((j) => j?.state === 'failed').length,
    cancelled: runJobs.filter((j) => j?.state === 'cancelled').length,
  };
}

/** True while any view has an unfinished job. */
export function isRunning(items: readonly BatchItem[], jobs: Record<string, JobInfo>): boolean {
  return items.some((item) => item.jobId !== null && !isFinished(jobs[item.jobId]));
}

/** Batch mode: render every selected named view, keeping the results consistent. */
export function createBatchStore({ bridge, app, jobs, params }: BatchStoreDeps): StoreApi<BatchStoreState> {
  const store = createStore<BatchStoreState>()((set, get) => ({
    items: [],
    loading: false,
    groupId: null,
    runJobIds: [],
    saving: false,
    status: status.info('placeholder.noViews'),

    async loadViews() {
      set({ loading: true });
      try {
        const views = await bridge.call('rhino.listNamedViews');
        if (views.length === 0) {
          set({ items: [], groupId: null, runJobIds: [], status: status.info('batch.noNamedViews') });
          return;
        }

        set({
          items: views.map((view) => ({
            viewName: view.name,
            selected: true,
            source: null,
            capturing: true,
            captureFailed: false,
            addonPrompt: '',
            jobId: null,
            result: null,
            error: null,
          })),
          groupId: null,
          runJobIds: [],
          status: status.busy('batch.loading', { count: views.length }),
        });

        // One at a time: every capture moves the same Rhino viewport.
        for (const view of views) {
          await captureOne(view.name);
        }

        const items = get().items;
        set({
          status: status.success('batch.loaded', {
            captured: items.filter((i) => i.source).length,
            total: items.length,
          }),
        });
      } catch (error) {
        set({ status: status.error(error) });
      } finally {
        set({ loading: false });
      }
    },

    async recapture(viewName) {
      updateItem(viewName, { capturing: true });
      await captureOne(viewName);
    },

    setSelected: (viewName, selected) => updateItem(viewName, { selected }),

    selectAll: (selected) => set((state) => ({ items: state.items.map((item) => ({ ...item, selected })) })),

    setAddonPrompt: (viewName, addonPrompt) => updateItem(viewName, { addonPrompt }),

    async render() {
      const chosen = get().items.filter((item) => item.selected && item.source);
      if (chosen.length === 0) {
        set({ status: status.info('batch.nothingSelected') });
        return;
      }
      if (!params.getState().prompt.trim()) {
        set({ status: status.info('status.promptRequired') });
        return;
      }

      const appState = app.getState();
      const requests = chosen.map((item) =>
        buildGenerateParams(params.getState(), appState, item.source!.assetId, {
          addonPrompt: item.addonPrompt || undefined,
          label: item.viewName,
        }),
      );

      try {
        const batch = await jobs.getState().submitBatch(requests, appState.settings?.batch.mode ?? 'serial');
        const jobIdByView = new Map(
          chosen.map((item, index) => [item.viewName, batch.jobs[index]?.id ?? null]),
        );
        set((state) => ({
          groupId: batch.groupId,
          runJobIds: batch.jobs.map((j) => j.id),
          status: status.busy('batch.rendering', { count: batch.jobs.length }),
          items: state.items.map((item) =>
            jobIdByView.has(item.viewName)
              ? { ...item, jobId: jobIdByView.get(item.viewName) ?? null, result: null, error: null }
              : item,
          ),
        }));
        sync(jobs.getState().jobs);
      } catch (error) {
        set({ status: status.error(error) });
      }
    },

    async regenerate(viewName) {
      const item = get().items.find((i) => i.viewName === viewName);
      if (!item?.source) {
        return;
      }
      const jobMap = jobs.getState().jobs;
      // The other finished views keep the regenerated one consistent with them.
      const dependsOn = get()
        .items.filter(
          (other) => other.viewName !== viewName && other.jobId && jobMap[other.jobId]?.state === 'succeeded',
        )
        .map((other) => other.jobId!);

      try {
        const job = await jobs.getState().submit(
          buildGenerateParams(params.getState(), app.getState(), item.source.assetId, {
            addonPrompt: item.addonPrompt || undefined,
            label: viewName,
            dependsOn,
          }),
        );
        updateItem(viewName, { jobId: job.id, error: null });
        sync(jobs.getState().jobs);
      } catch (error) {
        set({ status: status.error(error) });
      }
    },

    async cancel() {
      const { groupId, items } = get();
      const jobMap = jobs.getState().jobs;
      const tasks: Promise<unknown>[] = [];
      if (groupId) {
        tasks.push(jobs.getState().cancelGroup(groupId));
      }
      for (const item of items) {
        if (item.jobId && !isFinished(jobMap[item.jobId])) {
          tasks.push(jobs.getState().cancel(item.jobId));
        }
      }
      await Promise.all(tasks.map((task) => task.catch(() => undefined)));
    },

    async saveItem(viewName) {
      const item = get().items.find((i) => i.viewName === viewName);
      if (!item?.result) {
        return;
      }
      try {
        const saved = await bridge.call('files.saveImage', {
          assetId: item.result.assetId,
          suggestedName: viewName,
        });
        if (saved) {
          set({ status: status.success('status.saved', { path: saved.path }) });
        }
      } catch (error) {
        set({ status: status.error(error) });
      }
    },

    async saveAll() {
      const done = get().items.filter((i) => i.result);
      if (done.length === 0) {
        return;
      }
      set({ saving: true });
      try {
        const saved = await bridge.call('files.saveMany', {
          items: done.map((i) => ({ assetId: i.result!.assetId, fileName: i.viewName })),
        });
        if (saved) {
          set({ status: status.success('status.savedMany', { count: saved.saved, folder: saved.folder }) });
        }
      } catch (error) {
        set({ status: status.error(error) });
      } finally {
        set({ saving: false });
      }
    },

    setStatus: (message) => set({ status: message }),
  }));

  function updateItem(viewName: string, patch: Partial<BatchItem>): void {
    store.setState((state) => ({
      items: state.items.map((item) => (item.viewName === viewName ? { ...item, ...patch } : item)),
    }));
  }

  async function captureOne(viewName: string): Promise<void> {
    try {
      const source = await bridge.call('rhino.captureNamedView', { name: viewName });
      updateItem(viewName, {
        source,
        capturing: false,
        captureFailed: false,
        result: null,
        jobId: null,
        error: null,
      });
    } catch {
      updateItem(viewName, { capturing: false, captureFailed: true });
    }
  }

  /** Copies finished job outcomes onto their rows and reports when the run is over. */
  function sync(jobMap: Record<string, JobInfo>): void {
    const state = store.getState();
    let changed = false;
    const items = state.items.map((item) => {
      const job = item.jobId ? jobMap[item.jobId] : undefined;
      if (!job || !isFinished(job)) {
        return item;
      }
      const result = job.state === 'succeeded' ? (job.result?.assets[0] ?? null) : item.result;
      const error = job.state === 'failed' ? (job.error ?? null) : null;
      if (result === item.result && error === item.error) {
        return item;
      }
      changed = true;
      return { ...item, result, error };
    });

    const updates: Partial<BatchStoreState> = changed ? { items } : {};
    const progress = runProgress(state.runJobIds, jobMap);
    if (state.status?.kind === 'busy' && state.runJobIds.length > 0 && progress.done === progress.total) {
      updates.status =
        progress.cancelled > 0
          ? status.info('batch.cancelled', {
              done: progress.succeeded + progress.failed,
              total: progress.total,
            })
          : status.success('batch.allDone', { succeeded: progress.succeeded, failed: progress.failed });
    }
    if (Object.keys(updates).length > 0) {
      store.setState(updates);
    }
  }

  jobs.subscribe((state) => sync(state.jobs));
  return store;
}
