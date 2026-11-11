FROM mcr.microsoft.com/dotnet/sdk:11.0.100

# The packable libraries also target net10.0, and tests build and run net10.0 consumers of them.
# The .NET 11 SDK image has no .NET 10 shared runtime, so copy it from the .NET 10 runtime image.
COPY --from=mcr.microsoft.com/dotnet/runtime:10.0.15 /usr/share/dotnet/shared/Microsoft.NETCore.App /usr/share/dotnet/shared/Microsoft.NETCore.App

# Copy endpoint specific user settings into container to specify
# .NET Core should be used as the runtime.
COPY settings.vscode.json /root/.vscode-remote/data/Machine/settings.json

# Install git, process tools
RUN apt-get update \
    && apt-get install -y --no-install-recommends git procps \
    && rm -rf /var/lib/apt/lists/*
