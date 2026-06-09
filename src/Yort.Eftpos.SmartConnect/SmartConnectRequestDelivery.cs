namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// What the library knows about whether a failed request reached SmartConnect. See
/// <see cref="SmartConnectTransportException.Delivery"/>.
/// </summary>
public enum SmartConnectRequestDelivery
{
	/// <summary>
	/// The failure may have occurred after the request reached the service — it MAY have been processed.
	/// Never blind-retry a financial request on this value. This is the conservative default for any
	/// failure that cannot be proven to have happened before the request was sent.
	/// </summary>
	Unknown = 0,

	/// <summary>
	/// The failure provably occurred before any request bytes left this machine (DNS resolution, TCP
	/// connect, or TLS handshake). Nothing happened service-side; the operation is safe to retry.
	/// </summary>
	NotSent = 1
}
