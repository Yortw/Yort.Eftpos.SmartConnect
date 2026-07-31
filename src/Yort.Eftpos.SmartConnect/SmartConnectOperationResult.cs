namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The outcome of a NON-FINANCIAL SmartConnect operation — terminal status, acquirer logon, settlement
/// inquiry/cutover, or an arbitrary type sent via <c>ExecuteNonFinancialAsync</c>. It has no approve/decline
/// outcome and no money fields; see <see cref="SmartConnectTransactionResult"/> for financial transactions.
/// The operation-specific response fields (e.g. a terminal's <c>Status</c>, settlement totals) are exposed via
/// <see cref="SmartConnectResult.RawData"/>: the strongly-typed surface here is deliberately minimal until each
/// operation's response shape has been verified against the live environment, after which typed accessors can
/// be added without breaking callers.
/// </summary>
public sealed class SmartConnectOperationResult : SmartConnectResult
{
	/// <summary>
	/// Whether the operation succeeded, failed, or is of unknown outcome. Always handle
	/// <see cref="SmartConnectOperationStatus.Unknown"/> explicitly — for a state-changing operation such as
	/// settlement cutover it means the operation MAY have executed.
	/// </summary>
	public SmartConnectOperationStatus Status { get; init; } = SmartConnectOperationStatus.Unknown;

	/// <summary>
	/// A human-readable description from the service when the operation did not succeed: the failure reason when
	/// <see cref="Status"/> is <see cref="SmartConnectOperationStatus.Failed"/>, or the service/intermediary
	/// message (e.g. a 5xx body) when it is <see cref="SmartConnectOperationStatus.Unknown"/>. Null when none was
	/// available — a successful operation, or an Unknown from poll-timeout/dispose.
	/// </summary>
	public string? ErrorMessage { get; init; }
}
