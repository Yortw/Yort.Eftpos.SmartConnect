using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Tests for HTTP 429 backoff (Task 9). The PollDelay seam records every requested delay while advancing
/// the virtual clock, so the backoff sequence is asserted exactly — no wall-clock, no tolerance windows.
/// </summary>
public class SmartConnectClientBackoffTests
{
	private static readonly DateTimeOffset BaseTime = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

	private const string InitialResponseJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"https://poll.unit.test/poll?merchantAccessToken=tok123\"}}";

	private const string PendingPollJson = "{\"transactionId\": \"txn-1\", \"transactionStatus\": \"PENDING\", \"data\": {}}";

	private const string AcceptedPollJson =
		"{\"transactionId\": \"txn-1\", \"transactionStatus\": \"COMPLETED\", " +
		"\"data\": {\"TransactionResult\": \"OK-ACCEPTED\", \"Result\": \"OK\", \"AmountTotal\": \"1250\"}}";

	private sealed class RecordingProgress : IProgress<SmartConnectPollingStatus>
	{
		public List<SmartConnectPollingStatus> Reports { get; } = new List<SmartConnectPollingStatus>();

		public void Report(SmartConnectPollingStatus value) => Reports.Add(value);
	}

	private static HttpResponseMessage Json(HttpStatusCode status, string json)
		=> new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

	private static HttpResponseMessage RateLimited(RetryConditionHeaderValue? retryAfter = null)
	{
		var response = new HttpResponseMessage((HttpStatusCode)429);
		if (retryAfter != null)
		{
			response.Headers.RetryAfter = retryAfter;
		}

		return response;
	}

	/// <summary>First request gets the initial POST response; later requests walk the poll sequence (last repeats).</summary>
	private static MockHttpHandler SequencedHandler(params Func<HttpResponseMessage>[] pollResponses)
	{
		var index = -1;
		return new MockHttpHandler(_ =>
		{
			var i = Interlocked.Increment(ref index);
			if (i == 0)
			{
				return Task.FromResult(Json(HttpStatusCode.OK, InitialResponseJson));
			}

			var pollIndex = Math.Min(i - 1, pollResponses.Length - 1);
			return Task.FromResult(pollResponses[pollIndex]());
		});
	}

	private static SmartConnectTransactionRequest CreateRequest()
	{
		return new SmartConnectTransactionRequest
		{
			TransactionType = SmartConnectTransactionType.CardPurchase,
			AmountTotal = Money.FromCents(1250),
			POSRegisterID = "11111111-2222-3333-4444-555555555555",
			POSBusinessName = "Demo Business",
			POSVendorName = "Ontempo",
			ClientTransactionRef = "100123-abc"
		};
	}

	private static (SmartConnectClient Client, List<TimeSpan> Delays) CreateClient(MockHttpHandler handler)
	{
		var client = new SmartConnectClient(new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = new InMemoryTransactionStateStore(),
			HttpClient = new HttpClient(handler),
			// 3s base matches the design's documented sequence: 3 → 6 → 12 → 24 → capped at 30.
			PollInterval = TimeSpan.FromSeconds(3),
			MaxPollDuration = TimeSpan.FromMinutes(10)
		});

		var now = BaseTime;
		var delays = new List<TimeSpan>();
		client.Clock = () => now;
		client.PollDelay = delay =>
		{
			delays.Add(delay);
			now += delay;
			return Task.CompletedTask;
		};

