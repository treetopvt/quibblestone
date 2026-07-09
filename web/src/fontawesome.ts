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
  // accounts-identity/08: the kid-seat-presets manager uses faPen (edit a preset)
  // and faTrash (delete a preset) on the Account page.
  faPen,
  faTrash,
  faEyeSlash,
  // FamilySafeToggle icon (child-safety/02): the shield-heart glyph reads as
  // "protected and kid-friendly" beside the "Family-safe" toggle label.
  faShieldHeart,
  // Word Bank "Fresh runes" jumble action (game-modes/07): a dice glyph reads
  // as "roll me a fresh set of words" beside the on-brand "Fresh runes" label.
  faDice,
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
  // Reaction-row icons (reveal-delight/01, issue #56): narrowed from four pills
  // to three (Love / Wow / Didn't like) in the 2026-07 de-clutter. Love reuses
  // faThumbsUp and Didn't-like reuses faThumbsDown (both registered just above
  // for TaleFeedback); Wow reuses faFaceSurprise (registered in the de-clutter
  // block below). So the reaction row now needs NO glyph of its own here - the
  // former Laugh (faFaceLaughBeam) and Heart (faHeart) registrations were
  // dropped once those pills went away.
  // Golden Guardian icons (reveal-delight/03, issue #58): the "tap the funniest
  // word" vote affordance uses faHandPointer; the crowned winner reuses faCrown
  // (already registered above for the Lobby host indicator) and the "N of M
  // voted" tick reuses faCircleCheck (already registered above for the Waiting
  // status). So faHandPointer is the only genuinely new glyph here.
  faHandPointer,
  // Keepsake gallery icon (keepsake-gallery/01, issue #63): the low-key "Save
  // as image" action on the Reveal screen's bottom bar.
  faImage,
  // "Tales we've carved" gallery icons (keepsake-gallery/03, issue #65): the
  // stacked-photos glyph for the Home nav entry, the Gallery screen's header/
  // empty state, and each thumbnail's loading placeholder.
  faImages,
  // Shareable tale link icon (keepsake-gallery/04, issue #66): the host-only
  // "Share a public link" affordance on the Reveal screen's bottom bar.
  faLink,
  // Purchaser sign-in icons (accounts-identity/03, issue #69): faEnvelope for
  // the "check your email" confirmation on the Account restore surface. The
  // "Account" Home entry link reuses faUser (already registered above for Join),
  // and the signed-in state reuses faCircleCheck / the guide-to-purchase state
  // reuses faShieldHeart, so the envelope is the only genuinely new glyph.
  faEnvelope,
  // Screen de-clutter / fit-to-viewport redesign (design-handoff, 2026-07):
  //   - faBookOpen: Landing utility bar "Our tales" chip + the FillBlank
  //     tale-title pill's book glyph.
  //   - faGift: Landing utility bar "Get more" (store) chip - gold-tinted; it
  //     opens the /get-more surface (billing-entitlements/04). Also reused by the
  //     billing surfaces below for a one-time pack, so it is imported ONCE here.
  //   - faMugSaucer: Landing utility bar "Support" (tip us) chip - coral-tinted;
  //     it opens the /support tip jar (billing-entitlements/02).
  //   - faSliders: the Waiting-room (Lobby) collapsed "Game settings" row that
  //     opens the settings bottom sheet.
  //   - faChevronRight: that same "Game settings" row's right chevron.
  //   - faChevronDown: the "more options below" scroll cue at the foot of the
  //     Game settings sheet's scroll area (so players discover the modes past
  //     Classic when the sheet scrolls).
  //   - faFaceSurprise: the Reveal's "Wow" reaction pill (the 3-reaction set is
  //     Love / Wow / Didn't like); Love reuses faThumbsUp and Didn't-like reuses
  //     faThumbsDown (both already registered above).
  faBookOpen,
  faGift,
  faMugSaucer,
  faSliders,
  faChevronRight,
  faChevronDown,
  faFaceSurprise,
  // Billing surfaces (billing-entitlements/02 tip jar #71, /04 paywall #73):
  // faMugHot for "Buy the Guardians a coffee", faUnlock for the paywall CTA,
  // faCircleInfo for the returned-from-checkout cancel banner. (faGift, a
  // one-time pack, is registered just above for the Landing "Get more" chip -
  // shared, not duplicated.) The subscription glyph reuses faCrown, success
  // reuses faCircleCheck, the free-play reassurance reuses faShieldHeart.
  faMugHot,
  faUnlock,
  faCircleInfo,
  // One-blank remix (replay-remix/02, issue #61): the low-key "Remix a word"
  // secondary action on the Reveal screen's bottom cluster.
  faShuffle,
  // ErrorBoundary (B5, alpha-gate hardening): the "something went wrong" glyph
  // on the last-resort render-error fallback screen.
  faTriangleExclamation,
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
  faPen,
  faTrash,
  faEyeSlash,
  faShieldHeart,
  faDice,
  faStar,
  faArrowRotateRight,
  faPenRuler,
  faHourglassHalf,
  faBook,
  faThumbsUp,
  faThumbsDown,
  faHandPointer,
  faImage,
  faImages,
  faLink,
  faEnvelope,
  faBookOpen,
  faGift,
  faMugSaucer,
  faSliders,
  faChevronRight,
  faChevronDown,
  faFaceSurprise,
  faMugHot,
  faUnlock,
  faCircleInfo,
  faStarRegular,
  faShuffle,
  faTriangleExclamation,
);
