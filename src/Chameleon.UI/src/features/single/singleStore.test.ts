import { describe, expect, it } from 'vitest';
import { Bridge } from '../../bridge/Bridge';
import type { GenerateParams } from '../../bridge/types';
import { createAppStore } from '../../state/appStore';
import { createJobsStore } from '../../state/jobsStore';
import { FakeTransport, flush } from '../../test/fakeTransport';
import { appState, asset, job } from '../../test/fixtures';
import { createParamsStore } from '../params/paramsStore';
import { createSingleStore } from './singleStore';

async function setup() {
  const transport = new FakeTransport()
    .handle('app.getState', () => appState())
    .handle('rhino.captureActiveView', () => asset('captured', 1920, 1080))
    .handle('jobs.submit', () => job('j1', 'queued'))
    .handle('jobs.cancel', () => null)
    .handle('files.saveImage', () => ({ path: '/out/AIRender.png' }));
  const bridge = new Bridge(transport);
  const app = createAppStore(bridge, 'en');
  await app.getState().load();
  const jobs = createJobsStore(bridge);
  const params = createParamsStore(bridge);
  const single = createSingleStore({ bridge, app, jobs, params });
  return { transport, params, single };
}

describe('singleStore', () => {
  it('captures the active view and reports its size', async () => {
    const { single } = await setup();

    await single.getState().capture();

    expect(single.getState().source?.assetId).toBe('captured');
    expect(single.getState().status).toMatchObject({
      kind: 'success',
      key: 'status.captured',
      params: { width: 1920, height: 1080 },
    });
    expect(single.getState().capturing).toBe(false);
  });

  it('refuses to generate without a source or a prompt', async () => {
    const { single, params, transport } = await setup();

    await single.getState().generate();
    expect(single.getState().status?.key).toBe('status.sourceRequired');

    await single.getState().capture();
    params.getState().setPrompt('   ');
    await single.getState().generate();
    expect(single.getState().status?.key).toBe('status.promptRequired');
    expect(transport.callsOf('jobs.submit')).toHaveLength(0);
  });

  it('submits the job and shows the result when it succeeds', async () => {
    const { single, params, transport } = await setup();
    await single.getState().capture();
    params.getState().setPrompt('render');

    await single.getState().generate();
    expect(single.getState().activeJobId).toBe('j1');
    expect(single.getState().status?.kind).toBe('busy');
    transport.emit(
      'job.updated',
      job('j1', 'succeeded', { result: { assets: [asset('result', 2048, 1152)] } }),
    );

    expect(single.getState().activeJobId).toBeNull();
    expect(single.getState().result?.assetId).toBe('result');
    expect(single.getState().status).toMatchObject({
      key: 'status.generated',
      params: { width: 2048, height: 1152 },
    });
    expect((transport.callsOf('jobs.submit')[0] as GenerateParams).sourceAssetId).toBe('captured');
  });

  it('shows the localized error of a failed job, with the provider detail', async () => {
    const { single, params, transport } = await setup();
    await single.getState().capture();
    params.getState().setPrompt('render');
    await single.getState().generate();

    transport.emit(
      'job.updated',
      job('j1', 'failed', { error: { code: 'rate_limited', message: 'x', detail: 'slow down' } }),
    );

    expect(single.getState().status).toMatchObject({
      kind: 'error',
      key: 'error.rate_limited',
      error: { detail: 'slow down' },
    });
    expect(single.getState().result).toBeNull();
  });

  it('cancels the running job', async () => {
    const { single, params, transport } = await setup();
    await single.getState().capture();
    params.getState().setPrompt('render');
    await single.getState().generate();

    await single.getState().cancel();
    transport.emit('job.updated', job('j1', 'cancelled'));

    expect(transport.callsOf('jobs.cancel')).toEqual([{ jobId: 'j1' }]);
    expect(single.getState().status?.key).toBe('status.cancelled');
    expect(single.getState().activeJobId).toBeNull();
  });

  it('reports a submit error such as a missing key', async () => {
    const { single, params, transport } = await setup();
    transport.handle('jobs.submit', () => {
      throw Object.assign(new Error('no key'), {});
    });
    await single.getState().capture();
    params.getState().setPrompt('render');

    await single.getState().generate();

    expect(single.getState().status?.kind).toBe('error');
    expect(single.getState().activeJobId).toBeNull();
  });

  it('saves a PNG result without converting it, using the legacy file name', async () => {
    const { single, params, transport } = await setup();
    await single.getState().capture();
    params.getState().setPrompt('render');
    await single.getState().generate();
    transport.emit('job.updated', job('j1', 'succeeded', { result: { assets: [asset('result')] } }));

    await single.getState().save('png');
    await flush();

    const call = transport.callsOf('files.saveImage')[0] as { assetId: string; suggestedName: string };
    expect(call.assetId).toBe('result');
    expect(call.suggestedName).toMatch(/^AIRender_\d{8}_\d{6}$/);
    expect(single.getState().status).toMatchObject({
      key: 'status.saved',
      params: { path: '/out/AIRender.png' },
    });
  });

  it('turns the result into the new source, and clears everything', async () => {
    const { single, params, transport } = await setup();
    await single.getState().capture();
    params.getState().setPrompt('render');
    await single.getState().generate();
    transport.emit('job.updated', job('j1', 'succeeded', { result: { assets: [asset('result')] } }));

    single.getState().useResultAsSource();
    expect(single.getState().source?.assetId).toBe('result');
    expect(single.getState().result).toBeNull();

    single.getState().clear();
    expect(single.getState().source).toBeNull();
    expect(params.getState().prompt).toBe('');
  });
});
