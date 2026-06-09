using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// A reference <see cref="ISmartConnectTransactionState"/> implementation that stores one JSON file per
/// transaction in a directory. Suitable for samples and simple single-register integrations; products with a
/// database should provide their own store.
/// </summary>
/// <remarks>
/// Writes are atomic (write-temp-then-replace) so a crash mid-write cannot truncate a record. The directory
/// is per-store; point each register at its own directory. Files contain the polling URL, which carries a
/// bearer token — restrict access to the directory accordingly.
/// </remarks>
public sealed class FileBasedTransactionStateStore : ISmartConnectTransactionState
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

	private readonly string _directory;
	private readonly object _sync = new object();

	/// <summary>Creates a store backed by the given directory, creating it if it does not exist.</summary>
	/// <param name="directory">The directory in which to store transaction-state files.</param>
	/// <exception cref="ArgumentException"><paramref name="directory"/> is null, empty, or whitespace.</exception>
	public FileBasedTransactionStateStore(string directory)
	{
		if (string.IsNullOrWhiteSpace(directory))
		{
			throw new ArgumentException("Directory must not be null, empty, or whitespace.", nameof(directory));
		}

		_directory = directory;
		Directory.CreateDirectory(_directory);
	}

	/// <inheritdoc />
	/// <exception cref="IOException">The state file could not be written.</exception>
	public Task SaveTransactionAttemptAsync(string clientTransactionRef, string transactionType, long amountTotalCents)
	{
		var record = new StoredRecord
		{
			ClientTransactionRef = clientTransactionRef,
			TransactionType = transactionType,
			AmountTotalCents = amountTotalCents,
			CreatedAt = DateTimeOffset.UtcNow
		};

		lock (_sync)
		{
			WriteAtomic(PathFor(clientTransactionRef), record);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	/// <exception cref="InvalidOperationException">No state record exists for the reference, or it is unreadable/corrupt.</exception>
	/// <exception cref="IOException">The state file could not be written.</exception>
	public Task UpdatePollingDetailsAsync(string clientTransactionRef, string pollingUrl, string transactionId)
	{
		lock (_sync)
		{
			var record = ReadRequired(clientTransactionRef);
			record.PollingUrl = pollingUrl;
			record.TransactionId = transactionId;
			WriteAtomic(PathFor(clientTransactionRef), record);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	/// <exception cref="InvalidOperationException">No state record exists for the reference, or it is unreadable/corrupt.</exception>
	/// <exception cref="IOException">The state file could not be written.</exception>
	public Task UpdateCompletedAsync(string clientTransactionRef, SmartConnectTransactionStatus status)
	{
		lock (_sync)
		{
			var record = ReadRequired(clientTransactionRef);
			record.Status = status;
			record.CompletedAt = DateTimeOffset.UtcNow;
			WriteAtomic(PathFor(clientTransactionRef), record);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	/// <remarks>Individual unreadable/corrupt records and leftover temp files are skipped, not thrown.</remarks>
	/// <exception cref="IOException">The state directory could not be enumerated.</exception>
	public Task<IEnumerable<PendingTransaction>> GetPendingTransactionsAsync()
	{
		var results = new List<PendingTransaction>();

		lock (_sync)
		{
			foreach (var file in Directory.GetFiles(_directory))
			{
				// Match .json exactly via the extension, not a "*.json" glob (which can also match
				// "*.json.tmp" on Windows). Leftover .tmp files from an interrupted write are ignored.
				if (!string.Equals(Path.GetExtension(file), ".json", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var record = TryRead(file);
				if (record != null && record.Status == null)
				{
					results.Add(new PendingTransaction
					{
						ClientTransactionRef = record.ClientTransactionRef,
						PollingUrl = record.PollingUrl,
						TransactionId = record.TransactionId,
						CreatedAt = record.CreatedAt
					});
				}
			}
		}

		return Task.FromResult<IEnumerable<PendingTransaction>>(results);
	}

	/// <inheritdoc />
	/// <exception cref="InvalidOperationException">The record does not exist, is unreadable/corrupt, or has not reached a terminal state.</exception>
	/// <exception cref="IOException">The state file could not be deleted.</exception>
	public Task RemoveAsync(string clientTransactionRef)
	{
		lock (_sync)
		{
			var record = ReadRequired(clientTransactionRef);
			if (record.Status == null)
			{
				throw new InvalidOperationException($"Transaction '{clientTransactionRef}' has not reached a terminal state and cannot be removed.");
			}

			File.Delete(PathFor(clientTransactionRef));
		}

		return Task.CompletedTask;
	}

	// Escape so refs containing path-hostile characters (e.g. "branch/01-...") map to a safe, reversible file name.
	private string PathFor(string clientTransactionRef)
		=> Path.Combine(_directory, Uri.EscapeDataString(clientTransactionRef) + ".json");

	private StoredRecord ReadRequired(string clientTransactionRef)
	{
		var path = PathFor(clientTransactionRef);
		if (!File.Exists(path))
		{
			throw new InvalidOperationException($"No transaction state found for '{clientTransactionRef}'.");
		}

		var record = TryRead(path);
		if (record == null)
		{
			throw new InvalidOperationException($"Transaction state for '{clientTransactionRef}' is unreadable or corrupt.");
		}

		return record;
	}

	private static StoredRecord? TryRead(string path)
	{
		try
		{
			return JsonSerializer.Deserialize<StoredRecord>(File.ReadAllText(path), JsonOptions);
		}
		catch (IOException)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static void WriteAtomic(string path, StoredRecord record)
	{
		var json = JsonSerializer.Serialize(record, JsonOptions);
		var tempPath = path + ".tmp";

		File.WriteAllText(tempPath, json);

		// Replace is atomic when the destination exists; Move is atomic for the first write. Either way a
		// reader/recovery never sees a half-written .json.
		if (File.Exists(path))
		{
			File.Replace(tempPath, path, destinationBackupFileName: null);
		}
		else
		{
			File.Move(tempPath, path);
		}
	}

	private sealed class StoredRecord
	{
		public string ClientTransactionRef { get; set; } = string.Empty;
		public string? TransactionType { get; set; }
		public long AmountTotalCents { get; set; }
		public string? PollingUrl { get; set; }
		public string? TransactionId { get; set; }
		public SmartConnectTransactionStatus? Status { get; set; }
		public DateTimeOffset CreatedAt { get; set; }
		public DateTimeOffset? CompletedAt { get; set; }
	}
}
