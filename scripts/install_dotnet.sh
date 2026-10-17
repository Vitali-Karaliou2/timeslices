#!/bin/bash
# SessionStart-хук: ставит .NET SDK (LTS) в облачной сессии Claude Code, если его нет.
# Локально (на вашей машине) ничего не делает: CLAUDE_CODE_REMOTE там не равен "true".
# В облачной VM .NET не предустановлен (см. docs Claude Code: Installed tools), но
# dotnet.microsoft.com, dot.net и nuget.org входят в список разрешённых доменов уровня Trusted.

if [ "$CLAUDE_CODE_REMOTE" != "true" ]; then
  exit 0
fi

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

if command -v dotnet >/dev/null 2>&1; then
  echo "dotnet уже установлен: $(dotnet --version)"
else
  echo "Устанавливаю .NET SDK (LTS) в $DOTNET_ROOT ..."
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh || exit 0
  bash /tmp/dotnet-install.sh --channel LTS --install-dir "$DOTNET_ROOT" || exit 0
  echo "Установлено: $(dotnet --version)"
fi

# dotnet-ef нужен для миграций; ставим только если отсутствует.
if ! dotnet tool list -g 2>/dev/null | grep -q dotnet-ef; then
  dotnet tool install -g dotnet-ef >/dev/null 2>&1 || true
fi

# Делаем PATH видимым для команд, которые будет запускать Claude в этой сессии.
# (Файл профиля читается bash-инструментом при старте каждой команды.)
grep -q 'DOTNET_ROOT' "$HOME/.bashrc" 2>/dev/null || {
  echo 'export DOTNET_ROOT="$HOME/.dotnet"' >> "$HOME/.bashrc"
  echo 'export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"' >> "$HOME/.bashrc"
  echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1' >> "$HOME/.bashrc"
}
exit 0
