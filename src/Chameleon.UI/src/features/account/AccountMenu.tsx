import { useEffect, useRef } from 'react';
import { Button } from '../../components/Button';
import { useAccountStore, useAppStore, useServices, useT } from '../../state/services';
import styles from './account.module.css';

/** Header control: "Sign in" when signed out, the balance and an account menu when signed in. */
export function AccountMenu() {
  const t = useT();
  const { account } = useServices();
  const info = useAppStore((s) => s.account);
  const open = useAccountStore((s) => s.menuOpen);
  const anchor = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const close = (event: MouseEvent) => {
      if (anchor.current && !anchor.current.contains(event.target as Node)) {
        account.getState().toggleMenu(false);
      }
    };
    window.addEventListener('mousedown', close);
    return () => window.removeEventListener('mousedown', close);
  }, [open, account]);

  if (!info.available) {
    return null;
  }

  if (!info.signedIn) {
    return (
      <Button size="small" data-testid="account-sign-in" onClick={() => void account.getState().startLogin()}>
        {t('account.signIn')}
      </Button>
    );
  }

  return (
    <div className={styles.anchor} ref={anchor}>
      <Button
        size="small"
        data-testid="account-button"
        title={info.email}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => account.getState().toggleMenu()}
      >
        <span className={styles.credits} data-testid="account-credits">
          {t('account.credits', { credits: info.credits ?? 0 })}
        </span>
      </Button>
      {open && (
        <div className={styles.menu} role="menu" data-testid="account-menu">
          <div className={styles.menuHeader}>{t('account.signedInAs', { email: info.email ?? '' })}</div>
          <button type="button" role="menuitem" className={styles.menuItem} data-testid="account-redeem" onClick={() => account.getState().openRedeem()}>
            {t('account.redeem')}
          </button>
          <button type="button" role="menuitem" className={styles.menuItem} data-testid="account-ledger" onClick={() => void account.getState().openLedger()}>
            {t('account.ledger')}
          </button>
          <button type="button" role="menuitem" className={styles.menuItem} data-testid="account-sign-out" onClick={() => void account.getState().signOut()}>
            {t('account.signOut')}
          </button>
        </div>
      )}
    </div>
  );
}
