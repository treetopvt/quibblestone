// ----------------------------------------------------------------------------
//  SettingsController - the OPERATOR runtime-settings admin API (control-plane/01,
//  issue #197). Three actions, all [Authorize(Policy = OperatorSession.PolicyName)]
//  (AC-06) - EXACTLY the boundary AdminEntitlementsController / ReportedTalesController
//  use. There is NO anonymous or player-facing read; this is operator-only, and it does
//  NOT invent a new admin auth scheme (scoping is sysadmin-console's later work).
//
//  THE WRITE GUARD RAILS (2026-07-08 adversarial-review finding, ADR 0003 "The control
//  plane cannot disable its own safety rails"): a PUT validates IN ORDER before any
//  write - (1) the key is in the catalog, (2) the value parses against its declared type,
//  (3) if Bounds is declared, the value is within [Min, Max] (AC-08 - a type check ALONE
//  is not enough), (4) if RequiresConfirmation, an explicit confirm:true is present
//  (AC-10). ANY failure is a 400 with NO write and NO action-log row.
//
//  EVERY CHANGE IS LOGGED NOW (AC-09): a successful PUT / DELETE appends exactly one row
//  to IOperatorActionLog (the seam sysadmin-console/06 owns) - action settings.put /
//  settings.delete, target the key, note the old -> new value (or "reverted to default").
//  A rejected PUT and a no-op DELETE write NO row. The row-level changedBy/changedAt stamp
//  (AC-03) is a separate, overwritable display convenience - NOT the audit trail.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// The PUT body (control-plane/01): the new <see cref="Value"/> (a JSON string / number / bool,
/// coerced to the key's declared type) and an optional <see cref="Confirm"/> flag required for
/// confirmation-gated keys (AC-10). A null body / null value is a 400.
/// </summary>
/// <param name="Value">The new value as JSON (string, number, or bool - coerced to the declared type).</param>
/// <param name="Confirm">Explicit confirmation for a RequiresConfirmation key (AC-10); ignored otherwise.</param>
public sealed record UpdateSettingRequest(JsonElement? Value, bool? Confirm);

[ApiController]
[Route("api/admin/settings")]
[Authorize(Policy = OperatorSession.PolicyName)]
public sealed class SettingsController : ControllerBase
{
    // The action verbs the log records (AC-09) - stable strings, mirrored by sysadmin-console/06.
    private const string ActionPut = "settings.put";
    private const string ActionDelete = "settings.delete";

    private readonly IRuntimeSettingsService _settings;
    private readonly IOperatorActionLog _actionLog;

    /// <summary>
    /// Constructs the controller over the runtime settings service (the read / write / cache
    /// front door) and the operator action log seam (the AC-09 writer). It ORCHESTRATES those -
    /// it never reaches into a store directly, and never touches any room / player surface.
    /// </summary>
    public SettingsController(IRuntimeSettingsService settings, IOperatorActionLog actionLog)
    {
        _settings = settings;
        _actionLog = actionLog;
    }

