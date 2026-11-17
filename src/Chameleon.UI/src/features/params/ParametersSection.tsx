import { Field } from '../../components/Field';
import { Select } from '../../components/Select';
import { TextArea } from '../../components/TextArea';
import { localize, type MessageKey } from '../../i18n';
import { selectAspectRatios, selectImageSizes } from '../../state/appStore';
import { useAppStore, useParamsStore, useServices, useT } from '../../state/services';
import { Row, Section } from '../shell/Section';
import { pickAspectRatio, pickImageSize } from './paramsStore';

/** Style template, prompt, aspect ratio and size — the same controls in both modes. */
export function ParametersSection({ promptLabel }: { promptLabel: MessageKey }) {
  const t = useT();
  const { params } = useServices();
  const language = useAppStore((s) => s.language);
  const templates = useAppStore((s) => s.catalog?.styleTemplates ?? []);
  const ratios = useAppStore(selectAspectRatios);
  const sizes = useAppStore(selectImageSizes);
  const prompt = useParamsStore((s) => s.prompt);
  const styleId = useParamsStore((s) => s.styleId);
  const chosenRatio = useParamsStore((s) => s.aspectRatio);
  const chosenSize = useParamsStore((s) => s.imageSize);
  const defaultSize = useAppStore((s) => s.catalog?.defaultImageSize ?? '1K');

  // What is shown is what will be sent: a choice the current model does not offer falls back.
  const shownRatio = pickAspectRatio(chosenRatio, ratios);
  const shownSize = pickImageSize(chosenSize, sizes, defaultSize);

  return (
    <Section title={t('section.parameters')} testId="parameters-section">
      <Field label={t('label.styleTemplate')}>
        <Select
          data-testid="style-select"
          value={styleId}
          options={templates.map((tpl) => ({ value: tpl.id, label: localize(tpl.name, language) }))}
          onChange={(id) => {
            const template = templates.find((tpl) => tpl.id === id);
            if (template) {
              params.getState().applyStyle(template);
            }
          }}
        />
      </Field>
      <Field label={t(promptLabel)}>
        <TextArea
          data-testid="prompt-input"
          rows={6}
          value={prompt}
          placeholder={t('placeholder.prompt')}
          onChange={(event) => params.getState().setPrompt(event.target.value)}
        />
      </Field>
      <Row>
        <Field label={t('label.aspectRatio')}>
          <Select
            data-testid="aspect-select"
            value={shownRatio}
            options={[
              { value: '', label: t('aspect.original') },
              ...ratios.map((r) => ({ value: r, label: r })),
            ]}
            onChange={(value) => params.getState().setAspectRatio(value)}
          />
        </Field>
        <Field label={t('label.imageSize')}>
          <Select
            data-testid="size-select"
            value={shownSize}
            options={sizes.map((size) => ({ value: size, label: size }))}
            onChange={(value) => params.getState().setImageSize(value)}
          />
        </Field>
      </Row>
    </Section>
  );
}
