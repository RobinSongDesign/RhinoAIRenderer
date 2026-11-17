import { describe, expect, it } from 'vitest';
import { Bridge } from '../../bridge/Bridge';
import type { GenerateParams } from '../../bridge/types';
import { createAppStore } from '../../state/appStore';
import { createJobsStore } from '../../state/jobsStore';
import { FakeTransport } from '../../test/fakeTransport';
import { appState, asset, job } from '../../test/fixtures';
import { createParamsStore } from '../params/paramsStore';
import { createBatchStore, isRunning, itemStatusKey, runProgress } from './batchStore';

async function setup(views = ['Exterior', 'Interior', 'Aerial']) {
  let submitted = 0;
  const transport = new FakeTransport()
    .handle('app.getState', () => appState())
    .handle('rhino.listNamedViews', () => views.map((name) => ({ name })))
    .handle('rhino.captureNamedView', (p) => {
      const name = (p as { name: string }).name;
      if (name === 'Broken') {
        throw new Error('capture failed');
      }
      return asset(`src-${name}`);
    })
    .handle('jobs.submitBatch', (p) => {
      const items = (p as { items: GenerateParams[] }).items;
      return { groupId: 'g1', jobs: items.map((_, i) => job(`j${i + 1}`, 'queued')) };
    })
    .handle('jobs.submit', () => job(`regen${++submitted}`, 'queued'))
    .handle('jobs.cancel', () => null)
    .handle('files.saveMany', (p) => ({ folder: '/out', saved: (p as { items: unknown[] }).items.length }));
  const bridge = new Bridge(transport);
  const app = createAppStore(bridge, 'en');
  await app.getState().load();
  const jobs = createJobsStore(bridge);
  const params = createParamsStore(bridge);
  params.getState().setPrompt('shared prompt');
  const batch = createBatchStore({ bridge, app, jobs, params });
  return { transport, jobs, params, batch };
}

