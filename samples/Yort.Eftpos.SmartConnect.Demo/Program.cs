using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Yort.Eftpos.SmartConnect;
using SaleDataV1 = Yort.Eftpos.SmartConnect.SaleData.V1;

namespace Yort.Eftpos.SmartConnect.Demo;

/// <summary>
/// Interactive demo and dev-environment probe harness for Yort.Eftpos.SmartConnect.
/// Illustrative of correct library usage — NOT a production POS (no receipts printing, no tender
/// integration, no offline handling). Runs against a REAL dev pinpad: financial actions send real
/// transactions to the connected terminal.
/// </summary>
internal static class Program
{
	private static Settings _settings = new Settings();
	private static string _settingsPath = string.Empty;
	private static string _transcriptPath = string.Empty;

	private static async Task Main()
	{
		Console.OutputEncoding = System.Text.Encoding.UTF8;
		Console.WriteLine("Yort.Eftpos.SmartConnect demo / probe harness (" + RuntimeLabel + ")");
		Console.WriteLine("Illustrative sample — not a production POS. Financial actions hit the connected pinpad for real.");
		Console.WriteLine();

		var settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartConnectDemo");
		Directory.CreateDirectory(settingsDirectory);
		_settingsPath = Path.Combine(settingsDirectory, "settings.json");
		LoadOrPromptSettings(settingsDirectory);
		_transcriptPath = Path.Combine(_settings.StateDirectory!, "demo-transcript.log");

		// Ctrl+C mid-transaction: the pinpad transaction continues regardless (there is no cancel API),
		// and the pending sentinel survives on disk — tell the operator how to pick it back up.
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			Console.WriteLine();
			Console.WriteLine("Interrupted. A transaction may still be in progress on the pinpad; pending state is retained —");
			Console.WriteLine("use 'List pending / resume' on the next run to recover it.");
			Transcript("Ctrl+C — exited with possible transaction in progress.");
			Environment.Exit(0);
		};

		var logger = new ConsoleLogger();
		var store = new FileBasedTransactionStateStore(_settings.StateDirectory!, logger);
		var configuration = new SmartConnectClientConfiguration
		{
			BaseUrl = new Uri(_settings.BaseUrl!),
			StateStore = store,
			Logger = logger,
			// Interactive default: a human fumbling a dev card is slower than an unattended tender.
			MaxPollDuration = TimeSpan.FromMinutes(_settings.MaxPollMinutes)
		};

