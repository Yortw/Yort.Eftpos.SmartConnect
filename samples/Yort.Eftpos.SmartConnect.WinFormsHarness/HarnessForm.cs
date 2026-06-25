using System;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Yort.Eftpos.SmartConnect;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinFormsHarness;

/// <summary>A launcher of one button per dialog visual state, each driven by synthetic data — the pairing
/// dialog via a fake callback, the progress dialog via fake <see cref="IProgress{T}"/> reports plus a fake
/// result. No client and no network, so the dialog look/feel can be iterated on with a build-and-click loop.</summary>
internal sealed class HarnessForm : Form
{
	// Short delays so the busy/marquee states are actually visible before the outcome replaces them.
	private const int BusyDelayMs = 500;
	private const int PollDelayMs = 700;
	private static readonly TimeSpan AutoClose = TimeSpan.FromSeconds(5);

	private readonly Label _status;

	public HarnessForm()
	{
		Text = "SmartConnect Dialog Harness";
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(580, 470);

		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(8) };
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

		layout.Controls.Add(BuildPairingGroup(), 0, 0);
		layout.Controls.Add(BuildProgressGroup(), 1, 0);

		_status = new Label
		{
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			BorderStyle = BorderStyle.Fixed3D,
			Padding = new Padding(6, 0, 0, 0),
			Text = "Ready — click a scenario."
		};
		layout.Controls.Add(_status, 0, 1);
		layout.SetColumnSpan(_status, 2);

