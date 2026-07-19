# Daybook — Accounting API Design Specification (v1)

*Working name: **Daybook**, chosen because the journal/daybook is the central document. Rename freely.*

**Status:** Draft for review · **Scope:** v1 core engine + self-hosted service · **Author's basis:** GAAP-aware, household-first, flexible for larger books

---

## 1. Purpose, Goals, and Non-Goals

### 1.1 Purpose
A bulletproof, double-entry accounting API whose single source of truth is an **immutable journal**. Every other view — ledgers, trial balance, balance sheet — is *derived* from the journal by folding over posted entries, exactly as in traditional accounting. If the journal is correct, everything downstream is correct by construction.

### 1.2 Goals (v1)
- Correct, tamper-evident **double-entry** journal with a complete audit trail.
- **Derived** ledgers and reports (nothing is stored that can't be recomputed from the journal).
- **Per-book** choice of cash or accrual basis.
- Single-currency (USD) today, modeled so additional currencies are additive later.
- Reports: **trial balance**, **general ledger / account register**, **balance sheet** — all toner-friendly.
- Security and privacy appropriate to sensitive financial data, self-hosted for one household.
- Comprehensive TDD test suite and comprehensive structured + audit logging as first-class concerns.

### 1.3 Non-Goals (deferred to later releases)
Scheduled/recurring transactions · budgeting/envelopes · multi-currency FX · formal period close and closing entries · multi-tenancy · attachments (receipt images) · bank import/reconciliation · a formal income statement (P&L) — though see §6.4, it's nearly free.

---

## 2. Guiding Principles

1. **Journal is the source of truth.** Ledgers and statements are projections, never authoritative stores.
2. **Posted is permanent.** Once posted, an entry is immutable. Corrections happen through reversing entries, never edits or deletes.
3. **Everything balances, always.** No entry can be posted unless total debits equal total credits.
4. **Deterministic derivation.** Given the same set of posted entries, every report is reproducible to the penny.
5. **Deployment-agnostic core.** The accounting logic knows nothing about HTTP, databases, or hosting.
6. **Fail closed, log everything.** Business-rule violations are expected and returned as structured errors; every state change is audited.

---

## 3. Architecture

A clean, layered (hexagonal / ports-and-adapters) design. Dependencies point inward — the Core depends on nothing.

```
┌─────────────────────────────────────────────────────────┐
│  Daybook.Accounting.Api        (ASP.NET Core, auth, DTOs) │  ← adapter
├─────────────────────────────────────────────────────────┤
│  Daybook.Accounting.Application (use cases, ports)        │
├─────────────────────────────────────────────────────────┤
│  Daybook.Accounting.Core        (domain — no dependencies)│  ← the engine
├─────────────────────────────────────────────────────────┤
│  Daybook.Accounting.Infrastructure (EF Core/SQLite, logs) │  ← adapter
└─────────────────────────────────────────────────────────┘
```

- **Core** — entities, value objects, invariants, and the derivation engine. Pure C#, no NuGet dependencies beyond the BCL. This is where "bulletproof" lives and where testing is heaviest.
- **Application** — orchestrates use cases (create draft, post, reverse, run report) and defines *ports* (interfaces like `IJournalStore`, `IClock`, `ICurrentUser`) that adapters implement.
- **Infrastructure** — EF Core + SQLite persistence, Serilog wiring, encryption. Implements the Application's ports.
- **Api** — thin ASP.NET Core service: authentication, request/response DTOs, validation, ProblemDetails errors. Can be swapped for a desktop host later without touching Core.

Because Core and Application carry the rules, you can later run the same engine embedded in a desktop app, or host it multi-tenant, by writing new adapters only.

### Suggested solution layout
```
Daybook.slnx
├─ src/
│  ├─ Daybook.Accounting.Core/
│  ├─ Daybook.Accounting.Application/
│  ├─ Daybook.Accounting.Infrastructure/
│  └─ Daybook.Accounting.Api/
└─ tests/
   ├─ Daybook.Accounting.Core.Tests/          (unit + property-based)
   ├─ Daybook.Accounting.Application.Tests/    (use-case tests)
   ├─ Daybook.Accounting.Infrastructure.Tests/ (real SQLite integration)
   └─ Daybook.Accounting.Api.Tests/            (contract/integration)
```

---

## 4. Domain Model

### 4.1 Book
A **Book** is one complete set of accounts (one household = one book, but the system supports many).

| Field | Notes |
|---|---|
| `BookId` | Stable GUID |
| `Name` | e.g. "Household" |
| `Basis` | `Cash` or `Accrual` — fixed per book |
| `BaseCurrency` | `USD` for v1; stored explicitly so it's not a hidden assumption |
| `FiscalYearStart` | Month/day; used by reports, no hard close in v1 |
| `Status` | `Open` / `Archived` |

### 4.2 Account (Chart of Accounts)
Hierarchical, five root types, each with an enforced **normal balance**.

| Type | Normal balance | Balance-sheet / statement role |
|---|---|---|
| Asset | Debit | Balance sheet |
| Liability | Credit | Balance sheet |
| Equity | Credit | Balance sheet |
| Income | Credit | Rolls into equity (see §6.4) |
| Expense | Debit | Rolls into equity (see §6.4) |

| Field | Notes |
|---|---|
| `AccountId` | Stable GUID |
| `Code` | **Optional** free-form human identifier (e.g. `1000`). No numbering scheme is imposed. When present it must be **unique within the book** (enforced) and is **indexed for lookup** — you can fetch an account by code as well as by id |
| `Name` | e.g. "Checking" |
| `Type` | One of the five roots |
| `ParentAccountId` | Nullable self-reference — the hierarchy lives here, **never in the name**. Enables sub-accounts to arbitrary depth (0, 1, or many children per node) |
| `IsPlaceholder` | Optional. `true` marks a **roll-up-only** node that rejects direct postings (e.g. a pure summary account). Default `false` — accounts are postable, including parents |
| `IsActive` | Inactive accounts reject new postings but keep history |
| `Tags` | Optional set of **tag-ids** (§4.8) classifying the account itself. Inherited **down** the tree and onto lines as *effective* tags — see §4.8 |

**Hierarchy rules (invariants):**
- **Type inheritance** — a sub-account must share its parent's root `Type` (and therefore normal balance). A child of an Asset is an Asset.
- **No cycles** — an account may not be its own ancestor; enforced on create and on reparent.
- **Reparenting allowed** — accounts are mutable config (not journal entries), so a subtree can be moved, subject to the type and no-cycle rules. An account with postings can't be deleted (deactivate instead), and children can't be orphaned.
- **Display path is derived, not stored** — a readable path like `Utilities:Electric` is computed from the structure for the CLI and reports, so you get the friendly string without the fragility of name-encoded hierarchy.

**Rule:** An account's `Type` (and thus normal balance) is immutable once any entry references it. Reclassification is a future migration feature, not an edit.

### 4.3 Journal Entry (the daybook record)
The atomic unit of record. A header plus two or more balanced lines.

| Field | Notes |
|---|---|
| `EntryId` | Stable GUID |
| `BookId` | Owning book |
| `SequenceNumber` | **Gapless**, per-book, assigned at *post* time (§7.3) |
| `EntryDate` | The accounting/effective date (may differ from posting time) |
| `Description` | Narrative memo. **Frozen at post** — corrected by reversal, not edit |
| `Status` | `Draft` → `Posted` → (referenced by a `Reversal`) |
| `PostedAtUtc` | Set at post |
| `PostedByUserId` | Acting user (§8) |
| `ReversesEntryId` | Non-null if this entry reverses another |
| `ReversedByEntryId` | Non-null once a reversal has been posted against it |
| `References` | Optional list of typed references (check number, ACH/wire confirmation, invoice, etc.) — see §4.3.1. Metadata only; never part of the balancing math |
| `Lines` | ≥ 2 journal lines |

#### 4.3.1 References (check numbers and the like)
A check number is identifying **metadata**, not accounting math — so it rides alongside the entry and never affects debits/credits. Rather than a bespoke field per instrument (which invites `WireReference`, `InvoiceNumber`, … sprawl), each reference is a typed pair:

- `Type` — extensible enum: `Check`, `ACH`, `Wire`, `Invoice`, `Receipt`, `Card`, `Other`.
- `Value` — the string identifier (e.g. `"1234"`).

An entry may carry several (a check paying an invoice has both a `Check` and an `Invoice` reference). References are **indexed for lookup/filter** (find entries by check #1234) and are **frozen on post** like everything else — a wrong check number on a posted entry is corrected by reversal, not edit.

**Uniqueness policy:** check numbers are usually unique within a bank account, but the engine does **not** hard-block duplicates by default (voids, reissues, and multiple checkbooks are all legitimate). Instead it **detects and warns** — posting an entry whose check reference already exists on that account returns an actionable warning (per §10), not an error. A **per-book option** can promote this to hard enforcement for those who want the rails.

*Deferred (not v1):* line-level references (for compound entries touching multiple instruments — the header covers the household case), and payees/vendors as first-class entities (a reporting feature, on the roadmap, not metadata).

### 4.4 Journal Line
| Field | Notes |
|---|---|
| `AccountId` | Target account |
| `Side` | `Debit` or `Credit` (explicit — no signed-amount ambiguity) |
| `Amount` | `Money`, always positive |
| `Memo` | Optional per-line note. **Frozen at post** — corrected by reversal, not edit |
| `Reconciliation` | `Unreconciled` / `Cleared` / `Reconciled`; when `Reconciled`, links to the `Reconciliation` record that cleared it (§4.5). Metadata only — see below |
| `Tags` | Set of **tag-ids** (§4.8) for cross-cutting classification — a second reporting axis alongside accounts. Metadata only; mutable after post |

Representing the side explicitly (rather than signed amounts) matches how accountants think and eliminates a whole class of sign-flip bugs.

**Reconciliation is a separate axis from posting status, and it lives on the line.** Posting status (`Draft`/`Posted`) governs existence and immutability; reconciliation governs whether a line has been verified against an external statement (bank, bill, etc.). They're orthogonal — a reconciled line is still a posted line. It's per-*line* because you reconcile a specific account against its statement: a transfer's two lines clear on different statements on different dates, and income/expense lines never appear on a bank statement at all. The small enum mirrors the familiar Quicken `c`/`R` distinction: `Cleared` = "seen at the bank," `Reconciled` = "locked in a completed statement reconciliation."

Crucially, **reconciliation state is mutable metadata that never touches accounting truth.** Marking a line reconciled — or un-reconciling it — changes no amount, account, or date, so it does *not* violate posted-entry immutability. Only `Posted` lines can be reconciled (never a `Draft`). All reconciliation state changes are **audit-logged** (§9), preserving the who/when/what trail. Reversing an entry whose line is already `Reconciled` raises an actionable warning (§10), since it affects a closed statement — allowed, never silent.

### 4.5 Reconciliation (statement record)
Reconciled lines link to a reconciliation session so the match is provable and re-openable — a loose flag isn't enough.

| Field | Notes |
|---|---|
| `ReconciliationId` | Stable GUID |
| `AccountId` | The account being reconciled |
| `StatementDate` | The external statement's date |
| `StatementEndingBalance` | `Money` — the statement's ending balance |
| `ReconciledAtUtc` / `ReconciledByUserId` | Audit stamps |
| `ClearedLines` | The set of journal lines cleared in this session |

**Guarantee:** the sum of a session's cleared lines reconciles the account to `StatementEndingBalance`. A session can be re-opened (un-reconciling its lines), which is permitted and audit-logged. *Elaborate reconciliation workflow/UX is a later layer; the data model lands in v1 so the core can support it from the start.*

### 4.6 Money (value object)
- Backed by `decimal` stored as `decimal(19,4)`. **Never `float`/`double`.**
- Carries a `Currency` code even though v1 only allows `USD` — so multi-currency is a data-model non-event later.
- Arithmetic is currency-checked: adding two `Money` of different currencies throws.
- Rounding is explicit (banker's rounding, `MidpointRounding.ToEven`) and applied only at defined boundaries, never silently mid-calculation.
- Immutable; value equality.

### 4.7 Draft vs Posted lifecycle
```
        create/edit/delete freely
Draft ───────────────────────────►  (still Draft)
  │
  │ Post  (validates §5, assigns SequenceNumber, stamps user+time)
  ▼
Posted ──────────── immutable ──────────►
  │
  │ Reverse (creates a NEW entry: same lines, sides flipped)
  ▼
Posted reversal entry  (original now ReversedByEntryId = reversal)
```

*Reconciliation is a **separate, orthogonal axis** (§4.4/§4.5) — not a stage in this lifecycle. A posted line moves `Unreconciled → Cleared → Reconciled` independently, and that movement never mutates the entry.*

### 4.8 Tags (cross-cutting classification)
Accounts are one classification tree; tags are a **second reporting axis** for cross-cutting dimensions that don't fit that tree — a trip, a project, "tax-deductible," "business vs. personal." (Quicken calls these tags; QuickBooks "classes"; Xero "tracking categories.") Category answers *what kind of expense*; tag answers *what it was for*.

Tags are first-class metadata like references, but with three deliberate differences:

- **Entity-backed, referenced by id.** A per-book `Tag` entity, and lines carry **tag-ids** — never tag strings. This prevents the typo/rename rot of free-string tags and is what keeps the key:value door open (below).
- **Line-level.** Tags sit on the line, not the header, so a compound entry can split across dimensions (one line "business," another "personal"). Tag reports sum the tagged lines on their normal side; tagging both sides of an entry would net to zero, so line-level naturally guides you to tag the meaningful flow. An entry-level "apply to all lines" convenience exists for the common case.
- **Mutable after post, audit-logged.** Reclassification ("that was actually business") is legitimate, so tag assignments on a posted line stay editable — and, like reconciliation, this never touches accounting truth and is fully audit-logged (§9). Referencing tags by id means renaming a tag never disturbs the immutable journal.

**v1 Tag entity:** `{ TagId, Name, IsArchived }`. `Name` is unique per book, case-insensitive, trimmed. Tags can be renamed, archived, and merged safely because lines hold ids.

**Tagging accounts + inheritance (derived, never copied).** Accounts can also carry tags (§4.2), classifying the account itself. These flow **down** the account tree and onto lines as *effective* tags, computed at read time — never stored on the line:

```
effective(line) = explicit line tags
                ∪ the line's account's own tags
                ∪ every ancestor account's tags
```

This mirrors the balance rollup in reverse: balances roll *up* the tree, tags flow *down* it — both derivations, neither duplicated. Because nothing is copied into the journal, **retagging an account is instant and non-destructive across all history** and never touches an immutable entry. Set semantics dedupe, so a line both under a `business` account and explicitly tagged `business` counts once. Reports operate on effective tags (§6.6); a UI can still distinguish explicit (line) tags from inherited (account/ancestor) ones.

*Reserved controls (v1 keeps it simple — all account tags inherit, no line-level opt-out):* a per-assignment **inheritable** flag (an account tag that organizes accounts without flowing to lines) and **line-level suppression/override** of an inherited tag. Both are easy additions later since effective-tags is already a computed layer.

**Reserved extension — key:value tags.** Grouped tags (`trip:hawaii`, `client:acme`) are the documented next step, kept reachable without a breaking change by three v1 choices: (1) lines store tag-ids, so grouping touches only the `Tag` table and reports, never lines; (2) uniqueness is built to *widen* from per-book `Name` to per-`(Group, Name)` — always a non-breaking relaxation; (3) `Group` (a dimension/namespace) is the named reserved field, added later as a nullable column plus that uniqueness widening — purely additive per §15.2. The v1 entity stays clean rather than shipping an always-null column; the door is open because nothing in v1 blocks it.

---

## 5. Invariants and Posting Rules

Enforced in Core, so no adapter can bypass them.

**On post:**
1. Entry has **≥ 2 lines**.
2. **Σ debits = Σ credits** exactly (to `decimal(19,4)`).
3. No line amount is zero or negative.
4. All referenced accounts exist, belong to this book, are **active**, and are **not placeholders** (a roll-up-only account rejects direct postings).
5. All line currencies equal the book's base currency (v1).
6. `EntryDate` is present; not in a locked period (no locks in v1, so always passes — the check exists so period-close drops in cleanly later).
7. On success: assign gapless `SequenceNumber`, set `Status=Posted`, `PostedAtUtc`, `PostedByUserId`.

**Immutability:** A `Posted` entry exposes no mutators. The Application layer never issues UPDATE/DELETE against posted rows; Infrastructure enforces this too (§7.4) as defense-in-depth.

**Two metadata classes.** Everything attached to an entry falls into one of two buckets, and the split is deliberate:
- **Frozen at post** — accounting data (accounts, sides, amounts, `EntryDate`), plus `Description`, line `Memo`, and `References`. Corrected only by reversal. This is the immutable journal.
- **Mutable after post (audit-logged, never touches accounting truth)** — `Tags` and `Reconciliation` state. These are ongoing classification/verification, so they stay editable with a full audit trail (§9).

**Reversal semantics:** Reversing entry copies the original's lines with Debit↔Credit swapped, links both directions (`ReversesEntryId` / `ReversedByEntryId`), and is itself a normal posted (immutable) entry. To "fix" an entry: post its reversal, then post a fresh corrected entry. The audit trail shows the whole story.

---

## 6. Derivation Engine (ledgers & reports)

All reads are computed from posted entries. Reversed entries and their reversals both remain in the journal and both count — they net to zero, which is the point.

### 6.1 Account balance
`balance(account) = Σ(debit lines) − Σ(credit lines)` for debit-normal accounts, and the negation for credit-normal accounts — presented on the account's normal side.

**Hierarchical rollup:** each node has an *own* balance (its direct postings) and a *rolled-up* balance (own + the sum of all descendants). The engine walks the subtree via `ParentAccountId`. Reports (trial balance, balance sheet) present rolled-up totals; a parent's register can show either its own lines or the whole subtree. Placeholder nodes have no own balance by construction — only rolled-up.

### 6.2 Trial balance
For every account, its debit-or-credit balance. **Invariant check:** total debits = total credits across the book; if not, the engine raises an integrity alarm (this should be impossible given §5, and the report asserts it anyway).

### 6.3 General ledger / account register
Per account: posted lines in `(EntryDate, SequenceNumber)` order with a running balance. This is the "account register" a household user reads day to day.

### 6.4 Balance sheet
`Assets = Liabilities + Equity`, where **Equity** = equity-account balances **+ current-period earnings**, and current-period earnings = `Σ Income − Σ Expense` over the fiscal period. Because the engine already computes that income/expense rollup, a formal **income statement is nearly free** to add later — it's the same numbers presented before they fold into equity.

### 6.5 Cash vs accrual
Both bases are double-entry; the difference is *which accounts you use and when*. Accrual books use receivable/payable accounts and may post accrual entries dated to the period they belong to; cash books post when money moves. The engine treats them uniformly; the `Basis` flag drives (a) report presentation and (b) optional validation guidance surfaced to the user. No separate code path in the core math.

### 6.6 Tag dimension
Tags (§4.8) give reports a **second axis** orthogonal to accounts. The engine filters or groups any report by one or more tags — operating on **effective tags** (a line's explicit tags ∪ its account's and ancestors' tags), so account-level classification is swept in automatically. Examples: total spend on the "Hawaii trip" across every account it touched, or a tag-by-account matrix. Because tags live on lines and account tags derive at read time, this is a group-by over the same posted-line fold; no separate store. When grouped key:value tags arrive, this extends to grouping by dimension (`trip`, `client`) additively.

---

## 7. Persistence

### 7.1 Store
EF Core over **SQLite**. Single-file DB fits a self-hosted household well and is trivially backed up. The repository interfaces live in Application; the EF implementation lives in Infrastructure, so Postgres (for "larger books") is later a new adapter, not a rewrite.

### 7.2 Schema sketch
`Books`, `Accounts`, `JournalEntries`, `JournalLines`, and (not yet built) an append-only `AuditLog`. Foreign keys enforced. `JournalLines` cascade-deletes with their entry at the DB level, but the delete path itself is only ever reachable for a still-`Draft` entry — `EfJournalStore` never issues a delete against an entry the database has recorded as `Posted`, so the practical effect matches "cascade only while `Draft`" even though SQLite has no way to express that condition declaratively on the FK itself.

### 7.3 Gapless per-book sequence
No separate `SequenceCounters` table, as first sketched above — built and confirmed with the user during M10 scoping. `Journal` is loaded and saved as a whole per book (mirroring `Book`/`ChartOfAccounts`, spec §7.1), so on load `Journal.Rehydrate` derives the next sequence number from `MAX(SequenceNumber)` across the entries just read, and `Post`/`Reverse` assign it in memory before a single `SaveAsync` writes the result back. SQLite's single-writer model plus whole-journal load/save serializes this safely without a dedicated counter row. A `SequenceCounters`-style row becomes worth reintroducing only if persistence ever moves away from whole-journal loading — e.g. for the larger-book/Postgres adapter on the roadmap (§17) — since that's when scanning all entries to find the max stops being cheap.

### 7.4 Append-only enforcement (defense-in-depth)
Domain forbids mutating posted entries. Infrastructure's guard is structural, not a status check that could be skipped: `EfJournalStore.SaveAsync` computes which entry ids are safe to write (new, or still `Draft` in the database) and every write path — entry upsert, entry delete, line rewrite — is restricted to that set, so no code path can issue UPDATE or DELETE against a row the database already has as `Posted`. SQLite triggers remain optional/deferred, as originally noted.

### 7.5 Concurrency & migrations
Optimistic concurrency tokens on mutable rows (drafts, accounts). EF Core migrations checked into source control; every schema change ships with a migration and a test that applies it to a fresh DB.

---

## 8. Security & Privacy

Financial data is highly sensitive, so this is treated as a first-class requirement even for a single household.

**AuthN/AuthZ**
- ASP.NET Core Identity (or signed API keys for machine callers) over the self-hosted service.
- Per-book roles: `Owner`, `Editor`, `Viewer`. Posting/reversing requires Editor+; reports require Viewer+.
- **v1 posture:** effectively single-user — one posting user in practice. The role model is kept in the design so multi-user is a later config change, not a schema/logic change, but v1 doesn't build out multi-user management UI or flows.
- Every request carries an authenticated principal; the acting user is stamped on every posted entry.

**Data protection**
- **In transit:** TLS required; HTTP disabled or redirected.
- **At rest:** encrypted SQLite (SQLCipher via `Microsoft.Data.Sqlite` bundle). The database key is derived from a passphrase via a KDF and protected with envelope encryption — never hard-coded, never in source. Full custody design is in §13.5.
- **Local-first & no telemetry:** the service makes no outbound calls by default. Nothing leaves the household's machine.

**Privacy posture**
- Data minimization — store only what the ledger needs.
- User-controlled **export** (full journal to CSV/JSON) and **delete-book**, so the household owns its data.
- Sensitive fields (amounts, memos, account names) are **redacted in general application logs** (§9); only the dedicated audit sink records posting facts, and access to it is controlled.
- Backups: documented, encrypted-at-rest DB file copy; a restore test is part of the suite.

---

## 9. Logging & Observability

Two deliberately separate streams (Serilog):

1. **Application log** — structured JSON, rolling file + console. Levels: `Debug` (dev), `Information` (state transitions), `Warning` (recoverable), `Error` (faults). Every request gets a **correlation ID** propagated Api → Application → domain events. Sensitive values are redacted here.
2. **Audit log** — an append-only sink (dedicated table/file) capturing *who did what, when* for every **post** and **reverse**: `EntryId`, `SequenceNumber`, acting user, UTC timestamp, before/after status, correlation ID. This is the operational complement to the immutable journal and is never redacted, but is access-controlled.

Structured properties (not string-interpolated messages) so logs are queryable. Health/readiness endpoint and basic metrics counters (entries posted, reversals, report runs) for observability.

---

## 10. Error Handling & API Contract

**Error model** — expected, business-level failures (unbalanced entry, inactive account, closed book) are returned as a **`Result<T>`** from Core/Application, *not* thrown. Exceptions are reserved for truly exceptional infrastructure faults. This keeps the happy path clean and makes every rule violation testable.

**Error quality standard — no dead ends.** Every error must be *meaningful and actionable*. A bare "an error occurred" is a defect, not an acceptable outcome. Each error carries:

- a stable machine-readable `code` (e.g. `entry.unbalanced`);
- a human-readable message stating **what** happened and **why**, in plain language;
- one or more **recovery options** telling the caller/user what they can actually do about it;
- a `correlationId` tying the response back to the logs (§9).

The wire format is RFC 7807 `ProblemDetails` extended with:

| Field | Purpose |
|---|---|
| `code` | Stable rule/fault identifier |
| `title` / `detail` | What went wrong, in human terms |
| `field` | The specific input at fault, for validation errors |
| `recovery` | Array of `{ action, description, hint }` so a front end can render **real affordances** (a button, a link, a retry), not just prose |
| `correlationId` | Trace handle into the audit/app logs |

Errors are categorized so recovery can be specific:

- **Validation** (fixable input) — name the offending `field` and the constraint. Example: an unbalanced entry returns the debit/credit totals *and their difference* so the fix is obvious.
- **Business-rule violation** — state the rule and how to comply (inactive account → "reactivate the account or choose another"; closed period → "reopen the period or change the entry date").
- **Conflict / idempotency** — explain the collision and offer the safe next step.
- **Auth** — say whether it's authentication vs authorization, and what access is needed.
- **Infrastructure fault** — the user sees a safe, non-leaky message *plus* a `correlationId` and a concrete next step (retry, or contact with that id); full detail goes to the logs, never the response. Even here there is no dead end.

Because errors are part of the API contract, each rule ships with a test asserting its `code`, category, and that a usable `recovery` payload is present (§11).

**API surface (v1, REST)**

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/books` | Create a book |
| `GET` | `/books/{id}` | Book details |
| `POST` | `/books/{id}/accounts` | Add account |
| `GET` | `/books/{id}/accounts` | Chart of accounts |
| `POST` | `/books/{id}/entries` | Create **draft** entry |
| `PUT` | `/books/{id}/entries/{eid}` | Edit draft (drafts only) |
| `POST` | `/books/{id}/entries/{eid}/post` | Post (validates §5) |
| `POST` | `/books/{id}/entries/{eid}/reverse` | Post a reversal |
| `GET` | `/books/{id}/entries` | Journal listing (filter by date/account) |
| `GET` | `/books/{id}/reports/trial-balance` | Trial balance |
| `GET` | `/books/{id}/reports/ledger/{accountId}` | Account register |
| `GET` | `/books/{id}/reports/balance-sheet` | Balance sheet |

- **Idempotency:** `post` and `reverse` accept an idempotency key so a retried request can't double-post.
- **Errors:** RFC 7807 `ProblemDetails` with a stable machine-readable `code` per rule (e.g. `entry.unbalanced`, `account.inactive`).
- **Validation:** two tiers — shape validation at the API (FluentValidation) and rule validation in Core.

---

## 11. Testing Strategy (TDD)

Testing is treated as HIGH importance and written test-first.

- **Framework:** xUnit + **AwesomeAssertions** (the free, Apache-licensed FluentAssertions fork — FluentAssertions itself is commercially licensed from v8 on, so it's deliberately not used). Property-based tests via **CsCheck** for invariants (pure C#, no FSharp.Core transitive dependency, unlike FsCheck).
- **Test-first flow:** red → green → refactor. Domain rules (§5) get their failing test before their implementation.
- **Determinism:** inject `IClock` and `ICurrentUser` so time and identity are controllable in tests — no wall-clock or ambient state.
- **Test pyramid:**
  - *Core unit tests* — every invariant and derivation rule; aim for near-total coverage of domain logic. Data built via test-data builders (e.g. `AnEntry.WithLines(...)`).
  - *Property-based* — e.g. "any posted entry has Σdebits = Σcredits"; "reversing an entry and summing the pair yields zero for every account"; "trial balance always balances."
  - *Application tests* — use cases against in-memory/fake ports.
  - *Infrastructure tests* — against a **real SQLite** file (temp DB per test) to prove EF mappings, gapless sequence, and append-only enforcement.
  - *Api tests* — `WebApplicationFactory` for full request/response contract, auth, and ProblemDetails shapes.
- **CI gate:** all tests green + coverage threshold on Core before merge.

---

## 12. Reporting Output & Toner-Friendliness

Reports are produced as **data first** (JSON), with presenters that render printable views. **v1 ships two presenters: HTML and Markdown** (PDF deferred). Markdown is inherently toner-friendly, is the natural output for the CLI (pipeable, terminal-readable), diffs cleanly in version control, and converts onward to HTML/PDF/Word via pandoc. Print rules (apply to the HTML/print presenter):
- Black text on white; **no** dark headers, shaded bands, or filled backgrounds.
- Thin hairline rules only where needed; whitespace over ink for separation.
- Right-aligned, monospaced-figure numerals; clear subtotal/total lines.
- Standard system fonts; no heavy weights across large areas.
- Fits standard Letter with sane margins.

(If you later want polished PDFs or Word versions of these, they can render from the same report data.)

---

## 13. Cross-Platform, Containerization & Key Custody

### 13.1 Target framework & portability
- Target **.NET 10 (LTS)** — the current long-term-support release, supported into November 2028. It is Linux-first and fully cross-platform.
- **Core** and **Application** are pure managed code with zero OS dependencies, so they're portable by construction. The *only* native dependency is the encrypted-SQLite provider, pinned per runtime identifier (`linux-x64`, `linux-arm64`).

### 13.2 Container image (one image, two engines)
- A single multi-stage **OCI** image built from a `Containerfile`, which builds identically with `docker build` and `podman build`.
- Build stage on the SDK image (`mcr.microsoft.com/dotnet/sdk:10.0`); runtime stage on the ASP.NET image (`mcr.microsoft.com/dotnet/aspnet:10.0`).
- The image runs **unmodified on both Docker and Podman** because it's standard OCI — there is no engine-specific build.
- The SQLite file lives on a **mounted volume**, never baked into the image, so upgrades never touch data.

### 13.3 Docker run recipe
- `docker compose` providing:
  - a `secrets:` entry mounting the passphrase at `/run/secrets/db_passphrase`,
  - a named volume for the SQLite file,
  - TLS and port configuration.
- Plain `docker run` lacks first-class runtime secrets outside Swarm/compose, so **compose is the supported Docker path**.

### 13.4 Podman run recipe
- `podman secret create db_passphrase …`, then `podman run --secret db_passphrase,type=mount …`; **or**
- a `podman kube play` manifest or a Quadlet `.container` unit for systemd-managed startup.
- **Rootless Podman is recommended** (no root daemon = smaller attack surface). Map the data volume with correct user-namespace ownership (e.g. `:U` or `--userns=keep-id`) so the container user can write the SQLite file.

### 13.5 Key custody (resolves the earlier open question)
- The passphrase is an **input, never the key**. At startup the app reads it from a fixed file path (`/run/secrets/db_passphrase`), so the *source* is swappable without code changes. **Built**: `PassphraseFile.Read`.
- Derive the data-encryption key with **Argon2id** (a KDF) — the raw passphrase is never used directly as the key. **Built**: `Argon2Kdf` (128 MiB / 3 iterations / p=1 — OWASP's cited "enhanced" profile, not their bare minimum, since this KDF gates a household's entire financial history and runs rarely).
- Use **envelope encryption**: the passphrase-derived key-encryption-key wraps a stored data-encryption-key. Rotating the passphrase re-wraps the stored key — **no full-database re-encryption**. **Built**: `WrappedDataKey`/`DataKeyEnvelope` (AES-GCM).
- **Never** place the passphrase in an environment variable — it leaks via `inspect`, `/proc`, crash dumps, and logs.
- **Upgrade path:** because the app only reads a file path, swapping in an external secret store (Vault, cloud KMS) later for hosted/larger books is a deployment change, not a code change.
- **SQLCipher provider status (2026-07-19): the free prebuilt path assumed by §8 is gone.** `SQLitePCLRaw.bundle_e_sqlcipher` (and `bundle_e_sqlite3mc`) are deprecated upstream as of SQLitePCLRaw 3.0 — the maintainer stopped distributing free prebuilt encrypted-SQLite binaries; both are frozen at 2.1.11 (March 2025) with an explicit "no longer maintained" notice. The actual SQLite-provider swap is deliberately deferred until a path is chosen among: pin the deprecated bundle anyway (free, unmaintained), pay for Zetetic's commercial SQLCipher-for-.NET package (maintained, real cost), or build SQLCipher/SQLite3MC from source (free, real ongoing cross-platform native-build burden). Everything else in this section — KDF, envelope encryption, passphrase-file handling — has no native dependency and is built and tested regardless of which path gets picked.

---

## 14. Licensing & Open-Core Boundary

**Intent:** the API and a basic CLI are open source; GUI/TUI front ends may be paid (undecided).

### 14.1 The API is the open/closed seam
- Everything through the public interface is open source: **Core, Application, Infrastructure, the ASP.NET host, and the CLI**.
- Front ends consume the **public HTTP API or the CLI as clients** — they never compile against or link the engine internals. This keeps the boundary clean both technically and legally, and lets front ends ship on their own cadence and license.
- The open CLI does double duty: it's the **reference client** and a living contract test that keeps the public API honest and pleasant to build on.

### 14.2 License for the open core (leaning AGPL-3.0)
- As sole copyright holder, **you can build closed, paid front ends regardless of the core's license** — a license binds everyone *except* the owner. So the real question is what *others* may do with the engine, not whether you can monetize a GUI.
- Candidate licenses:
  - **Apache-2.0** — permissive, with an explicit patent grant. Maximizes adoption; anyone (including commercial competitors) may build on it.
  - **AGPL-3.0** — strong network copyleft; the classic open-core lever. The engine stays free, while anyone wanting to embed it in a closed product is nudged toward a commercial license from you (dual-licensing is yours to grant since you own the copyright). A separate-process front end talking over HTTP generally stays independent even under AGPL.
- Whether front ends are free or paid is a **separate, independent** decision and can stay undecided without blocking anything.
- **Current direction: AGPL-3.0** for the open core, keeping dual-licensing in reserve. (Reminder that the separate-process-front-end independence under AGPL is the general reading, not a guarantee — worth a lawyer's eye before you rely on it commercially.)

### 14.3 Contributions
- Because dual-licensing is part of the plan, prefer a **CLA** over a bare DCO: a DCO only certifies a contributor had the right to submit under the project license, whereas a CLA grants **you** the rights to relicense — without it, contributors' copyrights can quietly foreclose the ability to dual-license their code later. The tradeoff is a little more contributor friction; for an open-core project that friction is usually worth it.
- *(Not legal advice — this is to help you choose, not a substitute for counsel.)*

---

## 15. Versioning & Journal Durability

### 15.1 Four independent version axes
Treat these as separate contracts that move at different speeds — conflating them is what makes breaking changes feel dangerous:

1. **HTTP API contract** — what clients depend on.
2. **Journal / entry-schema** — how each entry is serialized on disk.
3. **Domain model** — accounting semantics.
4. **Database schema** — EF Core migrations.

The API can go v1 → v2 → v3 with breaking client changes while the journal format only ever *extends*. The journal is deliberately the narrowest, slowest-moving, most conservative contract in the system.

### 15.2 API versioning (strict SemVer)
- **SemVer** on the public HTTP contract; only the **major** appears in the URL (`/v1/...`). Minor/patch are backward-compatible by definition and must not move clients.
- The exact version is echoed in an `API-Version` response header and in the OpenAPI document.
- **Additive-only within a major:** new optional fields and endpoints are allowed; never remove, rename, or repurpose a field. Error `code`s are part of the contract too.
- **Deprecation** via `Deprecation` / `Sunset` headers (RFC 8594) with a documented overlap window where the old and new majors run side by side.
- **CI gate:** diff each release's OpenAPI against the previous (e.g. `oasdiff`); an unintended breaking change fails the build.
- The **CLI** carries its own SemVer, pinned to the API major(s) it supports.
- **Commitments:** the public API starts at **`v1` from the first release**. When a new major ships, the previous major runs alongside it for a **12-month deprecation-overlap window** (announced via `Deprecation`/`Sunset` headers) before sunset. Twelve months suits a system-of-record that people upgrade infrequently; it's a policy knob, easily lengthened.

### 15.3 Journal durability through change (event-sourcing playbook)
Because entries are immutable and append-only, the truth is protected by evolving *interpretation*, not stored data:

- **Version-stamp every entry** at write time; an entry written under schema v1 stays v1 on disk forever. **Built**: `JournalEntry.SchemaVersion` (persisted on `JournalEntries.SchemaVersion`) is stamped once at draft creation and carried forward unchanged through every later operation — edits, posting, reversal, and reload never re-stamp it.
- **Upcast on read, never rewrite on disk.** Current code transforms older entries in-memory to the current model via an upcaster pipeline. Stored bytes are preserved. **Deliberately deferred**: schema v1 is still the only version that has ever existed, so there is nothing to upcast *from* yet — building dispatch machinery for a hypothetical v2 now would be speculative. Add the upcaster pipeline when a real v2 lands, not before.
- If a change is genuinely irreconcilable, **write a new correcting entry** — never edit history. This is the reversing-entry philosophy (§5) applied to schema evolution.
- The **versioned export format** (§8) is the ultimate backstop: the journal's logical truth stays recoverable even if the storage engine is swapped wholesale.
- **"Golden journal" regression fixture** — a corpus of entries from every historical schema version, with a test asserting they still read and still derive *identical* reports under current code. This test **is** the durability guarantee (§11). **Built** (`GoldenJournalTests.cs`): today's corpus is the v1 case only, built through the real public API (there being no second version yet to source a checked-in historical fixture from) and asserted against hard-coded `TrialBalance`/`BalanceSheet` values. When a real v2 lands, this v1 case is frozen as-is and a v2 counterpart is added alongside it, never replacing it.
- **Tamper-evidence via an anchored hash chain (in v1).** Each posted entry stores `hash(canonical_content + previous_entry_hash)`, following the gapless sequence, so any alteration or removal of history is detectable. Built in from day one because retrofitting a chain onto an existing journal is awkward and weakens the guarantee for early data; the marginal cost is low since canonical serialization is already required for export and golden-journal tests. The chain is **anchored** so it resists a database-level attacker (who could otherwise re-chain from an altered entry forward): key it with an **HMAC using the passphrase-derived key** (which lives outside the DB, §13.5), and emit a signed **head hash** alongside backups. *Lean fallback if v1 scope tightens:* store the chain fields from day one but defer the verification/anchoring tooling — the data is still chained from entry #1.

---

## 16. Extension Model / Secondary Services

Ingestion, scheduling, budgeting, reconciliation, and any future service follow **one rule**, decided once:

- **Logic lives in a library/module** — pure, isolated, unit-testable (e.g. ingestion = parse OFX/CSV/QIF, map, deduplicate).
- **Writes go only through the Application/API layer**, producing *draft* entries via the same posting pipeline as everything else. No secondary service touches the journal store directly — the journal keeps **one guarded write door**, so every invariant, audit record, and version stamp applies regardless of caller.
- **In-process module vs standalone worker is a deployment choice, not an architecture choice.** Because services integrate only through the public ports, you embed now and split out later with zero domain changes.
- Net shape: a **modular monolith today, microservices-optional later** — the right fit for a solo developer.

Applied to the deferred services:
- **Ingestion** — library for the transform; creates draft entries through the API; deployment deferred.
- **Scheduling** — on a trigger, calls the same create-draft / post endpoints.
- **Budgeting** — reads derived data via the API and keeps its own budget store; never bypasses the journal.

---

## 17. Roadmap (post-v1)

Scheduled/recurring transactions · budgeting/envelopes · formal income statement · period close & closing entries to retained earnings · multi-currency + FX gain/loss · attachments (receipts) · bank import & reconciliation · payees/vendors as first-class entities · line-level references · grouped key:value tags (tag dimensions) · Postgres adapter for larger books · multi-tenancy.

---

## 18. Decisions Log

| # | Decision | Rationale |
|---|---|---|
| 1 | Journal-central, ledgers derived | Matches traditional accounting; single source of truth |
| 2 | Posted entries immutable; fix via reversal | Standard GAAP audit-trail integrity |
| 3 | Deployment-agnostic Core + self-hosted service | Household now, larger/hosted later without engine rewrite |
| 4 | C# / .NET, SQLite/EF Core | Chosen stack; SQLite ideal for self-hosted household |
| 5 | Per-book cash **or** accrual | Flexibility without branching the core math |
| 6 | USD only, but currency-aware Money | Multi-currency becomes additive later |
| 7 | v1 reports: trial balance, register, balance sheet | Requested set; P&L nearly free later |
| 8 | Explicit Debit/Credit sides, `decimal(19,4)` Money | Eliminates sign bugs and float error |
| 9 | Target **.NET 10 (LTS)** | Current LTS, supported to Nov 2028; Linux-first |
| 10 | One OCI image for **Docker + Podman** | Single build runs on both engines; rootless Podman preferred |
| 11 | Passphrase → Argon2id → envelope encryption, secret-as-file | Strong at-rest protection without running a secret manager; rotation-friendly; external store drops in later |
| 12 | API is the open/closed seam; front ends are API/CLI clients | Clean open-core split; CLI doubles as reference client + contract test |
| 13 | Open-core license: **leaning AGPL-3.0**; **CLA** for contributions | AGPL is the open-core lever; owner can sell closed front ends regardless; CLA preserves dual-licensing |
| 14 | Errors must be meaningful + carry recovery options | No dead-end "an error occurred"; errors are part of the tested contract |
| 15 | Strict SemVer on API; major in URL, additive-only within a major | Predictable contract; CI diff blocks accidental breaks |
| 16 | Four independent version axes (API / journal / domain / DB) | Lets the API break while the journal only extends |
| 17 | Journal durability via version-stamp + upcast-on-read + golden-journal tests | Truth preserved byte-for-byte through change; history never rewritten |
| 18 | Secondary services = library logic + writes through the API, deployment-flexible | One guarded write door; modular monolith now, split later |
| 19 | Account codes optional; unique-when-present (enforced) + indexed for lookup | No scheme imposed; suits a household; still safe if used |
| 20 | v1 effectively single-user; role model retained | Multi-user becomes a config change later, not a rewrite |
| 21 | Reports render **HTML + Markdown** in v1 (PDF deferred) | Markdown is toner-friendly, CLI-native, diffable, pandoc-convertible |
| 22 | API starts at **v1**; **12-month** deprecation-overlap window | Predictable, generous for a system-of-record; policy knob |
| 23 | **Anchored hash chain in v1** (HMAC + signed head) | Tamper-evidence from entry #1; cheap when built early; resists DB-level tampering |
| 24 | Accounts nest structurally via `ParentAccountId` (not name-encoded); postable by default with optional placeholder flag | Referential integrity, type inheritance, rollup; derived display path gives the readable string safely |
| 25 | Check numbers etc. modeled as typed `{Type, Value}` **references** on the entry header; detect-and-warn on duplicates (optional per-book enforcement) | Metadata not math; one model for all instrument ids; no field sprawl; respects legitimate reuse |
| 26 | Reconciliation is a per-**line** state (`Unreconciled`/`Cleared`/`Reconciled`) on a separate axis from posting status, backed by a statement record | Reconcile is per-account vs a statement; mutable metadata that never breaks entry immutability; provable + audit-logged |
| 27 | Tags = per-book **entity**, referenced by **id** on lines; line-level, mutable-after-post, audit-logged; flat in v1 with `Group` reserved for key:value | Second reporting axis; id-based storage keeps key:value reachable non-breakingly; no free-string rot |
| 28 | Accounts can be tagged; inheritance is **derived** effective tags (line ∪ account ∪ ancestors), never copied into the journal | Retagging is instant + non-destructive; mirrors balance rollup; simple v1 with inheritance controls reserved |

---

## 19. Open Questions for You

The initial set is now **resolved** — see Decisions Log rows 13, 19–23:

1. Account codes → optional, unique-when-present, indexed (row 19).
2. Multi-user → single-user v1, role model retained (row 20).
3. Entry date vs posting order → confirmed `(EntryDate, SequenceNumber)`.
4. Report delivery → HTML + Markdown in v1, PDF deferred (row 21).
5. Open-core license → leaning AGPL-3.0, CLA for contributions (row 13).
6. Versioning → start at `v1`, 12-month overlap window; anchored hash chain in v1 (rows 22–23).

*(Encryption key custody was resolved earlier in §13.5.)*

No open questions remain blocking. This section stands ready for any that surface during implementation.
