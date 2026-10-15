$comparisonRows = @()
foreach ($comparisonFamily in @('physical','hybrid')) {
    foreach ($comparisonForm in @('inverse','exponential')) {
        $protocolPath = 'docs/validation/guarded-acs-nearby-tribal-' + $comparisonFamily + '-' + $comparisonForm + '-protocol-2026-10-14.json'
        $resultPath = 'docs/validation/bundles/guarded-acs-nearby-tribal-' + $comparisonFamily + '-' + $comparisonForm + '-results-2026-10-14.json'
        $protocol = Get-Content -LiteralPath $protocolPath -Raw | ConvertFrom-Json
        $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        if ($result.protocolHash -ne (Get-FileHash -LiteralPath $protocolPath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Protocol bytes do not match the scored result.' }
        $candidate = if ($null -ne $result.selected) { $result.selected } else { $result.bestDiagnosticCandidate }
        $allKeys = @($candidate.observations | ForEach-Object { $_.caseKey } | Sort-Object)
        $declaredKeys = @(@($protocol.training) + @($protocol.selection) | Sort-Object)
        if (($allKeys -join '|') -ne ($declaredKeys -join '|') -or $result.finalHoldoutScored -or $result.releaseAccepted) { throw 'Development comparison changed its declared cohort or scored a final holdout.' }
        $partitions = @()
        foreach ($partition in @('training','selection')) {
            $keys = $protocol.$partition
            $observations = @($candidate.observations | Where-Object { $_.caseKey -in $keys })
            $observedTotal = ($observations | Measure-Object observed -Sum).Sum
            $absoluteError = 0.0; $signedError = 0.0; $apeTotal = 0.0; $maximumApe = 0.0
            foreach ($observation in $observations) {
                $errorValue = $observation.predicted - $observation.observed
                $ape = 100 * [Math]::Abs($errorValue) / $observation.observed
                $absoluteError += [Math]::Abs($errorValue); $signedError += $errorValue; $apeTotal += $ape
                $maximumApe = [Math]::Max($maximumApe, $ape)
            }
            $mape = $apeTotal / $observations.Count
            $reported = $candidate.($partition + 'Metrics').meanAbsolutePercentageError
            if ([Math]::Abs($mape - $reported) -gt 1e-10) { throw 'Recomputed property error does not match the calibration result.' }
            $partitions += [ordered]@{partition=$partition;properties=$observations.Count;mape=$mape;wape=100*$absoluteError/$observedTotal;normalizedBias=100*$signedError/$observedTotal;maximumPropertyApe=$maximumApe}
        }
        $comparisonRows += [ordered]@{model=$comparisonFamily;friction=$protocol.frictionForm;protocolHash=$result.protocolHash;
            resultHash=(Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash.ToLowerInvariant();
            candidateCount=$result.candidateCount;admittedCandidateCount=$result.admittedCandidateCount;
            referenceIncome=$protocol.referenceIncome;reservedObservedRevenueIdentities=$protocol.observedGgrExcludedVenueIds.Count;
            diagnosticCandidateKey=$candidate.key;partitions=$partitions;failures=$candidate.admission.failures}
    }
}
[ordered]@{version='development-friction-comparison-audit-v1';scope='Independent recomputation of metrics from the saved development observations; no input/fiscal audit, untouched holdout or empirical Northeast Indiana accuracy claim.';
    observedPropertyCount=36;trainingPropertyCount=22;selectionPropertyCount=14;releaseAccepted=$false;freshHoldoutScored=$false;comparisons=$comparisonRows} |
    ConvertTo-Json -Depth 15 | Set-Content -LiteralPath 'docs/validation/bundles/acs-common-budget-friction-comparison-audit-2026-10-14.json' -Encoding utf8
$comparisonRows | ForEach-Object { [pscustomobject]@{Model=$_.model;Friction=$_.friction;Candidates=$_.candidateCount;Admitted=$_.admittedCandidateCount;SelectionMape=$_.partitions[1].mape;SelectionWape=$_.partitions[1].wape} } | Format-Table -AutoSize
