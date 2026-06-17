using System.Collections.Generic;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The response fields common to every SmartConnect operation, whether a financial transaction
/// (<see cref="SmartConnectTransactionResult"/>) or a non-financial operation
/// (<see cref="SmartConnectOperationResult"/>). This base type is never returned on its own — each client
/// method returns the concrete derived type for the operation it performs, so callers never downcast.
/// </summary>
public abstract class SmartConnectResult
{
	/// <summary>The server-issued id of this exchange (the response envelope's <c>transactionId</c>).</summary>
	public string? TransactionId { get; init; }

	/// <summary>
	/// The raw response timestamp string. This is a non-ISO, vendor-specific format — treat as opaque and do
	/// not parse it.
	/// </summary>
	public string? ResponseTimestamp { get; init; }

	/// <summary>Any response fields not mapped to a strongly-typed property, for diagnostics.</summary>
	public IReadOnlyDictionary<string, string>? RawData { get; init; }
}
