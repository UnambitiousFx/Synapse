---
name: changelog
description: |
  Record a resolved bug / known issue in the three-file changelog system for this repo.
  Triggers on: "add a changelog entry", "record a resolved issue", "document a known issue",
  "update the changelog", "add a known issue", "log a fixed bug".
---

# Changelog skill

Use this skill whenever a bug is found AND resolved (on the current branch) and needs to be
documented. Three files must stay in sync. Always update all three; never update just one.

## Next ID

Find the highest `NNN` already in `docs/known-issues/` and increment:

```bash
ls docs/known-issues/ | grep -oP '^\d+' | sort -n | tail -1
```

Zero-pad to three digits (e.g. 024).

---

## Step 1 — Detail file `docs/known-issues/NNN-slug.md`

File name: `NNN-kebab-case-slug-of-bug-title.md` (all lowercase, hyphens, no spaces).

Canonical format (mirror `.github/ISSUE_TEMPLATE/bug_report.yml` + the repo's header block).
Issue [001](../docs/known-issues/001-open-generic-pipeline-behavior-aot-value-type.md) is the
canonical example.

```markdown
# [Bug]: <title matching the slug>

**Severity:** High | Medium | Low
**Area:** <one of the Area values in the table below>
**Discovered on:** `<branch>`, .NET X, <other context>
**Status:** ✅ **Resolved** on `<branch>` — see [Resolution](#resolution).

> **TL;DR.** One-sentence fix summary.

---

## Describe the bug

...

---

## Steps to reproduce

1. ...

---

## Expected behavior

...

---

## Actual behavior

...

---

## Code sample

```csharp
// Minimal repro
```

---

## Library version

`<branch or package version>`

## .NET version

.NET X.0

## Operating system

Windows 11 / macOS / Ubuntu

---

## Additional context

### Root cause

...

### Resolution

...

**Verification.** <how the fix was confirmed — tests, build, manual run>
```

---

## Step 2 — Index table `docs/known-issues/README.md`

Append a row to the table **in numeric order**:

```markdown
| [NNN](NNN-slug.md) | <one-line summary — match the changelog wording exactly> | ✅ Resolved | **High** | <Area> |
```

Severity format: `**High**` for High, plain `Medium` / `Low` for the others (match existing rows).

If the new issue extends the discovery range in the blockquote at the bottom, update it:
> `NNN–MMM were found in …`

---

## Step 3 — Published changelog `docs/docs/changelog.mdx`

Add a row to the **matching area section** (existing sections: Core DI, Source Generator,
Pipeline & CQRS, Outbox, Observability, ASP.NET Core). If no section fits, add a new `## Area`
heading with its own table following the same column structure.

Row format:
```markdown
| [NNN](https://github.com/UnambitiousFx/Synapse/blob/<branch>/docs/known-issues/NNN-slug.md) | <summary> | **High** |
```

Use the **current branch** in the blob URL. Once the branch merges to `main`, the URL should be
updated to point to `main` (a follow-up commit is fine; don't block the initial entry on it).

---

## Consistency rules

| Field | Must match across all 3 files |
|---|---|
| Severity | `High` / `Medium` / `Low` — identical casing |
| Area | Identical string |
| Summary | Wording should match between README row and changelog row |
| Status | `✅ Resolved` in all three |

---

## Verification

After all three files are written:

```bash
cd docs && pnpm build
```

Build must succeed with **zero broken-link warnings** and the document count must increment by 1
(the new `docs/docs/changelog.mdx` entry does not add a page, but the build output should stay
clean). If you get a 404 on the blob link, check the branch name in the URL.

---

## Valid Area values

Match one of these exactly (used in the README table and changelog heading):

- `Core DI`
- `Generator`
- `Pipeline / CQRS` (README) / `Pipeline & CQRS` (changelog heading)
- `Outbox`
- `Observability`
- `AspNetCore mapping` (README) / `ASP.NET Core` (changelog heading)
- Add a new value only if none of the above fits; use the same string in all three files.
