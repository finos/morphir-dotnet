FROM mcr.microsoft.com/dotnet/sdk:10.0.403

# Copy endpoint specific user settings into container to specify
# .NET Core should be used as the runtime.
COPY settings.vscode.json /root/.vscode-remote/data/Machine/settings.json

# Install git, process tools
RUN apt-get update \
    && apt-get install -y --no-install-recommends git procps \
    && rm -rf /var/lib/apt/lists/*
