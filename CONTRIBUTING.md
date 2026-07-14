# Contributing to Daybook

Thanks for your interest. Daybook is an accounting *system of record*, so correctness and a
clean audit trail matter more than speed. Please read this before opening a PR.

## How we work

- **Small vertical slices, test-first (TDD).** Red → green → refactor. Write the failing
  test, watch it fail for the right reason, make it pass, then clean up. Production code
  without a failing test demanding it is out of scope.
- **Stop at milestone boundaries.** Work is organised into
  [milestones](https://github.com/rmwarriner/daybook/milestones); don't build ahead of the
  current one.
- **Many small, focused commits** with clear messages over large ones.
- When a decision isn't covered by the design spec or `CLAUDE.md`, open a
  [Discussion](https://github.com/rmwarriner/daybook/discussions) or an issue rather than
  guessing.

The authoritative rules for working in this repo live in [`CLAUDE.md`](CLAUDE.md); the full
rationale is in [`docs/design-spec.md`](docs/design-spec.md).

## Ground rules (non-negotiable)

1. **Double-entry always balances** — enforced in Core.
2. **Posted entries are immutable** — corrections are reversing entries, never edits/deletes.
3. **Money is never a float** — use the `Money` value object.
4. **Core stays dependency-free** — don't add a NuGet package to `Daybook.Accounting.Core`.
5. **No dead-end errors** — every error carries a stable `code`, a plain message, recovery
   options, and a correlation id.

## Building and testing

```bash
dotnet restore Daybook.slnx
dotnet build   Daybook.slnx      # warnings are errors
dotnet test    Daybook.slnx
dotnet format  Daybook.slnx      # required to pass CI
```

- **Core** gets near-total coverage; every invariant gets a test.
- Use **property-based tests** (CsCheck) for algebraic/invariant properties.
- Inject `IClock`/`ICurrentUser`; no wall-clock or ambient state in domain code.
- Use **AwesomeAssertions**, not FluentAssertions ≥ 8 (commercially licensed).

## Pull requests

- Branch from `main`; keep PRs focused on one slice.
- Ensure `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes` all pass.
- Link the issue(s) the PR closes and note which milestone it belongs to.
- Fill out the PR template.

## Contributor License Agreement (CLA)

Because Daybook is open-core and the author keeps dual-licensing in reserve, contributions
require agreeing to a **CLA** that grants the author the rights needed to relicense
contributed code. Until the CLA process is automated, state in your PR that you agree to
license your contribution under the project license **and** grant the maintainer the right
to relicense it. (This is not legal advice.)

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating you
are expected to uphold it.
