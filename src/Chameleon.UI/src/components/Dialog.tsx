import { useEffect, type ReactNode } from 'react';
import { useT } from '../state/services';
import { Button } from './Button';
import styles from './components.module.css';

export interface DialogProps {
  title: string;
  onClose(): void;
  footer?: ReactNode;
  children: ReactNode;
  testId?: string;
}

/** Modal dialog inside the page (no extra native window). Esc closes it. */
export function Dialog({ title, onClose, footer, children, testId }: DialogProps) {
  const t = useT();
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div
      className={styles.dialogScrim}
      onMouseDown={(event) => event.target === event.currentTarget && onClose()}
    >
      <div className={styles.dialog} role="dialog" aria-modal="true" aria-label={title} data-testid={testId}>
        <div className={styles.dialogHeader}>
          <span>{title}</span>
          <Button variant="ghost" size="small" onClick={onClose} aria-label={t('action.close')}>
            ✕
          </Button>
        </div>
        <div className={styles.dialogBody}>{children}</div>
        {footer && <div className={styles.dialogFooter}>{footer}</div>}
      </div>
    </div>
  );
}
