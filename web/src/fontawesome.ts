// ----------------------------------------------------------------------------
//  fontawesome.ts - FontAwesome icon setup (imported once for side effects).
//
//  We register the free solid icons the app uses into the global library so any
//  component can render them by name: <FontAwesomeIcon icon="bolt" />.
//
//  This uses the FREE FontAwesome packages (no auth token needed). If a Pro kit
//  is adopted later, change the import sources here and add the new icons to the
//  library; call sites that reference icons by name do not change.
// ----------------------------------------------------------------------------

import { library } from '@fortawesome/fontawesome-svg-core';
import {
  faBolt,
  faPlug,
  faCircleCheck,
  faCircleXmark,
  // App-bar icons (design-system/01, AC-03): the shared <AppBar> renders
  // these for its left/right action slots across screens (back, close,
  // settings, help, home, share).
  faArrowLeft,
  faXmark,
  faGear,
  faCircleQuestion,
  faHouse,
  faShareNodes,
} from '@fortawesome/free-solid-svg-icons';

library.add(
  faBolt,
  faPlug,
  faCircleCheck,
  faCircleXmark,
  faArrowLeft,
  faXmark,
  faGear,
  faCircleQuestion,
  faHouse,
  faShareNodes,
);
