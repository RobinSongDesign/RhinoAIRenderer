import { useEffect } from 'react';
import { useBridge, useServices, useT, useUiStore } from '../state/services';
import styles from './components.module.css';

/** Full-screen view of one image; Esc or a click closes it. */
export function PreviewOverlay() {
  const preview = useUiStore((s) => s.preview);
  const { ui } = useServices();
  const bridge = useBridge();
  const t = useT();

  useEffect(() => {
    if (!preview) {
      return undefined;
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        ui.getState().closePreview();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [preview, ui]);

  if (!preview) {
    return null;
  }
  return (
    <div
      className={styles.overlay}
      data-testid="preview-overlay"
      onClick={() => ui.getState().closePreview()}
    >
      <img src={bridge.assetUrl(preview.assetId)} alt="" />
      <span className={styles.overlayHint}>
        {preview.width}×{preview.height} · {t('preview.close')}
      </span>
    </div>
  );
}
