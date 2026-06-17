# SaleData support — design

**Date:** 2026-06-17 · **Status:** Approved (pending spec review) · **Branch:** `feat/saledata`

## Goal

Let callers attach the SmartConnect optional `SaleData` payload (sale/line-item/customer metadata) to a
transaction, as a strongly-typed, versioned model that the library serialises onto the wire. SaleData is a
documented optional field worth supporting in a general-purpose library; the host accepts it and echoes it in
the transaction's own completed result.

## Non-goals

- **Not a recovery/correlation aid.** Probed live 2026-06-17: `SaleData` is request-only — it is **not** echoed
  in `Journal.GetTransResult`, so it cannot key Layer-2 recovery (see `docs/design-decisions.md` Decision 10).
  This feature exists for its own sake (line-item/sale metadata), not the reliability gap.
- **No format invention.** We do not impose a numeric encoding the vendor docs leave unspecified.

## Typing rule (the core principle)

**Model the format only where the docs actually specify it; expose everything else as the faithful `string`
the vendor declares.**

- **Monetary fields → `string`.** The docs type every amount as `string` with **no** encoding specified and
  **no** populated examples anywhere on the SaleData reference page (verified 2026-06-17, two extractions):
  cents (`"1999"`) vs decimal (`"19.99"`) is undocumented. These read as display figures, and the library never
  interprets them, so we pass the caller's string through verbatim. `Money` is reserved for the one place the
  encoding is **verified** — the transaction `AmountTotal` (cents-string).
- **`quantity` → `string`.** As declared; never `int` (and we do not impose `decimal`).
- **`createdAt` / `updatedAt` → `DateTimeOffset?`.** Here the docs *do* specify the format ("ISO 8601 UTC"), so
  typing them is faithful; the library serialises to ISO 8601.
- ids / names / codes / `barcode` → `string`, as declared.

## Public API

### Version-agnostic base (root namespace `Yort.Eftpos.SmartConnect`)

```csharp
public abstract class SmartConnectSaleData
{
    // The wire schema's root `version`. The one cross-version invariant; serialised at the envelope
    // level by the library, not inside the saleData body.
    public abstract string Version { get; }
}
```

### V1 model (namespace `Yort.Eftpos.SmartConnect.SaleData.V1`)

A future schema revision becomes a new `…SaleData.V2` namespace; V1 and V2 coexist, no caller code moves.

```csharp
public sealed class SaleData : SmartConnectSaleData
{
    public override string Version => "1.0.0";

    public string? SaleId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? TotalAmount { get; set; }        // required by the wire schema
    public string? TotalTax { get; set; }           // required by the wire schema
    public string? TotalTips { get; set; }
    public string? TotalSurcharge { get; set; }
    public string? ReturnFor { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public IList<LineItem>? LineItems { get; set; }   // null (default) → field omitted from the wire
}

public sealed class LineItem
{
    public string? LineId { get; set; }
    public string? SequenceNumber { get; set; }
    public string? ProductId { get; set; }
    public string? ProductName { get; set; }        // required by the wire schema
    public string? ProductDescription { get; set; }
    public IList<Category>? Categories { get; set; }  // null (default) → field omitted from the wire
    public string? BrandId { get; set; }
    public string? BrandName { get; set; }
    public string? Quantity { get; set; }           // required; string (not int/decimal)
    public string? UnitPrice { get; set; }          // required
    public string? UnitTax { get; set; }            // required
    public string? UnitDiscount { get; set; }
    public string? TotalPrice { get; set; }         // required; negative for returns
    public string? TotalTax { get; set; }           // required
    public string? TotalDiscount { get; set; }
    public string? ModifierFor { get; set; }
    public string? SkuCode { get; set; }
    public string? Barcode { get; set; }
}

public sealed class Category
{
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }       // required by the wire schema
    public string? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
}
```

Types are **mutable** (object-initialiser ergonomics) — consumer-built input, per the project convention that
only library-*produced* types are immutable.

### Request property

```csharp
// on SmartConnectTransactionRequest
public SmartConnectSaleData? SaleData { get; set; }
```

Typed to the base, not `object`: it conveys intent, accepts any of our versioned types, and lets a third party
who must step outside our schema derive from `SmartConnectSaleData` (set their own `Version` + fields) — the
library serialises the **runtime** type, so their fields go out. `object?` would buy nothing extra and lose all
signal of intent.