		using (var client = new SmartConnectClient(configuration))
		{
			await RunMenuAsync(client, store).ConfigureAwait(false);
		}
	}

	private static async Task RunMenuAsync(SmartConnectClient client, FileBasedTransactionStateStore store)
	{
		while (true)
		{
			Console.WriteLine();
			Console.WriteLine("== " + _settings.BusinessName + " / " + _settings.RegisterName + " @ " + _settings.BaseUrl + " ==");
			Console.WriteLine(" 1) Pair with terminal");
			Console.WriteLine(" 2) Purchase");
			Console.WriteLine(" 3) Refund");
			Console.WriteLine(" 4) Purchase + cash-out   (F9 probe: amount-relationship verdict)");
			Console.WriteLine(" 5) List pending / resume (PollingUrl lifetime / F8 probe)");
			Console.WriteLine(" 6) Journal.GetTransResult — last transaction (Decision 10)");
			Console.WriteLine(" 7) Re-pair with same register id (idempotency probe)");
			Console.WriteLine(" 8) Transport-shape probe (no pinpad needed; run on BOTH TFMs — R4)");
			Console.WriteLine(" 9) Terminal status (is the cloud able to reach the pinpad?)");
			Console.WriteLine("10) Settlement inquiry (read-only)");
			Console.WriteLine("11) Settlement CUTOVER (state-changing!)");
			Console.WriteLine("12) Acquirer logon");
			Console.WriteLine("13) Purchase + sample SaleData (illustrative)");
			Console.WriteLine(" 0) Quit");
			Console.Write("> ");

			var choice = Console.ReadLine()?.Trim();
			try
			{
				switch (choice)
				{
					case "1": await PairAsync(client).ConfigureAwait(false); break;
					case "2": await TransactAsync(client, SmartConnectTransactionType.CardPurchase).ConfigureAwait(false); break;
					case "3": await TransactAsync(client, SmartConnectTransactionType.CardRefund).ConfigureAwait(false); break;
					case "4": await TransactAsync(client, SmartConnectTransactionType.CardPurchasePlusCash).ConfigureAwait(false); break;
					case "5": await ListAndResumeAsync(client, store).ConfigureAwait(false); break;
					case "6": await JournalQueryAsync(client).ConfigureAwait(false); break;
					case "7": await PairAsync(client).ConfigureAwait(false); break;
					case "8": await TransportShapeProbeAsync().ConfigureAwait(false); break;
					case "9": RenderNonFinancial("Terminal.GetStatus", await client.GetTerminalStatusAsync(Registration(), new ConsoleProgress()).ConfigureAwait(false)); break;
					case "10": RenderResult(await client.SettlementInquiryAsync(Registration(), new ConsoleProgress()).ConfigureAwait(false), "(Acquirer.Settlement.Inquiry)"); break;
					case "11": await CutoverAsync(client).ConfigureAwait(false); break;
					case "12": RenderResult(await client.LogonAsync(Registration(), new ConsoleProgress()).ConfigureAwait(false), "(Acquirer.Logon)"); break;
					case "13": await PurchaseWithSaleDataAsync(client).ConfigureAwait(false); break;
					case "0": return;
				}
			}
			catch (SmartConnectTransportException ex)
			{
				// One-shot operations (pairing, journal query) throw the typed transport exception;
				// Delivery says whether a retry is safe. Transactions never throw — they return results.
				Console.WriteLine($"Transport failure: Delivery={ex.Delivery} ({ex.InnerException?.GetType().Name})");
				Console.WriteLine(ex.Delivery == SmartConnectRequestDelivery.NotSent
					? "Nothing reached SmartConnect — safe to retry."
					: "The request MAY have been processed — do not blind-retry financial operations.");
				Transcript($"TransportException Delivery={ex.Delivery} Inner={ex.InnerException?.GetType().Name}");
			}
		}
	}

	private static async Task PairAsync(SmartConnectClient client)
	{
		Console.Write("Pairing code shown on the terminal: ");
		var code = Console.ReadLine()?.Trim();
		if (string.IsNullOrWhiteSpace(code))
		{
			return;
		}

		var result = await client.PairAsync(code!, new SmartConnectPairingRequest
		{
			POSRegisterID = _settings.RegisterId!,
			POSRegisterName = _settings.RegisterName,
			POSBusinessName = _settings.BusinessName!,
			POSVendorName = _settings.VendorName!
		}).ConfigureAwait(false);

		Console.WriteLine(result.Success ? "Paired." : "Pairing failed: " + result.ErrorMessage);
		Transcript($"Pair code={code} registerId={_settings.RegisterId} success={result.Success} error={result.ErrorMessage}");
	}

	private static async Task TransactAsync(SmartConnectClient client, string transactionType)
	{
		var amountTotal = PromptAmount("Total amount (e.g. 1.50): ");
		if (amountTotal == null)
		{
			return;
		}

		Money amountCash = default;
		if (transactionType == SmartConnectTransactionType.CardPurchasePlusCash)
		{
			var cash = PromptAmount("Cash-out amount: ");
			if (cash == null)
			{
				return;
			}

			amountCash = cash.Value;
			Console.WriteLine("F9 NOTE: the docs say AmountTotal INCLUDES the cash-out portion (\"cash portion of the");
			Console.WriteLine("AmountTotal\") — this probe CONFIRMS docs-vs-reality: values are sent exactly as typed;");
			Console.WriteLine("compare the completed response's amount fields and what the terminal actually charged.");
		}

		// (H8) Echo in unambiguous units and confirm — this is a REAL transaction on the connected pinpad.
		Console.WriteLine($"{transactionType}: AmountTotal = {amountTotal.Value.ToDecimal():0.00} ({amountTotal.Value.ToCents()} cents)"
			+ (transactionType == SmartConnectTransactionType.CardPurchasePlusCash ? $", AmountCash = {amountCash.ToDecimal():0.00} ({amountCash.ToCents()} cents)" : string.Empty));
		Console.Write("This sends a REAL transaction to the connected pinpad. Proceed? (y/N): ");
		if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("Cancelled before send.");
			return;
		}

		var clientTransactionRef = "demo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
		Transcript($"SEND {transactionType} ref={clientTransactionRef} total={amountTotal.Value.ToCents()}c cash={amountCash.ToCents()}c");

		var result = await client.ProcessTransactionAsync(new SmartConnectTransactionRequest
		{
			TransactionType = transactionType,
			AmountTotal = amountTotal.Value,
			AmountCash = amountCash,
			POSRegisterID = _settings.RegisterId!,
			POSBusinessName = _settings.BusinessName!,
			POSVendorName = _settings.VendorName!,
			ClientTransactionRef = clientTransactionRef
		}, new ConsoleProgress()).ConfigureAwait(false);

		RenderResult(result, clientTransactionRef);
	}

	// Illustrative: how an integrator builds a typed SaleData (line items + a category) and attaches it to a
	// purchase. SaleData is descriptive metadata only — request-only, not echoed back, not a recovery aid. The
	// amount fields are vendor strings of unspecified encoding (the library does not interpret them); here they
	// mirror the AmountTotal as a plain decimal string.
	private static async Task PurchaseWithSaleDataAsync(SmartConnectClient client)
	{
		var amountTotal = PromptAmount("Total amount (e.g. 5.00): ");
		if (amountTotal == null)
		{
			return;
		}

		Console.WriteLine($"Card.Purchase + sample SaleData: AmountTotal = {amountTotal.Value.ToDecimal():0.00} ({amountTotal.Value.ToCents()} cents)");
		Console.Write("This sends a REAL transaction to the connected pinpad. Proceed? (y/N): ");
		if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("Cancelled before send.");
			return;
		}

		var totalText = amountTotal.Value.ToDecimal().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
		var saleData = new SaleDataV1.SaleData
		{
			SaleId = "demo-sale-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
			TotalAmount = totalText,
			TotalTax = "0.00",
			LineItems = new List<SaleDataV1.LineItem>
			{
				new SaleDataV1.LineItem
				{
					ProductName = "Demo item",
					Quantity = "1",
					UnitPrice = totalText,
					UnitTax = "0.00",
					TotalPrice = totalText,
					TotalTax = "0.00",
					Categories = new List<SaleDataV1.Category>
					{
						new SaleDataV1.Category { CategoryName = "Demo" }
					}
				}
			}
		};

		var clientTransactionRef = "demo-saledata-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
		Transcript($"SEND Card.Purchase+SaleData ref={clientTransactionRef} total={amountTotal.Value.ToCents()}c");

		var result = await client.ProcessTransactionAsync(new SmartConnectTransactionRequest
		{
			TransactionType = SmartConnectTransactionType.CardPurchase,
			AmountTotal = amountTotal.Value,
			POSRegisterID = _settings.RegisterId!,
			POSBusinessName = _settings.BusinessName!,
			POSVendorName = _settings.VendorName!,
			ClientTransactionRef = clientTransactionRef,
			SaleData = saleData
		}, new ConsoleProgress()).ConfigureAwait(false);

		RenderResult(result, clientTransactionRef);
	}

	private static async Task ListAndResumeAsync(SmartConnectClient client, FileBasedTransactionStateStore store)
	{
		var pending = new List<PendingTransaction>(await store.GetPendingTransactionsAsync().ConfigureAwait(false));
		if (pending.Count == 0)
		{
			Console.WriteLine("No pending transactions.");
			return;
		}

		for (var i = 0; i < pending.Count; i++)
		{
			// (H6) The polling URL carries a bearer token — display it REDACTED, the way integrators should.
			Console.WriteLine($" {i + 1}) {pending[i].ClientTransactionRef}  created {pending[i].CreatedAt:u}  url {RedactToken(pending[i].PollingUrl)}");
		}

		Console.Write("Resume which (number, blank to cancel)? ");
		if (!int.TryParse(Console.ReadLine()?.Trim(), out var index) || index < 1 || index > pending.Count)
		{
			return;
		}

		var chosen = pending[index - 1];
		if (string.IsNullOrEmpty(chosen.PollingUrl))
		{
			Console.WriteLine("No polling URL persisted for this record — use the Journal probe (Layer 2) and reconcile manually.");
			return;
		}

		Transcript($"RESUME ref={chosen.ClientTransactionRef} created={chosen.CreatedAt:u}");
		var result = await client.ResumePollingAsync(chosen.PollingUrl!, chosen.ClientTransactionRef, new ConsoleProgress()).ConfigureAwait(false);
		RenderResult(result, chosen.ClientTransactionRef);

		if (result.FailureCause == SmartConnectFailureCause.PollingUrlInvalid)
		{
			Console.WriteLine("F8 verdict: the persisted polling URL was rejected — record how old it was (created above) for the lifetime question.");
		}
	}

	// Exercises the library's Layer-2 recovery query (Journal.GetTransResult): fetches the terminal's most-
	// recent transaction. DEVICE-scoped, not register-scoped (Decision 10) — on a terminal shared by several
	// registers this can return another register's transaction, so a real recovery must confirm the reported
	// transaction matches its sentinel (ReferenceId / AmountTotal) before adopting it.
	private static async Task JournalQueryAsync(SmartConnectClient client)
	{
		var result = await client.GetLastTransactionResultAsync(Registration(), new ConsoleProgress()).ConfigureAwait(false);
		RenderResult(result, "(Journal.GetTransResult — device's last transaction)");
	}

	// (R4) No pinpad needed. Run this on BOTH builds (net48 and net8.0) and compare: the same failures
	// must classify to the same Delivery from different BCL exception shapes.
	private static async Task TransportShapeProbeAsync()
	{
		var cases = new (string Label, string BaseUrl, string Expected)[]
		{
			("DNS failure", "https://no-such-host.invalid/POS", "NotSent"),
			("Connection refused", "https://127.0.0.1:1/POS", "NotSent"),
			("Connect timeout", "https://10.255.255.1/POS", "Unknown (times out)")
		};

		foreach (var probeCase in cases)
		{
			Console.WriteLine($"{probeCase.Label} (expect {probeCase.Expected}) ...");
			var configuration = new SmartConnectClientConfiguration
			{
				BaseUrl = new Uri(probeCase.BaseUrl),
				StateStore = new FileBasedTransactionStateStore(Path.Combine(_settings.StateDirectory!, "probe-scratch"))
			};

			using (var probeClient = new SmartConnectClient(configuration))
			{
				try
				{
					await probeClient.PairAsync("00000000", new SmartConnectPairingRequest
					{
						POSRegisterID = _settings.RegisterId!,
						POSBusinessName = "probe",
						POSVendorName = "probe"
					}).ConfigureAwait(false);
					Console.WriteLine("  unexpectedly succeeded?!");
				}
				catch (SmartConnectTransportException ex)
				{
					Console.WriteLine($"  Delivery={ex.Delivery}  inner={ex.InnerException?.GetType().Name}");
					Transcript($"R4 [{RuntimeLabel}] {probeCase.Label}: Delivery={ex.Delivery} inner={ex.InnerException?.GetType().Name} (expected {probeCase.Expected})");
				}
			}
		}

		Console.WriteLine($"Recorded with TFM '{RuntimeLabel}' — run the other build and diff the transcript lines.");
	}

	// (H7) The outcome display is what integrators copy — especially what happens on Unknown.
	private static void RenderResult(SmartConnectTransactionResult result, string clientTransactionRef)
	{
		Console.WriteLine();
		Console.WriteLine($"Status: {result.Status}   FailureCause: {result.FailureCause}");
		Console.WriteLine($"Ref: {clientTransactionRef}   TransactionId: {result.TransactionId}");
		if (!string.IsNullOrEmpty(result.ReferenceId))
		{
			// Journal.GetTransResult path: the recovered transaction's id, distinct from the query's TransactionId.
			Console.WriteLine($"ReferenceId (reported txn): {result.ReferenceId}");
		}

		if (result.Status == SmartConnectTransactionStatus.Accepted || result.Status == SmartConnectTransactionStatus.Declined)
		{
			Console.WriteLine($"AuthId: {result.AuthId}   Card: {result.CardType} {result.CardPan}   Total: {result.AmountTotal.ToDecimal():0.00}");
		}

		if (!string.IsNullOrEmpty(result.Receipt))
		{
			Console.WriteLine("--- receipt (fixed-width) ---");
			Console.WriteLine(result.Receipt);
			Console.WriteLine("-----------------------------");
		}

		if (result.Status != SmartConnectTransactionStatus.Accepted && result.Status != SmartConnectTransactionStatus.Declined)
		{
			// Abnormal outcome — the raw fields are the diagnosis (e.g. DeviceOffline = CANCELLED/FAILED-INTERFACE:
			// the terminal must be at its idle screen BEFORE the POS sends; safe to retry once it is).
			RenderRawData(result);
		}

		if (result.Status == SmartConnectTransactionStatus.DeviceOffline)
		{
			Console.WriteLine("DEVICE OFFLINE: the cloud could not reach the pinpad. Check it is at its idle SmartConnect");
			Console.WriteLine("screen (not in a menu) and retry — no money moved; immediate retry is safe.");
		}

		if (result.Status == SmartConnectTransactionStatus.Unknown)
		{
			Console.WriteLine("OUTCOME UNKNOWN — the customer may or may not have been charged.");
			Console.WriteLine("Do NOT retry. Verify via Journal.GetTransResult (menu 6) or the acquirer before re-tendering.");
		}

		Transcript($"RESULT ref={clientTransactionRef} status={result.Status} cause={result.FailureCause} txnId={result.TransactionId} auth={result.AuthId} total={result.AmountTotal.ToCents()}c surcharge={result.AmountSurcharge.ToCents()}c tip={result.AmountTip.ToCents()}c");
	}

	private static SmartConnectRegistration Registration()
	{
		return new SmartConnectRegistration
		{
			POSRegisterID = _settings.RegisterId!,
			POSBusinessName = _settings.BusinessName!,
			POSVendorName = _settings.VendorName!
		};
	}

	private static async Task CutoverAsync(SmartConnectClient client)
	{
		// (J1) Cutover closes the acquirer settlement window — state-changing, NOT a read-only query.
		Console.Write("Settlement CUTOVER is a state-changing acquirer operation. Proceed? (y/N): ");
		if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("Cancelled.");
			return;
		}

		var result = await client.SettlementCutoverAsync(Registration(), new ConsoleProgress()).ConfigureAwait(false);
		RenderResult(result, "(Acquirer.Settlement.Cutover)");

		if (result.Status == SmartConnectTransactionStatus.Unknown)
		{
			Console.WriteLine("CUTOVER OUTCOME UNKNOWN — it MAY have executed. Run Settlement inquiry (menu 10) to");
			Console.WriteLine("verify before re-issuing; a repeated cutover double-cuts the settlement window.");
		}
	}

	// (J3) Renders a SmartConnectOperationResult (now only Terminal.GetStatus — the acquirer ops return a
	// transaction result and render via RenderResult). Status is Succeeded/Failed/Unknown (from Result=="OK");
	// operation-specific fields aren't typed, so the raw fields are dumped verbatim to confirm an op's shape.
	private static void RenderNonFinancial(string operation, SmartConnectOperationResult result)
	{
		Console.WriteLine();
		Console.WriteLine($"{operation}: Status = {result.Status}"
			+ (string.IsNullOrEmpty(result.ErrorMessage) ? string.Empty : $"   Error: {result.ErrorMessage}"));
		Console.WriteLine($"TransactionId: {result.TransactionId}");
		RenderRawData(result);
		Transcript($"NONFIN {operation} status={result.Status} error={result.ErrorMessage} txnId={result.TransactionId} raw={RedactToken(RawDataLine(result))}");
		Console.WriteLine("Record the response-shape verdict (raw fields above) in the ADR open-questions table.");
	}

	private static void RenderRawData(SmartConnectResult result)
	{
		if (result.RawData == null || result.RawData.Count == 0)
		{
			Console.WriteLine("(no raw response data)");
			return;
		}

		Console.WriteLine("raw response fields:");
		foreach (var pair in result.RawData)
		{
			Console.WriteLine($"  {pair.Key} = {RedactToken(pair.Value)}");
		}
	}

	private static string RawDataLine(SmartConnectResult result)
	{
		if (result.RawData == null)
		{
			return "(none)";
		}

		var parts = new List<string>();
		foreach (var pair in result.RawData)
		{
			parts.Add(pair.Key + "=" + pair.Value);
		}

		return string.Join("; ", parts);
	}

	private static Money? PromptAmount(string prompt)
	{
		Console.Write(prompt);
		var text = Console.ReadLine()?.Trim();
		if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out var amount)
			|| amount <= 0
			|| amount > 100m)
		{
			Console.WriteLine("Enter an amount between 0.01 and 100.00 (small amounts only — this is a dev probe tool).");
			return null;
		}

		return Money.FromDecimal(amount);
	}

	/// <summary>
	/// Masks the bearer token for display/transcript in BOTH the forms it appears: as a URL query parameter
	/// (<c>merchantAccessToken=...</c>) and as its own JSON field (<c>"merchantAccessToken": "..."</c> at the
	/// response root). Display-only — persisted state always keeps the full URL, or recovery would break.
	/// </summary>
	private static string RedactToken(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "(none)";
		}

		const string name = "merchantAccessToken";
		var output = text!;
		var searchFrom = 0;
		int index;
		while ((index = output.IndexOf(name, searchFrom, StringComparison.OrdinalIgnoreCase)) >= 0)
		{
			var i = index + name.Length;
			while (i < output.Length && (output[i] == '"' || char.IsWhiteSpace(output[i])))
			{
				i++;
			}

			if (i >= output.Length || (output[i] != '=' && output[i] != ':'))
			{
				searchFrom = index + name.Length;
				continue;
			}

			i++;
			while (i < output.Length && char.IsWhiteSpace(output[i]))
			{
				i++;
			}

			var quoted = i < output.Length && output[i] == '"';
			if (quoted)
			{
				i++;
			}

			var valueStart = i;
			var valueEnd = valueStart;
			while (valueEnd < output.Length
				&& (quoted ? output[valueEnd] != '"' : output[valueEnd] != '&' && output[valueEnd] != '"' && !char.IsWhiteSpace(output[valueEnd])))
			{
				valueEnd++;
			}

			output = output.Substring(0, valueStart) + "****" + output.Substring(valueEnd);
			searchFrom = valueStart + 4;
		}

		return output;
	}

	// (H9) Probe outcomes must survive the console scroll — verdicts feed the design doc / ADR.
	private static void Transcript(string line)
	{
		try
		{
			File.AppendAllText(_transcriptPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{RuntimeLabel}] {line}{Environment.NewLine}");
		}
		catch
		{
			// The transcript must never break the demo.
		}
	}

	private static string RuntimeLabel =>
