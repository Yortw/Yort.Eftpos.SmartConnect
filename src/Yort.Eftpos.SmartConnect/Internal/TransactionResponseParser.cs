using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Yort.Eftpos.SmartConnect.Internal;

/// <summary>Progress of a polled transaction.</summary>
internal enum PollProgress
{
	/// <summary>Still processing at the terminal.</summary>
	Pending,

	/// <summary>The cloud reported the transaction as delayed (terminal may be offline).</summary>
	Delayed,

	/// <summary>The transaction reached a terminal state; <see cref="PollResult.Result"/> is populated.</summary>
	Completed
}

/// <summary>The transactionId and polling URL extracted from the initial <c>POST /Transaction</c> response.</summary>
internal sealed class InitialTransactionResponse
{
	public string? TransactionId { get; init; }
	public string? PollingUrl { get; init; }
}

/// <summary>The interpreted result of a single poll.</summary>
internal sealed class PollResult
{
	public PollProgress Progress { get; init; }

	/// <summary>The completed result; populated only when <see cref="Progress"/> is <see cref="PollProgress.Completed"/>.</summary>
	public SmartConnectTransactionResult? Result { get; init; }

	/// <summary>The envelope transaction id, surfaced for every poll (not only completed ones) so a caller
	/// that seeded no id (the resume path) can still report which transaction a non-terminal exit concerns.</summary>
	public string? TransactionId { get; init; }
}

/// <summary>
/// Parses SmartConnect transaction responses. The envelope is camelCase (<c>transactionStatus</c>,
/// <c>transactionId</c>, <c>data</c>) while fields inside <c>data</c> are PascalCase, so this navigates by exact
/// property name rather than a global naming policy.
/// </summary>
internal static class TransactionResponseParser
{
	/// <summary>Extracts the transactionId and polling URL from the initial POST response.</summary>
	/// <exception cref="JsonException">The body is not valid JSON.</exception>
	public static InitialTransactionResponse ParseInitialResponse(string json)
	{
		using (var document = JsonDocument.Parse(json))
		{
			var root = document.RootElement;
			return new InitialTransactionResponse
			{
				TransactionId = GetString(root, "transactionId"),
				PollingUrl = TryGetData(root, out var data) ? GetString(data, "PollingUrl") : null
			};
		}
	}

	/// <summary>Interprets a poll response as pending, delayed, or completed-with-result.</summary>
	/// <exception cref="JsonException">The body is not valid JSON.</exception>
	public static PollResult ParsePollResponse(string json)
	{
		using (var document = JsonDocument.Parse(json))
		{
			var root = document.RootElement;
			var hasData = TryGetData(root, out var data);

			if (!Eq(GetString(root, "transactionStatus"), "COMPLETED"))
			{
				var progress = hasData && IsDelayed(data) ? PollProgress.Delayed : PollProgress.Pending;
				return new PollResult { Progress = progress, TransactionId = GetString(root, "transactionId") };
			}

			if (!hasData)
			{
				// COMPLETED but no data to interpret — the financial outcome is ambiguous (F10).
				return new PollResult
				{
					Progress = PollProgress.Completed,
					Result = new SmartConnectTransactionResult { Status = SmartConnectTransactionStatus.Unknown }
				};
			}

			var result = new SmartConnectTransactionResult
			{
				Status = MapOutcome(GetString(data, "TransactionResult"), GetString(data, "Result")),
				TransactionId = GetString(root, "transactionId"),
				// data.ReferenceId carries the SUBJECT transaction's id on the Journal.GetTransResult path,
				// where the envelope transactionId is only the query's own id (ADR Decision 10).
				ReferenceId = GetString(data, "ReferenceId"),
				ResponseTimestamp = GetString(root, "transactionTimeStamp"),
				AuthId = GetString(data, "AuthId"),
				AcquirerRef = GetString(data, "AcquirerRef"),
				TerminalRef = GetString(data, "TerminalRef"),
				CardPan = GetString(data, "CardPan"),
				CardType = GetString(data, "CardType"),
				AccountType = GetString(data, "AccountType"),
				Receipt = GetString(data, "Receipt"),
				AmountTotal = ReadMoney(data, "AmountTotal"),
				AmountSurcharge = ReadMoney(data, "AmountSurcharge"),
				AmountTip = ReadMoney(data, "AmountTip"),
				RawData = ReadRawData(data)
			};

			return new PollResult { Progress = PollProgress.Completed, Result = result };
		}
	}

