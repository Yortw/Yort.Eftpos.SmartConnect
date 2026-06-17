# Yort.Eftpos.SmartConnect — full code review (2026-06-17)

Multi-round, fresh-reviewer-per-round, stop-gate on *no new Critical/High*. Scope: `src/` (library) + `tests/`,
light pass over `samples/Demo`. Fix-as-you-go for Critical/High; Medium/Low reported here for confirmation.
Each round uses a different lens. Findings are triaged against the actual code (✓ confirmed / ✗ false positive).

**Status:** in progress.

---

## Round 1 — correctness, concurrency/async, resource lifetime, error/failure-surfacing

**Verdict: no Critical, no High.** Financial-safety invariants (pre-POST sentinel gate, NotSent/Unknown
classification with Unknown winning mixed chains, sentinel-stays-pending on Unknown, never-log-the-token) all
hold under tracing.

### Critical — none.
### High — none.

### Medium

- [ ] **R1-M1 ✓ — `ObjectDisposedException` from a cross-thread `Dispose()` mid-send escapes `ProcessTransactionAsync`** (`SmartConnectClient.cs:960` + `TransportFailureClassifier.cs:32-41`). `SendAsync` only wraps `IsTransportFailure` exceptions; `ObjectDisposedException` isn't one, so disposing the owned `HttpClient` while a POST/poll `SendAsync` is awaiting throws it raw — violating the never-throws-for-runtime-conditions contract (Decision 9) on the documented dispose-mid-poll shutdown path. Trigger: thread A in `ProcessTransactionAsync` awaiting a send; thread B calls `Dispose()`. Narrow race, low harm (shutdown), but a core-contract breach. **Recommend fix:** catch `ObjectDisposedException` in `SendAsync` → `SmartConnectTransportException(Unknown)` (a disposed-mid-send POST genuinely is Unknown). *Borderline High — recommend fixing.*

- [ ] **R1-M2 ✓ — `Validate()` doesn't sanity-check `MaxPollDuration`/`BackoffCap`** (`SmartConnectClientConfiguration.cs:62-78`). A non-positive `MaxPollDuration` makes `deadline ≤ startedAt`, so the poll loop returns `Unknown` on the first check *without ever polling* — a POST that may have charged, reported Unknown with zero polls. A non-positive `BackoffCap` collapses 429 backoff. Programming errors, so throwing from `Validate()` is in-contract. **Recommend fix:** throw `ArgumentOutOfRangeException` for `MaxPollDuration <= TimeSpan.Zero` (ideally `>= PollInterval`) and `BackoffCap < PollInterval`; update the `Validate()` XML doc. *Recommend fixing.*

- [ ] **R1-M3 ✓ — `_disposed` is not `volatile`** (`SmartConnectClient.cs:34`, read at `:660`/`ThrowIfDisposed`, written in `Dispose()`). Cross-thread dispose (the intended shutdown path) has no memory barrier, so the poll loop may observe the write late (≈ one extra poll cycle). Low impact. **Recommend fix:** `volatile bool _disposed` or `Volatile.Read/Write`.

### Low

- [ ] **R1-L1 ✓ — `SaleData` serialised twice on the happy path** (`SmartConnectClient.cs:877` validate + `:842` send). Harmless; redundant for large line-item payloads. Optional: serialise once and thread the string through.
- [ ] **R1-L2 ✓ — `AmountCash` not validated for `Card.PurchasePlusCash`** (`ValidateTransactionRequest`). Zero/negative cash-out sent verbatim (vendor likely rejects → ServiceError, safe). Optional: validate `AmountCash > 0` and `<= AmountTotal` for that type. (Ties to the existing F9 amount-relationship verification note.)
- [ ] **R1-L3 ✓ — unmapped `COMPLETED` body maps to `Failed`, not `Unknown`** (`TransactionResponseParser.cs:128`, `MapOutcome` default). A COMPLETED body with `data` the mapper can't read becomes `Failed` (definite "no money moved") rather than the financially-safer `Unknown`. Judgement call — verify against a real declined/failed body shape before changing.

### Build/CI/Packaging
- **R1-B1 (non-defect)** — `LICENSE` file isn't packed; the package uses `<PackageLicenseExpression>MIT</PackageLicenseExpression>` which is valid and shows on nuget.org. No action unless embedding the file is specifically wanted.

### Docs — none.

