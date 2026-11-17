import type { ReactNode } from 'react';
import styles from './shell.module.css';

/** A titled group of controls in the side panel. */
export function Section({
  title,
  children,
  testId,
}: {
  title: string;
  children: ReactNode;
  testId?: string;
}) {
  return (
    <section className={styles.section} data-testid={testId}>
      <h2>{title}</h2>
      {children}
    </section>
  );
}

/** Lays children out side by side with equal widths. */
export function Row({ children }: { children: ReactNode }) {
  return <div className={styles.row}>{children}</div>;
}
