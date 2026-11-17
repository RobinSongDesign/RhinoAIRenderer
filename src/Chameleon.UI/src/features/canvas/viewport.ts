/**
 * Viewport math for the image canvas, free of the DOM so it can be unit tested.
 *
 * A viewport maps image pixels to view (CSS) pixels: `view = image * scale + offset`.
 */

export interface Size {
  width: number;
  height: number;
}

export interface Point {
  x: number;
  y: number;
}

export interface Rect extends Point, Size {}

export interface Viewport {
  /** View pixels per image pixel. */
  scale: number;
  offsetX: number;
  offsetY: number;
}

/** Smallest and largest zoom, as a multiple of the fitted scale and in device pixels per image pixel. */
export const MIN_FIT_FRACTION = 0.1;
export const MAX_DEVICE_ZOOM = 32;
/** Above this many device pixels per image pixel, smoothing is turned off so pixels stay crisp. */
export const SMOOTHING_LIMIT = 4;
/** Empty space kept around a fitted image, in view pixels. */
export const FIT_PADDING = 8;
/** How much of the image must stay inside the view while panning, in view pixels. */
export const KEEP_VISIBLE = 48;

/** Scale and centre an image so it fits inside the view, never enlarging it beyond 1:1 device pixels. */
export function fit(image: Size, view: Size, devicePixelRatio = 1, padding = FIT_PADDING): Viewport {
  if (image.width <= 0 || image.height <= 0 || view.width <= 0 || view.height <= 0) {
    return { scale: 1, offsetX: 0, offsetY: 0 };
  }
  const availableWidth = Math.max(1, view.width - 2 * padding);
  const availableHeight = Math.max(1, view.height - 2 * padding);
  const scale = Math.min(availableWidth / image.width, availableHeight / image.height, 1 / devicePixelRatio);
  return center(image, view, scale);
}

/** Shows the image at one image pixel per device pixel, centred. */
export function actualSize(image: Size, view: Size, devicePixelRatio = 1): Viewport {
  return center(image, view, 1 / devicePixelRatio);
}

/** Zoom limits for an image in a view. */
export function scaleLimits(image: Size, view: Size, devicePixelRatio = 1): { min: number; max: number } {
  const fitted = fit(image, view, devicePixelRatio).scale;
  return { min: Math.min(fitted, 1 / devicePixelRatio) * MIN_FIT_FRACTION, max: MAX_DEVICE_ZOOM / devicePixelRatio };
}

/**
 * Zooms by `factor` around a view point, keeping the image point under it in place.
 * The scale is clamped to `limits`.
 */
export function zoomAt(viewport: Viewport, factor: number, at: Point, limits: { min: number; max: number }): Viewport {
  const scale = clamp(viewport.scale * factor, limits.min, limits.max);
  const anchor = viewToImage(viewport, at);
  return { scale, offsetX: at.x - anchor.x * scale, offsetY: at.y - anchor.y * scale };
}

/** Moves the image by a view-space distance. */
export function pan(viewport: Viewport, dx: number, dy: number): Viewport {
  return { ...viewport, offsetX: viewport.offsetX + dx, offsetY: viewport.offsetY + dy };
}

/**
 * Keeps the image from being dragged out of sight: along each axis, an image smaller than the
 * view stays inside it, and a larger one keeps at least `KEEP_VISIBLE` pixels in view.
 */
export function constrain(viewport: Viewport, image: Size, view: Size): Viewport {
  return {
    scale: viewport.scale,
    offsetX: constrainAxis(viewport.offsetX, image.width * viewport.scale, view.width),
    offsetY: constrainAxis(viewport.offsetY, image.height * viewport.scale, view.height),
  };
}

/** Converts a view point to image coordinates. */
export function viewToImage(viewport: Viewport, point: Point): Point {
  return { x: (point.x - viewport.offsetX) / viewport.scale, y: (point.y - viewport.offsetY) / viewport.scale };
}

/** Converts an image point to view coordinates. */
export function imageToView(viewport: Viewport, point: Point): Point {
  return { x: point.x * viewport.scale + viewport.offsetX, y: point.y * viewport.scale + viewport.offsetY };
}

/** The image's rectangle in view coordinates. */
export function imageRect(viewport: Viewport, image: Size): Rect {
  return {
    x: viewport.offsetX,
    y: viewport.offsetY,
    width: image.width * viewport.scale,
    height: image.height * viewport.scale,
  };
}

/** Image pixel under a view point, or null outside the image. */
export function pixelAt(viewport: Viewport, image: Size, point: Point): Point | null {
  const p = viewToImage(viewport, point);
  const x = Math.floor(p.x);
  const y = Math.floor(p.y);
  return x >= 0 && y >= 0 && x < image.width && y < image.height ? { x, y } : null;
}

/** Whether the image should be drawn with smoothing at this zoom. */
export function smoothingEnabled(viewport: Viewport, devicePixelRatio = 1): boolean {
  return viewport.scale * devicePixelRatio <= SMOOTHING_LIMIT;
}

/** Zoom in percent of device pixels (100 = one image pixel per device pixel). */
export function zoomPercent(viewport: Viewport, devicePixelRatio = 1): number {
  return Math.round(viewport.scale * devicePixelRatio * 100);
}

/**
 * Where to draw an image of `source` size so it fits inside `target` (both in the same space),
 * centred and keeping its aspect ratio. Used to line up two images of different sizes.
 */
export function containRect(source: Size, target: Rect): Rect {
  if (source.width <= 0 || source.height <= 0) {
    return { ...target };
  }
  const scale = Math.min(target.width / source.width, target.height / source.height);
  const width = source.width * scale;
  const height = source.height * scale;
  return { x: target.x + (target.width - width) / 2, y: target.y + (target.height - height) / 2, width, height };
}

/** Converts a wheel delta to a zoom factor; a notch of a mouse wheel (≈100) is about 1.2×. */
export function wheelZoomFactor(deltaY: number): number {
  return Math.exp(-clamp(deltaY, -300, 300) * 0.0018);
}

function center(image: Size, view: Size, scale: number): Viewport {
  return {
    scale,
    offsetX: (view.width - image.width * scale) / 2,
    offsetY: (view.height - image.height * scale) / 2,
  };
}

function constrainAxis(offset: number, extent: number, view: number): number {
  if (extent <= view) {
    return clamp(offset, 0, view - extent);
  }
  const keep = Math.min(KEEP_VISIBLE, extent);
  return clamp(offset, keep - extent, view - keep);
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}
