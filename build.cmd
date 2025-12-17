@echo off

dotnet build "%~dp0build\_build.csproj" /nodeReuse:false /p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet
dotnet run --project "%~dp0build\_build.csproj" --no-build -- %*
