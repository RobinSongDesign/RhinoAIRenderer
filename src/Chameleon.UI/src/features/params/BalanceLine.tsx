import { useEffect, useState } from 'react';
import type { BackendInfo, ProviderBalance } from '../../bridge/types';
import { useAppStore, useBridge, useJobsStore, useServices, useT } from '../../state/services';
import { balanceText } from './balanceText';
import styles from './params.module.css';

const DIRECT_PREFIX = 'direct:';

/** Provider id of a direct backend ("direct:{providerId}"); empty for other backends. */
export function providerIdOf(backendId: string): string {
  return backendId.startsWith(DIRECT_PREFIX) ? backendId.slice(DIRECT_PREFIX.length) : '';
}


/**
 * For relays that report it: what is left on the key, refreshed when the backend changes and
 * after every finished generation, with a link to the provider's top-up page. A signed-in relay
 * account also shows its name and a way to sign out.
 */
export function BalanceLine({ backend }: { backend: BackendInfo }) {
  const t = useT();
  const bridge = useBridge();
  const { relaySignIn } = useServices();
  const language = useAppStore((s) => s.language);
  // Generations change the balance; counting succeeded jobs is a cheap signal to refresh.
  const finished = useJobsStore((s) => Object.values(s.jobs).filter((j) => j.state === 'succeeded').length);
  const [refreshes, setRefreshes] = useState(0);
  const [result, setResult] = useState<{ key: string; balance: ProviderBalance | null } | null>(null);
  const providerId = providerIdOf(backend.id);
  const enabled = backend.kind === 'direct' && backend.supportsBalance === true && backend.ready && providerId !== '';
  const key = `${providerId}|${finished}|${refreshes}`;

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }
    let current = true;
    bridge
      .call('providers.balance', { id: providerId })
      .then((balance) => current && setResult({ key, balance }))
      .catch(() => current && setResult({ key, balance: null }));
    return () => {
      current = false;
    };
  }, [bridge, enabled, providerId, key]);

  const topUpUrl = backend.topUpUrl;
  const account = backend.ready ? backend.signedInAs : null;
  if (!enabled && !topUpUrl && !account) {
    return null;
  }

  const text = enabled && result ? balanceText(result.balance, t, language) : null;

  return (
    <div className={styles.balance} data-testid="balance-line">
      {account && (
        <>
          <span data-testid="signed-in-as">{t('signIn.account', { name: account })}</span>
          <button
            type="button"
            className={styles.linkButton}
            data-testid="signin-signout"
            onClick={() => void relaySignIn.getState().signOut(providerId)}
          >
            {t('signIn.signOut')}
          </button>
        </>
      )}
      {text && <span data-testid="balance-text">{text}</span>}
      {enabled && (
        <button
          type="button"
          className={styles.linkButton}
          title={t('balance.refresh')}
          aria-label={t('balance.refresh')}
          data-testid="balance-refresh"
          onClick={() => setRefreshes((n) => n + 1)}
        >
          ↻
        </button>
      )}
      {topUpUrl && (
        <button
          type="button"
          className={styles.linkButton}
          data-testid="balance-topup"
          onClick={() => void bridge.call('shell.openUrl', { url: topUpUrl }).catch(() => undefined)}
        >
          {t('balance.topUp')} ↗
        </button>
      )}
    </div>
  );
}
