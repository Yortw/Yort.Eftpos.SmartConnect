using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests.Fakes;

internal sealed class FakeProgressView : IProgressView
{
	public int ShowBusyCount { get; private set; }
	public List<string> Captions { get; } = new();
	public ResultVisual? ShownResult { get; private set; }
	public TimeSpan? ResultAutoClose { get; private set; }

	public void ShowBusy(string caption)
	{
		ShowBusyCount++;
		Captions.Add(caption);
	}

	public void UpdateCaption(string caption)
	{
		Captions.Add(caption);
	}

	public Task ShowResultAsync(ResultVisual visual, TimeSpan? autoCloseAfter)
	{
		ShownResult = visual;
		ResultAutoClose = autoCloseAfter;
		return Task.CompletedTask;
	}
}
