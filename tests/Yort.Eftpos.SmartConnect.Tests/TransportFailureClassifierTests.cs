using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using Xunit;
using Yort.Eftpos.SmartConnect.Internal;

namespace Yort.Eftpos.SmartConnect.Tests;

/// <summary>
/// Tests for the transport-failure classifier (ADR Decision 9). NotSent must only ever be returned for
/// failures that provably occur before the request leaves (DNS, TCP connect, TLS handshake); everything
/// ambiguous is Unknown. These tests construct both runtimes' exception shapes directly — net48 wraps
/// WebException, modern .NET wraps SocketException — which proves the classifier logic, not that each
/// runtime actually produces these shapes (that is the net48 smoke probe's job, pre-release).
/// </summary>
public class TransportFailureClassifierTests
{
	// --- net48 shapes: HttpRequestException wrapping WebException ---

	[Theory]
	[InlineData(WebExceptionStatus.NameResolutionFailure)]
	[InlineData(WebExceptionStatus.ProxyNameResolutionFailure)]
	[InlineData(WebExceptionStatus.ConnectFailure)]
	[InlineData(WebExceptionStatus.TrustFailure)]
	[InlineData(WebExceptionStatus.SecureChannelFailure)]
	public void Classify_WebException_PreSendStatus_IsNotSent(WebExceptionStatus status)
	{
		var exception = new HttpRequestException("send failed", new WebException("web failure", status));

		Assert.Equal(SmartConnectRequestDelivery.NotSent, TransportFailureClassifier.Classify(exception));
	}

	[Theory]
	[InlineData(WebExceptionStatus.Timeout)]
	[InlineData(WebExceptionStatus.ReceiveFailure)]
	[InlineData(WebExceptionStatus.KeepAliveFailure)]
	[InlineData(WebExceptionStatus.UnknownError)]
	[InlineData(WebExceptionStatus.SendFailure)]
	public void Classify_WebException_AmbiguousStatus_IsUnknown(WebExceptionStatus status)
	{
		var exception = new HttpRequestException("send failed", new WebException("web failure", status));

		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(exception));
	}

	// --- modern .NET shapes: HttpRequestException wrapping SocketException ---

	[Theory]
	[InlineData(SocketError.HostNotFound)]
	[InlineData(SocketError.TryAgain)]
	[InlineData(SocketError.NoData)]
	[InlineData(SocketError.ConnectionRefused)]
	public void Classify_SocketException_PreSendCode_IsNotSent(SocketError socketError)
	{
		var exception = new HttpRequestException("send failed", new SocketException((int)socketError));

		Assert.Equal(SmartConnectRequestDelivery.NotSent, TransportFailureClassifier.Classify(exception));
	}

	[Theory]
	[InlineData(SocketError.TimedOut)]
	[InlineData(SocketError.ConnectionReset)]
	[InlineData(SocketError.ConnectionAborted)]
	public void Classify_SocketException_AmbiguousCode_IsUnknown(SocketError socketError)
	{
		var exception = new HttpRequestException("send failed", new SocketException((int)socketError));

		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(exception));
	}

	[Fact]
	public void Classify_TlsHandshakeFailure_IsNotSent()
	{
		// The TLS handshake completes before any request bytes are sent.
		var exception = new HttpRequestException("ssl failed", new AuthenticationException("handshake failed"));

		Assert.Equal(SmartConnectRequestDelivery.NotSent, TransportFailureClassifier.Classify(exception));
	}

	// --- timeouts and cancellation ---

	[Fact]
	public void Classify_TaskCanceledException_IsUnknown()
	{
		// HttpClient.Timeout surfaces as TaskCanceledException — the request may already be in flight.
		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(new TaskCanceledException("timed out")));
	}

	[Fact]
	public void Classify_OperationCanceledException_IsUnknown()
	{
		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(new OperationCanceledException("cancelled")));
	}

	[Fact]
	public void Classify_TimeoutExceptionInChain_IsUnknown()
	{
		// .NET 5+ timeout shape: TaskCanceledException with inner TimeoutException.
		var exception = new TaskCanceledException("timed out", new TimeoutException("HttpClient.Timeout elapsed"));

		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(exception));
	}

	// --- mid-stream failures ---

	[Fact]
	public void Classify_IOException_IsUnknown()
	{
		var exception = new HttpRequestException("read failed", new IOException("connection closed mid-read"));

		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(exception));
	}

	// --- conservative default (invariant) ---

	[Fact]
	public void Classify_UnrecognisedExceptionType_IsUnknown_NeverNotSent()
	{
		// The load-bearing invariant: anything we can't prove pre-send must classify Unknown. A new/unknown
		// failure shape must never license a blind re-POST.
		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(new InvalidOperationException("novel failure")));
	}

	[Fact]
	public void Classify_BareHttpRequestException_NoInner_IsUnknown()
	{
		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(new HttpRequestException("failed")));
	}

	// --- (R1) chain-verdict rule: Unknown always wins a mixed chain ---

	[Fact]
	public void Classify_MixedChain_UnknownNodeAboveNotSentNode_IsUnknown()
	{
		// A timeout (Unknown) wrapping a connection-refused (NotSent): "deepest classifiable" would say
		// NotSent — the exact misclassification that could double-charge. Unknown must win.
		var exception = new HttpRequestException(
			"send failed",
			new TaskCanceledException("timed out", new SocketException((int)SocketError.ConnectionRefused)));

		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(exception));
	}

	[Fact]
	public void Classify_MixedChain_NotSentNodeAboveUnknownNode_IsUnknown()
	{
		var exception = new WebException(
			"connect failed",
			new SocketException((int)SocketError.TimedOut),
			WebExceptionStatus.ConnectFailure,
			null);

		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(exception));
	}

	[Fact]
	public void Classify_AggregateException_MixedBranches_IsUnknown()
	{
		var exception = new AggregateException(
			new SocketException((int)SocketError.ConnectionRefused),
			new SocketException((int)SocketError.TimedOut));

		Assert.Equal(SmartConnectRequestDelivery.Unknown, TransportFailureClassifier.Classify(exception));
	}

	[Fact]
	public void Classify_AggregateException_AllNotSentBranches_IsNotSent()
	{
		var exception = new AggregateException(
			new SocketException((int)SocketError.ConnectionRefused),
			new WebException("dns", WebExceptionStatus.NameResolutionFailure));

		Assert.Equal(SmartConnectRequestDelivery.NotSent, TransportFailureClassifier.Classify(exception));
	}
}
