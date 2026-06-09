using System;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// Base URLs for the SmartConnect environments. Use these for <see cref="SmartConnectClientConfiguration.BaseUrl"/>.
/// </summary>
/// <remarks>
/// The only difference between environments is the subdomain. It is a common and costly mistake to ship
/// against <see cref="Development"/>; double-check the configured environment before release.
/// </remarks>
public static class SmartConnectEnvironments
{
	/// <summary>The production environment (<c>https://api.smart-connect.cloud/POS</c>).</summary>
	public static readonly Uri Production = new Uri("https://api.smart-connect.cloud/POS");

	/// <summary>The development/testing environment (<c>https://api-dev.smart-connect.cloud/POS</c>).</summary>
	public static readonly Uri Development = new Uri("https://api-dev.smart-connect.cloud/POS");
}