    /// <summary>
    /// GET /api/admin/settings -> the full catalog with defaults + overrides + effective values
    /// (AC-01 / AC-03). Every catalog key appears; a key with no override carries a null stamp.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var all = await _settings.GetAllAsync(cancellationToken);
        return Ok(all);
    }

    /// <summary>
    /// PUT /api/admin/settings/{key} -> write (or change) an override (AC-02). Validates key /
    /// type / bounds / confirmation IN ORDER (AC-08 / AC-10); any failure is a 400 with no write
    /// and no log row. On success: stamps changedBy (the operator email) + changedAt, writes the
    /// override, then appends one action-log row noting the old -> new value (AC-09).
    /// </summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Put(string key, [FromBody] UpdateSettingRequest? request, CancellationToken cancellationToken)
    {
        // (0) Guard a missing / null body explicitly (matches every other [FromBody] action here)
        // rather than leaning on [ApiController] validation to be the only thing between a null
        // body and the deref below.
        if (request?.Value is not JsonElement valueElement)
        {
            return BadRequest(new { message = "A settings value is required." });
        }

        // (1) The key must be a real catalog key - a PUT never invents a key.
        var def = SettingsCatalog.TryGet(key);
        if (def is null)
        {
            return BadRequest(new { message = $"'{key}' is not a settings key." });
        }

        // (2) The value must be a JSON SCALAR (string / number / bool) and parse against the
        // declared type. A JSON null / object / array is not a settings value - reject it here so
        // a structural or null payload can never be persisted (notably under a String key, whose
        // parse would otherwise accept any raw JSON text).
        if (!TryToWire(valueElement, out var wire))
        {
            return BadRequest(new { message = $"{def.Key} needs a string, number, or boolean value." });
        }
        if (!SettingValue.TryParse(def.Type, wire, out var parsed))
        {
            return BadRequest(new { message = $"'{wire}' is not a valid {def.Type} value for {def.Key}." });
        }

        // (3) BOUNDS (AC-08): a numeric value that type-parses but falls outside [Min, Max] is
        // rejected - a type check alone would let an operator uncap spend or zero a limiter.
        if (def.Bounds is { } bounds && def.IsNumeric)
        {
            var numeric = SettingValue.ToDecimal(def.Type, parsed);
            if (!bounds.Contains(numeric))
            {
                return BadRequest(new { message = $"{def.Key} must be within {bounds.Describe()}." });
            }
        }

        // (4) CONFIRMATION (AC-10): a RequiresConfirmation key needs an explicit confirm:true, so
        // a load-bearing flip (a kill switch, the spend ceiling) can never be an accidental PUT.
        if (def.RequiresConfirmation && request.Confirm != true)
        {
            return BadRequest(new { message = $"{def.Key} is confirmation-gated - resend with confirm:true to change it." });
        }

        // Read the OLD effective value BEFORE the write, for the action-log note.
        var before = await _settings.GetViewAsync(key, cancellationToken);
        var oldValue = before is null ? "(unknown)" : SettingValue.Format(def.Type, before.EffectiveValue);
        var newValue = SettingValue.Format(def.Type, parsed);

        // Stamp changedBy from the operator credential (ClaimTypes.Name, set by
        // OperatorAuthenticationHandler) and changedAt from now (AC-03). Persist as the canonical
        // wire form so a re-read parses identically.
        var operatorEmail = User.Identity?.Name ?? string.Empty;
        var changedAt = DateTimeOffset.UtcNow;
        await _settings.SetOverrideAsync(key, newValue, operatorEmail, changedAt, cancellationToken);

        // AC-09: log the completed, effectful change - exactly one row, AFTER the successful write.
        await _actionLog.AppendAsync(operatorEmail, ActionPut, key, $"{oldValue} -> {newValue}", cancellationToken);

        var after = await _settings.GetViewAsync(key, cancellationToken);
        return Ok(after);
    }

    /// <summary>
    /// DELETE /api/admin/settings/{key} -> clear an override, reverting the key to its code default
    /// (AC-04). On a successful clear, appends one action-log row noting "reverted to default"
    /// (AC-09). A DELETE against a key with no existing override is a harmless no-op - no write and
    /// no log row (mirrors the log's own "no row on a no-op" rule).
    /// </summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key, CancellationToken cancellationToken)
    {
        var def = SettingsCatalog.TryGet(key);
        if (def is null)
        {
            return BadRequest(new { message = $"'{key}' is not a settings key." });
        }

        var operatorEmail = User.Identity?.Name ?? string.Empty;
        var cleared = await _settings.DeleteOverrideAsync(key, operatorEmail, DateTimeOffset.UtcNow, cancellationToken);

        // Only a real clear is an effectful action - a no-op DELETE writes no log row (AC-09).
        if (cleared)
        {
            await _actionLog.AppendAsync(operatorEmail, ActionDelete, key, "reverted to default", cancellationToken);
        }

        var after = await _settings.GetViewAsync(key, cancellationToken);
        return Ok(after);
    }

    /// <summary>
    /// Reduces a JSON SCALAR to its wire-string form for parsing against a declared type, returning
    /// false for any non-scalar. A JSON string yields its content; a number / bool yields its literal
    /// text (so <c>42</c> and <c>"42"</c> both reach the Int parser, and <c>true</c> / <c>"true"</c>
    /// both reach Bool). A JSON null / object / array / undefined is NOT a settings value and returns
    /// false - a clean 400 - so a structural or null payload can never be persisted, notably under a
    /// String key (whose parse always succeeds and would otherwise store raw JSON text).
    /// </summary>
    private static bool TryToWire(JsonElement value, out string wire)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                wire = value.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.True:
                wire = "true";
                return true;
            case JsonValueKind.False:
                wire = "false";
                return true;
            case JsonValueKind.Number:
                wire = value.GetRawText();
                return true;
            default:
                wire = string.Empty;
                return false;
        }
    }
}
