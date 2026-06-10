using System.Collections.Generic;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The outcome and details of a completed (or abandoned) transaction. Amounts are authoritative in minor
/// units (cents); the <see cref="decimal"/> convenience properties expose them as dollars.
/// </summary>
public sealed class SmartConnectTransactionResult
{
	/// <summary>The terminal outcome. Always handle <see cref="SmartConnectTransactionStatus.Unknown"/> explicitly.</summary>
	public SmartConnectTransactionStatus Status { get; init; } = SmartConnectTransactionStatus.Unknown;

	/// <summary>
	/// Why the transaction did not complete normally (ADR Decision 9). <see cref="SmartConnectFailureCause.None"/>
	/// for normal outcomes including Declined; see the enum values for retry guidance.
	/// </summary>
	public SmartConnectFailureCause FailureCause { get; init; } = SmartConnectFailureCause.None;

	/// <summary>The server-issued transaction id.</summary>
	public string? TransactionId { get; init; }

	/// <summary>The acquirer authorisation id, when approved.</summary>
	public string? AuthId { get; init; }

	/// <summary>The acquirer reference.</summary>
	public string? AcquirerRef { get; init; }

	/// <summary>The terminal reference.</summary>
	public string? TerminalRef { get; init; }

	/// <summary>The masked card PAN (e.g. <c>....1234</c>).</summary>
	public string? CardPan { get; init; }

	/// <summary>The card scheme/type (e.g. <c>VISA</c>).</summary>
	public string? CardType { get; init; }

	/// <summary>The account type selected (e.g. <c>CREDIT</c>, <c>CHEQUE</c>, <c>SAVINGS</c>).</summary>
	public string? AccountType { get; init; }

	/// <summary>The total amount.</summary>
	public Money AmountTotal { get; init; }

	/// <summary>The surcharge amount applied by the terminal.</summary>
	public Money AmountSurcharge { get; init; }

	/// <summary>The tip amount applied at the terminal.</summary>
	public Money AmountTip { get; init; }

	/// <summary>
	/// The device-generated receipt text (fixed-width, newline-delimited). Render as-is in a monospaced
	/// context; do not attempt to parse it into fields.
	/// </summary>
	public string? Receipt { get; init; }

	/// <summary>
	/// The raw response timestamp string. This is a non-ISO, vendor-specific format — treat as opaque and do
	/// not parse it.
	/// </summary>
	public string? ResponseTimestamp { get; init; }

	/// <summary>Any response fields not mapped to a strongly-typed property, for diagnostics.</summary>
	public IReadOnlyDictionary<string, string>? RawData { get; init; }
}
