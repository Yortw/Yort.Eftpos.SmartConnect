# Design Decisions

This is the design-rationale record for **Yort.Eftpos.SmartConnect** — the *why* behind the
public API shape, captured as an ADR (Architecture Decision Record). The [README](../README.md) is
the practical overview; this document explains the reasoning a contributor or an integrator
evaluating the library would want.

> Scope: this records decisions about **the library** — the vendor-protocol layer. How any
> particular point-of-sale product wires the library into its own payment model, persistence, and
> UI is that product's concern and is intentionally out of scope here. Decision numbers are stable
> and are referenced from code comments and XML docs; gaps in the rationale of a decision usually
> mean the omitted detail was consumer-specific.

**Status:** Accepted · **Last updated:** 2026-06-17

The library wraps the **SmartPay / Shift4 SmartConnect** (New Zealand) cloud REST API: a register
is paired to a payment terminal, transactions are submitted with `POST /Transaction`, and the
outcome is retrieved by polling a returned URL. Two properties of that API shape almost everything
below: **there is no idempotency key, and there is no programmatic cancel.** A POST that times out
may still have charged the customer, and once a transaction is on the terminal only a person can
stop it.

---

## Decision 1: A standalone, independently testable library

### Context

The integration could live inline inside a consuming application's payment code, or as a separate
library with no application dependencies.

### Decision

Ship a standalone library targeting **.NET Standard 2.0** (consumable from .NET Framework 4.6.1+
through modern .NET), with no dependency on any particular POS application.

### Rationale

- **Independent testability** — the whole client can be exercised against a mocked `HttpMessageHandler`
  with no application or hardware in the loop. Payment code warrants high coverage, and the polling /
  backoff / recovery logic is the interesting part to test.
- **Separation of concerns** — "deal with the vendor API" and "integrate with an application" are
  different jobs with different change rates; a binary boundary keeps them from tangling.
- **netstandard2.0** — maximises reach (legacy desktop POS through modern .NET, and cloud-HTTP means
  it is feasible directly from mobile too) at no meaningful cost; nothing here needs a newer surface.

### Trade-offs Accepted

- A versioned package boundary to maintain. For a component expected to be stable after initial
  development, that discipline is paid mostly at change-time and is cheap relative to the contract
  clarity it buys for payment code.

---

## Decision 2: Mandatory persistence contract via constructor injection

### Context

The async polling model means the consumer must persist the polling URL (and a pre-flight sentinel)
to survive a crash mid-transaction. The library needs to guarantee that happens at the right moments.

### Decision

The library requires an `ISmartConnectTransactionState` implementation injected at construction. The
library calls it at the defined lifecycle points (sentinel before POST, polling details once known,
completion). Persistence is **not** an event the consumer may forget to subscribe to.

### Rationale

- **Impossible to skip** — a missing implementation is a startup error, not a silent runtime gap.
- **Correct by construction** — the library decides *when* to persist; the consumer only supplies *how*.
- **Storage-agnostic** — different consumers have different stores (file, RDBMS, cloud); the library
  must not own that choice. A `FileBasedTransactionStateStore` reference implementation is bundled so
  trivial cases need no custom code.

### Options Considered

| Option | Verdict | Reason |
|---|---|---|
| `TransactionInitiated` event | Rejected | Fire-and-forget; easy to not subscribe or to swallow exceptions; reads as optional logging |
| **Injected `ISmartConnectTransactionState`** | **Selected** | Mandatory, self-documenting, correct by construction |
| Library persists internally | Rejected | Storage choice belongs to the consumer |

---

## Decision 3: Result object with an explicit `Unknown` state, and no `CancellationToken`

### Context

How should a transaction outcome be communicated — and should a caller be able to cancel polling?

### Decision

Outcomes are a result object whose `SmartConnectTransactionStatus` includes a terminal **`Unknown`**.
`ProcessTransactionAsync` takes **no** `CancellationToken`; an internal max-poll-duration returns
`Unknown` when exceeded.

### Rationale

