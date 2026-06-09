using System;
using System.Linq;
using System.Net;
using NSubstitute;
using Xunit;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Verifies the "HTTP good citizenship" settings applied when the library creates its own
/// <see cref="System.Net.Http.HttpClient"/> (i.e. none was injected).
/// </summary>
public class SmartConnectClientHttpSetupTests
{
	private static SmartConnectClientConfiguration CreateConfiguration()
	{
		return new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri("https://unit.test/POS"),
			StateStore = Substitute.For<ISmartConnectTransactionState>()
		};
	}

	[Fact]
	public void CreateHandler_DisablesAutoRedirect()
	{
		// A redirect on a payment POST or a token-bearing poll URL is unexpected and a leak risk —
		// it must surface as an error, never be silently followed.
		using var handler = SmartConnectClient.CreateHandler();

		Assert.False(handler.AllowAutoRedirect);
	}

	[Fact]
	public void CreateHandler_EnablesGzipAndDeflateDecompression()
	{
		using var handler = SmartConnectClient.CreateHandler();

		Assert.Equal(DecompressionMethods.GZip | DecompressionMethods.Deflate, handler.AutomaticDecompression);
	}

	[Fact]
	public void CreateHttpClient_SetsPerRequestTimeoutShorterThanMaxPollDuration()
	{
		var configuration = CreateConfiguration();
		using var httpClient = SmartConnectClient.CreateHttpClient(configuration);

		// The poll loop owns the long overall duration; each individual request should fail fast.
		Assert.Equal(TimeSpan.FromSeconds(30), httpClient.Timeout);
		Assert.True(httpClient.Timeout < configuration.MaxPollDuration);
	}

	[Fact]
	public void CreateHttpClient_SetsDescriptiveUserAgentFromConfiguration()
	{
		var configuration = CreateConfiguration();
		configuration.UserAgentProductName = "MyPos";
		configuration.UserAgentProductVersion = "1.2.3";

		using var httpClient = SmartConnectClient.CreateHttpClient(configuration);

		var userAgent = httpClient.DefaultRequestHeaders.UserAgent.ToString();
		Assert.Equal("MyPos/1.2.3", userAgent);
	}

	[Fact]
	public void CreateHttpClient_NoConfiguredUserAgent_FallsBackToLibraryIdentity()
	{
		using var httpClient = SmartConnectClient.CreateHttpClient(CreateConfiguration());

		var userAgent = httpClient.DefaultRequestHeaders.UserAgent.ToString();
		Assert.StartsWith("Yort.Eftpos.SmartConnect/", userAgent);
	}

	[Fact]
	public void CreateHttpClient_InvalidUserAgentProductName_FallsBackRatherThanThrowing()
	{
		// "Ontempo Store" is a natural thing to configure but a space is not a valid HTTP token;
		// the client must not blow up at construction over a cosmetic header.
		var configuration = CreateConfiguration();
		configuration.UserAgentProductName = "Ontempo Store";
		configuration.UserAgentProductVersion = "1.0";

		using var httpClient = SmartConnectClient.CreateHttpClient(configuration);

		Assert.StartsWith("Yort.Eftpos.SmartConnect/", httpClient.DefaultRequestHeaders.UserAgent.ToString());
	}

	[Fact]
	public void CreateHttpClient_AcceptsJson()
	{
		using var httpClient = SmartConnectClient.CreateHttpClient(CreateConfiguration());

		Assert.Contains(httpClient.DefaultRequestHeaders.Accept, a => a.MediaType == "application/json");
	}
}
