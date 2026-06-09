using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Yort.Eftpos.SmartConnect.Tests;

public sealed class FileBasedTransactionStateStoreTests : IDisposable
{
	private readonly string _directory;

	public FileBasedTransactionStateStoreTests()
	{
		_directory = Path.Combine(Path.GetTempPath(), "scstore-" + Guid.NewGuid().ToString("N"));
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_directory))
			{
				Directory.Delete(_directory, recursive: true);
			}
		}
		catch
		{
			// best-effort cleanup
		}
	}

	private FileBasedTransactionStateStore NewStore() => new FileBasedTransactionStateStore(_directory);

	[Fact]
	public void Constructor_CreatesMissingDirectory()
	{
		Assert.False(Directory.Exists(_directory));
		_ = NewStore();
		Assert.True(Directory.Exists(_directory));
	}

	[Fact]
	public async Task SaveTransactionAttempt_ThenGetPending_ReturnsSentinelWithNoPollingUrl()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		var pending = (await store.GetPendingTransactionsAsync()).ToList();

		var record = Assert.Single(pending);
		Assert.Equal("ref-1", record.ClientTransactionRef);
		Assert.Null(record.PollingUrl);
	}

	[Fact]
	public async Task UpdatePollingDetails_RecordsPollingUrlAndTransactionId()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		await store.UpdatePollingDetailsAsync("ref-1", "https://poll/here?merchantAccessToken=abc", "txn-1");

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal("https://poll/here?merchantAccessToken=abc", record.PollingUrl);
		Assert.Equal("txn-1", record.TransactionId);
	}

	[Fact]
	public async Task UpdateCompleted_RemovesFromPending()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		await store.UpdateCompletedAsync("ref-1", SmartConnectTransactionStatus.Accepted);

		Assert.Empty(await store.GetPendingTransactionsAsync());
	}

	[Fact]
	public async Task RemoveAsync_OnCompletedRecord_DeletesIt()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		await store.UpdateCompletedAsync("ref-1", SmartConnectTransactionStatus.Accepted);

		await store.RemoveAsync("ref-1");

		Assert.Empty(Directory.GetFiles(_directory, "*.json"));
	}

	[Fact]
	public async Task RemoveAsync_OnPendingRecord_Throws()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);

		await Assert.ThrowsAsync<InvalidOperationException>(() => store.RemoveAsync("ref-1"));
	}

	// F6: a kill mid-write can leave a *.tmp; recovery must ignore it, not crash or surface it as pending.
	[Fact]
	public async Task GetPending_IgnoresLeftoverTempFiles()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("ref-1", SmartConnectTransactionType.CardPurchase, 500);
		File.WriteAllText(Path.Combine(_directory, "ref-2.json.tmp"), "{ partial, not valid json");

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal("ref-1", record.ClientTransactionRef);
	}

	// F6: a corrupt record must not crash recovery for the other (valid) records.
	[Fact]
	public async Task GetPending_SkipsCorruptRecord_WithoutCrashing()
	{
		var store = NewStore();
		await store.SaveTransactionAttemptAsync("good", SmartConnectTransactionType.CardPurchase, 500);
		File.WriteAllText(Path.Combine(_directory, "corrupt.json"), "{ this is not valid json");

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal("good", record.ClientTransactionRef);
	}

	// Refs with filename-hostile characters must round-trip.
	[Fact]
	public async Task HandlesRefsWithPathUnsafeCharacters()
	{
		var store = NewStore();
		var ref1 = "branch/01-9444ae07";
		await store.SaveTransactionAttemptAsync(ref1, SmartConnectTransactionType.CardPurchase, 500);

		var record = Assert.Single(await store.GetPendingTransactionsAsync());
		Assert.Equal(ref1, record.ClientTransactionRef);

		await store.UpdateCompletedAsync(ref1, SmartConnectTransactionStatus.Declined);
		await store.RemoveAsync(ref1);
		Assert.Empty(await store.GetPendingTransactionsAsync());
	}
}
