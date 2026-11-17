import { describe, expect, it } from 'vitest';
import { Bridge } from '../bridge/Bridge';
import { BridgeError } from '../bridge/BridgeError';
import { FakeTransport, flush } from '../test/fakeTransport';
import { appState, job } from '../test/fixtures';
import { createAppStore, selectActiveModel, selectAspectRatios, selectImageSizes } from './appStore';
import { createJobsStore, isFinished, newerJob } from './jobsStore';

describe('appStore', () => {
  it('loads the host state', async () => {
    const transport = new FakeTransport().handle('app.getState', () => appState({ language: 'zh' }));
    const store = createAppStore(new Bridge(transport), 'en');

    await store.getState().load();

    const state = store.getState();
    expect(state.loadState).toBe('ready');
    expect(state.language).toBe('zh');
    expect(state.host?.platform).toBe('dev');
    expect(selectActiveModel(state)?.id).toBe('model-a');
    expect(selectImageSizes(state)).toEqual(['1K', '2K']);
    expect(selectAspectRatios(state)).toEqual(['1:1', '16:9']);
  });

  it('falls back to catalog lists when the model does not restrict them', async () => {
    const transport = new FakeTransport().handle('app.getState', () =>
      appState({ activeModelId: 'model-b' }),
    );
    const store = createAppStore(new Bridge(transport), 'en');
    await store.getState().load();

    expect(selectImageSizes(store.getState())).toEqual(['0.5K', '1K', '2K', '4K']);
    expect(selectAspectRatios(store.getState())).toEqual(['1:1', '4:3', '16:9']);
  });

  it('reports a failed first load', async () => {
    const store = createAppStore(new Bridge(new FakeTransport()), 'en');
    await store.getState().load();

    expect(store.getState().loadState).toBe('failed');
    expect(store.getState().loadError).toContain('app.getState');
  });

  it('follows backend and settings events', async () => {
    const transport = new FakeTransport().handle('app.getState', () => appState());
    const store = createAppStore(new Bridge(transport), 'en');
    await store.getState().load();

    transport.emit('backends.changed', { backends: [], activeBackendId: 'cloud', activeModelId: 'm' });
    transport.emit('settings.changed', {
      language: 'zh',
      systemPrompt: 'x',
      batch: { mode: 'independent', maxConsistencyRefs: 1 },
    });

    expect(store.getState().activeBackendId).toBe('cloud');
    expect(store.getState().language).toBe('zh');
    expect(store.getState().settings?.batch.mode).toBe('independent');
  });

  it('selects a backend optimistically and applies the host answer', async () => {
    const transport = new FakeTransport()
      .handle('app.getState', () => appState())
      .handle('backend.select', () => ({
        backends: appState().backends,
        activeBackendId: 'direct:BltAI',
        activeModelId: 'model-b',
      }));
    const store = createAppStore(new Bridge(transport), 'en');
    await store.getState().load();

    const pending = store.getState().selectBackend('direct:BltAI', 'model-b');
    expect(store.getState().activeModelId).toBe('model-b');
    await pending;

    expect(transport.callsOf('backend.select')).toEqual([{ backendId: 'direct:BltAI', modelId: 'model-b' }]);
  });

  it('returns to the host selection when selecting a backend fails', async () => {
    const transport = new FakeTransport()
      .handle('app.getState', () => appState())
      .handle('backend.select', () => {
        throw new BridgeError('not_found', 'gone');
      });
    const store = createAppStore(new Bridge(transport), 'en');
    await store.getState().load();

    await expect(store.getState().selectBackend('direct:Gemini')).rejects.toThrow();
    await flush();

    expect(store.getState().activeBackendId).toBe('direct:BltAI');
  });

  it('fetches the catalog again when switching into or out of account mode', async () => {
    let active = 'direct:BltAI';
    const transport = new FakeTransport()
      .handle('app.getState', () =>
        appState({
          activeBackendId: active,
          catalog: { ...appState().catalog, defaultSystemPrompt: active === 'cloud' ? 'gateway' : 'local' },
        }),
      )
      .handle('backend.select', (p) => {
        active = (p as { backendId: string }).backendId;
        return { backends: appState().backends, activeBackendId: active, activeModelId: 'm' };
      });
    const store = createAppStore(new Bridge(transport), 'en');
    await store.getState().load();

    await store.getState().selectBackend('direct:Gemini');
    await flush();
    expect(transport.callsOf('app.getState')).toHaveLength(1);

    await store.getState().selectBackend('cloud');
    await flush();
    expect(store.getState().catalog?.defaultSystemPrompt).toBe('gateway');

    await store.getState().selectBackend('direct:BltAI');
    await flush();
    expect(store.getState().catalog?.defaultSystemPrompt).toBe('local');
  });

  it('keeps the newest catalog when refreshes overlap', async () => {
    const answers: ((value: unknown) => void)[] = [];
    const transport = new FakeTransport()
      .handle('app.getState', () => appState())
      .handle('backend.select', () => ({ backends: appState().backends, activeBackendId: 'cloud', activeModelId: 'm' }));
    const store = createAppStore(new Bridge(transport), 'en');
    await store.getState().load();
    transport.handle('app.getState', () => new Promise((resolve) => answers.push(resolve)));
    const catalog = (prompt: string) => appState({ catalog: { ...appState().catalog, defaultSystemPrompt: prompt } });

    // Signing in while the account backend is already selected refreshes once...
    store.setState({ activeBackendId: 'cloud' });
    transport.emit('account.changed', { available: true, signedIn: true, credits: 1 });
    // ...and the host's answer to the switch refreshes again.
    await store.getState().selectBackend('cloud');
    await flush();
    expect(answers).toHaveLength(2);

    answers[1](catalog('newest'));
    await flush();
    answers[0](catalog('stale'));
    await flush();

    expect(store.getState().catalog?.defaultSystemPrompt).toBe('newest');
  });

  it('reloads everything after the connection comes back', async () => {
    let loads = 0;
    const transport = new FakeTransport().handle('app.getState', () => {
      loads++;
      return appState();
    });
    const store = createAppStore(new Bridge(transport), 'en');
    await store.getState().load();

    transport.setConnection('connected');
    transport.setConnection('disconnected');
    transport.setConnection('connected');
    await flush();

    expect(store.getState().connection).toBe('connected');
    expect(loads).toBe(2);
  });
});

