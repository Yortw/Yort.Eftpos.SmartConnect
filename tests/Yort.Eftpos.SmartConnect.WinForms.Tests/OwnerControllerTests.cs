using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class OwnerControllerTests
{
	private sealed class FakeWindow : IWin32Window
	{
		public IntPtr Handle => new IntPtr(42);
	}

	[Fact]
	public void Disable_NullOwner_DoesNothingAndDoesNotThrow()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(owner: null, disableWhileBusy: true, (h, e) => calls.Add((h, e)));

		controller.Disable();
		controller.Restore();

		Assert.Empty(calls);
	}

	[Fact]
	public void Disable_WhenEnabled_DisablesThenRestoreReenables()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(new FakeWindow(), disableWhileBusy: true, (h, e) => calls.Add((h, e)));

		controller.Disable();
		controller.Restore();

		Assert.Equal(2, calls.Count);
		Assert.Equal((new IntPtr(42), false), calls[0]);
		Assert.Equal((new IntPtr(42), true), calls[1]);
	}

	[Fact]
	public void Disable_WhenDisableWhileBusyFalse_NeverTouchesOwner()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(new FakeWindow(), disableWhileBusy: false, (h, e) => calls.Add((h, e)));

		controller.Disable();
		controller.Restore();

		Assert.Empty(calls);
	}

	[Fact]
	public void Restore_WithoutDisable_DoesNothing()
	{
		var calls = new List<(IntPtr handle, bool enabled)>();
		var controller = new OwnerController(new FakeWindow(), disableWhileBusy: true, (h, e) => calls.Add((h, e)));

		controller.Restore();

		Assert.Empty(calls);
	}
}
