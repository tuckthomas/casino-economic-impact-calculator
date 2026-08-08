FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install Node.js and npm for frontend assets build
RUN apt-get update && apt-get install -y curl \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Copy solution and project files
COPY ["SaveNEIN.sln", "./"]
COPY ["global.json", "./"]
COPY ["SaveNEIN.Server/SaveNEIN.Server.csproj", "SaveNEIN.Server/"]
COPY ["SaveNEIN.Client/SaveNEIN.Client.csproj", "SaveNEIN.Client/"]
COPY ["SaveNEIN.Shared/SaveNEIN.Shared.csproj", "SaveNEIN.Shared/"]

# Restore dependencies
RUN dotnet restore

# Copy the rest of the source code
COPY . .

# Build the project
WORKDIR "/src/SaveNEIN.Server"
RUN dotnet build "SaveNEIN.Server.csproj" -c Release -o /app/build

# Publish the project
FROM build AS publish
RUN dotnet publish "SaveNEIN.Server.csproj" -c Release -o /app/publish

# Final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# Install debugging tools
RUN apt-get update && apt-get install -y curl procps vim && rm -rf /var/lib/apt/lists/*
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SaveNEIN.Server.dll"]
