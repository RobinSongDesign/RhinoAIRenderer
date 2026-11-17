import { useState, type DragEvent } from 'react';
import type { AssetRef } from '../bridge/types';
import { useBridge, useT } from '../state/services';
import { Button } from './Button';
import styles from './components.module.css';

export interface DropZoneProps {
  asset: AssetRef | null;
  /** Called with a dropped image file. */
  onFile(file: File): void;
  /** Called when the zone is clicked while empty (opens the host's file dialog). */
  onPick(): void;
  onClear(): void;
  busy?: boolean;
  testId?: string;
}

/** Drop target for one image, with click-to-choose and a clear button. */
export function DropZone({ asset, onFile, onPick, onClear, busy, testId }: DropZoneProps) {
  const t = useT();
  const bridge = useBridge();
  const [over, setOver] = useState(false);

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setOver(false);
    const file = event.dataTransfer.files?.[0];
    if (file) {
      onFile(file);
    }
  };

  return (
    <div
      className={styles.dropZone}
      data-over={over}
      data-testid={testId}
      role="button"
      tabIndex={0}
      onClick={() => !asset && !busy && onPick()}
      onKeyDown={(event) => event.key === 'Enter' && !asset && !busy && onPick()}
      onDragEnter={(event) => {
        event.preventDefault();
        setOver(true);
      }}
      onDragOver={(event) => event.preventDefault()}
      onDragLeave={() => setOver(false)}
      onDrop={onDrop}
    >
      {busy ? (
        <span>{t('reference.uploading')}</span>
      ) : asset ? (
        <>
          <img src={bridge.assetUrl(asset.assetId)} alt="" data-asset-id={asset.assetId} />
          <Button
            className={styles.dropZoneClear}
            size="small"
            aria-label={t('action.removeReference')}
            onClick={(event) => {
              event.stopPropagation();
              onClear();
            }}
          >
            ✕
          </Button>
        </>
      ) : (
        <>
          <span>{t('reference.hint')}</span>
          <span className={styles.dropZoneSub}>{t('reference.sub')}</span>
        </>
      )}
    </div>
  );
}
