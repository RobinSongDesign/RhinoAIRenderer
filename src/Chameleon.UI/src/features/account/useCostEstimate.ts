import { useEffect, useState } from 'react';
import { selectImageSizes } from '../../state/appStore';
import { useAppStore, useBridge, useParamsStore } from '../../state/services';
import { pickImageSize } from '../params/paramsStore';
import { CLOUD_BACKEND_ID } from '../../bridge/types';

/** Cost of the next generation(s) in account mode. */
export interface CostEstimate {
  credits: number;
  /** The balance does not cover the cost: generating would fail with insufficient_credits. */
  short: boolean;
}

/**
 * Asks the host what `count` generations with the current model and size will cost.
 * Returns null outside account mode, while the estimate is loading, or when it is unknown.
 */
export function useCostEstimate(count: number): CostEstimate | null {
  const bridge = useBridge();
  const isCloud = useAppStore((s) => s.activeBackendId === CLOUD_BACKEND_ID && s.account.signedIn);
  const modelId = useAppStore((s) => s.activeModelId);
  const sizes = useAppStore(selectImageSizes);
  const fallback = useAppStore((s) => s.catalog?.defaultImageSize ?? '1K');
  const chosen = useParamsStore((s) => s.imageSize);
  const balance = useAppStore((s) => s.account.credits ?? 0);
  const imageSize = pickImageSize(chosen, sizes, fallback);
  const enabled = isCloud && Boolean(modelId) && count > 0;
  const key = `${modelId}|${imageSize}|${count}`;
  const [result, setResult] = useState<{ key: string; credits: number | null } | null>(null);

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }
    // Ignore answers that arrive after the inputs changed again.
    let current = true;
    bridge
      .call('account.estimate', { modelId, imageSize, count })
      .then((estimate) => current && setResult({ key, credits: estimate.credits }))
      .catch(() => current && setResult({ key, credits: null }));
    return () => {
      current = false;
    };
  }, [bridge, enabled, modelId, imageSize, count, key]);

  if (!enabled || !result || result.key !== key || result.credits === null) {
    return null;
  }
  return { credits: result.credits, short: result.credits > balance };
}
