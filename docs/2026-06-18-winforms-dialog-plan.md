# Yort.Eftpos.SmartConnect.WinForms Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a WinForms companion package with two dialogs — a progress/outcome dialog for the polling operations and a pairing dialog — over the public surface of the core `Yort.Eftpos.SmartConnect` client.

**Architecture:** All decision logic lives in small, UI-free internal types (caption/severity resolution, the pairing loop state machine, owner-window control) behind narrow internal *view* interfaces; the WinForms `Form`s are thin views implementing those interfaces. This isolates the testable logic from the untestable `Form`, mirroring the spec's testing strategy. The public surface is two `IDisposable` wrapper types; each `Form` stays internal.

**Tech Stack:** C# / .NET (`net48` + `net8.0-windows`), WinForms, xUnit, the core `Yort.Eftpos.SmartConnect` package. Design + ADR: [`2026-06-18-winforms-dialog-design.md`](./2026-06-18-winforms-dialog-design.md), [`2026-06-18-winforms-dialog-adr.md`](./2026-06-18-winforms-dialog-adr.md).

## Global Constraints

- **Target frameworks:** `<TargetFrameworks>net48;net8.0-windows</TargetFrameworks>`, `<UseWindowsForms>true</UseWindowsForms>`.
- **Project conventions (match the core project):** `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>`, `<GenerateDocumentationFile>true</GenerateDocumentationFile>`.
- **Code style (match the core + Troy's conventions):** hard tabs; full braces always; pattern matching on `if` statements, **not** `switch` expressions; file-scoped namespaces in hand-written files; XML doc comments on all public and internal members.
- **Dependency rule:** depend only on the public surface of `Yort.Eftpos.SmartConnect` (no `Internal` types). The published package takes a `PackageReference` dependency on `Yort.Eftpos.SmartConnect`; in-repo it is a `ProjectReference`.
- **Package metadata:** PackageId `Yort.Eftpos.SmartConnect.WinForms`; Version `0.1.0-preview.1` (aligned with the core); MIT; "unofficial, not affiliated with or endorsed by Shift4 / SmartPay"; reuse the existing non-vendor `Icon.png`.
- **Threading:** the dialogs must be constructed on the UI thread (documented on each public type) — `Progress<T>` captures the construction-time `SynchronizationContext`.
- **API rule:** overloads over optional/default parameters on public members.
- **Commits:** no `Co-Authored-By` trailers.
- **Build/test execution:** run builds and `dotnet test` through clio (`start_terminal` → `send_input` → `wait_for_command` → `read_build_errors`), not background shell polling.

---

## File Structure

**Library — `src/Yort.Eftpos.SmartConnect.WinForms/`:**
- `Yort.Eftpos.SmartConnect.WinForms.csproj` — project + package metadata.
- `ResultSeverity.cs` — internal enum: Success / Ambiguous / Negative.
- `ResultVisual.cs` — internal readonly struct: caption + severity + optional detail.
- `ResultVisuals.cs` — internal static: status → `ResultVisual` for both result enums.
- `DefaultCaptions.cs` — internal static: factory methods for the three pre-populated caption maps.
- `CaptionResolver.cs` — internal static: progress caption resolution (Message vs map).
- `NativeMethods.cs` — internal static: `EnableWindow` P/Invoke.
- `OwnerController.cs` — internal: owner-window disable/restore with an injectable enable-action.
- `IProgressView.cs` — internal interface.
- `ProgressController.cs` — internal: drives an `IProgressView` from progress reports.
- `ProgressForm.cs` — internal `Form : IProgressView` (hand-coded, no designer file).
- `SmartConnectProgressDialog.cs` — public wrapper.
- `IPairingView.cs` — internal interface.
- `PairingController.cs` — internal: the pairing loop state machine.
- `PairingForm.cs` — internal `Form : IPairingView` (hand-coded, no designer file).
- `SmartConnectPairingDialog.cs` — public wrapper.
- `DialogChrome.cs` — internal: shared appearance holder + `ApplyTo(Form, PictureBox)`.
- `README.md`, `Icon.png` (linked from repo root).

**Tests — `tests/Yort.Eftpos.SmartConnect.WinForms.Tests/`:**
- `Yort.Eftpos.SmartConnect.WinForms.Tests.csproj` — `net8.0-windows`, xUnit.
- `ResultVisualsTests.cs`, `CaptionResolverTests.cs`, `OwnerControllerTests.cs`,
  `ProgressControllerTests.cs`, `PairingControllerTests.cs`, plus the fakes
  `Fakes/FakeProgressView.cs`, `Fakes/FakePairingView.cs`.

**Sample — `samples/Yort.Eftpos.SmartConnect.WinFormsDemo/`:**
- `Yort.Eftpos.SmartConnect.WinFormsDemo.csproj` — `net8.0-windows`, `OutputType` WinExe.
- `Program.cs`, `MainForm.cs`.

---

### Task 1: Solution & project scaffold

**Files:**
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/Yort.Eftpos.SmartConnect.WinForms.csproj`
- Create: `tests/Yort.Eftpos.SmartConnect.WinForms.Tests/Yort.Eftpos.SmartConnect.WinForms.Tests.csproj`
- Modify: the solution file at repo root (add both projects)

**Interfaces:**
- Consumes: the core project `src/Yort.Eftpos.SmartConnect/Yort.Eftpos.SmartConnect.csproj`.
- Produces: a buildable WinForms library assembly and a runnable (empty) test assembly.

- [ ] **Step 1: Create the library csproj**

`src/Yort.Eftpos.SmartConnect.WinForms/Yort.Eftpos.SmartConnect.WinForms.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net48;net8.0-windows</TargetFrameworks>
    <UseWindowsForms>true</UseWindowsForms>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Yort.Eftpos.SmartConnect.WinForms</RootNamespace>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <!-- NuGet package metadata. Unofficial: not affiliated with or endorsed by Shift4 / SmartPay. -->
  <PropertyGroup>
    <PackageId>Yort.Eftpos.SmartConnect.WinForms</PackageId>
    <Version>0.1.0-preview.1</Version>
    <Authors>Yortw</Authors>
    <Company>Yortw</Company>
    <Description>WinForms progress, outcome and pairing dialogs for the unofficial Yort.Eftpos.SmartConnect client (SmartPay / Shift4 SmartConnect, New Zealand). Not affiliated with or endorsed by Shift4 / SmartPay.</Description>
    <Copyright>Copyright (c) 2026 Yortw</Copyright>
    <PackageTags>eftpos;smartconnect;smartpay;shift4;winforms;pos;nz</PackageTags>
    <PackageProjectUrl>https://github.com/yortw/Yort.Eftpos.SmartConnect</PackageProjectUrl>
    <RepositoryUrl>https://github.com/yortw/Yort.Eftpos.SmartConnect</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageRequireLicenseAcceptance>false</PackageRequireLicenseAcceptance>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageIcon>Icon.png</PackageIcon>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>

  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
    <None Include="..\..\Icon.png" Pack="true" PackagePath="\" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Yort.Eftpos.SmartConnect\Yort.Eftpos.SmartConnect.csproj" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>Yort.Eftpos.SmartConnect.WinForms.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create a placeholder README for the package**

`src/Yort.Eftpos.SmartConnect.WinForms/README.md`:

```markdown
# Yort.Eftpos.SmartConnect.WinForms

WinForms progress/outcome and pairing dialogs for the unofficial
[`Yort.Eftpos.SmartConnect`](https://github.com/yortw/Yort.Eftpos.SmartConnect) client.

> **Unofficial:** not affiliated with, endorsed by, or supported by Shift4 / SmartPay.

(Full usage docs added in Task 9.)
```

- [ ] **Step 3: Create the test csproj**

`tests/Yort.Eftpos.SmartConnect.WinForms.Tests/Yort.Eftpos.SmartConnect.WinForms.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Yort.Eftpos.SmartConnect.WinForms\Yort.Eftpos.SmartConnect.WinForms.csproj" />
  </ItemGroup>

</Project>
```

(If the core test project pins different xUnit/Test.Sdk versions, match those exact versions instead.)

- [ ] **Step 4: Add both projects to the solution**

Run: `dotnet sln add src/Yort.Eftpos.SmartConnect.WinForms/Yort.Eftpos.SmartConnect.WinForms.csproj tests/Yort.Eftpos.SmartConnect.WinForms.Tests/Yort.Eftpos.SmartConnect.WinForms.Tests.csproj`
(Find the `.sln` path first with `ls *.sln`; if the repo has no solution, skip this step.)

- [ ] **Step 5: Build to verify the scaffold**

Run: `dotnet build src/Yort.Eftpos.SmartConnect.WinForms/Yort.Eftpos.SmartConnect.WinForms.csproj`
Expected: build succeeds for both `net48` and `net8.0-windows`, zero warnings.

- [ ] **Step 6: Run the (empty) test project**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests/Yort.Eftpos.SmartConnect.WinForms.Tests.csproj`
Expected: builds and runs; 0 tests, exit 0.

- [ ] **Step 7: Commit**

```bash
git add src/Yort.Eftpos.SmartConnect.WinForms tests/Yort.Eftpos.SmartConnect.WinForms.Tests *.sln
git commit -m "build: scaffold Yort.Eftpos.SmartConnect.WinForms project and tests"
```

---

### Task 2: Result severity & visual mapping

**Files:**
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/ResultSeverity.cs`
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/ResultVisual.cs`
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/ResultVisuals.cs`
- Test: `tests/Yort.Eftpos.SmartConnect.WinForms.Tests/ResultVisualsTests.cs`

**Interfaces:**
- Consumes: core `SmartConnectTransactionStatus` {Unknown, Accepted, Declined, Cancelled, DeviceOffline, Failed}, `SmartConnectOperationStatus` {Unknown, Succeeded, Failed}.
- Produces:
  - `internal enum ResultSeverity { Success, Ambiguous, Negative }`
  - `internal readonly struct ResultVisual` with `string Caption`, `ResultSeverity Severity`, `string? Detail`.
  - `internal static class ResultVisuals` with
    `ResultVisual ForTransaction(SmartConnectTransactionStatus status, IReadOnlyDictionary<SmartConnectTransactionStatus, string> captions)` and
    `ResultVisual ForOperation(SmartConnectOperationStatus status, string? errorMessage, IReadOnlyDictionary<SmartConnectOperationStatus, string> captions)`.

- [ ] **Step 1: Write the failing tests**

`tests/Yort.Eftpos.SmartConnect.WinForms.Tests/ResultVisualsTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class ResultVisualsTests
{
	private static readonly Dictionary<SmartConnectTransactionStatus, string> TxnCaptions = new()
	{
		[SmartConnectTransactionStatus.Unknown] = "Outcome unknown",
		[SmartConnectTransactionStatus.Accepted] = "Approved",
		[SmartConnectTransactionStatus.Declined] = "Declined",
		[SmartConnectTransactionStatus.Cancelled] = "Cancelled",
		[SmartConnectTransactionStatus.DeviceOffline] = "Terminal offline",
		[SmartConnectTransactionStatus.Failed] = "Failed",
	};

	private static readonly Dictionary<SmartConnectOperationStatus, string> OpCaptions = new()
	{
		[SmartConnectOperationStatus.Unknown] = "Outcome unknown",
		[SmartConnectOperationStatus.Succeeded] = "Completed",
		[SmartConnectOperationStatus.Failed] = "Failed",
	};

	[Theory]
	[InlineData(SmartConnectTransactionStatus.Accepted, ResultSeverity.Success)]
	[InlineData(SmartConnectTransactionStatus.Unknown, ResultSeverity.Ambiguous)]
	[InlineData(SmartConnectTransactionStatus.Declined, ResultSeverity.Negative)]
	[InlineData(SmartConnectTransactionStatus.Cancelled, ResultSeverity.Negative)]
	[InlineData(SmartConnectTransactionStatus.DeviceOffline, ResultSeverity.Negative)]
	[InlineData(SmartConnectTransactionStatus.Failed, ResultSeverity.Negative)]
	public void ForTransaction_MapsSeverityForEveryStatus(SmartConnectTransactionStatus status, ResultSeverity expected)
	{
		var visual = ResultVisuals.ForTransaction(status, TxnCaptions);
		Assert.Equal(expected, visual.Severity);
		Assert.Equal(TxnCaptions[status], visual.Caption);
	}

	[Theory]
	[InlineData(SmartConnectOperationStatus.Succeeded, ResultSeverity.Success)]
	[InlineData(SmartConnectOperationStatus.Unknown, ResultSeverity.Ambiguous)]
	[InlineData(SmartConnectOperationStatus.Failed, ResultSeverity.Negative)]
	public void ForOperation_MapsSeverityForEveryStatus(SmartConnectOperationStatus status, ResultSeverity expected)
	{
		var visual = ResultVisuals.ForOperation(status, errorMessage: null, OpCaptions);
		Assert.Equal(expected, visual.Severity);
		Assert.Equal(OpCaptions[status], visual.Caption);
	}

	[Fact]
	public void ForOperation_Failed_CarriesErrorMessageAsDetail()
	{
		var visual = ResultVisuals.ForOperation(SmartConnectOperationStatus.Failed, "Acquirer rejected", OpCaptions);
		Assert.Equal("Acquirer rejected", visual.Detail);
	}

	[Fact]
	public void ForOperation_NonFailed_HasNoDetail()
	{
		var visual = ResultVisuals.ForOperation(SmartConnectOperationStatus.Succeeded, "ignored", OpCaptions);
		Assert.Null(visual.Detail);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests --filter ResultVisualsTests`
Expected: FAIL — `ResultSeverity` / `ResultVisual` / `ResultVisuals` do not exist.

- [ ] **Step 3: Implement the types**

`src/Yort.Eftpos.SmartConnect.WinForms/ResultSeverity.cs`:

```csharp
namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The visual severity bucket for a rendered outcome — controls the colour cue. The exact
/// grouping is the contract (the design doc); concrete colours are chosen by the form.</summary>
internal enum ResultSeverity
{
	/// <summary>A successful outcome (Accepted / Succeeded). Rendered green.</summary>
	Success,

	/// <summary>An ambiguous outcome the caller must reconcile (Unknown). Rendered amber/prominent.</summary>
	Ambiguous,

	/// <summary>A negative or non-success outcome (Declined, Cancelled, Failed, DeviceOffline). Rendered red.</summary>
	Negative
}
```

`src/Yort.Eftpos.SmartConnect.WinForms/ResultVisual.cs`:

```csharp
namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>A resolved outcome ready to render: a caption, its severity (colour cue), and optional
/// detail text (e.g. an operation failure message).</summary>
internal readonly struct ResultVisual
{
	/// <summary>Creates a resolved outcome visual.</summary>
	public ResultVisual(string caption, ResultSeverity severity, string? detail)
	{
		Caption = caption;
		Severity = severity;
		Detail = detail;
	}

	/// <summary>The primary caption (e.g. "Approved").</summary>
	public string Caption { get; }

	/// <summary>The severity bucket controlling the colour cue.</summary>
	public ResultSeverity Severity { get; }

	/// <summary>Optional secondary detail (e.g. an operation's error message); otherwise null.</summary>
	public string? Detail { get; }
}
```

`src/Yort.Eftpos.SmartConnect.WinForms/ResultVisuals.cs`:

```csharp
using System.Collections.Generic;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Maps a core result status to a <see cref="ResultVisual"/>. Pure — no UI dependency.</summary>
internal static class ResultVisuals
{
	/// <summary>Resolves the visual for a financial transaction status.</summary>
	public static ResultVisual ForTransaction(SmartConnectTransactionStatus status, IReadOnlyDictionary<SmartConnectTransactionStatus, string> captions)
	{
		ResultSeverity severity;
		if (status == SmartConnectTransactionStatus.Accepted)
		{
			severity = ResultSeverity.Success;
		}
		else if (status == SmartConnectTransactionStatus.Unknown)
		{
			severity = ResultSeverity.Ambiguous;
		}
		else
		{
			// Declined, Cancelled, DeviceOffline, Failed — all non-success.
			severity = ResultSeverity.Negative;
		}

		return new ResultVisual(captions[status], severity, detail: null);
	}

	/// <summary>Resolves the visual for a non-financial operation status. The error message becomes the
	/// detail only for a <see cref="SmartConnectOperationStatus.Failed"/> outcome.</summary>
	public static ResultVisual ForOperation(SmartConnectOperationStatus status, string? errorMessage, IReadOnlyDictionary<SmartConnectOperationStatus, string> captions)
	{
		ResultSeverity severity;
		string? detail = null;
		if (status == SmartConnectOperationStatus.Succeeded)
		{
			severity = ResultSeverity.Success;
		}
		else if (status == SmartConnectOperationStatus.Unknown)
		{
			severity = ResultSeverity.Ambiguous;
		}
		else
		{
			severity = ResultSeverity.Negative;
			detail = errorMessage;
		}

		return new ResultVisual(captions[status], severity, detail);
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests --filter ResultVisualsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Yort.Eftpos.SmartConnect.WinForms/Result*.cs tests/Yort.Eftpos.SmartConnect.WinForms.Tests/ResultVisualsTests.cs
git commit -m "feat: result status to visual/severity mapping for WinForms dialog"
```

---

### Task 3: Default captions & progress caption resolution

**Files:**
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/DefaultCaptions.cs`
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/CaptionResolver.cs`
- Test: `tests/Yort.Eftpos.SmartConnect.WinForms.Tests/CaptionResolverTests.cs`

**Interfaces:**
- Consumes: core `SmartConnectPollingStatus` {`SmartConnectPollingState State`, `string? Message`}, `SmartConnectPollingState` {Polling, Delayed, BackingOff, NetworkError}.
- Produces:
  - `internal static class DefaultCaptions` with `Dictionary<SmartConnectPollingState, string> CreateStateCaptions()`, `Dictionary<SmartConnectTransactionStatus, string> CreateTransactionResultCaptions()`, `Dictionary<SmartConnectOperationStatus, string> CreateOperationResultCaptions()`.
  - `internal static class CaptionResolver` with `string Resolve(SmartConnectPollingStatus status, IReadOnlyDictionary<SmartConnectPollingState, string> captions)`.

- [ ] **Step 1: Write the failing tests**

`tests/Yort.Eftpos.SmartConnect.WinForms.Tests/CaptionResolverTests.cs`:

```csharp
using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class CaptionResolverTests
{
	[Fact]
	public void Resolve_PrefersLibraryMessageWhenPresent()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.Polling, Message = "Insert card" };

		Assert.Equal("Insert card", CaptionResolver.Resolve(status, captions));
	}

	// Negative/invariant: when the library supplies a message, the default caption must NOT be used.
	[Fact]
	public void Resolve_DoesNotUseDefaultWhenMessagePresent()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.Polling, Message = "Insert card" };

		Assert.NotEqual(captions[SmartConnectPollingState.Polling], CaptionResolver.Resolve(status, captions));
	}

	[Fact]
	public void Resolve_FallsBackToStateCaptionWhenMessageNull()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.Delayed, Message = null };

		Assert.Equal(captions[SmartConnectPollingState.Delayed], CaptionResolver.Resolve(status, captions));
	}

	[Fact]
	public void Resolve_TreatsEmptyMessageAsAbsent()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.BackingOff, Message = "" };

		Assert.Equal(captions[SmartConnectPollingState.BackingOff], CaptionResolver.Resolve(status, captions));
	}

	[Fact]
	public void Resolve_RespectsCustomisedCaption()
	{
		var captions = DefaultCaptions.CreateStateCaptions();
		captions[SmartConnectPollingState.NetworkError] = "Custom retry text";
		var status = new SmartConnectPollingStatus { State = SmartConnectPollingState.NetworkError, Message = null };

		Assert.Equal("Custom retry text", CaptionResolver.Resolve(status, captions));
	}

	[Fact]
	public void DefaultCaptionMaps_CoverEveryEnumValue()
	{
		var states = DefaultCaptions.CreateStateCaptions();
		Assert.True(System.Enum.GetValues<SmartConnectPollingState>().All(states.ContainsKey));

		var txn = DefaultCaptions.CreateTransactionResultCaptions();
		Assert.True(System.Enum.GetValues<SmartConnectTransactionStatus>().All(txn.ContainsKey));

		var op = DefaultCaptions.CreateOperationResultCaptions();
		Assert.True(System.Enum.GetValues<SmartConnectOperationStatus>().All(op.ContainsKey));
	}
}
```

(Add `using System.Linq;` at the top.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests --filter CaptionResolverTests`
Expected: FAIL — `DefaultCaptions` / `CaptionResolver` do not exist.

- [ ] **Step 3: Implement the captions and resolver**

`src/Yort.Eftpos.SmartConnect.WinForms/DefaultCaptions.cs`:

```csharp
using System.Collections.Generic;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Factory for the pre-populated, overridable caption maps used by the dialogs. Each call
/// returns a fresh dictionary so callers can mutate their own copy without affecting others.</summary>
internal static class DefaultCaptions
{
	/// <summary>Default progress captions per polling state (used when the library reports no message).</summary>
	public static Dictionary<SmartConnectPollingState, string> CreateStateCaptions()
	{
		return new Dictionary<SmartConnectPollingState, string>
		{
			[SmartConnectPollingState.Polling] = "Processing payment…",
			[SmartConnectPollingState.Delayed] = "Waiting for pinpad — it may be offline…",
			[SmartConnectPollingState.BackingOff] = "Busy, retrying…",
			[SmartConnectPollingState.NetworkError] = "Network problem, retrying…"
		};
	}

	/// <summary>Default outcome captions for financial transaction statuses.</summary>
	public static Dictionary<SmartConnectTransactionStatus, string> CreateTransactionResultCaptions()
	{
		return new Dictionary<SmartConnectTransactionStatus, string>
		{
			[SmartConnectTransactionStatus.Accepted] = "Approved",
			[SmartConnectTransactionStatus.Declined] = "Declined",
			[SmartConnectTransactionStatus.Cancelled] = "Cancelled",
			[SmartConnectTransactionStatus.DeviceOffline] = "Terminal offline",
			[SmartConnectTransactionStatus.Failed] = "Failed",
			[SmartConnectTransactionStatus.Unknown] = "Outcome unknown — reconcile"
		};
	}

	/// <summary>Default outcome captions for non-financial operation statuses.</summary>
	public static Dictionary<SmartConnectOperationStatus, string> CreateOperationResultCaptions()
	{
		return new Dictionary<SmartConnectOperationStatus, string>
		{
			[SmartConnectOperationStatus.Succeeded] = "Completed",
			[SmartConnectOperationStatus.Failed] = "Failed",
			[SmartConnectOperationStatus.Unknown] = "Outcome unknown — verify"
		};
	}
}
```

`src/Yort.Eftpos.SmartConnect.WinForms/CaptionResolver.cs`:

```csharp
using System.Collections.Generic;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Resolves the caption to display for a progress report: the library's own message when it
/// supplies one, otherwise the configured per-state default. Pure — no UI dependency.</summary>
internal static class CaptionResolver
{
	/// <summary>Returns <see cref="SmartConnectPollingStatus.Message"/> when non-blank; otherwise the
	/// caption mapped for the report's <see cref="SmartConnectPollingStatus.State"/>.</summary>
	public static string Resolve(SmartConnectPollingStatus status, IReadOnlyDictionary<SmartConnectPollingState, string> captions)
	{
		if (!string.IsNullOrEmpty(status.Message))
		{
			return status.Message!;
		}

		return captions[status.State];
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests --filter CaptionResolverTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Yort.Eftpos.SmartConnect.WinForms/DefaultCaptions.cs src/Yort.Eftpos.SmartConnect.WinForms/CaptionResolver.cs tests/Yort.Eftpos.SmartConnect.WinForms.Tests/CaptionResolverTests.cs
git commit -m "feat: default captions and progress caption resolution"
```

---

### Task 4: Owner-window controller

**Files:**
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/NativeMethods.cs`
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/OwnerController.cs`
- Test: `tests/Yort.Eftpos.SmartConnect.WinForms.Tests/OwnerControllerTests.cs`

**Interfaces:**
- Consumes: `System.Windows.Forms.IWin32Window`.
- Produces: `internal sealed class OwnerController` with ctor `(IWin32Window? owner, bool disableWhileBusy, Action<IntPtr, bool> setWindowEnabled)`, and methods `void Disable()` / `void Restore()`. `Restore()` is idempotent and only re-enables if `Disable()` actually disabled.

- [ ] **Step 1: Write the failing tests**

`tests/Yort.Eftpos.SmartConnect.WinForms.Tests/OwnerControllerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests --filter OwnerControllerTests`
Expected: FAIL — `OwnerController` does not exist.

- [ ] **Step 3: Implement `NativeMethods` and `OwnerController`**

`src/Yort.Eftpos.SmartConnect.WinForms/NativeMethods.cs`:

```csharp
using System;
using System.Runtime.InteropServices;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Native interop. Used to enable/disable an owner window for modal-like behaviour without a
/// blocking <c>ShowDialog</c>.</summary>
internal static class NativeMethods
{
	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

	/// <summary>Enables or disables mouse and keyboard input to the given window.</summary>
	public static void SetWindowEnabled(IntPtr handle, bool enabled)
	{
		EnableWindow(handle, enabled);
	}
}
```

`src/Yort.Eftpos.SmartConnect.WinForms/OwnerController.cs`:

```csharp
using System;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Disables an owner window while the dialog is busy and restores it afterwards, giving
/// modal-like behaviour without a thread-blocking <c>ShowDialog</c>. A null owner is a no-op (the
/// dialog simply centres on screen). The enable action is injectable for testing.</summary>
internal sealed class OwnerController
{
	private readonly IWin32Window? _owner;
	private readonly bool _disableWhileBusy;
	private readonly Action<IntPtr, bool> _setWindowEnabled;
	private bool _disabled;

	/// <summary>Creates a controller for the given owner.</summary>
	/// <param name="owner">The owner window, or null when there is none.</param>
	/// <param name="disableWhileBusy">Whether to disable the owner while busy.</param>
	/// <param name="setWindowEnabled">Action that enables/disables a window by handle.</param>
	public OwnerController(IWin32Window? owner, bool disableWhileBusy, Action<IntPtr, bool> setWindowEnabled)
	{
		_owner = owner;
		_disableWhileBusy = disableWhileBusy;
		_setWindowEnabled = setWindowEnabled;
	}

	/// <summary>Disables the owner if there is one and disabling is enabled. Idempotent.</summary>
	public void Disable()
	{
		if (_disabled)
		{
			return;
		}

		if (_owner != null && _disableWhileBusy)
		{
			_setWindowEnabled(_owner.Handle, false);
			_disabled = true;
		}
	}

	/// <summary>Re-enables the owner if (and only if) this controller disabled it. Idempotent.</summary>
	public void Restore()
	{
		if (!_disabled)
		{
			return;
		}

		_setWindowEnabled(_owner!.Handle, true);
		_disabled = false;
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests --filter OwnerControllerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Yort.Eftpos.SmartConnect.WinForms/NativeMethods.cs src/Yort.Eftpos.SmartConnect.WinForms/OwnerController.cs tests/Yort.Eftpos.SmartConnect.WinForms.Tests/OwnerControllerTests.cs
git commit -m "feat: owner-window controller for modal-like dialog behaviour"
```

---

### Task 5: Progress view interface & controller

**Files:**
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/IProgressView.cs`
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/ProgressController.cs`
- Create: `tests/Yort.Eftpos.SmartConnect.WinForms.Tests/Fakes/FakeProgressView.cs`
- Test: `tests/Yort.Eftpos.SmartConnect.WinForms.Tests/ProgressControllerTests.cs`

**Interfaces:**
- Consumes: `CaptionResolver`, core `SmartConnectPollingStatus` / `SmartConnectPollingState`, `ResultVisual`.
- Produces:
  - `internal interface IProgressView` with `void ShowBusy(string caption)`, `void UpdateCaption(string caption)`, `Task ShowResultAsync(ResultVisual visual, TimeSpan? autoCloseAfter)`.
  - `internal sealed class ProgressController` with ctor `(IProgressView view, IReadOnlyDictionary<SmartConnectPollingState, string> stateCaptions, Action onFirstShow)`, method `void Report(SmartConnectPollingStatus status)`, and `Task ShowResultAsync(ResultVisual visual, TimeSpan? autoCloseAfter)`.
  - Behaviour: first `Report` calls `onFirstShow()` then `view.ShowBusy(caption)`; subsequent reports call `view.UpdateCaption(caption)`.

- [ ] **Step 1: Write the fake view and failing tests**

`tests/Yort.Eftpos.SmartConnect.WinForms.Tests/Fakes/FakeProgressView.cs`:

```csharp
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
```

`tests/Yort.Eftpos.SmartConnect.WinForms.Tests/ProgressControllerTests.cs`:

```csharp
using System;
using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;
using Yort.Eftpos.SmartConnect.WinForms.Tests.Fakes;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class ProgressControllerTests
{
	private static ProgressController Create(FakeProgressView view, out int firstShowCount)
	{
		var counter = 0;
		var captions = DefaultCaptions.CreateStateCaptions();
		var controller = new ProgressController(view, captions, () => counter++);
		firstShowCount = 0; // replaced via closure below
		return controller;
	}

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
```

(Remove the unused `Create` helper if the implementer prefers; it is illustrative only — the three tests above are the contract.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests --filter ProgressControllerTests`
Expected: FAIL — `IProgressView` / `ProgressController` do not exist.

- [ ] **Step 3: Implement the interface and controller**

`src/Yort.Eftpos.SmartConnect.WinForms/IProgressView.cs`:

```csharp
using System;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The view surface the <see cref="ProgressController"/> drives. Implemented by the internal
/// progress form; faked in tests.</summary>
internal interface IProgressView
{
	/// <summary>Shows the busy state with the given caption (called once, on the first report).</summary>
	void ShowBusy(string caption);

	/// <summary>Updates the busy caption on a subsequent report.</summary>
	void UpdateCaption(string caption);

	/// <summary>Switches to the outcome state, returning when the operator acknowledges (OK) or the
	/// optional timeout elapses.</summary>
	Task ShowResultAsync(ResultVisual visual, TimeSpan? autoCloseAfter);
}
```

`src/Yort.Eftpos.SmartConnect.WinForms/ProgressController.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Translates progress reports into view calls: shows the dialog (once) on the first report
/// and updates the caption thereafter. UI-free; the view is an abstraction.</summary>
internal sealed class ProgressController
{
	private readonly IProgressView _view;
	private readonly IReadOnlyDictionary<SmartConnectPollingState, string> _stateCaptions;
	private readonly Action _onFirstShow;
	private bool _shown;

	/// <summary>Creates the controller.</summary>
	/// <param name="view">The view to drive.</param>
	/// <param name="stateCaptions">Per-state default captions.</param>
	/// <param name="onFirstShow">Invoked once, immediately before the dialog is first shown (used to
	/// disable the owner window).</param>
	public ProgressController(IProgressView view, IReadOnlyDictionary<SmartConnectPollingState, string> stateCaptions, Action onFirstShow)
	{
		_view = view;
		_stateCaptions = stateCaptions;
		_onFirstShow = onFirstShow;
	}

	/// <summary>Handles a progress report. Marshalling to the UI thread is the caller's responsibility
	/// (the wrapper passes this to a <see cref="System.Progress{T}"/> created on the UI thread).</summary>
	public void Report(SmartConnectPollingStatus status)
	{
		var caption = CaptionResolver.Resolve(status, _stateCaptions);
		if (!_shown)
		{
			_shown = true;
			_onFirstShow();
			_view.ShowBusy(caption);
		}
		else
		{
			_view.UpdateCaption(caption);
		}
	}

	/// <summary>Shows the outcome screen via the view.</summary>
	public Task ShowResultAsync(ResultVisual visual, TimeSpan? autoCloseAfter)
	{
		return _view.ShowResultAsync(visual, autoCloseAfter);
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests --filter ProgressControllerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Yort.Eftpos.SmartConnect.WinForms/IProgressView.cs src/Yort.Eftpos.SmartConnect.WinForms/ProgressController.cs tests/Yort.Eftpos.SmartConnect.WinForms.Tests/Fakes/FakeProgressView.cs tests/Yort.Eftpos.SmartConnect.WinForms.Tests/ProgressControllerTests.cs
git commit -m "feat: progress view interface and controller"
```

---

### Task 6: Pairing view interface & controller (loop state machine)

**Files:**
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/IPairingView.cs`
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/PairingController.cs`
- Create: `tests/Yort.Eftpos.SmartConnect.WinForms.Tests/Fakes/FakePairingView.cs`
- Test: `tests/Yort.Eftpos.SmartConnect.WinForms.Tests/PairingControllerTests.cs`

**Interfaces:**
- Consumes: core `SmartConnectPairingResult` {`bool Success`, `string? ErrorMessage`}, `SmartConnectTransportException` {`SmartConnectRequestDelivery Delivery`, `string Message`}, `SmartConnectRequestDelivery` {Unknown, NotSent}, `ResultSeverity`.
- Produces:
  - `internal interface IPairingView` with `Task<string?> GetCodeAsync()` (null = cancel), `void ShowBusy()`, `void HideBusy()`, `Task<bool> ShowFailureAsync(string message, ResultSeverity severity)` (true = retry), `Task ShowSuccessAsync(SmartConnectPairingResult result)`.
  - `internal sealed class PairingController` with `Task<SmartConnectPairingResult?> RunAsync(IPairingView view, Func<string, Task<SmartConnectPairingResult>> pairWithCode)`.

- [ ] **Step 1: Write the fake view and failing tests**

`tests/Yort.Eftpos.SmartConnect.WinForms.Tests/Fakes/FakePairingView.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Yort.Eftpos.SmartConnect;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests.Fakes;

/// <summary>Scripts a sequence of operator interactions so the controller's loop can be driven
/// deterministically. Each GetCodeAsync call dequeues the next scripted code (null = cancel); each
/// ShowFailureAsync dequeues the next retry decision (true = retry).</summary>
internal sealed class FakePairingView : IPairingView
{
	private readonly Queue<string?> _codes;
	private readonly Queue<bool> _retryDecisions;

	public FakePairingView(IEnumerable<string?> codes, IEnumerable<bool> retryDecisions)
	{
		_codes = new Queue<string?>(codes);
		_retryDecisions = new Queue<bool>(retryDecisions);
	}

	public int BusyShownCount { get; private set; }
	public List<(string message, ResultSeverity severity)> Failures { get; } = new();
	public SmartConnectPairingResult? SuccessShown { get; private set; }

	public Task<string?> GetCodeAsync()
	{
		return Task.FromResult(_codes.Count > 0 ? _codes.Dequeue() : null);
	}

	public void ShowBusy()
	{
		BusyShownCount++;
	}

	public void HideBusy()
	{
	}

	public Task<bool> ShowFailureAsync(string message, ResultSeverity severity)
	{
		Failures.Add((message, severity));
		return Task.FromResult(_retryDecisions.Count > 0 && _retryDecisions.Dequeue());
	}

	public Task ShowSuccessAsync(SmartConnectPairingResult result)
	{
		SuccessShown = result;
		return Task.CompletedTask;
	}
}
```

`tests/Yort.Eftpos.SmartConnect.WinForms.Tests/PairingControllerTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Xunit;
using Yort.Eftpos.SmartConnect;
using Yort.Eftpos.SmartConnect.WinForms;
using Yort.Eftpos.SmartConnect.WinForms.Tests.Fakes;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

public class PairingControllerTests
{
	private static Func<string, Task<SmartConnectPairingResult>> Counting(out Counter counter, SmartConnectPairingResult result)
	{
		var c = new Counter();
		counter = c;
		return code =>
		{
			c.Calls++;
			c.LastCode = code;
			return Task.FromResult(result);
		};
	}

	private sealed class Counter
	{
		public int Calls;
		public string? LastCode;
	}

	[Fact]
	public async Task Cancel_AtFirstPrompt_ReturnsNull_AndNeverCallsCallback()
	{
		var view = new FakePairingView(new string?[] { null }, Array.Empty<bool>());
		var callback = Counting(out var counter, new SmartConnectPairingResult { Success = true });

		var result = await new PairingController().RunAsync(view, callback);

		Assert.Null(result);
		Assert.Equal(0, counter.Calls);   // cancel never pairs
	}

	[Fact]
	public async Task BlankCode_NeverCallsCallback_AndReprompts()
	{
		// First a blank code (must be ignored), then cancel.
		var view = new FakePairingView(new string?[] { "   ", null }, Array.Empty<bool>());
		var callback = Counting(out var counter, new SmartConnectPairingResult { Success = true });

		var result = await new PairingController().RunAsync(view, callback);

		Assert.Null(result);
		Assert.Equal(0, counter.Calls);   // blank code must not reach the callback
	}

	[Fact]
	public async Task SuccessfulCode_ShowsSuccess_ReturnsResult()
	{
		var view = new FakePairingView(new string?[] { "1234" }, Array.Empty<bool>());
		var expected = new SmartConnectPairingResult { Success = true };
		var callback = Counting(out var counter, expected);

		var result = await new PairingController().RunAsync(view, callback);

		Assert.Same(expected, result);
		Assert.Equal(1, counter.Calls);
		Assert.Equal("1234", counter.LastCode);   // trimmed/forwarded
		Assert.Same(expected, view.SuccessShown);
	}

	[Fact]
	public async Task ServiceRejection_ShowsNegativeFailure_RetriesThenSucceeds()
	{
		var view = new FakePairingView(new string?[] { "bad", "good" }, new[] { true });
		var calls = 0;
		Func<string, Task<SmartConnectPairingResult>> callback = code =>
		{
			calls++;
			return Task.FromResult(code == "good"
				? new SmartConnectPairingResult { Success = true }
				: new SmartConnectPairingResult { Success = false, ErrorMessage = "Invalid code" });
		};

		var result = await new PairingController().RunAsync(view, callback);

		Assert.True(result!.Success);
		Assert.Equal(2, calls);
		Assert.Single(view.Failures);
		Assert.Equal(ResultSeverity.Negative, view.Failures[0].severity);
		Assert.Equal("Invalid code", view.Failures[0].message);
	}

	[Fact]
	public async Task TransportException_IsCaught_RenderedAmber_AndRetryable()
	{
		var view = new FakePairingView(new string?[] { "1234", "1234" }, new[] { false });
		var attempt = 0;
		Func<string, Task<SmartConnectPairingResult>> callback = code =>
		{
			attempt++;
			if (attempt == 1)
			{
				throw new SmartConnectTransportException(SmartConnectRequestDelivery.NotSent, new Exception("boom"));
			}

			return Task.FromResult(new SmartConnectPairingResult { Success = true });
		};

		var result = await new PairingController().RunAsync(view, callback);

		Assert.Null(result);   // operator chose not to retry
		Assert.Single(view.Failures);
		Assert.Equal(ResultSeverity.Ambiguous, view.Failures[0].severity);   // transport failure → amber
	}

	[Fact]
	public async Task NonTransportException_Propagates()
	{
		var view = new FakePairingView(new string?[] { "1234" }, Array.Empty<bool>());
		Func<string, Task<SmartConnectPairingResult>> callback = code => throw new InvalidOperationException("bug");

		await Assert.ThrowsAsync<InvalidOperationException>(() => new PairingController().RunAsync(view, callback));
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests --filter PairingControllerTests`
Expected: FAIL — `IPairingView` / `PairingController` do not exist.

- [ ] **Step 3: Implement the interface and controller**

`src/Yort.Eftpos.SmartConnect.WinForms/IPairingView.cs`:

```csharp
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The view surface the <see cref="PairingController"/> drives. Implemented by the internal
/// pairing form; faked in tests.</summary>
internal interface IPairingView
{
	/// <summary>Prompts for a pairing code; returns the entered code, or null if the operator cancelled.</summary>
	Task<string?> GetCodeAsync();

	/// <summary>Shows the busy state while a pairing attempt is in flight.</summary>
	void ShowBusy();

	/// <summary>Hides the busy state.</summary>
	void HideBusy();

	/// <summary>Shows a failure with its severity; returns true if the operator chose to retry, false to cancel.</summary>
	Task<bool> ShowFailureAsync(string message, ResultSeverity severity);

	/// <summary>Shows the success state and returns when the operator acknowledges it.</summary>
	Task ShowSuccessAsync(SmartConnectPairingResult result);
}
```

`src/Yort.Eftpos.SmartConnect.WinForms/PairingController.cs`:

```csharp
using System;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Drives the pairing interaction loop — prompt, attempt, present, retry-or-cancel — over an
/// <see cref="IPairingView"/>, invoking a caller-supplied callback to perform each attempt. UI-free.
/// Catches <see cref="SmartConnectTransportException"/> (the only failure the core's PairAsync throws)
/// and renders it as a retryable, ambiguous failure; other exceptions propagate.</summary>
internal sealed class PairingController
{
	/// <summary>Runs the loop. Returns the successful result, or null if the operator cancelled.</summary>
	public async Task<SmartConnectPairingResult?> RunAsync(IPairingView view, Func<string, Task<SmartConnectPairingResult>> pairWithCode)
	{
		while (true)
		{
			var entered = await view.GetCodeAsync().ConfigureAwait(true);
			if (entered == null)
			{
				return null;
			}

			var code = entered.Trim();
			if (code.Length == 0)
			{
				// Never send a blank code to the callback (it would trigger the core's ArgumentException).
				continue;
			}

			SmartConnectPairingResult result;
			try
			{
				view.ShowBusy();
				result = await pairWithCode(code).ConfigureAwait(true);
			}
			catch (SmartConnectTransportException ex)
			{
				// Transport failure: the message is already operator-appropriate and URL-free; NotSent and
				// Unknown both render amber (ambiguous) — neither is a clean "declined".
				if (await view.ShowFailureAsync(ex.Message, ResultSeverity.Ambiguous).ConfigureAwait(true))
				{
					continue;
				}

				return null;
			}
			finally
			{
				view.HideBusy();
			}

			if (result.Success)
			{
				await view.ShowSuccessAsync(result).ConfigureAwait(true);
				return result;
			}

			var message = string.IsNullOrEmpty(result.ErrorMessage) ? "Pairing failed." : result.ErrorMessage!;
			if (await view.ShowFailureAsync(message, ResultSeverity.Negative).ConfigureAwait(true))
			{
				continue;
			}

			return null;
		}
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests --filter PairingControllerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Yort.Eftpos.SmartConnect.WinForms/IPairingView.cs src/Yort.Eftpos.SmartConnect.WinForms/PairingController.cs tests/Yort.Eftpos.SmartConnect.WinForms.Tests/Fakes/FakePairingView.cs tests/Yort.Eftpos.SmartConnect.WinForms.Tests/PairingControllerTests.cs
git commit -m "feat: pairing loop state machine (controller + view interface)"
```

---

### Task 7: Shared dialog chrome

**Files:**
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/DialogChrome.cs`

**Interfaces:**
- Consumes: `System.Windows.Forms.Form`, `System.Windows.Forms.PictureBox`, `System.Drawing`.
- Produces: `internal sealed class DialogChrome` holding `WindowTitle`, `Logo`, `BackgroundColour`, `ForegroundColour`, `Font?`, `DisableOwnerWhileBusy`, with `void ApplyTo(Form form, PictureBox? logoBox)`.

This task has no unit test — it only sets `Form`/`PictureBox` properties (verified via the sample in Task 10). It is small and self-contained.

- [ ] **Step 1: Implement `DialogChrome`**

`src/Yort.Eftpos.SmartConnect.WinForms/DialogChrome.cs`:

```csharp
using System.Drawing;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Holds the shared appearance settings for the dialogs and applies them to a form. Shared by
/// composition rather than an internal base class, because a public dialog type cannot derive from an
/// internal one (CS0060).</summary>
internal sealed class DialogChrome
{
	/// <summary>The window title. Defaults to "EFTPOS".</summary>
	public string WindowTitle { get; set; } = "EFTPOS";

	/// <summary>An optional logo image.</summary>
	public Image? Logo { get; set; }

	/// <summary>The dialog background colour.</summary>
	public Color BackgroundColour { get; set; } = SystemColors.Window;

	/// <summary>The dialog foreground (text) colour.</summary>
	public Color ForegroundColour { get; set; } = SystemColors.ControlText;

	/// <summary>The dialog font; null leaves the form default.</summary>
	public Font? Font { get; set; }

	/// <summary>Whether to disable the owner window while the dialog is busy. Defaults to true.</summary>
	public bool DisableOwnerWhileBusy { get; set; } = true;

	/// <summary>Applies the current settings to the form (and its logo box, if any).</summary>
	public void ApplyTo(Form form, PictureBox? logoBox)
	{
		form.Text = WindowTitle;
		form.BackColor = BackgroundColour;
		form.ForeColor = ForegroundColour;
		if (Font != null)
		{
			form.Font = Font;
		}

		if (logoBox != null)
		{
			logoBox.Image = Logo;
			logoBox.Visible = Logo != null;
		}
	}
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Yort.Eftpos.SmartConnect.WinForms/Yort.Eftpos.SmartConnect.WinForms.csproj`
Expected: succeeds, zero warnings.

- [ ] **Step 3: Commit**

```bash
git add src/Yort.Eftpos.SmartConnect.WinForms/DialogChrome.cs
git commit -m "feat: shared dialog chrome (appearance application)"
```

---

### Task 8: Progress form & public `SmartConnectProgressDialog`

**Files:**
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/ProgressForm.cs`
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/SmartConnectProgressDialog.cs`

**Interfaces:**
- Consumes: `IProgressView`, `ProgressController`, `OwnerController`, `DialogChrome`, `ResultVisuals`, `DefaultCaptions`, core result types and `IProgress<SmartConnectPollingStatus>`.
- Produces: public `SmartConnectProgressDialog : IDisposable` per the design's §2a signature.

This task is hand-verified (no automated UI test) — the logic it relies on is covered by Tasks 2–5.

- [ ] **Step 1: Implement the internal form**

`src/Yort.Eftpos.SmartConnect.WinForms/ProgressForm.cs`:

```csharp
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The internal form implementing <see cref="IProgressView"/>. Hand-coded layout (no designer
/// file): a logo, a caption label, an indeterminate marquee progress bar (busy), and an outcome panel
/// with a coloured caption, optional detail, and an OK button.</summary>
internal sealed class ProgressForm : Form, IProgressView
{
	private readonly PictureBox _logo;
	private readonly Label _caption;
	private readonly ProgressBar _busy;
	private readonly Panel _resultPanel;
	private readonly Label _resultCaption;
	private readonly Label _resultDetail;
	private readonly Button _ok;
	private System.Windows.Forms.Timer? _autoClose;
	private TaskCompletionSource<bool>? _resultAck;

	public ProgressForm()
	{
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		ControlBox = false;
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(360, 160);

		_logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(12, 12, 64, 64), Visible = false };
		_caption = new Label { Bounds = new Rectangle(12, 84, 336, 28), TextAlign = ContentAlignment.MiddleCenter };
		_busy = new ProgressBar { Style = ProgressBarStyle.Marquee, Bounds = new Rectangle(12, 120, 336, 16) };

		_resultCaption = new Label { Bounds = new Rectangle(12, 24, 336, 40), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, 16, FontStyle.Bold) };
		_resultDetail = new Label { Bounds = new Rectangle(12, 68, 336, 36), TextAlign = ContentAlignment.MiddleCenter };
		_ok = new Button { Text = "OK", Bounds = new Rectangle(140, 112, 80, 30), DialogResult = DialogResult.OK };
		_ok.Click += (_, _) => CompleteResult();
		_resultPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
		_resultPanel.Controls.Add(_resultCaption);
		_resultPanel.Controls.Add(_resultDetail);
		_resultPanel.Controls.Add(_ok);

		Controls.Add(_logo);
		Controls.Add(_caption);
		Controls.Add(_busy);
		Controls.Add(_resultPanel);
	}

	public PictureBox LogoBox => _logo;

	public void ShowBusy(string caption)
	{
		_resultPanel.Visible = false;
		_logo.Visible = _logo.Image != null;
		_caption.Visible = true;
		_busy.Visible = true;
		_caption.Text = caption;
		if (!Visible)
		{
			Show();
		}

		BringToFront();
		Activate();
	}

	public void UpdateCaption(string caption)
	{
		_caption.Text = caption;
	}

	public Task ShowResultAsync(ResultVisual visual, TimeSpan? autoCloseAfter)
	{
		if (!Visible)
		{
			Show();
		}

		_caption.Visible = false;
		_busy.Visible = false;
		_resultCaption.Text = visual.Caption;
		_resultCaption.ForeColor = SeverityColour(visual.Severity);
		_resultDetail.Text = visual.Detail ?? string.Empty;
		_resultPanel.Visible = true;
		_resultPanel.BringToFront();
		_ok.Focus();

		_resultAck = new TaskCompletionSource<bool>();
		if (autoCloseAfter.HasValue)
		{
			_autoClose = new System.Windows.Forms.Timer { Interval = (int)autoCloseAfter.Value.TotalMilliseconds };
			_autoClose.Tick += (_, _) => CompleteResult();
			_autoClose.Start();
		}

		return _resultAck.Task;
	}

	private void CompleteResult()
	{
		_autoClose?.Stop();
		_autoClose?.Dispose();
		_autoClose = null;
		_resultAck?.TrySetResult(true);
	}

	private static Color SeverityColour(ResultSeverity severity)
	{
		if (severity == ResultSeverity.Success)
		{
			return Color.ForestGreen;
		}

		if (severity == ResultSeverity.Ambiguous)
		{
			return Color.DarkGoldenrod;
		}

		return Color.Firebrick;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_autoClose?.Dispose();
			_resultAck?.TrySetResult(false);
		}

		base.Dispose(disposing);
	}
}
```

- [ ] **Step 2: Implement the public dialog**

`src/Yort.Eftpos.SmartConnect.WinForms/SmartConnectProgressDialog.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>A reusable WinForms dialog that shows progress while a SmartConnect operation runs and
/// optionally presents its outcome. Construct it on the UI thread (it captures the current
/// <see cref="System.Threading.SynchronizationContext"/> for progress marshalling). Pass
/// <see cref="Progress"/> into a client call; the dialog auto-shows on the first report and closes on
/// <see cref="Dispose"/>. Call a <c>ShowResultAsync</c> overload to present the outcome, or omit it to
/// suppress the outcome screen.</summary>
public sealed class SmartConnectProgressDialog : IDisposable
{
	private readonly ProgressForm _form;
	private readonly DialogChrome _chrome = new DialogChrome();
	private readonly OwnerController _owner;
	private readonly ProgressController _controller;
	private readonly IProgress<SmartConnectPollingStatus> _progress;
	private readonly IDictionary<SmartConnectPollingState, string> _stateCaptions = DefaultCaptions.CreateStateCaptions();
	private bool _appearanceApplied;

	/// <summary>Creates an owner-less dialog (centres on screen).</summary>
	public SmartConnectProgressDialog()
		: this(null)
	{
	}

	/// <summary>Creates a dialog owned by <paramref name="owner"/> (centres on it; disables it while busy).</summary>
	public SmartConnectProgressDialog(IWin32Window? owner)
	{
		_form = new ProgressForm();
		_owner = new OwnerController(owner, _chrome.DisableOwnerWhileBusy, NativeMethods.SetWindowEnabled);
		_controller = new ProgressController(_form, (IReadOnlyDictionary<SmartConnectPollingState, string>)_stateCaptions, OnFirstShow);
		_progress = new Progress<SmartConnectPollingStatus>(_controller.Report);
		TransactionResultCaptions = DefaultCaptions.CreateTransactionResultCaptions();
		OperationResultCaptions = DefaultCaptions.CreateOperationResultCaptions();
	}

	/// <summary>The progress sink to pass into a client operation.</summary>
	public IProgress<SmartConnectPollingStatus> Progress => _progress;

	/// <summary>The window title (default "EFTPOS").</summary>
	public string WindowTitle { get => _chrome.WindowTitle; set => _chrome.WindowTitle = value; }

	/// <summary>An optional logo image.</summary>
	public Image? Logo { get => _chrome.Logo; set => _chrome.Logo = value; }

	/// <summary>The background colour.</summary>
	public Color BackgroundColour { get => _chrome.BackgroundColour; set => _chrome.BackgroundColour = value; }

	/// <summary>The foreground (text) colour.</summary>
	public Color ForegroundColour { get => _chrome.ForegroundColour; set => _chrome.ForegroundColour = value; }

	/// <summary>The dialog font.</summary>
	public Font? Font { get => _chrome.Font; set => _chrome.Font = value; }

	/// <summary>Whether to disable the owner window while busy (default true).</summary>
	public bool DisableOwnerWhileBusy { get => _chrome.DisableOwnerWhileBusy; set => _chrome.DisableOwnerWhileBusy = value; }

	/// <summary>Overridable progress captions per polling state (pre-populated with defaults).</summary>
	public IDictionary<SmartConnectPollingState, string> StateCaptions => _stateCaptions;

	/// <summary>Overridable outcome captions per transaction status (pre-populated with defaults).</summary>
	public IDictionary<SmartConnectTransactionStatus, string> TransactionResultCaptions { get; }

	/// <summary>Overridable outcome captions per operation status (pre-populated with defaults).</summary>
	public IDictionary<SmartConnectOperationStatus, string> OperationResultCaptions { get; }

	/// <summary>Shows the outcome of a financial transaction and returns when the operator acknowledges it.</summary>
	public Task ShowResultAsync(SmartConnectTransactionResult result)
	{
		return ShowResultAsync(result, autoCloseAfter: null);
	}

	/// <summary>Shows the outcome of a financial transaction, auto-closing after the given delay if the
	/// operator does not acknowledge it first.</summary>
	public Task ShowResultAsync(SmartConnectTransactionResult result, TimeSpan autoCloseAfter)
	{
		return ShowResultAsync(result, (TimeSpan?)autoCloseAfter);
	}

	/// <summary>Shows the outcome of a non-financial operation and returns when the operator acknowledges it.</summary>
	public Task ShowResultAsync(SmartConnectOperationResult result)
	{
		return ShowResultAsync(result, autoCloseAfter: null);
	}

	/// <summary>Shows the outcome of a non-financial operation, auto-closing after the given delay if the
	/// operator does not acknowledge it first.</summary>
	public Task ShowResultAsync(SmartConnectOperationResult result, TimeSpan autoCloseAfter)
	{
		return ShowResultAsync(result, (TimeSpan?)autoCloseAfter);
	}

	private Task ShowResultAsync(SmartConnectTransactionResult result, TimeSpan? autoCloseAfter)
	{
		if (result == null)
		{
			throw new ArgumentNullException(nameof(result));
		}

		EnsureAppearanceAndOwner();
		var visual = ResultVisuals.ForTransaction(result.Status, (IReadOnlyDictionary<SmartConnectTransactionStatus, string>)TransactionResultCaptions);
		return _controller.ShowResultAsync(visual, autoCloseAfter);
	}

	private Task ShowResultAsync(SmartConnectOperationResult result, TimeSpan? autoCloseAfter)
	{
		if (result == null)
		{
			throw new ArgumentNullException(nameof(result));
		}

		EnsureAppearanceAndOwner();
		var visual = ResultVisuals.ForOperation(result.Status, result.ErrorMessage, (IReadOnlyDictionary<SmartConnectOperationStatus, string>)OperationResultCaptions);
		return _controller.ShowResultAsync(visual, autoCloseAfter);
	}

	private void OnFirstShow()
	{
		EnsureAppearanceAndOwner();
	}

	private void EnsureAppearanceAndOwner()
	{
		if (_appearanceApplied)
		{
			return;
		}

		_appearanceApplied = true;
		_chrome.ApplyTo(_form, _form.LogoBox);
		_owner.Disable();
	}

	/// <summary>Closes the dialog and re-enables the owner window.</summary>
	public void Dispose()
	{
		_owner.Restore();
		_form.Dispose();
	}
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Yort.Eftpos.SmartConnect.WinForms/Yort.Eftpos.SmartConnect.WinForms.csproj`
Expected: succeeds for both TFMs, zero warnings.

- [ ] **Step 4: Re-run the full test suite (no regressions)**

Run: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests`
Expected: all prior tests still PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Yort.Eftpos.SmartConnect.WinForms/ProgressForm.cs src/Yort.Eftpos.SmartConnect.WinForms/SmartConnectProgressDialog.cs
git commit -m "feat: progress form and public SmartConnectProgressDialog"
```

---

### Task 9: Pairing form & public `SmartConnectPairingDialog`

**Files:**
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/PairingForm.cs`
- Create: `src/Yort.Eftpos.SmartConnect.WinForms/SmartConnectPairingDialog.cs`

**Interfaces:**
- Consumes: `IPairingView`, `PairingController`, `OwnerController`, `DialogChrome`, core `SmartConnectPairingResult`.
- Produces: public `SmartConnectPairingDialog : IDisposable` per the design's §2b signature, with `Task<SmartConnectPairingResult?> ShowAsync(Func<string, Task<SmartConnectPairingResult>> pairWithCode)`.

This task is hand-verified (no automated UI test) — the loop logic is covered by Task 6.

- [ ] **Step 1: Implement the internal pairing form**

`src/Yort.Eftpos.SmartConnect.WinForms/PairingForm.cs`:

```csharp
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The internal form implementing <see cref="IPairingView"/>. Hand-coded layout: a logo, a
/// prompt label, a code textbox with Pair/Cancel, a busy spinner, and a failure panel with a coloured
/// message and Try-again/Cancel. Success is shown briefly with an OK button.</summary>
internal sealed class PairingForm : Form, IPairingView
{
	private readonly PictureBox _logo;
	private readonly Label _prompt;
	private readonly TextBox _code;
	private readonly Button _pair;
	private readonly Button _cancel;
	private readonly ProgressBar _busy;
	private readonly Label _message;
	private readonly Button _retry;
	private readonly Button _cancel2;
	private readonly Button _ok;

	private TaskCompletionSource<string?>? _codeResult;
	private TaskCompletionSource<bool>? _failureResult;
	private TaskCompletionSource<bool>? _successAck;

	public PairingForm()
	{
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(380, 200);

		_logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(12, 12, 56, 56), Visible = false };
		_prompt = new Label { Bounds = new Rectangle(12, 76, 356, 24), Text = "Enter the pairing code shown on the terminal:" };
		_code = new TextBox { Bounds = new Rectangle(12, 104, 356, 24) };
		_code.TextChanged += (_, _) => _pair!.Enabled = _code.Text.Trim().Length > 0;
		_pair = new Button { Text = "Pair", Bounds = new Rectangle(206, 140, 76, 30), Enabled = false };
		_cancel = new Button { Text = "Cancel", Bounds = new Rectangle(292, 140, 76, 30) };
		_busy = new ProgressBar { Style = ProgressBarStyle.Marquee, Bounds = new Rectangle(12, 140, 180, 16), Visible = false };

		_message = new Label { Bounds = new Rectangle(12, 76, 356, 52), Visible = false, Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };
		_retry = new Button { Text = "Try again", Bounds = new Rectangle(206, 140, 76, 30), Visible = false };
		_cancel2 = new Button { Text = "Cancel", Bounds = new Rectangle(292, 140, 76, 30), Visible = false };
		_ok = new Button { Text = "OK", Bounds = new Rectangle(292, 140, 76, 30), Visible = false };

		_pair.Click += (_, _) => _codeResult?.TrySetResult(_code.Text);
		_cancel.Click += (_, _) => _codeResult?.TrySetResult(null);
		_retry.Click += (_, _) => _failureResult?.TrySetResult(true);
		_cancel2.Click += (_, _) => _failureResult?.TrySetResult(false);
		_ok.Click += (_, _) => _successAck?.TrySetResult(true);

		Controls.AddRange(new Control[] { _logo, _prompt, _code, _pair, _cancel, _busy, _message, _retry, _cancel2, _ok });
	}

	public PictureBox LogoBox => _logo;

	public string PromptText
	{
		get => _prompt.Text;
		set => _prompt.Text = value;
	}

	public Task<string?> GetCodeAsync()
	{
		ShowPromptControls();
		if (!Visible)
		{
			Show();
		}

		BringToFront();
		Activate();
		_code.Focus();
		_codeResult = new TaskCompletionSource<string?>();
		return _codeResult.Task;
	}

	public void ShowBusy()
	{
		_pair.Enabled = false;
		_cancel.Enabled = false;
		_busy.Visible = true;
	}

	public void HideBusy()
	{
		_busy.Visible = false;
		_cancel.Enabled = true;
	}

	public Task<bool> ShowFailureAsync(string message, ResultSeverity severity)
	{
		HidePromptControls();
		_message.Text = message;
		_message.ForeColor = severity == ResultSeverity.Ambiguous ? Color.DarkGoldenrod : Color.Firebrick;
		_message.Visible = true;
		_retry.Visible = true;
		_cancel2.Visible = true;
		_ok.Visible = false;
		_retry.Focus();
		_failureResult = new TaskCompletionSource<bool>();
		return _failureResult.Task;
	}

	public Task ShowSuccessAsync(SmartConnectPairingResult result)
	{
		HidePromptControls();
		_message.Text = "Paired";
		_message.ForeColor = Color.ForestGreen;
		_message.Visible = true;
		_retry.Visible = false;
		_cancel2.Visible = false;
		_ok.Visible = true;
		_ok.Focus();
		_successAck = new TaskCompletionSource<bool>();
		return _successAck.Task;
	}

	private void ShowPromptControls()
	{
		_prompt.Visible = true;
		_code.Visible = true;
		_pair.Visible = true;
		_cancel.Visible = true;
		_pair.Enabled = _code.Text.Trim().Length > 0;
		_cancel.Enabled = true;
		_message.Visible = false;
		_retry.Visible = false;
		_cancel2.Visible = false;
		_ok.Visible = false;
		_busy.Visible = false;
	}

	private void HidePromptControls()
	{
		_prompt.Visible = false;
		_code.Visible = false;
		_pair.Visible = false;
		_cancel.Visible = false;
		_busy.Visible = false;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_codeResult?.TrySetResult(null);
			_failureResult?.TrySetResult(false);
			_successAck?.TrySetResult(true);
		}

		base.Dispose(disposing);
	}
}
```

- [ ] **Step 2: Implement the public pairing dialog**

`src/Yort.Eftpos.SmartConnect.WinForms/SmartConnectPairingDialog.cs`:

```csharp
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>A reusable WinForms dialog that onboards a terminal: it prompts for the pairing code, runs
/// the pairing attempt via a caller-supplied callback, presents the result, and lets the operator retry
/// a bad code or cancel. Construct it on the UI thread. The dialog depends on no client type — only on
/// the callback that turns an entered code into a <see cref="SmartConnectPairingResult"/>.</summary>
public sealed class SmartConnectPairingDialog : IDisposable
{
	private readonly PairingForm _form;
	private readonly DialogChrome _chrome = new DialogChrome { WindowTitle = "Pair Terminal" };
	private readonly OwnerController _owner;
	private readonly PairingController _controller = new PairingController();
	private bool _appearanceApplied;

	/// <summary>Creates an owner-less dialog (centres on screen).</summary>
	public SmartConnectPairingDialog()
		: this(null)
	{
	}

	/// <summary>Creates a dialog owned by <paramref name="owner"/> (centres on it; disables it while busy).</summary>
	public SmartConnectPairingDialog(IWin32Window? owner)
	{
		_form = new PairingForm();
		_owner = new OwnerController(owner, _chrome.DisableOwnerWhileBusy, NativeMethods.SetWindowEnabled);
	}

	/// <summary>The window title (default "Pair Terminal").</summary>
	public string WindowTitle { get => _chrome.WindowTitle; set => _chrome.WindowTitle = value; }

	/// <summary>An optional logo image.</summary>
	public Image? Logo { get => _chrome.Logo; set => _chrome.Logo = value; }

	/// <summary>The background colour.</summary>
	public Color BackgroundColour { get => _chrome.BackgroundColour; set => _chrome.BackgroundColour = value; }

	/// <summary>The foreground (text) colour.</summary>
	public Color ForegroundColour { get => _chrome.ForegroundColour; set => _chrome.ForegroundColour = value; }

	/// <summary>The dialog font.</summary>
	public Font? Font { get => _chrome.Font; set => _chrome.Font = value; }

	/// <summary>Whether to disable the owner window while busy (default true).</summary>
	public bool DisableOwnerWhileBusy { get => _chrome.DisableOwnerWhileBusy; set => _chrome.DisableOwnerWhileBusy = value; }

	/// <summary>The prompt text shown above the code field.</summary>
	public string Prompt { get => _form.PromptText; set => _form.PromptText = value; }

	/// <summary>Runs the pairing flow. Returns the successful result, or null if the operator cancelled.
	/// The callback is invoked with the (non-blank, trimmed) entered code for each attempt.</summary>
	public async Task<SmartConnectPairingResult?> ShowAsync(Func<string, Task<SmartConnectPairingResult>> pairWithCode)
	{
		if (pairWithCode == null)
		{
			throw new ArgumentNullException(nameof(pairWithCode));
		}

		EnsureAppearanceAndOwner();
		try
		{
			return await _controller.RunAsync(_form, pairWithCode).ConfigureAwait(true);
		}
		finally
		{
			_owner.Restore();
		}
	}

	private void EnsureAppearanceAndOwner()
	{
		if (_appearanceApplied)
		{
			return;
		}

		_appearanceApplied = true;
		_chrome.ApplyTo(_form, _form.LogoBox);
		_owner.Disable();
	}

	/// <summary>Closes the dialog and re-enables the owner window.</summary>
	public void Dispose()
	{
		_owner.Restore();
		_form.Dispose();
	}
}
```

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build src/Yort.Eftpos.SmartConnect.WinForms/Yort.Eftpos.SmartConnect.WinForms.csproj`
Then: `dotnet test tests/Yort.Eftpos.SmartConnect.WinForms.Tests`
Expected: build succeeds (both TFMs, zero warnings); all tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Yort.Eftpos.SmartConnect.WinForms/PairingForm.cs src/Yort.Eftpos.SmartConnect.WinForms/SmartConnectPairingDialog.cs
git commit -m "feat: pairing form and public SmartConnectPairingDialog"
```

---

### Task 10: WinForms sample app (manual smoke harness)

**Files:**
- Create: `samples/Yort.Eftpos.SmartConnect.WinFormsDemo/Yort.Eftpos.SmartConnect.WinFormsDemo.csproj`
- Create: `samples/Yort.Eftpos.SmartConnect.WinFormsDemo/Program.cs`
- Create: `samples/Yort.Eftpos.SmartConnect.WinFormsDemo/MainForm.cs`
- Modify: the solution file (add the sample)

**Interfaces:**
- Consumes: `SmartConnectProgressDialog`, `SmartConnectPairingDialog`, and the core `SmartConnectClient`.
- Produces: a runnable Windows app for manual verification against a dev terminal.

- [ ] **Step 1: Create the sample csproj**

`samples/Yort.Eftpos.SmartConnect.WinFormsDemo/Yort.Eftpos.SmartConnect.WinFormsDemo.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Yort.Eftpos.SmartConnect.WinForms\Yort.Eftpos.SmartConnect.WinForms.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `Program.cs`**

`samples/Yort.Eftpos.SmartConnect.WinFormsDemo/Program.cs`:

```csharp
using System;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinFormsDemo;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.Run(new MainForm());
	}
}
```

- [ ] **Step 3: Create `MainForm.cs`**

`samples/Yort.Eftpos.SmartConnect.WinFormsDemo/MainForm.cs`:

```csharp
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Yort.Eftpos.SmartConnect;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinFormsDemo;

/// <summary>Manual smoke harness for the WinForms dialogs. Fill in BaseUrl/state store and the
/// registration triple for your dev environment before running against a real terminal.</summary>
internal sealed class MainForm : Form
{
	private readonly Button _pair = new Button { Text = "Pair…", Bounds = new Rectangle(20, 20, 160, 40) };
	private readonly Button _purchase = new Button { Text = "Purchase $1.00", Bounds = new Rectangle(20, 70, 160, 40) };

	public MainForm()
	{
		Text = "SmartConnect WinForms Demo";
		ClientSize = new Size(220, 130);
		Controls.Add(_pair);
		Controls.Add(_purchase);
		_pair.Click += async (_, _) => await PairAsync();
		_purchase.Click += async (_, _) => await PurchaseAsync();
	}

	private SmartConnectClient CreateClient()
	{
		// TODO (manual): supply a real dev configuration (BaseUrl, StateStore) before running.
		var configuration = new SmartConnectClientConfiguration
		{
			BaseUrl = SmartConnectEnvironments.Development,
			StateStore = new FileBasedTransactionStateStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SmartConnectWinFormsDemo"))
		};
		return new SmartConnectClient(configuration);
	}

	private async Task PairAsync()
	{
		using var client = CreateClient();
		using var dialog = new SmartConnectPairingDialog(this) { WindowTitle = "Pair Terminal" };

		var request = new SmartConnectPairingRequest
		{
			POSRegisterID = SmartConnectRegisterId.Generate("DemoMerchant", "Register-01"),
			POSBusinessName = "Demo Store",
			POSVendorName = "WinFormsDemo",
			POSRegisterName = "Front Counter"
		};

		var result = await dialog.ShowAsync(code => client.PairAsync(code, request));
		MessageBox.Show(result is null ? "Cancelled" : (result.Success ? "Paired" : "Failed: " + result.ErrorMessage));
	}

	private async Task PurchaseAsync()
	{
		using var client = CreateClient();
		using var dialog = new SmartConnectProgressDialog(this) { WindowTitle = "EFTPOS" };

		var request = new SmartConnectTransactionRequest
		{
			TransactionType = SmartConnectTransactionType.CardPurchase,
			AmountTotal = Money.FromDecimal(1.00m),
			POSRegisterID = SmartConnectRegisterId.Generate("DemoMerchant", "Register-01"),
			POSBusinessName = "Demo Store",
			POSVendorName = "WinFormsDemo",
			ClientTransactionRef = "demo-" + Guid.NewGuid().ToString("N")
		};

		var result = await client.ProcessTransactionAsync(request, dialog.Progress);
		await dialog.ShowResultAsync(result, TimeSpan.FromSeconds(5));
	}
}
```

- [ ] **Step 4: Add to the solution and build**

Run: `dotnet sln add samples/Yort.Eftpos.SmartConnect.WinFormsDemo/Yort.Eftpos.SmartConnect.WinFormsDemo.csproj`
Then: `dotnet build samples/Yort.Eftpos.SmartConnect.WinFormsDemo/Yort.Eftpos.SmartConnect.WinFormsDemo.csproj`
Expected: builds, zero warnings.

- [ ] **Step 5: Manual smoke (record the result)**

Run the app from the IDE (or `dotnet run --project samples/Yort.Eftpos.SmartConnect.WinFormsDemo`). With a dev terminal configured, verify: pairing prompt accepts a code / cancels / shows result; purchase shows the marquee then the outcome screen with the right colour. Record the observed behaviour in the task report (this is the only verification for the form code).

- [ ] **Step 6: Commit**

```bash
git add samples/Yort.Eftpos.SmartConnect.WinFormsDemo *.sln
git commit -m "samples: WinForms demo app for the SmartConnect dialogs"
```

---

### Task 11: Package README & pack verification

**Files:**
- Modify: `src/Yort.Eftpos.SmartConnect.WinForms/README.md`

**Interfaces:**
- Consumes: the finished public API.
- Produces: a publishable package (verified locally; not pushed).

- [ ] **Step 1: Write the package README**

Replace `src/Yort.Eftpos.SmartConnect.WinForms/README.md` with real usage docs: the two dialogs, the canonical model-D progress usage, the pairing `ShowAsync` callback usage, the "construct on the UI thread" note, and the unofficial/trademark disclaimer. Copy the two code samples verbatim from the design doc §2a and §2b.

- [ ] **Step 2: Pack and verify contents**

Run: `dotnet pack src/Yort.Eftpos.SmartConnect.WinForms/Yort.Eftpos.SmartConnect.WinForms.csproj -c Release -o ./artifacts`
Then inspect: `unzip -l ./artifacts/Yort.Eftpos.SmartConnect.WinForms.0.1.0-preview.1.nupkg`
Expected: contains `lib/net48/...dll`, `lib/net8.0-windows/...dll`, `README.md`, `Icon.png`, and a dependency on `Yort.Eftpos.SmartConnect` (verify via the `.nuspec` inside the package — the `ProjectReference` should have produced a package dependency).

- [ ] **Step 3: Commit**

```bash
git add src/Yort.Eftpos.SmartConnect.WinForms/README.md
git commit -m "docs: WinForms package README"
```

(Do **not** push to the NuGet feed in this plan — publishing is a separate, deliberate step.)

---

## Self-Review

**1. Spec coverage:**
- Two dialogs (progress + pairing) → Tasks 8, 9. ✔
- Progress scope B (transaction + operation result types) → ShowResultAsync overloads (Task 8) + `ResultVisuals` both enums (Task 2). ✔
- Model D (Progress sink, auto-show on first report, close on Dispose, optional ShowResultAsync) → Task 5 + Task 8. ✔
- Outcome timeout via overloads, suppress = don't call → Task 8 overloads. ✔
- Colour buckets covering every enum value → Task 2 (`ResultVisuals`, tested for all values). ✔
- Caption maps pre-populated + Message-preferred + negative invariant → Task 3. ✔
- Pairing callback seam, loop, retry, cancel→null, transport-caught-as-retryable (NotSent/Unknown amber), blank-code-never-calls-callback → Task 6 (+ Task 9 wrapper). ✔
- No cancel during progress (only OK on outcome) → ProgressForm has no cancel control (Task 8). ✔
- Modal-like via owner disable; null owner safe → Task 4 + wrappers. ✔
- Construct on UI thread (Progress<T>) → documented on both public types (Tasks 8, 9). ✔
- net48 + net8.0-windows, UseWindowsForms, package depends on core package → Task 1 + Task 11. ✔
- Shared appearance via composition (not internal base — CS0060) → Task 7 + wrappers. ✔
- Separate WinForms sample → Task 10. ✔
- Internal types, public surface = two dialogs → InternalsVisibleTo (Task 1); all logic types internal. ✔

**2. Placeholder scan:** The only `TODO` is the deliberate manual-config marker in the sample's `CreateClient` (Task 10) — intentional, it is a manual smoke harness, not shipped code. No other placeholders.

**3. Type consistency:** `IProgressView`/`ProgressController` (Task 5) match their use in Task 8; `IPairingView`/`PairingController` (Task 6) match Task 9; `ResultVisuals.ForTransaction/ForOperation` signatures (Task 2) match the calls in Task 8; `DefaultCaptions` factory names match usage in Tasks 3/8; `OwnerController(owner, disableWhileBusy, setWindowEnabled)` (Task 4) matches the wrapper construction. Enum members verified against the core source.

**Note for the implementer (verify at implementation):** the WinForms test project and sample target `net8.0-windows`, so they build and run **only on Windows** — run the suite via clio on this machine. If the core test project pins specific xUnit / `Microsoft.NET.Test.Sdk` versions, match those exact versions in Task 1's test csproj rather than the ones shown.
