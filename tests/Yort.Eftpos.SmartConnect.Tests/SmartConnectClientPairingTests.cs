using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NSubstitute;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;

namespace Yort.Eftpos.SmartConnect.Tests;

public class SmartConnectClientPairingTests
{
	private static readonly Uri BaseUrl = new Uri("https://unit.test/POS");

	private static SmartConnectClientConfiguration CreateConfiguration(MockHttpHandler handler)
	{
		return new SmartConnectClientConfiguration
		{
			BaseUrl = BaseUrl,
			StateStore = Substitute.For<ISmartConnectTransactionState>(),
			HttpClient = new HttpClient(handler)
		};
	}

	private static SmartConnectPairingRequest CreatePairingRequest()
	{
		return new SmartConnectPairingRequest
		{
			POSRegisterID = "11111111-2222-3333-4444-555555555555",
			POSRegisterName = "Register 1",
			POSBusinessName = "Demo Business",
			POSVendorName = "Ontempo"
		};
	}

	private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
	{
		return new HttpResponseMessage(status)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		};
	}

	private static MockHttpHandler SuccessHandler()
	{
		return new MockHttpHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"result\": \"success\"}")));
	}

	[Fact]
	public async Task PairAsync_SuccessfulPairing_ReturnsSuccessTrue()
	{
		var handler = SuccessHandler();
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		var result = await client.PairAsync("12345678", CreatePairingRequest());

		Assert.True(result.Success);
		Assert.Null(result.ErrorMessage);
	}

	[Fact]
	public async Task PairAsync_SendsFormEncodedPutToPairingUrl()
	{
		var handler = SuccessHandler();
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		await client.PairAsync("12345678", CreatePairingRequest());

		var request = Assert.Single(handler.Requests);
		Assert.Equal(HttpMethod.Put, request.Method);
		Assert.Equal("https://unit.test/POS/Pairing/12345678", request.Uri?.AbsoluteUri);
		Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
	}

	[Fact]
	public async Task PairAsync_SendsAllMandatoryFields()
	{
		var handler = SuccessHandler();
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		await client.PairAsync("12345678", CreatePairingRequest());

		// Literal expected body — deliberately NOT computed via FormUrlEncoder, so an encoding defect in
		// the library cannot self-confirm here.
		Assert.Equal(
			"POSRegisterID=11111111-2222-3333-4444-555555555555&POSRegisterName=Register%201&POSBusinessName=Demo%20Business&POSVendorName=Ontempo",
			handler.Requests[0].Body);
	}

	[Fact]
	public async Task PairAsync_NullRegisterName_OmitsField()
	{
		var handler = SuccessHandler();
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		var request = CreatePairingRequest();
		request.POSRegisterName = null;
		await client.PairAsync("12345678", request);

		Assert.Equal(
			"POSRegisterID=11111111-2222-3333-4444-555555555555&POSBusinessName=Demo%20Business&POSVendorName=Ontempo",
			handler.Requests[0].Body);
	}

	[Fact]
	public async Task PairAsync_EscapesPairingCodeInUrl()
	{
		var handler = SuccessHandler();
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		await client.PairAsync("AB CD", CreatePairingRequest());

		Assert.Equal("https://unit.test/POS/Pairing/AB%20CD", handler.Requests[0].Uri?.AbsoluteUri);
	}

	[Fact]
	public async Task PairAsync_Http400WithJsonError_ReturnsErrorMessage()
	{
		var handler = new MockHttpHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.BadRequest, "{\"error\": \"Invalid pairing code\"}")));
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		var result = await client.PairAsync("12345678", CreatePairingRequest());

		Assert.False(result.Success);
		Assert.Equal("Invalid pairing code", result.ErrorMessage);
	}

	[Fact]
	public async Task PairAsync_Http400WithNonJsonBody_UsesBodyAsErrorMessage()
	{
		// The contract says errors are JSON, but a proxy/gateway can hand back anything — a malformed error
		// body must surface as a failed result, never an unhandled parse exception.
		var handler = new MockHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("Bad gateway response", Encoding.UTF8, "text/plain")
		}));
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		var result = await client.PairAsync("12345678", CreatePairingRequest());

		Assert.False(result.Success);
		Assert.Equal("Bad gateway response", result.ErrorMessage);
	}

	[Fact]
	public async Task PairAsync_HttpErrorWithEmptyBody_ReportsStatusCode()
	{
		var handler = new MockHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		var result = await client.PairAsync("12345678", CreatePairingRequest());

		Assert.False(result.Success);
		Assert.NotNull(result.ErrorMessage);
		Assert.Contains("500", result.ErrorMessage);
	}

	[Fact]
	public async Task PairAsync_TransportFailure_ThrowsSmartConnectTransportException()
	{
		// Pairing is one-shot, not polled — transport failures propagate, but always as the library's
		// typed exception (ADR Decision 9), never a raw BCL exception.
		var original = new HttpRequestException("send failed", new System.Net.Sockets.SocketException(10061 /* ConnectionRefused */));
		var handler = new MockHttpHandler(_ => throw original);
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		var thrown = await Assert.ThrowsAsync<SmartConnectTransportException>(() => client.PairAsync("12345678", CreatePairingRequest()));

		Assert.Equal(SmartConnectRequestDelivery.NotSent, thrown.Delivery);
		Assert.Same(original, thrown.InnerException);
	}

	[Fact]
	public async Task PairAsync_Timeout_ThrowsTransportExceptionWithDeliveryUnknown()
	{
		var handler = new MockHttpHandler(_ => throw new TaskCanceledException("timed out"));
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		var thrown = await Assert.ThrowsAsync<SmartConnectTransportException>(() => client.PairAsync("12345678", CreatePairingRequest()));

		Assert.Equal(SmartConnectRequestDelivery.Unknown, thrown.Delivery);
	}

	[Fact]
	public async Task PairAsync_TransportException_IsCatchableAsSmartConnectException()
	{
		// The umbrella contract: one base type catches everything operational the library throws.
		var handler = new MockHttpHandler(_ => throw new HttpRequestException("send failed"));
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		await Assert.ThrowsAnyAsync<SmartConnectException>(() => client.PairAsync("12345678", CreatePairingRequest()));
	}

	[Fact]
	public async Task PairAsync_AuthCallbackThrows_PropagatesUnwrapped()
	{
		// (R3-adjacent invariant) The transport wrap covers the HTTP exchange only — the consumer's own
		// AuthorizeRequestAsync code throwing is their bug and must not be disguised as a transport failure.
		var configuration = CreateConfiguration(SuccessHandler());
		configuration.AuthorizeRequestAsync = _ => throw new InvalidOperationException("consumer bug");
		using var client = new SmartConnectClient(configuration);

		await Assert.ThrowsAsync<InvalidOperationException>(() => client.PairAsync("12345678", CreatePairingRequest()));
	}

	[Fact]
	public async Task PairAsync_TransportExceptionMessage_DoesNotContainRequestUrl()
	{
		// (R6) The wrapper must not add the request URL to its own message — poll URLs carry the
		// merchantAccessToken bearer credential, and consumers will log ex.Message.
		var handler = new MockHttpHandler(_ => throw new HttpRequestException("send failed"));
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		var thrown = await Assert.ThrowsAsync<SmartConnectTransportException>(() => client.PairAsync("12345678", CreatePairingRequest()));

		Assert.DoesNotContain("unit.test", thrown.Message);
		Assert.DoesNotContain("12345678", thrown.Message);
	}

	[Fact]
	public async Task PairAsync_NullPairingCode_Throws()
	{
		using var client = new SmartConnectClient(CreateConfiguration(SuccessHandler()));

		await Assert.ThrowsAsync<ArgumentNullException>(() => client.PairAsync(null!, CreatePairingRequest()));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task PairAsync_EmptyOrWhitespacePairingCode_Throws(string pairingCode)
	{
		using var client = new SmartConnectClient(CreateConfiguration(SuccessHandler()));

		await Assert.ThrowsAsync<ArgumentException>(() => client.PairAsync(pairingCode, CreatePairingRequest()));
	}

	[Fact]
	public async Task PairAsync_NullRequest_Throws()
	{
		using var client = new SmartConnectClient(CreateConfiguration(SuccessHandler()));

		await Assert.ThrowsAsync<ArgumentNullException>(() => client.PairAsync("12345678", null!));
	}

	[Theory]
	[InlineData("POSRegisterID")]
	[InlineData("POSBusinessName")]
	[InlineData("POSVendorName")]
	public async Task PairAsync_MissingMandatoryRequestField_Throws(string fieldName)
	{
		using var client = new SmartConnectClient(CreateConfiguration(SuccessHandler()));

		var request = CreatePairingRequest();
		switch (fieldName)
		{
			case "POSRegisterID":
				request.POSRegisterID = string.Empty;
				break;
			case "POSBusinessName":
				request.POSBusinessName = string.Empty;
				break;
			case "POSVendorName":
				request.POSVendorName = string.Empty;
				break;
		}

		await Assert.ThrowsAsync<ArgumentException>(() => client.PairAsync("12345678", request));
	}

	[Fact]
	public async Task PairAsync_AuthSeamSet_AppliesAuthorizationToRequest()
	{
		// (F3) Every outbound request must flow through the single send path that applies the auth seam.
		var handler = SuccessHandler();
		var configuration = CreateConfiguration(handler);
		configuration.AuthorizeRequestAsync = request =>
		{
			request.Headers.Add("X-Api-Key", "secret-key");
			return Task.CompletedTask;
		};
		using var client = new SmartConnectClient(configuration);

		await client.PairAsync("12345678", CreatePairingRequest());

		Assert.Equal(new[] { "secret-key" }, handler.Requests[0].Headers["X-Api-Key"]);
	}

	[Fact]
	public async Task PairAsync_AuthSeamNotSet_SendsNoAuthHeaders()
	{
		// Negative case: with no seam configured, nothing must invent credentials.
		var handler = SuccessHandler();
		using var client = new SmartConnectClient(CreateConfiguration(handler));

		await client.PairAsync("12345678", CreatePairingRequest());

		Assert.False(handler.Requests[0].Headers.ContainsKey("Authorization"));
		Assert.False(handler.Requests[0].Headers.ContainsKey("X-Api-Key"));
	}

	[Fact]
	public async Task PairAsync_AfterDispose_Throws()
	{
		var client = new SmartConnectClient(CreateConfiguration(SuccessHandler()));
		client.Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => client.PairAsync("12345678", CreatePairingRequest()));
	}

	[Fact]
	public void Constructor_NullConfiguration_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new SmartConnectClient(null!));
	}

	[Fact]
	public void Constructor_InvalidConfiguration_Throws()
	{
		// Constructor must run Validate() — a missing BaseUrl fails fast, not at first request.
		var configuration = new SmartConnectClientConfiguration
		{
			StateStore = Substitute.For<ISmartConnectTransactionState>()
		};

		Assert.Throws<ArgumentNullException>(() => new SmartConnectClient(configuration));
	}

	[Fact]
	public void Constructor_InjectedHttpClient_IsNotMutated()
	{
		// The consumer owns an injected client's settings; the library must not touch them.
		var httpClient = new HttpClient(SuccessHandler());
		var originalTimeout = httpClient.Timeout;
		var configuration = CreateConfiguration(SuccessHandler());
		configuration.HttpClient = httpClient;

		using var client = new SmartConnectClient(configuration);

		Assert.Equal(originalTimeout, httpClient.Timeout);
		Assert.Empty(httpClient.DefaultRequestHeaders);
	}

	[Fact]
	public async Task Dispose_InjectedHttpClient_IsNotDisposed()
	{
		var handler = SuccessHandler();
		var httpClient = new HttpClient(handler);
		var configuration = CreateConfiguration(handler);
		configuration.HttpClient = httpClient;

		var client = new SmartConnectClient(configuration);
		client.Dispose();

		// The injected client must remain usable after the SmartConnect client is disposed.
		using var response = await httpClient.GetAsync("https://unit.test/POS/anything");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}
}
