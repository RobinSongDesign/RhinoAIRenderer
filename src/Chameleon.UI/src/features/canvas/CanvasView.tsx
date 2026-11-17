import { useCallback, useEffect, useMemo, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react';
import type { AssetRef } from '../../bridge/types';
import { Button } from '../../components/Button';
import componentStyles from '../../components/components.module.css';
import { useServices, useT } from '../../state/services';
import styles from './canvas.module.css';
import type { CanvasBitmap } from './decode';
import {
  actualSize,
  constrain,
  containRect,
  fit,
  imageRect,
  pan,
  pixelAt,
  scaleLimits,
  smoothingEnabled,
  wheelZoomFactor,
  zoomAt,
  zoomPercent,
  type Point,
  type Size,
  type Viewport,
} from './viewport';

export type CompareMode = 'off' | 'slider' | 'sideBySide';

export interface CanvasViewProps {
  /** The image being looked at. */
  image: AssetRef | null;
  /** The image to compare with (the "before"), if any. */
  compareWith?: AssetRef | null;
  placeholder: string;
  busyText?: string | null;
  testId?: string;
}

/** Loads a decoded image from the shared cache; null while loading or when there is none. */
function useBitmap(asset: AssetRef | null | undefined): CanvasBitmap | null {
  const { bitmaps } = useServices();
  const id = asset?.assetId ?? null;
  const [loaded, setLoaded] = useState<{ id: string; bitmap: CanvasBitmap } | null>(null);

  useEffect(() => {
    if (!id || bitmaps.peek(id)) {
      return undefined;
    }
    let current = true;
    bitmaps
      .get(id)
      .then((bitmap) => current && setLoaded({ id, bitmap }))
      .catch(() => undefined);
    return () => {
      current = false;
    };
  }, [bitmaps, id]);

  if (!id) {
    return null;
  }
  return bitmaps.peek(id) ?? (loaded?.id === id ? loaded.bitmap : null);
}

/** Tracks an element's size in CSS pixels. */
function useElementSize<T extends HTMLElement>(): [(element: T | null) => void, Size] {
  const [size, setSize] = useState<Size>({ width: 0, height: 0 });
  const observer = useRef<ResizeObserver | null>(null);
  const ref = useCallback((element: T | null) => {
    observer.current?.disconnect();
    if (!element) {
      return;
    }
    observer.current = new ResizeObserver(([entry]) => {
      const { width, height } = entry.contentRect;
      setSize((s) => (s.width === width && s.height === height ? s : { width, height }));
    });
    observer.current.observe(element);
  }, []);
  return [ref, size];
}

interface PaneProps {
  bitmap: CanvasBitmap | null;
  /** Drawn to the left of `split` (a fraction of the pane width), lined up with `bitmap`. */
  before?: CanvasBitmap | null;
  split?: number | null;
  viewport: Viewport | null;
  size: Size;
  label?: string;
}

/** One canvas showing an image (and optionally the "before" image left of a divider). */
function Pane({ bitmap, before, split, viewport, size, label }: PaneProps) {
  const canvas = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const element = canvas.current;
    const context = element?.getContext('2d');
    if (!element || !context) {
      return;
    }
    const dpr = window.devicePixelRatio || 1;
    element.width = Math.max(1, Math.round(size.width * dpr));
    element.height = Math.max(1, Math.round(size.height * dpr));
    context.setTransform(dpr, 0, 0, dpr, 0, 0);
    context.clearRect(0, 0, size.width, size.height);
    if (!bitmap || !viewport) {
      return;
    }
    context.imageSmoothingEnabled = smoothingEnabled(viewport, dpr);
    context.imageSmoothingQuality = 'high';
    const rect = imageRect(viewport, bitmap);
    if (before && split != null) {
      const x = size.width * split;
      const beforeRect = containRect(before, rect);
      context.save();
      context.beginPath();
      context.rect(0, 0, x, size.height);
      context.clip();
      context.drawImage(before.source, beforeRect.x, beforeRect.y, beforeRect.width, beforeRect.height);
      context.restore();
      context.save();
      context.beginPath();
      context.rect(x, 0, size.width - x, size.height);
      context.clip();
      context.drawImage(bitmap.source, rect.x, rect.y, rect.width, rect.height);
      context.restore();
      // Divider with a grip in the middle.
      context.fillStyle = '#ffffff';
      context.fillRect(x - 1, 0, 2, size.height);
      context.beginPath();
      context.arc(x, size.height / 2, 9, 0, Math.PI * 2);
      context.fill();
      context.strokeStyle = 'rgba(0,0,0,0.35)';
      context.stroke();
    } else {
      context.drawImage(bitmap.source, rect.x, rect.y, rect.width, rect.height);
    }
  }, [bitmap, before, split, viewport, size]);

  return (
    <div className={styles.pane}>
      <canvas ref={canvas} />
      {label && <span className={styles.paneLabel}>{label}</span>}
    </div>
  );
}

