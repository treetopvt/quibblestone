// ----------------------------------------------------------------------------
//  vaultGallery.test.ts - Vitest spec for the gallery-over-vault merge
//  (keepsake-vault/02, issue #212). Covers the PURE merge function over two
//  already-fetched arrays (no IndexedDB, no fetch, no async), plus fetchVaultTales'
//  read-only vault-id gating. The merge is where AC-01..AC-04 live, so it is
//  exercised directly with hand-built arrays - mirroring talesToEvict's
//  no-adapter, no-mocks precedent in localGallery.test.ts.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchVaultTales, mergeGalleryTales } from './vaultGallery';
import type { TaleMeta } from '../gallery/localGallery';
import type { VaultTaleView } from './vaultClient';
import { VAULT_ID_STORAGE_KEY } from './vaultId';

// A local record and its vault twin carry BYTE-IDENTICAL title/byline/parts
// (Reveal.tsx hands the same values to saveTale and autoSaveTaleToVault), so
// these builders keep the two in lock-step for the dedupe-by-content tests.
const PARTS = [
  { isWord: false, text: 'The ' },
  { isWord: true, text: 'wobbly' },
  { isWord: false, text: ' knight.' },
];

function localTale(overrides: Partial<TaleMeta> = {}): TaleMeta {
  return {
    id: 'local-1',
    title: 'A Knightly Tale',
    savedAt: 1_000,
    bylineNames: 'Sam & Mia',
    parts: PARTS,
    ...overrides,
  };
}

function vaultTale(overrides: Partial<VaultTaleView> = {}): VaultTaleView {
  return {
    taleId: 'vault-1',
    title: 'A Knightly Tale',
    parts: PARTS,
    bylineNames: 'Sam & Mia',
    createdUtc: '2026-07-01T12:00:00.000Z',
    ...overrides,
  };
}

