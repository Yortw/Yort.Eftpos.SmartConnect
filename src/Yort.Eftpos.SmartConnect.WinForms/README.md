# Yort.Eftpos.SmartConnect.WinForms

WinForms progress/outcome and pairing dialogs for the unofficial
[`Yort.Eftpos.SmartConnect`](https://github.com/yortw/Yort.Eftpos.SmartConnect) client
library (SmartPay / Shift4 SmartConnect, New Zealand).

> **Unofficial:** not affiliated with, endorsed by, or supported by Shift4 / SmartPay.
> SmartConnect and SmartPay are trademarks of their respective owners.

## Overview

This package provides two ready-to-use WinForms dialogs:

- **`SmartConnectProgressDialog`** — shows a "please wait" dialog while a progress-bearing
  SmartConnect operation runs, and optionally presents that operation's outcome afterwards
  (green/amber/red colour-coded, with an auto-close timeout).
- **`SmartConnectPairingDialog`** — prompts the operator for the terminal's pairing code,
  runs the pairing attempt, shows the result, and lets the operator retry a bad code or
  cancel.

Both dialogs support appearance customisation (window title, logo, colours, font) and
parent-window ownership (centres on screen when no owner is supplied).

## Requirements

- .NET Framework 4.8 or .NET 8.0-windows (or later `net*-windows` TFM).
- The consumer app must set `<UseWindowsForms>true</UseWindowsForms>` in its csproj (or
  target WinForms in some other way).

## Threading precondition

**Both dialogs must be constructed on the UI thread.**

`SmartConnectProgressDialog.Progress` is a `System.Progress<SmartConnectPollingStatus>`,
which captures the `SynchronizationContext` at construction time and marshals callbacks back
to it. Constructing either dialog on a background thread will cause cross-thread exceptions
or silent marshalling failures. Use `await` throughout the call chain rather than
`.Result`/`.Wait()`.

## `SmartConnectProgressDialog`

### Usage (model D — dialog observes, consumer drives)

```csharp
using var dialog = new SmartConnectProgressDialog(this) { WindowTitle = "EFTPOS", Logo = myLogo };

// Pristine: the real method, the real result, returned directly. The dialog appears
// only as the IProgress argument.
var result = await client.ProcessTransactionAsync(request, dialog.Progress);

// Optional. Omit entirely to suppress the outcome screen.
await dialog.ShowResultAsync(result, TimeSpan.FromSeconds(5));
```

Pass `dialog.Progress` as the `IProgress<SmartConnectPollingStatus>` argument to any
progress-bearing client method:

- Financial: `ProcessTransactionAsync`, `ResumePollingAsync`, `GetLastTransactionResultAsync`
- Non-financial: `LogonAsync`, `SettlementInquiryAsync`, `SettlementCutoverAsync`,
  `GetTerminalStatusAsync`, `ExecuteNonFinancialAsync`

The dialog auto-shows on the first progress report and closes on `Dispose`. The
library's `PollingStatus.Message` is used as the caption when non-null; otherwise the
configurable `StateCaptions` dictionary is used (pre-populated with sensible defaults).

### Outcome display

Call `ShowResultAsync` after the operation returns to show the outcome. Pass a
`TimeSpan` to auto-close after a delay, or omit it to wait for the operator's OK. Omit
the call entirely to suppress the outcome screen.

Outcomes are colour-coded:

| Colour | Statuses |
|--------|----------|
| Green  | `Accepted`, `Succeeded` |
| Amber  | `Unknown` (both result types) — requires explicit handling per the core library contract |
| Red    | `Declined`, `Failed`, `DeviceOffline`, `Cancelled` |

### Appearance customisation

```csharp
using var dialog = new SmartConnectProgressDialog(this)
{
    WindowTitle = "EFTPOS",
    Logo = Properties.Resources.MyLogo,
    BackgroundColour = Color.White,
    ForegroundColour = Color.Black,
};
// Override individual captions (all have defaults):
dialog.StateCaptions[SmartConnectPollingState.Delayed] = "Waiting for terminal…";
dialog.TransactionResultCaptions[SmartConnectTransactionStatus.Declined] = "Card declined";
```

## `SmartConnectPairingDialog`

### Usage (callback seam — dialog owns the loop, you supply the client call)

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

The dialog handles the full interactive loop: prompt → busy → result, retrying on a
failed pairing result or a transport error, until the operator either succeeds or cancels.
`ShowAsync` returns the successful `SmartConnectPairingResult` on success, or `null` if
the operator cancelled at any point.

Transport errors (`SmartConnectTransportException`) are caught and presented as retryable
failures. Any other exception propagates out of `ShowAsync`.

## Disclaimer

This package is an unofficial, community-maintained library. It is **not** affiliated with,
endorsed by, or supported by Shift4 or SmartPay. SmartConnect and SmartPay are trademarks
of their respective owners. Use at your own risk.

Source and licence: [github.com/yortw/Yort.Eftpos.SmartConnect](https://github.com/yortw/Yort.Eftpos.SmartConnect) (MIT).
