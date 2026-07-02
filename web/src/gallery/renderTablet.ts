// ----------------------------------------------------------------------------
//  renderTablet.ts - client-side canvas render of a finished tale to a
//  shareable stone-tablet PNG image (keepsake-gallery/01, issue #63; watermark
//  added by keepsake-gallery/02, issue #64).
//
//  This is the FOUNDATION render function the whole keepsake-gallery feature
//  is built on: story 02 (share-with-watermark) extends this same render pass
//  with a watermark step (see computeLayout's watermarkLines and the paint
//  pass at the end of buildTabletCanvas below - it reuses paintPlainLines, the
//  same routine the byline already uses, so there is one rendering code path,
//  never a separate post-process), and story 03 (local history) stores the
//  Blob this function produces. Design it as a clean, reusable seam - do not
//  fold feature-03-specific concerns in here.
//
//  Watermark (keepsake-gallery/02, AC-03): every image this function produces
//  now carries a small, muted "carved with QuibbleStone" footer line - the
//  feature's entire ad-free growth touch (README section 3 "avoid ads"). It
//  is laid out and painted exactly like the byline (same wrap helper, same
//  paintPlainLines routine) so there is one rendering code path, and its
//  height is reserved in computeLayout up front so it can never overlap the
//  story body or byline above it.
//
//  Approach: a HAND-BUILT <canvas> render, deliberately NOT a DOM-to-image
//  library. This is a PWA where bundle size matters (CLAUDE.md section 4), and
//  the story's Technical Notes call a hand-built canvas render "the safer
//  default" - so that is what ships. No new dependency was added for this
//  story; if a canvas render is ever found insufficient, that would be a
//  documented fallback decision (AC-06), not a silent library add.
//
//  Reuse, not re-derivation: the story TEXT comes from
//  `buildRevealParts(template, assembled)` (../pages/revealParts.ts) - the
//  EXACT SAME pure function the live Reveal screen renders from, so the
//  image's literal-text/coral-word interleaving can never drift from what was
//  actually shown on screen. Word-wrapping that text to fit the canvas is
//  itself extracted into a further pure, unit-tested module
//  (./tabletLayout.ts) since canvas drawing cannot run under Vitest - only the
//  ACTUAL paint calls (fillText, gradients, the rim stroke) live in this file.
//
//  Theming: every color comes from the passed-in MUI `theme` (never a
//  hardcoded hex) - the stone-tablet gradient stops
//  (theme.palette.tablet.top/mid/bottom, the same tokens behind
//  theme.palette.tablet.gradient's CSS string), the coral highlight
//  (theme.palette.coral.main), the carved rim (theme.palette.stoneEdge.main),
//  and text colors (theme.palette.primary.main for the title, matching the
//  live screen; theme.palette.text.primary/secondary for body/byline). Canvas
//  APIs need concrete color VALUES (a gradient stop, a fillStyle string) - not
//  MUI `sx` - so reading those values off the theme object here (rather than
//  hardcoding a literal) is the correct, expected pattern for this one file
//  (see the story-agent's stack guardrails).
//
//  Child safety (AC-04/AC-05): this function renders ONLY the already-
//  assembled, already-filtered `AssembledStory`/`Template` the live Reveal
//  screen already displays (every word in it passed the safety filter
//  upstream, per child-safety/01) plus an optional caller-supplied `byline`
//  string, which callers must build from in-session nickname(s) + Guardian
//  variant(s) only - the same identity already shown in the room (no PII, no
//  device id). This module introduces no new free-text input surface: it only
//  paints data that was already vetted and already on screen.
//
//  No server round-trip (AC-06): everything below runs synchronously in the
//  browser against an in-memory <canvas> element; the only asynchronous steps
//  are (a) waiting for the already-loaded web fonts to be ready (best-effort,
//  degrades gracefully if unsupported) and (b) `canvas.toBlob`, which is a
//  local, in-process callback - never a `fetch`/hub call.
// ----------------------------------------------------------------------------

import { alpha } from '@mui/material/styles';
import type { Theme } from '@mui/material/styles';
import type { AssembledStory } from '../engine/assemble';
import type { Template } from '../engine/template';
import { buildRevealParts } from '../pages/revealParts';
import {
  lineToPlainText,
  wrapPlainTextIntoLines,
  wrapRevealPartsIntoLines,
  type MeasureTextWidth,
  type TabletLine,
} from './tabletLayout';

