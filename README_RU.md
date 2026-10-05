# ValCraft — Minecraft внутри Valheim

Настоящий Minecraft Java 26.3 работает рядом с Valheim и вписывается в его кадр: Steve ходит по миру Valheim,
ставит и ломает блоки, а мечи, стрелы, TNT и криперы действуют на существ, деревья и землю Valheim.
Сделано по схеме Minecraft-Ring / SkyCraft на основе примера `minecraft-gta5-passthrough` из universal-modder.

## Как это устроено
- **Minecraft** (Fabric-мод `mc/`) открывает пустой мир, берёт камеру Valheim по WebSocket `127.0.0.1:25599`
  и отдаёт каждый кадр (цвет + глубина) через shared memory.
- **Valheim**:
  - BepInEx-плагин `valheim/plugin` отправляет камеру и позицию игрока, превращает землю Valheim в невидимые
    барьеры Minecraft, а блоки Minecraft в коллайдеры Valheim (по постройкам можно ходить). Переводит удары, стрелы
    и взрывы в урон Valheim, взрывы оставляют кратеры, мобы Minecraft дерутся с существами Valheim.
  - ReShade-аддон `valheim/addon` вместе с шейдером `MCPassthrough.fx` смешивает кадр Minecraft с кадром Valheim
    по глубине, до отрисовки интерфейса.

## Запуск
1. Один раз запусти `prism\prismlauncher.exe` и войди своим Microsoft-аккаунтом (Аккаунты → Добавить Microsoft).
2. Дальше просто запускай **`ValCraft.bat`**: он стартует Minecraft (инстанс ValCraft), а затем Valheim в режиме DX11.
   В Steam ничего переключать не нужно, батник передаёт `-force-d3d11` сам.

## Управление
| Клавиша | Что делает |
|---|---|
| F6 | мышь и хотбар: руки Minecraft ↔ оружие Valheim |
| F7 | включить/выключить Minecraft |
| F8 | заново выровнять землю Minecraft под тобой |
| ЛКМ / ПКМ | (руки Minecraft) ломать/бить и ставить/использовать |
| колесо, 1–9 | хотбар Minecraft |
| Q | выбросить предмет Minecraft |

Ходьба, прыжки, камера, взаимодействие (E), инвентарь и меню Valheim работают как обычно.

## Установка и удаление
- Установить или обновить: `powershell -File valheim\install.ps1`
- Удалить ValCraft из папки Valheim: `powershell -File valheim\install.ps1 -Remove`
- BepInEx удаляется по списку файлов из `backup\bepinex-installed-files.txt`. Бэкап сохранений лежит в `backup\`.

## Сборка
- Мод Minecraft: `cd mc && gradlew build` (JDK 25)
- Плагин: `cd valheim\plugin && dotnet build -c Release` (.NET SDK 8)
- Аддон: `valheim\addon\build.bat` (MSVC). Заголовки ReShade лежат в `gta\third_party` (скачиваются, как в `gta/fetch_deps.sh`).

## Ограничения
- Только одиночная игра или свой сервер.
- Шейдер подстраивает свет и цвет Minecraft под картинку Valheim, но тени от блоков пока «контактные», а не настоящие.
- Мобы Valheim бьют мобов Minecraft, когда стоят рядом с ними, но сами за ними не охотятся.
