import { Button } from '../../components/Button';
import { useAccountStore, useAppStore, useServices, useT } from '../../state/services';
import styles from './account.module.css';

/**
 * First-run guidance: while no real backend is usable (no API key stored, not signed in),
 * offer the two ways to start. Inline rather than modal, so it never blocks the window.
 */
export function WelcomeCard() {
  const t = useT();
  const { account, ui } = useServices();
  const available = useAppStore((s) => s.account.available);
  const anyReady = useAppStore((s) => s.backends.some((b) => b.ready && b.kind !== 'dev'));
  const dismissed = useAccountStore((s) => s.welcomeDismissed);

  if (!available || anyReady || dismissed) {
    return null;
  }

  return (
    <div className={styles.welcome} data-testid="welcome-card">
      <span className={styles.welcomeTitle}>{t('welcome.title')}</span>
      <span>{t('welcome.body')}</span>
      <Button variant="primary" block data-testid="welcome-account" onClick={() => void account.getState().startLogin()}>
        {t('welcome.account')}
      </Button>
      <Button block data-testid="welcome-own-key" onClick={() => ui.getState().openSettings()}>
        {t('welcome.ownKey')}
      </Button>
      <Button size="small" variant="ghost" data-testid="welcome-dismiss" onClick={() => account.getState().dismissWelcome()}>
        {t('welcome.dismiss')}
      </Button>
    </div>
  );
}
