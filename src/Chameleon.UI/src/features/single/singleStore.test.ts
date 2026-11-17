import { describe, expect, it } from 'vitest';
import { Bridge } from '../../bridge/Bridge';
import type { GenerateParams } from '../../bridge/types';
import { createAppStore } from '../../state/appStore';
import { createJobsStore } from '../../state/jobsStore';
import { FakeSession } from '../../test/fakeSession';
import { FakeTransport, flush } from '../../test/fakeTransport';
import { appState, asset, job } from '../../test/fixtures';
import { createSessionStore, selectCurrentNode } from '../history/sessionStore';
import { createParamsStore } from '../params/paramsStore';
import { createSingleStore } from './singleStore';

async function setup() {
  const transport = new FakeTransport()
    .handle('app.getState', () => appState())
    .handle('rhino.captureActiveView', () => asset('captured', 1920, 1080))
    .handle('files.pickImages', () => [asset('imported-1'), asset('imported-2')])
    .handle('jobs.submit', () => job('j1', 'queued'))
    .handle('jobs.cancel', () => null)
    .handle('files.saveImage', () => ({ path: '/out/AIRender.png' }))
    .handle('backend.select', (p) => ({
      backends: appState().backends,
      activeBackendId: (p as { backendId: string }).backendId,
      activeModelId: (p as { modelId: string }).modelId,
    }));
  const host = new FakeSession(transport);
  const bridge = new Bridge(transport);
  const app = createAppStore(bridge, 'en');
  const session = createSessionStore(bridge, app);
  await app.getState().load();
  await flush();
  const jobs = createJobsStore(bridge);
  const params = createParamsStore(bridge);
  const single = createSingleStore({ bridge, app, jobs, params, session });
  const current = () => selectCurrentNode(session.getState());
  return { transport, host, params, session, single, current };
}

/** Finishes job j1 the way the host does: the result becomes a node under the job's parent. */
function succeed(context: Awaited<ReturnType<typeof setup>>, resultId = 'result') {
  const submitted = context.transport.callsOf('jobs.submit').at(-1) as GenerateParams;
  const node = context.host.add({ kind: 'generation', assetId: resultId, parentId: submitted.parentNodeId, recipe: {
    backendId: submitted.backendId, modelId: submitted.modelId, prompt: submitted.prompt, imageSize: submitted.imageSize,
    aspectRatio: submitted.aspectRatio, references: [],
  } });
  context.transport.emit('job.updated', job('j1', 'succeeded', { result: { assets: [asset(resultId, 2048, 1152)], nodeIds: [node.id] } }));
  return node;
}

