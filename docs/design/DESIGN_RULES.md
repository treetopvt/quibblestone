# QuibbleStone — design rules

Brand: playful storybook-fantasy, joyful, warm, all-ages. Motif: ancient glowing stone tablet where the story gets "carved."

## Palette
- Background: parchment `#F6EEDD`
- Surfaces/cards: sandstone `#E8DCC4` (cards often `#ECE2CC`)
- Text: warm dark brown `#2B2622`
- Primary: purple `#6C4BD8`
- CTA (always): gold `#FFB22E`
- Accents: coral `#FF6B57`, teal `#2FB8A0`

## Type
- Headings/wordmark/buttons: **Fredoka** (600/700)
- Body/UI: **Nunito** (600/700/800)

## CONSISTENT APP BAR (every screen with one — do not deviate)
- Container: `display:flex; align-items:center; gap:10px; padding:6px 16px 8px;`
- Icon buttons: `42x42`, `border:none; border-radius:14px; background:rgba(43,38,34,.07); cursor:pointer;` — icon `stroke:#2B2622; stroke-width:2.4`
- Title: `flex:1; text-align:center; font-family:'Fredoka'; font-weight:600; font-size:21px; color:#2B2622;`
- Always balance the right side with a matching 42px button or empty `<div style="width:42px;height:42px"></div>`

## CONSISTENT BUTTONS (every screen — do not deviate)
- **Primary CTA (gold)** — the main action on each screen:
  `height:62px; border:none; border-radius:20px; gap:11px; background:linear-gradient(180deg,#FFC24E 0%,#FFB22E 100%); color:#2B2622; font-family:'Fredoka'; font-weight:600; font-size:20px; box-shadow:0 12px 22px -8px rgba(255,178,46,.85), inset 0 2px 0 rgba(255,255,255,.5);`
- **Secondary (outlined purple)**:
  `height:60px; border:2.5px solid #6C4BD8; border-radius:20px; gap:11px; background:rgba(108,75,216,.06); color:#6C4BD8; font-family:'Fredoka'; font-weight:600; font-size:20px;`
- Icon inside a button: 22px, stroke 2.6, color matches the button's text color.

## Other
- Portrait phone frame, ~390×844, consistent bezel/status bar across screens.
- Reusable `Guardian.dc.html` renders player avatars (variant: purple/gold/coral/teal/sand/plum).
- Material UI vocabulary (app bars, cards, FABs, chips, dialogs), generous spacing, friendly rounded corners, 44px+ hit targets.