	/// <summary>Maps the SmartConnect <c>TransactionResult</c> + <c>Result</c> pair to an outcome status.</summary>
	internal static SmartConnectTransactionStatus MapOutcome(string? transactionResult, string? result)
	{
		if (Eq(transactionResult, "OK-ACCEPTED") || Eq(transactionResult, "OK-DECLINED"))
		{
			if (Eq(result, "OK"))
			{
				return Eq(transactionResult, "OK-ACCEPTED")
					? SmartConnectTransactionStatus.Accepted
					: SmartConnectTransactionStatus.Declined;
			}

			// A verdict-bearing TransactionResult whose Result does not corroborate is CONTRADICTORY
			// evidence about a financial outcome — the terminal may have approved it. Asserting Failed
			// here invites a re-tender over a possibly-live charge; Unknown routes to reconciliation.
			// (The non-financial mapper applies the same never-assert-what-we-cannot-see rule.)
			return SmartConnectTransactionStatus.Unknown;
		}

		if (Eq(transactionResult, "CANCELLED"))
		{
			return Eq(result, "FAILED-INTERFACE")
				? SmartConnectTransactionStatus.DeviceOffline
				: SmartConnectTransactionStatus.Cancelled;
		}

		// No accept/decline claim present at all — a failure shape (known or novel). Deliberately NOT
		// Unknown: flipping this fallback would route every genuine failure to manual reconciliation.
		return SmartConnectTransactionStatus.Failed;
	}

	private static bool IsDelayed(JsonElement data)
		=> Eq(GetString(data, "TransactionResult"), "OK-DELAYED") || Eq(GetString(data, "Result"), "DELAYED-TRANSACTION");

	private static bool TryGetData(JsonElement root, out JsonElement data)
	{
		if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Object)
		{
			return true;
		}

		data = default;
		return false;
	}

	private static string? GetString(JsonElement element, string propertyName)
	{
		if (element.ValueKind == JsonValueKind.Object
			&& element.TryGetProperty(propertyName, out var value)
			&& value.ValueKind == JsonValueKind.String)
		{
			return value.GetString();
		}

		return null;
	}

	// Reuses MoneyJsonConverter (via the [JsonConverter] on Money) rather than re-implementing the
	// string/number cents parsing — keeps that logic in one place.
	private static Money ReadMoney(JsonElement data, string propertyName)
	{
		if (!data.TryGetProperty(propertyName, out var value)
			|| value.ValueKind == JsonValueKind.Null
			|| value.ValueKind == JsonValueKind.Undefined)
		{
			return default;
		}

		try
		{
			return JsonSerializer.Deserialize<Money>(value.GetRawText());
		}
		catch (JsonException)
		{
			// A malformed/empty amount must not discard an otherwise-complete outcome. Without this, a COMPLETED
			// body with a bad AmountTotal would throw, the poll loop would treat it as a garbled response, and a
			// real Accepted/Declined would degrade to Unknown after spinning to MaxPollDuration. Default the
			// amount instead — the Status still surfaces, and the raw value remains in RawData for diagnostics.
			return default;
		}
	}

	private static IReadOnlyDictionary<string, string> ReadRawData(JsonElement data)
	{
		var dictionary = new Dictionary<string, string>();
		foreach (var property in data.EnumerateObject())
		{
			dictionary[property.Name] = property.Value.ValueKind == JsonValueKind.String
				? (property.Value.GetString() ?? string.Empty)
				: property.Value.GetRawText();
		}

		// Wrap so the exposed IReadOnlyDictionary can't be cast back to the mutable Dictionary by a consumer.
		return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(dictionary);
	}

	private static bool Eq(string? actual, string expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
