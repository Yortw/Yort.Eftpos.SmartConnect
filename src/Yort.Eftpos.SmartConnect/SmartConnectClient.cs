using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Yort.Eftpos.SmartConnect.Internal;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The SmartConnect protocol client. Pair a register once with <see cref="PairAsync"/>, then process
/// transactions. Create one instance per register and reuse it for the application's lifetime.
/// </summary>
/// <remarks>
/// <para>When no <see cref="SmartConnectClientConfiguration.HttpClient"/> is injected, the client creates and
/// owns one configured with automatic decompression, redirects disabled (a redirect on a payment request or a
/// token-bearing poll URL is treated as an error, never followed), a descriptive User-Agent, and a 30 second
/// per-request timeout. An injected client is used as-is — its settings and lifetime belong to the consumer.</para>
/// <para>TLS: the client does not force a protocol version. On .NET Framework the effective TLS set is a
/// process-global host concern (<c>ServicePointManager.SecurityProtocol</c>); ensure the host enables TLS 1.2+.</para>
/// </remarks>
public sealed class SmartConnectClient : IDisposable
{
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

	private readonly SmartConnectClientConfiguration _config;
	private readonly HttpClient _httpClient;
	private readonly bool _ownsHttpClient;
	private readonly string _baseUrl;
	private bool _disposed;

	/// <summary>Creates a client from the given configuration.</summary>
	/// <param name="configuration">The configuration; validated immediately so misconfiguration fails at construction, not at first request.</param>
	/// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null, or a required configuration value is missing.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A configuration value is out of range (see <see cref="SmartConnectClientConfiguration.Validate"/>).</exception>
	public SmartConnectClient(SmartConnectClientConfiguration configuration)
	{
		if (configuration == null)
		{
			throw new ArgumentNullException(nameof(configuration));
		}

		configuration.Validate();
		_config = configuration;
		_baseUrl = configuration.BaseUrl!.AbsoluteUri.TrimEnd('/');

		if (configuration.HttpClient != null)
		{
			_httpClient = configuration.HttpClient;
			_ownsHttpClient = false;
		}
		else
		{
			_httpClient = CreateHttpClient(configuration);
			_ownsHttpClient = true;
		}
	}

	/// <summary>
	/// Pairs this register with a terminal via <c>PUT /Pairing/{code}</c>. One-shot and not polled —
	/// transport failures propagate as exceptions, while service rejections (e.g. an invalid code) are
	/// returned as a result with <see cref="SmartConnectPairingResult.Success"/> false.
	/// </summary>
	/// <param name="pairingCode">The pairing code displayed on the terminal.</param>
	/// <param name="request">The registration details. <c>POSRegisterID</c>, <c>POSBusinessName</c> and
	/// <c>POSVendorName</c> are mandatory and must match the values used for all subsequent transactions.</param>
	/// <exception cref="ArgumentNullException"><paramref name="pairingCode"/> or <paramref name="request"/> is null.</exception>
	/// <exception cref="ArgumentException"><paramref name="pairingCode"/> is empty/whitespace, or a mandatory field of <paramref name="request"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="HttpRequestException">The service could not be reached.</exception>
	public async Task<SmartConnectPairingResult> PairAsync(string pairingCode, SmartConnectPairingRequest request)
	{
		if (pairingCode == null)
		{
			throw new ArgumentNullException(nameof(pairingCode));
		}

		if (string.IsNullOrWhiteSpace(pairingCode))
		{
			throw new ArgumentException("A pairing code is required.", nameof(pairingCode));
		}

		if (request == null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		RequireField(request.POSRegisterID, nameof(request.POSRegisterID));
		RequireField(request.POSBusinessName, nameof(request.POSBusinessName));
		RequireField(request.POSVendorName, nameof(request.POSVendorName));
		ThrowIfDisposed();

		var fields = new List<KeyValuePair<string, string?>>(4)
		{
			new KeyValuePair<string, string?>("POSRegisterID", request.POSRegisterID)
		};

		if (!string.IsNullOrEmpty(request.POSRegisterName))
		{
			fields.Add(new KeyValuePair<string, string?>("POSRegisterName", request.POSRegisterName));
		}

		fields.Add(new KeyValuePair<string, string?>("POSBusinessName", request.POSBusinessName));
		fields.Add(new KeyValuePair<string, string?>("POSVendorName", request.POSVendorName));

		var url = _baseUrl + "/Pairing/" + Uri.EscapeDataString(pairingCode);

		using (var httpRequest = new HttpRequestMessage(HttpMethod.Put, url))
		{
			httpRequest.Content = new StringContent(FormUrlEncoder.Encode(fields), Encoding.UTF8, "application/x-www-form-urlencoded");

			using (var response = await SendAsync(httpRequest).ConfigureAwait(false))
			{
				if (response.IsSuccessStatusCode)
				{
					return new SmartConnectPairingResult { Success = true };
				}

				var body = response.Content == null
					? string.Empty
					: await response.Content.ReadAsStringAsync().ConfigureAwait(false);

				return new SmartConnectPairingResult
				{
					Success = false,
					ErrorMessage = GetErrorMessage(response, body)
				};
			}
		}
	}

	/// <summary>Releases the <see cref="HttpClient"/> if the client created it; an injected client is never disposed.</summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		if (_ownsHttpClient)
		{
			_httpClient.Dispose();
		}
	}

