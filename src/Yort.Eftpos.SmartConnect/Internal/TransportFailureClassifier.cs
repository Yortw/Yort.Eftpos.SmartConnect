using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Yort.Eftpos.SmartConnect.Internal;

/// <summary>
/// Classifies transport failures for <see cref="SmartConnectTransportException"/> (ADR Decision 9).
/// </summary>
/// <remarks>
/// Chain-verdict rule: every node in the exception graph is classified; the verdict is
/// <see cref="SmartConnectRequestDelivery.NotSent"/> only if at least one node is provably pre-send AND no
/// node is ambiguous — <c>Unknown</c> always wins a mixed chain. ("Deepest classifiable cause" was rejected:
/// it can return NotSent from a chain that also contains an ambiguous node, which would license an unsafe
/// re-POST of a payment.) Handles both runtime shapes: net48 wraps <see cref="WebException"/>; modern .NET
/// wraps <see cref="SocketException"/>.
/// </remarks>
internal static class TransportFailureClassifier
{
	private enum NodeVerdict
	{
		/// <summary>A wrapper or unrecognised type — contributes nothing to the verdict.</summary>
		Neutral,
		NotSent,
		Unknown
	}

	/// <summary>Returns whether <paramref name="exception"/> is a transport-layer failure the send path should wrap.</summary>
	public static bool IsTransportFailure(Exception exception)
	{
		return exception is HttpRequestException
			|| exception is WebException
			|| exception is SocketException
			|| exception is IOException
			|| exception is OperationCanceledException
			|| exception is AuthenticationException
			|| exception is TimeoutException;
	}

	/// <summary>Classifies a transport failure as provably-not-sent or outcome-unknown (the conservative default).</summary>
	public static SmartConnectRequestDelivery Classify(Exception exception)
	{
		var anyNotSent = false;
		var anyUnknown = false;
		Walk(exception, ref anyNotSent, ref anyUnknown);

		return anyNotSent && !anyUnknown
			? SmartConnectRequestDelivery.NotSent
			: SmartConnectRequestDelivery.Unknown;
	}

	private static void Walk(Exception? exception, ref bool anyNotSent, ref bool anyUnknown)
	{
		if (exception == null)
		{
			return;
		}

		switch (ClassifyNode(exception))
		{
			case NodeVerdict.NotSent:
				anyNotSent = true;
				break;
			case NodeVerdict.Unknown:
				anyUnknown = true;
				break;
		}

		if (exception is AggregateException aggregate)
		{
			// InnerException duplicates the first branch of InnerExceptions — walk the branches only.
			foreach (var branch in aggregate.InnerExceptions)
			{
				Walk(branch, ref anyNotSent, ref anyUnknown);
			}

			return;
		}

		Walk(exception.InnerException, ref anyNotSent, ref anyUnknown);
	}

	private static NodeVerdict ClassifyNode(Exception exception)
	{
		// net48 shape: HttpClient failures wrap WebException.
		if (exception is WebException webException)
		{
			switch (webException.Status)
			{
				case WebExceptionStatus.NameResolutionFailure:
				case WebExceptionStatus.ProxyNameResolutionFailure:
				case WebExceptionStatus.ConnectFailure:
				case WebExceptionStatus.TrustFailure:
				case WebExceptionStatus.SecureChannelFailure:
					return NodeVerdict.NotSent;
				default:
					// Any other WebException is a transport failure of ambiguous timing (Timeout,
					// SendFailure, ReceiveFailure, ...) — never NotSent.
					return NodeVerdict.Unknown;
			}
		}

		// Modern .NET shape: HttpClient failures wrap SocketException.
		if (exception is SocketException socketException)
		{
			switch (socketException.SocketErrorCode)
			{
				case SocketError.HostNotFound:
				case SocketError.TryAgain:
				case SocketError.NoData:
				case SocketError.ConnectionRefused:
				// Connect-phase routing/interface failures: no route means the request never left the machine, the
				// same pre-send class as ConnectionRefused. On net48 these all surface as WebExceptionStatus.ConnectFailure
				// (already NotSent above), so classifying them NotSent here keeps the two runtimes' verdicts consistent.
				case SocketError.NetworkDown:
				case SocketError.NetworkUnreachable:
				case SocketError.HostUnreachable:
					return NodeVerdict.NotSent;
				default:
					return NodeVerdict.Unknown;
			}
		}

		// The TLS handshake completes before any request bytes are sent.
		if (exception is AuthenticationException)
		{
			return NodeVerdict.NotSent;
		}

		// Timeouts (HttpClient.Timeout surfaces as TaskCanceledException) and mid-stream IO failures:
		// the request may already be in flight or processed.
		if (exception is OperationCanceledException || exception is TimeoutException || exception is IOException)
		{
			return NodeVerdict.Unknown;
		}

		// Wrappers (HttpRequestException, AggregateException) and unrecognised types contribute nothing;
		// a chain of only-neutral nodes falls through to the conservative Unknown verdict.
		return NodeVerdict.Neutral;
	}
}
