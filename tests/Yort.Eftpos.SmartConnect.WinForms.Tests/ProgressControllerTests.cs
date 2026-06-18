using System;
using System.Threading.Tasks;
using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;
using Yort.Eftpos.SmartConnect.WinForms.Tests.Fakes;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class ProgressControllerTests
{
	[Fact]
	public void FirstReport_ShowsBusyExactlyOnce_AndSignalsFirstShow()
	{
		var view = new FakeProgressView();
		var firstShowCalls = 0;
		var controller = new ProgressController(view, DefaultCaptions.CreateStateCaptions(), () => firstShowCalls++);

		controller.Report(new SmartConnectPollingStatus { State = SmartConnectPollingState.Polling });
		controller.Report(new SmartConnectPollingStatus { State = SmartConnectPollingState.Delayed });

		Assert.Equal(1, view.ShowBusyCount);   // shown once, not per report
		Assert.Equal(1, firstShowCalls);        // owner disabled once
	}

	[Fact]
	public void SubsequentReports_UpdateCaption()
	{
		var view = new FakeProgressView();
		var controller = new ProgressController(view, DefaultCaptions.CreateStateCaptions(), () => { });

		controller.Report(new SmartConnectPollingStatus { State = SmartConnectPollingState.Polling, Message = "first" });
		controller.Report(new SmartConnectPollingStatus { State = SmartConnectPollingState.Polling, Message = "second" });

		Assert.Equal(new[] { "first", "second" }, view.Captions);
	}

	[Fact]
	public async Task ShowResultAsync_ForwardsVisualAndTimeout()
	{
		var view = new FakeProgressView();
		var controller = new ProgressController(view, DefaultCaptions.CreateStateCaptions(), () => { });
		var visual = new ResultVisual("Approved", ResultSeverity.Success, null);

		await controller.ShowResultAsync(visual, TimeSpan.FromSeconds(5));

		Assert.Equal("Approved", view.ShownResult!.Value.Caption);
		Assert.Equal(TimeSpan.FromSeconds(5), view.ResultAutoClose);
	}
}
