param(
    [string]$IpAddress = "192.168.x.x"
)

if ($IpAddress -eq "192.168.x.x") {
    Write-Host "Por favor, corre o script passando o IP que aparece no ecrã do teu WeatherOS." -ForegroundColor Red
    Write-Host "Exemplo: .\Send-TestData.ps1 -IpAddress `"192.168.1.50`"" -ForegroundColor Yellow
    exit
}

$endpoint = New-Object System.Net.IPEndPoint ([System.Net.IPAddress]::Parse($IpAddress), 6000)
$udpClient = New-Object System.Net.Sockets.UdpClient

Write-Host "A enviar dados meteorologicos de teste para $IpAddress na porta 6000..." -ForegroundColor Cyan

# Pacote 1
$data = "T:32.5;H:85;P:995.2;W:50;D:S;C:Tempestade"
$bytes = [System.Text.Encoding]::ASCII.GetBytes($data)
$udpClient.Send($bytes, $bytes.Length, $endpoint)
Write-Host "Enviado: $data" -ForegroundColor Green
Start-Sleep -Seconds 3

# Pacote 2
$data = "T:25.0;H:50;P:1015;W:10;D:NW;C:Ceu Limpo"
$bytes = [System.Text.Encoding]::ASCII.GetBytes($data)
$udpClient.Send($bytes, $bytes.Length, $endpoint)
Write-Host "Enviado: $data" -ForegroundColor Green

$udpClient.Close()
Write-Host "Concluido!"