/** Input to {@link renderTabletImage} / {@link renderTabletDataUrl}. */
export interface RenderTabletInput {
  /** The assembled story to render (title, storyText, per-blank filled words) - same shape Reveal.tsx consumes. */
  assembled: AssembledStory;
  /** The template whose body is walked (via buildRevealParts) to interleave literal text with coral words. */
  template: Template;
  /** The live MUI theme - every color painted onto the canvas is read from here, never hardcoded. */
  theme: Theme;
  /**
   * Optional "carved by [names]" byline text (AC-02), sourced from the SAME
   * crew data the caller already shows (group play builds it from `buildCrew`
   * via ../gallery/byline.ts's `formatCrewByline`) - never a second byline
   * format invented here. Omit for no byline (still a valid image, AC-02 says
   * "when present" - e.g. solo, which has no crew to name).
   */
  byline?: string;
}

// Logical (CSS-pixel) layout constants. The actual <canvas> backing store is
// scaled by devicePixelRatio (see renderCanvas below) for a crisp image on
// high-DPI phones (AC-02/story Technical Notes) - all layout math here stays
// in this one fixed logical size, per the story's "one sensible fixed output
// size is enough" guidance (no resolution options to build/maintain).
const LOGICAL_WIDTH = 900;
const SIDE_PADDING = 64;
const TOP_PADDING = 76;
const BOTTOM_PADDING = 76;
const CONTENT_WIDTH = LOGICAL_WIDTH - SIDE_PADDING * 2;

const TITLE_FONT_SIZE = 44;
const TITLE_LINE_HEIGHT = 56;
const TITLE_TO_BODY_GAP = 34;

const BODY_FONT_SIZE = 32;
const BODY_LINE_HEIGHT = 48;

const BYLINE_FONT_SIZE = 24;
const BYLINE_LINE_HEIGHT = 32;
const BODY_TO_BYLINE_GAP = 40;

// The watermark footer (keepsake-gallery/02, AC-03): small and muted so it
// never competes with the story text or the coral words, but always legible.
// Sits below the byline (or directly below the body when there is no
// byline) - see the gap math in buildTabletCanvas below.
const WATERMARK_TEXT = 'carved with QuibbleStone';
const WATERMARK_FONT_SIZE = 15;
const WATERMARK_LINE_HEIGHT = 20;
const WATERMARK_TOP_GAP = 32;

// The stone tablet's arched corners (mirrors Reveal.tsx's '40px 40px 28px
// 28px' borderRadius shorthand: top-left, top-right, bottom-right, bottom-left).
const RIM_RADII: readonly [number, number, number, number] = [40, 40, 28, 28];
const RIM_INSET = 10;
const RIM_LINE_WIDTH = 6;

function normalBodyFont(): string {
  return `600 ${BODY_FONT_SIZE}px "Nunito", sans-serif`;
}
function coralBodyFont(): string {
  return `800 ${BODY_FONT_SIZE}px "Nunito", sans-serif`;
}
function titleFont(): string {
  return `700 ${TITLE_FONT_SIZE}px "Fredoka", sans-serif`;
}
function bylineFont(): string {
  return `700 ${BYLINE_FONT_SIZE}px "Nunito", sans-serif`;
}
function watermarkFont(): string {
  return `700 ${WATERMARK_FONT_SIZE}px "Nunito", sans-serif`;
}

/** Traces a rounded-rect path with 4 independent corner radii (top-left, top-right, bottom-right, bottom-left). */
function traceRoundedRect(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  radii: readonly [number, number, number, number],
): void {
  const [tl, tr, br, bl] = radii;
  ctx.beginPath();
  ctx.moveTo(x + tl, y);
  ctx.lineTo(x + width - tr, y);
  ctx.arcTo(x + width, y, x + width, y + tr, tr);
  ctx.lineTo(x + width, y + height - br);
  ctx.arcTo(x + width, y + height, x + width - br, y + height, br);
  ctx.lineTo(x + bl, y + height);
  ctx.arcTo(x, y + height, x, y + height - bl, bl);
  ctx.lineTo(x, y + tl);
  ctx.arcTo(x, y, x + tl, y, tl);
  ctx.closePath();
}

/** Creates a small offscreen canvas purely for `measureText` calls (never painted/appended to the DOM). */
function createMeasuringContext(): CanvasRenderingContext2D {
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas 2D context is unavailable - cannot render the tablet image.');
  return ctx;
}

/** Best-effort wait for the Fredoka/Nunito web fonts (loaded via <link> in index.html) to be ready before measuring/painting, so the image does not fall back to a system font. Degrades silently if the FontFaceSet API is unavailable. */
async function waitForFonts(): Promise<void> {
  if (typeof document === 'undefined') return;
  const fontSet = (document as Document & { fonts?: { ready?: Promise<unknown> } }).fonts;
  if (!fontSet?.ready) return;
  try {
    await fontSet.ready;
  } catch {
    // Font readiness is a nice-to-have for crispness, never a hard requirement.
  }
}

/** Computed layout for the whole tablet image: wrapped lines plus the total logical canvas height. */
interface TabletImageLayout {
  titleLines: TabletLine[];
  bodyLines: TabletLine[];
  bylineLines: TabletLine[];
  watermarkLines: TabletLine[];
  canvasHeight: number;
}

