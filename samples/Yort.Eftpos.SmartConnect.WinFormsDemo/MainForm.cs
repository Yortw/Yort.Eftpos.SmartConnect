using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Yort.Eftpos.SmartConnect;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinFormsDemo;

/// <summary>Manual smoke harness for the WinForms dialogs. Fill in BaseUrl/state store and the
/// registration triple for your dev environment before running against a real terminal.</summary>
internal sealed class MainForm : Form
{
	private readonly Button _pair = new Button { Text = "Pair…", Bounds = new Rectangle(20, 20, 160, 40) };
	private readonly Button _purchase = new Button { Text = "Purchase $1.00", Bounds = new Rectangle(20, 70, 160, 40) };

	public MainForm()
	{
		Text = "SmartConnect WinForms Demo";
		ClientSize = new Size(220, 130);
		Controls.Add(_pair);
		Controls.Add(_purchase);
		_pair.Click += async (_, _) => await PairAsync();
		_purchase.Click += async (_, _) => await PurchaseAsync();
	}

	private SmartConnectClient CreateClient()
	{
		// TODO (manual): supply a real dev configuration (BaseUrl, StateStore) before running.
		var configuration = new SmartConnectClientConfiguration
		{
			BaseUrl = SmartConnectEnvironments.Development,
			StateStore = new FileBasedTransactionStateStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SmartConnectWinFormsDemo"))
		};
		return new SmartConnectClient(configuration);
	}

	private async Task PairAsync()
	{
		using var client = CreateClient();
		using var dialog = new SmartConnectPairingDialog(this) { WindowTitle = "Pair Terminal" };

		var request = new SmartConnectPairingRequest
		{
			POSRegisterID = SmartConnectRegisterId.Generate("DemoMerchant", "Register-01"),
			POSBusinessName = "Demo Store",
			POSVendorName = "WinFormsDemo",
			POSRegisterName = "Front Counter"
		};

		var result = await dialog.ShowAsync(code => client.PairAsync(code, request));
		MessageBox.Show(result is null ? "Cancelled" : (result.Success ? "Paired" : "Failed: " + result.ErrorMessage));
	}

	private async Task PurchaseAsync()
	{
		using var client = CreateClient();
		using var dialog = new SmartConnectProgressDialog(this) { WindowTitle = "EFTPOS" };

		var request = new SmartConnectTransactionRequest
		{
			TransactionType = SmartConnectTransactionType.CardPurchase,
			AmountTotal = Money.FromDecimal(1.00m),
			POSRegisterID = SmartConnectRegisterId.Generate("DemoMerchant", "Register-01"),
			POSBusinessName = "Demo Store",
			POSVendorName = "WinFormsDemo",
			ClientTransactionRef = "demo-" + Guid.NewGuid().ToString("N")
		};

		var result = await client.ProcessTransactionAsync(request, dialog.Progress);
		await dialog.ShowResultAsync(result, TimeSpan.FromSeconds(5));
	}
}
