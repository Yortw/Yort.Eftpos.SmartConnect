# ADR — `Yort.Eftpos.SmartConnect.WinForms` dialog library (progress/outcome + pairing)

**Date:** 2026-06-18
**Status:** Accepted
**Context:** A WinForms companion package for the core `Yort.Eftpos.SmartConnect` client,
analogous to `Yort.Eftpos.Verifone.PosLink.WinForms`. The core exposes progress only through
`IProgress<SmartConnectPollingStatus>` passed into each call, has no events, no operator-query
mechanism, and no programmatic cancel / `CancellationToken`.
**Design doc:** [`2026-06-18-winforms-dialog-design.md`](./2026-06-18-winforms-dialog-design.md)

---

## Decision 1 — Dialog observes; the consumer invokes the operation (model "D")

**Context.** The dialog needs progress (to display) and, for the outcome screen, the result
and a completion signal. The prior art got both from client *events*; SmartConnect has none.
Candidate shapes: (A) dialog wraps the call via a lambda; (B) dialog exposes `Progress` plus an
explicit `Complete(result)`; (D) dialog exposes `Progress`, auto-shows on first progress, closes
on `Dispose`, and offers an optional `ShowResultAsync(result)`.

**Decision.** Model **D**. The consumer calls the client operation itself, passing
`dialog.Progress`; the call's signature and return are untouched. The dialog auto-shows on the
first progress report and closes on `Dispose`; outcome display is an optional `ShowResultAsync`
overload that *consumes* the already-returned result.

**Rationale.** Keeps the operation's call and return completely pristine — the dialog never
invokes business logic (the objection to A) and never relays the result through a UI method.
`Dispose` handles teardown, so there is no `Complete()` to forget (the desync risk in B).

**Trade-offs accepted.** The consumer references the dialog twice (once for `Progress`, once
optionally for `ShowResultAsync`). Forgetting `ShowResultAsync` simply shows no outcome screen —
a benign "feature not shown", never a leak or hang.

**Options considered.** A (wrapper/lambda) — rejected: makes the UI object the thing that runs
the payment, an inversion of responsibility. B (property + `Complete`) — rejected: manual
multi-step lifecycle the consumer can desync; the event-driven auto-show that made the
property style ergonomic for Verifone does not exist here.

## Decision 2 — Scope covers all progress-bearing operations, not just transactions

**Context.** The please-wait progress half is identical for every operation (all take
`IProgress<SmartConnectPollingStatus>`); only the outcome render differs by result type.

**Decision.** Support all progress-bearing operations (scope **B**): financial
(`SmartConnectTransactionResult`) and non-financial (`SmartConnectOperationResult`).

**Rationale.** The progress half costs nothing extra; the only addition is a second
`ShowResultAsync` overload pair for the operation-status enum. Settlements and logons poll too
and benefit from the same UX.

**Trade-offs accepted.** Two result-rendering paths (two status enums) instead of one.

**Options considered.** Transactions only — rejected: would not meaningfully simplify the
dialog, and would leave acquirer ops without the same UX for no real saving.

## Decision 3 — Outcome timeout via overloads, not an optional parameter; no show/timeout config flags

**Context.** The outcome screen may wait for OK or auto-close after a duration; suppression is
also needed.

**Decision.** Express the timeout as a second `ShowResultAsync` overload taking a `TimeSpan`.
Showing the outcome *is* calling `ShowResultAsync`; suppressing it *is* not calling it. No
`ShowResult` bool and no `ResultDisplayTimeout` property.

**Rationale.** Honours the project convention of overloads over optional/default parameters on
public APIs (a default compiled into each caller's IL cannot be changed later without
recompiling callers). Collapses three config knobs into the call itself — one obvious way to do
each behaviour.

**Trade-offs accepted.** Four `ShowResultAsync` overloads (two result types × with/without
timeout) instead of two methods with optional parameters.

## Decision 4 — Multi-target `net48` + `net8.0-windows`

**Context.** WinForms is unavailable on `netstandard`; the core is `netstandard2.0`. The
near-term consumer is a point-of-sale application on .NET Framework 4.x. An assembly compiled for `net48` can only be
referenced by an app targeting `net48`+.

**Decision.** `<TargetFrameworks>net48;net8.0-windows</TargetFrameworks>`.

**Rationale.** `net48` is the last and recommended 4.x and covers any consumer on the 4.8
runtime; `net8.0-windows` covers current .NET. Clean modern multi-target.

**Trade-offs accepted.** A consumer pinned to 4.7.2 or lower cannot reference the package. The
floor can be lowered later (add `net472`, etc.) if a real consumer needs it, without breaking
existing consumers.

**Options considered.** `net8.0-windows` only — rejected: excludes the .NET Framework consumer app.
`net472` + `net8.0-windows` — deferred: only if a sub-4.8 consumer actually appears.

## Decision 5 — Same repo, separate project and package

**Context.** The prior art kept its WinForms companion in the same repo/solution as the core,
as a separate project producing a separate package.

**Decision.** New `src/Yort.Eftpos.SmartConnect.WinForms` in this repo; separate NuGet package
depending on the core package; in-repo `ProjectReference`, published `PackageReference`.

**Rationale.** Versions and releases the companion in lockstep with the core; the Windows-only
dependency is isolated at the project/package level, so consumers who don't want WinForms simply
don't reference it. A separate repo would add cross-repo version alignment for little gain.