#if NET48
		"net48";
#else
		"net8.0";
#endif

	private static void LoadOrPromptSettings(string settingsDirectory)
	{
		if (File.Exists(_settingsPath))
		{
			try
			{
				_settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath)) ?? new Settings();
			}
			catch (JsonException)
			{
				_settings = new Settings();
			}
		}

		_settings.BaseUrl = PromptWithDefault("Base URL", _settings.BaseUrl ?? SmartConnectEnvironments.Development.AbsoluteUri);
		_settings.StateDirectory = PromptWithDefault("State directory", _settings.StateDirectory ?? Path.Combine(settingsDirectory, "state"));
		_settings.BusinessName = PromptWithDefault("Business name = the RETAILER/STORE, e.g. Acme Stores (docs: 'Store Name'; must match across pairing + transactions)", _settings.BusinessName ?? "Demo Business");
		_settings.VendorName = PromptWithDefault("Vendor name = the POS SOFTWARE PROVIDER, e.g. MyPosCo (docs: 'POS Software Vendor'; must match too)", _settings.VendorName ?? "YortSmartConnectDemo");
		_settings.RegisterName = PromptWithDefault("Register name", _settings.RegisterName ?? "Demo Register 1");
		_settings.MaxPollMinutes = int.TryParse(PromptWithDefault("Max poll duration (minutes; generous for manual card handling)", _settings.MaxPollMinutes.ToString()), out var minutes) && minutes > 0 ? minutes : 10;

		// Deterministic UUID v5: the same business + register names always produce the same id, so this
		// demo re-uses its pairing across runs and reinstalls.
		_settings.RegisterId = SmartConnectRegisterId.Generate(_settings.BusinessName!, _settings.RegisterName!).ToString();
		Console.WriteLine("POSRegisterID (deterministic): " + _settings.RegisterId);

		Directory.CreateDirectory(_settings.StateDirectory!);
		// Identifiers only — never credentials. The STATE directory is the sensitive one (it persists
		// token-bearing polling URLs); see the sample README.
		File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
	}

	private static string PromptWithDefault(string prompt, string defaultValue)
	{
		Console.Write($"{prompt} [{defaultValue}]: ");
		var input = Console.ReadLine()?.Trim();
		return string.IsNullOrEmpty(input) ? defaultValue : input!;
	}

	private sealed class Settings
	{
		public string? BaseUrl { get; set; }
		public string? StateDirectory { get; set; }
		public string? BusinessName { get; set; }
		public string? VendorName { get; set; }
		public string? RegisterName { get; set; }
		public string? RegisterId { get; set; }
		public int MaxPollMinutes { get; set; } = 10;
	}

	private sealed class ConsoleProgress : IProgress<SmartConnectPollingStatus>
	{
		public void Report(SmartConnectPollingStatus value)
			=> Console.WriteLine($"  [{value.State}]" + (value.Error != null ? $" {value.Error.GetType().Name}" : string.Empty));
	}

	/// <summary>Minimal console logger — any <see cref="ILogger"/> implementation works here (Serilog, NLog, M.E.Logging.Console, ...).</summary>
	private sealed class ConsoleLogger : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			var original = Console.ForegroundColor;
			Console.ForegroundColor = logLevel >= LogLevel.Error ? ConsoleColor.Red
				: logLevel == LogLevel.Warning ? ConsoleColor.Yellow
				: ConsoleColor.DarkGray;
			Console.WriteLine($"  log[{logLevel}] {formatter(state, exception)}");
			Console.ForegroundColor = original;
		}
	}
}
