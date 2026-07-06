using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
/// <para>Cancellation: the public async methods intentionally take no <see cref="System.Threading.CancellationToken"/>
/// (ADR Decision 3). SmartConnect has no cancel endpoint, so cancelling a poll would only orphan an in-flight
/// payment; a poll that exceeds its internal maximum duration returns a terminal
/// <see cref="SmartConnectTransactionStatus.Unknown"/> result rather than throwing. To shut down, persist the polling
/// URL and resume after restart, or dispose the client mid-poll.</para>
/// </remarks>
public sealed class SmartConnectClient : IDisposable
{
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

	// Configuration is snapshotted at construction, never read live: Validate() runs once at construction,
	// so reading these back from a caller-held (mutable) config object would let a post-construction mutation
	// bypass the validated invariants mid-flight (e.g. PollInterval below the minimum, or StateStore nulled).
	private readonly ISmartConnectTransactionState _stateStore;
	private readonly ILogger? _logger;
	private readonly Func<HttpRequestMessage, Task>? _authorizeRequestAsync;
	private readonly TimeSpan _pollInterval;
	private readonly TimeSpan _maxPollDuration;
	private readonly TimeSpan _backoffCap;
	private readonly HttpClient _httpClient;
	private readonly bool _ownsHttpClient;
	private readonly string _baseUrl;
	// volatile: Dispose() may run on a different thread than an in-flight poll loop (the documented
	// dispose-mid-poll shutdown path), so the loop must observe the write without a stale cache read.
	private volatile bool _disposed;

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
		_stateStore = configuration.StateStore!;
		_logger = configuration.Logger;
		_authorizeRequestAsync = configuration.AuthorizeRequestAsync;
		_pollInterval = configuration.PollInterval;
		_maxPollDuration = configuration.MaxPollDuration;
		_backoffCap = configuration.BackoffCap;
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
	/// <exception cref="SmartConnectTransportException">The exchange could not be completed — no answer was
	/// received. Check <see cref="SmartConnectTransportException.Delivery"/> before retrying.</exception>
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

