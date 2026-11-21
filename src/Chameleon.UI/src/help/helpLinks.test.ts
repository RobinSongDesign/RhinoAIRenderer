import { describe, expect, it } from 'vitest';
import { helpLinkFor, HELP_PAGES } from './helpLinks';

describe('helpLinkFor', () => {
  it('points connection problems to the network page of the website', () => {
    expect(helpLinkFor('network_blocked')).toBe('https://rhchameleon.top/docs/network');
    expect(helpLinkFor('network')).toBe(HELP_PAGES.network);
  });

  it('has no page for other errors, including inherited object keys', () => {
    expect(helpLinkFor('auth_failed')).toBeUndefined();
    expect(helpLinkFor('toString')).toBeUndefined();
  });
});
