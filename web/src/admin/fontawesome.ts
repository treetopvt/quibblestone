// ----------------------------------------------------------------------------
//  admin/fontawesome.ts - FontAwesome icon setup for the SEPARATE operator back
//  office (sysadmin-console/01, issue #135). Imported once for side effects.
//
//  Registered the SAME way the kid app does (web/src/fontawesome.ts: library.add
//  of free-solid icons, rendered by name), but as a SEPARATE, minimal registration
//  in the admin bundle (AC-04): it imports NOTHING from the kid app and pulls in
//  only the handful of glyphs the operator login uses, so no kid-app icon list is
//  bundled into the back office (and no admin-only code leaks into the kid app).
//  Free FontAwesome packs only - no auth token needed.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { library } from '@fortawesome/fontawesome-svg-core';
import {
  // The lock reads "restricted / operator area" in the login header; the envelope
  // is the "email me a link" CTA + the "check your inbox" confirmation; the check
  // circle is the signed-in state; the triangle is the not-authorized / error state.
  faLock,
  faEnvelope,
  faCircleCheck,
  faTriangleExclamation,
  // sysadmin-console/03 (#137) - the review queue: the shield reads "moderation";
  // the flag is the report-count badge; the trash confirms a tale stays hidden; the
  // rotate-left restores a tale to serving.
  faShieldHalved,
  faFlag,
  faTrashCan,
  faRotateLeft,
  // sysadmin-console/02 (#136) - purchaser entitlements: the key reads "capability /
  // entitlement"; the magnifying glass is the email lookup; the plus is the grant CTA;
  // the ban glyph is the per-grant revoke.
  faKey,
  faMagnifyingGlass,
  faPlus,
  faBan,
  // sysadmin-console/04 (one console, one auth) - the Stripe-mode panel: the credit
  // card reads "billing mode"; the triangle above doubles as the go-live warning.
  faCreditCard,
  // sysadmin-console/05 (#214) - the Operations tab settings panel: the gear reads
  // "runtime settings" on both the available list header and the "not wired up yet"
  // dependency-tolerant fallback row.
  faGear,
  // sysadmin-console/06 (#233) - the Operations tab action-log view: the
  // clipboard-list reads "operator action log" on the header and the empty /
  // unavailable fallback rows.
  faClipboardList,
} from '@fortawesome/free-solid-svg-icons';

library.add(
  faLock,
  faEnvelope,
  faCircleCheck,
  faTriangleExclamation,
  faShieldHalved,
  faFlag,
  faTrashCan,
  faRotateLeft,
  faKey,
  faMagnifyingGlass,
  faPlus,
  faBan,
  faCreditCard,
  faGear,
  faClipboardList,
);
