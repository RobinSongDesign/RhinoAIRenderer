import { Button } from '../../components/Button';
import { useAppStore, useServices, useT } from '../../state/services';
import styles from './account.module.css';
import type { CostEstimate } from './useCostEstimate';

/** Shows what a generation costs in account mode, with a shortcut to redeem when the balance is short. */
export function CostHint({ estimate }: { estimate: CostEstimate | null }) {
  const t = useT();
  const { account } = useServices();
  const balance = useAppStore((s) => s.account.credits ?? 0);

  if (!estimate) {
    return null;
  }
  return (
    <div className={`${styles.cost} ${estimate.short ? styles.costShort : ''}`} data-testid="cost-hint" data-short={estimate.short}>
      <span>
        {estimate.short ? t('cost.insufficient', { credits: balance }) : t('cost.estimate', { credits: estimate.credits })}
      </span>
      {estimate.short && (
        <Button size="small" data-testid="cost-top-up" onClick={() => account.getState().openRedeem('redeem.insufficient')}>
          {t('cost.topUp')}
        </Button>
      )}
    </div>
  );
}
