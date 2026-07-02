// ----------------------------------------------------------------------------
//  localGallery.ts - the device-local "Tales we've carved" gallery storage
//  (keepsake-gallery/03, "Tales we've carved local history", issue #65).
//
//  This is the ONLY place raw IndexedDB calls live for this feature - never
//  scattered through Gallery.tsx or anywhere else (see the story's Technical
//  Notes). It stores exactly two things per saved tale, together, keyed by a
//  locally-generated id:
//    - the rendered tablet image BLOB (story 01's `renderTabletImage()`
//      output - this module NEVER re-renders from engine data, it only stores
//      what the caller already rendered)
//    - a small metadata record: `{ id, title, savedAt (epoch ms), bylineNames? }`
//      - already-vetted display content only (the SAME title/byline the
//      Reveal screen already shows), no PII, no raw per-player submissions
//      (AC-05).
//
//  Why IndexedDB (not localStorage): localStorage's ~5-10MB, string-only quota
//  is a poor fit for storing multiple PNG images (the Technical Notes call
//  this out explicitly). IndexedDB stores Blobs natively and has a much larger
//  practical quota.
//
//  Cap/eviction (AC-04): a concrete, recorded policy - GALLERY_CAP = 30 saved
//  tales, oldest-first eviction once a save would push the count over the cap
//  (see docs/features/keepsake-gallery/03-tales-weve-carved-history.md's
//  Technical Notes for the recorded numbers). The eviction DECISION is a PURE
//  function, `talesToEvict`, so it is unit-testable with plain arrays and no
//  IndexedDB at all (AC-04's test requirement).
//
//  Testability without a new dependency: every function that touches storage
//  takes an optional `GalleryAdapter` (defaulting to the real IndexedDB-backed
//  adapter, constructed lazily so importing this module never touches
//  IndexedDB by itself). Tests inject a small in-memory fake implementing the
//  same interface instead of adding a new dependency (no fake-indexeddb) - see
//  localGallery.test.ts.
//
//  Robustness: every public function swallows a storage failure (IndexedDB
//  unavailable, blocked, quota, private browsing) rather than throwing -
//  mirroring ../content/favorites.ts's posture. A gallery write is a
//  convenience, never a requirement gameplay depends on to proceed, so
//  `saveTale` failing can never break the caller's other work (e.g.
//  Reveal.tsx's "Save as image" download, AC-01's capture-on-save wiring).
//
//  Child safety (AC-05, AC-06): this module stores only what a caller already
//  rendered/displayed (the image + title/date/byline-names) - it introduces no
//  new free-text entry point and performs no filtering itself (the content was
//  already filtered upstream, per child-safety/01). Clearing the browser's
//  storage empties the gallery - expected, documented behavior for
//  device-local, account-free storage (AC-06), not a bug this module works
//  around (no server backup, no recovery path).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/**
 * One saved tale's small metadata record (AC-05: nothing beyond this - no
 * PII, no raw engine/story data). `savedAt` is an epoch-ms timestamp.
 * `bylineNames` mirrors Reveal's `saveImageByline` prop (already-filtered
 * display names, e.g. "carved by Sam & Mia") - omitted when the caller had no
 * byline to give (e.g. solo).
 */
export interface TaleMeta {
  id: string;
  title: string;
  savedAt: number;
  bylineNames?: string;
}

/** Input to {@link saveTale}: the rendered image plus its small display metadata. */
export interface SaveTaleInput {
  /** The story-01-rendered tablet PNG blob - this module never re-renders one. */
  image: Blob;
  title: string;
  bylineNames?: string;
}

/**
 * The storage seam this module is built behind (AC-03/AC-04's test
 * requirement): every raw IndexedDB call lives inside an implementation of
 * this interface, so `saveTale`/`listTales`/`getTaleImage` can be exercised in
 * Vitest against a plain in-memory fake with no real IndexedDB and no new
 * test dependency.
 */
export interface GalleryAdapter {
  /** Every stored tale's metadata, in no particular order (callers sort). */
  readAllMeta(): Promise<TaleMeta[]>;
  /** Upsert a tale's metadata + image blob together, keyed by `meta.id`. */
  putTale(meta: TaleMeta, image: Blob): Promise<void>;
  /** The stored image blob for `id`, or `undefined` if there is none. */
  readImage(id: string): Promise<Blob | undefined>;
  /** Remove a tale (metadata + image) by id. A no-op if it was never stored. */
  deleteTale(id: string): Promise<void>;
}

// ----------------------------------------------------------------------------
//  Cap/eviction policy (AC-04) - RECORDED numbers, not left unspecified:
//  a maximum of 30 saved tales, oldest-first eviction once a save would push
//  the count over the cap. See the story doc's Technical Notes for the
//  rationale (a generous number for a "families over months" use case while
//  keeping device storage bounded).
// ----------------------------------------------------------------------------
export const GALLERY_CAP = 30;

/**
 * PURE eviction decision (AC-04): given the currently-stored tale metadata and
 * a cap, returns the ids that must be evicted (oldest-`savedAt`-first) to make
 * room for ONE more tale about to be saved, so the total never exceeds `cap`.
 * Takes and returns plain data only - no IndexedDB, no async - so it is
 * directly unit-testable with hand-built arrays.
 */
export function talesToEvict(existing: readonly TaleMeta[], cap: number): string[] {
  const overBy = existing.length + 1 - cap;
  if (overBy <= 0) return [];
  return [...existing]
    .sort((a, b) => a.savedAt - b.savedAt)
    .slice(0, overBy)
    .map((tale) => tale.id);
}

