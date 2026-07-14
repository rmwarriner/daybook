# Security Policy

Daybook handles sensitive financial data. Security is treated as a first-class requirement
even for a single-household deployment.

## Reporting a vulnerability

**Please do not open a public issue for security vulnerabilities.**

Report privately using GitHub's
[**Private vulnerability reporting**](https://github.com/rmwarriner/daybook/security/advisories/new)
(Security → Advisories → *Report a vulnerability*). Include:

- a description of the issue and its impact,
- steps to reproduce or a proof of concept,
- affected version/commit,
- any suggested remediation.

You can expect an initial acknowledgement within a few days. Please allow a reasonable
period for a fix before any public disclosure.

## Supported versions

The project is in early development; only the latest `main` is supported until a tagged
release exists.

| Version | Supported |
|---------|-----------|
| `main`  | ✅        |

## Security posture (by design)

- **Local-first, no telemetry** — the service makes no outbound calls by default.
- **In transit:** TLS required; plain HTTP disabled or redirected.
- **At rest:** encrypted SQLite (SQLCipher); the key is derived from a passphrase via
  Argon2id and never hard-coded or placed in an environment variable.
- **Tamper-evidence:** posted entries are chained with an HMAC-anchored hash chain.
- **Secrets** are read from a file path (e.g. `/run/secrets/db_passphrase`), never from
  source or env vars.

See [`docs/design-spec.md`](docs/design-spec.md) §8 and §13.5 for the full design.
