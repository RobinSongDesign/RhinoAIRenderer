/** A decoded image the canvas can draw. */
export interface DecodedImage {
  width: number;
  height: number;
  /** Frees the decoded pixels. */
  close(): void;
}

/** Default budget: about 150 million pixels (≈600 MB of RGBA) across all cached images. */
export const DEFAULT_PIXEL_BUDGET = 150_000_000;

/**
 * Least-recently-used cache of decoded images, bounded by total pixel count rather than entry
 * count, since one 8K image weighs as much as dozens of thumbnails. Loads of the same id are shared.
 */
export class BitmapCache<T extends DecodedImage> {
  private readonly entries = new Map<string, T>();
  private readonly loading = new Map<string, Promise<T>>();
  private pixels = 0;

  constructor(
    private readonly load: (id: string) => Promise<T>,
    private readonly budget = DEFAULT_PIXEL_BUDGET,
  ) {}

  /** Total pixels held. */
  get size(): number {
    return this.pixels;
  }

  /** The decoded image if it is cached; marks it as recently used. */
  peek(id: string): T | undefined {
    const entry = this.entries.get(id);
    if (entry) {
      // Re-inserting moves the entry to the end of the Map's iteration order (most recent).
      this.entries.delete(id);
      this.entries.set(id, entry);
    }
    return entry;
  }

  /** Decodes an image (once, however many callers ask at the same time). */
  get(id: string): Promise<T> {
    const cached = this.peek(id);
    if (cached) {
      return Promise.resolve(cached);
    }
    const pending = this.loading.get(id);
    if (pending) {
      return pending;
    }
    const promise = this.load(id).then(
      (image) => {
        this.loading.delete(id);
        this.add(id, image);
        return image;
      },
      (error: unknown) => {
        this.loading.delete(id);
        throw error;
      },
    );
    this.loading.set(id, promise);
    return promise;
  }

  /** Frees every cached image. */
  clear(): void {
    for (const entry of this.entries.values()) {
      entry.close();
    }
    this.entries.clear();
    this.pixels = 0;
  }

  private add(id: string, image: T): void {
    this.entries.set(id, image);
    this.pixels += image.width * image.height;
    // Evict the least recently used, but always keep the image just added.
    for (const [oldId, old] of this.entries) {
      if (this.pixels <= this.budget || oldId === id) {
        break;
      }
      this.entries.delete(oldId);
      this.pixels -= old.width * old.height;
      old.close();
    }
  }
}
