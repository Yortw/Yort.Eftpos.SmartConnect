using System;
using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class DialogTimeoutsTests
{
	[Fact]
	public void ToIntervalMs_NormalDuration_ReturnsMilliseconds()
	{
		Assert.Equal(5000, DialogTimeouts.ToIntervalMs(TimeSpan.FromSeconds(5)));
	}

	[Fact]
	public void ToIntervalMs_Zero_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => DialogTimeouts.ToIntervalMs(TimeSpan.Zero));
	}

	[Fact]
	public void ToIntervalMs_Negative_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => DialogTimeouts.ToIntervalMs(TimeSpan.FromSeconds(-1)));
	}

	[Fact]
	public void ToIntervalMs_HugeDuration_ClampsToIntMax()
	{
		Assert.Equal(int.MaxValue, DialogTimeouts.ToIntervalMs(TimeSpan.FromDays(365)));
	}
}
