#!/usr/bin/env bash

bash_source="${BASH_SOURCE[0]}"
while [ -h "$bash_source" ]; do # resolve $bash_source until the file is no longer a symlink
  scriptroot="$( cd -P "$( dirname "$bash_source" )" && pwd )"
  bash_source="$(readlink "$bash_source")"
  [[ $bash_source != /* ]] && bash_source="$scriptroot/$bash_source" # if $bash_source was a relative symlink, resolve it relative to the path where the symlink file was located
done
scriptroot="$( cd -P "$( dirname "$bash_source" )" && pwd )"

dotnet build "$scriptroot/build/_build.csproj" /nodeReuse:false /p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet
dotnet run --project "$scriptroot/build/_build.csproj" --no-build -- "$@"
