import { describe, expect, it } from 'vitest';
import { helpLinkFor, HELP_PAGES } from './helpLinks';

describe('helpLinkFor', () => {
  it('points connection problems to the network page, in the plug-in language', () => {
    expect(helpLinkFor('network_blocked', 'zh')).toBe('https://rhchameleon.top/docs/network?lang=zh');
    expect(helpLinkFor('network', 'en')).toBe(`${HELP_PAGES.network}?lang=en`);
  });

  it('has no page for other errors, including inherited object keys', () => {
    expect(helpLinkFor('auth_failed', 'zh')).toBeUndefined();
    expect(helpLinkFor('toString', 'en')).toBeUndefined();
  });
});
