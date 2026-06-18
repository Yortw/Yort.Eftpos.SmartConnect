# Design — `Yort.Eftpos.SmartConnect.WinForms` dialog library (progress/outcome + pairing)

**Date:** 2026-06-18
**Status:** Approved (brainstorm complete; implementation plan pending)
**Companion ADR:** [`2026-06-18-winforms-dialog-adr.md`](./2026-06-18-winforms-dialog-adr.md)
**Core library design:** [`design-decisions.md`](./design-decisions.md)

## 1. Purpose & scope

A reusable, opt-in WinForms companion package providing two dialogs:

1. **`SmartConnectProgressDialog`** — shows a "please wait" dialog while a progress-bearing
   SmartConnect operation runs, and optionally presents that operation's outcome afterwards.
2. **`SmartConnectPairingDialog`** — prompts the operator for the terminal's pairing code,
   runs the pairing attempt, shows the result, and lets the operator retry a bad code or
   cancel.

It is general-purpose — it depends only on the public surface of the core
`Yort.Eftpos.SmartConnect` package and contains no consumer-specific logic.

**Progress dialog scope (B):** every progress-bearing client operation, not just customer
transactions:

- Financial (`SmartConnectTransactionResult`): `ProcessTransactionAsync`,
  `ResumePollingAsync`, `GetLastTransactionResultAsync`.
- Non-financial (`SmartConnectOperationResult`): `LogonAsync`, `SettlementInquiryAsync`,
  `SettlementCutoverAsync`, `GetTerminalStatusAsync`, `ExecuteNonFinancialAsync`.

**Pairing dialog scope:** `PairAsync`. It is structurally different from the progress
operations — a one-shot that takes no `IProgress`, **throws** `SmartConnectTransportException`
on transport failure (a service rejection such as a bad code instead returns
`Success = false` + `ErrorMessage`), and is **UI-initiated** (the operator types a code the
dialog collects before anything is called). It therefore gets its own dialog rather than
sharing the progress dialog's observe-only model.

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

Two public dialog types; each `Form` stays internal. The shared appearance surface
(`WindowTitle`, `Logo`, `BackgroundColour`, `ForegroundColour`, `Font`, `DisableOwnerWhileBusy`)
plus owner handling (centre-on-screen when null, disable/re-enable while busy) is shared via an
**internal composition helper**, *not* an internal base class — a public type cannot derive from
an internal one (CS0060), so the two sealed public dialogs each expose the appearance properties
and forward to the shared helper.

### 2a. `SmartConnectProgressDialog`

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
    // SmartConnectPollingStatus.Message is null (progress) / for the mapped status (outcome).
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

### 2b. `SmartConnectPairingDialog`

```csharp
namespace Yort.Eftpos.SmartConnect.WinForms;

public sealed class SmartConnectPairingDialog : IDisposable
{
    public SmartConnectPairingDialog();                  // owner-less → centre on screen
    public SmartConnectPairingDialog(IWin32Window owner);

    // Shared appearance surface (forwarded to the internal composition helper)
    public string WindowTitle { get; set; }
    public Image? Logo { get; set; }
    public Color BackgroundColour { get; set; }
    public Color ForegroundColour { get; set; }
    public Font Font { get; set; }
    public bool DisableOwnerWhileBusy { get; set; } = true;

    // Overridable, localisable prompt/labels (pre-populated with defaults)
    public string Prompt { get; set; }                   // "Enter the pairing code shown on the terminal"

    // Drives prompt → busy → result, looping on failure until success or cancel. Returns the
    // successful result, or null if the operator cancelled. The dialog depends on NO client
    // type — only on a callback that turns the entered code into a pairing result.
    public Task<SmartConnectPairingResult?> ShowAsync(Func<string, Task<SmartConnectPairingResult>> pairWithCode);

    public void Dispose();
}
```

Canonical usage (model **A** — callback seam; the dialog owns the interactive loop, you write
the client call):

```csharp
using var dialog = new SmartConnectPairingDialog(this) { WindowTitle = "Pair Terminal", Logo = myLogo };

var request = new SmartConnectPairingRequest
{
    POSRegisterID = SmartConnectRegisterId.Generate("MyMerchant", "Register-01"),
    POSBusinessName = "My Store",
    POSVendorName = "MyPos",
    POSRegisterName = "Front Counter"
};

var result = await dialog.ShowAsync(code => client.PairAsync(code, request));
if (result is null)            { /* operator cancelled — not paired */ }
else                           { /* paired (result.Success is true) */ }
```

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
- **Caption resolution (progress).** For each report, prefer `SmartConnectPollingStatus.Message` when
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

