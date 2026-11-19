import { Button } from '../../components/Button';
import componentStyles from '../../components/components.module.css';
import { Field } from '../../components/Field';
import { Select } from '../../components/Select';
import { selectActiveBackend } from '../../state/appStore';
import { useAppStore, useServices, useT } from '../../state/services';
import { Section } from '../shell/Section';
import { BalanceLine, providerIdOf } from './BalanceLine';

/** Which backend and model generate the image; warns when the backend still needs a key. */
export function BackendSection() {
  const t = useT();
  const { app, ui, account, relaySignIn } = useServices();
  const backends = useAppStore((s) => s.backends);
  const activeBackendId = useAppStore((s) => s.activeBackendId);
  const activeModelId = useAppStore((s) => s.activeModelId);
  const backend = useAppStore(selectActiveBackend);

  return (
    <Section title={t('section.backend')} testId="backend-section">
      <Field label={t('label.backend')}>
        <Select
          data-testid="backend-select"
          value={activeBackendId}
          options={backends.map((b) => ({ value: b.id, label: b.displayName }))}
          onChange={(id) => void app.getState().selectBackend(id)}
        />
      </Field>
      {backend && !backend.ready && (
        <div className={componentStyles.banner} data-testid="backend-not-ready">
          {backend.kind === 'cloud' ? (
            <>
              {t('backend.signInRequired')}{' '}
              <Button size="small" onClick={() => void account.getState().startLogin()}>
                {t('account.signIn')}
              </Button>
            </>
          ) : backend.supportsSignIn ? (
            <>
              {t('backend.signInPrompt', { name: backend.displayName })}{' '}
              <Button
                size="small"
                variant="primary"
                data-testid="backend-signin"
                onClick={() => void relaySignIn.getState().open(providerIdOf(backend.id))}
              >
                {t('signIn.action')}
              </Button>{' '}
              <Button size="small" variant="ghost" data-testid="backend-use-key" onClick={() => ui.getState().openSettings()}>
                {t('backend.useKey')}
              </Button>
            </>
          ) : (
            <>
              {t('backend.notReady', { name: backend.displayName })}{' '}
              <Button size="small" onClick={() => ui.getState().openSettings()}>
                {t('action.openSettings')}
              </Button>
            </>
          )}
        </div>
      )}
      {backend && <BalanceLine backend={backend} />}
      <Field label={t('label.model')}>
        <Select
          data-testid="model-select"
          value={activeModelId}
          options={(backend?.models ?? []).map((m) => ({
            value: m.id,
            label: m.displayName && m.displayName !== m.id ? `${m.displayName} (${m.id})` : m.id,
          }))}
          onChange={(modelId) => void app.getState().selectBackend(activeBackendId, modelId)}
        />
      </Field>
    </Section>
  );
}
