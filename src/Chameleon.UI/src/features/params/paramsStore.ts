import { createStore, type StoreApi } from 'zustand/vanilla';
import type { Bridge } from '../../bridge/Bridge';
import type { AssetRef, GenerateParams, StyleTemplate } from '../../bridge/types';
import { normalizeForUpload } from '../../imaging/imaging';
import type { AppStoreState } from '../../state/appStore';
import { selectAspectRatios, selectImageSizes } from '../../state/appStore';
import { status, type StatusMessage } from '../../state/status';

/** Id of the style template that leaves the prompt alone. */
export const CUSTOM_STYLE_ID = 'custom';

/** Generation parameters shared by single and batch mode (as in the WPF version). */
export interface ParamsStoreState {
  prompt: string;
  styleId: string;
  /** '' keeps the source's aspect ratio. */
  aspectRatio: string;
  /** '' means "the catalog default". */
  imageSize: string;
  reference: AssetRef | null;
  referenceBusy: boolean;

  setPrompt(prompt: string): void;
  /** Picks a style template; any template but "custom" replaces the prompt with its text. */
  applyStyle(template: StyleTemplate): void;
  setAspectRatio(ratio: string): void;
  setImageSize(size: string): void;
  setReference(asset: AssetRef | null): void;
  /** Uploads a dropped file as the reference. Returns an error status when that fails. */
  uploadReference(file: Blob): Promise<StatusMessage | null>;
  /** Lets the user choose a reference with the host's file dialog. */
  pickReference(): Promise<StatusMessage | null>;
}

export function createParamsStore(bridge: Bridge): StoreApi<ParamsStoreState> {
  return createStore<ParamsStoreState>()((set) => ({
    prompt: '',
    styleId: CUSTOM_STYLE_ID,
    aspectRatio: '',
    imageSize: '',
    reference: null,
    referenceBusy: false,

    setPrompt: (prompt) => set({ prompt }),

    applyStyle: (template) =>
      set(
        template.id === CUSTOM_STYLE_ID
          ? { styleId: template.id }
          : { styleId: template.id, prompt: template.prompt },
      ),

    setAspectRatio: (aspectRatio) => set({ aspectRatio }),
    setImageSize: (imageSize) => set({ imageSize }),
    setReference: (reference) => set({ reference }),

    async uploadReference(file) {
      set({ referenceBusy: true });
      try {
        const asset = await bridge.upload(await normalizeForUpload(file));
        set({ reference: asset });
        return null;
      } catch (error) {
        return status.error(error);
      } finally {
        set({ referenceBusy: false });
      }
    },

    async pickReference() {
      set({ referenceBusy: true });
      try {
        const [asset] = await bridge.call('files.pickImages', { multiple: false });
        if (asset) {
          set({ reference: asset });
        }
        return null;
      } catch (error) {
        return status.error(error);
      } finally {
        set({ referenceBusy: false });
      }
    },
  }));
}

/** The size to use: the chosen one when offered, else the fallback when offered, else the first offered. */
export function pickImageSize(chosen: string, offered: string[], fallback: string): string {
  if (chosen && (offered.length === 0 || offered.includes(chosen))) {
    return chosen;
  }
  return offered.length === 0 || offered.includes(fallback) ? fallback : offered[0];
}

/** The aspect ratio to use: the chosen one when offered, else '' (keep the source ratio). */
export function pickAspectRatio(chosen: string, offered: string[]): string {
  return chosen && offered.includes(chosen) ? chosen : '';
}

/** Size actually sent for the active model. */
export function effectiveImageSize(params: ParamsStoreState, app: AppStoreState): string {
  return pickImageSize(params.imageSize, selectImageSizes(app), app.catalog?.defaultImageSize ?? '1K');
}

/** Aspect ratio actually sent for the active model. */
export function effectiveAspectRatio(params: ParamsStoreState, app: AppStoreState): string {
  return pickAspectRatio(params.aspectRatio, selectAspectRatios(app));
}

/** Builds the request for one source image from the shared parameters. */
export function buildGenerateParams(
  params: ParamsStoreState,
  app: AppStoreState,
  sourceAssetId: string,
  extra: Partial<GenerateParams> = {},
): GenerateParams {
  const aspectRatio = effectiveAspectRatio(params, app);
  return {
    backendId: app.activeBackendId,
    modelId: app.activeModelId,
    prompt: params.prompt,
    sourceAssetId,
    imageSize: effectiveImageSize(params, app),
    ...(aspectRatio ? { aspectRatio } : {}),
    references: params.reference ? [{ assetId: params.reference.assetId, role: 'style' }] : [],
    ...extra,
  };
}
