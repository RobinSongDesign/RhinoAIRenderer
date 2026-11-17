import type { DecodedImage } from './bitmapCache';

/** A decoded image together with what the 2D context draws. */
export interface CanvasBitmap extends DecodedImage {
  source: CanvasImageSource;
}

/**
 * Decodes the image at a URL. Uses createImageBitmap where available (decoding off the main
 * thread on most engines) and falls back to the decoded <img> element.
 */
export async function decodeImage(url: string): Promise<CanvasBitmap> {
  const element = new Image();
  element.decoding = 'async';
  element.src = url;
  await element.decode();
  if (typeof createImageBitmap === 'function') {
    const bitmap = await createImageBitmap(element);
    return { source: bitmap, width: bitmap.width, height: bitmap.height, close: () => bitmap.close() };
  }
  return { source: element, width: element.naturalWidth, height: element.naturalHeight, close: () => undefined };
}
