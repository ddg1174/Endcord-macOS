#!/bin/bash
cd "$(dirname "$0")"
arch=$(uname -m)
if [ "$arch" = "arm64" ]; then
  APP="EndcordInstaller-arm64.app"
else
  APP="EndcordInstaller-x64.app"
fi
chmod +x "$APP/Contents/MacOS/EndcordInstaller"
xattr -cr "$APP"
codesign --force --deep --sign - "$APP"
open "$APP"