- A `catch (Exception)` can silently swallow an exceptional outcome; a result forces `Unknown` onto the
  normal code path where it must be handled. Timeout is a *known possible outcome*, not an error.
- SmartConnect has no cancel endpoint. A `CancellationToken` would imply a transaction can be called
  off; in reality cancelling the poll only **orphans** an in-flight payment — the worst outcome for
  payment code. App shutdown is handled instead by persisting the polling URL and resuming on restart;
  the library simply gets disposed mid-poll.

### Options Considered

| Option | Verdict | Reason |
|---|---|---|
| Custom "unknown" exception | Rejected | Can be swallowed by a broad catch |
| **Result with `Unknown` status** | **Selected** | Forces explicit handling in normal control flow |
| Result + `CancellationToken` | Rejected | The token is a footgun — no programmatic cancel exists |

---

## Decision 4: Deterministic `POSRegisterID` via UUID v5

### Context

SmartConnect requires a stable, globally-unique `POSRegisterID` (UUID) per paired register. If it is
lost (e.g. a reinstall), the register must be re-paired.

### Decision

Provide a **vendor-neutral** helper, `SmartConnectRegisterId.Generate(merchantIdentifier, registerIdentifier)`,
producing a deterministic UUID v5 within the caller's own namespace.

### Rationale

- **Stable across reinstalls** — the same inputs always yield the same id, so a reinstalled register
  keeps its pairing. The docs warn callers to feed a stable *logical* identifier, never a hardware id
  or licence key (which would change and silently force re-pairing).
- **Globally unique** — distinct merchant/register pairs yield distinct ids.
- **Optional** — purely a convenience; callers may supply their own id strategy.
- UUID v5 uses SHA-1: fine here — this is a determinism/uniqueness context, not a security one.

---

## Decision 5: Polling-status events are for UI only, never for persistence

### Context

Progress (`Polling`, `Delayed`, `BackingOff`, `NetworkError`) is useful for UI feedback. Should events
also drive persistence?

### Decision

Events are informational only. Persistence is exclusively `ISmartConnectTransactionState` (Decision 2).
One mechanism per job; no ambiguity about which to use.

---

## Decision 6: Proceed with a documented, partly-unmitigable recovery gap

### Context

There is no documented way to recover a transaction when the polling URL is lost (POST succeeded, the
terminal has the transaction, the process dies before the URL is persisted). This is contrary to the
EFTPOS norm of the POS minting a reference *before* sending to the device. The vendor has acknowledged
the gap and indicated there is no code-level mitigation today.

### Decision

Ship the integration with the strongest protection the API allows, and document the residual hole
prominently rather than pretending it is closed:

