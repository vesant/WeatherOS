#!/bin/bash
set -e

echo "====================================================="
echo "   Building WeatherOS (Cosmos Gen3 / NativeAOT)      "
echo "====================================================="

# O Bazzite usa Podman nativamente. Vamos usar a imagem oficial do .NET 10 Nightly.
# Isso evita ter que instalar a preview do .NET 10 no teu sistema local.

echo "A puxar/iniciar o contentor do .NET 10 Preview..."
podman run --rm -it \
  -v "$(pwd):/app:z" \
  -w /app \
  mcr.microsoft.com/dotnet/nightly/sdk:10.0 \
  bash -c "
    echo 'Preparando ambiente de build...' &&
    export PATH=\"\$PATH:/root/.dotnet/tools\" &&
    dotnet tool install -g Cosmos.Patcher &&
    apt-get update && apt-get install -y xorriso mtools clang lld make &&
    echo 'Restaurando pacotes NuGet...' &&
    dotnet restore &&
    echo 'Compilando OS...' &&
    dotnet publish -c Debug -r linux-x64 -p:CosmosArch=x64
  "

echo "====================================================="
echo " Build concluída!"
echo " Podes encontrar a tua imagem ISO na pasta:"
echo " bin/Debug/net10.0/linux-x64/publish/"
echo "====================================================="
