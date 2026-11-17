import type { TextareaHTMLAttributes } from 'react';
import { controlClass } from './Field';

/** Multi-line text input. */
export function TextArea({ className, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea className={[controlClass, className ?? ''].join(' ')} spellCheck={false} {...rest} />;
}
