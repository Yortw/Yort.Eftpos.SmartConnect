namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The outcome and details of a completed (or abandoned) financial transaction. Amounts are authoritative in
/// minor units (cents); use <see cref="Money.ToDecimal"/> when you need dollars. Common envelope fields
/// (<see cref="SmartConnectResult.TransactionId"/>, <see cref="SmartConnectResult.RawData"/>, etc.) are on the
/// base type.
/// </summary>
public sealed class SmartConnectTransactionResult : SmartConnectResult
{
	/// <summary>The terminal outcome. Always handle <see cref="SmartConnectTransactionStatus.Unknown"/> explicitly.</summary>
	public SmartConnectTransactionStatus Status { get; init; } = SmartConnectTransactionStatus.Unknown;

	/// <summary>
	/// Why the transaction did not complete normally (ADR Decision 9). <see cref="SmartConnectFailureCause.None"/>
	/// for normal outcomes including Declined; see the enum values for retry guidance.
	/// </summary>
	public SmartConnectFailureCause FailureCause { get; init; } = SmartConnectFailureCause.None;

	/// <summary>
	/// A human-readable failure reason from the service when the transaction did not complete (for example a
	/// service rejection message such as "This register is not paired to a device", or a gateway/proxy message
	/// on an ambiguous <see cref="SmartConnectTransactionStatus.Unknown"/> outcome). Null on success. This is
	/// diagnostic/display text only — branch on <see cref="Status"/> and <see cref="FailureCause"/>, never on
	/// this string.
	/// </summary>
	public string? ErrorMessage { get; init; }

	/// <summary>
	/// The id of the transaction actually being <em>reported</em>, when it differs from
	/// <see cref="SmartConnectResult.TransactionId"/>. Populated from the response's <c>ReferenceId</c> field,
	/// which <c>Journal.GetTransResult</c> uses to carry the reported (last) transaction's id while the
	/// envelope <see cref="SmartConnectResult.TransactionId"/> identifies the journal query itself (ADR
	/// Decision 10). Null on the normal transaction path, where
	/// <see cref="SmartConnectResult.TransactionId"/> already identifies the transaction.
	/// </summary>
	public string? ReferenceId { get; init; }

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
}
