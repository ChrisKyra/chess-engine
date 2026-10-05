#!/usr/bin/env bash
# Builds Chess.app: a double-clickable macOS app with the C++ engine bundled inside.
#
#   ./build-mac-app.sh               # creates ~/Desktop/Chess.app
#   ./build-mac-app.sh /Applications # or somewhere else
#
# Run it again after changing the GUI or the engine to refresh the app.
set -euo pipefail

GUI_DIR="$(cd "$(dirname "$0")" && pwd)"
# The newest engine version. Change this line to bundle a different one.
ENGINE_DIR="$GUI_DIR/../Engine_12"
DEST="${1:-$HOME/Desktop}"
APP="$DEST/Chess.app"
DOTNET="$(command -v dotnet || echo "$HOME/.dotnet/dotnet")"
case "$(uname -m)" in
  arm64) RID=osx-arm64 ;;
  *) RID=osx-x64 ;;
esac

echo "==> Building the engine"
make -C "$ENGINE_DIR"

echo "==> Publishing the GUI ($RID, self-contained so no .NET install is needed to run it)"
PUBLISH="$GUI_DIR/bin/publish/$RID"
rm -rf "$PUBLISH"
"$DOTNET" publish "$GUI_DIR/src/ChessGui/ChessGui.csproj" -c Release -r "$RID" --self-contained true -o "$PUBLISH"

echo "==> Assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
cp "$ENGINE_DIR/engine" "$APP/Contents/Resources/engine"
cp "$GUI_DIR/src/ChessGui/Assets/Chess.icns" "$APP/Contents/Resources/Chess.icns"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Chess</string>
  <key>CFBundleDisplayName</key>
  <string>Chess</string>
  <key>CFBundleIdentifier</key>
  <string>local.chessgui</string>
  <key>CFBundleExecutable</key>
  <string>ChessGui</string>
  <key>CFBundleIconFile</key>
  <string>Chess</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
</dict>
</plist>
PLIST

# Apple Silicon only runs signed code; an ad-hoc signature is enough for your own machine.
codesign --force --deep --sign - "$APP"
touch "$APP"   # make Finder pick up the icon

echo "==> Done: $APP"
