# Contributing

Thanks for your interest in improving `Yort.Eftpos.SmartConnect` — an unofficial .NET client for the
SmartPay / Shift4 SmartConnect EFTPOS API. Bug reports, fixes, tests, and documentation are all welcome.

> This library moves real money. Correctness and diagnosability come first: prefer a clear, well-tested
> change over a clever one, and never weaken the transaction-recovery or payment-outcome contracts
> without a very good reason and a test that pins the new behaviour.

## Prerequisites

- **.NET 8 SDK** (the repository pins the C# language version to match; `latest` is intentionally not used).
- **Windows** is required to build and test the full solution — the WinForms package targets `net48` and
  `net8.0-windows`. The core library itself is `netstandard2.0` and is portable, but the solution build,
  the test suite, and CI all run on Windows.

## Build and test

```
dotnet restore Yort.Eftpos.SmartConnect.sln
dotnet build   Yort.Eftpos.SmartConnect.sln -c Release
dotnet test    Yort.Eftpos.SmartConnect.sln -c Release
```

CI (GitHub Actions) runs build + test + `dotnet pack` on every pull request; a PR must be green to merge.
Warnings are treated as errors, so a clean local build is a clean CI build.

To eyeball the WinForms dialogs without a real terminal, run the **`Yort.Eftpos.SmartConnect.WinFormsGallery`**
sample — it drives every dialog visual state with synthetic data (no client, no network).

## Coding conventions

These are enforced by `.editorconfig` and `Directory.Build.props`; please don't fight them:

- **Tabs** for indentation in C# files; file-scoped namespaces; Allman braces; braces always, even on
  single-statement bodies; `using` directives outside the namespace, `System` first.
- **Nullable reference types** enabled; keep the public API `netstandard2.0`-expressible.
- **XML doc comments** on all public/protected members — focus on behaviour, units, edge cases, and
  anything a caller can't infer from the signature (not restating the name).
- **Caller-observed result/DTO types are immutable** (`init`/get-only). Consumer-built request/config
  types may stay mutable for object-initialiser ergonomics.
- Prefer **overloads over optional parameters** on public APIs.

## Tests

- New behaviour and bug fixes should come with tests; the money, transaction-state, retry, and
  response-parsing paths are expected to stay well covered.
- Test the **requirement**, not just the mechanism — include a negative/invariant assertion where it applies.
- Wire-format tests assert **literal expected bytes**; do not recompute them via the library's own encoder.

## Please don't include real data

Never put real credentials, `merchantAccessToken`s, merchant/terminal identifiers, or captured
transaction/receipt data in issues, pull requests, tests, or sample state directories. Use synthetic
values only.

## Reporting security issues

Do not open a public issue for a security vulnerability — see [SECURITY.md](SECURITY.md).
