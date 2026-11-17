import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { Bridge } from './bridge/Bridge';
import { HttpTransport, resolveBridgeBaseUrl } from './bridge/httpTransport';
import { installPageGuards } from './features/shell/installPageGuards';
import { normalizeLanguage } from './i18n';
import { createServices, ServicesProvider } from './state/services';
import './styles/global.css';

const bridge = new Bridge(new HttpTransport({ baseUrl: resolveBridgeBaseUrl() }));
const services = createServices(bridge, normalizeLanguage(navigator.language));

if (import.meta.env.PROD) {
  installPageGuards();
}
void services.app.getState().load();

createRoot(document.getElementById('root') as HTMLElement).render(
  <StrictMode>
    <ServicesProvider services={services}>
      <App />
    </ServicesProvider>
  </StrictMode>,
);
