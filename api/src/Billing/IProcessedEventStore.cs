// ----------------------------------------------------------------------------
//  IProcessedEventStore - the tiny idempotency ledger for Stripe webhook events
//  (billing-entitlements/03, issue #72, AC-05).
//
//  WHY (Stripe delivers at-least-once): the same event id can arrive twice. Before
//  applying an event the handler asks "have I already processed this id?"; after a
//  successful apply it records the id. A redelivery then finds the id present and is
//  a no-op. Kept deliberately SIMPLE (a toy, CLAUDE.md section 10) - a small
//  "processed events" table, NOT a full outbox. Grant writes are ALSO upserts, so
//  this is belt-and-suspenders over an already-idempotent write path, and it is what
//  stops a past_due grace from being applied twice.
//
//  CONFIG-PRESENCE SPLIT: mirrors the grant store - a working InMemory ledger when
//  no storage connection string is configured (local dev / CI), the Table Storage
//  ledger when one is (reusing the SAME storage account as the grant store).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Billing;

/// <summary>
/// Records and checks processed Stripe webhook event ids for idempotency (billing-
/// entitlements/03, AC-05). One implementation writes to Azure Table Storage
/// (deployed); the other is a working in-memory ledger (local dev / CI).
/// </summary>
public interface IProcessedEventStore
{
    /// <summary>
    /// True if <paramref name="eventId"/> has already been processed (so the handler
    /// skips it). A read is a read - never records anything.
    /// </summary>
    /// <param name="eventId">The Stripe event id.</param>
    /// <param name="ct">Cancellation for the (storage-bound) read.</param>
    Task<bool> HasProcessedAsync(string eventId, CancellationToken ct = default);

    /// <summary>
    /// Records <paramref name="eventId"/> as processed. Idempotent - recording the same
    /// id twice is harmless (an upsert).
    /// </summary>
    /// <param name="eventId">The Stripe event id just applied.</param>
    /// <param name="ct">Cancellation for the (storage-bound) write.</param>
    Task MarkProcessedAsync(string eventId, CancellationToken ct = default);
}
