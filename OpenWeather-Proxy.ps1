param(
    [string]$ApiKey = "bd7e29393f3846fb31118232bdb2001f",
    [string]$VmIpAddress = "127.0.0.1",
    [int]$VmPort = 6000,
    [int]$HostListenPort = 6001
)

Write-Host "--- Satelite OpenWeather para Cosmos OS ---" -ForegroundColor Cyan
Write-Host "A tentar escutar pedidos automáticos do SO na porta $HostListenPort..." -ForegroundColor Gray

# Job assíncrono para escutar UDP do Cosmos
$listener = [System.Net.Sockets.UdpClient]::new($HostListenPort)
$endpoint = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, $HostListenPort)

Write-Host "`n[MODO DUAL ATIVO]" -ForegroundColor Yellow
Write-Host "1. Podes escrever 'fetch cidade' no Cosmos e eu recebo automaticamente."
Write-Host "2. OU podes digitar a cidade diretamente AQUI neste terminal se a firewall bloquear o Cosmos!"

while ($true) {
    if ($listener.Available -gt 0) {
        $receiveBytes = $listener.Receive([ref]$endpoint)
        $city = [System.Text.Encoding]::ASCII.GetString($receiveBytes).Trim()
        Write-Host "`n[Cosmos OS] Pediu a localizacao automaticamente: $city" -ForegroundColor Magenta
    }
    else {
        if ([System.Console]::KeyAvailable) {
            $city = Read-Host "`n[Input Manual] Digite a cidade para enviar para o Cosmos"
        }
        else {
            Start-Sleep -Milliseconds 100
            continue
        }
    }
    
    if ([string]::IsNullOrWhiteSpace($city)) { continue }
    
    $url = "http://api.openweathermap.org/data/2.5/weather?q=$city&appid=$ApiKey&units=metric"
    
    try {
        Write-Host "A contactar OpenWeather API para '$city'..." -ForegroundColor DarkGray
        $response = Invoke-RestMethod -Uri $url -Method Get
        
        $temp = [math]::Round($response.main.temp, 1)
        $humidity = $response.main.humidity
        $pressure = $response.main.pressure
        $windSpeed = [math]::Round($response.wind.speed * 3.6, 1) # m/s to km/h
        $windDir = $response.wind.deg
        $condition = $response.weather[0].main
        
        $packet = "T:$temp;H:$humidity;P:$pressure;W:$windSpeed;D:$windDir;C:$condition"
        
        Write-Host "Dados sacados da Internet! A disparar pacote de volta para o Cosmos OS -> $packet" -ForegroundColor Green
        
        $sendClient = New-Object System.Net.Sockets.UdpClient
        $sendClient.Connect($VmIpAddress, $VmPort)
        $sendBytes = [System.Text.Encoding]::ASCII.GetBytes($packet)
        $sendClient.Send($sendBytes, $sendBytes.Length) | Out-Null
        $sendClient.Close()
    }
    catch {
        Write-Host "Erro ao consultar a API para '$city'." -ForegroundColor Red
        
        # Destrancar o Cosmos em caso de erro
        $errorPacket = "T:0;H:0;P:0;W:0;D:0;C:Error API"
        $sendClient = New-Object System.Net.Sockets.UdpClient
        $sendClient.Connect($VmIpAddress, $VmPort)
        $sendBytes = [System.Text.Encoding]::ASCII.GetBytes($errorPacket)
        $sendClient.Send($sendBytes, $sendBytes.Length) | Out-Null
        $sendClient.Close()
    }
}
