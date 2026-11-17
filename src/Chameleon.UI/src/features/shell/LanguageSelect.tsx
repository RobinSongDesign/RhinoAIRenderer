import type { Language } from '../../bridge/types';
import { Select } from '../../components/Select';
import { useAppStore, useServices, useT } from '../../state/services';

/** Switches the UI language; the change applies immediately and is saved by the host. */
export function LanguageSelect({ compact }: { compact?: boolean }) {
  const t = useT();
  const { app } = useServices();
  const language = useAppStore((s) => s.language);
  return (
    <Select<Language>
      aria-label={t('label.language')}
      data-testid="language-select"
      style={compact ? { width: 'auto' } : undefined}
      value={language}
      options={[
        { value: 'zh', label: t('language.zh') },
        { value: 'en', label: t('language.en') },
      ]}
      onChange={(value) => void app.getState().updateSettings({ language: value })}
    />
  );
}
