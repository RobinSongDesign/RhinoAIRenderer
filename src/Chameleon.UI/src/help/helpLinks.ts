import type { ErrorCode } from '../bridge/types';

/** The product website; its documentation explains how to fix problems on the user's computer. */
export const WEBSITE_URL = 'https://rhchameleon.top';

/** Pages of the documentation the plug-in links to. */
export const HELP_PAGES = {
  network: `${WEBSITE_URL}/docs/network`,
} as const;

// Errors the user fixes on their own computer, with the page that explains how.
const HELP_BY_ERROR: Partial<Record<ErrorCode, string>> = {
  network_blocked: HELP_PAGES.network,
  network: HELP_PAGES.network,
};

/** The help page for an error code, if the documentation has one. */
export function helpLinkFor(code: string): string | undefined {
  return Object.prototype.hasOwnProperty.call(HELP_BY_ERROR, code) ? HELP_BY_ERROR[code as ErrorCode] : undefined;
}
