import { describe, expect, it } from 'vitest';
import { defaultResultName, isAcceptedImageFile, matchesFormat, mimeTypeOf, needsReencode } from './imaging';

describe('imaging helpers', () => {
  it('know which formats the host accepts', () => {
    expect(needsReencode('image/png')).toBe(false);
    expect(needsReencode('IMAGE/JPEG')).toBe(false);
    expect(needsReencode('image/webp')).toBe(false);
    expect(needsReencode('image/bmp')).toBe(true);
    expect(needsReencode('')).toBe(true);
  });

  it('accept image files by type or extension', () => {
    expect(isAcceptedImageFile({ name: 'a.bin', type: 'image/bmp' })).toBe(true);
    expect(isAcceptedImageFile({ name: 'photo.JPG', type: '' })).toBe(true);
    expect(isAcceptedImageFile({ name: 'notes.txt', type: 'text/plain' })).toBe(false);
  });

  it('map output formats', () => {
    expect(mimeTypeOf('jpg')).toBe('image/jpeg');
    expect(mimeTypeOf('png')).toBe('image/png');
    expect(matchesFormat('image/png', 'png')).toBe(true);
    expect(matchesFormat('image/webp', 'png')).toBe(false);
  });

  it('build the legacy default file name', () => {
    expect(defaultResultName(new Date(2026, 0, 2, 3, 4, 5))).toBe('AIRender_20260102_030405');
  });
});