function computeLayout(input: RenderTabletInput, measuringCtx: CanvasRenderingContext2D): TabletImageLayout {
  const measure: MeasureTextWidth = (word, coral) => {
    measuringCtx.font = coral ? coralBodyFont() : normalBodyFont();
    return measuringCtx.measureText(word).width;
  };
  const measureTitle: MeasureTextWidth = (word) => {
    measuringCtx.font = titleFont();
    return measuringCtx.measureText(word).width;
  };
  const measureByline: MeasureTextWidth = (word) => {
    measuringCtx.font = bylineFont();
    return measuringCtx.measureText(word).width;
  };
  const measureWatermark: MeasureTextWidth = (word) => {
    measuringCtx.font = watermarkFont();
    return measuringCtx.measureText(word).width;
  };

  const parts = buildRevealParts(input.template, input.assembled);
  const titleLines = wrapPlainTextIntoLines(input.assembled.title, measureTitle, CONTENT_WIDTH);
  const bodyLines = wrapRevealPartsIntoLines(parts, measure, CONTENT_WIDTH);
  const bylineLines = input.byline ? wrapPlainTextIntoLines(input.byline, measureByline, CONTENT_WIDTH) : [];
  // keepsake-gallery/02 (AC-03): a fixed, short string - wrapped through the
  // same helper as the byline/title purely for consistency (it fits on one
  // line at the tablet's CONTENT_WIDTH in practice, but wrapping defensively
  // costs nothing and keeps every text block on one code path).
  const watermarkLines = wrapPlainTextIntoLines(WATERMARK_TEXT, measureWatermark, CONTENT_WIDTH);

  const titleBlockHeight = titleLines.length * TITLE_LINE_HEIGHT;
  const bodyBlockHeight = bodyLines.length * BODY_LINE_HEIGHT;
  const bylineBlockHeight = bylineLines.length > 0 ? BODY_TO_BYLINE_GAP + bylineLines.length * BYLINE_LINE_HEIGHT : 0;
  // Reserved unconditionally (AC-03: every rendered image carries the
  // watermark), so it can never overlap the body/byline above it.
  const watermarkBlockHeight = WATERMARK_TOP_GAP + watermarkLines.length * WATERMARK_LINE_HEIGHT;

  const canvasHeight =
    TOP_PADDING +
    titleBlockHeight +
    TITLE_TO_BODY_GAP +
    bodyBlockHeight +
    bylineBlockHeight +
    watermarkBlockHeight +
    BOTTOM_PADDING;

  return { titleLines, bodyLines, bylineLines, watermarkLines, canvasHeight };
}

/** Paints the stone-tablet background: the gradient fill (theme.palette.tablet.*) inside a rounded-rect, plus the carved rim stroke (theme.palette.stoneEdge.main). */
function paintTabletBackground(ctx: CanvasRenderingContext2D, width: number, height: number, theme: Theme): void {
  traceRoundedRect(ctx, 0, 0, width, height, RIM_RADII);
  const gradient = ctx.createLinearGradient(0, 0, 0, height);
  gradient.addColorStop(0, theme.palette.tablet.top);
  gradient.addColorStop(0.52, theme.palette.tablet.mid);
  gradient.addColorStop(1, theme.palette.tablet.bottom);
  ctx.fillStyle = gradient;
  ctx.fill();

  // The carved rim: an inset stroke in the stone-edge tone, matching the live
  // screen's glowing carved-rim treatment (Reveal.tsx's tabletGlow rim color).
  traceRoundedRect(
    ctx,
    RIM_INSET / 2,
    RIM_INSET / 2,
    width - RIM_INSET,
    height - RIM_INSET,
    RIM_RADII.map((r) => Math.max(r - RIM_INSET / 2, 0)) as [number, number, number, number],
  );
  ctx.strokeStyle = alpha(theme.palette.stoneEdge.main, 0.55);
  ctx.lineWidth = RIM_LINE_WIDTH;
  ctx.stroke();
}

/** Paints a block of single-color wrapped lines (the title or the byline), left- or center-aligned, returning the y position just below the block. */
function paintPlainLines(
  ctx: CanvasRenderingContext2D,
  lines: readonly TabletLine[],
  startY: number,
  lineHeight: number,
  font: string,
  color: string,
  align: 'left' | 'center',
): number {
  ctx.font = font;
  ctx.fillStyle = color;
  ctx.textBaseline = 'alphabetic';
  let y = startY;
  for (const line of lines) {
    const lineText = lineToPlainText(line);
    const x = align === 'center' ? LOGICAL_WIDTH / 2 : SIDE_PADDING;
    ctx.textAlign = align;
    ctx.fillText(lineText, x, y);
    y += lineHeight;
  }
  return y;
}

