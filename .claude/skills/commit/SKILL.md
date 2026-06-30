---
name: commit
description: Stage changes and create a conventional-commit-style commit for QuibbleStone. Detects type and scope from the changed files. Use when the user says "commit", "commit changes", or wants to create a commit.
disable-model-invocation: true
allowed-tools: Bash, Read, Edit, Grep, Glob
---

# Commit (QuibbleStone)

Create a clean conventional commit. Keeps history readable and easy to scan.

## Steps

1. `git status` and `git diff --stat` (and `git diff` for the substance) to see
   what changed. Never commit unrelated changes together.
2. Pick the **type**: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `ci`,
   `build`, `style`.
3. Pick the **scope** from the paths changed:
   - `web/` -> `web`
   - `api/` -> `api`
   - `infra/` -> `infra`
   - `.github/workflows/` -> `ci`
   - `docs/features/` -> `docs` (or the feature slug)
   - `.claude/` -> `agents`
   - repo-root config -> omit scope or use `repo`
4. Write the message: `type(scope): summary` in the imperative, under ~72 chars.
   Add a short body only if the *why* is not obvious from the summary.
5. Stage deliberately (`git add <paths>`) and commit.

## Conventions

- **No em dashes** anywhere in the message - use hyphens, colons, or parentheses.
- Imperative mood ("add", not "added"/"adds").
- One logical change per commit.
- Co-author trailer:

  ```
  Co-Authored-By: Claude <noreply@anthropic.com>
  ```

- Commit or push only when the user asks. If on `main`, create a branch first.
- Never use `--no-verify`.

## Example

```
feat(web): show reveal screen when host ends the round

Subscribes to the RoundRevealed hub event and renders the assembled story.
Closes the Slice 1 reveal story.

Co-Authored-By: Claude <noreply@anthropic.com>
```
