$ErrorActionPreference = 'Stop'

$uri = 'https://flash-filling.com/user/thsottiaux'
$handler = $null
$client = $null
try {
    Add-Type -AssemblyName System.Net.Http
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.UseProxy = $false
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(20)
    $response = $client.GetAsync($uri).GetAwaiter().GetResult()
    $response.EnsureSuccessStatusCode()
    Write-Host "Connection succeeded: HTTP $([int]$response.StatusCode)"
    Write-Host 'Direct local mode is available.'
}
catch {
    Write-Error "Connection failed: $($_.Exception.Message)"
    Write-Host 'Check DNS and direct access to flash-filling.com.'
    exit 1
}
finally {
    if ($null -ne $client) { $client.Dispose() }
    if ($null -ne $handler) { $handler.Dispose() }
}
