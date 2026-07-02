// ----------------------------------------------------------------------------
//  fontawesome.ts - FontAwesome icon setup (imported once for side effects).
//
//  We register the free solid icons the app uses into the global library so any
//  component can render them by name: <FontAwesomeIcon icon="bolt" />. Story
//  story-selection/06 (favorites, AC-01) adds the ONE free-REGULAR icon this app
//  uses so far - the outline star - registered under FontAwesome's 'far' prefix
//  so a caller renders it as `icon={['far', 'star']}` (vs the already-registered
//  solid `icon="star"` for the filled state). Same registration convention:
//  register once here, render by name everywhere else.
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
  // Home + Lobby icons (session-engine/01): the gold "Create a game" CTA (plus),
  // the outlined-purple "Join a game" button (login arrow), the "No account
  // needed" reassurance check, and the Lobby host indicator (crown).
  faPlus,
  faRightToBracket,
  faCheck,
  faCrown,
  // Join icons (session-engine/02): the "100% anonymous" reassurance shield,
  // the display-name field's person icon, and the gold "Join [CODE] ->" CTA
  // arrow.
  faShield,
  faUser,
  faArrowRight,
  // Lobby roster icons (session-engine/03): the "Carvers gathered" count chip
  // (users) and the host-only "Start game" CTA glyph (play).
  faUsers,
  faPlay,
  // Lobby share widget (session-engine/04): the outlined-purple "Copy" CTA's
  // clipboard glyph. faCheck (already above) becomes the teal confirmation
  // icon and faShareNodes (already above) is the filled-purple "Share" glyph.
  faCopy,
  // FillBlank icons (game-modes/02): the progress row's chisel/carving glyph,
  // the category chip's sparkle, the carved input slot's pen-nib, and the
  // blind-mode reassurance panel's eye-slash. faArrowRight (already above) is
  // the "Next word ->" CTA's arrow.
  faHammer,
  faWandMagicSparkles,
  faPenNib,
  faEyeSlash,
  // FamilySafeToggle icon (child-safety/02): the shield-heart glyph reads as
  // "protected and kid-friendly" beside the "Family-safe" toggle label.
  faShieldHeart,
  // Reveal icons (the-reveal/01): twinkling star glyphs flanking the "Your
  // tale is carved!" header, the narration bar's inactive play glyph (faPlay
  // is already registered above), and the gold "Play another round" CTA's
  // redo arrow. faShareNodes (already above) is the outlined-purple "Share
  // the tale" button's glyph.
  faStar,
  faArrowRotateRight,
  // Waiting-screen icons (group-play/03): faCircleCheck (already above) is the
  // teal check-circle on the "[N] of [M] quibblers done" status card and the
  // done-player badge; faPenRuler is the carving glyph on the outlined-purple
  // "Review my words" button (a chisel-like tool - FontAwesome's "chisel" is a
  // Pro-only icon, so pen-ruler is the closest free-solid carving mark).
  faPenRuler,
  // StoryLengthChoice icons (story-selection/02): faHourglassHalf reads as
  // "how long will this take" beside the "Story length" label; faBolt (already
  // registered above) is the "Quick tale" option's glyph and faBook is the
  // "Full tale" option's glyph.
  faHourglassHalf,
  faBook,
  // TaleFeedback icons (story-selection/05, issue #95): the quiet "Did you
  // like this story?" thumbs up/down curation vote on the Reveal and Round
  // Complete screens.
  faThumbsUp,
  faThumbsDown,
  // Reaction-row icons (reveal-delight/01, issue #56): the four reaction pills
  // on the Reveal - Laugh (gold, a beaming face) and Heart (coral) are new here;
  // Wow (teal) reuses faWandMagicSparkles (registered above for FillBlank) and
  // Star (purple) reuses faStar (registered above for the celebration header),
  // so only the laugh face + heart are genuinely new.
  faFaceLaughBeam,
  faHeart,
  // Golden Guardian icons (reveal-delight/03, issue #58): the "tap the funniest
  // word" vote affordance uses faHandPointer; the crowned winner reuses faCrown
  // (already registered above for the Lobby host indicator) and the "N of M
  // voted" tick reuses faCircleCheck (already registered above for the Waiting
  // status). So faHandPointer is the only genuinely new glyph here.
  faHandPointer,
} from '@fortawesome/free-solid-svg-icons';
// FavoriteStarButton (story-selection/06, AC-01): the OUTLINE star for the
// not-favorited state. The FILLED star reuses the faStar already registered
// above (solid pack) - this is the app's first free-REGULAR-pack icon.
import { faStar as faStarRegular } from '@fortawesome/free-regular-svg-icons';

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
  faPlus,
  faRightToBracket,
  faCheck,
  faCrown,
  faShield,
  faUser,
  faArrowRight,
  faUsers,
  faPlay,
  faCopy,
  faHammer,
  faWandMagicSparkles,
  faPenNib,
  faEyeSlash,
  faShieldHeart,
  faStar,
  faArrowRotateRight,
  faPenRuler,
  faHourglassHalf,
  faBook,
  faThumbsUp,
  faThumbsDown,
  faFaceLaughBeam,
  faHeart,
  faHandPointer,
  faStarRegular,
);
