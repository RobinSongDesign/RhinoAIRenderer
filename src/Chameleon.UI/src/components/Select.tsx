import type { SelectHTMLAttributes } from 'react';
import { controlClass } from './Field';

export interface SelectOption<T extends string> {
  value: T;
  label: string;
}

export interface SelectProps<T extends string> extends Omit<
  SelectHTMLAttributes<HTMLSelectElement>,
  'onChange' | 'value'
> {
  value: T;
  options: readonly SelectOption<T>[];
  onChange(value: T): void;
}

/** Native select (keeps keyboard and IME behaviour of the platform WebView). */
export function Select<T extends string>({ value, options, onChange, className, ...rest }: SelectProps<T>) {
  return (
    <select
      className={[controlClass, className ?? ''].join(' ')}
      value={value}
      onChange={(event) => onChange(event.target.value as T)}
      {...rest}
    >
      {options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  );
}
