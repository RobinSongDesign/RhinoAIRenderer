import type { AssetRef } from '../bridge/types';
import { useBridge, useServices } from '../state/services';
import styles from './components.module.css';

export interface ImageViewProps {
  asset: AssetRef | null;
  placeholder: string;
  /** Text shown over the image (or placeholder) while work is in progress. */
  busyText?: string | null;
  alt: string;
  testId?: string;
}

/** Shows a host asset scaled to fit; clicking opens the full-screen preview. */
export function ImageView({ asset, placeholder, busyText, alt, testId }: ImageViewProps) {
  const bridge = useBridge();
  const { ui } = useServices();
  return (
    <div className={styles.imageView} data-testid={testId}>
      {asset ? (
        <img
          src={bridge.assetUrl(asset.assetId)}
          alt={alt}
          data-asset-id={asset.assetId}
          draggable={false}
          onClick={() => ui.getState().showPreview(asset)}
        />
      ) : (
        <div className={styles.imagePlaceholder}>{placeholder}</div>
      )}
      {busyText && (
        <div className={styles.imageBusy}>
          <span className={styles.spinner} aria-hidden="true" />
          {busyText}
        </div>
      )}
    </div>
  );
}
