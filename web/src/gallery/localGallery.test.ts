// ----------------------------------------------------------------------------
//  localGallery.test.ts - Vitest spec for the "Tales we've carved" local
//  gallery storage (keepsake-gallery/03, AC-03/AC-04).
//
//  Runs against a small in-memory fake implementing `GalleryAdapter` - no
//  IndexedDB, no network, no new test dependency (per the story's guardrail:
//  design for injectability rather than adding fake-indexeddb). The pure
//  `talesToEvict` eviction decision is also covered directly, with no adapter
//  at all.
// ----------------------------------------------------------------------------

import { describe, expect, it, vi } from 'vitest';
import {
  GALLERY_CAP,
  getTaleImage,
  listTales,
  markTaleSynced,
  saveTale,
  talesToEvict,
  type GalleryAdapter,
  type TaleMeta,
} from './localGallery';

/** A minimal in-memory fake of the storage seam - the ONLY thing tests touch. */
function createFakeAdapter(): GalleryAdapter {
  const store = new Map<string, TaleMeta & { image: Blob }>();
  return {
    async readAllMeta() {
      return [...store.values()].map(({ id, title, savedAt, bylineNames, parts, cloudTaleId }) => ({
        id,
        title,
        savedAt,
        bylineNames,
        parts,
        cloudTaleId,
      }));
    },
    async putTale(meta, image) {
      store.set(meta.id, { ...meta, image });
    },
    async readImage(id) {
      return store.get(id)?.image;
    },
    async deleteTale(id) {
      store.delete(id);
    },
    async setCloudTaleId(id, cloudTaleId) {
      const record = store.get(id);
      if (record) store.set(id, { ...record, cloudTaleId });
    },
  };
}

function makeBlob(): Blob {
  return new Blob(['pretend-png-bytes'], { type: 'image/png' });
}

describe('talesToEvict (pure, AC-04)', () => {
  it('evicts nothing when under the cap', () => {
    const existing: TaleMeta[] = [
      { id: 'a', title: 'A', savedAt: 1 },
      { id: 'b', title: 'B', savedAt: 2 },
    ];
    expect(talesToEvict(existing, 5)).toEqual([]);
  });

  it('evicts nothing when exactly one under the cap (room for one more)', () => {
    const existing: TaleMeta[] = [
      { id: 'a', title: 'A', savedAt: 1 },
      { id: 'b', title: 'B', savedAt: 2 },
    ];
    expect(talesToEvict(existing, 3)).toEqual([]);
  });

  it('evicts the single oldest entry once adding one more exceeds the cap', () => {
    const existing: TaleMeta[] = [
      { id: 'a', title: 'A', savedAt: 3 },
      { id: 'b', title: 'B', savedAt: 1 },
      { id: 'c', title: 'C', savedAt: 2 },
    ];
    expect(talesToEvict(existing, 3)).toEqual(['b']);
  });

  it('evicts multiple oldest entries, oldest-first, when far over the cap', () => {
    const existing: TaleMeta[] = Array.from({ length: 10 }, (_, i) => ({
      id: `id-${i}`,
      title: `t${i}`,
      savedAt: i,
    }));
    const evicted = talesToEvict(existing, 5);
    // 10 existing + 1 new = 11, cap 5 -> evict 6 (the oldest, savedAt 0..5).
    expect(evicted).toEqual(['id-0', 'id-1', 'id-2', 'id-3', 'id-4', 'id-5']);
    expect(existing.length + 1 - evicted.length).toBeLessThanOrEqual(5);
  });
});

