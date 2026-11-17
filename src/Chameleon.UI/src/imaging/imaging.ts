/** Formats the host accepts as uploads; anything else is re-encoded to PNG first. */
export const UPLOADABLE_TYPES: readonly string[] = ['image/png', 'image/jpeg', 'image/webp'];

/** Extensions offered when picking reference images (bmp is converted before upload). */
export const ACCEPTED_IMAGE_EXTENSIONS: readonly string[] = ['png', 'jpg', 'jpeg', 'webp', 'bmp'];

export type OutputFormat = 'png' | 'jpg';

/** True when a file of this type must be re-encoded before the host will accept it. */
export function needsReencode(mimeType: string): boolean {
  return !UPLOADABLE_TYPES.includes(mimeType.toLowerCase());
}

/** True when a dropped file looks like an image we can use, judged by type or extension. */
export function isAcceptedImageFile(file: { name: string; type: string }): boolean {
  if (file.type.startsWith('image/')) {
    return true;
  }
  const extension = file.name.split('.').pop()?.toLowerCase() ?? '';
  return ACCEPTED_IMAGE_EXTENSIONS.includes(extension);
}

/** MIME type of an output format. */
export function mimeTypeOf(format: OutputFormat): 'image/png' | 'image/jpeg' {
  return format === 'jpg' ? 'image/jpeg' : 'image/png';
}

/** True when an image of this MIME type already has the requested output format. */
export function matchesFormat(mimeType: string, format: OutputFormat): boolean {
  return mimeType.toLowerCase() === mimeTypeOf(format);
}

/** Loads an image element; `crossOrigin` keeps canvases readable when the page and assets differ in origin (dev server). */
export function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.crossOrigin = 'anonymous';
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error('The image could not be decoded.'));
    image.src = src;
  });
}

/** Promise wrapper around canvas.toBlob. */
export function canvasToBlob(canvas: HTMLCanvasElement, type: string, quality?: number): Promise<Blob> {
  return new Promise((resolve, reject) => {
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Encoding failed.'))), type, quality);
  });
}

/** Draws an image onto a new canvas of the same size. JPEG output gets a white backdrop instead of black. */
function drawToCanvas(image: HTMLImageElement, opaque: boolean): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = image.naturalWidth;
  canvas.height = image.naturalHeight;
  const context = canvas.getContext('2d');
  if (!context) {
    throw new Error('Canvas 2D is not available.');
  }
  if (opaque) {
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, canvas.width, canvas.height);
  }
  context.drawImage(image, 0, 0);
  return canvas;
}

/** Re-encodes an image (by URL) to PNG or JPEG. */
export async function encodeAs(src: string, format: OutputFormat, quality = 0.92): Promise<Blob> {
  const image = await loadImage(src);
  const type = mimeTypeOf(format);
  return canvasToBlob(drawToCanvas(image, type === 'image/jpeg'), type, quality);
}

/** Makes a user-supplied file uploadable: supported formats pass through, others become PNG. */
export async function normalizeForUpload(file: Blob): Promise<Blob> {
  if (!needsReencode(file.type)) {
    return file;
  }
  const url = URL.createObjectURL(file);
  try {
    return await encodeAs(url, 'png');
  } finally {
    URL.revokeObjectURL(url);
  }
}

/** Default file name for a saved result, e.g. AIRender_20261117_093015 (kept from the WPF version). */
export function defaultResultName(now: Date = new Date()): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return (
    `AIRender_${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}_` +
    `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`
  );
}
