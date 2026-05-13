#!/bin/bash

# --- 配置区域 ---
PROJECT_NAME="Terraria_Wiki"                     # 你的项目名称
CSPROJ_PATH="./Terraria_Wiki.csproj" # .csproj 的相对路径
FRAMEWORK="net10.0-ios"                         # 目标框架
CONFIG="Release"                                  # 使用 Debug 模式以便 Safari 调试
OUTPUT_DIR="bin/$CONFIG/$FRAMEWORK/ios-arm64"
DESKTOP_PATH="$HOME/Desktop"
IPA_NAME="${PROJECT_NAME}_Unsigned.ipa"

echo "🚀 开始构建项目: $PROJECT_NAME ($CONFIG)..."

# 1. 清理旧的构建产物
echo "🧹 清理旧文件..."
rm -rf bin/ obj/
rm -rf Payload/
rm -f "$IPA_NAME"

# 2. 执行 dotnet publish
echo "📦 正在编译并发布 (跳过签名)..."
dotnet publish "$CSPROJ_PATH" \
  -f "$FRAMEWORK" \
  -c "$CONFIG" \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:CodesignKey="-" \
  -p:CodesignProvision="-" \
  -p:RequireProvisioningProfile=false \
  -p:BuildIpa=true \
  --verbosity:minimal

# 3. 检查并打包成 IPA
echo "压缩成 IPA 格式..."
APP_PATH=$(find "$OUTPUT_DIR" -type d -name "*.app" | head -n 1)

if [ -z "$APP_PATH" ]; then
    echo "❌ 错误：未找到生成的 .app 文件，请检查控制台报错。"
    exit 1
fi

mkdir -p Payload
cp -r "$APP_PATH" Payload/
zip -qr "$IPA_NAME" Payload/
rm -rf Payload/

# 4. 移动到桌面
echo "🚚 正在将最终文件移至桌面..."
mv "$IPA_NAME" "$DESKTOP_PATH/"

echo "✅ 完成！文件已存放在: $DESKTOP_PATH/$IPA_NAME"
echo "💡 现在你可以直接把桌面上的 IPA 拖进 Sideloadly 或其他自签工具了。"