import type { Language, LocalizedText } from '../bridge/types';
import en from './en.json';
import zh from './zh.json';

/** Every translation key. Both language files must define the same keys (checked by a test). */
export type MessageKey = keyof typeof en;

export type MessageParams = Record<string, string | number>;

/** Translates a key in the given language, filling {placeholders}. */
export type Translate = (key: MessageKey, params?: MessageParams) => string;

const dictionaries: Record<Language, Record<MessageKey, string>> = { en, zh };

/** Returns a translator for a language. */
export function createTranslator(language: Language): Translate {
  const dictionary = dictionaries[language] ?? dictionaries.en;
  return (key, params) => {
    const template = dictionary[key] ?? dictionaries.en[key] ?? key;
    if (!params) {
      return template;
    }
    return template.replace(/\{(\w+)\}/g, (match, name: string) =>
      Object.prototype.hasOwnProperty.call(params, name) ? String(params[name]) : match,
    );
  };
}

/** Maps any browser or host language tag onto a supported language. */
export function normalizeLanguage(tag: string | null | undefined): Language {
  return tag && tag.toLowerCase().startsWith('zh') ? 'zh' : 'en';
}

/** Picks the text for a language from a catalog entry, falling back to the other language. */
export function localize(text: LocalizedText, language: Language): string {
  const primary = language === 'zh' ? text.zh : text.en;
  return primary || (language === 'zh' ? text.en : text.zh);
}

/** True when the string is a known message key. */
export function isMessageKey(key: string): key is MessageKey {
  return Object.prototype.hasOwnProperty.call(dictionaries.en, key);
}