	// (F3) The single outbound send path: every request — pairing PUT, transaction POST, poll GET — must go
	// through here so the optional auth seam can never be bypassed by a future call site.
	private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
	{
		var authorize = _config.AuthorizeRequestAsync;
		if (authorize != null)
		{
			await authorize(request).ConfigureAwait(false);
		}

		return await _httpClient.SendAsync(request).ConfigureAwait(false);
	}

	internal static HttpClientHandler CreateHandler()
	{
		return new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
			// A redirect on a payment POST, or on a poll URL carrying the merchantAccessToken, is unexpected
			// and a credential-leak risk — surface it as an error rather than silently following it.
			AllowAutoRedirect = false
		};
	}

	internal static HttpClient CreateHttpClient(SmartConnectClientConfiguration configuration)
	{
		var client = new HttpClient(CreateHandler())
		{
			// Each individual request fails fast; the poll loop owns the long overall duration (MaxPollDuration).
			Timeout = RequestTimeout
		};

		client.DefaultRequestHeaders.UserAgent.Add(BuildUserAgent(configuration));
		client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

		return client;
	}

	private static ProductInfoHeaderValue BuildUserAgent(SmartConnectClientConfiguration configuration)
	{
		// A configured name like "Ontempo Store" is not a valid HTTP token — fall back to the library
		// identity rather than throwing at construction over a cosmetic header.
		if (!string.IsNullOrWhiteSpace(configuration.UserAgentProductName)
			&& !string.IsNullOrWhiteSpace(configuration.UserAgentProductVersion)
			&& ProductInfoHeaderValue.TryParse(configuration.UserAgentProductName + "/" + configuration.UserAgentProductVersion, out var configured))
		{
			return configured;
		}

		var version = typeof(SmartConnectClient).Assembly.GetName().Version?.ToString() ?? "1.0";
		return new ProductInfoHeaderValue("Yort.Eftpos.SmartConnect", version);
	}

	private static string GetErrorMessage(HttpResponseMessage response, string body)
	{
		var jsonError = TryGetJsonError(body);
		if (!string.IsNullOrWhiteSpace(jsonError))
		{
			return jsonError!;
		}

		if (!string.IsNullOrWhiteSpace(body))
		{
			return body;
		}

		return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
	}

	private static string? TryGetJsonError(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return null;
		}

		try
		{
			using (var document = JsonDocument.Parse(body))
			{
				var root = document.RootElement;
				if (root.ValueKind == JsonValueKind.Object
					&& root.TryGetProperty("error", out var error)
					&& error.ValueKind == JsonValueKind.String)
				{
					return error.GetString();
				}
			}
		}
		catch (JsonException)
		{
			// The contract says errors are JSON, but a proxy/gateway can hand back anything — a malformed
			// error body must surface as a failed result, never as an unhandled parse exception.
		}

		return null;
	}

	private static void RequireField(string? value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException($"{fieldName} is mandatory — it must match across pairing and all subsequent transactions.", "request");
		}
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(SmartConnectClient));
		}
	}
}
