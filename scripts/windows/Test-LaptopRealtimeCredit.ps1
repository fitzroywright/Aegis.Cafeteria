param(
    [int]$CustomerId = 900001,
    [decimal]$Limit = 1000,
    [decimal]$Amount = 100
)

$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5088"

$policyBody = @{
    additionalCreditLimit = $Limit
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "$baseUrl/api/management/customers/$CustomerId/credit-policy" `
    -Method Put `
    -UseDefaultCredentials `
    -ContentType "application/json" `
    -Body $policyBody | Out-Host

$operationId = [Guid]::NewGuid()
$creditBody = @{
    customerId = $CustomerId
    amount = $Amount
    reason = "Laptop SignalR integration test"
    operationId = $operationId
} | ConvertTo-Json

$result = Invoke-RestMethod `
    -Uri "$baseUrl/api/management/additional-credit" `
    -Method Post `
    -UseDefaultCredentials `
    -ContentType "application/json" `
    -Body $creditBody

$result | Format-List
Write-Host ""
Write-Host "Approved $Amount additional credit for test customer $CustomerId."
Write-Host "If that employee is displayed, the POS should refresh immediately through SignalR."
Write-Host "If SignalR is unavailable, the existing 15-second durable poll must still refresh it."