describe('jobsStore', () => {
  it('keeps the newest state when answers and events arrive out of order', () => {
    expect(newerJob(job('a', 'running'), job('a', 'queued')).state).toBe('running');
    expect(newerJob(job('a', 'queued'), job('a', 'succeeded')).state).toBe('succeeded');
    expect(newerJob(undefined, job('a', 'queued')).state).toBe('queued');
    expect(isFinished(job('a', 'cancelled'))).toBe(true);
    expect(isFinished(job('a', 'running'))).toBe(false);
    expect(isFinished(undefined)).toBe(false);
  });

  it('records submitted jobs and follows their events', async () => {
    const transport = new FakeTransport().handle('jobs.submit', () => job('j1', 'queued'));
    const store = createJobsStore(new Bridge(transport));

    transport.emit('job.updated', job('j1', 'running'));
    const submitted = await store
      .getState()
      .submit({ backendId: 'b', modelId: 'm', prompt: 'p', imageSize: '1K' });
    transport.emit('job.updated', job('j1', 'succeeded'));

    expect(submitted.state).toBe('running');
    expect(store.getState().jobs.j1.state).toBe('succeeded');
  });

  it('cancels jobs and groups', async () => {
    const transport = new FakeTransport().handle('jobs.cancel', () => null);
    const store = createJobsStore(new Bridge(transport));

    await store.getState().cancel('j1');
    await store.getState().cancelGroup('g1');

    expect(transport.callsOf('jobs.cancel')).toEqual([{ jobId: 'j1' }, { groupId: 'g1' }]);
  });

  it('resynchronizes from jobs.list on reconnect', async () => {
    const transport = new FakeTransport().handle('jobs.list', () => [job('j9', 'failed')]);
    const store = createJobsStore(new Bridge(transport));

    transport.setConnection('connected');
    await flush();

    expect(store.getState().jobs.j9.state).toBe('failed');
  });
});
