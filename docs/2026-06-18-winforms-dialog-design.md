# Design — `Yort.Eftpos.SmartConnect.WinForms` progress/outcome dialog

**Date:** 2026-06-18
**Status:** Approved (brainstorm complete; implementation plan pending)
**Companion ADR:** [`2026-06-18-winforms-dialog-adr.md`](./2026-06-18-winforms-dialog-adr.md)
**Core library design:** [`design-decisions.md`](./design-decisions.md)

## 1. Purpose & scope

A reusable, opt-in WinForms companion package that:

1. shows a "please wait" dialog while a progress-bearing SmartConnect operation runs, and
2. optionally presents that operation's outcome afterwards.

It is general-purpose — it depends only on the public surface of the core
`Yort.Eftpos.SmartConnect` package and contains no consumer-specific logic.

**Scope (B):** every progress-bearing client operation, not just customer transactions:

- Financial (`SmartConnectTransactionResult`): `ProcessTransactionAsync`,
  `ResumePollingAsync`, `GetLastTransactionResultAsync`.
- Non-financial (`SmartConnectOperationResult`): `LogonAsync`, `SettlementInquiryAsync`,
  `SettlementCutoverAsync`, `GetTerminalStatusAsync`, `ExecuteNonFinancialAsync`.

`PairAsync` is **out of scope** — it is a one-shot that takes no `IProgress` and throws
`SmartConnectTransportException` on transport failure, so there is no polling to surface.

### Relationship to the Verifone prior art

This package is the analog of `Yort.Eftpos.Verifone.PosLink.WinForms`, but the protocols
differ fundamentally and most of the prior art's mechanism does **not** carry over:

| Verifone PosLink (interactive pinpad protocol) | SmartConnect (cloud poll) |
| --- | --- |
| `DisplayMessage` / `QueryOperator` **events** drive the dialog | **No events**; progress only via `IProgress<SmartConnectPollingStatus>` passed into each call |
| Operator-response buttons (SIG/ASK) → `e.SetResponse(...)` | No operator query; the terminal handles PIN/signature itself → **no response buttons** |
| Cancel button sends `EftposCancelRequest` | **No programmatic cancel and no `CancellationToken`** → no cancel affordance |
| `PrintRequested` for signature slips | Out of scope |

What carries over is the **shell**: a reusable, customisable, owner-parented modal showing
a caption + busy indicator, with title/logo/colour/font customisation.

## 2. Public surface

One public type; the `Form` stays internal.

```csharp
namespace Yort.Eftpos.SmartConnect.WinForms;

public sealed class SmartConnectProgressDialog : IDisposable
{
    public SmartConnectProgressDialog();                 // owner-less → centre on screen
    public SmartConnectProgressDialog(IWin32Window owner);

    // The only coupling into the operation — an IProgress, nothing more.
    public IProgress<SmartConnectPollingStatus> Progress { get; }

    // Appearance (carried from the prior art)
    public string WindowTitle { get; set; }
    public Image? Logo { get; set; }
    public Color BackgroundColour { get; set; }
    public Color ForegroundColour { get; set; }
    public Font Font { get; set; }
    public bool DisableOwnerWhileBusy { get; set; } = true;

    // Overridable, localisable captions. A default is used only when the library's
    // PollingStatus.Message is null (progress) / for the mapped status (outcome).
    public IDictionary<SmartConnectPollingState, string> StateCaptions { get; }
    public IDictionary<SmartConnectTransactionStatus, string> TransactionResultCaptions { get; }
    public IDictionary<SmartConnectOperationStatus, string> OperationResultCaptions { get; }

    // Outcome display — consumes the real result, never produces it. Suppress = don't call.
    public Task ShowResultAsync(SmartConnectTransactionResult result);
    public Task ShowResultAsync(SmartConnectTransactionResult result, TimeSpan autoCloseAfter);
    public Task ShowResultAsync(SmartConnectOperationResult result);
    public Task ShowResultAsync(SmartConnectOperationResult result, TimeSpan autoCloseAfter);

    public void Dispose();   // re-enables owner, closes and disposes the form
}
```

### Canonical usage (model D — dialog observes, consumer drives)

```csharp
using var dialog = new SmartConnectProgressDialog(this) { WindowTitle = "EFTPOS", Logo = myLogo };

// Pristine: the real method, the real result, returned directly. The dialog appears
// only as the IProgress argument.
var result = await client.ProcessTransactionAsync(request, dialog.Progress);

// Optional. Omit entirely to suppress the outcome screen.
await dialog.ShowResultAsync(result, TimeSpan.FromSeconds(5));
```

The transaction call's signature and return are exactly the core library's; the dialog never
wraps, substitutes, or relays the result. Non-WinForms consumers simply don't reference this
package.

## 3. Behaviour

- **Lifecycle.** The dialog auto-shows on the **first** progress report and closes on
  `Dispose`. `ShowResultAsync` also shows the form if it is not already visible, so an
  operation that reports no progress before completing can still display its outcome.
