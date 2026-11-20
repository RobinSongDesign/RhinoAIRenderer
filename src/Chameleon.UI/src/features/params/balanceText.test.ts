import { describe, expect, it } from 'vitest';
import { createTranslator } from '../../i18n';
import type { ProviderBalance } from '../../bridge/types';
import { balanceText } from './balanceText';

const t = createTranslator('en');

describe('balanceText', () => {
  it('shows what is left', () => {
    expect(balanceText({ remaining: 2.9, used: 0.1, total: 3, currency: 'USD' }, t, 'en')).toBe('Balance: $2.90');
  });

  it('shows keys without a limit as unlimited, whether the amount is null or missing', () => {
    expect(balanceText({ remaining: null, used: 0.5, total: null, currency: 'USD' }, t, 'en')).toContain('$0.50');
    // What an older host sent for a relay key without a limit: no remaining at all (shown as "NaN").
    const missing = { used: 0, currency: 'USD' } as unknown as ProviderBalance;
    const text = balanceText(missing, t, 'en');
    expect(text).not.toContain('NaN');
    expect(text).toBe(balanceText({ remaining: null, used: 0, total: null, currency: 'USD' }, t, 'en'));
  });

  it('reports a failed query', () => {
    expect(balanceText(null, t, 'en')).toBe(t('balance.failed'));
  });
});