### Pairing dialog (`SmartConnectPairingDialog`)

- **Loop:** prompt for code (with `Cancel`) → on submit, disable input and show a brief
  "Pairing…" busy state while awaiting the callback → present the result. On a failed result
  (or a caught `SmartConnectTransportException`), show the error and return to the prompt so the
  operator can retry or cancel. On success, show success and dismiss on the operator's OK,
  returning the result. Cancel at any prompt returns `null`. (No auto-close timeout on pairing —
  unlike a transaction outcome, the operator is actively onboarding and should acknowledge.)
- **Code validation:** the dialog requires a non-blank, trimmed code before invoking the
  callback (so the callback never triggers the core's empty-code `ArgumentException`).
- **Exception handling:** the dialog catches `SmartConnectTransportException` from the callback
  and renders it as a retryable failure, surfacing `Delivery` — `NotSent` ("couldn't reach the
  service — safe to retry"; amber) vs `Unknown` ("may have paired — retrying the same register
  is harmless, or cancel and check the terminal"; amber). Other exceptions (e.g.
  `ObjectDisposedException`, a programming error) propagate out of `ShowAsync`.
- **Busy state:** `PairAsync` is one-shot with no `IProgress`, so the busy state is a simple
  indeterminate spinner shown for the duration of the single await — there are no poll states
  to caption.
- **Threading / null owner / modal-like behaviour:** identical to the progress dialog (construct
  on the UI thread; centre-on-screen when owner is null; disable owner while busy).

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

Pairing dialog (`SmartConnectPairingDialog`):

```
  Prompt state                            Result state (success / retryable failure)
 ┌─ Pair Terminal ────────────┐          ┌─ Pair Terminal ────────────┐
 │   [ logo ]                  │          │   [ logo ]                  │
 │   Enter the pairing code    │          │   ✔  Paired                 │  ← green
 │   shown on the terminal:    │          │   ✘  Invalid code           │  ← red (+ ErrorMessage)
 │   [____________]            │          │                             │
 │        [ Pair ] [ Cancel ]  │          │   [ Try again ] [ Cancel ]  │  (failure only)
 └─────────────────────────────┘          └─────────────────────────────┘
```

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

- caption resolution — including the **negative/invariant case**: when `SmartConnectPollingStatus.Message`
  is non-null the default caption is **not** used;
- status → caption + colour mapping for both result types (every enum value, incl. `Unknown`);
- the auto-show / first-progress decision and the owner-disable/re-enable decision (as pure
  logic, separate from the `Form`).

For pairing, the loop logic is testable without any `Form` because the dialog depends only on a
`Func<string, Task<SmartConnectPairingResult>>`: feed a fake callback and assert the
state-machine decisions — code-validation gating (blank code never invokes the callback),
retry-on-failure vs close-on-success, cancel → `null`, and `SmartConnectTransportException`
caught-and-rendered-as-retryable (with the `NotSent`/`Unknown` distinction) while other
exceptions propagate.

The `Form`s themselves stay thin shells, verified by hand and via a **separate tiny WinForms
sample** (`samples/Yort.Eftpos.SmartConnect.WinFormsDemo`, `net8.0-windows`) — kept apart from
the existing cross-platform console demo so the WinForms/Windows-only dependency does not
constrain that demo's target frameworks. Manual smoke against a dev terminal.

## 7. Deferred / out of scope

| Topic | Why deferred |
| --- | --- |
| Receipt rendering | Explicitly not wanted; `SmartConnectTransactionResult.Receipt` is available to consumers who want their own rendering. |
| `PrintRequested` event | Prior-art feature with no SmartConnect analog. |
| Cancel / abort affordance | No programmatic cancel and no `CancellationToken` in the core API; would be misleading. |
| Dialog reuse across operations | One-shot per `using` keeps lifecycle simple; revisit only if a real need appears. |
| Re-pair / unpair UI | There is no unpair API (it is done at the terminal); pairing covers the onboarding gesture only. |
