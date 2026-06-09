using System;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// Thrown when an exchange with SmartConnect could not be completed — the library never got an answer.
/// (When SmartConnect <em>does</em> answer, even with an error, the library returns a result instead.)
/// Inspect <see cref="Delivery"/> before retrying: <see cref="SmartConnectRequestDelivery.NotSent"/> means
/// nothing reached the service and retry is safe; <see cref="SmartConnectRequestDelivery.Unknown"/> means
/// the request may have been processed — never blind-retry a financial operation on it.
/// </summary>
/// <remarks>
/// The original BCL failure (e.g. <c>SocketException</c>, <c>WebException</c>, <c>TaskCanceledException</c>)
/// is preserved as <see cref="Exception.InnerException"/> for logs and telemetry. The message text of this
/// exception never includes the request URL (poll URLs carry a bearer credential).
/// </remarks>
public sealed class SmartConnectTransportException : SmartConnectException
{
	/// <summary>Creates the exception for the given delivery knowledge and underlying cause.</summary>
	/// <param name="delivery">What is known about whether the request reached the service.</param>
	/// <param name="innerException">The original transport failure.</param>
	public SmartConnectTransportException(SmartConnectRequestDelivery delivery, Exception innerException)
		: base(GetMessage(delivery), innerException)
	{
		Delivery = delivery;
	}

	/// <summary>What is known about whether the request reached SmartConnect. See enum members for retry guidance.</summary>
	public SmartConnectRequestDelivery Delivery { get; }

	private static string GetMessage(SmartConnectRequestDelivery delivery)
	{
		// Deliberately no request details here — the poll URL carries the merchantAccessToken bearer
		// credential and consumers will log ex.Message.
		return delivery == SmartConnectRequestDelivery.NotSent
			? "The request could not be sent to SmartConnect — the service was never reached. The operation is safe to retry. See InnerException for the underlying cause."
			: "Communication with SmartConnect failed and the outcome is unknown — the request may have been processed. Do not blind-retry a financial operation. See InnerException for the underlying cause.";
	}
}
