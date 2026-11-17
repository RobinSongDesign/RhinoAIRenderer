import { describe, expect, it } from 'vitest';
import { Bridge } from '../../bridge/Bridge';
import { createAppStore } from '../../state/appStore';
import { FakeTransport } from '../../test/fakeTransport';
import { appState, asset } from '../../test/fixtures';
import {
  buildGenerateParams,
  createParamsStore,
  CUSTOM_STYLE_ID,
  pickAspectRatio,
  pickImageSize,
} from './paramsStore';

const dusk = { id: 'dusk', name: { zh: '黄昏', en: 'Dusk' }, prompt: 'golden hour' };
const custom = { id: CUSTOM_STYLE_ID, name: { zh: '自定义', en: 'Custom' }, prompt: '' };

describe('paramsStore', () => {
  it('replaces the prompt with a template, but leaves it alone for Custom', () => {
    // Custom used to clear the prompt in the WPF version (legacy defect L10).
    const store = createParamsStore(new Bridge(new FakeTransport()));
    store.getState().setPrompt('my own words');

    store.getState().applyStyle(dusk);
    expect(store.getState().prompt).toBe('golden hour');

    store.getState().setPrompt('edited');
    store.getState().applyStyle(custom);
    expect(store.getState().prompt).toBe('edited');
    expect(store.getState().styleId).toBe(CUSTOM_STYLE_ID);
  });

  it('uploads a dropped reference and picks one through the host dialog', async () => {
    const transport = new FakeTransport().handle('files.pickImages', () => [asset('picked')]);
    const store = createParamsStore(new Bridge(transport));

    expect(await store.getState().uploadReference(new Blob(['x'], { type: 'image/png' }))).toBeNull();
    expect(store.getState().reference?.assetId).toBe('uploaded-1');

    expect(await store.getState().pickReference()).toBeNull();
    expect(store.getState().reference?.assetId).toBe('picked');
    expect(store.getState().referenceBusy).toBe(false);
  });

  it('reports a failed pick as an error status', async () => {
    const store = createParamsStore(new Bridge(new FakeTransport()));

    const message = await store.getState().pickReference();

    expect(message?.kind).toBe('error');
    expect(store.getState().reference).toBeNull();
  });
});

describe('effective parameters', () => {
  it('fall back when the model does not offer the choice', () => {
    expect(pickImageSize('2K', ['1K', '2K'], '1K')).toBe('2K');
    expect(pickImageSize('4K', ['1K', '2K'], '1K')).toBe('1K');
    expect(pickImageSize('', ['2K', '4K'], '1K')).toBe('2K');
    expect(pickImageSize('8K', [], '1K')).toBe('8K');
    expect(pickAspectRatio('16:9', ['1:1', '16:9'])).toBe('16:9');
    expect(pickAspectRatio('21:9', ['1:1'])).toBe('');
  });

  it('build a request for the active backend and model', async () => {
    const app = createAppStore(
      new Bridge(new FakeTransport().handle('app.getState', () => appState())),
      'en',
    );
    await app.getState().load();
    const params = createParamsStore(new Bridge(new FakeTransport()));
    params.getState().setPrompt('render');
    params.getState().setAspectRatio('16:9');
    params.getState().setImageSize('4K');
    params.getState().setReference(asset('ref'));

    const request = buildGenerateParams(params.getState(), app.getState(), 'src', { label: 'View' });

    expect(request).toEqual({
      backendId: 'direct:BltAI',
      modelId: 'model-a',
      prompt: 'render',
      sourceAssetId: 'src',
      imageSize: '1K',
      aspectRatio: '16:9',
      references: [{ assetId: 'ref', role: 'style' }],
      label: 'View',
    });
  });
});
