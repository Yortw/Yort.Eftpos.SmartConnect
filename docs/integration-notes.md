# Integration Notes - what SmartConnect's design means for your POS

This document explains **what this library handles and what your POS still has to solve** based on the API provided by the SmartConnect vendor.
Nothing here is a defect report: these are characteristics of the platform, and they
apply to any POS integrating with it, not just to this library. Where the SmartConnect vendor has given guidance it is
noted as such, and observations made against a *test* terminal are labelled, because test and production
terminal behaviour is not always identical.

> If you want the reasoning behind the library's API shape - why outcomes are results rather than
> exceptions, why there is no `CancellationToken`, why the state store is mandatory - that is a separate
> document: [design-decisions.md](design-decisions.md).

## The short version

| Concern | This library | Your POS |
|---|---|---|
| Pairing a register to a terminal | Provides `PairAsync` | Operator workflow, and re-pairing after reinstall or terminal swap |
| Reaching the service | Retries, HTTP 429 backoff, honest failure classification | Behaviour when the internet is down - including whether you trade at all |
| Recording a transaction before it is sent | Writes a recovery sentinel and **refuses to send** if that write fails | Supplying a durable `ISmartConnectTransactionState` |
| Getting the outcome | Polls to a terminal state; resumes a persisted polling URL after a crash | Running recovery promptly on startup |
| Ambiguous outcomes | Reports `Unknown` with a `FailureCause` - never guesses | **Everything after that**: operator workflow, reconciliation, how the sale is settled |
| Cancelling in flight | Nothing - the platform has no cancel | Deciding what, if anything, your cancel button does |
| Signature slips | Nothing - the platform reports nothing about them | Cash-drawer behaviour and any signature workflow |
| Double-charge protection | Nothing - the platform has no idempotency key | Never blind-retrying a payment |

The rest of this document explains each row.

## Pairing is an on-site step

A register must be paired to a terminal before it can be used, and pairing generally requires action on the
terminal itself as well as in the POS. It is a one-off at install, and again whenever the terminal is
swapped or replaced - and, depending on how you generate your register id, possibly after a POS reinstall.

Because part of the process happens on the terminal, it usually cannot be completed by a remote support
agent alone; someone needs to be at the device.

**The library provides** `PairAsync`, and `SmartConnectRegisterId.Generate` - a deterministic UUID v5 from
your merchant and register names, so a reinstalled register derives the *same* id and keeps its existing
pairing rather than needing a new one.

**You must handle** the operator-facing pairing workflow and the re-pairing cases. Note there is no
unpairing API: unpairing is done by a person at the terminal, and re-pairing a register to a different
terminal automatically unpairs the previous one.

## The POS-to-terminal path runs through the cloud

Any integrated EFTPOS needs to reach a payment gateway to authorise. What is different here is that the
**POS-to-terminal conversation depends on the internet too**. If the POS cannot reach the service, it cannot
send anything to the terminal - even when the terminal itself is perfectly capable of taking a payment on
its own (offline/floor-limit modes, or a terminal with its own cellular connection).

"Cannot reach the service" is broader than a cut cable: a fault at the branch, a DNS problem, an upstream
network issue, or a service-side incident all present the same way to the POS.

The SmartConnect vendor's guidance for this situation is to run the transaction manually on the terminal and
record it in the POS afterwards.

**The library provides** honest failure reporting: a `SmartConnectTransportException` with a `Delivery`
property distinguishing "provably never sent" (safe to retry) from "may have been processed", and result
statuses rather than silent failures.

**You must handle** whether and how the business keeps trading. In practice that means some form of
manually-keyed payment type that records a payment taken on the terminal without electronic confirmation
from it - plus the staff training that goes with it. The library has no offline mode and no queue; there is
nothing to queue to, because the terminal is only reachable through the service.

## There is no POS-supplied transaction reference

Most EFTPOS integrations let the POS mint a reference *before* sending, then use it to ask "what happened to
this one?" SmartConnect does not accept a POS-generated reference, and the identifiers it does return come
back in the response.

The consequence: if the POS does not receive **and durably save** the initial response, it has no key with
which to ask the service what became of that transaction. This needs a failure inside a narrow window - a
power cut, network drop, or process restart between the request being sent and the response being saved - so
it is uncommon. It is worth knowing about because of what it costs when it happens: the outcome of a real
payment is unknown until a person establishes it.

**The library provides** the strongest protection the API allows. It writes a recovery sentinel *before*
every transaction POST and **will not send at all** if that write fails, so a transaction can never be in
flight without a local record that it was attempted. It then persists the polling details as soon as they
arrive.

**You must handle** providing a genuinely durable `ISmartConnectTransactionState` - this is the load-bearing
part, and a bundled file-based reference implementation is included for reference rather than as the answer
for every deployment. You must also handle the residual window itself, which the library cannot close.

## The recovery window is time-boxed

The service returns a URL to poll for the outcome. That URL carries an access token, and the token expires:
measured at roughly **15 minutes from the original transaction POST** - not from when the transaction
completes - in the development environment. We are not aware of a way to refresh or reissue it.

