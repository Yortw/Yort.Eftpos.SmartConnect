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

	// A positive-but-sub-millisecond span passes the <= 0 guard yet truncates to 0, which Timer.Interval
	// rejects — defeating the whole point of this "single guarded conversion". It must floor to 1ms.
	[Theory]
	[InlineData(1)]                 // TimeSpan.FromTicks(1) — smallest positive
	[InlineData(5000)]              // 0.5ms
	[InlineData(9999)]              // just under 1ms
	public void ToIntervalMs_SubMillisecondPositive_ReturnsAtLeastOne(long ticks)
	{
		Assert.Equal(1, DialogTimeouts.ToIntervalMs(TimeSpan.FromTicks(ticks)));
	}
}
