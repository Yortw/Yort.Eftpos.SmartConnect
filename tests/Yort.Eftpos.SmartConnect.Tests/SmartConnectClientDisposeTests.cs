using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Dispose/shutdown semantics (Task 10). After-dispose throws for <c>PairAsync</c>/<c>ProcessTransactionAsync</c>
/// are covered in their own test files; injected-HttpClient-not-disposed is covered in the pairing tests.
/// This file pins: dispose during an active poll abandons gracefully (Unknown + sentinel closed), idle and
/// double dispose are safe, and an internally-created HttpClient IS disposed with the client.
/// </summary>
public class SmartConnectClientDisposeTests
{
	private const string Ref = "100123-abc";

	private const string InitialResponseJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"https://poll.unit.test/poll?merchantAccessToken=tok123\"}}";

	private static SmartConnectTransactionRequest CreateRequest()
	{
		return new SmartConnectTransactionRequest
		{
			TransactionType = SmartConnectTransactionType.CardPurchase,
			AmountTotal = Money.FromCents(1250),
			POSRegisterID = "11111111-2222-3333-4444-555555555555",
			POSBusinessName = "Demo Business",
			POSVendorName = "DemoVendor",
			ClientTransactionRef = Ref
		};
	}

	private static SmartConnectClientConfiguration CreateConfiguration(MockHttpHandler handler, InMemoryTransactionStateStore store)
	{
		return new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = store,
			HttpClient = new HttpClient(handler)
		};
	}

	private static MockHttpHandler PendingForeverHandler()
	{
		return new MockHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(InitialResponseJson, Encoding.UTF8, "application/json")
		}));
	}

	[Fact]
	public async Task Dispose_DuringActivePoll_CompletesWithUnknownAndClosesSentinel()
	{
		// Shutdown mid-transaction: the loop must notice the disposal at the next iteration, close the
		// sentinel as Unknown, and complete the task — never hang the host's shutdown on a pinpad.
		var store = new InMemoryTransactionStateStore();
		var client = new SmartConnectClient(CreateConfiguration(PendingForeverHandler(), store));

		var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
		var delayCount = 0;
		client.Clock = () => now;
		client.PollDelay = delay =>
		{
			now += delay;
			if (++delayCount == 3)
			{
				client.Dispose();
			}

			return Task.CompletedTask;
		};

		var result = await client.ProcessTransactionAsync(CreateRequest());

		Assert.Equal(SmartConnectTransactionStatus.Unknown, result.Status);
		Assert.Contains("UpdateCompleted:" + Ref + ":Unknown", store.CallLog);
	}

	[Fact]
	public void Dispose_WhenIdle_DoesNotThrow()
	{
		var client = new SmartConnectClient(CreateConfiguration(PendingForeverHandler(), new InMemoryTransactionStateStore()));

		client.Dispose();
	}

	[Fact]
	public void Dispose_Twice_IsIdempotent()
	{
		var client = new SmartConnectClient(CreateConfiguration(PendingForeverHandler(), new InMemoryTransactionStateStore()));

		client.Dispose();
		client.Dispose();
	}

	[Fact]
	public async Task Dispose_InternallyCreatedHttpClient_IsDisposed()
	{
		// Counterpart to the pairing tests' injected-client-NOT-disposed case: when the library created
		// the HttpClient, disposing the SmartConnect client must release it.
		var client = new SmartConnectClient(new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = new InMemoryTransactionStateStore()
		});
		var ownedHttpClient = client.HttpClientInternal;

		client.Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => ownedHttpClient.GetAsync("https://unit.test/POS/anything"));
	}
}
