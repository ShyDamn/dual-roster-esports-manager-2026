# Dual Roster — Esports Manager 2026

[Русский](#русский) · [English](#english) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3819071177)

## Русский

Мод позволяет заявить основу и академию одного клуба на один турнир, если каждый состав проходит по рейтингу и не занят на другом турнире.

- Отдельная регистрация и отмена на странице турнира.
- В письмах три кнопки: «Заявить основу», «Заявить академию», «Отказаться». Отказ закрывает оба приглашения и сохраняется после загрузки карьеры. Уже поданные заявки он не отменяет.
- При общей поездке билеты и отель оплачиваются за каждый состав из его собственного бюджета.
- В разделе «Цифр. товары» есть переключатель «Основа / Академия». Меняются суммы, график, наборы наклеек и доход от автографов по никнеймам игроков.
- Загрузчик берёт мод из подписки Workshop при запуске игры. Обычные обновления мода применяются без повторной установки. Отписка отключает мод при следующем запуске.

### Установка

Версия **0.3.1 beta**, Windows x64, **Steam build 26012398**. Интерфейс игрового мода на русском; инструкции на русском и английском.

1. Подпишитесь на мод в Workshop и дождитесь загрузки.
2. Закройте игру и сделайте резервную копию карьеры.
3. Откройте `SteamLibrary/steamapps/workshop/content/2749950/3819071177` и запустите `Install.cmd`.
4. Запускайте игру через Steam. В главном меню дождитесь статуса `Dual Roster: 0.3.1 active / 0.3.1 включён`.

После обновления игры до build 26012398 запустите новый установщик **повторно**, в том числе при переходе с 0.3.0. Версия 0.3.1 обновляет привязки к игре и загрузчик. Старые файлы сохраняются в резервной копии. При ошибке записи в папку игры запустите установщик от имени администратора.

После обновления самой игры может понадобиться новая версия загрузчика и повторная установка. Подписка без первоначальной установки загрузчика не запускает мод с кодом.

### Сборка из исходников

Установите поддерживаемый Workshop-пакет: он содержит BepInEx и API-обёртки для этой сборки игры. Затем установите .NET SDK 10 и выполните:

```powershell
./Build.ps1 -GamePath 'C:\Program Files (x86)\Steam\steamapps\common\Esports Manager 2026'
dotnet run --project tests/WorkshopChecks.csproj -c Release
```

DLL появятся в `build/`. Скрипт сборки не устанавливает их в игру. `src/` содержит игровую логику, `loader/` — загрузчик Workshop, `installer/` — установщик, `tests/` — проверки загрузки пакета.

Разработка игрового мода: `-p:TestBuild=true` включает интеграционные проверки из `src/*Checks.cs`. Используйте только отдельную копию игры и сохранения с маркером `.dualroster-test-copy`; тесты меняют карьеру. В `RosterPanel.cs` задаётся тестовый слот. Карьеры и журналы пользователя в репозиторий не входят.

### Что проверено и что ограничено

Проверены обе очередности регистрации, отказ от приглашений и его сохранение, раздельные бюджеты поездок, штатные призовые и выплаты наклеек, отображение академии на экране наклеек, загрузка пакета и ожидание обновления. Проверки выполнялись на изолированных копиях карьеры. Полного многосезонного прогона и проверки всех сочетаний модов нет.

Мод не пересчитывает прошлые недоплаты за поездки и не начисляет призовые или наклейки повторно. После отключения штатная логика игры может удалить совместную заявку академии. Загрузчик не меняет DLL посреди запущенной игры.

Чтобы отключить мод, отпишитесь и перезапустите игру. Для удаления загрузчика при закрытой игре удалите `BepInEx/plugins/DualRosterWorkshop/DualRoster.Workshop.dll`. Резервные копии изменённых установщиком файлов находятся в `DualRosterBackups` внутри папки игры.

## English

Dual Roster lets your club's main team and academy enter the same tournament. Both must meet the ranking requirements and be free of another event.

- Independent registration and cancellation on the tournament page.
- Three invitation buttons: register main, register academy, or decline. Declining closes both letters, survives save/load and keeps existing tournament entries.
- Shared travel choices charge flights and hotels separately to each roster's own budget.
- A main/academy selector in Digital Goods switches totals, the chart, sticker sets and autograph income by player nickname.
- After one-time loader installation, the mod loads from its Workshop subscription on game launch. Ordinary updates need no installer rerun; unsubscribing disables the mod on the next launch.

### Install and build

**0.3.1 beta; Windows x64; Steam build 26012398 only. Gameplay UI is Russian.** Subscribe, wait for Steam, close the game, back up your career, and run `Install.cmd` from `SteamLibrary/steamapps/workshop/content/2749950/3819071177`. Launch normally through Steam and wait for the main-menu status to say `active` before opening a career.

After the game update to build 26012398, rerun the new installer, including when upgrading from 0.3.0. Version 0.3.1 refreshes the generated game bindings and loader. Existing files are backed up. If Windows denies write access, run as administrator. Updates to the game or loader compatibility components may require another installation.

To build, install the supported Workshop package and .NET SDK 10, then run `Build.ps1 -GamePath '<game directory>'`. The two production DLLs are written to `build/`; no game files are changed. Run `dotnet run --project tests/WorkshopChecks.csproj -c Release` for package checks. See `BUILD.txt` for dependency and runtime rebuild details.

Integration tests are opt-in (`-p:TestBuild=true`) and require an isolated game/save copy. Never point them at a live career. Saves, user logs, original game binaries and game assets are not distributed.

Travel fixes apply going forward. Prize and sticker calculations remain native; rewards are not duplicated. Full multi-season runs and all mod combinations have not been tested. The base game may remove shared academy registrations after disabling the mod. The loader never swaps gameplay code mid-session.

Unsubscribe and restart to disable the mod. To remove the loader, close the game and delete `BepInEx/plugins/DualRosterWorkshop/DualRoster.Workshop.dll`. Installation backups are under the game's `DualRosterBackups` directory.

## License

Original Dual Roster code, loader, tests and installer: **MIT**, copyright 2026 Danila Shubin (ShyDamn) and contributors. See `LICENSE`.

Dependencies retain their own licenses. The compatibility runtime is a separate LGPL library; its complete modified source archive and license are included under `third-party/`. See `THIRD-PARTY.txt` and `BUILD.txt`. MIT does not relicense BepInEx, Il2CppInterop or the game.

Unofficial community mod, not affiliated with the game's developer.
