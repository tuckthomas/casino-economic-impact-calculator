[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProtocolPath,
    [Parameter(Mandatory)][string]$ResultPath,
    [Parameter(Mandatory)][string]$SourceBundlePath,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw 'Summary output already exists.' }
$protocol = Get-Content -LiteralPath $ProtocolPath -Raw | ConvertFrom-Json
$result = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
$bundle = Get-Content -LiteralPath $SourceBundlePath -Raw | ConvertFrom-Json
$protocolHash = (Get-FileHash -LiteralPath $ProtocolPath -Algorithm SHA256).Hash.ToLowerInvariant()
$resultHash = (Get-FileHash -LiteralPath $ResultPath -Algorithm SHA256).Hash.ToLowerInvariant()
$bundleHash = (Get-FileHash -LiteralPath $SourceBundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($result.protocolHash -cne $protocolHash -or $protocol.sourceBundleSha256 -cne $bundleHash) {
    throw 'Protocol, result or source-bundle checksum does not match.'
}
if ($result.releaseAccepted -ne $false) { throw 'This command only summarizes unaccepted component diagnostics.' }
$expected = @($protocol.holdout.PSObject.Properties | ForEach-Object { $_.Value } | Sort-Object)
$actual = @($result.holdout.stableVenueId | Sort-Object)
if (!$expected.Count -or $actual.Count -ne $expected.Count -or (Compare-Object $expected $actual)) {
    throw 'Result property identities do not match the frozen holdout.'
}
if (@($actual | Select-Object -Unique).Count -ne $actual.Count) { throw 'Duplicate holdout property.' }
$facilities = @{}
foreach ($facility in $bundle.facilities.rows) { $facilities[$facility.stableVenueId] = $facility }
$yearStart = [datetime]::new([int]$protocol.observedYear, 1, 1)
$yearEnd = $yearStart.AddYears(1).AddDays(-1)
foreach ($row in $result.holdout) {
    if (!$facilities.ContainsKey($row.stableVenueId) -or $row.state -cne $facilities[$row.stableVenueId].state) {
        throw 'A holdout property or its jurisdiction is absent from the captured source.'
    }
    $market = @($protocol.holdout.PSObject.Properties | Where-Object { $_.Value -ccontains $row.stableVenueId })
    if ($market.Count -ne 1 -or $row.market -cne $market[0].Name) { throw 'Result market differs from frozen partition.' }
    $periods = @($bundle.performance.rows | Where-Object {
        $_.stableVenueId -ceq $row.stableVenueId -and $_.reportedMetricKey -ceq 'comparable-land-based-gaming-revenue' -and
        [datetime]$_.periodStart -ge $yearStart -and [datetime]$_.periodEnd -le $yearEnd
    } | Sort-Object periodStart)
    $cursor = $yearStart; $amount = 0.0
    foreach ($period in $periods) {
        $end = [datetime]$period.periodEnd
        if ([datetime]$period.periodStart -ne $cursor -or $end -lt $cursor) {
            throw 'Captured comparable revenue periods have gaps or overlaps.'
        }
        $cursor = $end.AddDays(1)
        $amount += [double]$(if ($null -ne $period.inflationAdjustedAmount) { $period.inflationAdjustedAmount } else { $period.reportedAmount })
    }
    if ($cursor -ne $yearEnd.AddDays(1) -or @($periods.reportedMetricDefinition | Select-Object -Unique).Count -ne 1 -or
        [Math]::Abs($amount - [double]$row.observed) -gt 1e-4) {
        throw 'Holdout observation does not reconcile to complete comparable source revenue.'
    }
}

function Measure-Residuals([object[]]$Rows) {
    $totalObserved = 0.0; $absoluteError = 0.0; $signedError = 0.0; $apes = @()
    foreach ($row in $Rows) {
        if ($null -eq $row.observed -or $null -eq $row.predicted -or $null -eq $row.ape) {
            throw 'A property observation, prediction or error is missing.'
        }
        $observed = [double]$row.observed; $predicted = [double]$row.predicted
        if (![double]::IsFinite($observed) -or $observed -le 0 -or ![double]::IsFinite($predicted) -or $predicted -lt 0) {
            throw 'Nonfinite, nonpositive observed or negative predicted revenue.'
        }
        $error = $predicted - $observed
        $ape = 100 * [Math]::Abs($error) / $observed
        if (![double]::IsFinite([double]$row.ape) -or [Math]::Abs([double]$row.ape - $ape) -gt 1e-7) {
            throw 'Reported property error does not reconcile.'
        }
        $totalObserved += $observed; $absoluteError += [Math]::Abs($error); $signedError += $error
        $apes += $ape
    }
    $mape = [double](($apes | Measure-Object -Average).Average)
    $wape = 100 * $absoluteError / $totalObserved
    $bias = 100 * $signedError / $totalObserved
    return [ordered]@{
        propertyCount = $Rows.Count; observedRevenue = $totalObserved
        mape = $mape; wape = $wape; normalizedSignedBias = $bias
        maximumPropertyApe = [double](($apes | Measure-Object -Maximum).Maximum)
        fractionWithinThirtyPercent = @($apes | Where-Object { $_ -le 30 }).Count / [double]$Rows.Count
    }
}
$overall = Measure-Residuals @($result.holdout)
foreach ($metric in @(
    @($overall.mape, $result.holdoutMetrics.meanAbsolutePercentageError),
    @($overall.wape, $result.holdoutWape),
    @($overall.normalizedSignedBias, $result.holdoutNormalizedBias)
)) {
    if ($null -eq $metric[1]) { throw 'Reported aggregate metric is missing.' }
    if ([Math]::Abs([double]$metric[0] - [double]$metric[1]) -gt 1e-7) { throw 'Reported aggregate metric does not reconcile.' }
}
$markets = @($result.holdout | Group-Object market | ForEach-Object {
    [ordered]@{ market = $_.Name; residuals = Measure-Residuals $_.Group }
})
$jurisdictions = @($result.holdout | Group-Object state | ForEach-Object {
    [ordered]@{ state = $_.Name; residuals = Measure-Residuals $_.Group }
})
$inputGaps = @($result.holdout | ForEach-Object {
    $facility = $facilities[$_.stableVenueId]
    $missing = @('gamingPositions','slotOrVltPositions','tableGameCount','hotelRoomCount',
        'gamingFloorSquareFeet','foodBeverageVenueCount','eventCapacity' | Where-Object { $null -eq $facility.$_ })
    [ordered]@{ stableVenueId = $_.stableVenueId; missingAttributes = $missing; sourceNotes = $facility.notes }
})
$summary = [ordered]@{
    scope = 'Checksum-bound resident-revenue diagnostic; descriptive gates do not authorize release.'
    protocolHash = $protocolHash; resultHash = $resultHash; sourceBundleHash = $bundleHash
    frozenScope = $protocol.scope; graphHash = $result.graphHash
    observedRevenueSourceReconciled = $true
    overall = $overall; markets = $markets; jurisdictions = $jurisdictions
    descriptiveGates = [ordered]@{
        mape = $overall.mape -le 20; wape = $overall.wape -le 15
        bias = [Math]::Abs($overall.normalizedSignedBias) -le 5
        maximumProperty = $overall.maximumPropertyApe -le 40
        propertyTail = $overall.fractionWithinThirtyPercent -ge 0.9
        twelveProperties = $overall.propertyCount -ge 12
        threeMarkets = $markets.Count -ge 3
        twoJurisdictions = $jurisdictions.Count -ge 2
        everyGroupHasThreeProperties = @($markets + $jurisdictions | Where-Object { $_.residuals.propertyCount -lt 3 }).Count -eq 0
        everyGroupMeetsErrorAndBias = @($markets + $jurisdictions | Where-Object {
            $_.residuals.wape -gt 20 -or [Math]::Abs($_.residuals.normalizedSignedBias) -gt 10
        }).Count -eq 0
        noMissingCandidateRoutes = @($result.holdout | Where-Object { $_.missingCandidateRoutes -gt 0 }).Count -eq 0
    }
    worstProperties = @($result.holdout | Sort-Object ape -Descending | Select-Object stableVenueId, market, state, observed, predicted, ape, missingCandidateRoutes)
    candidateInputGaps = $inputGaps
    releaseAccepted = $false
    releaseBlocks = @($result.releaseBlocks) + @(
        'Full eligible-pool coverage, baseline improvement, freshness, frozen audit and all competitor routes require independent policy evidence.',
        'Property capacity proxies and static facility coordinates require temporal verification.'
    )
}
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath
Write-Output ('Summary retained: ' + $OutputPath)
