# CLAUDE.md — Daybook Accounting Engine

Operating manual for working in this repo. Read this first, every session. The full
design rationale lives in `docs/design-spec.md` — this file is the day-to-day rules;
the spec is the "why." When they disagree, the spec wins and this file should be updated.

---

## What this is

**Daybook** is a journal-central, double-entry accounting engine and API. The journal
(daybook) is the single source of truth; ledgers, trial balance, and statements are all
*derived* from it. Primary user is a household, but the core stays flexible for larger
books. Open source (leaning **AGPL-3.0**), self-hosted, Linux-container-friendly.

**v1 is the core engine + a self-hosted service. Nothing more.** See "Scope" below.

---

## Golden rules (never violate these)

1. **Double-entry always balances.** No entry posts unless Σ debits = Σ credits exactly,
   at `decimal(19,4)`. Enforced in Core so no adapter can bypass it.
2. **Posted is immutable.** A posted entry has no mutators. Corrections happen by posting
   a **reversing entry**, never by editing or deleting. No UPDATE/DELETE against posted
   rows, ever — enforce in the domain *and* in the repository layer.
3. **Money is never a float.** Use the `Money` value object (`decimal(19,4)` + currency).
   All rounding is explicit (banker's rounding, `MidpointRounding.ToEven`) and only at
   defined boundaries.
4. **The journal is the narrowest, most conservative contract in the system.** It only
   ever *extends*. Version-stamp every entry; upcast on read; never rewrite history on disk.
5. **One guarded write door.** Every write to the journal goes through the
   Application/Core posting pipeline. Ingestion, scheduling, and any future service produce
   *draft* entries through that same pipeline — they never touch the store directly.
6. **No dead-end errors.** Every error carries a stable `code`, a plain what-and-why
   message, actionable `recovery` options, and a `correlationId`. A bare "an error
   occurred" is a defect.

---

## Two metadata classes (know which bucket a field is in)

- **Frozen at post** — accounting data (accounts, sides, amounts, `EntryDate`),
  `Description`, line `Memo`, and `References` (check numbers etc.). Fixed only by reversal.
- **Mutable after post (audit-logged, never touches accounting truth)** — `Tags` and
  `Reconciliation` state. Editable with a full audit trail.

Any new annotation field must be consciously placed in one of these buckets.

---

## Architecture (dependencies point inward)

```
Daybook.Accounting.Api            (ASP.NET Core, auth, DTOs)        ← adapter
Daybook.Accounting.Application    (use cases, ports/interfaces)
Daybook.Accounting.Core           (domain — ZERO external deps)     ← the engine
Daybook.Accounting.Infrastructure (EF Core/SQLite, Serilog, crypto) ← adapter
```

- **Core** depends on nothing but the BCL. Entities, value objects, invariants, and the
  derivation engine live here. This is where correctness matters most and tests are heaviest.
- **Application** orchestrates use cases and defines ports (`IJournalStore`, `IClock`,
  `ICurrentUser`, …). No infrastructure knowledge.
- **Infrastructure** implements the ports (EF Core + SQLite/SQLite3 Multiple Ciphers, Serilog, Argon2).
- **Api** is a thin adapter. Swappable for a desktop host later without touching Core.

Never make Core or Application depend on Infrastructure or Api. If you need something from
the outside, define a port and inject it.

Solution layout:
```
src/   Daybook.Accounting.{Core,Application,Infrastructure,Api}
tests/ Daybook.Accounting.{Core,Application,Infrastructure,Api}.Tests
```

---

## Tech stack

- **.NET 10 (LTS)**, C#. `Nullable` enabled, `TreatWarningsAsErrors` on, latest langversion.
- **Testing:** xUnit + **AwesomeAssertions** (free Apache-licensed FluentAssertions fork —
  do NOT use FluentAssertions ≥ 8, it is commercially licensed) + a property-based library
  (FsCheck, or CsCheck for pure-C#).
- **Logging:** Serilog (structured), with a **separate audit sink**.
- **Persistence:** EF Core + SQLite; SQLite3 Multiple Ciphers (`SQLite3MC.PCLRaw.bundle`) for
  encryption at rest — not SQLCipher; see design-spec §13.5 for why.
- **Crypto:** Argon2id KDF for the passphrase-derived key; HMAC-anchored hash chain.

Pin dependency versions. Do not introduce a dependency into **Core** without asking —
Core is meant to stay dependency-free.

---

## How we work (this repo's rhythm)

- **Work in small vertical slices, TDD, and stop at the stated milestone boundary for
  review.** Do not build ahead of the current milestone, even if the next step is obvious.
  This project has been over-scoped before; staying tight is a feature, not timidity.
- **Test-first, always.** Red → green → refactor. Write the failing test, watch it fail for
  the right reason, then make it pass, then clean up. Never write production code without a
  failing test demanding it.
- Prefer many small, focused commits with clear messages over large ones.
- When a decision isn't covered here or in the spec, ask rather than guess. Surface the
  tradeoff briefly and propose a recommendation.

---

## Testing conventions

- **Core gets near-total coverage** of domain rules. Every invariant in the spec has a test.
- **Property-based tests for invariants**, e.g.: any posted entry has Σdebits = Σcredits;
  reversing an entry nets every affected account to zero; the trial balance always balances;
  hash-chain verification holds across arbitrary valid histories.
- **Determinism:** inject `IClock` and `ICurrentUser`. No `DateTime.Now`, no ambient state,
  no wall-clock or random in domain code.
- **Test-data builders** (e.g. `AnEntry.WithLines(...)`) over ad-hoc construction.
- **Infrastructure tests run against real SQLite** (temp DB per test) — prove EF mappings,
  the gapless sequence, and append-only enforcement, not mocks.
- **Golden-journal fixture:** a corpus of entries from every historical schema version, with
  a test asserting they still read and still derive identical reports under current code.
  This is the durability guarantee — keep it green.
- **Errors are contract-tested:** each rule asserts its `code`, category, and a usable
  `recovery` payload.

Commands:
```
dotnet build
dotnet test
dotnet format          # keep it clean before committing
```

---

## Logging & error handling

- Two Serilog streams: an **application log** (structured, correlation IDs, sensitive
  values redacted) and an **append-only audit log** (who/what/when for every post, reverse,
  tag change, and reconciliation change — access-controlled, not redacted).
- Log via structured properties, not interpolated strings.
- Expected failures return a **`Result<T>`** from Core/Application — do not throw for
  business-rule violations. Exceptions are for genuine infrastructure faults only.
- API errors are RFC 7807 `ProblemDetails` extended with `code`, `field`, `recovery[]`,
  and `correlationId`. See Golden Rule 6.

---

## Versioning discipline

- **SemVer** on the public HTTP API; only the **major** in the URL (`/v1/...`). Additive-only
  within a major — never remove, rename, or repurpose a field (error `code`s included).
- Four independent version axes: API contract / journal-entry schema / domain model / DB
  schema. They move at different speeds; do not couple them.
- Every EF migration ships with a test that applies it to a fresh DB.

---

## Scope — v1 IN

Books (per-book cash **or** accrual, USD), hierarchical chart of accounts (optional codes,
placeholder nodes, account tags with inherited "effective tags"), the immutable journal
(draft→posted, reversal), typed references, line-level reconciliation + statement records,
line/entry tags (flat; key:value dimension reserved), the derivation engine (trial balance,
general ledger/register, balance sheet), HTML + Markdown reports (toner-friendly), security
(auth, TLS, encryption at rest, HMAC-anchored hash chain), structured + audit logging, the
self-hosted API, and a basic CLI.

## Scope — v1 OUT (do not build; park on the roadmap)

Scheduled/recurring transactions · budgeting/envelopes · formal income statement · period
close & closing entries · multi-currency/FX · attachments · bank import/reconciliation
automation · payees/vendors as entities · line-level references · Postgres adapter ·
multi-tenancy · GUI/TUI front ends.

If a task drifts into this list, stop and flag it.
