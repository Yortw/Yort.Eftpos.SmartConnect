using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Tests that <c>ProcessTransactionAsync</c> drives <see cref="ISmartConnectTransactionState"/> at the
/// correct lifecycle points, and that the state-store boundary policy (ADR Decisions 9/10, R3/G6/G7/G10)
/// holds: the gate is absolute but surfaces as a result; post-POST store failures never abort a live
/// transaction and never masquerade as pre-send refusals.
/// </summary>
public class SmartConnectClientStateLifecycleTests
{

	private static SmartConnectClient WithVirtualTime(SmartConnectClient client)
	{
		// The polling loop is real now: without virtual time, any test whose handler keeps answering
		// PENDING would poll for the actual MaxPollDuration (minutes of wall-clock per test).
		var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
		client.Clock = () => now;
		client.PollDelay = delay =>
		{
			now += delay;
			return Task.CompletedTask;
		};
		return client;
	}
	private const string Ref = "100123-abc";
	private const string PollUrl = "https://poll.unit.test/poll?merchantAccessToken=tok123";

	private static readonly string InitialResponseJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"" + PollUrl + "\"}}";

	private static SmartConnectTransactionRequest CreateRequest()
	{
		return new SmartConnectTransactionRequest
		{
			TransactionType = SmartConnectTransactionType.CardPurchase,
			AmountTotal = Money.FromCents(1250),
			POSRegisterID = "11111111-2222-3333-4444-555555555555",
			POSBusinessName = "Demo Business",
			POSVendorName = "Ontempo",
			ClientTransactionRef = Ref
		};
	}