		return (client, delays);
	}

	private static TimeSpan[] Seconds(params int[] values) => values.Select(v => TimeSpan.FromSeconds(v)).ToArray();

	[Fact]
	public async Task Poll_Single429_ReportsBackingOffAndDoublesNextDelay()
	{
		var handler = SequencedHandler(
			() => RateLimited(),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var progress = new RecordingProgress();
		var (client, delays) = CreateClient(handler);
		using (client)
		{
			var result = await client.ProcessTransactionAsync(CreateRequest(), progress);

			Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
			Assert.Equal(Seconds(3, 6), delays);
			Assert.Contains(progress.Reports, r => r.State == SmartConnectPollingState.BackingOff);
		}
	}

	[Fact]
	public async Task Poll_Repeated429s_ExponentialBackoffCappedAtBackoffCap()
	{
		var handler = SequencedHandler(
			() => RateLimited(),
			() => RateLimited(),
			() => RateLimited(),
			() => RateLimited(),
			() => RateLimited(),
			() => RateLimited(),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var (client, delays) = CreateClient(handler);
		using (client)
		{
			var result = await client.ProcessTransactionAsync(CreateRequest());

			Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
			// 3 (first poll), then 6 → 12 → 24 → capped at 30, 30, 30.
			Assert.Equal(Seconds(3, 6, 12, 24, 30, 30, 30), delays);
		}
	}

	[Fact]
	public async Task Poll_RetryAfterSeconds_UsedInsteadOfExponential()
	{
		var handler = SequencedHandler(
			() => RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(10))),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var (client, delays) = CreateClient(handler);
		using (client)
		{
			await client.ProcessTransactionAsync(CreateRequest());

			Assert.Equal(Seconds(3, 10), delays);
		}
	}

	[Fact]
	public async Task Poll_RetryAfterHttpDate_Used()
	{
		// The 429 arrives after the first 3s poll delay, so the virtual clock reads BaseTime+3s when the
		// header is evaluated; a date of BaseTime+18s therefore means a 15s wait.
		var handler = SequencedHandler(
			() => RateLimited(new RetryConditionHeaderValue(BaseTime.AddSeconds(18))),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var (client, delays) = CreateClient(handler);
		using (client)
		{
			await client.ProcessTransactionAsync(CreateRequest());

			Assert.Equal(Seconds(3, 15), delays);
		}
	}

	[Fact]
	public async Task Poll_SuccessfulPoll_ResetsBackoffToConfiguredInterval()
	{
		var handler = SequencedHandler(
			() => RateLimited(),
			() => RateLimited(),
			() => Json(HttpStatusCode.OK, PendingPollJson),
			() => RateLimited(),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var (client, delays) = CreateClient(handler);
		using (client)
		{
			var result = await client.ProcessTransactionAsync(CreateRequest());

			Assert.Equal(SmartConnectTransactionStatus.Accepted, result.Status);
			// 3, backoff 6, 12; a successful poll resets to 3; the next 429 restarts doubling at 6.
			Assert.Equal(Seconds(3, 6, 12, 3, 6), delays);
		}
	}

	[Fact]
	public async Task Poll_RetryAfterBeyondBackoffCap_IsClamped()
	{
		// A vendor-instructed two-minute wait exceeds our patience ceiling; MaxPollDuration is the true
		// bound and BackoffCap caps any single wait.
		var handler = SequencedHandler(
			() => RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var (client, delays) = CreateClient(handler);
		using (client)
		{
			await client.ProcessTransactionAsync(CreateRequest());

			Assert.Equal(Seconds(3, 30), delays);
		}
	}

	[Fact]
	public async Task Poll_RetryAfterDateInPast_FlooredToMinimumPollInterval()
	{
		// A stale/past date must never produce a zero/negative wait — that would be an immediate re-poll,
		// the exact rate-limit violation 429 is telling us off for.
		var handler = SequencedHandler(
			() => RateLimited(new RetryConditionHeaderValue(BaseTime.AddSeconds(-60))),
			() => Json(HttpStatusCode.OK, AcceptedPollJson));
		var (client, delays) = CreateClient(handler);
		using (client)
		{
			await client.ProcessTransactionAsync(CreateRequest());

			Assert.Equal(new[] { TimeSpan.FromSeconds(3), SmartConnectClientConfiguration.MinimumPollInterval }, delays);
		}
	}
}
