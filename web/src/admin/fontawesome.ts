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
} from '@fortawesome/free-solid-svg-icons';

library.add(faLock, faEnvelope, faCircleCheck, faTriangleExclamation);
