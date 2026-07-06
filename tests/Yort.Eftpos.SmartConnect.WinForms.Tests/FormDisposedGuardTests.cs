using System.Threading.Tasks;
using Xunit;
using Yort.Eftpos.SmartConnect;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

/// <summary>
/// A form disposed mid-flight (host shutdown, early using-scope exit) must resolve its awaitable view methods
/// without touching disposed controls or minting a TaskCompletionSource nothing would complete — otherwise the
/// controller's caller hangs. All three dialog forms guard this the same way; these pin the whole class.
/// </summary>
public class FormDisposedGuardTests
{
	[Fact]
	public async Task PairingForm_Disposed_AwaitablesCompleteWithCancelEquivalentValues()
	{
		var form = new PairingForm();
		form.Dispose();

		var codeTask = form.GetCodeAsync();
		var failureTask = form.ShowFailureAsync("gone", ResultSeverity.Negative);
		var successTask = form.ShowSuccessAsync(new SmartConnectPairingResult { Success = true });

		// The load-bearing invariant: each resolves synchronously rather than hanging on a TCS nothing completes.
		Assert.True(codeTask.IsCompleted);
		Assert.True(failureTask.IsCompleted);
		Assert.True(successTask.IsCompleted);
		Assert.Null(await codeTask);      // reads as operator-cancel
		Assert.False(await failureTask);  // reads as "don't retry"
		await successTask;
	}

	[Fact]
	public async Task ProgressForm_Disposed_ShowResultCompletes()
	{
		var form = new ProgressForm();
		form.Dispose();

		var task = form.ShowResultAsync(new ResultVisual("Approved", ResultSeverity.Success, null), null);

		Assert.True(task.IsCompleted);
		await task;
	}

	[Fact]
	public async Task ReceiptForm_Disposed_ShowReceiptCompletes()
	{
		var form = new ReceiptForm();
		form.Dispose();

		var task = form.ShowReceiptAsync("RECEIPT");

		Assert.True(task.IsCompleted);
		await task;
	}
}