- **No dismiss button during progress — by design.** There is no cancel in the protocol, and
  the core library bounds the wait at `SmartConnectClientConfiguration.MaxPollDuration`
  (default 5 minutes) before returning `Unknown`, so the dialog cannot hang indefinitely and a
  "Cancel" button would be a lie. The only button anywhere is **OK**, on the outcome screen.
- **Modal-like, async-friendly.** True `ShowDialog` cannot be used — it blocks the thread,
  which is incompatible with awaiting the operation. The dialog is **modeless** but **disables
  the owner window while busy** (`DisableOwnerWhileBusy`, default `true`), re-enabling it on
  close. The outcome screen resolves its `Task` via a `TaskCompletionSource` completed by the
  OK click **or** the auto-close timeout, whichever comes first.
- **Null owner is supported.** With no owner the dialog centres on screen, skips the
  owner-disable step entirely, and never dereferences the owner.
- **Threading.** The `Progress` property is a `System.Progress<SmartConnectPollingStatus>`,
  which marshals its callbacks to the `SynchronizationContext` captured at construction.
  Therefore the dialog **must be constructed on the UI thread** (documented on the type). This
  removes the prior art's manual `InvokeRequired` marshalling.
- **Caption maps come pre-populated** with the defaults below; consumers replace individual
  entries to override, and the values are plain strings/resources so they can be localised.
- **Caption resolution (progress).** For each report, prefer `PollingStatus.Message` when
  non-null; otherwise use `StateCaptions[State]`; defaults:
  - `Polling` → "Processing payment…"
  - `Delayed` → "Waiting for pinpad — it may be offline…"
  - `BackingOff` → "Busy, retrying…"
  - `NetworkError` → "Network problem, retrying…"
- **Outcome rendering.** Status caption (from the relevant caption map) plus a colour cue,
  grouped so every enum value is covered:
  - **green (success):** `Accepted`, `Succeeded`;
  - **amber (ambiguous — prominent):** `Unknown` (both result types), because the core contract
    requires callers to handle it explicitly;
  - **red / negative:** `Declined`, `Failed`, `DeviceOffline`, `Cancelled`.

  (Exact shades are finalised in the implementation plan; the grouping above is the contract.)
  A `Failed` `SmartConnectOperationResult` also shows its `ErrorMessage`. No receipt is
  rendered.
- **One operation per instance.** Construct a dialog per operation (`using`). Reuse across
  multiple sequential operations is not a goal.

## 4. Layout

```
  Progress state                          Outcome state (ShowResultAsync)
 ┌─ EFTPOS ───────────────────┐          ┌─ EFTPOS ───────────────────┐
 │   [ logo ]                  │          │   [ logo ]                  │
 │                             │          │                             │
 │   Processing payment…       │          │   ✔  Approved               │  ← green/red/amber
 │   ▰▰▰▰▱▱▱▱  (marquee)        │          │                             │
 │                             │          │              [   OK   ]     │
 └─────────────────────────────┘          └─────────────────────────────┘
```

The busy indicator is an **indeterminate** marquee — SmartConnect polling reports *states*,
never a percentage, so there is no progress fraction to show.

## 5. Project, packaging, frameworks

- New project `src/Yort.Eftpos.SmartConnect.WinForms` in this repo (mirrors the prior art's
  single-repo layout; core and companion are versioned and released together).
- `<TargetFrameworks>net48;net8.0-windows</TargetFrameworks>`, `<UseWindowsForms>true</UseWindowsForms>`,
  SDK-style csproj.
- **In-repo**: `ProjectReference` to the core project. **Published package**: a
  `PackageReference` dependency on `Yort.Eftpos.SmartConnect` (the package, not the project).
- Own package metadata (id `Yort.Eftpos.SmartConnect.WinForms`), README, and the shared
  non-vendor EFTPOS icon.
- A consumer app must target `net48` or higher (or a `net8.0-windows` TFM) to reference it;
  this is the agreed floor.

## 6. Testing

WinForms UI is awkward to unit-test, so the testable logic is extracted into an internal,
UI-free **presenter** and unit-tested directly:

- caption resolution — including the **negative/invariant case**: when `PollingStatus.Message`
  is non-null the default caption is **not** used;
- status → caption + colour mapping for both result types (every enum value, incl. `Unknown`);
- the auto-show / first-progress decision and the owner-disable/re-enable decision (as pure
  logic, separate from the `Form`).

The `Form` itself stays a thin shell, verified by hand and via the existing demo app
(manual smoke against a dev terminal).

## 7. Deferred / out of scope

| Topic | Why deferred |
| --- | --- |
| Receipt rendering | Explicitly not wanted; `SmartConnectTransactionResult.Receipt` is available to consumers who want their own rendering. |
| `PrintRequested` event | Prior-art feature with no SmartConnect analog. |
| Cancel / abort affordance | No programmatic cancel and no `CancellationToken` in the core API; would be misleading. |
| Dialog reuse across operations | One-shot per `using` keeps lifecycle simple; revisit only if a real need appears. |
| Pairing UI | `PairAsync` is a one-shot with no progress; a separate concern if ever wanted. |
