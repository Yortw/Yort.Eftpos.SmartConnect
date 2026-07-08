# Yort.Eftpos.SmartConnect

[![CI](https://github.com/Yortw/Yort.Eftpos.SmartConnect/actions/workflows/ci.yml/badge.svg)](https://github.com/Yortw/Yort.Eftpos.SmartConnect/actions/workflows/ci.yml)

A .NET client library for the **SmartPay / Shift4 SmartConnect** EFTPOS integration (New Zealand) — a cloud REST API that pairs a point-of-sale register to a payment terminal and processes card transactions via an asynchronous polling model.

> ⚠️ **Pre-release / work in progress.** Under active initial development; pre-release builds only, not yet on public NuGet. The API may change before 1.0.
>
> **Unofficial:** this is an independent, unofficial client library and is not affiliated with, endorsed by, or supported by Shift4 / SmartPay. "SmartConnect", "SmartPay", and "Shift4" are trademarks of their respective owners. The official API documentation is at <https://smartconnectdev.shift4.co.nz>.

## What it is

- Targets **.NET Standard 2.0** (consumable by .NET Framework 4.6.1+ through modern .NET).
- Wraps the SmartConnect endpoints (`PUT /Pairing/{code}`, `POST /Transaction` + GET-poll) behind a small, testable client with built-in poll-interval handling, HTTP 429 backoff (`Retry-After` honoured), and per-poll progress reporting.
- Treats declines/cancellations as **data** (a result status), and reserves exceptions for failures to obtain an answer.
- Makes crash-recovery a first-class concern: the caller persists transaction state (including the polling URL) via an injected contract, the library refuses to send a transaction it could not first record, and polling can be resumed after a restart.

## The contract in one paragraph

If SmartConnect **answered**, you get a `SmartConnectTransactionResult` — including declines (`Declined` is a normal outcome, not an error) and including `Unknown` (which you **must** handle explicitly: it means the financial outcome is ambiguous and needs reconciliation; it is never safe to treat as approved *or* as not-charged). If the library **could not get an answer**, methods that are one-shot (`PairAsync`, `GetLastTransactionResultAsync`) throw a single exception type, `SmartConnectTransportException`, whose `Delivery` property tells you what is known: `NotSent` (provably never reached the service — retry freely) or `Unknown` (may have been processed — do not blind-retry a payment). `ProcessTransactionAsync` never throws for runtime conditions at all — every operational failure is a result; inspect `Status` plus `FailureCause`.

## Quick start

### 1. Pair the register (once)

```csharp
var configuration = new SmartConnectClientConfiguration
{
	BaseUrl = SmartConnectEnvironments.Development, // switch to .Production for release builds
	StateStore = new FileBasedTransactionStateStore(@"C:\ProgramData\MyPos\EftposState"),
	UserAgentProductName = "MyPos",
	UserAgentProductVersion = "1.0.0"
};

using var client = new SmartConnectClient(configuration);

// Deterministic UUID v5: same merchant + register always produce the same id (returned as the canonical
// UUID string), so a reinstalled register keeps its existing pairing.
var registerId = SmartConnectRegisterId.Generate("MyMerchant", "Register-01");

var pairing = await client.PairAsync("12345678", new SmartConnectPairingRequest
{
	POSRegisterID = registerId,
	POSBusinessName = "My Store",
	POSVendorName = "MyPos",
	POSRegisterName = "Front Counter"
});

if (!pairing.Success)
{
	Console.WriteLine($"Pairing failed: {pairing.ErrorMessage}");
}
```

The `POSRegisterID`/`POSBusinessName`/`POSVendorName` triple must match across pairing and every subsequent transaction. There is no unpairing API — unpairing is performed by a human at the terminal, and re-pairing a register to a different terminal automatically unpairs the previous one.

### 2. Process a transaction

```csharp
var saleReference = "sale-0001"; // your stable per-sale id — the SAME value across restarts (the crash-recovery key)

var result = await client.ProcessTransactionAsync(new SmartConnectTransactionRequest
{
	TransactionType = SmartConnectTransactionType.CardPurchase,
	AmountTotal = Money.FromDecimal(19.95m),
	POSRegisterID = registerId,          // registerId + the names from pairing (step 1) — the triple must match
	POSBusinessName = "My Store",
	POSVendorName = "MyPos",
	ClientTransactionRef = saleReference // stable across restarts — it is the crash-recovery key
});

switch (result.Status)
{
	case SmartConnectTransactionStatus.Accepted:
		// result.AuthId, result.CardType, result.Receipt (fixed-width text — render monospaced) ...
		break;
	case SmartConnectTransactionStatus.Declined:
	case SmartConnectTransactionStatus.Cancelled:
		// Normal outcomes — show the operator, move on.
		break;
	case SmartConnectTransactionStatus.Unknown:
		// MANDATORY handling: outcome ambiguous (timeout, lost response, gateway 5xx). Reconcile before
		// retrying — result.FailureCause distinguishes "never sent" (safe retry) from "may have been processed".
		break;
	case SmartConnectTransactionStatus.Failed:
		// result.FailureCause: ServiceError (fix request/config), TransportNotSent (safe to retry),
		// StateStoreFailure (nothing sent — store unavailable; retry once it recovers).
		break;
}

// A resolved financial outcome (Accepted/Declined/Cancelled) is yours to record. AFTER you have durably
// recorded it against your sale, complete the recovery record so recovery stops re-polling it — persist
// idempotently by ClientTransactionRef, since recovery can replay a still-pending completed transaction.
// Do NOT blanket-complete every status: on Unknown/Failed the library has already closed what it can
// (a rejected or never-sent POST, as Failed) and deliberately leaves the rest — including
// poll-exhaustion and dispose — pending for recovery or manual reconciliation (see step 3). Completing
// a StateStoreFailure would throw — no record exists.
if (result.Status is SmartConnectTransactionStatus.Accepted
	or SmartConnectTransactionStatus.Declined
	or SmartConnectTransactionStatus.Cancelled)
{
	await configuration.StateStore.UpdateCompletedAsync(saleReference, result.Status);
}
```

Pass an `IProgress<SmartConnectPollingStatus>` to the second overload for UI feedback while polling (`Polling`, `Delayed`, `BackingOff`, `NetworkError`).

### 3. Recover after a crash

```csharp
foreach (var pending in await configuration.StateStore.GetPendingTransactionsAsync())
{
	if (!string.IsNullOrEmpty(pending.PollingUrl))
	{
		// Resume polling the persisted URL — the ONLY programmatic way to recover an outcome.
		var recovered = await client.ResumePollingAsync(pending.PollingUrl, pending.ClientTransactionRef);
		// The library leaves this record pending too. On a real terminal status, durably record the outcome
		// (idempotently — recovery may re-deliver a sale you already processed), THEN call
		// configuration.StateStore.UpdateCompletedAsync(pending.ClientTransactionRef, recovered.Status).
		// If this resume itself times out (recovered.Status == Unknown with no PollingUrlInvalid cause), the
		// library leaves the record PENDING — do NOT complete it. The next recovery pass re-polls it, and a
		// late-settling transaction is delivered then. Complete it yourself only once you have a real outcome
		// (a later resume) or you have reconciled it (pass Unknown to accept the ambiguity and stop tracking).
		// recovered.FailureCause == PollingUrlInvalid means the URL expired: the outcome is
		// unknown — resolve it by manual reconciliation, then update the sentinel yourself.
	}
	else
	{
		// No polling URL was persisted (the crash hit before it arrived): the outcome CANNOT be
		// determined programmatically. Surface this sale as unknown and reconcile manually against
		// the terminal/acquirer records. GetLastTransactionResultAsync can fetch the device's last
		// transaction as supporting EVIDENCE, but it is device-scoped and nothing in its result
		// reliably identifies that transaction as this sale — never adopt it as the outcome.
		var evidence = await client.GetLastTransactionResultAsync(new SmartConnectRegistration
		{
			POSRegisterID = registerId,
			POSBusinessName = "My Store",
			POSVendorName = "MyPos"
		});
	}
}
```

## Things to know before going live

- **The state store is load-bearing, not optional.** The library writes a sentinel *before* every transaction POST and refuses to send if that write fails — it is the only thing that makes a crash mid-transaction recoverable. The bundled `FileBasedTransactionStateStore` is a reference implementation (pre-sized records, transient-IO retry, atomic writes); production systems with a database should implement `ISmartConnectTransactionState` against it.
- **The polling URL contains a bearer credential** (`merchantAccessToken`). The library never logs it; your state store persists it, so restrict access to wherever that lands. Never log it yourself.
- **There is no idempotency key and no programmatic cancel in the SmartConnect API.** A timed-out POST may still have charged the customer — that is what `Unknown` is for. Do not blind-retry; route `Unknown` outcomes to manual reconciliation (there is no API that can resolve them for you — see the design doc).
- **Logging:** supply an `ILogger` via `SmartConnectClientConfiguration.Logger` — normal operation, backoff, store trouble, and every ambiguous outcome are logged with the client transaction reference. Logging failures never affect transaction processing.

## Trying it against a real dev terminal

`samples/Yort.Eftpos.SmartConnect.Demo` is an interactive console app (run it from your IDE, or `dotnet run -f net8.0`) that pairs with a dev pinpad and exercises the library end-to-end: purchases, refunds, crash-recovery resume, journal queries, and a no-pinpad-needed transport-failure probe. It is **illustrative, not a production POS** — no receipt printing, no tender integration, no offline handling — but its `Unknown`-handling and progress/logging wiring are the patterns to copy.

Two warnings: financial menu actions send **real transactions** to the connected terminal (the app echoes amounts and asks for confirmation first), and its state directory contains **bearer-token polling URLs** — don't commit it or attach it to bug reports. It multi-targets `net48` and `net8.0`; running the transport probe on both is how the cross-runtime failure-classification gets verified.

## Design rationale

The *why* behind the API shape — the result-with-`Unknown` contract, the mandatory state-store, the transport `Delivery` classification, and the verified limits of `Journal.GetTransResult` (a diagnostic only — it cannot reliably identify a specific transaction) — is recorded in [docs/design-decisions.md](https://github.com/Yortw/Yort.Eftpos.SmartConnect/blob/main/docs/design-decisions.md).

## Licence

Licensed under the [MIT License](https://github.com/Yortw/Yort.Eftpos.SmartConnect/blob/main/LICENSE).
