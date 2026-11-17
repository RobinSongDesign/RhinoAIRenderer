import { describe, expect, it } from 'vitest';
import {
  actualSize,
  constrain,
  containRect,
  fit,
  imageToView,
  KEEP_VISIBLE,
  pixelAt,
  scaleLimits,
  smoothingEnabled,
  viewToImage,
  wheelZoomFactor,
  zoomAt,
  zoomPercent,
} from './viewport';

const image = { width: 2000, height: 1000 };
const view = { width: 816, height: 616 };

describe('viewport', () => {
  it('fits and centres an image, leaving padding', () => {
    const vp = fit(image, view);
    expect(vp.scale).toBeCloseTo(0.4);
    expect(vp.offsetX).toBeCloseTo(8);
    expect(vp.offsetY).toBeCloseTo((616 - 400) / 2);
  });

  it('never enlarges a small image beyond one device pixel per image pixel', () => {
    expect(fit({ width: 100, height: 50 }, view).scale).toBe(1);
    expect(fit({ width: 100, height: 50 }, view, 2).scale).toBe(0.5);
  });

  it('shows 100% as one image pixel per device pixel', () => {
    expect(actualSize(image, view, 2).scale).toBe(0.5);
    expect(zoomPercent(actualSize(image, view, 2), 2)).toBe(100);
  });

  it('keeps the image point under the pointer in place when zooming', () => {
    const vp = fit(image, view);
    const at = { x: 300, y: 200 };
    const before = viewToImage(vp, at);
    const zoomed = zoomAt(vp, 2.5, at, { min: 0.01, max: 100 });

    expect(zoomed.scale).toBeCloseTo(1);
    const after = imageToView(zoomed, before);
    expect(after.x).toBeCloseTo(at.x);
    expect(after.y).toBeCloseTo(at.y);
  });

  it('clamps zoom to the limits, still anchored at the pointer', () => {
    const limits = scaleLimits(image, view);
    const vp = zoomAt(fit(image, view), 1e6, { x: 10, y: 10 }, limits);
    expect(vp.scale).toBe(limits.max);
    expect(zoomAt(vp, 1e-9, { x: 10, y: 10 }, limits).scale).toBe(limits.min);
  });

  it('keeps a small image inside the view and part of a large one in view', () => {
    const small = constrain({ scale: 0.1, offsetX: -500, offsetY: 9000 }, image, view);
    expect(small.offsetX).toBe(0);
    expect(small.offsetY).toBe(616 - 100);

    const large = constrain({ scale: 2, offsetX: 5000, offsetY: -99999 }, image, view);
    expect(large.offsetX).toBe(816 - KEEP_VISIBLE);
    expect(large.offsetY).toBe(KEEP_VISIBLE - 2000);
  });

  it('reports the pixel under the pointer, or nothing outside the image', () => {
    const vp = { scale: 2, offsetX: 10, offsetY: 20 };
    expect(pixelAt(vp, image, { x: 10, y: 20 })).toEqual({ x: 0, y: 0 });
    expect(pixelAt(vp, image, { x: 15.9, y: 23.9 })).toEqual({ x: 2, y: 1 });
    expect(pixelAt(vp, image, { x: 9, y: 20 })).toBeNull();
    expect(pixelAt(vp, image, { x: 10 + 4000, y: 20 })).toBeNull();
  });

  it('turns smoothing off when zoomed far in', () => {
    expect(smoothingEnabled({ scale: 4, offsetX: 0, offsetY: 0 })).toBe(true);
    expect(smoothingEnabled({ scale: 2.5, offsetX: 0, offsetY: 0 }, 2)).toBe(false);
  });

  it('lines up an image of another size inside a rectangle', () => {
    expect(containRect({ width: 100, height: 100 }, { x: 0, y: 0, width: 200, height: 100 })).toEqual({
      x: 50,
      y: 0,
      width: 100,
      height: 100,
    });
    expect(containRect({ width: 400, height: 200 }, { x: 10, y: 10, width: 200, height: 100 })).toEqual({
      x: 10,
      y: 10,
      width: 200,
      height: 100,
    });
  });

  it('turns wheel deltas into symmetric zoom factors', () => {
    expect(wheelZoomFactor(-100)).toBeGreaterThan(1.15);
    expect(wheelZoomFactor(-100) * wheelZoomFactor(100)).toBeCloseTo(1);
    expect(wheelZoomFactor(-1e6)).toBe(wheelZoomFactor(-300));
  });
});