describe('mergeGalleryTales', () => {
  it('AC-01: dedupes a local tale against its vault twin by content signature (never double-lists)', () => {
    // Same title/byline/parts on both sides, NO vaultTaleId stamp - the common
    // case (story 01's fire-and-forget save does not round-trip the id back).
    const merged = mergeGalleryTales([localTale()], [vaultTale()]);
    expect(merged).toHaveLength(1);
    expect(merged[0].source).toBe('local'); // local wins - it holds the image
    expect(merged[0].id).toBe('local-1');
  });

  it('AC-01: dedupes by the explicit vaultTaleId stamp even when content differs', () => {
    const local = localTale({ vaultTaleId: 'vault-1', title: 'Locally Renamed' });
    const merged = mergeGalleryTales([local], [vaultTale({ taleId: 'vault-1' })]);
    expect(merged).toHaveLength(1);
    expect(merged[0].source).toBe('local');
  });

  it('AC-01: returns one recency-ordered (newest-first) list across both sources', () => {
    const oldLocal = localTale({ id: 'local-old', savedAt: 1_000, parts: [{ isWord: false, text: 'old' }], title: 'Old' });
    const newVault = vaultTale({ taleId: 'vault-new', createdUtc: '2026-07-05T00:00:00.000Z', title: 'New', parts: [{ isWord: false, text: 'new' }] });
    const merged = mergeGalleryTales([oldLocal], [newVault]);
    expect(merged.map((t) => t.title)).toEqual(['New', 'Old']);
  });

  it('AC-02: a null vault result returns the local list alone, recency-ordered, without throwing', () => {
    const a = localTale({ id: 'a', savedAt: 1_000, title: 'A' });
    const b = localTale({ id: 'b', savedAt: 2_000, title: 'B' });
    const merged = mergeGalleryTales([a, b], null);
    expect(merged.map((t) => t.title)).toEqual(['B', 'A']);
    expect(merged.every((t) => t.source === 'local')).toBe(true);
  });

  it('AC-02: an empty local list with a null vault result is an empty merge (no throw)', () => {
    expect(mergeGalleryTales([], null)).toEqual([]);
  });

  it('AC-03: a tale evicted locally but present in the vault reappears in the merged view', () => {
    // The local list no longer holds it (evicted past GALLERY_CAP); the vault does.
    const merged = mergeGalleryTales([], [vaultTale({ taleId: 'evicted-then-vaulted' })]);
    expect(merged).toHaveLength(1);
    expect(merged[0].source).toBe('vault');
    expect(merged[0].id).toBe('evicted-then-vaulted');
    expect(merged[0].vaultTaleId).toBe('evicted-then-vaulted');
  });

  it('AC-04: a tale present only in the vault (a failed local write) still appears', () => {
    const localOnly = localTale({ id: 'other', title: 'Other', parts: [{ isWord: false, text: 'other' }] });
    const vaultOnly = vaultTale({ taleId: 'vault-only', title: 'Vault Only', parts: [{ isWord: false, text: 'vault only' }] });
    const merged = mergeGalleryTales([localOnly], [vaultOnly]);
    expect(merged).toHaveLength(2);
    const fromVault = merged.find((t) => t.source === 'vault');
    expect(fromVault?.title).toBe('Vault Only');
  });

  it('AC-05: a vault-sourced entry carries only the story-01 shape (title, parts, byline, createdUtc)', () => {
    const merged = mergeGalleryTales([], [vaultTale()]);
    const entry = merged[0];
    expect(entry.title).toBe('A Knightly Tale');
    expect(entry.parts).toEqual(PARTS);
    expect(entry.bylineNames).toBe('Sam & Mia');
    expect(entry.savedAt).toBe(Date.parse('2026-07-01T12:00:00.000Z'));
    // No stray fields beyond TaleMeta + the source discriminator.
    expect(Object.keys(entry).sort()).toEqual(
      ['bylineNames', 'id', 'parts', 'savedAt', 'source', 'title', 'vaultTaleId'].sort(),
    );
  });

  it('maps an empty vault byline to undefined (matching a solo local tale)', () => {
    const merged = mergeGalleryTales([], [vaultTale({ bylineNames: '' })]);
    expect(merged[0].bylineNames).toBeUndefined();
  });

  it('sinks a vault tale with an unparseable createdUtc to the oldest position (never NaN-scrambles the sort)', () => {
    const good = vaultTale({ taleId: 'good', createdUtc: '2026-07-05T00:00:00.000Z', title: 'Good', parts: [{ isWord: false, text: 'good' }] });
    const bad = vaultTale({ taleId: 'bad', createdUtc: 'not-a-date', title: 'Bad', parts: [{ isWord: false, text: 'bad' }] });
    const merged = mergeGalleryTales([], [bad, good]);
    expect(merged.map((t) => t.title)).toEqual(['Good', 'Bad']);
    expect(merged.find((t) => t.title === 'Bad')?.savedAt).toBe(0);
  });

  it('drops a duplicate vault tale id defensively (never double-lists the same vault tale)', () => {
    const merged = mergeGalleryTales(
      [],
      [vaultTale({ taleId: 'dup' }), vaultTale({ taleId: 'dup' })],
    );
    expect(merged).toHaveLength(1);
  });

  it('AC-01: dedupes across the server-side normalization (trimmed title/byline)', () => {
    // The local copy keeps the untrimmed values the reveal handed it; the vault
    // copy is what the server stored after .Trim(). They must still dedupe.
    const local = localTale({ title: '  A Knightly Tale  ', bylineNames: ' Sam & Mia ' });
    const vault = vaultTale({ title: 'A Knightly Tale', bylineNames: 'Sam & Mia' });
    const merged = mergeGalleryTales([local], [vault]);
    expect(merged).toHaveLength(1);
    expect(merged[0].source).toBe('local');
  });

  it('AC-01: dedupes when the vault dropped an empty coral-word slot the local copy kept', () => {
    // An unfilled blank: the local record keeps the empty word part; the server
    // skips it before storing. The signature drops it on both sides, so they match.
    const localParts = [
      { isWord: false, text: 'The ' },
      { isWord: true, text: '   ' }, // unfilled blank, whitespace only
      { isWord: false, text: ' knight.' },
    ];
    const vaultParts = [
      { isWord: false, text: 'The ' },
      { isWord: false, text: ' knight.' },
    ];
    const local = localTale({ parts: localParts });
    const vault = vaultTale({ parts: vaultParts });
    const merged = mergeGalleryTales([local], [vault]);
    expect(merged).toHaveLength(1);
    expect(merged[0].source).toBe('local');
  });

  it('does not falsely collide two tales whose fields differ only at the boundary', () => {
    // "The wobbly"/"" vs "The"/"wobbly" would collide under a space delimiter;
    // the unit-separator keeps the field boundaries distinct.
    const a = localTale({ id: 'a', title: 'The wobbly', bylineNames: '', parts: [] });
    const b = localTale({ id: 'b', title: 'The', bylineNames: 'wobbly', parts: [] });
    const vault = vaultTale({ taleId: 'v', title: 'The wobbly', bylineNames: '', parts: [] });
    const merged = mergeGalleryTales([a, b], [vault]);
    // a covers the vault tale; b is a genuinely distinct local tale. Two locals,
    // vault deduped against a - never b.
    expect(merged).toHaveLength(2);
    expect(merged.every((t) => t.source === 'local')).toBe(true);
  });

  it('does NOT dedupe an old parts-less local tale against an unrelated vault tale', () => {
    // A pre-keepsake-gallery/05 local tale (no parts) can have no vault twin;
    // its content signature must not collide with a real vault tale's.
    const oldLocal: TaleMeta = { id: 'old', title: 'Old Local', savedAt: 1_000 };
    const merged = mergeGalleryTales([oldLocal], [vaultTale({ title: 'Different', parts: [{ isWord: false, text: 'x' }] })]);
    expect(merged).toHaveLength(2);
  });
});

