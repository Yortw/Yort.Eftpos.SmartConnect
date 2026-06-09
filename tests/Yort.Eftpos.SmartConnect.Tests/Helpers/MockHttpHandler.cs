using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.Tests.Helpers;

/// <summary>
/// An <see cref="HttpMessageHandler"/> whose behaviour is supplied per-test as a delegate. Records every
/// request (method, URI, headers, body) so tests can assert on the actual wire traffic.
/// </summary>
public sealed class MockHttpHandler : HttpMessageHandler
{
	private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;
	private readonly List<RecordedRequest> _requests = new List<RecordedRequest>();

	public MockHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
	{
		_responder = responder ?? throw new ArgumentNullException(nameof(responder));
	}

	public IReadOnlyList<RecordedRequest> Requests => _requests;

	public int RequestCount => _requests.Count;

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		// Snapshot everything assertions need NOW — HttpClient disposes request content after the send,
		// so a test holding the live HttpRequestMessage would read disposed state later.
		var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
		_requests.Add(new RecordedRequest
		{
			Method = request.Method,
			Uri = request.RequestUri,
			ContentType = request.Content?.Headers.ContentType?.MediaType,
			Body = body,
			Headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray())
		});

		return await _responder(request);
	}
}

/// <summary>An immutable snapshot of a single request seen by <see cref="MockHttpHandler"/>.</summary>
public sealed class RecordedRequest
{
	public HttpMethod Method { get; init; } = HttpMethod.Get;
	public Uri? Uri { get; init; }
	public string? ContentType { get; init; }
	public string? Body { get; init; }
	public IReadOnlyDictionary<string, string[]> Headers { get; init; } = new Dictionary<string, string[]>();
}