function isTyping(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName));
}

/**
 * Zoomable, pannable image view with before/after comparison.
 * Wheel zooms at the pointer (pinch on trackpads), space+drag or middle-drag pans, a
 * horizontal trackpad scroll pans, double-click fits, `1` shows 100% and holding `\` shows the
 * comparison image.
 */
export function CanvasView({ image, compareWith, placeholder, busyText, testId }: CanvasViewProps) {
  const t = useT();
  const { ui } = useServices();
  const primary = useBitmap(image);
  const secondaryAsset = compareWith && compareWith.assetId !== image?.assetId ? compareWith : null;
  const secondary = useBitmap(secondaryAsset);
  const [panesRef, panesSize] = useElementSize<HTMLDivElement>();
  const [mode, setMode] = useState<CompareMode>('off');
  const [split, setSplit] = useState(0.5);
  const [peek, setPeek] = useState(false);
  const [spaceDown, setSpaceDown] = useState(false);
  const [pointer, setPointer] = useState<Point | null>(null);
  // null = "fit to the view", which follows resizes; set once the user zooms or pans.
  const [manual, setManual] = useState<{ key: string; viewport: Viewport } | null>(null);
  const hovered = useRef(false);
  const drag = useRef<{ kind: 'pan' | 'split'; last: Point; pane: number } | null>(null);

  const effectiveMode: CompareMode = secondary ? mode : 'off';
  const paneCount = effectiveMode === 'sideBySide' ? 2 : 1;
  const paneSize = useMemo<Size>(
    () => ({ width: Math.max(0, (panesSize.width - (paneCount - 1) * 2) / paneCount), height: panesSize.height }),
    [panesSize, paneCount],
  );
  const dpr = typeof window === 'undefined' ? 1 : window.devicePixelRatio || 1;
  // A manual viewport stays while the image size and layout stay; anything else refits.
  const layoutKey = primary ? `${primary.width}x${primary.height}|${paneCount}` : '';
  const viewport = useMemo<Viewport | null>(() => {
    if (!primary || paneSize.width <= 0 || paneSize.height <= 0) {
      return null;
    }
    return manual && manual.key === layoutKey ? manual.viewport : fit(primary, paneSize, dpr);
  }, [primary, paneSize, dpr, manual, layoutKey]);

  // Functional, so several wheel or move events between two renders all count.
  const update = useCallback(
    (change: (viewport: Viewport) => Viewport) => {
      if (!primary || paneSize.width <= 0 || paneSize.height <= 0) {
        return;
      }
      setManual((previous) => {
        const base = previous && previous.key === layoutKey ? previous.viewport : fit(primary, paneSize, dpr);
        return { key: layoutKey, viewport: constrain(change(base), primary, paneSize) };
      });
    },
    [primary, paneSize, dpr, layoutKey],
  );

  const showActualSize = useCallback(() => {
    if (primary) {
      setManual({ key: layoutKey, viewport: actualSize(primary, paneSize, dpr) });
    }
  }, [primary, paneSize, dpr, layoutKey]);

  // Wheel needs a non-passive listener to stop the page from scrolling; it is re-attached
  // whenever what it reads changes, which is cheap.
  const [wheelTarget, setWheelTarget] = useState<HTMLDivElement | null>(null);
  const setPanes = useCallback(
    (element: HTMLDivElement | null) => {
      panesRef(element);
      setWheelTarget(element);
    },
    [panesRef],
  );

  useEffect(() => {
    if (!wheelTarget || !primary) {
      return undefined;
    }
    const limits = scaleLimits(primary, paneSize, dpr);
    const paneStride = paneSize.width + 2;
    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      const bounds = wheelTarget.getBoundingClientRect();
      const at = { x: (event.clientX - bounds.left) % paneStride, y: event.clientY - bounds.top };
      // Trackpads: pinch arrives as ctrl+wheel, two-finger scrolling with a horizontal part pans.
      if (!event.ctrlKey && Math.abs(event.deltaX) > 0) {
        update((v) => pan(v, -event.deltaX, -event.deltaY));
      } else {
        const delta = event.deltaMode === 1 ? event.deltaY * 33 : event.deltaY;
        update((v) => zoomAt(v, wheelZoomFactor(event.ctrlKey ? delta * 4 : delta), at, limits));
      }
    };
    wheelTarget.addEventListener('wheel', onWheel, { passive: false });
    return () => wheelTarget.removeEventListener('wheel', onWheel);
  }, [wheelTarget, primary, paneSize, dpr, update]);

  // Shortcuts work while the pointer is over the canvas and nothing is being typed.
  useEffect(() => {
    const down = (event: KeyboardEvent) => {
      if (!hovered.current || isTyping(event.target)) {
        return;
      }
      if (event.key === ' ') {
        event.preventDefault();
        setSpaceDown(true);
      } else if (event.key === '\\') {
        setPeek(true);
      } else if (event.key === '1') {
        showActualSize();
      } else if (event.key === '0') {
        setManual(null);
      }
    };
    const up = (event: KeyboardEvent) => {
      if (event.key === ' ') {
        setSpaceDown(false);
      } else if (event.key === '\\') {
        setPeek(false);
      }
    };
    const reset = () => {
      setSpaceDown(false);
      setPeek(false);
    };
    window.addEventListener('keydown', down);
    window.addEventListener('keyup', up);
    window.addEventListener('blur', reset);
    return () => {
      window.removeEventListener('keydown', down);
      window.removeEventListener('keyup', up);
      window.removeEventListener('blur', reset);
    };
  }, [showActualSize]);

  const localPoint = (event: ReactPointerEvent<HTMLDivElement>): { point: Point; pane: number } => {
    const bounds = event.currentTarget.getBoundingClientRect();
    const x = event.clientX - bounds.left;
    const pane = Math.min(paneCount - 1, Math.floor(x / (paneSize.width + 2)));
    return { point: { x: x - pane * (paneSize.width + 2), y: event.clientY - bounds.top }, pane };
  };

  const onPointerDown = (event: ReactPointerEvent<HTMLDivElement>) => {
    const { point, pane } = localPoint(event);
    const panning = event.button === 1 || (event.button === 0 && spaceDown);
    const splitting = event.button === 0 && !panning && effectiveMode === 'slider';
    if (!panning && !splitting) {
      return;
    }
    event.preventDefault();
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { kind: panning ? 'pan' : 'split', last: point, pane };
    if (splitting) {
      setSplit(Math.min(1, Math.max(0, point.x / paneSize.width)));
    }
  };

  const onPointerMove = (event: ReactPointerEvent<HTMLDivElement>) => {
    const { point } = localPoint(event);
    setPointer(point);
    const current = drag.current;
    if (!current) {
      return;
    }
    if (current.kind === 'pan') {
      const dx = point.x - current.last.x;
      const dy = point.y - current.last.y;
      update((v) => pan(v, dx, dy));
    } else {
      setSplit(Math.min(1, Math.max(0, point.x / paneSize.width)));
    }
    current.last = point;
  };

  const endDrag = () => {
    drag.current = null;
  };

  const showBefore = peek && secondary !== null;
  const shown = showBefore ? secondary : primary;
  const pixel = viewport && primary && pointer ? pixelAt(viewport, primary, pointer) : null;
  const zoom = viewport ? zoomPercent(viewport, dpr) : null;

  return (
    <div
      className={styles.root}
      data-testid={testId}
      data-asset-id={image?.assetId ?? ''}
      data-compare={effectiveMode}
      data-peek={showBefore}
      data-zoom={zoom ?? ''}
      data-split={Math.round(split * 100)}
      onPointerEnter={() => (hovered.current = true)}
      onPointerLeave={() => {
        hovered.current = false;
        setPointer(null);
      }}
    >
      <div className={styles.toolbar}>
        <Button size="small" data-testid="canvas-fit" disabled={!primary} onClick={() => setManual(null)}>
          {t('canvas.fit')}
        </Button>
        <Button size="small" data-testid="canvas-actual" disabled={!primary} onClick={showActualSize}>
          100%
        </Button>
        <span className={styles.spacer} />
        <span className={styles.group} role="group" aria-label={t('canvas.compare')}>
          {(['off', 'slider', 'sideBySide'] as const).map((value) => (
            <Button
              key={value}
              size="small"
              variant={effectiveMode === value ? 'primary' : undefined}
              data-testid={`canvas-compare-${value}`}
              disabled={value !== 'off' && !secondary}
              aria-pressed={effectiveMode === value}
              onClick={() => setMode(value)}
            >
              {t(`canvas.compare.${value}`)}
            </Button>
          ))}
        </span>
        <Button
          size="small"
          data-testid="canvas-fullscreen"
          disabled={!image}
          title={t('canvas.fullscreen')}
          aria-label={t('canvas.fullscreen')}
          onClick={() => image && ui.getState().showPreview(image)}
        >
          ⛶
        </Button>
      </div>
      <div
        ref={setPanes}
        className={`${styles.panes} ${spaceDown ? styles.panning : effectiveMode === 'slider' ? styles.splitting : ''}`}
        data-testid={testId ? `${testId}-panes` : undefined}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
        onDoubleClick={() => setManual(null)}
        onAuxClick={(e) => e.preventDefault()}
      >
        {effectiveMode === 'sideBySide' ? (
          <>
            <Pane bitmap={secondary} viewport={viewport && secondary && primary ? alignedViewport(viewport, primary, secondary) : null} size={paneSize} label={t('canvas.before')} />
            <Pane bitmap={primary} viewport={viewport} size={paneSize} label={t('canvas.after')} />
          </>
        ) : (
          <Pane
            bitmap={shown}
            before={effectiveMode === 'slider' && !showBefore ? secondary : null}
            split={effectiveMode === 'slider' && !showBefore ? split : null}
            viewport={viewport && shown && primary ? alignedViewport(viewport, primary, shown) : null}
            size={paneSize}
            label={showBefore ? t('canvas.before') : undefined}
          />
        )}
        {!image && <div className={styles.placeholder}>{placeholder}</div>}
        {busyText && (
          <div className={styles.busy}>
            <span className={componentStyles.spinner} aria-hidden="true" />
            {busyText}
          </div>
        )}
      </div>
      <div className={styles.status} data-testid={testId ? `${testId}-status` : undefined}>
        <span>{primary ? `${primary.width}×${primary.height}` : '—'}</span>
        <span data-testid={testId ? `${testId}-zoom` : undefined}>{zoom != null ? `${zoom}%` : ''}</span>
        <span>{pixel ? `${pixel.x}, ${pixel.y}` : ''}</span>
      </div>
    </div>
  );
}

/**
 * Viewport for drawing `other` so it covers the same display rectangle as `primary`
 * (centred and aspect-preserving when the sizes differ).
 */
function alignedViewport(viewport: Viewport, primary: Size, other: Size): Viewport {
  if (other.width === primary.width && other.height === primary.height) {
    return viewport;
  }
  const target = containRect(other, imageRect(viewport, primary));
  return { scale: target.width / other.width, offsetX: target.x, offsetY: target.y };
}
