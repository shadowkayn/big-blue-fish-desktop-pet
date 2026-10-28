#!/bin/bash
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
runtime=${1:-}
if [[ -z "$runtime" ]]; then
    case $(uname -m) in
        arm64) runtime=osx-arm64 ;;
        x86_64) runtime=osx-x64 ;;
        *) echo "不支持的 Mac 架构：$(uname -m)" >&2; exit 1 ;;
    esac
fi
if [[ "$runtime" != osx-arm64 && "$runtime" != osx-x64 ]]; then
    echo "用法：$0 [osx-arm64|osx-x64]" >&2
    exit 1
fi

dotnet_path=$(command -v dotnet || true)
if [[ -z "$dotnet_path" ]]; then
    echo "未找到 dotnet，请先安装项目 global.json 要求的 .NET SDK。" >&2
    exit 1
fi

version=$(/usr/bin/sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$root/Version.props" | /usr/bin/head -n 1)
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "无法从 Version.props 读取有效版本号：$version" >&2
    exit 1
fi

output="$root/release/macos"
app="$output/蓝色大肥鱼.app"
work=$(/usr/bin/mktemp -d "${TMPDIR:-/tmp}/bigbluefish-macos.XXXXXXXX")
if [[ ! -d "$work" || "$work" != *bigbluefish-macos.* ]]; then
    echo "无法创建安全的临时构建目录。" >&2
    exit 1
fi
cleanup() {
    status=$?
    trap - EXIT
    /bin/rm -rf "$work"
    exit "$status"
}
trap cleanup EXIT

main_publish="$work/main"
mcp_publish="$work/mcp"
staged_app="$work/蓝色大肥鱼.app"

echo "发布蓝色大肥鱼 ${version}（${runtime}）…"
"$dotnet_path" publish "$root/code/BigBlueFish.Linux/BigBlueFish.Linux.csproj" \
    -c Release \
    -r "$runtime" \
    --self-contained true \
    -p:CompanionEdition=Standard \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false \
    -p:RestoreLockedMode=true \
    -o "$main_publish"

echo "发布 MCP 连接组件…"
"$dotnet_path" publish "$root/code/BigBlueFish.Mcp/BigBlueFish.Mcp.csproj" \
    -c Release \
    -r "$runtime" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false \
    -p:RestoreLockedMode=true \
    -o "$mcp_publish"

/bin/mkdir -p "$staged_app/Contents/MacOS/mcp" "$staged_app/Contents/Resources"
/usr/bin/ditto "$main_publish" "$staged_app/Contents/MacOS"
/usr/bin/ditto "$mcp_publish" "$staged_app/Contents/MacOS/mcp"
/bin/ln -s "mcp/BigBlueFish.Mcp" "$staged_app/Contents/MacOS/BigBlueFish.Mcp"
/bin/chmod 755 "$staged_app/Contents/MacOS/BigBlueFish.Linux"
/bin/chmod 755 "$staged_app/Contents/MacOS/mcp/BigBlueFish.Mcp"

/usr/bin/sed "s/@VERSION@/$version/g" "$root/macos/Info.plist.in" > "$staged_app/Contents/Info.plist"
/usr/bin/plutil -lint "$staged_app/Contents/Info.plist"

icon_png="$work/BigBlueFish.png"
iconset="$work/BigBlueFish.iconset"
/bin/mkdir -p "$iconset"
/usr/bin/sips -s format png "$root/code/BigBlueFish.Windows/BigBlueFish.ico" --out "$icon_png" >/dev/null
make_icon() {
    local pixels=$1 name=$2
    /usr/bin/sips -z "$pixels" "$pixels" "$icon_png" --out "$iconset/$name" >/dev/null
}
make_icon 16 icon_16x16.png
make_icon 32 icon_16x16@2x.png
make_icon 32 icon_32x32.png
make_icon 64 icon_32x32@2x.png
make_icon 128 icon_128x128.png
make_icon 256 icon_128x128@2x.png
make_icon 256 icon_256x256.png
make_icon 512 icon_256x256@2x.png
make_icon 512 icon_512x512.png
make_icon 1024 icon_512x512@2x.png
/usr/bin/iconutil -c icns "$iconset" -o "$staged_app/Contents/Resources/BigBlueFish.icns"

echo "执行本机 ad-hoc 签名…"
/usr/bin/codesign --force --deep --sign - "$staged_app"
/usr/bin/codesign --verify --deep --strict "$staged_app"

/bin/mkdir -p "$output"
next_app="$output/.蓝色大肥鱼.app.next"
if [[ "$app" != "$root/release/macos/蓝色大肥鱼.app" || "$next_app" != "$root/release/macos/.蓝色大肥鱼.app.next" ]]; then
    echo "拒绝写入非预期输出路径。" >&2
    exit 1
fi
/bin/rm -rf "$next_app"
/usr/bin/ditto "$staged_app" "$next_app"
/bin/rm -rf "$app"
/bin/mv "$next_app" "$app"

echo
echo "构建完成：$app"
echo "启动命令：open \"$app\""
if [[ ! -x /opt/homebrew/bin/llama-server && ! -x /usr/local/bin/llama-server && ! -x "$app/Contents/MacOS/runtime/llama-server" ]]; then
    echo "提示：未发现 llama-server；桌宠可启动，但本地模型功能需要另行安装运行时。"
fi
