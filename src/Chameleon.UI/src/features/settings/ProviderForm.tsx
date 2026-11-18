import { Button } from '../../components/Button';
import { controlClass, Field } from '../../components/Field';
import { Select } from '../../components/Select';
import type { ApiFormat, AuthType } from '../../bridge/types';
import { useServices, useSettingsStore, useT } from '../../state/services';
import { Row } from '../shell/Section';
import styles from './settings.module.css';

/** Add/edit form for a provider (fields as in the WPF "Add provider" dialog). */
export function ProviderForm() {
  const t = useT();
  const { settings } = useServices();
  const draft = useSettingsStore((s) => s.draft);
  const busy = useSettingsStore((s) => s.busy);
  if (!draft) {
    return null;
  }
  const update = settings.getState().updateDraft;

  return (
    <div className={styles.box} data-testid="provider-form">
      <strong>{draft.id ? t('settings.editProvider') : t('settings.newProvider')}</strong>
      <Field label={t('settings.name')}>
        <input
          className={controlClass}
          data-testid="provider-name"
          value={draft.displayName}
          onChange={(e) => update({ displayName: e.target.value })}
        />
      </Field>
      <Field label={t('settings.baseUrl')}>
        <input
          className={controlClass}
          data-testid="provider-url"
          value={draft.baseUrl}
          placeholder="https://"
          onChange={(e) => update({ baseUrl: e.target.value })}
        />
      </Field>
      <Row>
        <Field label={t('settings.apiFormat')}>
          <Select<ApiFormat>
            data-testid="provider-format"
            value={draft.apiFormat}
            options={[
              { value: 'gemini', label: t('format.gemini') },
              { value: 'openai', label: t('format.openai') },
            ]}
            onChange={(apiFormat) => update({ apiFormat })}
          />
        </Field>
        <Field label={t('settings.authType')}>
          <Select<AuthType>
            data-testid="provider-auth"
            value={draft.authType}
            disabled={draft.apiFormat === 'openai'}
            options={[
              { value: 'bearer', label: t('auth.bearer') },
              { value: 'goog', label: t('auth.goog') },
            ]}
            onChange={(authType) => update({ authType })}
          />
        </Field>
      </Row>
      <Field label={t('settings.models')} hint={t('settings.modelsHint')}>
        <input
          className={controlClass}
          data-testid="provider-models"
          value={draft.models}
          onChange={(e) => update({ models: e.target.value })}
        />
      </Field>
      {draft.apiFormat === 'gemini' && (
        <label className={styles.checkbox}>
          <input
            type="checkbox"
            data-testid="provider-search"
            checked={draft.enableSearchGrounding}
            onChange={(e) => update({ enableSearchGrounding: e.target.checked })}
          />
          {t('settings.searchGrounding')}
        </label>
      )}
      {!draft.isBuiltIn && (
        <>
          <label className={styles.checkbox}>
            <input
              type="checkbox"
              data-testid="provider-balance"
              checked={draft.reportsBalance}
              onChange={(e) => update({ reportsBalance: e.target.checked })}
            />
            {t('settings.reportsBalance')}
          </label>
          <Field label={t('settings.topUpUrl')} hint={t('settings.topUpUrlHint')}>
            <input
              className={controlClass}
              data-testid="provider-topup"
              value={draft.topUpUrl}
              placeholder="https://"
              onChange={(e) => update({ topUpUrl: e.target.value })}
            />
          </Field>
        </>
      )}
      <div className={styles.actions}>
        <Button
          variant="primary"
          busy={busy}
          data-testid="provider-save"
          onClick={() => void settings.getState().saveDraft()}
        >
          {t('settings.saveProvider')}
        </Button>
        <Button onClick={() => settings.getState().cancelEdit()}>{t('action.cancel')}</Button>
      </div>
    </div>
  );
}
