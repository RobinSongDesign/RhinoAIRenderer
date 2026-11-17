import { useState } from 'react';
import { Button } from '../../components/Button';
import componentStyles from '../../components/components.module.css';
import { Dialog } from '../../components/Dialog';
import { ErrorNotice } from '../../components/ErrorNotice';
import { controlClass, Field } from '../../components/Field';
import type { MessageKey } from '../../i18n';
import { useAccountStore, useAppStore, useBridge, useServices, useT } from '../../state/services';
import styles from './account.module.css';
import { formatRedeemCode } from './accountStore';

function LoginDialog() {
  const t = useT();
  const { account } = useServices();
  const bridge = useBridge();
  const login = useAppStore((s) => s.account.login);
  const error = useAccountStore((s) => s.error);
  const busy = useAccountStore((s) => s.busy);
  const failed = login?.state === 'failed';

  return (
    <Dialog title={t('login.title')} testId="login-dialog" onClose={() => void account.getState().cancelLogin()}>
      <div className={styles.stack}>
        {error && <ErrorNotice error={error} />}
        {login && !failed && (
          <>
            <span>{t('login.instructions')}</span>
            <div className={styles.code} data-testid="login-code">
              {login.userCode}
            </div>
            <div className={styles.waiting}>
              <span className={componentStyles.spinner} aria-hidden="true" />
              {t('login.waiting')}
            </div>
            <div>
              <Button size="small" onClick={() => void bridge.call('shell.openUrl', { url: login.verificationUrl }).catch(() => undefined)}>
                {t('login.reopen')} ↗
              </Button>
            </div>
          </>
        )}
        {failed && (
          <>
            <span className={styles.error}>{t('login.failed')}</span>
            {login?.error && <ErrorNotice error={login.error} />}
            <div>
              <Button variant="primary" busy={busy} onClick={() => void account.getState().startLogin()}>
                {t('login.retry')}
              </Button>
            </div>
          </>
        )}
        {!login && !error && <span className={styles.waiting}>{t('app.loading')}</span>}
      </div>
    </Dialog>
  );
}

function RedeemDialog() {
  const t = useT();
  const { account } = useServices();
  const [code, setCode] = useState('');
  const busy = useAccountStore((s) => s.busy);
  const outcome = useAccountStore((s) => s.redeemOutcome);
  const reason = useAccountStore((s) => s.redeemReason);
  const credits = useAppStore((s) => s.account.credits ?? 0);

  const submit = () => {
    if (code.trim()) {
      void account.getState().redeem(code);
    }
  };

  return (
    <Dialog
      title={t('redeem.title')}
      testId="redeem-dialog"
      onClose={() => account.getState().closeDialog()}
      footer={
        <>
          <Button onClick={() => account.getState().closeDialog()}>{t('action.close')}</Button>
          <Button variant="primary" busy={busy} disabled={!code.trim()} data-testid="redeem-submit" onClick={submit}>
            {t('redeem.submit')}
          </Button>
        </>
      }
    >
      <div className={styles.stack}>
        {reason && <span className={styles.notice}>{t(reason)}</span>}
        <span className={styles.notice}>{t('account.credits', { credits })}</span>
        <Field label={t('redeem.label')}>
          <input
            className={controlClass}
            data-testid="redeem-input"
            value={code}
            autoComplete="off"
            spellCheck={false}
            placeholder={t('redeem.placeholder')}
            onChange={(e) => setCode(formatRedeemCode(e.target.value))}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
        </Field>
        {outcome && (
          <span className={outcome.kind === 'success' ? styles.success : styles.error} data-testid="redeem-outcome" data-kind={outcome.kind}>
            {t(outcome.key, outcome.params)}
          </span>
        )}
      </div>
    </Dialog>
  );
}

function LedgerDialog() {
  const t = useT();
  const { account } = useServices();
  const language = useAppStore((s) => s.language);
  const items = useAccountStore((s) => s.ledger);
  const cursor = useAccountStore((s) => s.ledgerCursor);
  const busy = useAccountStore((s) => s.busy);
  const error = useAccountStore((s) => s.error);
  const formatter = new Intl.DateTimeFormat(language === 'zh' ? 'zh-CN' : 'en-US', { dateStyle: 'medium', timeStyle: 'short' });

  return (
    <Dialog title={t('ledger.title')} testId="ledger-dialog" onClose={() => account.getState().closeDialog()}>
      <div className={styles.stack}>
        {error && <ErrorNotice error={error} />}
        {!busy && !error && items.length === 0 && <span className={styles.notice}>{t('ledger.empty')}</span>}
        {items.length > 0 && (
          <table className={styles.ledger}>
            <tbody>
              {items.map((item) => (
                <tr key={item.id}>
                  <td>{formatter.format(new Date(item.createdAt))}</td>
                  <td>{t(`ledger.type.${item.type}` as MessageKey)}</td>
                  <td className={item.delta > 0 ? styles.plus : styles.minus}>{item.delta > 0 ? `+${item.delta}` : item.delta}</td>
                  <td>{item.balanceAfter}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {cursor && (
          <div>
            <Button size="small" busy={busy} onClick={() => void account.getState().loadMoreLedger()}>
              {t('ledger.more')}
            </Button>
          </div>
        )}
      </div>
    </Dialog>
  );
}

/** Whichever account dialog is open. */
export function AccountDialogs() {
  const dialog = useAccountStore((s) => s.dialog);
  switch (dialog) {
    case 'login':
      return <LoginDialog />;
    case 'redeem':
      return <RedeemDialog />;
    case 'ledger':
      return <LedgerDialog />;
    default:
      return null;
  }
}
