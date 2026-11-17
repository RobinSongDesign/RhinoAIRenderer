import type { ReactNode } from 'react';
import styles from './components.module.css';

/** A labelled form control. */
export function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label className={styles.field}>
      <span className={styles.fieldLabel}>{label}</span>
      {children}
      {hint && <span className={styles.fieldHint}>{hint}</span>}
    </label>
  );
}

/** Class name for inputs, selects and textareas, so they all look alike. */
export const controlClass = styles.control;