describe('saveTale / listTales (AC-01, AC-03)', () => {
  it('lists saved tales newest-first', async () => {
    const adapter = createFakeAdapter();
    const nowSpy = vi.spyOn(Date, 'now');
    try {
      nowSpy.mockReturnValueOnce(1_000);
      await saveTale({ title: 'First', image: makeBlob() }, adapter);
      nowSpy.mockReturnValueOnce(2_000);
      await saveTale({ title: 'Second', image: makeBlob() }, adapter);
      nowSpy.mockReturnValueOnce(3_000);
      await saveTale({ title: 'Third', image: makeBlob() }, adapter);
    } finally {
      nowSpy.mockRestore();
    }

    const tales = await listTales(adapter);
    expect(tales.map((t) => t.title)).toEqual(['Third', 'Second', 'First']);
  });

  it('stores the byline names (or omits them) exactly as given', async () => {
    const adapter = createFakeAdapter();
    await saveTale({ title: 'With a crew', image: makeBlob(), bylineNames: 'carved by Sam & Mia' }, adapter);
    await saveTale({ title: 'Solo tale', image: makeBlob() }, adapter);

    const tales = await listTales(adapter);
    const withCrew = tales.find((t) => t.title === 'With a crew');
    const solo = tales.find((t) => t.title === 'Solo tale');
    expect(withCrew?.bylineNames).toBe('carved by Sam & Mia');
    expect(solo?.bylineNames).toBeUndefined();
  });

  it('round-trips the stored image blob via getTaleImage', async () => {
    const adapter = createFakeAdapter();
    await saveTale({ title: 'Has an image', image: makeBlob() }, adapter);

    const [{ id }] = await listTales(adapter);
    const image = await getTaleImage(id, adapter);
    expect(image).toBeInstanceOf(Blob);
  });

  it('returns undefined for a missing tale image', async () => {
    const adapter = createFakeAdapter();
    expect(await getTaleImage('does-not-exist', adapter)).toBeUndefined();
  });
});

describe('flattened parts + synced marker (keepsake-gallery/05)', () => {
  it('round-trips the flattened display parts (upload payload)', async () => {
    const adapter = createFakeAdapter();
    await saveTale(
      {
        title: 'A carved tale',
        image: makeBlob(),
        parts: [
          { isWord: false, text: 'The ' },
          { isWord: true, text: 'wobbly' },
          { isWord: false, text: ' dragon sneezed.' },
        ],
      },
      adapter,
    );

    const [tale] = await listTales(adapter);
    expect(tale.parts).toEqual([
      { isWord: false, text: 'The ' },
      { isWord: true, text: 'wobbly' },
      { isWord: false, text: ' dragon sneezed.' },
    ]);
  });

  it('leaves parts undefined for an old-style save (not uploadable, never crashes)', async () => {
    const adapter = createFakeAdapter();
    await saveTale({ title: 'Legacy tale', image: makeBlob() }, adapter);

    const [tale] = await listTales(adapter);
    expect(tale.parts).toBeUndefined();
    expect(tale.cloudTaleId).toBeUndefined();
  });

  it('marks a local tale synced with its cloud tale id (upload dedupe)', async () => {
    const adapter = createFakeAdapter();
    await saveTale({ title: 'To sync', image: makeBlob(), parts: [{ isWord: false, text: 'hi' }] }, adapter);

    const [before] = await listTales(adapter);
    expect(before.cloudTaleId).toBeUndefined();

    await markTaleSynced(before.id, 'cloud-abc-123', adapter);

    const [after] = await listTales(adapter);
    expect(after.cloudTaleId).toBe('cloud-abc-123');
    // The image and other metadata survive the in-place stamp.
    expect(after.title).toBe('To sync');
    expect(await getTaleImage(after.id, adapter)).toBeInstanceOf(Blob);
  });

  it('marking a missing tale synced is a graceful no-op', async () => {
    const adapter = createFakeAdapter();
    await expect(markTaleSynced('does-not-exist', 'cloud-x', adapter)).resolves.toBeUndefined();
  });
});

describe('cap/eviction end-to-end (AC-04)', () => {
  it('never stores more than GALLERY_CAP tales, evicting the oldest first', async () => {
    const adapter = createFakeAdapter();
    const nowSpy = vi.spyOn(Date, 'now');
    const totalToSave = GALLERY_CAP + 2;
    try {
      for (let i = 0; i < totalToSave; i += 1) {
        nowSpy.mockReturnValueOnce(i);
        await saveTale({ title: `tale-${i}`, image: makeBlob() }, adapter);
      }
    } finally {
      nowSpy.mockRestore();
    }

    const tales = await listTales(adapter);
    expect(tales).toHaveLength(GALLERY_CAP);

    const titles = tales.map((t) => t.title);
    // The two oldest saves (tale-0, tale-1) were evicted to make room.
    expect(titles).not.toContain('tale-0');
    expect(titles).not.toContain('tale-1');
    // Newest-first order is preserved even after eviction.
    expect(titles[0]).toBe(`tale-${totalToSave - 1}`);
    expect(titles[titles.length - 1]).toBe('tale-2');
  });
});