describe('batchStore', () => {
  it('loads and captures every named view', async () => {
    const { batch } = await setup(['Exterior', 'Broken']);

    await batch.getState().loadViews();

    const [exterior, broken] = batch.getState().items;
    expect(exterior).toMatchObject({
      viewName: 'Exterior',
      selected: true,
      capturing: false,
      source: { assetId: 'src-Exterior' },
    });
    expect(broken).toMatchObject({ captureFailed: true, source: null });
    expect(batch.getState().status).toMatchObject({ key: 'batch.loaded', params: { captured: 1, total: 2 } });
  });

  it('says so when the document has no named views', async () => {
    const { batch } = await setup([]);

    await batch.getState().loadViews();

    expect(batch.getState().items).toEqual([]);
    expect(batch.getState().status?.key).toBe('batch.noNamedViews');
  });

  it('submits the selected views as one batch with per-view prompts and the configured mode', async () => {
    const { batch, transport } = await setup();
    await batch.getState().loadViews();
    batch.getState().setSelected('Interior', false);
    batch.getState().setAddonPrompt('Aerial', 'from above');

    await batch.getState().render();

    const call = transport.callsOf('jobs.submitBatch')[0] as { items: GenerateParams[]; mode: string };
    expect(call.mode).toBe('serial');
    expect(call.items.map((i) => i.label)).toEqual(['Exterior', 'Aerial']);
    expect(call.items[0]).toMatchObject({ prompt: 'shared prompt', sourceAssetId: 'src-Exterior' });
    expect(call.items[0].addonPrompt).toBeUndefined();
    expect(call.items[1].addonPrompt).toBe('from above');
    expect(batch.getState().items.map((i) => i.jobId)).toEqual(['j1', null, 'j2']);
    expect(batch.getState().status).toMatchObject({
      kind: 'busy',
      key: 'batch.rendering',
      params: { count: 2 },
    });
  });

  it('refuses to render with nothing selected or without a prompt', async () => {
    const { batch, params, transport } = await setup();
    await batch.getState().loadViews();

    batch.getState().selectAll(false);
    await batch.getState().render();
    expect(batch.getState().status?.key).toBe('batch.nothingSelected');

    batch.getState().selectAll(true);
    params.getState().setPrompt(' ');
    await batch.getState().render();
    expect(batch.getState().status?.key).toBe('status.promptRequired');
    expect(transport.callsOf('jobs.submitBatch')).toHaveLength(0);
  });

  it('fills in results and errors as jobs finish, then reports the outcome', async () => {
    const { batch, transport, jobs } = await setup();
    await batch.getState().loadViews();
    await batch.getState().render();

    transport.emit('job.updated', job('j1', 'succeeded', { result: { assets: [asset('r1')] } }));
    transport.emit(
      'job.updated',
      job('j2', 'failed', { error: { code: 'provider_error', message: 'x', detail: 'boom' } }),
    );
    expect(batch.getState().status?.kind).toBe('busy');
    expect(isRunning(batch.getState().items, jobs.getState().jobs)).toBe(true);
    transport.emit('job.updated', job('j3', 'succeeded', { result: { assets: [asset('r3')] } }));

    const [first, second, third] = batch.getState().items;
    expect(first.result?.assetId).toBe('r1');
    expect(second.error?.detail).toBe('boom');
    expect(third.result?.assetId).toBe('r3');
    expect(isRunning(batch.getState().items, jobs.getState().jobs)).toBe(false);
    expect(runProgress(batch.getState().runJobIds, jobs.getState().jobs)).toMatchObject({
      done: 3,
      succeeded: 2,
      failed: 1,
    });
    expect(batch.getState().status).toMatchObject({
      key: 'batch.allDone',
      params: { succeeded: 2, failed: 1 },
    });
  });

  it('cancels the whole run and reports how far it got', async () => {
    const { batch, transport } = await setup();
    await batch.getState().loadViews();
    await batch.getState().render();
    transport.emit('job.updated', job('j1', 'succeeded', { result: { assets: [asset('r1')] } }));

    await batch.getState().cancel();
    transport.emit('job.updated', job('j2', 'cancelled'));
    transport.emit('job.updated', job('j3', 'cancelled'));

    expect(transport.callsOf('jobs.cancel')).toContainEqual({ groupId: 'g1' });
    expect(batch.getState().status).toMatchObject({ key: 'batch.cancelled', params: { done: 1, total: 3 } });
  });

  it('regenerates one view with the other finished views as references', async () => {
    const { batch, transport } = await setup();
    await batch.getState().loadViews();
    await batch.getState().render();
    transport.emit('job.updated', job('j1', 'succeeded', { result: { assets: [asset('r1')] } }));
    transport.emit('job.updated', job('j2', 'failed', { error: { code: 'timeout', message: 'x' } }));
    transport.emit('job.updated', job('j3', 'succeeded', { result: { assets: [asset('r3')] } }));

    await batch.getState().regenerate('Interior');

    const call = transport.callsOf('jobs.submit')[0] as GenerateParams;
    expect(call.dependsOn).toEqual(['j1', 'j3']);
    expect(call.label).toBe('Interior');
    expect(batch.getState().items[1]).toMatchObject({ jobId: 'regen1', error: null });
    transport.emit('job.updated', job('regen1', 'succeeded', { result: { assets: [asset('r2')] } }));
    expect(batch.getState().items[1].result?.assetId).toBe('r2');
  });

  it('saves every result into one folder, named after the views', async () => {
    const { batch, transport } = await setup();
    await batch.getState().loadViews();
    await batch.getState().render();
    transport.emit('job.updated', job('j1', 'succeeded', { result: { assets: [asset('r1')] } }));
    transport.emit('job.updated', job('j3', 'succeeded', { result: { assets: [asset('r3')] } }));

    await batch.getState().saveAll();

    expect(transport.callsOf('files.saveMany')[0]).toEqual({
      items: [
        { assetId: 'r1', fileName: 'Exterior' },
        { assetId: 'r3', fileName: 'Aerial' },
      ],
    });
    expect(batch.getState().status).toMatchObject({
      key: 'status.savedMany',
      params: { count: 2, folder: '/out' },
    });
  });

  it('derives each row status', () => {
    const base = {
      viewName: 'v',
      selected: true,
      source: null,
      capturing: false,
      captureFailed: false,
      addonPrompt: '',
      jobId: null,
      result: null,
      error: null,
    };
    expect(itemStatusKey(base, undefined)).toBe('item.waiting');
    expect(itemStatusKey({ ...base, capturing: true }, undefined)).toBe('item.capturing');
    expect(itemStatusKey({ ...base, captureFailed: true }, undefined)).toBe('item.captureFailed');
    expect(itemStatusKey({ ...base, source: asset('s') }, undefined)).toBe('item.ready');
    expect(itemStatusKey(base, job('j', 'running'))).toBe('item.generating');
    expect(itemStatusKey(base, job('j', 'failed'))).toBe('item.failed');
  });
});
