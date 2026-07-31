using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;
using V1 = Yort.Eftpos.SmartConnect.SaleData.V1;

namespace Yort.Eftpos.SmartConnect.Tests;

public class SmartConnectClientSaleDataTests
{
	private const string AcceptedPollJson =
		"{\"transactionId\": \"t\", \"transactionStatus\": \"COMPLETED\", \"data\": {\"TransactionResult\": \"OK-ACCEPTED\", \"Result\": \"OK\"}}";
	private const string InitialJson =
		"{\"transactionId\": \"t\", \"transactionStatus\": \"PENDING\", \"data\": {\"PollingUrl\": \"https://poll.test/p?merchantAccessToken=x\"}}";

	private static MockHttpHandler Handler()
	{
		var i = -1;
		return new MockHttpHandler(_ =>
		{
			var n = Interlocked.Increment(ref i);
			var body = n == 0 ? InitialJson : AcceptedPollJson;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
		});
	}

	private static SmartConnectClient Client(MockHttpHandler h, InMemoryTransactionStateStore? store = null)
	{
		var c = new SmartConnectClient(new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = store ?? new InMemoryTransactionStateStore(),
			HttpClient = new HttpClient(h),
			PollInterval = TimeSpan.FromSeconds(2),
			MaxPollDuration = TimeSpan.FromSeconds(10)
		});
		var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
		c.Clock = () => now;
		c.PollDelay = d => { now += d; return Task.CompletedTask; };
		return c;
	}

	private static SmartConnectTransactionRequest BaseRequest() => new SmartConnectTransactionRequest
	{
		TransactionType = SmartConnectTransactionType.CardPurchase,
		AmountTotal = Money.FromCents(1999),
		POSRegisterID = "11111111-2222-3333-4444-555555555555",
		POSBusinessName = "Demo",
		POSVendorName = "DemoVendor",
		ClientTransactionRef = "ref-1"
	};

	// Extracts a form field's raw (still URL-encoded) value from an application/x-www-form-urlencoded body.
	private static string? FormField(string? body, string key)
		=> body?.Split('&').Where(p => p.StartsWith(key + "=")).Select(p => p.Substring(key.Length + 1)).FirstOrDefault();

	[Fact]
	public async Task NoSaleData_OmitsTheField()
	{
		var h = Handler();
		using var c = Client(h);

		await c.ProcessTransactionAsync(BaseRequest());

		Assert.DoesNotContain("SaleData=", h.Requests[0].Body);
	}

	[Fact]
	public async Task SaleDataSet_IncludesUrlEncodedEnvelope()
	{
		var h = Handler();
		using var c = Client(h);
		var req = BaseRequest();
		req.SaleData = new V1.SaleData { TotalAmount = "19.99", TotalTax = "2.61" };

		await c.ProcessTransactionAsync(req);

		var encoded = FormField(h.Requests[0].Body, "SaleData");
		Assert.NotNull(encoded);
		using var doc = System.Text.Json.JsonDocument.Parse(Uri.UnescapeDataString(encoded!));
		Assert.Equal("1.0.0", doc.RootElement.GetProperty("version").GetString());
		Assert.Equal("19.99", doc.RootElement.GetProperty("saleData").GetProperty("totalAmount").GetString());
	}

	[Fact]
	public async Task SaleDataSet_HostileStringSurvivesFormEncoding()
	{
		var h = Handler();
		using var c = Client(h);
		var req = BaseRequest();
		const string hostile = "a&b=c\"\\é😀";
		req.SaleData = new V1.SaleData { TotalAmount = "1", TotalTax = "0", CustomerName = hostile };

		await c.ProcessTransactionAsync(req);

		// The form body must still split correctly on '&'/'=' despite the hostile content.
		var encoded = FormField(h.Requests[0].Body, "SaleData");
		Assert.NotNull(encoded);
		using var doc = System.Text.Json.JsonDocument.Parse(Uri.UnescapeDataString(encoded!));
		Assert.Equal(hostile, doc.RootElement.GetProperty("saleData").GetProperty("customerName").GetString());
	}

	[Fact]
	public async Task UnserialisableSaleData_Throws_AndSendsNothing_AndWritesNoSentinel()
	{
		var h = Handler();
		var store = new InMemoryTransactionStateStore();
		var c = Client(h, store);
		var req = BaseRequest();
		req.SaleData = new CyclicSaleData();

		await Assert.ThrowsAsync<ArgumentException>(() => c.ProcessTransactionAsync(req));

		Assert.Equal(0, h.RequestCount);   // nothing sent
		Assert.Empty(store.CallLog);       // no sentinel persisted (validation precedes the sentinel)
		c.Dispose();
	}

	private sealed class CyclicSaleData : SmartConnectSaleData
	{
		public override string Version => "1.0.0";
		public CyclicSaleData Self => this; // reference cycle -> STJ throws on serialise
	}
}
