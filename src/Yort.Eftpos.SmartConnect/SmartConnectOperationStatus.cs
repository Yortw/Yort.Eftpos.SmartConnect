namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The outcome of a non-financial SmartConnect operation (terminal status, acquirer logon, settlement
/// inquiry/cutover). Distinct from <see cref="SmartConnectTransactionStatus"/>: a non-financial operation has
/// no approve/decline notion — only whether it succeeded, failed, or is of unknown outcome.
/// </summary>
public enum SmartConnectOperationStatus
{
	/// <summary>
	/// The outcome could not be determined — the result was never retrieved (the poll timed out, the client
	/// was disposed, or the polling URL was rejected). For a STATE-CHANGING operation such as settlement
	/// cutover this means it MAY have executed; verify before re-issuing. The default.
	/// </summary>
	Unknown = 0,

	/// <summary>The operation completed successfully.</summary>
	Succeeded,

	/// <summary>
	/// The operation was reached and answered but did not succeed (e.g. the service rejected it, or the
	/// terminal could not perform it). See <see cref="SmartConnectOperationResult.ErrorMessage"/>.
	/// </summary>
	Failed
}