/** Paints the story body: each line's tokens drawn left-to-right, switching color/weight per coral segment. */
function paintBodyLines(
  ctx: CanvasRenderingContext2D,
  lines: readonly TabletLine[],
  startY: number,
  theme: Theme,
): number {
  ctx.textAlign = 'left';
  ctx.textBaseline = 'alphabetic';
  let y = startY;
  for (const line of lines) {
    let x = SIDE_PADDING;
    line.segments.forEach((segment, index) => {
      ctx.font = segment.coral ? coralBodyFont() : normalBodyFont();
      ctx.fillStyle = segment.coral ? theme.palette.coral.main : theme.palette.text.primary;
      ctx.fillText(segment.text, x, y);
      const width = ctx.measureText(segment.text).width;
      const spaceWidth = index < line.segments.length - 1 ? ctx.measureText(' ').width : 0;
      x += width + spaceWidth;
    });
    y += BODY_LINE_HEIGHT;
  }
  return y;
}

/** Builds the fully-painted canvas element (not yet exported to a Blob/data URL). */
async function buildTabletCanvas(input: RenderTabletInput): Promise<HTMLCanvasElement> {
  await waitForFonts();

  const measuringCtx = createMeasuringContext();
  const layout = computeLayout(input, measuringCtx);

  const dpr = typeof window !== 'undefined' && window.devicePixelRatio > 0 ? window.devicePixelRatio : 1;
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(LOGICAL_WIDTH * dpr);
  canvas.height = Math.round(layout.canvasHeight * dpr);
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas 2D context is unavailable - cannot render the tablet image.');
  ctx.scale(dpr, dpr);

  paintTabletBackground(ctx, LOGICAL_WIDTH, layout.canvasHeight, input.theme);

  let y = TOP_PADDING + TITLE_FONT_SIZE * 0.8;
  y = paintPlainLines(ctx, layout.titleLines, y, TITLE_LINE_HEIGHT, titleFont(), input.theme.palette.primary.main, 'left');
  // Tracks the line-height of the block just painted, so the "advance to the
  // next block" gap math below (GAP - lastLineHeight + nextFontSize * 0.8)
  // stays correct regardless of whether the byline is present.
  let lastLineHeight = TITLE_LINE_HEIGHT;

  y += TITLE_TO_BODY_GAP - lastLineHeight + BODY_FONT_SIZE * 0.8;
  y = paintBodyLines(ctx, layout.bodyLines, y, input.theme);
  lastLineHeight = BODY_LINE_HEIGHT;

  if (layout.bylineLines.length > 0) {
    y += BODY_TO_BYLINE_GAP - lastLineHeight + BYLINE_FONT_SIZE * 0.8;
    y = paintPlainLines(
      ctx,
      layout.bylineLines,
      y,
      BYLINE_LINE_HEIGHT,
      bylineFont(),
      input.theme.palette.text.secondary,
      'center',
    );
    lastLineHeight = BYLINE_LINE_HEIGHT;
  }

  // Watermark footer (keepsake-gallery/02, AC-03): painted last, in the SAME
  // pass, below whatever came before it (byline when present, otherwise the
  // story body) - reserved space in computeLayout means it never collides
  // with either. Muted via a reduced-alpha text.secondary (never a hardcoded
  // hex) so it reads as a quiet footer, not a competing element.
  y += WATERMARK_TOP_GAP - lastLineHeight + WATERMARK_FONT_SIZE * 0.8;
  paintPlainLines(
    ctx,
    layout.watermarkLines,
    y,
    WATERMARK_LINE_HEIGHT,
    watermarkFont(),
    alpha(input.theme.palette.text.secondary, 0.5),
    'center',
  );

  return canvas;
}

/**
 * Renders the finished tale to a PNG image and resolves with its Blob (AC-02,
 * AC-06). Client-side only - no network call. See the module header for the
 * full reuse/theming/safety contract.
 */
export async function renderTabletImage(input: RenderTabletInput): Promise<Blob> {
  const canvas = await buildTabletCanvas(input);
  return new Promise<Blob>((resolve, reject) => {
    canvas.toBlob((blob) => {
      if (blob) resolve(blob);
      else reject(new Error('Rendering the tablet image failed (toBlob returned null).'));
    }, 'image/png');
  });
}

/**
 * Same render as {@link renderTabletImage}, returned as a `data:` URL instead
 * of a Blob - a convenience for a future consumer (story 03's local gallery)
 * that may prefer a directly-embeddable `<img src>` value over a Blob/object
 * URL lifecycle.
 */
export async function renderTabletDataUrl(input: RenderTabletInput): Promise<string> {
  const canvas = await buildTabletCanvas(input);
  return canvas.toDataURL('image/png');
}
