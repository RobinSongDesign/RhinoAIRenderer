import { useState } from 'react';
import type { RpcError } from '../bridge/types';
import { helpLinkFor } from '../help/helpLinks';
import { isMessageKey } from '../i18n';
import { useAppStore, useBridge, useT } from '../state/services';
import { Button } from './Button';
import styles from './components.module.css';

/**
 * A localized error line with the provider's own message behind a "details" toggle, and a link
 * to the documentation for problems the user fixes on their own computer.
 */
export function ErrorNotice({ error }: { error: RpcError }) {
  const t = useT();
  const bridge = useBridge();
  const language = useAppStore((s) => s.language);
  const [open, setOpen] = useState(false);
  const help = helpLinkFor(error.code, language);
  const key = `error.${error.code}`;
  const message = isMessageKey(key) ? t(key) : t('error.internal');
  const detail = error.detail || error.message;

  return (
    <div className={styles.errorNotice} data-testid="error-notice" data-error-code={error.code}>
      <div className={styles.errorLine}>
        <span>{message}</span>
        {help && (
          <Button
            size="small"
            variant="ghost"
            data-testid="error-help"
            onClick={() => void bridge.call('shell.openUrl', { url: help }).catch(() => undefined)}
          >
            {t('action.howToFix')} ↗
          </Button>
        )}
        {detail && (
          <Button size="small" variant="ghost" onClick={() => setOpen(!open)}>
            {open ? t('action.hideDetails') : t('action.showDetails')}
          </Button>
        )}
      </div>
      {open && detail && <pre className={styles.errorDetail}>{detail}</pre>}
    </div>
  );
}
