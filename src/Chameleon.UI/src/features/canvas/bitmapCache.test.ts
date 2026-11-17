import { describe, expect, it } from 'vitest';
import { BitmapCache, type DecodedImage } from './bitmapCache';

class FakeImage implements DecodedImage {
  closed = false;
  constructor(
    readonly width: number,
    readonly height: number,
  ) {}
  close(): void {
    this.closed = true;
  }
}

function setup(budget: number) {
  const loads: string[] = [];
  const images = new Map<string, FakeImage>();
  const cache = new BitmapCache<FakeImage>(async (id) => {
    loads.push(id);
    const image = new FakeImage(10, Number(id.split(':')[1] ?? 10));
    images.set(id, image);
    return image;
  }, budget);
  return { cache, loads, images };
}

describe('BitmapCache', () => {
  it('decodes each image once, even when asked concurrently', async () => {
    const { cache, loads } = setup(1000);

    const [a, b] = await Promise.all([cache.get('x'), cache.get('x')]);
    await cache.get('x');

    expect(a).toBe(b);
    expect(loads).toEqual(['x']);
  });

  it('evicts the least recently used images when over the pixel budget', async () => {
    const { cache, images } = setup(250);
    await cache.get('a');
    await cache.get('b');
    cache.peek('a');

    await cache.get('c');

    expect(cache.size).toBe(200);
    expect(cache.peek('b')).toBeUndefined();
    expect(images.get('b')!.closed).toBe(true);
    expect(cache.peek('a')).toBeDefined();
  });

  it('keeps an image larger than the budget until the next one arrives', async () => {
    const { cache } = setup(50);

    await cache.get('big:100');

    expect(cache.peek('big:100')).toBeDefined();
  });

  it('retries after a failed load', async () => {
    let fail = true;
    const cache = new BitmapCache<FakeImage>(async () => {
      if (fail) {
        throw new Error('decode failed');
      }
      return new FakeImage(1, 1);
    });

    await expect(cache.get('x')).rejects.toThrow('decode failed');
    fail = false;
    await expect(cache.get('x')).resolves.toBeInstanceOf(FakeImage);
  });

  it('frees everything on clear', async () => {
    const { cache, images } = setup(1000);
    await cache.get('a');

    cache.clear();

    expect(images.get('a')!.closed).toBe(true);
    expect(cache.size).toBe(0);
  });
});
