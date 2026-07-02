using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// Configuration for a <c>SmartConnectClient</c>. Supply at minimum a <see cref="BaseUrl"/> (use
/// <see cref="SmartConnectEnvironments"/>) and a <see cref="StateStore"/>.
/// </summary>
public sealed class SmartConnectClientConfiguration
{
	/// <summary>The minimum permitted <see cref="PollInterval"/>; the service rate-limits faster polling (HTTP 429).</summary>
	public static readonly TimeSpan MinimumPollInterval = TimeSpan.FromSeconds(2);

	/// <summary>The environment base URL. Required. See <see cref="SmartConnectEnvironments"/>.</summary>
	public Uri? BaseUrl { get; set; }

	/// <summary>The interval between status polls. Default 3s; must be at least <see cref="MinimumPollInterval"/>.</summary>
	public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

	/// <summary>
	/// The maximum total time to poll before returning <see cref="SmartConnectTransactionStatus.Unknown"/>.
	/// Default 5 minutes — a conservative backstop past the terminal's own (~3 minute) timeout. SmartConnect
	/// does not document a transaction timeout, so this is a client-side safeguard, not a protocol value.
	/// </summary>
	public TimeSpan MaxPollDuration { get; set; } = TimeSpan.FromMinutes(5);

	/// <summary>The upper bound for exponential backoff after repeated HTTP 429 responses. Default 30s.</summary>
	public TimeSpan BackoffCap { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>
	/// An optional <see cref="System.Net.Http.HttpClient"/> to use. When <see langword="null"/>, the client
	/// creates and owns one (with decompression, a User-Agent, and redirects disabled). An injected client is
	/// not mutated or disposed by the client — the consumer owns its settings and lifetime.
	/// </summary>
	public HttpClient? HttpClient { get; set; }

	/// <summary>An optional logger. When <see langword="null"/>, logging is a no-op. The client never logs the polling URL.</summary>
	public ILogger? Logger { get; set; }

	/// <summary>The mandatory transaction-state store. Required — <see cref="Validate"/> throws if it is <see langword="null"/>.</summary>
	public ISmartConnectTransactionState? StateStore { get; set; }

	/// <summary>
	/// An optional, non-breaking seam for vendor authentication. When set, it is invoked on every outbound
	/// request (POST, PUT, poll GET) to attach credentials (e.g. a header). When <see langword="null"/>, no
	/// auth is applied — the documented SmartConnect flow requires none beyond pairing.
	/// </summary>
	public Func<HttpRequestMessage, Task>? AuthorizeRequestAsync { get; set; }

	/// <summary>The product name reported in the User-Agent when the client creates its own <see cref="HttpClient"/>.</summary>
	public string? UserAgentProductName { get; set; }

	/// <summary>The product version reported in the User-Agent when the client creates its own <see cref="HttpClient"/>.</summary>
	public string? UserAgentProductVersion { get; set; }

	/// <summary>Validates the configuration, throwing if a required value is missing or out of range.</summary>
	/// <exception cref="ArgumentNullException"><see cref="BaseUrl"/> or <see cref="StateStore"/> is null.</exception>
	/// <exception cref="ArgumentException"><see cref="BaseUrl"/> is a relative URI (an absolute URI is required to compose request URLs).</exception>
	/// <exception cref="ArgumentOutOfRangeException"><see cref="PollInterval"/> is below <see cref="MinimumPollInterval"/>, <see cref="MaxPollDuration"/> is not at least <see cref="PollInterval"/>, or <see cref="BackoffCap"/> is below <see cref="PollInterval"/>.</exception>
	public void Validate()
	{
		if (BaseUrl == null)
		{
			throw new ArgumentNullException(nameof(BaseUrl), "BaseUrl is required; use a SmartConnectEnvironments value.");
		}

		// A relative BaseUrl passes the null check but the client reads BaseUrl.AbsoluteUri at construction,
		// which throws an undocumented InvalidOperationException for a relative URI. Reject it here so the
		// failure names BaseUrl as the culprit and stays within the documented exception contract.
		if (!BaseUrl.IsAbsoluteUri)
		{
			throw new ArgumentException("BaseUrl must be an absolute URI (e.g. https://…); use a SmartConnectEnvironments value.", nameof(BaseUrl));
		}

		if (StateStore == null)
		{
			throw new ArgumentNullException(nameof(StateStore), "StateStore is required — transaction state persistence is mandatory for crash recovery.");
		}

		if (PollInterval < MinimumPollInterval)
		{
			throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, $"PollInterval must be at least {MinimumPollInterval.TotalSeconds:0}s; the service rate-limits faster polling.");
		}

		// A MaxPollDuration below one PollInterval would make the poll loop give up before its first poll —
		// returning Unknown for a transaction that may have been sent. Reject it at construction.
		if (MaxPollDuration < PollInterval)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxPollDuration), MaxPollDuration, "MaxPollDuration must be at least PollInterval; otherwise polling gives up before the first poll.");
		}

		// BackoffCap is the ceiling for 429 backoff and the Retry-After clamp; below PollInterval it would
		// collapse the backoff into a tight re-poll storm.
		if (BackoffCap < PollInterval)
		{
			throw new ArgumentOutOfRangeException(nameof(BackoffCap), BackoffCap, "BackoffCap must be at least PollInterval; it is the upper bound for 429 backoff.");
		}
	}
}
