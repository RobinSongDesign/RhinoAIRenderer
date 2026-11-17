import { AppShell, type ModeView } from './features/shell/AppShell';
import { useT } from './state/services';
import type { Mode } from './state/uiStore';

/** Root component: assembles the mode views and dialogs inside the shell. */
export function App() {
  const t = useT();
  const placeholder = (text: string): ModeView => ({
    workspace: <div>{text}</div>,
    panel: null,
    footer: null,
    status: null,
  });
  const views: Record<Mode, ModeView> = {
    single: placeholder(t('mode.single')),
    batch: placeholder(t('mode.batch')),
  };
  return <AppShell views={views} />;
}