**Trade-offs accepted.** The public repo now contains a Windows-only project alongside the
`netstandard2.0` core (build matrix slightly larger).

## Decision 6 — No cancel/dismiss affordance during progress

**Context.** The prior art's cancel button sent an `EftposCancelRequest`. SmartConnect has no
cancel and no `CancellationToken`.

**Decision.** The progress screen has no buttons. The only button is OK on the outcome screen.

**Rationale.** A cancel/close during progress could neither cancel the payment nor abort the
local wait, so it would mislead the operator. The core bounds the wait at `MaxPollDuration`
(default 5 min) then returns `Unknown`, so the dialog cannot hang indefinitely.

**Trade-offs accepted.** The operator cannot dismiss a slow please-wait early; mitigated by the
library's own timeout.

## Decision 7 — Construct on the UI thread; rely on `Progress<T>` marshalling

**Context.** The prior art performed manual `InvokeRequired`/`Invoke` marshalling.

**Decision.** Expose `Progress` as a `System.Progress<SmartConnectPollingStatus>` created in
the dialog constructor, and require construction on the UI thread (documented).

**Rationale.** `Progress<T>` marshals callbacks to the `SynchronizationContext` captured at
construction, removing the manual marshalling entirely.

**Trade-offs accepted.** Constructing the dialog off the UI thread would marshal progress to the
wrong context; this is a documented precondition rather than a runtime guard.

## Decision 8 — Internal `Form`, single public wrapper type

**Context.** The prior art exposed a public adapter and kept its `Form` internal.

**Decision.** One public `SmartConnectProgressDialog` (the wrapper, `IDisposable`); the `Form`
is internal. UI-free presentation logic (caption/colour resolution, auto-show decision) is
factored into an internal presenter for unit testing.

**Rationale.** Encapsulation — consumers cannot manipulate arbitrary `Form` internals; the
testable logic is isolated from the untestable `Form`.

**Trade-offs accepted.** Slightly more types than exposing the `Form` directly. The shared
appearance/owner logic is shared by **composition** (an internal helper), not an internal base
class, because a public type cannot derive from an internal one (CS0060).

## Decision 9 — Pairing gets its own dialog, driven via a callback seam

**Context.** `PairAsync` is structurally unlike the progress operations: one-shot, no
`IProgress`, it **throws** `SmartConnectTransportException` on transport failure (a bad code
instead returns `Success = false`), and it is **UI-initiated** — the operator types the pairing
code the dialog collects before anything is called. A UI library that cannot onboard a terminal
is missing the key piece of EFTPOS UI.

**Decision.** A separate `SmartConnectPairingDialog` (sharing the appearance surface) that owns
an interactive loop — prompt → busy → result → retry-on-failure / cancel — and invokes a
caller-supplied `Func<string, Task<SmartConnectPairingResult>>` with the entered code. It
returns the successful result, or `null` if cancelled, and catches
`SmartConnectTransportException` to render it as a retryable failure (surfacing `Delivery`).

**Rationale.** The callback is the async equivalent of the prior art's event round-trip
(raise → handle → `SetResponse` ≡ collect code → invoke callback → await result), and it leaves
the literal `client.PairAsync(code, request)` call in the consumer's code — consistent with the
model-D principle that the consumer, not the UI, drives the client. The dialog depends on **no
client type at all** (only the `Func`), so it is more decoupled than the Verifone adapter and
trivially unit-testable with a fake callback. Encapsulating the retry loop and the throw
handling is the library's value-add.

**Trade-offs accepted.** The dialog orchestrates *when* the call runs (it owns the loop), which
is the "UI drives it" coupling rejected for transactions — accepted here because pairing's input
originates in the dialog, the retry loop lives in the dialog, and it is a one-time setup gesture,
not a per-sale business operation. This is a deliberate, reasoned exception to Decision 1, not a
contradiction.

**Options considered.** Pure model-D (dialog only does `PromptForCodeAsync`/`ShowResultAsync`;
the consumer writes the `while` + `try/catch` loop) — rejected: pushes ~10 lines of error-prone
boilerplate onto every consumer, defeating the helper's purpose. Direct client argument
(`dialog.PairAsync(client, request)`) — rejected in favour of the callback: terser but makes the
UI object literally invoke the client and couples the dialog to `SmartConnectClient`. An
event-style API (`CodeEntered` + `SetResult`) — rejected: mutable state, ordering hazards, more
ceremony, and ill-suited to a UI-initiated flow.

---

## Decisions explicitly deferred

| Topic | Why deferred |
| --- | --- |
| Receipt rendering | Not wanted; `Receipt` is on the result for consumers who want their own. |
| `PrintRequested` event | Prior-art feature with no SmartConnect analog. |
| Cancel / abort affordance | No cancel and no `CancellationToken` in the core API. |
| Dialog reuse across operations | One-shot per `using` keeps lifecycle simple. |
| `net472` (or lower) floor | Add only if a sub-4.8 consumer appears. |
| Re-pair / unpair UI | No unpair API exists (done at the terminal); pairing covers onboarding only. |
| Diagnostic logging in the WinForms package | Deferred — the package is a thin view over the core client, which already logs operations and ambiguous outcomes with the client transaction reference. Revisit only if field issues prove undiagnosable from the core's logs. (Adversarial review F9.) |