### Solid (preserve)
Single send path wrapping every BCL transport exception (auth seam unbypassable); Unknown-wins-mixed-chain classifier; sentinel-stays-pending on every Unknown path; `SafeLog` strictly weaker than the path it diagnoses (token never a template arg); file store's non-reentrant semaphore + atomic write-temp-then-replace + affirmative-only transient retry; `Money` cents-authoritative with one wire-parsing home; `SmartConnectTransactionStatus.Unknown = 0` so defaults never read as Accepted.

---

## Round 2 — public API surface & type design

**Verdict: no Critical, no High.** The result hierarchy, immutability split, overloads-over-optionals, `Money`
naming, `SaleData` versioning, and nullability are all sound.

### Critical — none.
### High — none.

### Medium

- [ ] **R2-M1 ✓ — Stale/misleading XML doc on `SmartConnectTransactionRequest`** (`SmartConnectTransactionRequest.cs:3-7`). The type summary describes "`*Cents` properties" and "`decimal` convenience properties" — neither exists; `AmountTotal`/`AmountCash` are `Money`. A leftover from the pre-`Money` shape (Decision 8). It's the first doc a consumer reads on the most-used input type, describing an API that isn't there. **Recommend fix:** rewrite to describe `Money` (build with `Money.FromDecimal`/`FromCents`). *Confirmed against the file.*

### Low

