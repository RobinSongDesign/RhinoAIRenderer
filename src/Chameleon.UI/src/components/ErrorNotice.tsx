import { useState } from 'react';
import type { RpcError } from '../bridge/types';
import { isMessageKey } from '../i18n';
import { useT } from '../state/services';
import { Button } from './Button';
import styles from './components.module.css';

/** A localized error line with the provider's own message behind a "details" toggle. */
export function ErrorNotice({ error }: { error: RpcError }) {
  const t = useT();
  const [open, setOpen] = useState(false);
  const key = `error.${error.code}`;
  const message = isMessageKey(key) ? t(key) : t('error.internal');
  const detail = error.detail || error.message;

  return (
    <div className={styles.errorNotice} data-testid="error-notice" data-error-code={error.code}>
      <div className={styles.errorLine}>
        <span>{message}</span>
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