/** A locally-generated id for a new tale - never derived from any player-identifying data. */
function generateTaleId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  // A crypto-less fallback (older browser / test environment): still unique
  // enough for a single-device gallery, never used for anything security-sensitive.
  return `tale-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

// ----------------------------------------------------------------------------
//  The real, IndexedDB-backed adapter (lazily constructed - see
//  getDefaultAdapter below - so simply importing this module never touches
//  IndexedDB, which keeps it safe to import under Vitest's `node` environment).
// ----------------------------------------------------------------------------

const DB_NAME = 'qs-gallery';
const DB_VERSION = 1;
const STORE_NAME = 'tales';

/** The on-disk record shape: the metadata plus the image blob, one row per tale. */
interface StoredRecord extends TaleMeta {
  image: Blob;
}

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === 'undefined') {
      reject(new Error('IndexedDB is not available in this environment.'));
      return;
    }
    const request = indexedDB.open(DB_NAME, DB_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME, { keyPath: 'id' });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('Failed to open the gallery database.'));
    // Cannot fire at DB_VERSION 1, but a future schema bump while another tab
    // holds the DB open would otherwise leave this promise forever unsettled
    // (hanging every getDb() await). Reject instead - callers swallow it, so
    // the gallery degrades to empty rather than hanging (keepsake/03 SUG-02).
    request.onblocked = () => reject(new Error('The gallery database is blocked by another open tab.'));
  });
}

/** Builds the real IndexedDB-backed {@link GalleryAdapter}. Opens the database lazily, on first use. */
function createIndexedDbAdapter(): GalleryAdapter {
  let dbPromise: Promise<IDBDatabase> | undefined;
  const getDb = (): Promise<IDBDatabase> => {
    if (!dbPromise) dbPromise = openDatabase();
    return dbPromise;
  };

  return {
    async readAllMeta() {
      const db = await getDb();
      return new Promise<TaleMeta[]>((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, 'readonly');
        const request = tx.objectStore(STORE_NAME).getAll();
        request.onsuccess = () => {
          const records = request.result as StoredRecord[];
          resolve(records.map(({ id, title, savedAt, bylineNames }) => ({ id, title, savedAt, bylineNames })));
        };
        request.onerror = () => reject(request.error ?? new Error('Failed to read the gallery.'));
      });
    },
    async putTale(meta, image) {
      const db = await getDb();
      return new Promise<void>((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, 'readwrite');
        const record: StoredRecord = { ...meta, image };
        tx.objectStore(STORE_NAME).put(record);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error ?? new Error('Failed to save the tale.'));
      });
    },
    async readImage(id) {
      const db = await getDb();
      return new Promise<Blob | undefined>((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, 'readonly');
        const request = tx.objectStore(STORE_NAME).get(id);
        request.onsuccess = () => {
          const record = request.result as StoredRecord | undefined;
          resolve(record?.image);
        };
        request.onerror = () => reject(request.error ?? new Error('Failed to read the tale image.'));
      });
    },
    async deleteTale(id) {
      const db = await getDb();
      return new Promise<void>((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, 'readwrite');
        tx.objectStore(STORE_NAME).delete(id);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error ?? new Error('Failed to delete the tale.'));
      });
    },
  };
}

// Memoized singleton: constructed on first actual use (not at module import
// time), so importing this module - or calling its functions with an explicit
// fake adapter, as every test does - never touches IndexedDB.
let cachedDefaultAdapter: GalleryAdapter | undefined;
function getDefaultAdapter(): GalleryAdapter {
  if (!cachedDefaultAdapter) cachedDefaultAdapter = createIndexedDbAdapter();
  return cachedDefaultAdapter;
}

// ----------------------------------------------------------------------------
//  Public API
// ----------------------------------------------------------------------------

/**
 * Saves a newly-rendered tale to the local gallery (AC-01), evicting the
 * oldest tale(s) first if this save would push the count past
 * {@link GALLERY_CAP} (AC-04). Silently swallows any storage failure
 * (IndexedDB unavailable, blocked, quota, private browsing) - a gallery write
 * is a device convenience, never a requirement the caller's own flow (e.g.
 * Reveal's "Save as image" download) depends on to succeed.
 */
export async function saveTale(input: SaveTaleInput, adapter: GalleryAdapter = getDefaultAdapter()): Promise<void> {
  try {
    const meta: TaleMeta = {
      id: generateTaleId(),
      title: input.title,
      savedAt: Date.now(),
      bylineNames: input.bylineNames,
    };
    const existing = await adapter.readAllMeta();
    const evictIds = talesToEvict(existing, GALLERY_CAP);
    for (const evictId of evictIds) {
      await adapter.deleteTale(evictId);
    }
    await adapter.putTale(meta, input.image);
  } catch {
    // Storage unavailable/blocked/quota: no-op, matching favorites.ts's posture.
  }
}

/**
 * Lists every saved tale's metadata, newest-first (AC-01's gallery order).
 * Returns `[]` on any storage failure rather than throwing.
 */
export async function listTales(adapter: GalleryAdapter = getDefaultAdapter()): Promise<TaleMeta[]> {
  try {
    const all = await adapter.readAllMeta();
    return [...all].sort((a, b) => b.savedAt - a.savedAt);
  } catch {
    return [];
  }
}

/**
 * Fetches a saved tale's rendered image blob by id (AC-02's full-image view).
 * Returns `undefined` if the tale is missing or on any storage failure -
 * never throws.
 */
export async function getTaleImage(id: string, adapter: GalleryAdapter = getDefaultAdapter()): Promise<Blob | undefined> {
  try {
    return await adapter.readImage(id);
  } catch {
    return undefined;
  }
}
