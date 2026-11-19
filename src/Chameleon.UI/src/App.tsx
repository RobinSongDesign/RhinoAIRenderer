import { AccountDialogs } from './features/account/AccountDialogs';
import { useBatchView } from './features/batch/useBatchView';
import { RelaySignInDialog } from './features/relaySignIn/RelaySignInDialog';
import { SettingsDialog } from './features/settings/SettingsDialog';
import { AppShell, type ModeView } from './features/shell/AppShell';
import { useSingleView } from './features/single/useSingleView';
import type { Mode } from './state/uiStore';

/** Root component: assembles the mode views and dialogs inside the shell. */
export function App() {
  const views: Record<Mode, ModeView> = { single: useSingleView(), batch: useBatchView() };
  return (
    <AppShell
      views={views}
      dialogs={
        <>
          <SettingsDialog />
          <AccountDialogs />
          <RelaySignInDialog />
        </>
      }
    />
  );
}
