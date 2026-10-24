#!/bin/bash
# SessionStart-хук: ставит .NET SDK (LTS) в облачной сессии Claude Code, если его нет.
# Локально (на вашей машине) ничего не делает: CLAUDE_CODE_REMOTE там не равен "true".
# В облачной VM .NET не предустановлен (см. docs Claude Code: Installed tools).
# Проверено 17.10.2026: CDN Microsoft (builds.dotnet.microsoft.com) сетевая политика облака
# не пропускает, а архив пакетов Ubuntu и nuget.org доступны. Поэтому основной путь в облаке
# фактически — apt (запасная ветка ниже); dotnet-install.sh оставлен для других окружений.

if [ "$CLAUDE_CODE_REMOTE" != "true" ]; then
  exit 0
fi

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

if command -v dotnet >/dev/null 2>&1; then
  echo "dotnet уже установлен: $(dotnet --version)"
else
  echo "Устанавливаю .NET SDK (LTS) в $DOTNET_ROOT ..."
  if curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
     && bash /tmp/dotnet-install.sh --channel LTS --install-dir "$DOTNET_ROOT"; then
    echo "Установлено: $(dotnet --version)"
  elif command -v apt-get >/dev/null 2>&1; then
    # Запасной путь: сетевая политика облачной среды может не пускать на builds.dotnet.microsoft.com,
    # а архив Ubuntu доступен. dotnet-sdk-10.0 — текущий LTS; ставится в /usr/lib/dotnet.
    echo "dotnet-install.sh не сработал; ставлю dotnet-sdk-10.0 из пакетов Ubuntu ..."
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq >/dev/null 2>&1
    apt-get install -y -qq -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold \
      dotnet-sdk-10.0 >/dev/null 2>&1 || exit 0
    echo "Установлено: $(dotnet --version)"
  else
    exit 0
  fi
fi

# dotnet-ef нужен для миграций; ставим только если отсутствует.
if ! dotnet tool list -g 2>/dev/null | grep -q dotnet-ef; then
  dotnet tool install -g dotnet-ef >/dev/null 2>&1 || true
fi

# Документированный способ передать переменные командам сессии (SessionStart): Claude Code выполняет
# содержимое CLAUDE_ENV_FILE перед каждой командой Bash. Путь к dotnet-ef — $HOME/.dotnet/tools и при установке
# из пакета Ubuntu. DOTNET_ROOT — настоящая папка dotnet: при установке из пакета это /usr/lib/dotnet, а не
# $HOME/.dotnet, и с неверным DOTNET_ROOT глобальные инструменты (dotnet-ef) не находят среду выполнения.
if [ -n "$CLAUDE_ENV_FILE" ]; then
  REAL_DOTNET_ROOT="$(dirname "$(readlink -f "$(command -v dotnet)")" 2>/dev/null)"
  [ -d "$REAL_DOTNET_ROOT" ] || REAL_DOTNET_ROOT="$HOME/.dotnet"
  echo "export DOTNET_ROOT=\"$REAL_DOTNET_ROOT\"" >> "$CLAUDE_ENV_FILE"
  echo 'export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"' >> "$CLAUDE_ENV_FILE"
  echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1' >> "$CLAUDE_ENV_FILE"
fi

# Запасной путь: файл профиля, который bash-инструмент читает при старте каждой команды.
grep -q 'DOTNET_ROOT' "$HOME/.bashrc" 2>/dev/null || {
  echo 'export DOTNET_ROOT="$HOME/.dotnet"' >> "$HOME/.bashrc"
  echo 'export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"' >> "$HOME/.bashrc"
  echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1' >> "$HOME/.bashrc"
}
exit 0
