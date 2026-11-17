import type { ReactNode } from 'react';
import { Button } from '../../components/Button';
import componentStyles from '../../components/components.module.css';
import { PreviewOverlay } from '../../components/PreviewOverlay';
import { StatusBar } from '../../components/StatusBar';
import type { StatusMessage } from '../../state/status';
import type { Mode } from '../../state/uiStore';
import { useAppStore, useServices, useT, useUiStore } from '../../state/services';
import { LanguageSelect } from './LanguageSelect';
import styles from './shell.module.css';

export interface ModeView {
  workspace: ReactNode;
  panel: ReactNode;
  footer: ReactNode;
  status: StatusMessage | null;
  statusExtra?: string;
}

/**
 * Window layout: images on the left, the parameter panel on the right, the status bar at the
 * bottom. The active mode supplies the content of each area.
 */
export function AppShell({ views, dialogs }: { views: Record<Mode, ModeView>; dialogs?: ReactNode }) {
  const t = useT();
  const { ui } = useServices();
  const mode = useUiStore((s) => s.mode);
  const connection = useAppStore((s) => s.connection);
  const loadState = useAppStore((s) => s.loadState);
  const loadError = useAppStore((s) => s.loadError);

  if (loadState === 'loading') {
    return <div className={styles.splash}>{t('app.loading')}</div>;
  }
  if (loadState === 'failed') {
    return (
      <div className={styles.splash} data-testid="load-failed">
        {t('app.loadFailed')}
        {loadError ? ` (${loadError})` : ''}
      </div>
    );
  }

  const view = views[mode];
  const tab = (value: Mode, label: string) => (
    <button
      type="button"
      role="tab"
      className={styles.modeTab}
      aria-selected={mode === value}
      data-testid={`mode-${value}`}
      onClick={() => ui.getState().setMode(value)}
    >
      {label}
    </button>
  );

  return (
    <div className={styles.app} data-testid="app-shell" data-mode={mode}>
      <div className={styles.banner}>
        {connection === 'disconnected' && (
          <div className={componentStyles.banner} data-testid="connection-banner">
            {t('app.connectionLost')}
          </div>
        )}
      </div>
      <main className={styles.workspace}>{view.workspace}</main>
      <aside className={styles.panel}>
        <div className={styles.panelHeader}>
          <div className={styles.modeTabs} role="tablist">
            {tab('single', t('mode.single'))}
            {tab('batch', t('mode.batch'))}
          </div>
          <LanguageSelect compact />
          <Button
            variant="ghost"
            aria-label={t('action.settings')}
            title={t('action.settings')}
            data-testid="open-settings"
            onClick={() => ui.getState().openSettings()}
          >
            ⚙
          </Button>
        </div>
        <div className={styles.panelBody}>{view.panel}</div>
        <div className={styles.panelFooter}>{view.footer}</div>
      </aside>
      <div className={styles.status}>
        <StatusBar message={view.status} extra={view.statusExtra} />
      </div>
      {dialogs}
      <PreviewOverlay />
    </div>
  );
}
