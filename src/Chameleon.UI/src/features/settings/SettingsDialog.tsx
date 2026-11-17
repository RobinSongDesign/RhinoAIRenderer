import { useEffect, useState } from 'react';
import { Button } from '../../components/Button';
import { Dialog } from '../../components/Dialog';
import { ErrorNotice } from '../../components/ErrorNotice';
import { Field } from '../../components/Field';
import { Select } from '../../components/Select';
import { TextArea } from '../../components/TextArea';
import type { BatchMode } from '../../bridge/types';
import { useAppStore, useServices, useSettingsStore, useT, useUiStore } from '../../state/services';
import { CLOUD_BACKEND_ID } from '../../bridge/types';
import { LanguageSelect } from '../shell/LanguageSelect';
import { ProviderDetails } from './ProviderDetails';
import { ProviderForm } from './ProviderForm';
import styles from './settings.module.css';

const DIRECT_PREFIX = 'direct:';

function ProvidersGroup() {
  const t = useT();
  const { settings } = useServices();
  const providers = useSettingsStore((s) => s.providers);
  const selectedId = useSettingsStore((s) => s.selectedId);
  const editing = useSettingsStore((s) => s.draft !== null);
  const message = useSettingsStore((s) => s.message);

  return (
    <section className={styles.group}>
      <h2>{t('settings.providers')}</h2>
      <div className={styles.actions}>
        <Select
          className={styles.grow}
          data-testid="provider-select"
          disabled={editing}
          value={selectedId}
          options={providers.map((p) => ({
            value: p.id,
            label: `${p.displayName}${p.isBuiltIn ? ` · ${t('settings.builtIn')}` : ''}${p.hasKey ? ' · ✓' : ''}`,
          }))}
          onChange={(id) => settings.getState().select(id)}
        />
        <Button data-testid="provider-add" disabled={editing} onClick={() => settings.getState().startAdd()}>
          {t('settings.add')}
        </Button>
      </div>
      {editing ? <ProviderForm /> : <ProviderDetails />}
      <div className={styles.message} data-testid="settings-message" data-kind={message?.kind ?? ''}>
        {message?.kind === 'error' && message.error ? (
          <ErrorNotice error={message.error} />
        ) : (
          message && (
            <span className={message.kind === 'success' ? styles.success : ''}>
              {t(message.key, message.params)}
            </span>
          )
        )}
      </div>
    </section>
  );
}

type AccessMode = 'account' | 'ownKey';

/** Switches between account mode (cloud backend) and bring-your-own-key mode (direct backends). */
function AccessGroup() {
  const t = useT();
  const { app, account } = useServices();
  const info = useAppStore((s) => s.account);
  const backends = useAppStore((s) => s.backends);
  const mode: AccessMode = useAppStore((s) => (s.activeBackendId === CLOUD_BACKEND_ID ? 'account' : 'ownKey'));

  const choose = (next: AccessMode) => {
    if (next === mode) {
      return;
    }
    if (next === 'account') {
      void app.getState().selectBackend(CLOUD_BACKEND_ID);
      return;
    }
    // Prefer a provider that already has a key.
    const direct = backends.filter((b) => b.kind === 'direct');
    const target = direct.find((b) => b.ready) ?? direct[0];
    if (target) {
      void app.getState().selectBackend(target.id);
    }
  };

  return (
    <section className={styles.group}>
      <h2>{t('settings.access')}</h2>
      <Field label={t('settings.accessMode')}>
        <Select<AccessMode>
          data-testid="access-mode"
          value={mode}
          options={[
            { value: 'account', label: t('settings.access.account') },
            { value: 'ownKey', label: t('settings.access.ownKey') },
          ]}
          onChange={choose}
        />
      </Field>
      {mode === 'account' && (
        <div className={styles.actions}>
          <span className={styles.grow} data-testid="settings-account-status">
            {info.signedIn
              ? `${t('account.signedInAs', { email: info.email ?? '' })} · ${t('account.credits', { credits: info.credits ?? 0 })}`
              : t('account.notSignedIn')}
          </span>
          {info.signedIn ? (
            <Button onClick={() => void account.getState().signOut()}>{t('account.signOut')}</Button>
          ) : (
            <Button variant="primary" onClick={() => void account.getState().startLogin()}>
              {t('account.signIn')}
            </Button>
          )}
        </div>
      )}
    </section>
  );
}

