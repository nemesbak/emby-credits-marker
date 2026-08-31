#!/bin/sh
# Build Emby.CreditsMarker.dll in a throwaway .NET 8 SDK container.
#
# Needs the Emby server reference assemblies in ./lib/ :
#   MediaBrowser.*.dll and Emby.*.dll  (copy them from an Emby install,
#   e.g.  docker cp emby:/app/emby/system/. ./_all/  then move the two globs)
#
# Usage:  ./build.sh          -> ./out/Emby.CreditsMarker.dll
set -e

if [ ! -d lib ] || [ -z "$(ls lib/MediaBrowser.*.dll 2>/dev/null)" ]; then
  echo "ERROR: put the Emby reference assemblies in ./lib/ first." >&2
  echo "  MediaBrowser.Common.dll MediaBrowser.Controller.dll MediaBrowser.Model.dll Emby.Web.GenericEdit.dll (at minimum)" >&2
  exit 1
fi

docker run --rm -v "$PWD":/src -w /src --entrypoint sh \
  mcr.microsoft.com/dotnet/sdk:8.0 -c \
  'dotnet build Emby.CreditsMarker.csproj -c Release -o /src/out'

echo
echo "built: out/Emby.CreditsMarker.dll"
echo "install: copy it to your Emby 'plugins' folder and restart Emby."
