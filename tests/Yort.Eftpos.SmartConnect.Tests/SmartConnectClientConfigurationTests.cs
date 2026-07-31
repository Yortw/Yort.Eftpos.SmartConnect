using System;
using Xunit;
using Yort.Eftpos.SmartConnect.Tests.Helpers;

namespace Yort.Eftpos.SmartConnect.Tests;

public class SmartConnectClientConfigurationTests
{
	private static SmartConnectClientConfiguration ValidConfig() => new SmartConnectClientConfiguration
	{
		BaseUrl = SmartConnectEnvironments.Development,
		StateStore = new InMemoryTransactionStateStore()
	};

	[Fact]
	public void Validate_WithValidDefaults_DoesNotThrow()
	{
		Assert.Null(Record.Exception(() => ValidConfig().Validate()));
	}

	[Fact]
	public void Validate_NullStateStore_Throws()
	{
		var config = ValidConfig();
		config.StateStore = null;
		Assert.Throws<ArgumentNullException>(() => config.Validate());
	}

	[Fact]
	public void Validate_NullBaseUrl_Throws()
	{
		var config = ValidConfig();
		config.BaseUrl = null;
		Assert.Throws<ArgumentNullException>(() => config.Validate());
	}

	// A relative BaseUrl (e.g. from `new Uri(configValue, UriKind.RelativeOrAbsolute)` on a malformed
	// string) passed the null check, then the client ctor's `BaseUrl.AbsoluteUri` threw an UNDOCUMENTED
	// InvalidOperationException with no hint BaseUrl was the culprit. Validate must reject it up front, as
	// the documented ArgumentException, so the diagnosis points at the offending value.
	[Fact]
	public void Validate_RelativeBaseUrl_ThrowsArgumentException()
	{
		var config = ValidConfig();
		config.BaseUrl = new Uri("/pos", UriKind.Relative);
		var ex = Assert.Throws<ArgumentException>(() => config.Validate());
		Assert.Equal(nameof(SmartConnectClientConfiguration.BaseUrl), ex.ParamName);
	}

	[Fact]
	public void Validate_AbsoluteBaseUrl_DoesNotThrow()
	{
		var config = ValidConfig();
		config.BaseUrl = new Uri("https://example.test/pos", UriKind.RelativeOrAbsolute);
		Assert.Null(Record.Exception(() => config.Validate()));
	}

	[Fact]
	public void Validate_PollIntervalBelowMinimum_Throws()
	{
		var config = ValidConfig();
		config.PollInterval = TimeSpan.FromSeconds(1);
		Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
	}

	[Fact]
	public void Validate_PollIntervalAtMinimum_DoesNotThrow()
	{
		var config = ValidConfig();
		config.PollInterval = SmartConnectClientConfiguration.MinimumPollInterval;
		Assert.Null(Record.Exception(() => config.Validate()));
	}

	// SmartConnect docs specify no transaction/poll timeout. Pinpads self-time-out around ~3 minutes, so a
	// 5-minute POS-side backstop sits past the device timeout without stranding a customer (10 was too long).
	[Fact]
	public void Default_MaxPollDuration_IsFiveMinutes()
	{
		Assert.Equal(TimeSpan.FromMinutes(5), new SmartConnectClientConfiguration().MaxPollDuration);
	}

	// A MaxPollDuration below one PollInterval would make the poll loop give up before its first poll —
	// returning Unknown for a transaction that may have been sent. Rejected at construction.
	[Fact]
	public void Validate_MaxPollDurationBelowPollInterval_Throws()
	{
		var config = ValidConfig();
		config.PollInterval = TimeSpan.FromSeconds(3);
		config.MaxPollDuration = TimeSpan.FromSeconds(2);
		Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
	}

	[Fact]
	public void Validate_NonPositiveMaxPollDuration_Throws()
	{
		var config = ValidConfig();
		config.MaxPollDuration = TimeSpan.Zero;
		Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
	}

	// BackoffCap is the ceiling for 429 backoff and the Retry-After clamp; below PollInterval it collapses the
	// backoff into a tight re-poll storm.
	[Fact]
	public void Validate_BackoffCapBelowPollInterval_Throws()
	{
		var config = ValidConfig();
		config.PollInterval = TimeSpan.FromSeconds(3);
		config.BackoffCap = TimeSpan.FromSeconds(2);
		Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
	}
}
