<!--
  TEMPLATE: review-gates.md - the two-tier review-gate definition, generalized from the cadence+pulse
  reviews. Independence is a property of the reviewing CONTEXT, not of a second human. Tier A (a
  separate agent/bot context) is always on and cheap; Tier B (a human) is reserved for Critical classes.
  Every gate is tagged [machine-enforced] or [reviewer-checked] so none is silently honor-system.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Review gates (two-tier)

**Independence is structural, not human.** A change is "independently reviewed" when a **context other
than the one that produced it** examines it. That context is normally an agent/bot, and that is cheap -
do not gate on human availability except where the change class demands it.

## The two tiers of independence

### Tier A - structural independence (ALWAYS, near-zero cost)

Every builder diff and every plan is reviewed by a separate context from its author:

- **Automated PR review** - a Copilot-class bot on every PR. `[machine-enforced]` (a named gate; the
  bot's review is a required check).
- **A distinct review agent** - a separately-prompted agent whose sole mandate is "attack this diff /
  plan; default to rejecting." `[reviewer-checked, separate context]`. Emits a machine-readable
  clean / not-clean verdict.

Neither requires a human. This is the default and it is not optional.

### Tier B - human sign-off (RESERVED)

A second human (team) or the owner deliberately switching roles (solo) approves - **only** when the
change touches a **Critical class**:

- isolation or security boundaries
- cross-plane or public-contract changes
- data migrations
- money or safety seams
- the integration seam / composition root itself

For everything else, Tier A is sufficient. Tiering this is what keeps review from becoming a bottleneck
(and is why pulse's solo+agent setup shipped fast without a human in every loop).

## The gate sequence (tag every gate)

| Gate | What runs | Tier / enforcement |
|---|---|---|
| **0 - machine** | PR-triggered CI: affected stack's build + lint + typecheck + test; ALL stacks gated | `[machine-enforced]` |
| **1 - structural** | Automated PR review + the review-agent pass | `[machine-enforced]` (bot) + `[reviewer-checked]` (agent) |
| **2 - integration** | Re-run CI on the merged tree; review the integrated delta; seam owner reconciles the composition root | `[machine-enforced]` + `[reviewer-checked]` |
| **3 - human (conditional)** | Human sign-off - ONLY for a Critical-class change | `[reviewer-checked]` (Tier B) |
| **4 - release** | Full suite green; assume-green deploy; rollback path confirmed | `[machine-enforced]` |

Rules:
- **No gate is silently honor-system.** If it is not `[machine-enforced]`, it is a named person's job.
- **Deploy assumes-green.** A quality gate that only runs inside deploy is not a gate - move it onto the
  PR.
- **Gate 3 is skipped for non-Critical changes.** That is the entire point of tiering it.
