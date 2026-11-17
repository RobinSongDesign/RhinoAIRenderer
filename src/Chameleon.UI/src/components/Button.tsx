import type { ButtonHTMLAttributes, ReactNode } from 'react';
import styles from './components.module.css';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger';
  size?: 'normal' | 'small';
  block?: boolean;
  /** Shows a spinner and disables the button. */
  busy?: boolean;
  children: ReactNode;
}

/** The one button style of the application. */
export function Button({
  variant = 'secondary',
  size = 'normal',
  block,
  busy,
  disabled,
  className,
  children,
  ...rest
}: ButtonProps) {
  const classes = [
    styles.button,
    variant !== 'secondary' ? styles[variant] : '',
    size === 'small' ? styles.small : '',
    block ? styles.block : '',
    className ?? '',
  ]
    .filter(Boolean)
    .join(' ');
  return (
    <button type="button" className={classes} disabled={disabled || busy} {...rest}>
      {busy && <span className={styles.spinner} aria-hidden="true" />}
      {children}
    </button>
  );
}