		Controls.Add(layout);
	}

	private GroupBox BuildPairingGroup()
	{
		var panel = NewButtonPanel();
		AddScenario(panel, "Success", () => RunPairing(FakePair(true, null)));
		AddScenario(panel, "Declined (with message)", () => RunPairing(FakePair(false, "Invalid pairing code.")));
		AddScenario(panel, "Terse error (HTTP 401)", () => RunPairing(FakePair(false, "HTTP 401 Unauthorized")));
		AddScenario(panel, "Transport — NotSent (amber)", () => RunPairing(FakeTransport(SmartConnectRequestDelivery.NotSent)));
		AddScenario(panel, "Transport — Unknown (amber)", () => RunPairing(FakeTransport(SmartConnectRequestDelivery.Unknown)));
		AddScenario(panel, "Retry, then success", () => RunPairing(FakeRetryThenSuccess()));

		var group = new GroupBox { Text = "Pairing dialog", Dock = DockStyle.Fill };
		group.Controls.Add(panel);
		return group;
	}

	private GroupBox BuildProgressGroup()
	{
		var panel = NewButtonPanel();
		AddScenario(panel, "Financial — Accepted", () => RunFinancial(SmartConnectTransactionStatus.Accepted));
		AddScenario(panel, "Financial — Declined", () => RunFinancial(SmartConnectTransactionStatus.Declined));
		AddScenario(panel, "Financial — Cancelled", () => RunFinancial(SmartConnectTransactionStatus.Cancelled));
		AddScenario(panel, "Financial — DeviceOffline", () => RunFinancial(SmartConnectTransactionStatus.DeviceOffline));
		AddScenario(panel, "Financial — Unknown", () => RunFinancial(SmartConnectTransactionStatus.Unknown));
		AddScenario(panel, "Financial — Failed", () => RunFinancial(SmartConnectTransactionStatus.Failed));
		AddScenario(panel, "Operation — Succeeded", () => RunOperation(SmartConnectOperationStatus.Succeeded, null));
		AddScenario(panel, "Operation — Failed", () => RunOperation(SmartConnectOperationStatus.Failed, "Settlement was rejected by the acquirer."));
		AddScenario(panel, "Progress states sequence", RunProgressStates);

		var group = new GroupBox { Text = "Progress dialog", Dock = DockStyle.Fill };
		group.Controls.Add(panel);
		return group;
	}

	private static FlowLayoutPanel NewButtonPanel()
	{
		return new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
	}

	private void AddScenario(FlowLayoutPanel panel, string text, Func<Task> run)
	{
		var button = new Button { Text = text, Width = 250, Height = 30, Margin = new Padding(3) };
		button.Click += async (_, _) =>
		{
			try
			{
				await run();
			}
			catch (Exception ex)
			{
				// A scenario throwing is itself interesting (it's how transport failures present), so surface
				// it rather than letting it tear down the harness.
				SetStatus("Unhandled: " + ex.GetType().Name + " — " + ex.Message);
			}
		};
		panel.Controls.Add(button);
	}

	private async Task RunPairing(Func<string, Task<SmartConnectPairingResult>> callback)
	{
		using (var dialog = new SmartConnectPairingDialog(this) { WindowTitle = "Pair Terminal" })
		{
			var result = await dialog.ShowAsync(callback);
			SetStatus(result is null
				? "Pairing: cancelled"
				: result.Success ? "Pairing: paired" : "Pairing: failed — " + result.ErrorMessage);
		}
	}

	private async Task RunFinancial(SmartConnectTransactionStatus status)
	{
		using (var dialog = new SmartConnectProgressDialog(this) { WindowTitle = "EFTPOS" })
		{
			dialog.Progress.Report(new SmartConnectPollingStatus { State = SmartConnectPollingState.Polling });
			await Task.Delay(PollDelayMs);
			await dialog.ShowResultAsync(new SmartConnectTransactionResult { Status = status }, AutoClose);
			SetStatus("Progress: financial " + status);
		}
	}

	private async Task RunOperation(SmartConnectOperationStatus status, string? error)
	{
		using (var dialog = new SmartConnectProgressDialog(this) { WindowTitle = "EFTPOS" })
		{
			dialog.Progress.Report(new SmartConnectPollingStatus { State = SmartConnectPollingState.Polling });
			await Task.Delay(PollDelayMs);
			await dialog.ShowResultAsync(new SmartConnectOperationResult { Status = status, ErrorMessage = error }, AutoClose);
			SetStatus("Progress: operation " + status);
		}
	}

	private async Task RunProgressStates()
	{
		using (var dialog = new SmartConnectProgressDialog(this) { WindowTitle = "EFTPOS" })
		{
			var states = new[]
			{
				SmartConnectPollingState.Polling,
				SmartConnectPollingState.Delayed,
				SmartConnectPollingState.BackingOff,
				SmartConnectPollingState.NetworkError
			};

			foreach (var state in states)
			{
				dialog.Progress.Report(new SmartConnectPollingStatus
				{
					State = state,
					Error = state == SmartConnectPollingState.NetworkError ? new HttpRequestException("simulated network error") : null
				});
				await Task.Delay(1000);
			}

			await dialog.ShowResultAsync(new SmartConnectTransactionResult { Status = SmartConnectTransactionStatus.Accepted }, AutoClose);
			SetStatus("Progress: states sequence complete");
		}
	}

	private static Func<string, Task<SmartConnectPairingResult>> FakePair(bool success, string? error)
	{
		return async _ =>
		{
			await Task.Delay(BusyDelayMs);
			return new SmartConnectPairingResult { Success = success, ErrorMessage = error };
		};
	}

	private static Func<string, Task<SmartConnectPairingResult>> FakeTransport(SmartConnectRequestDelivery delivery)
	{
		return async _ =>
		{
			await Task.Delay(BusyDelayMs);
			throw new SmartConnectTransportException(delivery, new HttpRequestException("simulated transport failure"));
		};
	}

	private static Func<string, Task<SmartConnectPairingResult>> FakeRetryThenSuccess()
	{
		var attempt = 0;
		return async _ =>
		{
			await Task.Delay(BusyDelayMs);
			attempt++;
			return attempt == 1
				? new SmartConnectPairingResult { Success = false, ErrorMessage = "Invalid pairing code." }
				: new SmartConnectPairingResult { Success = true };
		};
	}

	private void SetStatus(string text)
	{
		_status.Text = text;
	}
}
