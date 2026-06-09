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
}
