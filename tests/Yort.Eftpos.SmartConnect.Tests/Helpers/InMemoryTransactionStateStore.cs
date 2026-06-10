using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.Tests.Helpers;

/// <summary>
/// A dictionary-backed <see cref="ISmartConnectTransactionState"/> for tests. Records every call in
/// <see cref="CallLog"/> (for ordering assertions) and exposes per-method throw hooks so failure paths can
/// be exercised deterministically.
/// </summary>
public sealed class InMemoryTransactionStateStore : ISmartConnectTransactionState
{
	public sealed class Record
	{
		public string ClientTransactionRef { get; set; } = string.Empty;
		public string? TransactionType { get; set; }
		public long AmountTotalCents { get; set; }
		public string? PollingUrl { get; set; }
		public string? TransactionId { get; set; }
		public SmartConnectTransactionStatus? Status { get; set; }
		public DateTimeOffset CreatedAt { get; set; }
	}

	public Dictionary<string, Record> Records { get; } = new Dictionary<string, Record>();

	/// <summary>Entries like "Save:ref", "UpdatePolling:ref", "UpdateCompleted:ref:Failed", "Remove:ref".</summary>
	public List<string> CallLog { get; } = new List<string>();

	public Exception? ThrowOnSave { get; set; }
	public Exception? ThrowOnUpdatePollingDetails { get; set; }
	public Exception? ThrowOnUpdateCompleted { get; set; }

	public Task SaveTransactionAttemptAsync(string clientTransactionRef, string transactionType, long amountTotalCents)
	{
		CallLog.Add("Save:" + clientTransactionRef);
		if (ThrowOnSave != null)
		{
			throw ThrowOnSave;
		}

		Records[clientTransactionRef] = new Record
		{
			ClientTransactionRef = clientTransactionRef,
			TransactionType = transactionType,
			AmountTotalCents = amountTotalCents,
			CreatedAt = DateTimeOffset.UtcNow
		};

		return Task.CompletedTask;
	}

	public Task UpdatePollingDetailsAsync(string clientTransactionRef, string pollingUrl, string transactionId)
	{
		CallLog.Add("UpdatePolling:" + clientTransactionRef);
		if (ThrowOnUpdatePollingDetails != null)
		{
			throw ThrowOnUpdatePollingDetails;
		}

		var record = Required(clientTransactionRef);
		record.PollingUrl = pollingUrl;
		record.TransactionId = transactionId;
		return Task.CompletedTask;
	}

	public Task UpdateCompletedAsync(string clientTransactionRef, SmartConnectTransactionStatus status)
	{
		CallLog.Add("UpdateCompleted:" + clientTransactionRef + ":" + status);
		if (ThrowOnUpdateCompleted != null)
		{
			throw ThrowOnUpdateCompleted;
		}

		Required(clientTransactionRef).Status = status;
		return Task.CompletedTask;
	}

	public Task<IEnumerable<PendingTransaction>> GetPendingTransactionsAsync()
	{
		var pending = Records.Values
			.Where(r => r.Status == null)
			.Select(r => new PendingTransaction
			{
				ClientTransactionRef = r.ClientTransactionRef,
				PollingUrl = r.PollingUrl,
				TransactionId = r.TransactionId,
				CreatedAt = r.CreatedAt
			})
			.ToList();

		return Task.FromResult<IEnumerable<PendingTransaction>>(pending);
	}

	public Task RemoveAsync(string clientTransactionRef)
	{
		CallLog.Add("Remove:" + clientTransactionRef);
		var record = Required(clientTransactionRef);
		if (record.Status == null)
		{
			throw new InvalidOperationException($"Transaction '{clientTransactionRef}' has not reached a terminal state and cannot be removed.");
		}

		Records.Remove(clientTransactionRef);
		return Task.CompletedTask;
	}

	private Record Required(string clientTransactionRef)
	{
		if (!Records.TryGetValue(clientTransactionRef, out var record))
		{
			throw new InvalidOperationException($"No transaction state found for '{clientTransactionRef}'.");
		}

		return record;
	}
}
