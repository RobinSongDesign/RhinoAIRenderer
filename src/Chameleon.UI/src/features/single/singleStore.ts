import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../../bridge/Bridge';
import { BridgeError } from '../../bridge/BridgeError';
import type { AssetRef, JobInfo } from '../../bridge/types';
import { defaultResultName, encodeAs, matchesFormat, type OutputFormat } from '../../imaging/imaging';
import type { AppStoreState } from '../../state/appStore';
import { isFinished, type JobsStoreState } from '../../state/jobsStore';
import { status, type StatusMessage } from '../../state/status';
import { buildGenerateParams, type ParamsStoreState } from '../params/paramsStore';

export interface SingleStoreState {
  source: AssetRef | null;
  result: AssetRef | null;
  activeJobId: string | null;
  capturing: boolean;
  saving: boolean;
  status: StatusMessage | null;

  capture(): Promise<void>;
  generate(): Promise<void>;
  cancel(): Promise<void>;
  save(format: OutputFormat): Promise<void>;
  useResultAsSource(): void;
  clear(): void;
  setStatus(message: StatusMessage | null): void;
}

export interface SingleStoreDeps {
  bridge: Bridge;
  app: StoreApi<AppStoreState>;
  jobs: StoreApi<JobsStoreState>;
  params: StoreApi<ParamsStoreState>;
}

/** Single-image mode: one source, one result, one job at a time. */
export function createSingleStore({
  bridge,
  app,
  jobs,
  params,
}: SingleStoreDeps): StoreApi<SingleStoreState> {
  const store = createStore<SingleStoreState>()((set, get) => ({
    source: null,
    result: null,
    activeJobId: null,
    capturing: false,
    saving: false,
    status: null,

    async capture() {
      set({ capturing: true, status: status.busy('status.capturing') });
      try {
        const source = await bridge.call('rhino.captureActiveView');
        set({
          source,
          result: null,
          status: status.success('status.captured', { width: source.width, height: source.height }),
        });
      } catch (error) {
        set({ status: status.error(error) });
      } finally {
        set({ capturing: false });
      }
    },

    async generate() {
      const { source, activeJobId } = get();
      if (activeJobId) {
        return;
      }
      if (!source) {
        set({ status: status.info('status.sourceRequired') });
        return;
      }
      if (!params.getState().prompt.trim()) {
        set({ status: status.info('status.promptRequired') });
        return;
      }

      set({ status: status.busy('status.generating') });
      try {
        const job = await jobs
          .getState()
          .submit(buildGenerateParams(params.getState(), app.getState(), source.assetId));
        set({ activeJobId: job.id });
        // The job may already have finished while the call was answered.
        settle(jobs.getState().jobs[job.id] ?? job);
      } catch (error) {
        set({ status: status.error(error) });
      }
    },

    async cancel() {
      const { activeJobId } = get();
      if (activeJobId) {
        await jobs
          .getState()
          .cancel(activeJobId)
          .catch(() => undefined);
      }
    },

    async save(format) {
      const { result } = get();
      if (!result) {
        return;
      }
      set({ saving: true });
      try {
        let assetId = result.assetId;
        if (!matchesFormat(result.mimeType, format)) {
          // The host writes bytes as they are, so conversion happens here first.
          set({ status: status.busy('status.converting') });
          assetId = (await bridge.upload(await encodeAs(bridge.assetUrl(result.assetId), format))).assetId;
        }
        const saved = await bridge.call('files.saveImage', { assetId, suggestedName: defaultResultName() });
        set({ status: saved ? status.success('status.saved', { path: saved.path }) : null });
      } catch (error) {
        set({ status: status.error(error) });
      } finally {
        set({ saving: false });
      }
    },

    useResultAsSource() {
      const { result } = get();
      if (result) {
        set({ source: result, result: null, status: null });
      }
    },

    clear() {
      if (get().activeJobId) {
        return;
      }
      params.getState().setPrompt('');
      set({ source: null, result: null, status: null });
    },

    setStatus: (message) => set({ status: message }),
  }));

  function settle(job: JobInfo): void {
    if (job.id !== store.getState().activeJobId || !isFinished(job)) {
      return;
    }
    if (job.state === 'succeeded' && job.result?.assets[0]) {
      const result = job.result.assets[0];
      store.setState({
        activeJobId: null,
        result,
        status: status.success('status.generated', { width: result.width, height: result.height }),
      });
    } else if (job.state === 'cancelled') {
      store.setState({ activeJobId: null, status: status.info('status.cancelled') });
    } else {
      const error = job.error
        ? BridgeError.fromRpc(job.error)
        : new BridgeError('no_image', 'No image returned.');
      store.setState({ activeJobId: null, status: status.error(error) });
    }
  }

  jobs.subscribe((state) => {
    const id = store.getState().activeJobId;
    if (id && state.jobs[id]) {
      settle(state.jobs[id]);
    }
  });

  return store;
}
