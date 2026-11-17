import type { StatusMessage } from '../state/status';
import { useT } from '../state/services';
import styles from './components.module.css';
import { ErrorNotice } from './ErrorNotice';

/** Bottom line: the current status of the active mode, or its error. */
export function StatusBar({ message, extra }: { message: StatusMessage | null; extra?: string }) {
  const t = useT();
  const classes = [
    styles.statusText,
    message?.kind === 'success' ? styles.statusSuccess : '',
    message?.kind === 'error' ? styles.statusError : '',
  ].join(' ');

  return (
    <footer className={styles.statusBar} data-testid="status-bar" data-kind={message?.kind ?? 'info'}>
      {message?.kind === 'busy' && <span className={styles.spinner} aria-hidden="true" />}
      {message?.kind === 'error' && message.error ? (
        <ErrorNotice error={message.error} />
      ) : (
        <span className={classes} data-testid="status-text">
          {message ? t(message.key, message.params) : t('status.ready')}
        </span>
      )}
      {extra && <span>{extra}</span>}
    </footer>
  );
}
