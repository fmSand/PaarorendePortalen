<#
.SYNOPSIS
    Validates the local FHIR profiles through the official HL7 validator.
    examples/valid must pass; examples/invalid must each fail on the constraint
    expected-errors.json names. See fhir/README.md.
#>
[CmdletBinding()]
param(
    [string] $ValidatorVersion = '6.10.3',
    [string] $NoBasisVersion = '2.2.2',
    [string] $JavaImage = 'eclipse-temurin:21-jre',
    [string] $PackageCacheVolume = 'fhir-package-cache'
)

$ErrorActionPreference = 'Stop'

$fhirDir = $PSScriptRoot
$repoRoot = Split-Path $fhirDir -Parent
$toolsDir = Join-Path $fhirDir '.tools'
$jarPath = Join-Path $toolsDir 'validator_cli.jar'
$profileCanonicalPrefix = 'http://parorendeportalen.example.org/fhir/StructureDefinition/parorendeportalen-'

if (-not (Test-Path $toolsDir)) {
    New-Item -ItemType Directory -Path $toolsDir | Out-Null
}

if (-not (Test-Path $jarPath)) {
    $jarUrl = "https://github.com/hapifhir/org.hl7.fhir.core/releases/download/$ValidatorVersion/validator_cli.jar"
    Write-Host "Downloading validator_cli $ValidatorVersion (~200 MB, once)..."
    $previousProgress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri $jarUrl -OutFile $jarPath
    }
    finally {
        $ProgressPreference = $previousProgress
    }
}

# Docker on Windows wants forward slashes in a bind mount.
$mountSource = $repoRoot -replace '\\', '/'

# A duplicate canonical is not an error to the validator: it keeps one silently,
# so a stray backup in profiles/ shadows the profile under test.
$canonicals = Get-ChildItem (Join-Path $fhirDir 'profiles') -File | ForEach-Object {
    $resource = Get-Content $_.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    [pscustomobject]@{ File = $_.Name; Url = "$($resource.url)|$($resource.version)" }
}

$shadowed = $canonicals | Group-Object Url | Where-Object { $_.Count -gt 1 }
if ($shadowed) {
    foreach ($group in $shadowed) {
        Write-Host "Duplicate canonical $($group.Name): $($group.Group.File -join ', ')" -ForegroundColor Red
    }
    throw 'Two files in profiles/ claim the same canonical URL; the validator would use only one of them.'
}

function Invoke-Validator {
    param(
        [Parameter(Mandatory)] [string] $ExamplesFolder,
        [Parameter(Mandatory)] [string] $ReportName
    )

    $reportPath = Join-Path $toolsDir $ReportName

    # Non-zero exit is expected for half these runs, so the report decides.
    # allow-example-urls lets Identifier.system point at example.org, which the
    # validator otherwise rejects outright.
    docker run --rm `
        -v "${mountSource}:/work" `
        -v "${PackageCacheVolume}:/root/.fhir" `
        $JavaImage `
        java -jar /work/fhir/.tools/validator_cli.jar `
        "/work/fhir/examples/$ExamplesFolder" `
        -version 4.0.1 `
        -allow-example-urls true `
        -ig /work/fhir/profiles `
        -ig "hl7.fhir.no.basis#$NoBasisVersion" `
        -output "/work/fhir/.tools/$ReportName" | Out-Null

    if (-not (Test-Path $reportPath)) {
        throw "The validator produced no report for examples/$ExamplesFolder."
    }

    $report = Get-Content $reportPath -Raw -Encoding utf8 | ConvertFrom-Json

    # A folder holding one file yields a bare OperationOutcome, several yield a Bundle.
    $outcomes = @()
    if ($report.resourceType -eq 'Bundle') {
        $outcomes = @($report.entry | ForEach-Object { $_.resource })
    }
    else {
        $outcomes = @($report)
    }

    return $outcomes | ForEach-Object {
        [pscustomobject]@{
            File   = Split-Path ($_.extension | Where-Object { $_.url -like '*operationoutcome-file' }).valueString -Leaf
            Errors = @($_.issue | Where-Object { $_.severity -in 'error', 'fatal' })
        }
    }
}

function Get-IssueText {
    param([Parameter(Mandatory)] $Issue)

    # Cardinality failures name the element in details.text, type failures in expression.
    $expressions = ''
    if ($Issue.expression) {
        $expressions = $Issue.expression -join ' '
    }

    return "$($Issue.details.text) $expressions"
}

function Test-RaisedByOurProfile {
    param([Parameter(Mandatory)] $Issue)

    $context = $Issue.extension | Where-Object { $_.url -like '*operationoutcome-issue-context' }
    return $null -ne ($context | Where-Object { $_.valueString -like "$profileCanonicalPrefix*" })
}

$failures = New-Object System.Collections.Generic.List[string]

Write-Host ''
Write-Host 'Conforming examples' -ForegroundColor Cyan

foreach ($result in Invoke-Validator -ExamplesFolder 'valid' -ReportName 'report-valid.json') {
    if ($result.Errors.Count -eq 0) {
        Write-Host "  PASS  $($result.File)" -ForegroundColor Green
    }
    else {
        Write-Host "  FAIL  $($result.File)" -ForegroundColor Red
        foreach ($issue in $result.Errors) {
            Write-Host "          $($issue.details.text)"
        }
        $failures.Add("$($result.File) should conform but does not.")
    }
}

Write-Host ''
Write-Host 'Non-conforming examples, one broken constraint each' -ForegroundColor Cyan

$expected = (Get-Content (Join-Path $fhirDir 'expected-errors.json') -Raw -Encoding utf8 | ConvertFrom-Json).expect
$invalidResults = Invoke-Validator -ExamplesFolder 'invalid' -ReportName 'report-invalid.json'

foreach ($result in $invalidResults) {
    $expectation = $expected.$($result.File)

    if (-not $expectation) {
        $failures.Add("$($result.File) has no entry in expected-errors.json.")
        Write-Host "  ????  $($result.File) - not listed in expected-errors.json" -ForegroundColor Red
        continue
    }

    # Failing is not enough - a typo fails too. It must be our constraint.
    $matched = @($result.Errors | Where-Object {
            (Get-IssueText $_) -like "*$expectation*" -and (Test-RaisedByOurProfile $_)
        })

    if ($matched.Count -gt 0) {
        Write-Host "  PASS  $($result.File) - rejected on $expectation" -ForegroundColor Green
    }
    elseif ($result.Errors.Count -eq 0) {
        Write-Host "  FAIL  $($result.File) - accepted; the constraint does not bite" -ForegroundColor Red
        $failures.Add("$($result.File) was accepted, so '$expectation' is not enforced.")
    }
    else {
        Write-Host "  FAIL  $($result.File) - rejected, but not on $expectation" -ForegroundColor Red
        foreach ($issue in $result.Errors) {
            Write-Host "          $($issue.details.text)"
        }
        $failures.Add("$($result.File) failed for the wrong reason.")
    }
}

$unseen = @($expected.PSObject.Properties.Name | Where-Object { $_ -notin $invalidResults.File })
foreach ($file in $unseen) {
    Write-Host "  MISS  $file - listed in expected-errors.json but never validated" -ForegroundColor Red
    $failures.Add("$file is expected to fail but was not validated.")
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) problem(s):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "All examples behaved as expected (validator_cli $ValidatorVersion, hl7.fhir.no.basis#$NoBasisVersion)." -ForegroundColor Green
Write-Host "FHIR packages are cached in the Docker volume $PackageCacheVolume; remove it with: docker volume rm $PackageCacheVolume"
