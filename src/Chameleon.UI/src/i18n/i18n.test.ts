import { describe, expect, it } from 'vitest';
import en from './en.json';
import zh from './zh.json';
import { createTranslator, isMessageKey, localize, normalizeLanguage } from './index';

describe('translations', () => {
  it('define exactly the same keys in both languages', () => {
    expect(Object.keys(zh).sort()).toEqual(Object.keys(en).sort());
  });

  it('have no empty values', () => {
    for (const [key, value] of [...Object.entries(en), ...Object.entries(zh)]) {
      expect(value, key).not.toBe('');
    }
  });

  it('use the same placeholders in both languages', () => {
    const placeholders = (text: string) => (text.match(/\{\w+\}/g) ?? []).sort().join(',');
    for (const key of Object.keys(en) as (keyof typeof en)[]) {
      expect(placeholders(zh[key]), key).toBe(placeholders(en[key]));
    }
  });

  it('cover every error code the host can send', () => {
    const codes = [
      'invalid_request',
      'auth_failed',
      'not_ready',
      'insufficient_credits',
      'rate_limited',
      'content_blocked',
      'model_unavailable',
      'timeout',
      'network',
      'provider_error',
      'no_image',
      'cancelled',
      'rhino_error',
      'not_found',
      'internal',
      'disconnected',
    ];
    for (const code of codes) {
      expect(isMessageKey(`error.${code}`), code).toBe(true);
    }
  });
});

describe('createTranslator', () => {
  it('fills placeholders and leaves unknown ones untouched', () => {
    const t = createTranslator('en');
    expect(t('status.captured', { width: 1920, height: 1080 })).toBe('Captured: 1920×1080');
    expect(t('batch.progress', { done: 2 })).toBe('2 / {total} done');
  });

  it('switches language', () => {
    expect(createTranslator('zh')('mode.batch')).toBe('批量');
    expect(createTranslator('en')('mode.batch')).toBe('Batch');
  });
});

describe('language helpers', () => {
  it('normalize any tag to zh or en', () => {
    expect(normalizeLanguage('zh-CN')).toBe('zh');
    expect(normalizeLanguage('ZH-tw')).toBe('zh');
    expect(normalizeLanguage('en-US')).toBe('en');
    expect(normalizeLanguage('fr')).toBe('en');
    expect(normalizeLanguage(undefined)).toBe('en');
  });

  it('localize catalog texts with fallback', () => {
    expect(localize({ zh: '黄昏', en: 'Dusk' }, 'zh')).toBe('黄昏');
    expect(localize({ zh: '', en: 'Dusk' }, 'zh')).toBe('Dusk');
  });
});
