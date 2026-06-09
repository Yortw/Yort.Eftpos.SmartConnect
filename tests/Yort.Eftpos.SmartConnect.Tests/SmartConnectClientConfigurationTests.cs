using System;
using NSubstitute;
using Xunit;

namespace Yort.Eftpos.SmartConnect.Tests;

public class SmartConnectClientConfigurationTests
{
	private static SmartConnectClientConfiguration ValidConfig() => new SmartConnectClientConfiguration
	{
		BaseUrl = SmartConnectEnvironments.Development,
		StateStore = Substitute.For<ISmartConnectTransactionState>()
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
}
