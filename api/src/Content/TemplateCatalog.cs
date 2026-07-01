// ----------------------------------------------------------------------------
//  TemplateCatalog - the MINIMAL server-side template catalog (group-play/01).
//
//  ============================ KEEP IN SYNC BY HAND ==========================
//  This catalog MIRRORS web/src/content/seedLibrary.ts - one entry per template,
//  matched by Id. It is a WIRE-CONTRACT-STYLE mirror (same discipline as the
//  PlayerDto / RoomStateDto DTOs in GameHub.cs): when a template is added,
//  removed, renamed, or its familySafe tag / blank count changes in
//  seedLibrary.ts, THIS file MUST be updated to match by hand. There is no
//  codegen and no shared source - the two drift silently if you forget.
//  ============================================================================
//
//  Why the server holds ONLY { Id, FamilySafe, BlankCount } and NOT the prose:
//  QuibbleStone's charter is "one engine, many thin modes" (README section 4) -
//  the template PROSE / prompts / body / titles stay CLIENT-SIDE in seedLibrary,
//  and each client resolves a template's full content from that bundled library
//  BY ID. Copying the prose into C# would duplicate content and risk forking the
//  engine, so the server deliberately carries only the tiny facts it needs to
//  make an authoritative, safety-aware selection:
//    - Id         : the stable key the client resolves full content from.
//    - FamilySafe : the ONLY signal the family-safe gate acts on (child-safety/02,
//                   AC-04). Every current seed template is family-safe:true, so a
//                   family-safe round is structurally correct even though the
//                   filtered set does not visibly differ yet - that is expected.
//    - BlankCount : carried NOW (group-play/01) so group-play/02's index-based
//                   blank distribution / attribution does not have to reshape this
//                   catalog later. group-play/01 itself does not read it.
//
//  This is PURE DATA (like seedLibrary), stateless, and registered as a singleton
//  in DI (Program.cs) so every transient GameHub instance shares the one catalog.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Content;

/// <summary>
/// One template as the SERVER knows it (group-play/01). Deliberately minimal:
/// the id the client resolves full content from, the family-safe flag the gate
/// acts on (child-safety/02), and the blank count group-play/02 will use for
/// index-based distribution. Mirrors one entry of web/src/content/seedLibrary.ts
/// by <see cref="Id"/> - see the file header's KEEP IN SYNC note.
/// </summary>
/// <param name="Id">Stable template id - the key the client resolves full prose/body from seedLibrary.</param>
/// <param name="FamilySafe">True when the template is tagged family-safe (seedLibrary tags.familySafe). The one signal the family-safe gate reads (AC-04).</param>
/// <param name="BlankCount">Number of blanks in the template - carried for group-play/02's distribution; unused by group-play/01.</param>
public sealed record TemplateCatalogEntry(string Id, bool FamilySafe, int BlankCount);

/// <summary>
/// The minimal server-side template catalog: one <see cref="TemplateCatalogEntry"/>
/// per web seed template, kept in sync with web/src/content/seedLibrary.ts BY HAND
/// (see the file header). Registered as a singleton; the hub reads
/// <see cref="Entries"/> to select a round's template.
/// </summary>
public sealed class TemplateCatalog
{
    // Mirrors web/src/content/seedLibrary.ts (id, tags.familySafe, blank count).
    // Every current seed template is family-safe (blank counts vary 4-6). If a
    // NON-family-safe template is ever added to seedLibrary, add it here with
    // FamilySafe: false so the server gate filters it correctly (AC-04).
    private static readonly IReadOnlyList<TemplateCatalogEntry> Catalog =
    [
        new TemplateCatalogEntry("wobbly-wizard", FamilySafe: true, BlankCount: 6),
        new TemplateCatalogEntry("space-llama", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("road-trip-disaster", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("school-of-noodles", FamilySafe: true, BlankCount: 4),
        new TemplateCatalogEntry("monster-under-bed", FamilySafe: true, BlankCount: 4),
        new TemplateCatalogEntry("food-truck-frenzy", FamilySafe: true, BlankCount: 4),
        new TemplateCatalogEntry("backyard-safari", FamilySafe: true, BlankCount: 4),
        new TemplateCatalogEntry("pirate-puddle", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("robot-recital", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("dragon-sock-thief", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("birthday-balloon-mayhem", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("snowman-summer-job", FamilySafe: true, BlankCount: 4),
    ];

    /// <summary>The full catalog (host first ordering is irrelevant - selection is random).</summary>
    public IReadOnlyList<TemplateCatalogEntry> Entries => Catalog;
}
