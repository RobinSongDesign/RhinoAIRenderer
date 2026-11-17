import { useState } from 'react';
import { Button } from '../../components/Button';
import { ImageView } from '../../components/ImageView';
import { Select } from '../../components/Select';
import type { OutputFormat } from '../../imaging/imaging';
import { useServices, useSingleStore, useT } from '../../state/services';
import { CostHint } from '../account/CostHint';
import { useCostEstimate } from '../account/useCostEstimate';
import { WelcomeCard } from '../account/WelcomeCard';
import { CanvasView } from '../canvas/CanvasView';
import { BackendSection } from '../params/BackendSection';
import { ParametersSection } from '../params/ParametersSection';
import { ReferenceSection } from '../params/ReferenceSection';
import { Section } from '../shell/Section';
import type { ModeView } from '../shell/AppShell';
import styles from './single.module.css';

/** The canvas plus the source/result thumbnails that choose what it shows. */
function SingleCanvas() {
  const t = useT();
  const source = useSingleStore((s) => s.source);
  const result = useSingleStore((s) => s.result);
  const capturing = useSingleStore((s) => s.capturing);
  const generating = useSingleStore((s) => s.activeJobId !== null);
  // Remounted per result (see SingleWorkspace), so a new result is always shown first.
  const [showSource, setShowSource] = useState(false);
  const shown = showSource || !result ? source : result;

  return (
    <div className={styles.workspace}>
      <CanvasView
        testId="canvas-view"
        image={shown}
        compareWith={shown === result ? source : null}
        placeholder={t('placeholder.source')}
        busyText={capturing ? t('status.capturing') : generating ? t('status.generating') : null}
      />
      <div className={styles.thumbnails}>
        <figure className={styles.thumbnail}>
          <ImageView
            testId="source-view"
            asset={source}
            alt={t('panel.source')}
            placeholder="—"
            selected={source !== null && shown === source}
            onSelect={() => setShowSource(true)}
          />
          <figcaption>{t('panel.source')}</figcaption>
        </figure>
        <figure className={styles.thumbnail}>
          <ImageView
            testId="result-view"
            asset={result}
            alt={t('panel.result')}
            placeholder="—"
            selected={result !== null && shown === result}
            onSelect={() => setShowSource(false)}
          />
          <figcaption>{t('panel.result')}</figcaption>
        </figure>
      </div>
    </div>
  );
}

function SingleWorkspace() {
  const resultId = useSingleStore((s) => s.result?.assetId ?? '');
  return <SingleCanvas key={resultId} />;
}

function SinglePanel() {
  const t = useT();
  const { single } = useServices();
  const capturing = useSingleStore((s) => s.capturing);
  const generating = useSingleStore((s) => s.activeJobId !== null);

  return (
    <>
      <WelcomeCard />
      <Section title={t('section.capture')}>
        <Button
          variant="primary"
          block
          data-testid="capture-button"
          busy={capturing}
          disabled={generating}
          onClick={() => void single.getState().capture()}
        >
          {t('action.captureActiveView')}
        </Button>
      </Section>
      <BackendSection />
      <ParametersSection promptLabel="label.prompt" />
      <ReferenceSection onStatus={(message) => single.getState().setStatus(message)} />
    </>
  );
}

function SingleFooter() {
  const t = useT();
  const { single } = useServices();
  const [format, setFormat] = useState<OutputFormat>('png');
  const source = useSingleStore((s) => s.source);
  const result = useSingleStore((s) => s.result);
  const generating = useSingleStore((s) => s.activeJobId !== null);
  const saving = useSingleStore((s) => s.saving);
  const cost = useCostEstimate(1);

  return (
    <>
      <CostHint estimate={cost} />
      {generating ? (
        <Button
          block
          variant="danger"
          data-testid="cancel-button"
          onClick={() => void single.getState().cancel()}
        >
          {t('action.cancel')}
        </Button>
      ) : (
        <Button
          block
          variant="primary"
          data-testid="generate-button"
          disabled={!source || cost?.short === true}
          onClick={() => void single.getState().generate()}
        >
          {t('action.generate')}
        </Button>
      )}
      <div className={styles.footerRow}>
        <Button
          data-testid="save-button"
          disabled={!result}
          busy={saving}
          onClick={() => void single.getState().save(format)}
        >
          {t('action.saveResult')}
        </Button>
        <Select<OutputFormat>
          className={styles.formatSelect}
          aria-label={t('label.format')}
          data-testid="format-select"
          value={format}
          options={[
            { value: 'png', label: 'PNG' },
            { value: 'jpg', label: 'JPEG' },
          ]}
          onChange={setFormat}
        />
      </div>
      <div className={styles.footerRow}>
        <Button
          data-testid="use-as-source-button"
          disabled={!result || generating}
          onClick={() => single.getState().useResultAsSource()}
        >
          {t('action.useAsSource')}
        </Button>
        <Button data-testid="clear-button" disabled={generating} onClick={() => single.getState().clear()}>
          {t('action.clearAll')}
        </Button>
      </div>
    </>
  );
}

/** Content of the shell's areas in single-image mode. */
export function useSingleView(): ModeView {
  const status = useSingleStore((s) => s.status);
  return {
    workspace: <SingleWorkspace />,
    panel: <SinglePanel />,
    footer: <SingleFooter />,
    status,
  };
}