/**
 * Editor for the system prompt. Remounted (via key) whenever the stored value changes, so the
 * draft always starts from what the host saved.
 */
function SystemPromptEditor({ stored, fallback }: { stored: string | null; fallback: string }) {
  const t = useT();
  const { app } = useServices();
  const initial = stored ?? fallback;
  const [draft, setDraft] = useState(initial);

  const save = () => {
    const text = draft.trim();
    void app.getState().updateSettings({ systemPrompt: text && text !== fallback ? draft : null });
  };

  return (
    <>
      <Field label={t('settings.systemPrompt')} hint={t('settings.systemPromptHint')}>
        <TextArea
          data-testid="system-prompt"
          rows={4}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
        />
      </Field>
      <div className={styles.actions}>
        <Button data-testid="system-prompt-save" disabled={draft === initial} onClick={save}>
          {t('action.save')}
        </Button>
        <Button
          data-testid="system-prompt-reset"
          disabled={stored === null}
          onClick={() => void app.getState().updateSettings({ systemPrompt: null })}
        >
          {t('settings.restoreDefault')}
        </Button>
      </div>
    </>
  );
}

function AdvancedGroup() {
  const t = useT();
  const { app } = useServices();
  const stored = useAppStore((s) => s.settings?.systemPrompt ?? null);
  const fallback = useAppStore((s) => s.catalog?.defaultSystemPrompt ?? '');
  const batchMode = useAppStore((s) => s.settings?.batch.mode ?? 'serial');

  return (
    <section className={styles.group}>
      <h2>{t('settings.advanced')}</h2>
      <SystemPromptEditor key={stored ?? '\u0000default'} stored={stored} fallback={fallback} />
      <Field label={t('settings.batchMode')}>
        <Select<BatchMode>
          data-testid="batch-mode"
          value={batchMode}
          options={[
            { value: 'serial', label: t('settings.batchMode.serial') },
            { value: 'anchorParallel', label: t('settings.batchMode.anchorParallel') },
            { value: 'independent', label: t('settings.batchMode.independent') },
          ]}
          onChange={(mode) => void app.getState().updateSettings({ batch: { mode } })}
        />
      </Field>
    </section>
  );
}

function AboutGroup() {
  const t = useT();
  const host = useAppStore((s) => s.host);
  return (
    <section className={styles.group}>
      <h2>{t('settings.about')}</h2>
      <dl className={styles.facts}>
        <dt>{t('settings.version')}</dt>
        <dd data-testid="about-version">{host?.appVersion}</dd>
        <dt>{t('settings.protocol')}</dt>
        <dd>{host?.protocolVersion}</dd>
        {host?.rhinoVersion && (
          <>
            <dt>{t('settings.rhino')}</dt>
            <dd>{host.rhinoVersion}</dd>
          </>
        )}
        <dt>Logs</dt>
        <dd>{host?.logsDirectory}</dd>
      </dl>
    </section>
  );
}

/** Providers and keys, language, advanced options and version information. */
export function SettingsDialog() {
  const t = useT();
  const { ui, settings } = useServices();
  const open = useUiStore((s) => s.settingsOpen);
  const activeBackendId = useAppStore((s) => s.activeBackendId);
  const accountAvailable = useAppStore((s) => s.account.available);
  const accountMode = activeBackendId === CLOUD_BACKEND_ID;

  useEffect(() => {
    if (open) {
      const providerId = activeBackendId.startsWith(DIRECT_PREFIX)
        ? activeBackendId.slice(DIRECT_PREFIX.length)
        : undefined;
      void settings.getState().open(providerId);
    }
    // Only when the dialog opens: switching backends afterwards must not reset what the user is editing.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  if (!open) {
    return null;
  }
  return (
    <Dialog
      title={t('settings.title')}
      testId="settings-dialog"
      onClose={() => ui.getState().closeSettings()}
      footer={
        <Button variant="primary" data-testid="settings-close" onClick={() => ui.getState().closeSettings()}>
          {t('action.close')}
        </Button>
      }
    >
      <div className={styles.body}>
        {accountAvailable && <AccessGroup />}
        {!accountMode && <ProvidersGroup />}
        <section className={styles.group}>
          <h2>{t('settings.general')}</h2>
          <Field label={t('label.language')}>
            <LanguageSelect />
          </Field>
        </section>
        <AdvancedGroup />
        <AboutGroup />
      </div>
    </Dialog>
  );
}
