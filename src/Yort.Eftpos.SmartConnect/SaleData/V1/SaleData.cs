using System;
using System.Collections.Generic;

namespace Yort.Eftpos.SmartConnect.SaleData.V1;

/// <summary>
/// The SmartConnect <c>SaleData</c> v1.0.0 payload (sale-level metadata). All monetary fields are vendor-typed
/// strings of unspecified numeric encoding — pass them as the terminal expects; the library does not interpret
/// or validate them. Descriptive metadata only: it is not echoed back and cannot be used for crash recovery.
/// Mutable, for object-initialiser construction.
/// </summary>
public sealed class SaleData : SmartConnectSaleData
{
	/// <inheritdoc />
	public override string Version => "1.0.0";

	/// <summary>Caller's unique sale identifier.</summary>
	public string? SaleId { get; set; }

	/// <summary>Invoice reference.</summary>
	public string? InvoiceNumber { get; set; }

	/// <summary>Sale creation time. Serialised as ISO 8601 with the value's offset (not normalised) — the vendor
	/// schema expects UTC, so supply a UTC value.</summary>
	public DateTimeOffset? CreatedAt { get; set; }

	/// <summary>Sale last-updated time. Serialised as ISO 8601 with the value's offset (not normalised) — the
	/// vendor schema expects UTC, so supply a UTC value.</summary>
	public DateTimeOffset? UpdatedAt { get; set; }

	/// <summary>Total amount including tax. Required by the wire schema. Vendor string; encoding unspecified.</summary>
	public string? TotalAmount { get; set; }

	/// <summary>Total tax amount. Required by the wire schema. Vendor string; encoding unspecified.</summary>
	public string? TotalTax { get; set; }

	/// <summary>Tips, if not entered on the terminal.</summary>
	public string? TotalTips { get; set; }

	/// <summary>Surcharge amount.</summary>
	public string? TotalSurcharge { get; set; }

	/// <summary>For a refund, the original sale id this returns against.</summary>
	public string? ReturnFor { get; set; }

	/// <summary>Operator/user identifier.</summary>
	public string? UserId { get; set; }

	/// <summary>Operator/user name.</summary>
	public string? UserName { get; set; }

	/// <summary>Customer identifier.</summary>
	public string? CustomerId { get; set; }

	/// <summary>Customer name.</summary>
	public string? CustomerName { get; set; }

	/// <summary>Line items. Null (the default) omits the field from the wire entirely.</summary>
	public IList<LineItem>? LineItems { get; set; }
}
