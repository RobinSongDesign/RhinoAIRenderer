import { AppShell, type ModeView } from './features/shell/AppShell';
import { useSingleView } from './features/single/useSingleView';
import { useT } from './state/services';
import type { Mode } from './state/uiStore';

/** Root component: assembles the mode views and dialogs inside the shell. */
export function App() {
  const t = useT();
  const single = useSingleView();
  const batch: ModeView = {
    workspace: <div>{t('mode.batch')}</div>,
    panel: null,
    footer: null,
    status: null,
  };
  const views: Record<Mode, ModeView> = { single, batch };
  return <AppShell views={views} />;
}