			try
			{
				using (var response = await SendAsync(httpRequest).ConfigureAwait(false))
				{
					if (response.IsSuccessStatusCode)
					{
						return new SmartConnectPairingResult { Success = true };
					}

					var body = response.Content == null
						? string.Empty
						: await response.Content.ReadAsStringAsync().ConfigureAwait(false);

					var errorMessage = GetErrorMessage(response, body);
					// Pairing was the one operation that logged on no failure path, so a terse service error
					// (e.g. a bare "HTTP 401") reached the operator with no diagnostic trail behind it. Log the
					// status and message here as the transaction paths already do.
					SafeLog(LogLevel.Error, null, "SmartConnect rejected pairing: HTTP {StatusCode} {ServiceError}",
						(int)response.StatusCode, errorMessage);

					return new SmartConnectPairingResult
					{
						Success = false,
						ErrorMessage = errorMessage
					};
				}
			}
			catch (SmartConnectTransportException ex)
			{
				// The dialog/controller deliberately shows a generic, reassuring message and discards this
				// exception, so the underlying cause (socket/TLS/DNS) would otherwise be lost entirely. Log it
				// here — the only place with both the operation context and the real cause — then rethrow to
				// preserve the documented throw contract.
				SafeLog(LogLevel.Warning, ex, "Pairing request failed at transport ({Delivery}, {FailureType}) — no service response received.",
					ex.Delivery, ex.InnerException?.GetType().Name);
				throw;
			}
		}
	}

	/// <summary>
	/// Processes a transaction: persists the recovery sentinel, POSTs to <c>/Transaction</c>, records the
	/// polling details, then polls to a terminal outcome. Never throws for runtime conditions (ADR
	/// Decision 9) — all operational failures surface as a result; check
	/// <see cref="SmartConnectTransactionResult.Status"/> and <see cref="SmartConnectTransactionResult.FailureCause"/>.
	/// Always handle <see cref="SmartConnectTransactionStatus.Unknown"/> explicitly.
	/// </summary>
	/// <remarks>There is deliberately no <see cref="System.Threading.CancellationToken"/> (ADR Decision 3): the
	/// transaction cannot be recalled once sent, so abandoning the wait would only orphan a possibly-live payment.
	/// A wait that exceeds the internal maximum poll duration returns <see cref="SmartConnectTransactionStatus.Unknown"/>;
	/// to abandon a wait during shutdown, dispose the client and resume from the persisted polling URL after restart.</remarks>
	/// <param name="request">The transaction to process. <c>ClientTransactionRef</c> must be stable across a
	/// restart for the same logical transaction — it is the crash-recovery key.</param>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field is blank, or the total amount is not positive.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	public Task<SmartConnectTransactionResult> ProcessTransactionAsync(SmartConnectTransactionRequest request)
		=> ProcessTransactionAsync(request, null);

	/// <summary>
	/// Processes a transaction, reporting per-poll progress to <paramref name="progress"/> for UI feedback.
	/// See <see cref="ProcessTransactionAsync(SmartConnectTransactionRequest)"/> for the full contract.
	/// </summary>
	/// <param name="request">The transaction to process.</param>
	/// <param name="progress">An optional progress sink; reports carry no outcome responsibility.</param>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field is blank, or the total amount is not positive.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	public async Task<SmartConnectTransactionResult> ProcessTransactionAsync(SmartConnectTransactionRequest request, IProgress<SmartConnectPollingStatus>? progress)
	{
		ValidateTransactionRequest(request);

		// (F5/R3) The absolute pre-POST gate: if the sentinel cannot be persisted, nothing is sent. The
		// refusal is a result, not an escaping store exception type (ADR Decisions 9/10).
		try
		{
			await _stateStore.SaveTransactionAttemptAsync(request.ClientTransactionRef, request.TransactionType, request.AmountTotal.ToCents()).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			SafeLog(LogLevel.Error, ex, "State store refused the pre-POST sentinel write for {ClientTransactionRef} — transaction NOT sent. EFTPOS is unavailable at this register until the store recovers.", request.ClientTransactionRef);
			return new SmartConnectTransactionResult
			{
				Status = SmartConnectTransactionStatus.Failed,
				FailureCause = SmartConnectFailureCause.StateStoreFailure
			};
		}

		SafeLog(LogLevel.Information, null, "Sending {TransactionType} for {ClientTransactionRef} (amount {AmountTotalCents} cents).", request.TransactionType, request.ClientTransactionRef, request.AmountTotal.ToCents());

		HttpResponseMessage response;
		try
		{
			response = await PostTransactionAsync(request).ConfigureAwait(false);
		}
		catch (SmartConnectTransportException ex) when (ex.Delivery == SmartConnectRequestDelivery.NotSent)
		{
			// Provably never reached the service — close the sentinel; the caller may retry freely.
			SafeLog(LogLevel.Warning, ex, "Transaction POST never reached SmartConnect ({FailureType}) for {ClientTransactionRef} — nothing was sent; safe to retry.", ex.InnerException?.GetType().Name, request.ClientTransactionRef);
			await CloseSentinelQuietlyAsync(request.ClientTransactionRef, SmartConnectTransactionStatus.Failed).ConfigureAwait(false);
			return new SmartConnectTransactionResult
			{
				Status = SmartConnectTransactionStatus.Failed,
				FailureCause = SmartConnectFailureCause.TransportNotSent
			};
		}
		catch (SmartConnectTransportException ex)
		{
			// Outcome unknown — the POST may have been processed. The sentinel MUST stay pending so
			// recovery investigates; closing it would hide a possibly-live charge.
			SafeLog(LogLevel.Error, ex, "Transaction POST outcome is UNKNOWN ({FailureType}) for {ClientTransactionRef} — the transaction may have been processed; recovery must investigate. Distinct from poll exhaustion: no response was ever received.", ex.InnerException?.GetType().Name, request.ClientTransactionRef);
			return new SmartConnectTransactionResult
			{
				Status = SmartConnectTransactionStatus.Unknown,
				FailureCause = SmartConnectFailureCause.TransportUnknown
			};
		}

		return await HandleInitialResponseAsync(request, response, progress).ConfigureAwait(false);
	}

	// The post-POST half of the Decision-9 mapping: 4xx rejection → ServiceError (sentinel closed);
	// 5xx/408 (possibly intermediary-generated) and unusable 200 → Unknown/TransportUnknown (sentinel
	// pending); good response → persist polling details (best-effort) and poll to a terminal outcome.
	private async Task<SmartConnectTransactionResult> HandleInitialResponseAsync(SmartConnectTransactionRequest request, HttpResponseMessage response, IProgress<SmartConnectPollingStatus>? progress)
	{
		using (response)
		{
			var body = response.Content == null
				? string.Empty
				: await response.Content.ReadAsStringAsync().ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				if (IsPossiblyIntermediaryStatus(response.StatusCode))
				{
					// (Decision 9, 2026-07-02 update) 5xx/408 are routinely generated by an intermediary
					// (LB/WAF/proxy) AFTER the origin received the POST — a 504 literally means "upstream
					// didn't answer in time". Epistemically the same state as a transport timeout: the
					// transaction may be live on the pinpad, so Unknown, sentinel pending. Labelling it
					// Failed ("blind retry will fail again") would invite a re-tender over a live charge.
					var intermediaryError = GetErrorMessage(response, body);
					SafeLog(LogLevel.Error, null, "SmartConnect transaction POST for {ClientTransactionRef} answered HTTP {StatusCode}, which an intermediary can generate after the service received the request — outcome is UNKNOWN; manual reconciliation required. {ServiceError}", request.ClientTransactionRef, (int)response.StatusCode, intermediaryError);
					return new SmartConnectTransactionResult
					{
						Status = SmartConnectTransactionStatus.Unknown,
						FailureCause = SmartConnectFailureCause.TransportUnknown,
						ErrorMessage = intermediaryError
					};
				}

				// A 4xx is a genuine verdict that the request was not processed (429 included: rate-limited
				// means refused wherever it was generated) — terminal; fix the request/config, blind retry
				// will fail again.
				var serviceError = GetErrorMessage(response, body);
				SafeLog(LogLevel.Error, null, "SmartConnect rejected the transaction POST for {ClientTransactionRef}: {ServiceError}", request.ClientTransactionRef, serviceError);
				await CloseSentinelQuietlyAsync(request.ClientTransactionRef, SmartConnectTransactionStatus.Failed).ConfigureAwait(false);
				return new SmartConnectTransactionResult
				{
					Status = SmartConnectTransactionStatus.Failed,
					FailureCause = SmartConnectFailureCause.ServiceError,
					ErrorMessage = serviceError
				};
			}

			InitialTransactionResponse initial;
			try
			{
				initial = TransactionResponseParser.ParseInitialResponse(body);
			}
			catch (JsonException)
			{
				initial = new InitialTransactionResponse();
			}

			if (string.IsNullOrEmpty(initial.PollingUrl))
			{
				// 200 but unusable — the transaction may be live on the pinpad with no way to poll it.
				// Outcome unknown, sentinel stays pending for recovery (F10).
				SafeLog(LogLevel.Error, null, "SmartConnect accepted the transaction POST for {ClientTransactionRef} but returned no polling URL — outcome is UNKNOWN; recovery must investigate.", request.ClientTransactionRef);
				return new SmartConnectTransactionResult
				{
					Status = SmartConnectTransactionStatus.Unknown,
					FailureCause = SmartConnectFailureCause.TransportUnknown,
					TransactionId = initial.TransactionId
				};
			}

			// (F2) transactionId ONLY — the URL carries the merchantAccessToken and is never logged.
			SafeLog(LogLevel.Information, null, "Polling URL received for {ClientTransactionRef} (transactionId {TransactionId}).", request.ClientTransactionRef, initial.TransactionId);

			try
			{
				await _stateStore.UpdatePollingDetailsAsync(request.ClientTransactionRef, initial.PollingUrl!, initial.TransactionId ?? string.Empty).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				// (R3) Best-effort happy path: the transaction is irrevocably in flight (no cancel API)
				// and most likely completes normally — continue on the in-memory URL. (G7) The URL was an
				// argument to the failing call and store exceptions commonly echo arguments: log the
				// exception TYPE only, never its message.
				SafeLog(LogLevel.Error, null, "Failed to persist polling details ({ExceptionType}) for {ClientTransactionRef} — continuing on the in-memory URL. Crash recovery for this transaction is degraded: manual verification may be needed if the POS terminates before completion.", ex.GetType().Name, request.ClientTransactionRef);
			}

			return await PollForResultAsync(initial.PollingUrl!, initial.TransactionId, request.ClientTransactionRef, progress).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Resumes polling a persisted polling URL after a crash/restart — the only programmatic way to recover
	/// a transaction's outcome. Jumps straight to the poll loop: the sentinel already exists from before the
	/// crash, so neither <c>SaveTransactionAttemptAsync</c> nor <c>UpdatePollingDetailsAsync</c> is called;
	/// <c>UpdateCompletedAsync</c> IS called when a terminal state is reached. Never throws for runtime
	/// conditions — an expired URL surfaces as <see cref="SmartConnectFailureCause.PollingUrlInvalid"/>,
	/// meaning the outcome can no longer be determined programmatically: resolve it by manual reconciliation.
	/// </summary>
	/// <param name="pollingUrl">The persisted polling URL (carries the access token — handle accordingly).</param>
	/// <param name="clientTransactionRef">The reference the transaction's state is persisted under.</param>
	/// <exception cref="ArgumentNullException"><paramref name="pollingUrl"/> or <paramref name="clientTransactionRef"/> is null.</exception>
	/// <exception cref="ArgumentException"><paramref name="pollingUrl"/> or <paramref name="clientTransactionRef"/> is empty/whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	public Task<SmartConnectTransactionResult> ResumePollingAsync(string pollingUrl, string clientTransactionRef)
		=> ResumePollingAsync(pollingUrl, clientTransactionRef, null);

	/// <summary>
	/// Resumes polling a persisted polling URL after a crash/restart, reporting per-poll progress. See
	/// <see cref="ResumePollingAsync(string, string)"/> for the full contract.
	/// </summary>
	/// <param name="pollingUrl">The persisted polling URL.</param>
	/// <param name="clientTransactionRef">The reference the transaction's state is persisted under.</param>
	/// <param name="progress">An optional progress sink.</param>
	/// <exception cref="ArgumentNullException"><paramref name="pollingUrl"/> or <paramref name="clientTransactionRef"/> is null.</exception>
	/// <exception cref="ArgumentException"><paramref name="pollingUrl"/> or <paramref name="clientTransactionRef"/> is empty/whitespace.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	public Task<SmartConnectTransactionResult> ResumePollingAsync(string pollingUrl, string clientTransactionRef, IProgress<SmartConnectPollingStatus>? progress)
	{
		if (pollingUrl == null)
		{
			throw new ArgumentNullException(nameof(pollingUrl));
		}

		if (string.IsNullOrWhiteSpace(pollingUrl))
		{
			throw new ArgumentException("A polling URL is required.", nameof(pollingUrl));
		}

		if (clientTransactionRef == null)
		{
			throw new ArgumentNullException(nameof(clientTransactionRef));
		}

		if (string.IsNullOrWhiteSpace(clientTransactionRef))
		{
			throw new ArgumentException("A client transaction reference is required.", nameof(clientTransactionRef));
		}

		ThrowIfDisposed();

		return PollForResultAsync(pollingUrl, null, clientTransactionRef, progress);
	}

	/// <summary>
	/// Queries the result of the terminal's last transaction via the deprecated <c>Journal.GetTransResult</c>.
	/// This is a DIAGNOSTIC, not a recovery mechanism: the call is device-scoped (on a shared terminal it can
	/// return another register's transaction) and nothing in the response reliably identifies the returned
	/// transaction as any particular sale — so its result must never be adopted as a transaction's outcome.
	/// Use it as supporting evidence during manual reconciliation. Makes NO state-store calls — the caller
	/// owns any existing sentinel. The POST phase throws the typed transport exception (this is a read-only,
	/// idempotent query — safe to retry the whole call regardless of
	/// <see cref="SmartConnectTransportException.Delivery"/>); the poll phase is result-based.
	/// </summary>
	/// <param name="registration">The registration triple, matching pairing and the original transaction.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field of <paramref name="registration"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The journal POST could not be completed.</exception>
	public Task<SmartConnectTransactionResult> GetLastTransactionResultAsync(SmartConnectRegistration registration)
		=> GetLastTransactionResultAsync(registration, null);

	/// <summary>
	/// Queries the result of the register's last transaction, reporting per-poll progress. See
	/// <see cref="GetLastTransactionResultAsync(SmartConnectRegistration)"/> for the full contract.
	/// </summary>
	/// <param name="registration">The registration triple, matching pairing and the original transaction.</param>
	/// <param name="progress">An optional progress sink.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field of <paramref name="registration"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The journal POST could not be completed.</exception>
	public Task<SmartConnectTransactionResult> GetLastTransactionResultAsync(SmartConnectRegistration registration, IProgress<SmartConnectPollingStatus>? progress)
		=> ExecuteNonFinancialCoreAsync(registration, SmartConnectTransactionType.JournalGetTransResult, progress);

	/// <summary>
	/// Asks the cloud whether it can reach the paired terminal (<c>Terminal.GetStatus</c>) — the natural
	/// "is this thing even paired and at its idle screen?" probe before tendering (the terminal must be at
	/// idle BEFORE a transaction is sent; the cloud does not hold delivery for a terminal that comes online
	/// late). Read-only and safe to retry. Makes no state-store calls.
	/// </summary>
	/// <remarks><see cref="SmartConnectOperationResult.Status"/> reflects whether the operation succeeded
	/// (taken from the response's <c>Result == "OK"</c>); the operation-specific detail (e.g. the terminal's
	/// own <c>Status</c> value) is in <see cref="SmartConnectResult.RawData"/>, which the typed surface
	/// intentionally leaves un-modelled until each response shape is verified.</remarks>
	/// <param name="registration">The registration triple, as used at pairing.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field of <paramref name="registration"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The POST could not be completed.</exception>
	public Task<SmartConnectOperationResult> GetTerminalStatusAsync(SmartConnectRegistration registration)
		=> GetTerminalStatusAsync(registration, null);

	/// <summary>Asks the cloud whether it can reach the paired terminal, with progress reporting. See <see cref="GetTerminalStatusAsync(SmartConnectRegistration)"/>.</summary>
	/// <param name="registration">The registration triple, as used at pairing.</param>
	/// <param name="progress">An optional progress sink.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field of <paramref name="registration"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The POST could not be completed.</exception>
	public Task<SmartConnectOperationResult> GetTerminalStatusAsync(SmartConnectRegistration registration, IProgress<SmartConnectPollingStatus>? progress)
		=> ExecuteOperationAsync(registration, SmartConnectTransactionType.TerminalGetStatus, progress);

	/// <summary>Performs an acquirer logon (<c>Acquirer.Logon</c>). Read-only/safe to retry; no state-store calls. A documented transaction-type operation: it returns a <see cref="SmartConnectTransactionResult"/> mapped like a transaction (the acquirer reference and receipt are populated; the money/card fields are unused). Handle <see cref="SmartConnectTransactionStatus.Unknown"/>.</summary>
	/// <param name="registration">The registration triple, as used at pairing.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field of <paramref name="registration"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The POST could not be completed.</exception>
	public Task<SmartConnectTransactionResult> LogonAsync(SmartConnectRegistration registration)
		=> LogonAsync(registration, null);

	/// <summary>Performs an acquirer logon, with progress reporting. See <see cref="LogonAsync(SmartConnectRegistration)"/>.</summary>
	/// <param name="registration">The registration triple, as used at pairing.</param>
	/// <param name="progress">An optional progress sink.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field of <paramref name="registration"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The POST could not be completed.</exception>
	public Task<SmartConnectTransactionResult> LogonAsync(SmartConnectRegistration registration, IProgress<SmartConnectPollingStatus>? progress)
		=> ExecuteNonFinancialCoreAsync(registration, SmartConnectTransactionType.AcquirerLogon, progress);

	/// <summary>Queries the current settlement totals (<c>Acquirer.Settlement.Inquiry</c>). Read-only/safe to retry; no state-store calls. Settlement shares the client-wide <see cref="SmartConnectClientConfiguration.MaxPollDuration"/> budget. A documented transaction-type operation: it returns a <see cref="SmartConnectTransactionResult"/> mapped like a transaction (the totals are in the receipt; the money/card fields are unused). Handle <see cref="SmartConnectTransactionStatus.Unknown"/>.</summary>
	/// <param name="registration">The registration triple, as used at pairing.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field of <paramref name="registration"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The POST could not be completed.</exception>
	public Task<SmartConnectTransactionResult> SettlementInquiryAsync(SmartConnectRegistration registration)
		=> SettlementInquiryAsync(registration, null);

	/// <summary>Queries the current settlement totals, with progress reporting. See <see cref="SettlementInquiryAsync(SmartConnectRegistration)"/>.</summary>
	/// <param name="registration">The registration triple, as used at pairing.</param>
	/// <param name="progress">An optional progress sink.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field of <paramref name="registration"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The POST could not be completed.</exception>
	public Task<SmartConnectTransactionResult> SettlementInquiryAsync(SmartConnectRegistration registration, IProgress<SmartConnectPollingStatus>? progress)
		=> ExecuteNonFinancialCoreAsync(registration, SmartConnectTransactionType.AcquirerSettlementInquiry, progress);

	/// <summary>
	/// Performs a settlement cutover (<c>Acquirer.Settlement.Cutover</c>) — closes the acquirer settlement
	/// window. <b>STATE-CHANGING, not idempotent:</b> on a <see cref="SmartConnectTransportException"/> with
	/// <see cref="SmartConnectRequestDelivery.Unknown"/>, or a result of
	/// <see cref="SmartConnectTransactionStatus.Unknown"/>, the cutover MAY have executed — verify via
	/// <see cref="SettlementInquiryAsync(SmartConnectRegistration)"/> before re-issuing; never blind-retry.
	/// Makes no state-store calls. Settlement shares the client-wide poll budget.
	/// </summary>
	/// <param name="registration">The registration triple, as used at pairing.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field of <paramref name="registration"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The POST could not be completed — see the Unknown-delivery caveat above.</exception>
	public Task<SmartConnectTransactionResult> SettlementCutoverAsync(SmartConnectRegistration registration)
		=> SettlementCutoverAsync(registration, null);

	/// <summary>Performs a settlement cutover, with progress reporting. See <see cref="SettlementCutoverAsync(SmartConnectRegistration)"/> — STATE-CHANGING; read its retry caveat.</summary>
	/// <param name="registration">The registration triple, as used at pairing.</param>
	/// <param name="progress">An optional progress sink.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field of <paramref name="registration"/> is blank.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The POST could not be completed — see the cutover retry caveat.</exception>
	public Task<SmartConnectTransactionResult> SettlementCutoverAsync(SmartConnectRegistration registration, IProgress<SmartConnectPollingStatus>? progress)
		=> ExecuteNonFinancialCoreAsync(registration, SmartConnectTransactionType.AcquirerSettlementCutover, progress);

	/// <summary>
	/// Sends an arbitrary NON-FINANCIAL transaction type — the escape hatch for vendor types this library
	/// does not yet know. Money-moving types are rejected: financial transactions must go through
	/// <see cref="ProcessTransactionAsync(SmartConnectTransactionRequest)"/>, where the crash-recovery
	/// sentinel is mandatory. Routing any money-moving vendor type this guard cannot recognise through this
	/// method voids the recovery guarantee — do not do it.
	/// </summary>
	/// <param name="registration">The registration triple, as used at pairing.</param>
	/// <param name="transactionType">The vendor <c>TransactionType</c> wire value.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> or <paramref name="transactionType"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field is blank, or <paramref name="transactionType"/> is a known financial type.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The POST could not be completed.</exception>
	public Task<SmartConnectOperationResult> ExecuteNonFinancialAsync(SmartConnectRegistration registration, string transactionType)
		=> ExecuteNonFinancialAsync(registration, transactionType, null);

	/// <summary>Sends an arbitrary NON-FINANCIAL transaction type, with progress reporting. See <see cref="ExecuteNonFinancialAsync(SmartConnectRegistration, string)"/>.</summary>
	/// <param name="registration">The registration triple, as used at pairing.</param>
	/// <param name="transactionType">The vendor <c>TransactionType</c> wire value.</param>
	/// <param name="progress">An optional progress sink.</param>
	/// <exception cref="ArgumentNullException"><paramref name="registration"/> or <paramref name="transactionType"/> is null.</exception>
	/// <exception cref="ArgumentException">A mandatory field is blank, or <paramref name="transactionType"/> is a known financial type.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	/// <exception cref="SmartConnectTransportException">The POST could not be completed.</exception>
	public Task<SmartConnectOperationResult> ExecuteNonFinancialAsync(SmartConnectRegistration registration, string transactionType, IProgress<SmartConnectPollingStatus>? progress)
	{
		if (transactionType == null)
		{
			throw new ArgumentNullException(nameof(transactionType));
		}

		if (string.IsNullOrWhiteSpace(transactionType))
		{
			throw new ArgumentException("A transaction type is required.", nameof(transactionType));
		}

		// (J2) The F5-bypass guard: financial types MUST take the sentinel path. One shared list
		// (SmartConnectTransactionType.IsKnownFinancial) so this guard and the financial path cannot drift.
		if (SmartConnectTransactionType.IsKnownFinancial(transactionType))
		{
			throw new ArgumentException($"'{transactionType}' is a financial transaction type — use ProcessTransactionAsync, which persists the mandatory crash-recovery sentinel.", nameof(transactionType));
		}

		return ExecuteOperationAsync(registration, transactionType, progress);
	}

	// The shared non-financial core (Task 12.7): registration triple + ASYNC + type, ZERO state-store calls
	// on any path including terminal state, POST transport failures propagate as the typed exception (R5 —
	// scoped to read-only ops; SettlementCutoverAsync documents its own retry caveat), poll phase result-based.
	private async Task<SmartConnectTransactionResult> ExecuteNonFinancialCoreAsync(SmartConnectRegistration registration, string transactionType, IProgress<SmartConnectPollingStatus>? progress)
	{
		if (registration == null)
		{
			throw new ArgumentNullException(nameof(registration));
		}

		RequireField(registration.POSRegisterID, nameof(registration.POSRegisterID));
		RequireField(registration.POSBusinessName, nameof(registration.POSBusinessName));
		RequireField(registration.POSVendorName, nameof(registration.POSVendorName));
		ThrowIfDisposed();

		SafeLog(LogLevel.Information, null, "Sending {TransactionType} (non-financial operation).", transactionType);

		var fields = new List<KeyValuePair<string, string?>>(5)
		{
			new KeyValuePair<string, string?>("POSRegisterID", registration.POSRegisterID),
			new KeyValuePair<string, string?>("POSBusinessName", registration.POSBusinessName),
			new KeyValuePair<string, string?>("POSVendorName", registration.POSVendorName),
			new KeyValuePair<string, string?>("TransactionMode", "ASYNC"),
			new KeyValuePair<string, string?>("TransactionType", transactionType)
		};

		// (R5) No transport catch here — the typed exception propagates by design.
		HttpResponseMessage response;
		using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/Transaction"))
		{
			httpRequest.Content = new StringContent(FormUrlEncoder.Encode(fields), Encoding.UTF8, "application/x-www-form-urlencoded");
			response = await SendAsync(httpRequest).ConfigureAwait(false);
		}

		using (response)
		{
			var body = response.Content == null
				? string.Empty
				: await response.Content.ReadAsStringAsync().ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				var serviceError = GetErrorMessage(response, body);
				if (IsPossiblyIntermediaryStatus(response.StatusCode))
				{
					// (I1) A 5xx/408 can be generated by an intermediary (LB/WAF/proxy) AFTER the service
					// received the POST — epistemically the same as a transport timeout (see the financial
					// path). For the STATE-CHANGING SettlementCutoverAsync, Failed ("blind retry will fail
					// again") would invite a second settlement window over an executed cutover, so map to
					// Unknown; the read-only ops (status/logon/inquiry/journal) get the same honest Unknown.
					SafeLog(LogLevel.Error, null, "SmartConnect answered the {TransactionType} POST with HTTP {StatusCode}, which an intermediary can generate after the service received the request — outcome UNKNOWN; manual reconciliation required. {ServiceError}", transactionType, (int)response.StatusCode, serviceError);
					return new SmartConnectTransactionResult
					{
						Status = SmartConnectTransactionStatus.Unknown,
						FailureCause = SmartConnectFailureCause.TransportUnknown,
						ErrorMessage = serviceError
					};
				}

				SafeLog(LogLevel.Error, null, "SmartConnect rejected the {TransactionType} request: {ServiceError}", transactionType, serviceError);
				return new SmartConnectTransactionResult
				{
					Status = SmartConnectTransactionStatus.Failed,
					FailureCause = SmartConnectFailureCause.ServiceError,
					ErrorMessage = serviceError
				};
			}

			InitialTransactionResponse initial;
			try
			{
				initial = TransactionResponseParser.ParseInitialResponse(body);
			}
			catch (JsonException)
			{
				initial = new InitialTransactionResponse();
			}

			if (string.IsNullOrEmpty(initial.PollingUrl))
			{
				SafeLog(LogLevel.Error, null, "SmartConnect accepted the {TransactionType} request but returned no polling URL — the result cannot be retrieved.", transactionType);
				return new SmartConnectTransactionResult
				{
					Status = SmartConnectTransactionStatus.Unknown,
					FailureCause = SmartConnectFailureCause.TransportUnknown,
					TransactionId = initial.TransactionId
				};
			}

			// Null ref = no state-store interaction at any point, including terminal state.
			return await PollForResultAsync(initial.PollingUrl!, initial.TransactionId, null, progress).ConfigureAwait(false);
		}
	}

	/// <summary>The HttpClient in use (owned or injected). Internal seam so tests can observe disposal.</summary>
	internal HttpClient HttpClientInternal => _httpClient;

	// The operation methods share the non-financial core but return the OPERATION result shape, not the
	// financial transaction shape — a non-financial response has no approve/decline and no money fields.
	// (Journal.GetTransResult is the exception: it reports a prior TRANSACTION, so it keeps the financial
	// result and does not route through here.)
	private async Task<SmartConnectOperationResult> ExecuteOperationAsync(SmartConnectRegistration registration, string transactionType, IProgress<SmartConnectPollingStatus>? progress)
	{
		var inner = await ExecuteNonFinancialCoreAsync(registration, transactionType, progress).ConfigureAwait(false);
		return ToOperationResult(inner);
	}

	// Maps the internal (financial-shaped) result of a non-financial operation onto the operation result. The
	// financial outcome mapper cannot read a non-financial body — there is no TransactionResult code — so
	// success is taken from the envelope's Result == "OK", the signal the terminal actually uses (e.g.
	// Terminal.GetStatus returns Result=OK / Status=READY). Unknown (poll timeout / invalid polling URL /
	// result never retrieved) and a service rejection carry through unchanged. A COMPLETED body with no Result
	// field stays Unknown — we will not assert success we cannot see.
	private static SmartConnectOperationResult ToOperationResult(SmartConnectTransactionResult inner)
	{
		SmartConnectOperationStatus status;
		string? error = null;

		if (inner.Status == SmartConnectTransactionStatus.Unknown)
		{
			status = SmartConnectOperationStatus.Unknown;
		}
		else if (inner.FailureCause == SmartConnectFailureCause.ServiceError)
		{
			status = SmartConnectOperationStatus.Failed;
			// Prefer the service's own message; the generic text stands in only if none was extractable.
			error = string.IsNullOrEmpty(inner.ErrorMessage)
				? "The operation was rejected by the service."
				: inner.ErrorMessage;
		}
		else
		{
			string? result = null;
			inner.RawData?.TryGetValue("Result", out result);
			if (string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase))
			{
				status = SmartConnectOperationStatus.Succeeded;
			}
			else if (!string.IsNullOrEmpty(result))
			{
				status = SmartConnectOperationStatus.Failed;
				error = result;
			}
			else
			{
				status = SmartConnectOperationStatus.Unknown;
			}
		}

		return new SmartConnectOperationResult
		{
			Status = status,
			ErrorMessage = error,
			TransactionId = inner.TransactionId,
			ResponseTimestamp = inner.ResponseTimestamp,
			RawData = inner.RawData
		};
	}

	/// <summary>The clock used for the poll deadline. Internal seam so tests run on virtual time.</summary>
	internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

	/// <summary>The inter-poll delay. Internal seam so tests advance a fake clock instead of sleeping.</summary>
	internal Func<TimeSpan, Task> PollDelay { get; set; } = interval => Task.Delay(interval);

	// A null clientTransactionRef means "no state-store interaction at all" — the diagnostic journal-query
	// mode, where the caller owns any original transaction's sentinel.
	private async Task<SmartConnectTransactionResult> PollForResultAsync(string pollingUrl, string? transactionId, string? clientTransactionRef, IProgress<SmartConnectPollingStatus>? progress)
	{
		var startedAt = Clock();
		var deadline = startedAt + _maxPollDuration;

		// 429 backoff state: the exponential doubles from the configured interval per consecutive 429
		// (capped at BackoffCap) and resets on any successful poll; a Retry-After header overrides the
		// exponential for that wait without disturbing it.
		var nextDelay = _pollInterval;
		var backoffInterval = _pollInterval;
		var attempt = 0;

		while (true)
		{
			if (_disposed || Clock() >= deadline)
			{
				// Poll exhaustion/abandonment is the "live caller" Unknown: the caller gets the result and
				// owns reconciliation, so the sentinel closes as Unknown (distinct from POST-phase
				// TransportUnknown, where no response ever arrived). Dispose is a deliberate host action
				// (shutdown) — Warning; only genuine exhaustion is an Error.
				SafeLog(
					_disposed ? LogLevel.Warning : LogLevel.Error,
					null,
					"Polling ended without a terminal answer for {ClientTransactionRef} after {ElapsedSeconds}s ({Reason}) — outcome UNKNOWN; reconcile before retrying.",
					clientTransactionRef ?? "(journal query)",
					(int)(Clock() - startedAt).TotalSeconds,
					_disposed ? "client disposed" : "MaxPollDuration exceeded");
				if (clientTransactionRef != null)
				{
					await CloseSentinelQuietlyAsync(clientTransactionRef, SmartConnectTransactionStatus.Unknown).ConfigureAwait(false);
				}

				return new SmartConnectTransactionResult
				{
					Status = SmartConnectTransactionStatus.Unknown,
					TransactionId = transactionId
				};
			}

			await PollDelay(nextDelay).ConfigureAwait(false);
			nextDelay = _pollInterval;
			attempt++;
			SafeLog(LogLevel.Debug, null, "Poll attempt {Attempt} (transactionId {TransactionId}).", attempt, transactionId);

			try
			{
				using (var pollRequest = new HttpRequestMessage(HttpMethod.Get, pollingUrl))
				using (var response = await SendAsync(pollRequest).ConfigureAwait(false))
				{
					if ((int)response.StatusCode == 429)
					{
						backoffInterval = Min(TimeSpan.FromTicks(backoffInterval.Ticks * 2), _backoffCap);
						nextDelay = GetRetryAfterDelay(response) ?? backoffInterval;
						SafeLog(LogLevel.Debug, null, "Rate-limited (HTTP 429) for {ClientTransactionRef} — backing off; next poll in {NextDelaySeconds}s.", clientTransactionRef, (int)nextDelay.TotalSeconds);
						progress?.Report(new SmartConnectPollingStatus { State = SmartConnectPollingState.BackingOff });
						continue;
					}

					if (IsPollingUrlVerdict(response.StatusCode))
					{
						// (F8) An ANSWER saying the URL itself is no good — spinning NetworkError to
						// timeout would waste MaxPollDuration and mislead the operator. The sentinel stays
						// pending: the outcome is unresolved and only manual reconciliation can resolve it.
						SafeLog(LogLevel.Error, null, "SmartConnect answered the poll with HTTP {StatusCode} for {ClientTransactionRef} — the polling URL is invalid or expired. Outcome UNKNOWN; manual reconciliation required.", (int)response.StatusCode, clientTransactionRef);
						return new SmartConnectTransactionResult
						{
							Status = SmartConnectTransactionStatus.Unknown,
							FailureCause = SmartConnectFailureCause.PollingUrlInvalid,
							TransactionId = transactionId
						};
					}

					if (!response.IsSuccessStatusCode)
					{
						// 5xx is transient — keep polling within MaxPollDuration (429 is handled above).
						progress?.Report(new SmartConnectPollingStatus { State = SmartConnectPollingState.NetworkError });
						continue;
					}

					var body = response.Content == null
						? string.Empty
						: await response.Content.ReadAsStringAsync().ConfigureAwait(false);

					PollResult poll;
					try
					{
						poll = TransactionResponseParser.ParsePollResponse(body);
					}
					catch (JsonException)
					{
						// A garbled poll body (proxy blip) is transient — the next poll re-asks.
						progress?.Report(new SmartConnectPollingStatus { State = SmartConnectPollingState.NetworkError });
						continue;
					}

					// A successful poll resets the 429 backoff to the configured interval.
					backoffInterval = _pollInterval;

					if (poll.Progress == PollProgress.Completed)
					{
						var result = poll.Result!;
						SafeLog(LogLevel.Information, null, "Terminal state {Status} for {ClientTransactionRef} (transactionId {TransactionId}).", result.Status, clientTransactionRef ?? "(journal query)", result.TransactionId);
						if (clientTransactionRef != null)
						{
							await CloseSentinelQuietlyAsync(clientTransactionRef, result.Status).ConfigureAwait(false);
						}

						return result;
					}

					progress?.Report(new SmartConnectPollingStatus
					{
						State = poll.Progress == PollProgress.Delayed
							? SmartConnectPollingState.Delayed
							: SmartConnectPollingState.Polling
					});
				}
			}
			catch (SmartConnectTransportException ex)
			{
				// (F11) Transient transport — report and retry; the PollDelay at the top of the loop still
				// runs, so there is no tight retry-storm. NEVER treat "couldn't reach the server" as "URL
				// expired" — a live transaction may still be fine.
				SafeLog(LogLevel.Warning, ex, "Network error during poll for {ClientTransactionRef} — retrying on the next interval.", clientTransactionRef);
				progress?.Report(new SmartConnectPollingStatus { State = SmartConnectPollingState.NetworkError, Error = ex });
			}
		}
	}

	private static bool IsPollingUrlVerdict(HttpStatusCode statusCode)
	{
		return statusCode == HttpStatusCode.Unauthorized
			|| statusCode == HttpStatusCode.Forbidden
			|| statusCode == HttpStatusCode.NotFound
			|| statusCode == HttpStatusCode.Gone;
	}

	// 5xx and 408 are not verdicts that the transaction was not processed: intermediaries (load balancers,
	// WAFs, proxies) commonly generate them after the origin received the request. 408 nominally means the
	// request was never fully received, but intermediaries are unreliable about status discipline, and
	// Decision 9's rule is that ambiguity resolves toward Unknown (a false Unknown costs one manual
	// reconciliation; a false Failed can cost a double charge).
	private static bool IsPossiblyIntermediaryStatus(HttpStatusCode statusCode)
	{
		return statusCode == HttpStatusCode.RequestTimeout || (int)statusCode >= 500;
	}

	// Honours Retry-After (delta-seconds or HTTP-date) clamped into [MinimumPollInterval, BackoffCap]: a
	// past/zero value must never cause an immediate re-poll (the violation 429 is telling us off for), and
	// a huge vendor value never out-waits our patience ceiling — MaxPollDuration stays the true bound.
	private TimeSpan? GetRetryAfterDelay(HttpResponseMessage response)
	{
		var retryAfter = response.Headers.RetryAfter;
		if (retryAfter == null)
		{
			return null;
		}

		TimeSpan value;
		if (retryAfter.Delta != null)
		{
			value = retryAfter.Delta.Value;
		}
		else if (retryAfter.Date != null)
		{
			value = retryAfter.Date.Value - Clock();
		}
		else
		{
			return null;
		}

		if (value < SmartConnectClientConfiguration.MinimumPollInterval)
		{
			return SmartConnectClientConfiguration.MinimumPollInterval;
		}

		return value > _backoffCap ? _backoffCap : value;
	}

	private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

	private async Task<HttpResponseMessage> PostTransactionAsync(SmartConnectTransactionRequest request)
	{
		var fields = new List<KeyValuePair<string, string?>>(8)
		{
			new KeyValuePair<string, string?>("POSRegisterID", request.POSRegisterID),
			new KeyValuePair<string, string?>("POSBusinessName", request.POSBusinessName),
			new KeyValuePair<string, string?>("POSVendorName", request.POSVendorName),
			new KeyValuePair<string, string?>("TransactionMode", "ASYNC"),
			new KeyValuePair<string, string?>("TransactionType", request.TransactionType),
			new KeyValuePair<string, string?>("AmountTotal", request.AmountTotal.ToCents().ToString(System.Globalization.CultureInfo.InvariantCulture))
		};

		if (string.Equals(request.TransactionType, SmartConnectTransactionType.CardPurchasePlusCash, StringComparison.Ordinal))
		{
			fields.Add(new KeyValuePair<string, string?>("AmountCash", request.AmountCash.ToCents().ToString(System.Globalization.CultureInfo.InvariantCulture)));
		}

		if (!string.IsNullOrEmpty(request.TransactionReference))
		{
			fields.Add(new KeyValuePair<string, string?>("TransactionReference", request.TransactionReference));
		}

		if (request.SaleData != null)
		{
			fields.Add(new KeyValuePair<string, string?>("SaleData", Internal.SaleDataSerializer.Serialize(request.SaleData)));
		}

		using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/Transaction"))
		{
			httpRequest.Content = new StringContent(FormUrlEncoder.Encode(fields), Encoding.UTF8, "application/x-www-form-urlencoded");
			return await SendAsync(httpRequest).ConfigureAwait(false);
		}
	}

	private void ValidateTransactionRequest(SmartConnectTransactionRequest request)
	{
		if (request == null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		RequireField(request.ClientTransactionRef, nameof(request.ClientTransactionRef));
		RequireField(request.POSRegisterID, nameof(request.POSRegisterID));
		RequireField(request.POSBusinessName, nameof(request.POSBusinessName));
		RequireField(request.POSVendorName, nameof(request.POSVendorName));
		RequireField(request.TransactionType, nameof(request.TransactionType));

		if (request.AmountTotal.ToCents() <= 0)
		{
			throw new ArgumentException("AmountTotal must be positive (refunds are positive amounts with TransactionType Card.Refund).", "request");
		}

		if (string.Equals(request.TransactionType, SmartConnectTransactionType.CardPurchasePlusCash, StringComparison.Ordinal))
		{
			// The docs state AmountCash is the cash-out COMPONENT of AmountTotal, so it must be positive and
			// not exceed the total — caught locally rather than relying on a vendor rejection.
			var cashCents = request.AmountCash.ToCents();
			if (cashCents <= 0)
			{
				throw new ArgumentException("AmountCash must be positive for Card.PurchasePlusCash.", "request");
			}

			if (cashCents > request.AmountTotal.ToCents())
			{
				throw new ArgumentException("AmountCash must not exceed AmountTotal (the cash-out is a component of AmountTotal).", "request");
			}
		}
		else if (request.AmountCash.ToCents() != 0)
		{
			// AmountCash is only carried on Card.PurchasePlusCash. A non-zero value on any other type would be
			// silently dropped from the wire — reject it so a mis-set cash-out is a loud caller error, not a
			// customer who expected cash-out and didn't get it.
			throw new ArgumentException("AmountCash is only valid for Card.PurchasePlusCash; leave it unset (zero) for other transaction types.", "request");
		}

		if (request.SaleData != null)
		{
			// Serialise SaleData up front (before the sentinel) so an unserialisable caller type fails here —
			// never as a dangling pending sentinel or a half-sent transaction. Bad caller input throws
			// (Decision 9); operational conditions do not. The V1 types are always serialisable.
			try
			{
				Internal.SaleDataSerializer.Serialize(request.SaleData);
			}
			catch (Exception ex) when (ex is JsonException || ex is NotSupportedException)
			{
				throw new ArgumentException("The SaleData could not be serialised (e.g. a derived type with a reference cycle or a non-serialisable property).", nameof(request), ex);
			}
		}

		ThrowIfDisposed();
	}

	// (R3) Closes the sentinel after an outcome the library already holds; a persistence failure must
	// never mask that outcome — log and continue.
	private async Task CloseSentinelQuietlyAsync(string clientTransactionRef, SmartConnectTransactionStatus status)
	{
		try
		{
			await _stateStore.UpdateCompletedAsync(clientTransactionRef, status).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			SafeLog(LogLevel.Warning, null, "Failed to persist terminal state ({ExceptionType}) for {ClientTransactionRef} — the outcome was still returned; the sentinel stays pending for recovery to investigate.", ex.GetType().Name, clientTransactionRef);
		}
	}

	// (G10) Diagnostics must be strictly weaker than the path they diagnose — a logger failure never
	// fails the operation being logged. Message templates with args preserve structured logging (H3);
	// (G7) the polling URL must never appear as a template argument either.
	private void SafeLog(LogLevel level, Exception? exception, string messageTemplate, params object?[] args)
	{
		var logger = _logger;
		if (logger == null)
		{
			return;
		}

		try
		{
			logger.Log(level, exception, messageTemplate, args);
		}
		catch
		{
			// Suppressed by design.
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
	// through here so the optional auth seam can never be bypassed and no raw BCL transport exception can
	// leak from a future call site (ADR Decision 9).
	private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
	{
		// Consumer code — deliberately OUTSIDE the transport wrap so a bug in their callback isn't
		// disguised as a transport failure.
		var authorize = _authorizeRequestAsync;
		if (authorize != null)
		{
			await authorize(request).ConfigureAwait(false);
		}

		try
		{
			// The default HttpCompletionOption.ResponseContentRead buffers the body during this call, so
			// mid-body network failures surface inside this wrap; later ReadAsStringAsync calls read from
			// the buffer and cannot fail on network. Do not change the completion option without
			// revisiting that assumption.
			return await _httpClient.SendAsync(request).ConfigureAwait(false);
		}
		catch (ObjectDisposedException ex)
		{
			// Disposed mid-send (host shutdown racing an in-flight request): the request may already have
			// reached the service, so the outcome is Unknown, never NotSent. Surfaced as the typed transport
			// exception so the poll loop / POST handler resolve it to Unknown rather than letting it escape
			// ProcessTransactionAsync raw (which would break the never-throw-for-runtime-conditions contract).
			throw new SmartConnectTransportException(SmartConnectRequestDelivery.Unknown, ex);
		}
		catch (Exception ex) when (TransportFailureClassifier.IsTransportFailure(ex))
		{
			throw new SmartConnectTransportException(TransportFailureClassifier.Classify(ex), ex);
		}
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
		// A configured name like "Demo Store" is not a valid HTTP token — fall back to the library
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