describe('fetchVaultTales', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function fakeLocalStorage(seed?: Record<string, string>): Storage {
    const map = new Map<string, string>(Object.entries(seed ?? {}));
    return {
      getItem: (k: string) => map.get(k) ?? null,
      setItem: (k: string, v: string) => void map.set(k, v),
      removeItem: (k: string) => void map.delete(k),
      clear: () => map.clear(),
      key: (i: number) => Array.from(map.keys())[i] ?? null,
      get length() {
        return map.size;
      },
    } as Storage;
  }

  it('resolves null WITHOUT minting when the device holds no vault id (a browse never creates a vault)', async () => {
    vi.stubGlobal('localStorage', fakeLocalStorage());
    const fetchSpy = vi.fn();
    vi.stubGlobal('fetch', fetchSpy);
    expect(await fetchVaultTales()).toBeNull();
    expect(fetchSpy).not.toHaveBeenCalled(); // no mint, no list - read-only
  });

  it('resolves null (never throws) when the vault fetch fails, so the gallery degrades to local', async () => {
    const vaultId = '11111111-1111-4111-8111-111111111111';
    vi.stubGlobal('localStorage', fakeLocalStorage({ [VAULT_ID_STORAGE_KEY]: vaultId }));
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')));
    expect(await fetchVaultTales()).toBeNull();
  });

  it('returns the vault tale list when a stored vault id and a good response are present', async () => {
    const vaultId = '11111111-1111-4111-8111-111111111111';
    vi.stubGlobal('localStorage', fakeLocalStorage({ [VAULT_ID_STORAGE_KEY]: vaultId }));
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ tales: [{ taleId: 'v1', title: 'T', parts: [], bylineNames: '', createdUtc: '2026-07-01T00:00:00Z' }] }),
      }),
    );
    const result = await fetchVaultTales();
    expect(result).toHaveLength(1);
    expect(result?.[0].taleId).toBe('v1');
  });
});
