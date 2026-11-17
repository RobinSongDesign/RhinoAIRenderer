import { Button } from '../../components/Button';
import { controlClass, Field } from '../../components/Field';
import { Select } from '../../components/Select';
import type { MessageKey } from '../../i18n';
import { useServices, useSettingsStore, useT } from '../../state/services';
import styles from './settings.module.css';

/** The selected provider: its endpoint, its API key and the key test. */
export function ProviderDetails() {
  const t = useT();
  const { settings } = useServices();
  const provider = useSettingsStore((s) => s.providers.find((p) => p.id === s.selectedId));
  const keyInput = useSettingsStore((s) => s.keyInput);
  const testModel = useSettingsStore((s) => s.testModel);
  const testState = useSettingsStore((s) => s.testState);
  const confirmingDelete = useSettingsStore((s) => s.confirmingDelete);
  const busy = useSettingsStore((s) => s.busy);
  if (!provider) {
    return null;
  }
  const store = settings.getState();

  return (
    <div className={styles.box} data-testid="provider-details" data-provider={provider.id}>
      <dl className={styles.facts}>
        <dt>{t('settings.baseUrl')}</dt>
        <dd data-testid="provider-detail-url">{provider.baseUrl}</dd>
        <dt>{t('settings.apiFormat')}</dt>
        <dd>{t(`format.${provider.apiFormat}` as MessageKey)}</dd>
        <dt>{t('settings.authType')}</dt>
        <dd>{t(`auth.${provider.authType}` as MessageKey)}</dd>
        <dt>{t('settings.models')}</dt>
        <dd data-testid="provider-detail-models">{provider.models.join(', ')}</dd>
      </dl>

      <div className={styles.actions}>
        <Button size="small" data-testid="provider-edit" onClick={() => store.startEdit()}>
          {t('settings.edit')}
        </Button>
        {provider.isBuiltIn ? (
          provider.isOverridden && (
            <Button
              size="small"
              data-testid="provider-reset"
              busy={busy}
              onClick={() => void store.resetSelected()}
            >
              {t('settings.reset')}
            </Button>
          )
        ) : confirmingDelete ? (
          <>
            <span className={styles.confirm}>
              {t('settings.confirmDelete', { name: provider.displayName })}
            </span>
            <Button
              size="small"
              variant="danger"
              data-testid="provider-delete-confirm"
              busy={busy}
              onClick={() => void store.deleteSelected()}
            >
              {t('settings.delete')}
            </Button>
            <Button size="small" onClick={() => store.cancelDelete()}>
              {t('action.cancel')}
            </Button>
          </>
        ) : (
          <Button
            size="small"
            variant="danger"
            data-testid="provider-delete"
            onClick={() => store.askDelete()}
          >
            {t('settings.delete')}
          </Button>
        )}
      </div>

      <Field label={t('settings.apiKey')}>
        <div className={styles.actions}>
          <input
            type="password"
            autoComplete="off"
            className={`${controlClass} ${styles.grow}`}
            data-testid="key-input"
            value={keyInput}
            placeholder={provider.hasKey ? t('settings.keySaved') : t('settings.keyPlaceholder')}
            onChange={(e) => store.setKeyInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && void store.saveKey()}
          />
          <Button
            variant="primary"
            data-testid="key-save"
            disabled={!keyInput.trim()}
            busy={busy}
            onClick={() => void store.saveKey()}
          >
            {t('settings.saveKey')}
          </Button>
          {provider.hasKey && (
            <Button data-testid="key-clear" onClick={() => void store.clearKey()}>
              {t('settings.clearKey')}
            </Button>
          )}
        </div>
      </Field>
      {provider.apiKeyUrl && (
        <div>
          <Button
            size="small"
            variant="ghost"
            data-testid="key-link"
            onClick={() => void store.openKeyPage()}
          >
            {t('settings.getKey')} ↗
          </Button>
        </div>
      )}

      <Field label={t('settings.testModel')}>
        <div className={styles.actions}>
          <Select
            className={styles.grow}
            data-testid="test-model"
            value={testModel}
            options={provider.models.map((m) => ({ value: m, label: m }))}
            onChange={(model) => store.setTestModel(model)}
          />
          <Button data-testid="key-test" busy={testState === 'testing'} onClick={() => void store.testKey()}>
            {t('settings.testKey')}
          </Button>
        </div>
      </Field>
    </div>
  );
}
