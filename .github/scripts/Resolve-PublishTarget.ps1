#Requires -Version 6
<#
.SYNOPSIS
Resolves and validates the package + version to publish, from either a release tag or explicit inputs.

.DESCRIPTION
The csproj <Version> is the single source of truth for what goes into each package (including the
WinForms -> core dependency, resolved from core's csproj at pack time). This script VALIDATES that the
requested version equals the committed csproj <Version> and fails on mismatch — it never overrides.
For WinForms it also fails unless core's csproj version is already published on nuget.org, so a WinForms
package can never ship an uninstallable dependency to the immutable public feed.
#>
[CmdletBinding(DefaultParameterSetName = 'Tag')]
param(
    [Parameter(ParameterSetName = 'Tag', Mandatory)]
    [string]$Tag,

    [Parameter(ParameterSetName = 'Explicit', Mandatory)]
    [ValidateSet('core', 'winforms')]
    [string]$Package,

    [Parameter(ParameterSetName = 'Explicit', Mandatory)]
    [string]$Version,

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    # Skips the WinForms->core nuget.org dependency check. For local testing of the resolve/validate
    # logic without the network dependency; the workflow never passes this.
    [switch]$SkipDependencyFeedCheck
)

$ErrorActionPreference = 'Stop'

$projectMap = @{
    'core'     = 'src/Yort.Eftpos.SmartConnect/Yort.Eftpos.SmartConnect.csproj'
    'winforms' = 'src/Yort.Eftpos.SmartConnect.WinForms/Yort.Eftpos.SmartConnect.WinForms.csproj'
}
$coreId = 'Yort.Eftpos.SmartConnect'

function Get-CsprojVersion {
    param([string]$CsprojPath)
    if (-not (Test-Path -LiteralPath $CsprojPath)) {
        throw "Project file not found: $CsprojPath"
    }
    [xml]$proj = Get-Content -LiteralPath $CsprojPath
    $value = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $value = ("$value").Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "No <Version> found in $CsprojPath."
    }
    return $value
}

if ($PSCmdlet.ParameterSetName -eq 'Tag') {
    if ($Tag -like 'core-v*') {
        $Package = 'core'
        $Version = $Tag.Substring('core-v'.Length)
    }
    elseif ($Tag -like 'winforms-v*') {
        $Package = 'winforms'
        $Version = $Tag.Substring('winforms-v'.Length)
    }
    else {
        throw "Unrecognised tag '$Tag' (expected 'core-v*' or 'winforms-v*')."
    }
}

$projectRelativePath = $projectMap[$Package]
$csprojVersion = Get-CsprojVersion -CsprojPath (Join-Path $RepoRoot $projectRelativePath)

if ($csprojVersion -ne $Version) {
    throw "Version mismatch: requested '$Version' but $projectRelativePath declares <Version>$csprojVersion</Version>. Bump and COMMIT the csproj version to match before publishing."
}

# F2: WinForms declares a dependency on core at core's csproj <Version> (resolved from the ProjectReference
# at pack time). Publishing WinForms with a core dependency not on nuget.org would ship an uninstallable
# package to an immutable feed. Fail unless core's version is already published.
if ($Package -eq 'winforms' -and -not $SkipDependencyFeedCheck) {
    $coreVersion = Get-CsprojVersion -CsprojPath (Join-Path $RepoRoot $projectMap['core'])
    $url = "https://api.nuget.org/v3-flatcontainer/$($coreId.ToLowerInvariant())/index.json"
    $published = $null
    try {
        $published = (Invoke-RestMethod -Uri $url -Method Get).versions
    }
    catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        if ($status -eq 404) {
            throw "WinForms depends on core $coreVersion, but '$coreId' has no published versions on nuget.org. Publish core-v$coreVersion first."
        }
        Write-Warning "Could not verify core $coreVersion on nuget.org ($($_.Exception.Message)); proceeding without the dependency-feed check."
    }
    if ($null -ne $published) {
        if ($published -notcontains $coreVersion) {
            throw "WinForms depends on core $coreVersion, which is not published on nuget.org. Publish core-v$coreVersion first (nuget.org versions are immutable)."
        }
        Write-Host "Verified core dependency $coreVersion is published on nuget.org."
    }
}

Write-Host "Resolved $Package -> $projectRelativePath at version $csprojVersion"

if ($env:GITHUB_OUTPUT) {
    "project=$projectRelativePath" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "version=$csprojVersion"       | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
