# Daybook

[![CI](https://github.com/rmwarriner/daybook/actions/workflows/ci.yml/badge.svg)](https://github.com/rmwarriner/daybook/actions/workflows/ci.yml)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)

**Daybook** is a journal-central, double-entry accounting engine and self-hosted API.
The journal (the *daybook*) is the single source of truth; ledgers, trial balance, and
statements are all *derived* from it by folding over posted entries — exactly as in
traditional accounting. If the journal is correct, everything downstream is correct by
construction.

The primary user is a single household, but the core stays flexible for larger books.
It is open source (AGPL-3.0), self-hosted, and container-friendly.

> **Status: core engine and persistence complete; no API or CLI yet.** The domain model
> (books, chart of accounts, the immutable journal with draft → post → reverse and
> gapless per-book sequencing), the derivation engine (trial balance, account register,
> balance sheet) with HTML + Markdown reports, and EF Core/SQLite persistence for all of
> the above — including append-only enforcement of posted entries — are built and fully
> tested (300+ tests, 0 warnings). The HTTP API, CLI, authentication, encryption-at-rest,
> and audit logging are not yet started. See the
> [milestones](https://github.com/rmwarriner/daybook/milestones) and
> [issues](https://github.com/rmwarriner/daybook/issues) for what's next.

## Design principles

1. **Journal is the source of truth.** Ledgers and statements are projections, never
   authoritative stores.
2. **Posted is permanent.** Once posted, an entry is immutable. Corrections happen through
   reversing entries, never edits or deletes.
3. **Everything balances, always.** No entry posts unless total debits equal total credits.
4. **Money is never a float.** A `decimal(19,4)` value object with explicit banker's
   rounding at defined boundaries only.
5. **Deployment-agnostic core.** The accounting logic knows nothing about HTTP, databases,
   or hosting.
6. **Fail closed, log everything.** Business-rule violations are expected, structured, and
   actionable; every state change is audited.

## Architecture

A clean, layered (hexagonal / ports-and-adapters) design. Dependencies point inward — the
Core depends on nothing but the BCL.

```
Daybook.Accounting.Api            (ASP.NET Core, auth, DTOs)        ← adapter
Daybook.Accounting.Application    (use cases, ports/interfaces)
Daybook.Accounting.Core           (domain — ZERO external deps)     ← the engine
Daybook.Accounting.Infrastructure (EF Core/SQLite, Serilog, crypto) ← adapter
```

```
src/    Daybook.Accounting.{Core,Application,Infrastructure,Api}
tests/  Daybook.Accounting.{Core,Application,Infrastructure,Api}.Tests
```

## Tech stack

- **.NET 10 (LTS)**, C# — `Nullable` on, warnings-as-errors, deterministic builds.
- **Testing:** xUnit + [AwesomeAssertions](https://github.com/AwesomeAssertions/AwesomeAssertions)
  (the free Apache-licensed FluentAssertions fork) + [CsCheck](https://github.com/AnthonyLloyd/CsCheck)
  for property-based tests.
- **Persistence:** EF Core + SQLite (SQLCipher for encryption at rest).
- **Logging:** Serilog (structured), with a separate audit sink.

## Build & test

```bash
dotnet restore Daybook.slnx
dotnet build   Daybook.slnx
dotnet test    Daybook.slnx
dotnet format  Daybook.slnx   # keep it clean before committing
```

Requires the .NET 10 SDK (see [`global.json`](global.json)).

## Documentation

- [`docs/design-spec.md`](docs/design-spec.md) — the full v1 design specification (the *why*).
- [`CLAUDE.md`](CLAUDE.md) — the day-to-day operating rules for working in this repo.
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — how to contribute (TDD rhythm, CLA, conventions).
- [`SECURITY.md`](SECURITY.md) — how to report a vulnerability.

## Roadmap

v1 is the core engine plus a self-hosted service and a basic CLI. Post-v1 ideas
(scheduled transactions, budgeting, multi-currency, bank import, and more) are tracked as
[issues labelled `scope: post-v1`](https://github.com/rmwarriner/daybook/issues?q=is%3Aissue+label%3A%22scope%3A+post-v1%22).

## License

[AGPL-3.0](LICENSE). As the sole copyright holder, the author reserves the right to offer
the engine under a separate commercial license (dual-licensing). See
[`CONTRIBUTING.md`](CONTRIBUTING.md) for what that means for contributions.