- [ ] **R2-L1 ✓ — `SmartConnectRegisterId.Generate` returns `Guid` but every `POSRegisterID` is `string`** (`SmartConnectRegisterId.cs:34`). Consumer must `.ToString()` to bridge, and the *stored* string must be byte-identical across pairing + every transaction (mismatch forces re-pairing). **Fix:** return the canonical `string`, or `///`-note `Guid.ToString("D")` (lowercase) used consistently.
- [ ] **R2-L2 ✓ (minor) — `RawData` backing is a mutable `Dictionary`** (`TransactionResponseParser.cs:171`), exposed as `IReadOnlyDictionary?` (good) but cast-back-mutable. Optional: wrap in `ReadOnlyDictionary` at the boundary.
- [ ] **R2-L4 ✓ — Deferred `TransactionType` constants over-promise** (`SmartConnectTransactionType.cs`). `CardAuthorise`/`CardFinalise`/QR constants are published, but the request type has no `AmountAuth`/`AmountFinal` field to express them. Documented deferral. **Fix:** `///`-note those constants (request type doesn't yet carry their amount fields), or drop until supported.
- [ ] **R2-L5 (was R2-M2, downgraded) — `SmartConnectException` public ctors widen a never-thrown base.** Contract is sound; only `SmartConnectTransportException` is thrown. Optional hardening: `private protected`/`internal` constructors. Not a defect.
- [ ] **R2-D2 (Low) — `Money.FromDecimal` rounding is advisory-not-enforced** (`Money.cs:31-35`): 3+ dp silently rounds (half-away-from-zero). Deliberate; optional `///` clarification.

### Build/CI/Packaging
- **Observation (no action):** exception types aren't `[Serializable]`. Recommended default on modern .NET; cross-AppDomain/remoting on `net48` would fail — almost certainly irrelevant (remoting rare, `BinaryFormatter` dead).

### Solid (API/type-design)
Two-type result hierarchy (base never returned, no downcasting); `Unknown = 0` on both status enums; `Money` `From`/`To` symmetry; correct produced-immutable / input-mutable split; overloads not optional params across the client; `SaleData` namespace-versioning with abstract `Version`; honest nullability; exemplary `POSBusinessName`-vs-`POSVendorName` docs.

---

## Round 3 — test-suite quality + docs/packaging

**Verdict: no Critical, no High.** The safety contract is well-pinned with negative/invariant cases; the
protocol-fake rule (literal expected wire bytes, encoder not reused) is followed; determinism is clean (virtual
clock/poll-delay, no `WaitAsync`-masks-`TimeoutException` anti-pattern); the file store has its own thorough
corruption/transient-retry/atomicity/async-not-sync tests.

### Critical — none.
### High — none.

### Medium

- [ ] **R3-M1 ✓ — A malformed `AmountTotal` in a COMPLETED body degrades a known outcome to `Unknown` (untested)** (`TransactionResponseParser.cs:159-169` `ReadMoney` → `MoneyJsonConverter.cs:26`; poll loop `SmartConnectClient.cs:730-739` catches `JsonException` as transient). A structurally-valid COMPLETED body with `TransactionResult=OK-ACCEPTED` but an empty/non-numeric `AmountTotal` throws in `ReadMoney`, is caught as a "garbled body," retries to `MaxPollDuration`, and returns `Unknown` — discarding the real Accepted/Declined. Fails *safe* (Unknown ≠ approved), but loses a known outcome. **Recommend fix (TDD):** `ReadMoney` tolerates an unparseable amount as `default(Money)` so `Status` still surfaces; add a parser test for a COMPLETED body with `AmountTotal:""`.
- [ ] **R3-M2 ✓ — `BackoffCap` unvalidated (same family as R1-M2)** (`SmartConnectClientConfiguration.cs:62-78`). Zero/negative `BackoffCap` collapses 429 backoff + the `Retry-After` clamp → tight re-poll storm. **Fix:** add `BackoffCap >= PollInterval` to `Validate()` (fold in with R1-M2) + a config test.

### Low
- [ ] **R3-L1 ✓ (minor) — `InMemoryTransactionStateStore` throw-hooks throw synchronously, not as a faulted `Task`** (`tests/.../Helpers/InMemoryTransactionStateStore.cs:38-41`). No current call site differs, but the real store faults the task. Optional: `Task.FromException(...)`.

### Build/CI/Packaging
- [ ] **R3-P1 ✓ — README contradicts the csproj/LICENSE and will render wrong on nuget.org** (`README.md:5`, `:131-133`). Says "not yet released… build from source" and "MIT (to be added)" while the csproj sets `PackageLicenseExpression=MIT`, `LICENSE` exists, and the README is the packed `PackageReadmeFile`. **Fix:** state MIT + link LICENSE; soften the "not yet released" banner at publish time.
- [ ] **R3-P2 ✓ — README relative doc link breaks in the packaged README** (`README.md:129`, `docs/design-decisions.md`). nuget.org resolves relative links against the package page (no `docs/`). **Fix:** use the absolute GitHub URL.

### Docs
- [ ] **R3-D1 ✓ — README quick-start step 2 uses undeclared `registerId`/`saleReference`** (`README.md:60,63`); the snippet won't compile as written. **Fix:** declare `registerId` from `SmartConnectRegisterId.Generate(...).ToString()` above the block (or inline it).
- **R3-D2 (verified, no action):** `docs/design-decisions.md` is internally consistent with the code (Decisions 9/10/11 spot-checked against the implementation).

### Solid (tests/docs)
Financial-safety invariants tested as requirements with negative cases (pre-POST gate → StateStoreFailure + 0 requests + no phantom; NotSent/Unknown in both net48/modern shapes + mixed-chain; sentinel-stays-pending asserted; 429 backoff + every Retry-After variant pinned to literal delays); literal-wire-byte protocol-fake assertions (encoder not reused); virtual-clock determinism; counting-fake invariants (RequestCount==0, exact poll counts, armed throw-hooks); file-store corruption/transient/atomicity/async-not-sync coverage; exhaustive token-never-logged sweeps.

---

## Gate result & disposition

**Converged.** Three rounds, three distinct lenses (correctness/concurrency · API/type-design · tests/docs-packaging), **zero Critical and zero High** in every round → stop (no Critical/High to fix-as-you-go). The library is in strong shape; everything below is Medium/Low for confirm-then-fix.

### Recommended before publishing the pre-release (cheap, real value)
- **R3-P1, R3-P2, R3-D1** — README drift (licence/banner, relative link, non-compiling snippet); the README ships in the package.
- **R2-M1** — stale `*Cents`/`decimal` doc on `SmartConnectTransactionRequest`.
- **R1-M2 + R3-M2** — `Validate()` sanity-checks for `MaxPollDuration` and `BackoffCap` (one change).
- **R3-M1** — `ReadMoney` tolerates a malformed amount so a real outcome isn't lost (TDD).
- **R1-M1** — `SendAsync` translates `ObjectDisposedException` → `Unknown` (upholds never-throws on dispose-mid-poll).
- **R1-M3** — `volatile bool _disposed`.

### Optional / lower
R1-L1 (double-serialise SaleData), R1-L2 (AmountCash validation), R1-L3 (unmapped-COMPLETED → Failed-vs-Unknown — needs a real body shape to decide), R2-L1 (Generate returns Guid not string), R2-L2 (RawData ReadOnlyDictionary), R2-L4 (deferred type-constant doc note), R2-L5 (exception ctor visibility), R2-D2 (Money rounding doc), R3-L1 (fake faulted-task). No-action: the `[Serializable]` observation.
