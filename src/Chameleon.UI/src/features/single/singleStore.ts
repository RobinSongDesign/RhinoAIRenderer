import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../../bridge/Bridge';
import { BridgeError } from '../../bridge/BridgeError';
import type { GenerateParams, JobInfo } from '../../bridge/types';
import { defaultResultName, encodeAs, matchesFormat, type OutputFormat } from '../../imaging/imaging';
import type { AppStoreState } from '../../state/appStore';
import { isFinished, type JobsStoreState } from '../../state/jobsStore';
import { status, type StatusMessage } from '../../state/status';
import { findNode, selectCurrentNode, type SessionStoreState } from '../history/sessionStore';
import { buildGenerateParams, type ParamsStoreState } from '../params/paramsStore';

export interface SingleStoreState {
  activeJobId: string | null;
  capturing: boolean;
  importing: boolean;
  saving: boolean;
  status: StatusMessage | null;

  /** Captures the active view as a new history root and selects it. */
  capture(): Promise<void>;
  /** Imports image files as new history roots and selects the last one. */
  importImages(): Promise<void>;
  /** Generates from the selected node; the result becomes its child and is selected. */
  generate(): Promise<void>;
  /** Runs a node's recipe again on its parent; the result becomes the node's sibling. */
  rerun(nodeId: string): Promise<void>;
  /** Loads a node's recipe into the parameter panel. */
  applyRecipe(nodeId: string): Promise<void>;
  cancel(): Promise<void>;
  /** Saves a node's image (the selected one by default). */
  save(format: OutputFormat, nodeId?: string): Promise<void>;
  setStatus(message: StatusMessage | null): void;
}

export interface SingleStoreDeps {
  bridge: Bridge;
  app: StoreApi<AppStoreState>;
  jobs: StoreApi<JobsStoreState>;
  params: StoreApi<ParamsStoreState>;
  session: StoreApi<SessionStoreState>;
}

/** Single-image mode: works on the selected history node, one job at a time. */
export function createSingleStore({ bridge, app, jobs, params, session }: SingleStoreDeps): StoreApi<SingleStoreState> {
  const store = createStore<SingleStoreState>()((set, get) => ({
    activeJobId: null,
    capturing: false,
    importing: false,
    saving: false,
    status: null,

    async capture() {
      set({ capturing: true, status: status.busy('status.capturing') });
      try {
        const source = await bridge.call('rhino.captureActiveView');
        await session.getState().add({ kind: 'capture', assetId: source.assetId });
        set({ status: status.success('status.captured', { width: source.width, height: source.height }) });
      } catch (error) {
        set({ status: status.error(error) });
      } finally {
        set({ capturing: false });
      }
    },

    async importImages() {
      set({ importing: true });
      try {
        const assets = await bridge.call('files.pickImages', { multiple: true });
        for (const asset of assets) {
          await session.getState().add({ kind: 'import', assetId: asset.assetId });
        }
        set({ status: assets.length > 0 ? status.success('status.imported', { count: assets.length }) : get().status });
      } catch (error) {
        set({ status: status.error(error) });
      } finally {
        set({ importing: false });
      }
    },

    async generate() {
      const current = selectCurrentNode(session.getState());
      if (!current) {
        set({ status: status.info('status.sourceRequired') });
        return;
      }
      if (!params.getState().prompt.trim()) {
        set({ status: status.info('status.promptRequired') });
        return;
      }
      await submit(
        buildGenerateParams(params.getState(), app.getState(), current.asset.assetId, { parentNodeId: current.id }),
      );
    },

    async rerun(nodeId) {
      const state = session.getState();
      const node = findNode(state, nodeId);
      const parent = findNode(state, node?.parentId);
      if (!node?.recipe || !parent) {
        set({ status: status.info('status.rerunUnavailable') });
        return;
      }
      const recipe = node.recipe;
      await submit({
        backendId: recipe.backendId,
        modelId: recipe.modelId,
        prompt: recipe.prompt,
        systemPrompt: recipe.systemPrompt ?? null,
        sourceAssetId: parent.asset.assetId,
        maskAssetId: recipe.maskAssetId,
        references: recipe.references,
        aspectRatio: recipe.aspectRatio ?? '',
        imageSize: recipe.imageSize,
        parentNodeId: parent.id,
      });
    },

    async applyRecipe(nodeId) {
      const recipe = findNode(session.getState(), nodeId)?.recipe;
      if (!recipe) {
        return;
      }
      const p = params.getState();
      p.setPrompt(recipe.prompt);
      p.setAspectRatio(recipe.aspectRatio ?? '');
      p.setImageSize(recipe.imageSize);
      if (app.getState().backends.some((b) => b.id === recipe.backendId)) {
        await app.getState().selectBackend(recipe.backendId, recipe.modelId).catch(() => undefined);
      }
      set({ status: status.info('status.recipeApplied') });
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

    async save(format, nodeId) {
      const node = nodeId ? findNode(session.getState(), nodeId) : selectCurrentNode(session.getState());
      if (!node) {
        return;
      }
      set({ saving: true });
      try {
        let assetId = node.asset.assetId;
        if (!matchesFormat(node.asset.mimeType, format)) {
          // The host writes bytes as they are, so conversion happens here first.
          set({ status: status.busy('status.converting') });
          assetId = (await bridge.upload(await encodeAs(bridge.assetUrl(assetId), format))).assetId;
        }
        const saved = await bridge.call('files.saveImage', { assetId, suggestedName: defaultResultName() });
        set({ status: saved ? status.success('status.saved', { path: saved.path }) : null });
      } catch (error) {
        set({ status: status.error(error) });
      } finally {
        set({ saving: false });
      }
    },

    setStatus: (message) => set({ status: message }),
  }));

  async function submit(request: GenerateParams): Promise<void> {
    if (store.getState().activeJobId) {
      return;
    }
    store.setState({ status: status.busy('status.generating') });
    try {
      const job = await jobs.getState().submit(request);
      store.setState({ activeJobId: job.id });
      // The job may already have finished while the call was answered.
      void settle(jobs.getState().jobs[job.id] ?? job);
    } catch (error) {
      store.setState({ status: status.error(error) });
    }
  }

  async function settle(job: JobInfo): Promise<void> {
    if (job.id !== store.getState().activeJobId || !isFinished(job)) {
      return;
    }
    if (job.state === 'succeeded' && job.result?.assets[0]) {
      const result = job.result.assets[0];
      store.setState({
        activeJobId: null,
        status: status.success('status.generated', { width: result.width, height: result.height }),
      });
      const nodeId = job.result.nodeIds?.[0];
      if (nodeId) {
        await session.getState().select(nodeId).catch(() => undefined);
      }
    } else if (job.state === 'cancelled') {
      store.setState({ activeJobId: null, status: status.info('status.cancelled') });
    } else {
      const error = job.error ? BridgeError.fromRpc(job.error) : new BridgeError('no_image', 'No image returned.');
      store.setState({ activeJobId: null, status: status.error(error) });
    }
  }

  jobs.subscribe((state) => {
    const id = store.getState().activeJobId;
    if (id && state.jobs[id]) {
      void settle(state.jobs[id]);
    }
  });

  return store;
}
