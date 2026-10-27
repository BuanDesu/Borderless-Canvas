param([Parameter(Mandatory)][string]$Directory, [switch]$Publish)
$ErrorActionPreference = 'Stop'
throw 'Automated release publishing is not configured. Build and verify packages locally with scripts/package-trial.ps1.'
