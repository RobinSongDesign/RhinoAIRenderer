import { useState } from 'react';
import { Button } from '../../components/Button';
import { Select } from '../../components/Select';
import type { OutputFormat } from '../../imaging/imaging';
import { useServices, useSessionStore, useSingleStore, useT } from '../../state/services';
import { CostHint } from '../account/CostHint';
import { useCostEstimate } from '../account/useCostEstimate';
import { WelcomeCard } from '../account/WelcomeCard';
import { CanvasView } from '../canvas/CanvasView';
import { HistoryPanel } from '../history/HistoryPanel';
import { selectCurrentNode, selectParentNode } from '../history/sessionStore';
import { BackendSection } from '../params/BackendSection';
import { ParametersSection } from '../params/ParametersSection';
import { ReferenceSection } from '../params/ReferenceSection';
import { Section } from '../shell/Section';
import type { ModeView } from '../shell/AppShell';
import styles from './single.module.css';

/** History on the left, the selected image on the canvas, compared with its parent. */
function SingleWorkspace() {
  const t = useT();
  const current = useSessionStore(selectCurrentNode);
  const parent = useSessionStore(selectParentNode);
  const capturing = useSingleStore((s) => s.capturing);
  const generating = useSingleStore((s) => s.activeJobId !== null);

  return (
    <div className={styles.workspace}>
      <HistoryPanel />
      <CanvasView
        testId="canvas-view"
        image={current?.asset ?? null}
        compareWith={parent?.asset ?? null}
        placeholder={t('placeholder.source')}
        busyText={capturing ? t('status.capturing') : generating ? t('status.generating') : null}
      />
    </div>
  );
}

/** How the selected image was made, with a shortcut to reuse its settings. */
function RecipeCard() {
  const t = useT();
  const { single } = useServices();
  const current = useSessionStore(selectCurrentNode);
  if (!current?.recipe) {
    return null;
  }
  return (
    <div className={styles.recipe} data-testid="recipe-card">
      <span className={styles.recipeTitle}>{t('history.recipe')}</span>
      <span className={styles.recipePrompt}>{current.recipe.prompt || '—'}</span>
      <span className={styles.recipeMeta}>
        {current.recipe.modelId} · {current.recipe.imageSize}
        {current.recipe.aspectRatio ? ` · ${current.recipe.aspectRatio}` : ''}
      </span>
      <Button size="small" data-testid="recipe-apply" onClick={() => void single.getState().applyRecipe(current.id)}>
        {t('history.applyRecipe')}
      </Button>
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
      <RecipeCard />
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
  const current = useSessionStore(selectCurrentNode);
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
          disabled={!current || cost?.short === true}
          onClick={() => void single.getState().generate()}
        >
          {t('action.generate')}
        </Button>
      )}
      <div className={styles.footerRow}>
        <Button
          data-testid="save-button"
          disabled={!current}
          busy={saving}
          onClick={() => void single.getState().save(format)}
        >
          {t('action.saveImage')}
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
