import type { Language, ProviderBalance } from '../../bridge/types';
import type { Translate } from '../../i18n';

/** Formats an amount of a currency for the UI language. */
export function formatMoney(amount: number, currency: string, language: Language): string {
  return new Intl.NumberFormat(language === 'zh' ? 'zh-CN' : 'en-US', {
    style: 'currency',
    currency,
    maximumFractionDigits: 2,
  }).format(amount);
}

/**
 * The balance line's text: what is left, "no limit" (with what was spent) for keys without a
 * limit, or a failure note. A missing amount counts as "no limit", never as a number: older
 * hosts left the field out, which used to show as "NaN".
 */
export function balanceText(balance: ProviderBalance | null, t: Translate, language: Language): string {
  if (!balance) {
    return t('balance.failed');
  }
  const used = typeof balance.used === 'number' && Number.isFinite(balance.used) ? balance.used : 0;
  if (typeof balance.remaining !== 'number' || !Number.isFinite(balance.remaining)) {
    return t('balance.unlimited', { used: formatMoney(used, balance.currency, language) });
  }
  return t('balance.label', { amount: formatMoney(balance.remaining, balance.currency, language) });
}