The library does **not** validate the *content* of the amount/quantity strings (their encoding is unspecified —
see "Open / unverified"); it passes them through verbatim. The request-property `///` says so, so the absence of
validation is discoverable at the call site.

## Wire format & serialisation

The `SaleData` form field on `POST /Transaction` carries a URL-encoded serialised JSON envelope:

```json
{ "version": "1.0.0", "saleData": { "totalAmount": "...", "totalTax": "...", "lineItems": [ ... ] } }
```

- `version` at the root; the rest nested under `saleData`.
- The library composes the envelope: `version` = `saleData.Version`; the `saleData` body is the serialised
  object with `Version` excluded (`[JsonIgnore]` on `Version`, or composed explicitly).
- **Serialise against the runtime type** — `JsonSerializer.Serialize(saleData, saleData.GetType(), options)`.
  Serialising a base-typed reference emits only the base's properties (it would send `{"version":"1.0.0"}` with
  an empty body); the runtime type captures the V1 (and any third-party) properties. **This trap is pinned by a
  test.**
- camelCase property naming (`JsonNamingPolicy.CamelCase` maps `TotalAmount`→`totalAmount`, `LineItems`→
  `lineItems`, etc.); `DateTimeOffset` → ISO 8601; null members omitted (`DefaultIgnoreCondition =
  WhenWritingNull`). Collections are **null by default**, so an *unset* `LineItems`/`Categories` is omitted from
  the JSON entirely (no `"lineItems":[]`); a caller who explicitly assigns an empty list gets `[]` (their choice).
- **SaleData is serialised up front, before the recovery sentinel is written.** A caller-supplied
  `SmartConnectSaleData` whose runtime type cannot be serialised (a non-serialisable property or reference cycle
  on a third-party derived type) throws `ArgumentException` from `ProcessTransactionAsync` *before* any sentinel
  is persisted or any HTTP request is made — bad caller input throws (per Decision 9); it never leaves a dangling
  sentinel or a half-sent transaction. The V1 types are always serialisable.
- Added to the POST body only when `request.SaleData != null`; the existing financial path (amounts, sentinel,
  poll loop) is otherwise untouched.

## Components touched

- New: `SmartConnectSaleData.cs` (base), `SaleData/V1/SaleData.cs`, `SaleData/V1/LineItem.cs`,
  `SaleData/V1/Category.cs`, and an internal `SaleDataSerializer` (envelope composition + runtime-type
  serialisation + options).
- Modified: `SmartConnectTransactionRequest.cs` (add `SaleData` property), `SmartConnectClient.PostTransactionAsync`
  (emit the field when set).
- The demo gains an optional SaleData on one purchase path (illustrative) — secondary, may be deferred.

## Testing

- **Envelope shape:** a populated V1 SaleData serialises to `{"version":"1.0.0","saleData":{...}}` with `version`
  at root and absent from the nested body.
- **Runtime-type trap (the key test):** a `SmartConnectSaleData`-typed reference to a V1 instance serialises the
  V1 properties (not just `version`) — would fail under naive base-typed serialisation.
- **Wire bytes:** `PostTransactionAsync` includes a correctly URL-encoded `SaleData=` pair only when set, and
  omits it entirely when `SaleData` is null (the negative/invariant case).
- **Field fidelity:** required fields present; camelCase names; `DateTimeOffset` → ISO 8601; line items and
  nested categories round-trip into the JSON.
- **Third-party derived type:** a custom `SmartConnectSaleData` subclass serialises its own properties and its
  own `Version`.
- **Hostile string content:** a string field containing `&`, `=`, `%`, `"`, a control char and non-ASCII unicode
  round-trips intact through JSON-escape + URL-encode (parse the URL-decoded `SaleData` field back to the exact
  original), and the form body still splits correctly on `&`/`=`. (Wire-correctness, not cosmetics.)
- **Unserialisable SaleData:** a derived type that fails to serialise causes `ProcessTransactionAsync` to throw
  `ArgumentException` with **zero** HTTP requests and **no** sentinel persisted.

## Open / unverified

- **Monetary encoding is undocumented.** Mitigated by exposing amounts as `string` (we never interpret them). If
  the format is later confirmed with Shift4 / via a receipt, typed convenience (e.g. `Money`/`decimal` overloads)
  can be added without breaking callers.
- **SaleData size is unbounded by the library.** A large line-item payload URL-encodes onto a single form field;
  the host's request-body / field-length limits are unverified and not enforced here — caller's responsibility.
- The demo SaleData wiring is illustrative; production line-item construction is the consumer's concern.
