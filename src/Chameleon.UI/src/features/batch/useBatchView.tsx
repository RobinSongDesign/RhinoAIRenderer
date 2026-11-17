import { Button } from '../../components/Button';
import { ErrorNotice } from '../../components/ErrorNotice';
import { ImageView } from '../../components/ImageView';
import { TextArea } from '../../components/TextArea';
import type { MessageKey } from '../../i18n';
import { useAppStore, useBatchStore, useJobsStore, useServices, useT } from '../../state/services';
import { BackendSection } from '../params/BackendSection';
import { ParametersSection } from '../params/ParametersSection';
import { ReferenceSection } from '../params/ReferenceSection';
import { Row, Section } from '../shell/Section';
import type { ModeView } from '../shell/AppShell';
import styles from './batch.module.css';
import { isRunning, itemStatusKey, runProgress, type BatchItem } from './batchStore';

const stateOfKey: Partial<Record<MessageKey, string>> = {
  'item.done': 'done',
  'item.failed': 'failed',
  'item.captureFailed': 'failed',
};

function ItemRow({ item, running }: { item: BatchItem; running: boolean }) {
  const t = useT();
  const { batch } = useServices();
  const job = useJobsStore((s) => (item.jobId ? s.jobs[item.jobId] : undefined));
  const statusKey = itemStatusKey(item, job);
  const busy = job?.state === 'queued' || job?.state === 'running';

  return (
    <div
      className={styles.row}
      data-testid="batch-item"
      data-view={item.viewName}
      data-selected={item.selected}
    >
      <label className={styles.check}>
        <input
          type="checkbox"
          data-testid="batch-item-check"
          checked={item.selected}
          disabled={running}
          onChange={(event) => batch.getState().setSelected(item.viewName, event.target.checked)}
        />
      </label>
      <div className={styles.thumb}>
        <ImageView
          testId="batch-item-source"
          asset={item.source}
          alt={item.viewName}
          placeholder={t('placeholder.notCaptured')}
          busyText={item.capturing ? t('item.capturing') : null}
        />
      </div>
      <div className={styles.thumb}>
        <ImageView
          testId="batch-item-result"
          asset={item.result}
          alt={item.viewName}
          placeholder={t('placeholder.result')}
          busyText={busy ? t(statusKey) : null}
        />
      </div>
      <div className={styles.side}>
        <div className={styles.name} title={item.viewName}>
          {item.viewName}
        </div>
        <div
          className={styles.itemStatus}
          data-testid="batch-item-status"
          data-state={stateOfKey[statusKey] ?? 'other'}
        >
          {t(statusKey)}
        </div>
        {item.error && <ErrorNotice error={item.error} />}
        <TextArea
          data-testid="batch-item-addon"
          rows={2}
          value={item.addonPrompt}
          placeholder={t('placeholder.addonPrompt')}
          onChange={(event) => batch.getState().setAddonPrompt(item.viewName, event.target.value)}
        />
        <div className={styles.actions}>
          <Button
            size="small"
            data-testid="batch-item-recapture"
            disabled={busy || item.capturing}
            onClick={() => void batch.getState().recapture(item.viewName)}
          >
            {t('action.recapture')}
          </Button>
          <Button
            size="small"
            data-testid="batch-item-regenerate"
            disabled={busy || !item.source}
            onClick={() => void batch.getState().regenerate(item.viewName)}
          >
            {t('action.regenerate')}
          </Button>
          <Button
            size="small"
            data-testid="batch-item-save"
            disabled={!item.result}
            onClick={() => void batch.getState().saveItem(item.viewName)}
          >
            {t('action.save')}
          </Button>
        </div>
      </div>
    </div>
  );
}

function BatchWorkspace() {
  const t = useT();
  const items = useBatchStore((s) => s.items);
  const running = useJobsStore((jobs) => isRunning(items, jobs.jobs));

  if (items.length === 0) {
    return <div className={styles.empty}>{t('placeholder.noViews')}</div>;
  }
  return (
    <div className={styles.list}>
      {items.map((item) => (
        <ItemRow key={item.viewName} item={item} running={running} />
      ))}
    </div>
  );
}

function BatchPanel() {
  const t = useT();
  const { batch } = useServices();
  const loading = useBatchStore((s) => s.loading);
  const hasItems = useBatchStore((s) => s.items.length > 0);
  const mode = useAppStore((s) => s.settings?.batch.mode ?? 'serial');

  return (
    <>
      <Section title={t('section.batchViews')}>
        <Button
          variant="primary"
          block
          busy={loading}
          data-testid="load-views-button"
          onClick={() => void batch.getState().loadViews()}
        >
          {t('action.loadNamedViews')}
        </Button>
        <Row>
          <Button
            size="small"
            disabled={!hasItems}
            data-testid="select-all"
            onClick={() => batch.getState().selectAll(true)}
          >
            {t('action.selectAll')}
          </Button>
          <Button
            size="small"
            disabled={!hasItems}
            data-testid="deselect-all"
            onClick={() => batch.getState().selectAll(false)}
          >
            {t('action.deselectAll')}
          </Button>
        </Row>
        <span className={styles.hint}>{t(`settings.batchMode.${mode}` as MessageKey)}</span>
      </Section>
      <BackendSection />
      <ParametersSection promptLabel="label.promptShared" />
      <ReferenceSection onStatus={(message) => batch.getState().setStatus(message)} />
    </>
  );
}

function BatchFooter() {
  const t = useT();
  const { batch } = useServices();
  const items = useBatchStore((s) => s.items);
  const saving = useBatchStore((s) => s.saving);
  const running = useJobsStore((jobs) => isRunning(items, jobs.jobs));
  const canRender = items.some((i) => i.selected && i.source);

  return (
    <>
      {running ? (
        <Button
          block
          variant="danger"
          data-testid="cancel-batch-button"
          onClick={() => void batch.getState().cancel()}
        >
          {t('action.cancelBatch')}
        </Button>
      ) : (
        <Button
          block
          variant="primary"
          data-testid="start-batch-button"
          disabled={!canRender}
          onClick={() => void batch.getState().render()}
        >
          {t('action.startBatch')}
        </Button>
      )}
      <Button
        block
        data-testid="save-all-button"
        busy={saving}
        disabled={running || !items.some((i) => i.result)}
        onClick={() => void batch.getState().saveAll()}
      >
        {t('action.saveAll')}
      </Button>
    </>
  );
}

/** Content of the shell's areas in batch mode. */
export function useBatchView(): ModeView {
  const t = useT();
  const message = useBatchStore((s) => s.status);
  const runJobIds = useBatchStore((s) => s.runJobIds);
  const done = useJobsStore((jobs) => runProgress(runJobIds, jobs.jobs).done);
  const total = runJobIds.length;

  return {
    workspace: <BatchWorkspace />,
    panel: <BatchPanel />,
    footer: <BatchFooter />,
    status: message,
    statusExtra: total > 0 ? t('batch.progress', { done, total }) : undefined,
  };
}
