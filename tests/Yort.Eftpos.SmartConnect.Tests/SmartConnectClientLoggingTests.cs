using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Library logging contract (Task 12): normal operation is reconstructible from logs (POST sent, polling
/// URL received, poll attempts, terminal state), dispose-abandonment logs Warning (deliberate host action)
/// while timeout logs Error, and — the F2 negative — the <c>merchantAccessToken</c> NEVER appears in any
/// log entry at any level, on any path.
/// </summary>
public class SmartConnectClientLoggingTests
{
	private const string Ref = "100123-abc";
	private const string Token = "tok123";
	private const string PollUrl = "https://poll.unit.test/poll?merchantAccessToken=" + Token;

	private static readonly string InitialResponseJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"" + PollUrl + "\"}}";

	private const string PendingPollJson = "{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {}}";

	private const string AcceptedPollJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"COMPLETED\", " +
		"\"data\": {\"TransactionResult\": \"OK-ACCEPTED\", \"Result\": \"OK\", \"AmountTotal\": \"1250\"}}";

	private static HttpResponseMessage Json(HttpStatusCode status, string json)
		=> new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

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

	private static SmartConnectClient CreateClient(MockHttpHandler handler, InMemoryTransactionStateStore store, ListLogger logger)
	{
		var client = new SmartConnectClient(new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = store,
			HttpClient = new HttpClient(handler),
			Logger = logger,
			PollInterval = TimeSpan.FromSeconds(2),
			MaxPollDuration = TimeSpan.FromSeconds(10)
		});

		var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
		client.Clock = () => now;
		client.PollDelay = delay =>
		{
			now += delay;
			return Task.CompletedTask;
		};

		return client;
	}

	[Fact]
	public async Task HappyPath_NormalOperationIsReconstructibleFromLogs()
	{
		var logger = new ListLogger();
		var index = -1;
		var handler = new MockHttpHandler(_ =>
		{
			var i = Interlocked.Increment(ref index);
			if (i == 0)
			{
				return Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson));
			}

			return Task.FromResult(Json(HttpStatusCode.OK, i < 3 ? PendingPollJson : AcceptedPollJson));
		});
		using var client = CreateClient(handler, new InMemoryTransactionStateStore(), logger);

		await client.ProcessTransactionAsync(CreateRequest());

		// POST sent (Info) — transaction type and amount, never card details (none exist at this point).
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("Card.Purchase") && e.Message.Contains("1250"));
		// PollingUrl received (Info) — transactionId ONLY.
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("txn-1") && e.Message.Contains("olling"));
		// Poll attempts (Debug).
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("attempt"));
		// Terminal state (Info).
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("Accepted"));
	}

	[Fact]
	public async Task FullFlow_TokenNeverAppearsInAnyLogEntry()
	{
		// (F2) The merchantAccessToken rides in the polling URL. This sweep exercises the URL-adjacent
		// paths — initial response handling, poll attempts, a transport error mid-poll, AND a store that
		// echoes the URL in its exception message — then asserts the token reached no entry at any level.
		var logger = new ListLogger();
		var store = new InMemoryTransactionStateStore
		{
			ThrowOnUpdatePollingDetails = new InvalidOperationException("write failed for " + PollUrl)
		};
		var index = -1;
		var handler = new MockHttpHandler(_ =>
		{
			var i = Interlocked.Increment(ref index);
			if (i == 0)
			{
				return Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson));
			}

			if (i == 1)
			{
				throw new HttpRequestException("reset", new SocketException((int)SocketError.ConnectionReset));
			}

			return Task.FromResult(Json(HttpStatusCode.OK, AcceptedPollJson));
		});
		using var client = CreateClient(handler, store, logger);

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
		Assert.NotEmpty(logger.Entries);
		Assert.All(logger.Entries, e =>
		{
			Assert.DoesNotContain(Token, e.Message);
			Assert.DoesNotContain(Token, e.Exception?.ToString() ?? string.Empty);
		});
	}

	[Fact]
	public async Task DisposeDuringPoll_LogsWarning_NotError()
	{
		// Dispose is a deliberate host action (shutdown), not a fault — Warning; only genuine poll
		// exhaustion is an Error.
		var logger = new ListLogger();
		var store = new InMemoryTransactionStateStore();
		var client = CreateClient(new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson))), store, logger);

		var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
		var delayCount = 0;
		client.Clock = () => now;
		client.PollDelay = delay =>
		{
			now += delay;
			if (++delayCount == 2)
			{
				client.Dispose();
			}

			return Task.CompletedTask;
		};

		await client.ProcessTransactionAsync(CreateRequest());

		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("disposed"));
	}

	[Fact]
	public async Task Timeout_LogsError()
	{
		var logger = new ListLogger();
		using var client = CreateClient(new MockHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson))), new InMemoryTransactionStateStore(), logger);

		await client.ProcessTransactionAsync(CreateRequest());

		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("MaxPollDuration"));
	}
}