	private static SmartConnectClientConfiguration CreateConfiguration(MockHttpHandler handler, InMemoryTransactionStateStore store, ILogger? logger = null)
	{
		return new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = store,
			HttpClient = new HttpClient(handler),
			Logger = logger
		};
	}

	private static MockHttpHandler PendingResponseHandler()
	{
		return new MockHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(InitialResponseJson, Encoding.UTF8, "application/json")
		}));
	}

	[Fact]
	public async Task Process_SaveCalledBeforePost()
	{
		var store = new InMemoryTransactionStateStore();
		List<string>? callLogAtPostTime = null;
		var handler = new MockHttpHandler(_ =>
		{
			callLogAtPostTime = store.CallLog.ToList();
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(InitialResponseJson, Encoding.UTF8, "application/json")
			});
		});
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler, store)));

		await client.ProcessTransactionAsync(CreateRequest());

		Assert.NotNull(callLogAtPostTime);
		Assert.Contains("Save:" + Ref, callLogAtPostTime!);
	}

	[Fact]
	public async Task Process_SentinelRecordsRefTypeAndAmount()
	{
		var store = new InMemoryTransactionStateStore();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler(), store)));

		await client.ProcessTransactionAsync(CreateRequest());

		var record = store.Records[Ref];
		Assert.Equal(SmartConnectTransactionType.CardPurchase, record.TransactionType);
		Assert.Equal(1250, record.AmountTotalCents);
	}

	[Fact]
	public async Task Process_UpdatePollingDetailsCalledAfterResponse_WithUrlAndTransactionId()
	{
		var store = new InMemoryTransactionStateStore();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler(), store)));

		await client.ProcessTransactionAsync(CreateRequest());

		var record = store.Records[Ref];
		Assert.Equal(PollUrl, record.PollingUrl);
		Assert.Equal("txn-1", record.TransactionId);
		Assert.True(store.CallLog.IndexOf("Save:" + Ref) < store.CallLog.IndexOf("UpdatePolling:" + Ref));
	}

	// --- (F5/R3) The absolute pre-POST gate, surfaced as a result ---

	[Fact]
	public async Task Process_SaveThrows_NoHttpRequestAndStateStoreFailureResult()
	{
		var store = new InMemoryTransactionStateStore { ThrowOnSave = new IOException("store down") };
		var handler = PendingResponseHandler();
		var logger = new ListLogger();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler, store, logger)));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(0, handler.RequestCount);
		Assert.Empty(store.Records);
		Assert.Equal(SmartConnectTransactionStatus.Failed, result.Status);
		Assert.Equal(SmartConnectFailureCause.StateStoreFailure, result.FailureCause);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
	}

	[Fact]
	public async Task Process_SaveThrows_NeverReportedAsTransportNotSent()
	{
		// (R7 extension) No transport was attempted — the cause must be the store, not the network.
		var store = new InMemoryTransactionStateStore { ThrowOnSave = new IOException("store down") };
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler(), store)));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.NotEqual(SmartConnectFailureCause.TransportNotSent, result.FailureCause);
	}

	[Fact]
	public async Task Process_SaveThrowsAndLoggerThrows_StillReturnsResult()
	{
		// (G10) Diagnostics must be strictly weaker than the path they diagnose.
		var store = new InMemoryTransactionStateStore { ThrowOnSave = new IOException("store down") };
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler(), store, new ThrowingLogger())));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectFailureCause.StateStoreFailure, result.FailureCause);
	}

	// --- (R3) UpdatePollingDetails-throws: best-effort happy path ---

	[Fact]
	public async Task Process_UpdatePollingDetailsThrows_DoesNotThrowAndContinues()
	{
		var store = new InMemoryTransactionStateStore { ThrowOnUpdatePollingDetails = new InvalidOperationException("db restarting") };
		var logger = new ListLogger();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler(), store, logger)));

		// The transaction is irrevocably in flight — the most likely outcome is a normal accept/decline,
		// so the library must continue on the in-memory URL, not abort.
		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.NotNull(result);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
	}

	[Fact]
	public async Task Process_UpdatePollingDetailsThrows_NeverStateStoreFailure()
	{
		// (G6) StateStoreFailure means "never sent — retry freely"; this failure is AFTER the POST.
		var store = new InMemoryTransactionStateStore { ThrowOnUpdatePollingDetails = new InvalidOperationException("db restarting") };
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler(), store)));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.NotEqual(SmartConnectFailureCause.StateStoreFailure, result.FailureCause);
	}

	[Fact]
	public async Task Process_UpdatePollingDetailsThrows_TokenNeverAppearsInLogs()
	{
		// (G7) The polling URL was an ARGUMENT to the failing store call; store exceptions commonly echo
		// arguments. The library must log exception type + ref only — never the store exception's message.
		var store = new InMemoryTransactionStateStore
		{
			ThrowOnUpdatePollingDetails = new InvalidOperationException("write failed for url " + PollUrl)
		};
		var logger = new ListLogger();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(PendingResponseHandler(), store, logger)));

		await client.ProcessTransactionAsync(CreateRequest());

		Assert.All(logger.Entries, e =>
		{
			Assert.DoesNotContain("tok123", e.Message);
			Assert.DoesNotContain("tok123", e.Exception?.ToString() ?? string.Empty);
		});
	}

	// --- (Decision 9) POST transport failures ---

	[Fact]
	public async Task Process_TransportNotSent_ClosesSentinelAndReturnsFailedTransportNotSent()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = new MockHttpHandler(_ => throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)));
		var logger = new ListLogger();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler, store, logger)));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Failed, result.Status);
		Assert.Equal(SmartConnectFailureCause.TransportNotSent, result.FailureCause);
		Assert.Equal(SmartConnectTransactionStatus.Failed, store.Records[Ref].Status);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
	}

	[Fact]
	public async Task Process_TransportNotSent_UpdateCompletedThrows_StillReturnsResult()
	{
		// (R3) A persistence failure must never mask the outcome the library already has; (G6) and the
		// cause stays transport-derived, never StateStoreFailure.
		var store = new InMemoryTransactionStateStore { ThrowOnUpdateCompleted = new IOException("store down") };
		var handler = new MockHttpHandler(_ => throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)));
		var logger = new ListLogger();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler, store, logger)));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectFailureCause.TransportNotSent, result.FailureCause);
		Assert.NotEqual(SmartConnectFailureCause.StateStoreFailure, result.FailureCause);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
	}

	[Fact]
	public async Task Process_TransportUnknown_LeavesSentinelPendingAndReturnsUnknown()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = new MockHttpHandler(_ => throw new TaskCanceledException("timed out"));
		var logger = new ListLogger();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler, store, logger)));

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Equal(SmartConnectFailureCause.TransportUnknown, result.FailureCause);
		// The sentinel MUST stay pending — recovery investigates; closing it would hide a possibly-live charge.
		Assert.Null(store.Records[Ref].Status);
		Assert.DoesNotContain(store.CallLog, entry => entry.StartsWith("UpdateCompleted:"));
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
	}

	// --- (F3) Auth seam on the POST ---

	[Fact]
	public async Task Process_AuthSeamSet_PostCarriesHeader()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = PendingResponseHandler();
		var configuration = CreateConfiguration(handler, store);
		configuration.AuthorizeRequestAsync = request =>
		{
			request.Headers.Add("X-Api-Key", "secret-key");
			return Task.CompletedTask;
		};
		using var client = WithVirtualTime(new SmartConnectClient(configuration));

		await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(new[] { "secret-key" }, handler.Requests[0].Headers["X-Api-Key"]);
	}

	[Fact]
	public async Task Process_AuthSeamNotSet_NoAuthHeaders()
	{
		var store = new InMemoryTransactionStateStore();
		var handler = PendingResponseHandler();
		using var client = WithVirtualTime(new SmartConnectClient(CreateConfiguration(handler, store)));

		await client.ProcessTransactionAsync(CreateRequest());

		Assert.False(handler.Requests[0].Headers.ContainsKey("Authorization"));
		Assert.False(handler.Requests[0].Headers.ContainsKey("X-Api-Key"));
	}
}
