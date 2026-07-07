using System;
using System.Windows.Forms;
using Xunit;
using Yort.Eftpos.SmartConnect;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class SmartConnectProgressDialogTests
{
	// An owner whose Handle must never be read. If ShowResultAsync validated its timeout late, the owner-disable
	// in EnsureAppearanceAndOwner would read Handle and throw THIS instead of the argument exception.
	private sealed class ExplodingOwner : IWin32Window
	{
		public IntPtr Handle => throw new InvalidOperationException("the owner handle must not be read");
	}

	[Fact]
	public void ShowResultAsync_NonPositiveTimeout_ThrowsBeforeShowingOrDisablingOwner()
	{
		// (F3) A non-positive auto-close span is rejected up front with a documented ArgumentOutOfRangeException,
		// before the dialog shows the outcome or disables the owner. Previously the same span threw from deep in
		// the form (DialogTimeouts.ToIntervalMs) AFTER the outcome was shown and the owner disabled, leaving a
		// dialog whose task nothing completes. The exploding owner proves nothing was touched.
		using var dialog = new SmartConnectProgressDialog(new ExplodingOwner());

		var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
		{
			_ = dialog.ShowResultAsync(new SmartConnectTransactionResult { Status = SmartConnectTransactionStatus.Accepted }, TimeSpan.Zero);
		});

		Assert.Equal("autoCloseAfter", ex.ParamName);
	}

	[Fact]
	public void ShowResultAsync_NegativeTimeout_OperationOverload_ThrowsBeforeDisablingOwner()
	{
		// Same guard on the operation-result overload (its own private core).
		using var dialog = new SmartConnectProgressDialog(new ExplodingOwner());

		var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
		{
			_ = dialog.ShowResultAsync(new SmartConnectOperationResult { Status = SmartConnectOperationStatus.Succeeded }, TimeSpan.FromSeconds(-1));
		});

		Assert.Equal("autoCloseAfter", ex.ParamName);
	}
}
