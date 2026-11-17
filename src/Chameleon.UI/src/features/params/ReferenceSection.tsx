import { DropZone } from '../../components/DropZone';
import type { StatusMessage } from '../../state/status';
import { useParamsStore, useServices, useT } from '../../state/services';
import { Section } from '../shell/Section';

/** The optional style reference image, sent with every request of either mode. */
export function ReferenceSection({ onStatus }: { onStatus(message: StatusMessage | null): void }) {
  const t = useT();
  const { params } = useServices();
  const reference = useParamsStore((s) => s.reference);
  const busy = useParamsStore((s) => s.referenceBusy);

  const report = (message: StatusMessage | null) => {
    if (message) {
      onStatus(message);
    }
  };

  return (
    <Section title={t('section.reference')} testId="reference-section">
      <DropZone
        testId="reference-drop"
        asset={reference}
        busy={busy}
        onFile={(file) => void params.getState().uploadReference(file).then(report)}
        onPick={() => void params.getState().pickReference().then(report)}
        onClear={() => params.getState().setReference(null)}
      />
    </Section>
  );
}