- **Sentinel written before every POST**, with an absolute pre-POST gate (Decision 10).
- **Persisted polling URL** resumed on restart — the only programmatic recovery mechanism.
- **`Journal.GetTransResult`** last-transaction fetch — originally framed as "Layer 2 recovery", since
  **rescoped to a diagnostic only** (see Decision 10's 2026-07-02 update).

### Trade-offs Accepted

- A real window remains where, if the POST succeeds but the URL is never persisted, the outcome is
  unknown and cannot be resolved programmatically. The library surfaces this honestly as `Unknown` /
  transport `Delivery = Unknown`; it does not guess — resolution is manual reconciliation. Adopters must
  be made aware of the residual risk. If an idempotency-key mechanism ever ships, it slots in without
  rearchitecting.

---

## Decision 7: Distribution as a built package, with an "unofficial" disclaimer

### Context

How the library is consumed and labelled.

### Decision

Distributed as a **built NuGet package** (not source/project references) so consumers build against a
frozen, tested binary and a semver contract. The package name and docs carry an **"unofficial — not
affiliated with or endorsed by Shift4 / SmartPay"** disclaimer, since it wraps a third party's product
(precedent: `Yort.Eftpos.Verifone.PosLink`). The SmartConnect integration guide is publicly published
and invites third-party integration, so a public client library is appropriate.

### Trade-offs Accepted

- Versioning discipline at change-time, in exchange for a stable contract for payment code.

---

## Decision 8: `Money` value type and immutable result types

### Context

Amounts are minor-unit integers on the wire, encoded as JSON **strings** (`"AmountTotal":"500"`), but
callers think in dollars. An early shape exposed cents (`long`) plus a `decimal` convenience on each
amount field, duplicating the conversion in several places.

### Decision

- A readonly `Money` value type: cents authoritative; factories `FromCents` / `FromDecimal` mirrored by
  `ToCents()` / `ToDecimal()`; a `MoneyJsonConverter` centralises the wire string/number handling.
- Library-produced types the caller only observes (results, polling status, pending records) are
  **immutable** (`init`-only); consumer-built request/config types stay mutable for object-initialiser
  ergonomics. `init` on netstandard2.0 uses an internal `IsExternalInit` shim.

### Rationale

- Removes the cents↔decimal duplication and gives the wire parsing a single tested home.
- Symmetric `From`/`To` naming avoids a `Cents`/`Dollars` property pair reading as the two *parts* of
  one amount rather than two representations of it.
- Immutable results cannot be accidentally mutated by consumers.

### Options Considered

| Option | Verdict | Reason |
|---|---|---|
| **`Money` value type + immutable results** | **Selected** | One home for wire parsing; safe defaults |
| cents `long` + `decimal` per field | Rejected | Conversion duplicated; `Cents`/`Dollars` misreads as parts |
| plain `decimal` | Rejected | Loses exact cents fidelity and the wire-string duality |

---

## Decision 9: Typed transport exception with delivery classification

### Context

Left raw, transport failures surface as four-plus BCL exception types whose inner shapes differ by
runtime (net48 wraps `WebException`; modern .NET wraps `SocketException`; a timeout is a
`TaskCanceledException`, indistinguishable from cancellation on net48). The distinction that actually
matters is financial: because there is no idempotency key (Decision 6), *"the request provably never
reached the service"* (safe to retry) versus *"the outcome is unknown"* (a re-POST may double-charge)
is the only signal that makes automatic retry safe.

### Decision

- `SmartConnectException` (base) and `SmartConnectTransportException` carrying a `Delivery` property
  (`SmartConnectRequestDelivery.NotSent` / `.Unknown`), with the original BCL exception as
  `InnerException` for logs only.
- A single internal pure-function classifier behind the one send path, so no call site can leak a raw
  transport exception. **Conservative rule:** only provably pre-send failures (DNS, TCP connect, TLS
  handshake) are `NotSent`; everything else — timeout, mid-exchange reset, response-read failure — is
  `Unknown`. Across an exception graph the verdict is `NotSent` only if at least one node is `NotSent`
  **and no node is `Unknown`** — `Unknown` always wins a mixed chain (the safe financial direction).
- **Contract:** if SmartConnect answered, you get a result; if we could not get an answer, you get
  `SmartConnectTransportException`. `ProcessTransactionAsync` **never throws for runtime conditions** —
  every operational failure is a result (`Status` + `FailureCause`: `None` / `ServiceError` /
  `TransportNotSent` / `TransportUnknown` / `StateStoreFailure`). One-shot, interactive methods
  (`PairAsync`, `GetLastTransactionResultAsync`'s POST phase) throw, since an exception is the natural
  shape there. Argument validation, `ObjectDisposedException`, and exceptions from a consumer's own
  `AuthorizeRequestAsync` callback are not covered — they are programming errors / the consumer's own code.
- **State-store boundary:** a failure of the pre-POST sentinel write surfaces as
  `Failed` / `StateStoreFailure` (nothing was sent — safe to retry); a failure to persist polling
  details *after* a successful POST takes the **best-effort happy path** (the transaction is irrevocably
  in flight and most likely succeeds — reporting failure would be wrong more often than right), logged
  Error, never re-thrown.

### Rationale

- One catchable type with a two-value enum is a contract a caller can hold in their head; the classifier
  does the inner-exception spelunking once, inside the library.
- A misclassified `Unknown` costs an unnecessary recovery check; a misclassified `NotSent` could cost a
  double charge — so the default errs toward `Unknown`.

### Options Considered

| Option | Verdict | Reason |
|---|---|---|
| **One `SmartConnectTransportException` + `Delivery` enum** | **Selected** | Handling is a policy switch; smallest surface; enum extends without new public types |
| Two exception subclasses | Rejected | Doubles the public surface for no structural gain |
| Let BCL exceptions propagate | Rejected | The exact problem being solved — many types, runtime-dependent shapes |

### Update (2026-07-02) — HTTP status buckets on the initial POST

The original decision classified *exceptions* (no response at all); which received **status codes**
constitute a "service rejection" was never adjudicated, and the implementation had read every non-2xx as
one. That mapping is unsafe for intermediary-generated statuses, so the POST response now splits:

- **4xx → `Failed` / `ServiceError`, sentinel closed.** A genuine verdict that the request was not
  processed. 429 included: rate-limited means refused, wherever it was generated.
- **5xx and 408 → `Unknown` / `TransportUnknown`, sentinel left pending.** 502/503/504 are routinely
  generated by an intermediary (load balancer, WAF, proxy) *after* the origin received the request — a
  504 literally means "upstream didn't answer in time" — so they are epistemically the same state as a
  transport timeout, which already mapped to `Unknown`. 408 nominally means the request was never fully
  received (which would make `Failed` safe), but intermediaries are unreliable about status discipline,
  and this decision's own rationale applies: a false `Unknown` costs one manual reconciliation; a false
  `Failed` ("blind retry will fail again") invites a re-tender over a possibly-live charge.

Extended 2026-07-07 — the same split now applies to the **non-financial** POST path (`LogonAsync`,
settlement inquiry/cutover, terminal status, journal, `ExecuteNonFinancialAsync`), which had also read every
non-2xx as a rejection. A 5xx/408 there maps through to the operation result's `Unknown`; a 4xx maps to
`Failed` and surfaces the service's error text. This matters most for the **state-changing settlement
cutover**: a 5xx after the service received the request must be `Unknown` (the cutover may have executed),
never `Failed` (which would invite a blind re-cutover). The non-financial path holds no sentinel, so there is
nothing to leave pending — the caller owns reconciliation of the `Unknown`.

The poll loop is unaffected (it already treated 5xx as transient and re-polled — it still holds a
polling URL, so it *can* re-ask; the POST path cannot).

---

## Decision 10: Absolute pre-POST gate, pre-sized sentinel, and verified correlation limits

### Context

With no idempotency key and (verified against the official docs) **no client-supplied reference field on
`POST /Transaction`** — the only correlation id is the server-generated `transactionId` in the POST
response — the pre-POST sentinel and the persisted polling details are the *entire* recovery story.
`Journal.GetTransResult` is flagged "no longer recommended" for async and is undocumented for parameters,
so Layer-2 recovery is a best-effort last-transaction fetch with heuristic matching, pending live
verification.

### Decision

1. **The pre-POST gate is absolute.** If the sentinel cannot be persisted, the transaction is refused
   (surfaced per Decision 9 as `Failed` / `StateStoreFailure`). No degraded mode, no override.
2. **The sentinel is pre-sized** — store implementations should reserve capacity comparable to the
   completed record (polling URL + token are long) so passing the gate genuinely predicts the post-POST
   update succeeding. The bundled file store pre-allocates; the contract docs carry the guidance for
   other implementations.
3. **Store implementations should briefly retry known-transient errors** (sharing/lock violations,
   DB deadlock/timeout) before throwing, so a gate refusal means "store actually unavailable", not "one
   blip". Bounded so tender start is not visibly delayed.
4. **Layer-2 recovery is documented as best-effort, not a safety net.** *(Superseded — see the
   2026-07-02 update below: the journal is a diagnostic, not a recovery layer of any strength.)*

### Rationale

- A transient store hiccup costs one safe retry (nothing was sent); a *persistent* store failure means
  the till likely cannot complete the sale anyway. The scenario where a degraded mode helps is narrow;
  the scenario where it hurts — a misconfigured store silently disabling crash recovery fleet-wide while
  payments appear to work — is severe and invisible. Retries + pre-sizing attack the legitimate
  availability concern (false refusals) without weakening the invariant.

### Trade-offs Accepted

- A till with a genuinely broken store cannot take payments until it is fixed — accepted because it is
  visible, bounded, and diagnosable, the opposite of the silent alternative.
- The file store keeps write-temp-then-replace: atomicity wins over space-prediction purity, so the gate
  is a good predictor, not a guarantee (a record exceeding the reservation grows the file and succeeds,
  with a Warning). "Transient" is defined affirmatively with a conservative no-retry default (disk-full
  throws immediately — retrying cannot free disk).

### Update (2026-06-16) — `Journal.GetTransResult` verified against a dev terminal

Probed directly against a physical PAX S920, with a two-register reproduction:

1. **It works** over async and returns the most-recent transaction's full result. The reported
   transaction's id is in the response's **`ReferenceId`** field, **not** the envelope `transactionId`
   (which is the journal query's own id).
2. **A legacy `POSReferenceID` is ignored** — a specific transaction cannot be targeted; the call only
   ever returns *the most-recent transaction on the terminal*.
3. **It is DEVICE-scoped, not register-scoped** — with two registers paired to one terminal, a query
   sent with register A's own registration returned register B's most-recent transaction.

**Consequence for Layer-2 recovery:**

- Auto-adopting "the device's last transaction" as ours is safe **only when the terminal serves a single
  register**. On a shared terminal a recovering register can be handed *another* register's transaction.
- Recovery code therefore must **match before adopting**: compare the returned transaction to the sentinel
  by **`AmountTotal`** — the only field carried across the gap (there is no shared id, and the terminal
  clock is unreliable for timestamp matching) — and on a non-match surface `Unknown` for reconciliation.
  A same-amount collision between two registers is unresolvable by this match; that residual risk folds
  into Decision 6's known hole. *(Superseded — see the 2026-07-02 update below: amount matching is not
  reliable evidence and no auto-adopt is sanctioned; the journal is diagnostic only.)*
- **Library support:** `SmartConnectTransactionResult.ReferenceId` surfaces the reported (recovered)
  transaction's id distinctly from the query's `TransactionId`; `AmountTotal` is the documented match
  field *(superseded — see the 2026-07-02 update: amount matching is not reliable and no auto-adopt is
  sanctioned)*, and `GetLastTransactionResultAsync` documents the device-scoped semantics.

### Update (2026-06-17) — recovery contract hardened; `SaleData` ruled out as a key

- **The library never auto-adopts.** `GetLastTransactionResultAsync` returns the device's last transaction as
  a *candidate* plus its match evidence (`ReferenceId`, `AmountTotal`); it makes no adopt decision and never
  asserts "this is yours." A recovery layer exists to *prevent* false certainty, so silently attaching the
  device's last transaction to our sale would defeat its purpose. The caller match-before-adopts (today:
  `AmountTotal`) and resolves anything short of a confident match to `Unknown`. *(Superseded — see the
  2026-07-02 update: no auto-adopt is sanctioned at all; amount matching is not reliable evidence.)*
- **How the caller confirms a candidate is a deferred, integrity-sensitive decision — not a glib prompt.** An
  "Is this your transaction? [Yes]" dialog to a busy operator degrades into a rubber-stamp, reintroducing the
  false certainty wearing a human fig-leaf. Whether to adopt automatically (single-register only), route to
  back-office reconciliation, or something else is left to the consumer and explicitly flagged UX-sensitive.
- **`SaleData` cannot serve as the correlation key (probed live 2026-06-17).** A purchase carrying a unique
  `SaleData` marker echoed it back in the transaction's **own** completed result, but **not** in a subsequent
  `Journal.GetTransResult`. So embedding a reference in `SaleData` gives Layer-2 recovery no exact key —
  `AmountTotal` remains the only field that survives the crash *and* appears in the journal. (Where `SaleData`
  does echo — the live result — Layer-1 recovery already holds the transaction-specific polling URL and needs
  no extra key.) Net: the journal narrows the gap in the common single-register case but cannot be made
  reliable; it is best-effort, never a safety net.

### Update (2026-07-02) — the journal is a diagnostic, not a recovery layer

Re-examined during integration testing, the "Layer 2" framing did not survive contact with retail
reality and is withdrawn:

- **Amount matching is not reliable evidence.** `AmountTotal` is the only field that crosses the crash
  gap (updates above), and in retail the same amount recurs constantly — single-item sales at standard
  price points. The dangerous case: the in-flight POST never reached the device, the device's actual
  last transaction is an unrelated prior same-amount sale, and an amount match silently attributes that
  outcome to this sale. Even the "single-register terminal" carve-out inherits this false-attribution
  risk, so no auto-adopt configuration is sanctioned at all.
- **Consequence:** when resuming the persisted polling URL cannot resolve an outcome (no URL was
  persisted, or the poll answers `PollingUrlInvalid`), the outcome is **`Unknown` → manual
  reconciliation**. There is no programmatic fallback; integrations should raise an alert so the
  operator or back office verifies against the terminal/acquirer records before re-tendering.
- **`GetLastTransactionResultAsync` stays in the API as a diagnostic.** The device's last transaction is
  useful supporting *evidence* during reconciliation and support investigations, but its result must
  never be adopted as a transaction's outcome, and its documentation now says so.
- Where earlier text in this document says "Layer 2 recovery", read it as historical: resume-by-polling-URL
  is the only recovery mechanism.

---

## Decision 11: A distinct result type for non-financial operations

**Status:** Added 2026-06-17 (after a live `Terminal.GetStatus` mis-map was observed).

### Context

Non-financial operations (terminal status, acquirer logon, settlement inquiry/cutover) share the polling
machinery with financial transactions but have no approve/decline outcome and no money fields. Routing their
responses through the financial outcome mapper — which keys off a `TransactionResult` code these bodies do not
carry — mis-reported success as failure: live, `Terminal.GetStatus` returning `Result=OK` / `Status=READY`
mapped to `SmartConnectTransactionStatus.Failed`.

### Decision

A small result hierarchy:

- `SmartConnectResult` (abstract base) — the common envelope: `TransactionId`, `ResponseTimestamp`, `RawData`.
- `SmartConnectTransactionResult : SmartConnectResult` — financial (`Status`, `FailureCause`, amounts, card
  fields, `ReferenceId`, `Receipt`).
- `SmartConnectOperationResult : SmartConnectResult` — non-financial: `SmartConnectOperationStatus`
  (`Succeeded` / `Failed` / `Unknown`) + `ErrorMessage`.

Each client method returns its **concrete** derived type — financial methods (incl. `GetLastTransactionResultAsync`,
which reports a recovered *transaction*) return `SmartConnectTransactionResult`; the four operation methods and
the `ExecuteNonFinancialAsync` escape hatch return `SmartConnectOperationResult`. The base is never returned, so
callers never downcast. Operation success is taken from the response's `Result == "OK"`; the payment-critical
poll loop is left untouched (the conversion happens at the operation methods' boundary).

### Rationale

- A non-financial operation genuinely has its own outcome — and it includes **`Unknown`**, which is
  load-bearing for the **state-changing settlement cutover** ("it may have executed"). A bool would lose that.
- A shared base avoids duplicating the envelope and lets internal logging/rendering take one type, while each
  derived type carries only fields that are meaningful for it.
- Deriving success from `Result == "OK"` is the signal the terminal actually uses, and leaves the payment hot
  loop unchanged.

### Trade-offs Accepted

- Breaking change to the four operation methods' return types — free pre-release.
- Operation-specific fields (a terminal's `Status`, settlement totals) stay in `RawData` until each response
  shape is verified live; typed accessors can be added later without breaking callers.
- The financial-shaped internal result is still produced and converted; the internal poll log still records the
  provisional financial status for these ops (a minor diagnostic wart, not a public-surface issue).

### Options Considered

| Option | Verdict | Reason |
|---|---|---|
| **Base + two derived result types** | **Selected** | Each type carries only relevant fields; shared envelope; no downcast |
| Two unrelated result types | Rejected | Duplicates the envelope and shared rendering for no gain |
| One type, fix the mapping in place | Rejected | Keeps money fields and a financial outcome enum on non-financial results |
| `bool Succeeded` instead of a status enum | Rejected | Loses `Unknown` — exactly the case cutover must not drop |

### Update (2026-06-17, later same day) — operation-result line narrowed after live verification

The original decision above routed **all four** non-financial methods to `SmartConnectOperationResult`, generalised
from the single `Terminal.GetStatus` shape available at the time. Capturing the other three live (against the dev
PAX S920) corrected that: `Acquirer.Logon`, `Acquirer.Settlement.Inquiry`, and `Acquirer.Settlement.Cutover` all
return a **transaction-shaped** envelope (`Result=OK`, `TransactionResult=OK-ACCEPTED`, plus `AcquirerRef`/
`TerminalRef`/`Receipt`) and map cleanly through the financial outcome mapper. Only `Terminal.GetStatus` has the
divergent shape (`Result=OK`, no `TransactionResult`, a `Status=READY` field) that the financial mapper can't read.

So the line is redrawn:

- **`SmartConnectOperationResult`:** `GetTerminalStatusAsync` (genuine status query) and the `ExecuteNonFinancialAsync`
  escape hatch (arbitrary types of unknown shape) only.
- **`SmartConnectTransactionResult`:** `LogonAsync`, `SettlementInquiryAsync`, `SettlementCutoverAsync` join the
  financial transactions and `GetLastTransactionResultAsync` — they route through the shared core directly (no
  `ToOperationResult` conversion), so the acquirer reference/receipt come through typed, and the state-changing
  cutover keeps its load-bearing `Unknown` (now `SmartConnectTransactionStatus.Unknown`). The accepted cost is that
  these results carry the financial money/card fields unused (empty defaults); the meaningful fields all fit.

This both matches the vendor's model (`Acquirer.*` are documented *transaction types* hitting `POST /Transaction`)
and simplifies the code. `SmartConnectOperationResult` remains justified by `Terminal.GetStatus`. The verification
is exactly what surfaced the over-broad original line.

---

## Decision 12: The consumer finalizes the recovery sentinel (Case C)

**Status:** Accepted 2026-07-08. Extends Decision 10's recovery model.

### Context

The completed-outcome path finalized the sentinel (moved it out of the pending/recovery scan) *before*
returning the result. A consumer that records the outcome after the return — e.g. via a downstream event or
decorator — can crash between the return and that durable write. Because the record is no longer pending,
crash recovery never re-surfaces it, and a real outcome (an approval the customer was charged for, or a
decline) is silently lost. "Returned" was being treated as "durably delivered".

### Decision

The library no longer finalizes a completed transaction's sentinel. On a terminal outcome it returns the
result with the record left **pending**; the consumer calls `UpdateCompletedAsync` only *after* it has durably
recorded the outcome (persist-before-complete), then `RemoveAsync`. `RemoveAsync`'s terminal-only guard is
unchanged. Scope is the completed-outcome path only — exhaustion (`MaxPollDuration`) still closes as
`Unknown`, and dispose already leaves the record pending.

### Rationale

A completed transaction's polling URL is idempotently re-pollable, so a pending record is replayable: a crash
before the consumer finalizes just means recovery re-polls and re-delivers the same outcome. Deferring the
finalize to after the consumer's durable write closes the window structurally rather than alerting on its
symptom. Self-healing: a failed `UpdateCompletedAsync` after a good persist is retried by replay.

### Trade-offs Accepted

- The library no longer guarantees the pending set drains — that is now the consumer's responsibility. A
  consumer that never calls `UpdateCompletedAsync` accumulates pending records that recovery re-polls on every
  pass; consumers should monitor pending-record age.
- The pending window now spans return→consumer-complete, so a consumer's recovery sweep can overlap a live
  operation for the same reference and re-deliver. Idempotent-by-`clientTransactionRef` persistence is the
  required mitigation; the library deliberately does not serialize (it is stateless per call).
- Moving completion ownership to the consumer is a contract change to `ISmartConnectTransactionState` affecting
  every consumer.

### Options Considered

| Option | Verdict | Reason |
|---|---|---|
| **Consumer finalizes (return + explicit complete)** | **Selected** | Closes the window structurally; fits a return-based consumer as a reorder; the consumer can make persist+complete atomic; no new API, no guard change |
| Callback/event delivery (library invokes a handler, finalizes on its success) | Rejected | Needs a consumer-side persist-confirmation point the library can call back into; forces control inversion and a persistence-pipeline restructure; returning a result alongside re-invites the loss |
| Non-breaking new "return-without-closing" entry point | Rejected | Spends permanent API surface to avoid a break that is free pre-1.0 with no stable consumers |
| Uniform "library never finalizes" (incl. the exhaustion path) | Deferred | Kept this change surgical to the completed-outcome money-loss window; the completed-vs-exhaustion asymmetry (and the recovery-exhaustion window) is a separate, larger decision |

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| QR transaction types (`QR.Merchant.Purchase`, `QR.Consumer.Purchase`, `QR.Refund`) | Structurally identical to card transactions (same polling/response, different `TransactionType`); the core can carry them, but they are not yet exercised. |
| Pre-auth / finalise (`Card.Authorise`, `Card.Finalise`) | Not yet needed; addable when a consumer requires it. |
| `SaleData` line-item attachment | A documented optional field worth supporting in a general-purpose library (the host accepts it and echoes it in the transaction's own result). Deferred as a feature; explicitly NOT a recovery aid — it is not echoed in the journal (Decision 10's 2026-06-17 update). |
| Multi-pinpad per register | SmartConnect pairing is 1:1 (register ↔ device). |

### Resolved former questions

- **Unpairing:** there is **no API** — unpairing is performed by a person at the terminal, and re-pairing
  a register to a different terminal auto-unpairs the previous one.
- **`merchantAccessToken`:** a bearer credential embedded in the polling URL query string — the GET poll
  authenticates via the URL. It is persisted by the state store (needed for recovery) and is **never**
  logged by the library; consumers must not log the full polling URL either.
- **Client reference field on `POST /Transaction`:** confirmed with the vendor — there is **none**; the only
  correlation id is the server-generated `transactionId`. The underlying design gap may get an idempotency-key
  fix 6–12 months out (Decision 6); not counted on.
- **`SaleData` echo:** probed live 2026-06-17 — echoed in the transaction's own completed result, **not** in
  `Journal.GetTransResult`, so it cannot serve as a journal correlation key (Decision 10's 2026-06-17
  update).
- **Non-financial operation status mapping:** the financial outcome mapper mis-reported non-financial success
  as `Failed` (live: `Terminal.GetStatus` `Result=OK`/`Status=READY` → `Failed`). Resolved by giving the genuine
  non-transaction ops their own `SmartConnectOperationResult` / `SmartConnectOperationStatus`, then **narrowed**
  (Decision 11 + its 2026-06-17 update): only `Terminal.GetStatus` and the `ExecuteNonFinancialAsync` escape
  hatch use it; the acquirer ops are transaction-shaped and return `SmartConnectTransactionResult`.
- **Vendor authentication model — resolved, no credential required.** The auth model is pairing + the
  registration triple + the per-transaction `merchantAccessToken` (in the polling URL). The documented API has
  no separate up-front credential, and live dev testing across pairing, transactions, journal, **and all four
  non-financial ops** worked with none. The production environment hasn't been exercised (no prod access), but
  the documented API contract is identical and has nowhere to carry an extra credential, so there is nothing
  environment-specific for the library to implement. `SmartConnectClientConfiguration.AuthorizeRequestAsync`
  remains as a defensive, non-breaking seam only — not because anything indicates a credential is needed.

### Still open

- **None on the library/design side.** The remaining steps are operational: flip the repo public and set up
  NuGet publish. (A typed accessor for `Terminal.GetStatus`'s `Status` field — currently `RawData`-only — is an
  optional, non-breaking future enhancement, not a gap.)