describe('singleStore', () => {
  it('captures the active view as a selected history root', async () => {
    const { single, current } = await setup();

    await single.getState().capture();

    expect(current()).toMatchObject({ kind: 'capture', asset: { assetId: 'captured' } });
    expect(single.getState().status).toMatchObject({ kind: 'success', key: 'status.captured', params: { width: 1920, height: 1080 } });
    expect(single.getState().capturing).toBe(false);
  });

  it('imports images as roots and selects the last one', async () => {
    const { single, session, current } = await setup();

    await single.getState().importImages();

    expect(session.getState().session?.nodes.map((n) => n.kind)).toEqual(['import', 'import']);
    expect(current()?.asset.assetId).toBe('imported-2');
    expect(single.getState().status?.key).toBe('status.imported');
  });

  it('refuses to generate without a selected image or a prompt', async () => {
    const { single, params, transport } = await setup();

    await single.getState().generate();
    expect(single.getState().status?.key).toBe('status.sourceRequired');

    await single.getState().capture();
    params.getState().setPrompt('   ');
    await single.getState().generate();
    expect(single.getState().status?.key).toBe('status.promptRequired');
    expect(transport.callsOf('jobs.submit')).toHaveLength(0);
  });

  it('generates from the selected node and selects the result', async () => {
    const context = await setup();
    const { single, params, transport, current } = context;
    await single.getState().capture();
    const capture = current()!;
    params.getState().setPrompt('render');

    await single.getState().generate();
    expect(single.getState().activeJobId).toBe('j1');
    const result = succeed(context);
    await flush();

    const submitted = transport.callsOf('jobs.submit')[0] as GenerateParams;
    expect(submitted.sourceAssetId).toBe('captured');
    expect(submitted.parentNodeId).toBe(capture.id);
    expect(single.getState().activeJobId).toBeNull();
    expect(current()?.id).toBe(result.id);
    expect(single.getState().status).toMatchObject({ key: 'status.generated', params: { width: 2048, height: 1152 } });
  });

  it('runs a recipe again on the same parent', async () => {
    const context = await setup();
    const { single, params, transport } = context;
    await single.getState().capture();
    params.getState().setPrompt('dusk');
    await single.getState().generate();
    const first = succeed(context);
    await flush();

    await single.getState().rerun(first.id);

    const again = transport.callsOf('jobs.submit')[1] as GenerateParams;
    expect(again).toMatchObject({ prompt: 'dusk', sourceAssetId: 'captured', parentNodeId: first.parentId });
  });

  it('loads a recipe into the parameters', async () => {
    const { single, params, host, session } = await setup();
    const root = host.add({ kind: 'capture', assetId: 'c' });
    const node = host.add({ kind: 'generation', assetId: 'g', parentId: root.id, recipe: {
      backendId: 'direct:BltAI', modelId: 'model-b', prompt: 'snow', imageSize: '2K', aspectRatio: '16:9', references: [],
    } });
    await session.getState().select(node.id);

    await single.getState().applyRecipe(node.id);

    expect(params.getState()).toMatchObject({ prompt: 'snow', imageSize: '2K', aspectRatio: '16:9' });
    expect(single.getState().status?.key).toBe('status.recipeApplied');
  });

  it('shows the localized error of a failed job, with the provider detail', async () => {
    const { single, params, transport } = await setup();
    await single.getState().capture();
    params.getState().setPrompt('render');
    await single.getState().generate();

    transport.emit('job.updated', job('j1', 'failed', { error: { code: 'rate_limited', message: 'x', detail: 'slow down' } }));
    await flush();

    expect(single.getState().status).toMatchObject({ kind: 'error', key: 'error.rate_limited', error: { detail: 'slow down' } });
  });

  it('cancels the running job', async () => {
    const { single, params, transport } = await setup();
    await single.getState().capture();
    params.getState().setPrompt('render');
    await single.getState().generate();

    await single.getState().cancel();
    transport.emit('job.updated', job('j1', 'cancelled'));
    await flush();

    expect(transport.callsOf('jobs.cancel')).toEqual([{ jobId: 'j1' }]);
    expect(single.getState().status?.key).toBe('status.cancelled');
    expect(single.getState().activeJobId).toBeNull();
  });

  it('reports a submit error such as a missing key', async () => {
    const { single, params, transport } = await setup();
    transport.handle('jobs.submit', () => {
      throw new Error('no key');
    });
    await single.getState().capture();
    params.getState().setPrompt('render');

    await single.getState().generate();

    expect(single.getState().status?.kind).toBe('error');
    expect(single.getState().activeJobId).toBeNull();
  });

  it('saves the selected image as PNG without converting it, using the legacy file name', async () => {
    const context = await setup();
    const { single, params, transport } = context;
    await single.getState().capture();
    params.getState().setPrompt('render');
    await single.getState().generate();
    succeed(context);
    await flush();

    await single.getState().save('png');

    const call = transport.callsOf('files.saveImage')[0] as { assetId: string; suggestedName: string };
    expect(call.assetId).toBe('result');
    expect(call.suggestedName).toMatch(/^AIRender_\d{8}_\d{6}$/);
    expect(single.getState().status).toMatchObject({ key: 'status.saved', params: { path: '/out/AIRender.png' } });
  });
});