So even when the polling URL *was* saved, the outcome can only be recovered programmatically if recovery
reaches it inside that window. After that, a poll returns HTTP 401 and the situation is the same as having
lost the URL entirely.

There is evidence in the response payload that the transaction record itself is retained server-side for far
longer (on the order of 180 days), so the vendor's support should be able to confirm an outcome after the
fact - it simply is not available to the POS programmatically.

**The library provides** `ResumePollingAsync`, and surfaces an expired or unusable URL distinctly as
`FailureCause.PollingUrlInvalid` rather than spinning until timeout.

**You must handle** running recovery **promptly** - on startup, not on a slow background timer. A recovery
pass that runs hourly will find most of its work already unrecoverable.

## Some outcomes cannot be determined programmatically

When the above situations occur, the honest answer is that the POS does not know whether the customer was
charged. This library will tell you so - status `Unknown` - and will not guess in either direction. Treating
`Unknown` as approved risks giving away goods; treating it as declined risks charging twice.

Two approaches to resolving this are common in POS software, and both put a person in the loop:

- **A separate, manually-keyed payment type.** The ambiguous payment is treated as failed, and if staff can
  independently confirm the payment succeeded (they saw the terminal approve it, there is a terminal-printed
  receipt, the customer can show it in their banking app) they record it as a manually-keyed payment. The
  same payment type is usually needed for the offline case anyway, so the training overlaps.
- **Prompting the operator to choose.** On an ambiguous outcome, the operator is asked to record the payment
  as accepted or declined.

Both carry the same underlying risk - a payment recorded as taken that was not - but they do not carry it
equally. A second, deliberate payment entry is a higher bar than a single choice made under time pressure at
a queue. Whichever you choose, restricting it by permission and training for it are worth planning before
go-live rather than after. This library takes no position and implements neither; it reports the ambiguity
accurately and leaves the resolution to you.

**You must handle** all of it: the operator workflow, any reconciliation reporting, and how an unresolved
sale is eventually settled in your ledger.

## There is no way to cancel a transaction from the POS

SmartConnect provides no mechanism for the POS to cancel a payment or refund once it is under way - not even
while the customer is still choosing an account or entering a PIN.

This has a sharp consequence for UI design: a cancel button on the POS can only ever stop *the POS waiting*.
It cannot stop the customer paying. If staff cancel and the customer completes the payment anyway, the sale
has no record of a payment that really happened. Offering a control that appears to do something it cannot
is arguably worse than not offering it.

Transactions can still be cancelled **at the terminal**, and when the POS is still receiving updates it is
told the transaction was cancelled and can handle that normally.

If the POS stops waiting while the terminal is still working, the two are out of step: the terminal may
continue for some minutes after the POS has given up. Whatever the POS does at that point is a policy choice
it must make on incomplete information.

**The library provides** a configurable `MaxPollDuration`, after which polling returns `Unknown` - and,
deliberately, **no `CancellationToken`** on the transaction methods, because abandoning the wait cannot recall
the payment and the API offers nothing to cancel it with. Critically, poll exhaustion leaves the recovery
record *pending*, so a later recovery pass can still discover a late outcome while the polling URL is valid.

**You must handle** your own timeout policy and what the operator sees when it fires.

## Signature slips are not visible to the POS

The platform gives the POS no signal that a signature slip needs printing or was printed, and no way for the
POS to report whether the operator accepted a signature.

The SmartConnect vendor advises that in production the terminal always prints the signature slip itself -
even when terminal receipt printing is otherwise disabled - and prompts for signature confirmation on the
device. On a *test* terminal we observed a PIN-less credit transaction approved immediately with no slip
and no signature step, so we have not been able to reproduce the production behaviour in test. This is
worth confirming on the first live terminal during rollout rather than assuming either way.

If PIN-less credit transactions are in scope, two consequences follow. Printing the slip is the terminal's
job rather than the POS's, so it depends on that terminal being able to print at all - worth establishing
for a given deployment, though nothing the POS software can influence. Because the POS is never told a
slip was printed, it cannot know if or when to open the cash drawer for a signed slip; whichever way you
resolve that is a policy choice with a cash-control trade-off.

**The library provides** the terminal's receipt text on the result when the service returns one
(`Receipt`, fixed-width - render it monospaced).

**You must handle** drawer behaviour and any signature-related workflow.

## There is no idempotency key

The API has no idempotency mechanism, which is why so much of the above resolves to "ask a person". If a
request times out, resending it is not a safe retry - it is potentially a second charge.

**The library provides** the `Delivery` classification precisely so you can tell the two cases apart:
`NotSent` is provably safe to retry; `Unknown` is not. It also treats gateway-generated `5xx` and `408`
responses as `Unknown` rather than failed, because an intermediary can return those *after* the service
received the request.

**You must handle** never blind-retrying a payment. Route `Unknown` to a person.

---

None of this prevents SmartConnect working well in normal trading, and most transactions are unremarkable.
These characteristics matter at the edges - outages, interrupted transactions, PIN-less credit - and those
are the cases worth deciding on, and training for, before go-live rather than during an incident.

If you find that any of this has changed, or that a platform capability exists that this document says does
not, please open an issue - the notes here reflect what we have been able to verify, and the platform is not
static.
