import { useState } from 'react';
import { Button } from '../../components/Button';
import { ImageView } from '../../components/ImageView';
import { Select } from '../../components/Select';
import type { OutputFormat } from '../../imaging/imaging';
import { useServices, useSingleStore, useT } from '../../state/services';
import { BackendSection } from '../params/BackendSection';
import { ParametersSection } from '../params/ParametersSection';
import { ReferenceSection } from '../params/ReferenceSection';
import { Section } from '../shell/Section';
import type { ModeView } from '../shell/AppShell';
import styles from './single.module.css';

function SingleWorkspace() {
  const t = useT();
  const source = useSingleStore((s) => s.source);
  const result = useSingleStore((s) => s.result);
  const capturing = useSingleStore((s) => s.capturing);
  const generating = useSingleStore((s) => s.activeJobId !== null);

  return (
    <div className={styles.workspace}>
      <h2>{t('panel.source')}</h2>
      <ImageView
        testId="source-view"
        asset={source}
        alt={t('panel.source')}
        placeholder={t('placeholder.source')}
        busyText={capturing ? t('status.capturing') : null}
      />
      <h2>{t('panel.result')}</h2>
      <ImageView
        testId="result-view"
        asset={result}
        alt={t('panel.result')}
        placeholder={t('placeholder.result')}
        busyText={generating ? t('status.generating') : null}
      />
    </div>
  );
}

function SinglePanel() {
  const t = useT();
  const { single } = useServices();
  const capturing = useSingleStore((s) => s.capturing);
  const generating = useSingleStore((s) => s.activeJobId !== null);

  return (
    <>
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

  return (
    <>
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
          disabled={!source}
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
