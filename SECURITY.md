# Security Policy

`Yort.Eftpos.SmartConnect` is an **unofficial, community-maintained** .NET client library for the
SmartPay / Shift4 SmartConnect EFTPOS API. It is not affiliated with, endorsed by, or supported by
Shift4 or SmartPay.

## Scope — what to report where

- **A vulnerability in *this library*** (e.g. a way it could mishandle a credential, log a bearer
  token, corrupt transaction-recovery state, or misclassify a payment outcome): report it here, using
  the process below.
- **A vulnerability in the SmartConnect *service*, a payment *terminal*, or your merchant account**:
  that is **not** this project — contact Shift4 / SmartPay directly. Do not report it here.

## Reporting a vulnerability

Please report security issues **privately** — do not open a public GitHub issue, and do not include
real credentials, merchant identifiers, bearer tokens, or captured transaction data in your report.

Use GitHub's private vulnerability reporting: on the repository's **Security** tab, choose
**"Report a vulnerability"**. This opens a private advisory visible only to the maintainers.

In your report, please include:

- The affected version(s) / commit.
- A description of the issue and its impact (what an attacker could do, and what access they need).
- Steps to reproduce, ideally with a minimal repro — using **synthetic** data only.

## What to expect

This is a volunteer-maintained project, so responses are best-effort rather than on a guaranteed SLA.
We aim to acknowledge a report within a few days, agree an assessment and, where a fix is warranted,
coordinate a fixed release and a disclosure timeline with you before any public write-up.

## Supported versions

The project is pre-1.0; only the latest published pre-release is supported. Fixes are made against
the current `main` and released as a new package version rather than back-ported.
