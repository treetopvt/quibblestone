// ----------------------------------------------------------------------------
//  distribute.ts - pure ROUND-ROBIN blank distribution (group-play/02, #31).
//
//  Group play is the SAME engine as solo, just spread across devices: one
//  template with typed blanks, collected and assembled the one way (README
//  section 4 / CLAUDE.md section 2 - "one engine, many thin modes"). The only
//  new thing a group needs is a rule for WHO owes WHICH blank so everyone
//  contributes. That rule is this one pure function - not a new engine, not a
//  fork, just a small deterministic helper the round wiring layers on top.
//
//  The rule (AC-01, AC-04): deal blanks out ROUND-ROBIN in player order,
//  wrapping - blank index k goes to player index (k % playerCount). This
//  guarantees, for any N players and M blanks:
//    - every blank is assigned exactly once (no gaps, no duplicates),
//    - per-player counts differ by at most one (8 blanks / 5 players ->
//      2/2/2/1/1),
//    - when M >= N, everyone contributes at least one blank.
//  Round-robin (NOT chunked, e.g. player 0 owning blanks 0-1, player 1 owning
//  2-3) is deliberate: it SPREADS each player's words across the whole story,
//  which reads funnier on the reveal than a player owning one contiguous run.
//
//  Index-based by design: this works over BLANK INDICES (0..M-1) and PLAYER
//  INDICES (0..N-1), never over template content or player identity. The caller
//  resolves an index back to a Blank via getBlanks(template)[index] (Classic
//  blind shows the prompt only, AC-02) and back to a player via the roster order
//  it dealt in. Keeping this purely numeric is what makes it trivially unit
//  testable and safe to mirror server-side.
//
//  ============================ MIRRORED IN C# ================================
//  The AUTHORITATIVE distribution runs server-side in the hub
//  (api/src/Hubs/GameHub.cs, StartRound) so a client can never assign itself an
//  easier share - the server deals the blanks and tells each client only its
//  own. That C# code MIRRORS this exact algorithm (k % playerCount, roster
//  order, host first). This TS version is the UNIT-TESTED REFERENCE / spec
//  (distribute.test.ts is the prime Vitest target); the C# version is the
//  authority on the wire. There is no codegen - the two are kept in step BY
//  HAND, same discipline as the DTO wire contracts. If you change the rule
//  here, change it there.
//  ============================================================================
//
//  Pure: no I/O, no Date, no random, no mutation of inputs, deterministic for a
//  given (playerCount, blankCount). Prose: hyphens / colons / parentheses,
//  never em dashes.
// ----------------------------------------------------------------------------

/**
 * Deals `blankCount` blanks round-robin across `playerCount` players and returns,
 * for each player index p (0..playerCount-1), the SORTED list of blank indices
 * that player owns. Blank index k is owned by player (k % playerCount).
 *
 * Guarantees (AC-01, AC-04): every blank index in 0..blankCount-1 appears in
 * exactly one player's list; per-player counts differ by at most one; when
 * blankCount >= playerCount every player owns at least one blank.
 *
 * Degenerate inputs are guarded sensibly rather than throwing (this is a toy,
 * not a system of record - a bad shape should fail calm):
 *   - playerCount <= 0 -> [] (no players to deal to).
 *   - blankCount <= 0  -> an array of `playerCount` empty arrays (players, but
 *     nothing to deal - e.g. a contentless template).
 *
 * @param playerCount Number of players in the round (roster order; host first).
 * @param blankCount  Number of blanks in the round's template.
 * @returns result[p] = the sorted blank indices player p owns.
 */
export function distributeBlanks(playerCount: number, blankCount: number): number[][] {
  if (playerCount <= 0) {
    return [];
  }

  // One (initially empty) bucket per player - preserved even when there is
  // nothing to deal, so callers always get exactly `playerCount` lists back.
  const perPlayer: number[][] = Array.from({ length: playerCount }, () => []);
  if (blankCount <= 0) {
    return perPlayer;
  }

  // Deal in ascending blank-index order, so each player's list comes out
  // already sorted (blank k -> player k % playerCount; the next blank that
  // player owns is always larger). No post-sort needed.
  for (let blankIndex = 0; blankIndex < blankCount; blankIndex += 1) {
    const playerIndex = blankIndex % playerCount;
    perPlayer[playerIndex].push(blankIndex);
  }

  return perPlayer;
}
