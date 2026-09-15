param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Versione non valida: '$Version'. Usa il formato MAJOR.MINOR.PATCH, per esempio 1.6.1."
}

$root = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $root 'Directory.Build.props'
if (-not (Test-Path $propsPath)) { throw "Directory.Build.props non trovato." }

[xml]$xml = Get-Content $propsPath -Raw
$node = @($xml.Project.PropertyGroup.Version)[0]
if ($null -eq $node) { throw "Nodo <Version> non trovato in Directory.Build.props." }
$node.InnerText = $Version
$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.IndentChars = '  '
$settings.NewLineChars = "`n"
$settings.NewLineHandling = 'Replace'
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($propsPath, $settings)
$xml.Save($writer)
$writer.Close()

Write-Host "DraftLite impostato alla versione $Version"
Write-Host "Eseguibile, About, updater, installer e release useranno questo numero."
Write-Host "Per pubblicare: git tag v$Version && git push origin v$Version"
