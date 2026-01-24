#!/bin/bash

# Define the development port
DEV_PORT=5000

# Kill any existing development processes (strictly dotnet watch for SaveFW.Server)
# This avoids killing the production container which runs "dotnet SaveFW.Server.dll" directly
echo "Cleaning up existing dotnet watch processes..."
# We explicitly look for "watch" in the command line
pids=$(pgrep -f "dotnet.*watch.*SaveFW.Server")

if [ -n "$pids" ]; then
  echo "Found development watchers: $pids"
  for pid in $pids; do
      echo "Killing watcher PID $pid..."
      kill -9 $pid 2>/dev/null || true
  done
else
  echo "No existing development watchers found."
fi

# NOTE: We do NOT kill port 80 or 8002 as those are used by the production Docker instance.

# Start the server
echo "Starting server on port $DEV_PORT (Development Environment)..."

# Build Tailwind CSS
echo "Building Assets..."
cd /root/SaveFW || exit 1
npm run copy-libs
npm run build:css

# Ensure the app runs with the correct content root/appsettings
cd /root/SaveFW/SaveFW.Server || exit 1

# Clean old wwwroot artifacts if they exist
rm -rf /root/SaveFW/SaveFW.Server/wwwroot

# Run the server with hot reload (dotnet watch)
# ASPNETCORE_URLS sets the listening port to 5000
# ASPNETCORE_ENVIRONMENT=Development ensures we use appsettings.Development.json
echo "Starting server with hot reload..."
echo "File changes will automatically trigger rebuilds."
echo "Press Ctrl+C to stop."
echo ""

env ASPNETCORE_ENVIRONMENT=Development DOTNET_ROOT=/root/.dotnet PATH=/root/.dotnet:$PATH \
  /root/.dotnet/dotnet watch run \
  --project /root/SaveFW/SaveFW.Server/SaveFW.Server.csproj \
  --urls "http://0.0.0.0:$DEV_PORT"