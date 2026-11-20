import { useState, type FormEvent } from 'react';
import { Button } from '../../components/Button';
import { Dialog } from '../../components/Dialog';
import { ErrorNotice } from '../../components/ErrorNotice';
import { controlClass, Field } from '../../components/Field';
import { useRelaySignInStore, useServices, useT } from '../../state/services';
import styles from './relaySignIn.module.css';

/** Username and password, then a two-factor code when the account has one. */
export function RelaySignInDialog() {
  const t = useT();
  const { relaySignIn } = useServices();
  const target = useRelaySignInStore((s) => s.target);
  const step = useRelaySignInStore((s) => s.step);
  const busy = useRelaySignInStore((s) => s.busy);
  const error = useRelaySignInStore((s) => s.error);
  // Typed values stay in the component: the password must not outlive the dialog.
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  if (!target) {
    return null;
  }
  const store = relaySignIn.getState();

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (step === 'credentials') {
      void store.signIn(username, password).then((done) => done && setPassword(''));
    } else {
      void store.verify(code).then((done) => done && setCode(''));
    }
  };
  const close = () => {
    setPassword('');
    setCode('');
    store.close();
  };

  return (
    <Dialog title={t('signIn.title', { name: target.displayName })} testId="relay-signin-dialog" onClose={close}>
      <form className={styles.form} onSubmit={submit}>
        {error &&
          (error.code === 'rate_limited' ? (
            // Gateways limit sign-in attempts per address; retrying at once only extends the lock.
            <div className={styles.error} data-testid="signin-error" role="alert">
              {t('signIn.tooMany')}
            </div>
          ) : error.code === 'auth_failed' ? (
            // The gateway's own words ("wrong password", "account banned") say why; they come in its language.
            <div className={styles.error} data-testid="signin-error" role="alert">
              <div>{t('signIn.failed')}</div>
              {error.message && <div className={styles.hint}>{error.message}</div>}
            </div>
          ) : (
            <ErrorNotice error={error} />
          ))}
        {step === 'credentials' ? (
          <>
            <Field label={t('signIn.username')}>
              <input
                className={controlClass}
                data-testid="signin-username"
                autoComplete="username"
                autoFocus
                value={username}
                onChange={(e) => setUsername(e.target.value)}
              />
            </Field>
            <Field label={t('signIn.password')}>
              <input
                type="password"
                className={controlClass}
                data-testid="signin-password"
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </Field>
            <span className={styles.hint}>{t('signIn.privacy')}</span>
            <div className={styles.actions}>
              <Button
                type="submit"
                variant="primary"
                data-testid="signin-submit"
                busy={busy}
                disabled={!username.trim() || !password}
              >
                {t('signIn.submit')}
              </Button>
              <Button type="button" size="small" variant="ghost" data-testid="signin-register" onClick={() => void store.openPage('register')}>
                {t('signIn.register')} ↗
              </Button>
              <Button type="button" size="small" variant="ghost" data-testid="signin-forgot" onClick={() => void store.openPage('forgotPassword')}>
                {t('signIn.forgot')} ↗
              </Button>
            </div>
          </>
        ) : (
          <>
            <Field label={t('signIn.code')} hint={t('signIn.codeHint')}>
              <input
                className={controlClass}
                data-testid="signin-code"
                autoComplete="one-time-code"
                inputMode="numeric"
                autoFocus
                value={code}
                onChange={(e) => setCode(e.target.value)}
              />
            </Field>
            <div className={styles.actions}>
              <Button type="submit" variant="primary" data-testid="signin-verify" busy={busy} disabled={!code.trim()}>
                {t('signIn.verify')}
              </Button>
            </div>
          </>
        )}
      </form>
    </Dialog>
  );
}
