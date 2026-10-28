# Dual Roster — Esports Manager 2026

[Русский](#русский) · [English](#english) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3819071177)

## Русский

**0.4.1 beta · Windows x64 · Steam build 26012398.** Основа и академия вашего клуба могут участвовать в одном турнире, если каждый состав проходит по условиям игры.

### Возможности

- Независимая регистрация и отмена на странице турнира. Видны текущая позиция и требование рейтинга, число недостающих игроков, название занятого турнира. Есть напоминание, когда заявлен один состав, а второй тоже проходит.
- В приглашениях три кнопки: заявить основу, заявить академию, отказаться. Даже при двух недоступных составах от приглашения можно отказаться. Отказ закрывает оба письма и сохраняется после загрузки; существующие заявки он не отменяет.
- Билеты и отель для общей поездки оплачиваются отдельно из бюджетов обоих составов.
- В «Цифр. товарах» переключаются основа и академия, включая накопленные выплаты и автографы игроков.
- В финансах появилась вкладка «Турниры клуба»: клуб, основа или академия; текущий месяц или вся доступная история; призовые, наклейки, билеты, отель и итог по турниру. Баланс выбранных бюджетов показан отдельно от результата операций. Отчёт ничего не начисляет.
- Перед открытием карьеры мод дожидается Workshop. Можно вернуться в меню или отдельно подтвердить запуск без мода. Перед подтверждённым запуском без мода создаётся копия карьеры; мод остаётся выключен до перезапуска.
- Кнопки и сообщения мода автоматически используют русский или английский язык игры. Для остальных языков используется английский.

### Установка и переход с 0.3.x

1. Подпишитесь в Workshop и дождитесь загрузки. Закройте игру.
2. Запустите `Install.cmd` из `SteamLibrary/steamapps/workshop/content/2749950/3819071177`. При отказе Windows в записи запустите его от имени администратора.
3. В папке игры откройте `DualRoster/Steam-LaunchOptions.txt`. Скопируйте строку в Steam → свойства игры → параметры запуска. Это однократная настройка обновлятора. Если у вас уже есть собственный сторонний запускатель, сначала согласуйте их совместную настройку.
4. Запускайте игру через Steam. Перед открытием карьеры дождитесь статуса `Dual Roster: 0.4.1 включён`.

**При переходе с 0.3.x установщик и настройка параметров запуска нужны один раз.** Обычная подписка сама по себе не устанавливает загрузчик с кодом.

Обновлятор запускается перед игрой, ждёт загрузку Workshop и проверяет совместимость и контрольные суммы. Когда мы уже выпустили пакет для новой сборки игры, он заменит нужные компоненты автоматически. Если совместимого пакета ещё нет, обновлятор остановит запуск и объяснит причину. Поддержку нового патча игры всё равно нужно проверить и выпустить; автоматическая установка не заменяет эту работу.

При изменении компонентов сохраняются их предыдущие версии и копия `Save` в `DualRosterBackups/update-*` внутри папки игры. При ошибке записи выполняется откат, а после прерванного обновления восстановление проверяется на следующем запуске. Сохранения обновлятор не исправляет и не пересчитывает.

### Финансовая история

Суммы берутся из штатных операций и бюджетов. Для новых призовых и поездок мод сохраняет связь с турниром в `Save/DualRoster/finance-<slot>.json`. История наклеек связывается по штатным ID турниров и месячным выплатам только при точном совпадении с поступлениями бюджета. Старые или неполные записи без надёжной связи показываются отдельно. Удалённые самой игрой операции мод не восстанавливает.

Журнал хранит только привязки: повреждение или отсутствие журнала не меняет деньги. Сохраняйте всю папку `Save`, включая `DualRoster`, когда переносите карьеру.

### Отключение

Отпишитесь и перезапустите игру. Загрузчик предложит явно подтвердить вход без мода. Штатная логика игры при этом может снять совместную заявку академии — именно поэтому перед входом создаётся копия.

Для полного удаления сначала уберите строку из параметров запуска Steam, затем при закрытой игре удалите `BepInEx/plugins/DualRosterWorkshop/DualRoster.Workshop.dll` и папку `DualRoster` в папке игры. Не удаляйте чужие плагины. Резервные копии и карьеру сохраняйте отдельно.

### Сборка и проверки

Установите совместимый пакет, затем .NET SDK 10:

```powershell
./Build.ps1 -GamePath 'C:\Program Files (x86)\Steam\steamapps\common\Esports Manager 2026'
dotnet run --project tests/WorkshopChecks.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests/UpdaterChecks.ps1 -WorkRoot '<temporary workspace>'
```

DLL появляются в `build/`; сборка не устанавливает их в игру. `src/` — игровая логика, `loader/` — загрузчик и защита карьеры, `shared/` — локализация и элементы интерфейса, `launcher/` — обновлятор до запуска, `installer/` — первичная установка.

Интеграционные проверки включаются только через `-p:TestBuild=true` и требуют отдельной копии игры и сохранений с маркером `.dualroster-test-copy`. Они меняют тестовую карьеру. Исходники не содержат сохранений, журналов пользователя, исполняемых файлов игры или игровых ресурсов. Подробности сборки и фактически выполненных проверок: `BUILD.txt`, `VALIDATION.txt`.

## English

**0.4.1 beta · Windows x64 · Steam build 26012398.** Register your club's main team and academy in the same event when both meet the game's requirements.

- Independent tournament registration and withdrawal, exact rank/player requirements, the conflicting event's name, and a warning when an eligible sibling roster has not entered.
- Three invitation actions: register main, register academy, decline. Decline remains available when neither roster is eligible, closes both invitations and survives reload. Existing entries are preserved.
- Separate flights and hotel charges for both participating rosters; main/academy sticker totals and player autographs.
- A new **Club events** finance tab with club/main/academy scope, this-month/all-history filters and per-event prizes, stickers, flights, hotel and net. Current wallet balance is separate. Viewing reports never creates money.
- A career gate waits for Workshop or asks you to explicitly load without the mod. The latter makes a career backup and keeps the mod disabled until restart.
- Mod UI follows the game's Russian/English locale, with English fallback for other languages.

### One-time upgrade/setup

Subscribe and let Steam finish downloading. Close the game and run `Install.cmd` from `SteamLibrary/steamapps/workshop/content/2749950/3819071177`. If Windows denies write access, run as administrator.

Copy the line from the installed game's `DualRoster/Steam-LaunchOptions.txt` into that game's Steam launch options. **Upgrading from 0.3.x requires this installer and launch-option setup once.** Do not overwrite an unrelated third-party launcher configuration without checking how they should be combined. Launch through Steam normally and wait for the main-menu `active` status.

The prelaunch updater waits for subscribed content, verifies the exact game hash and package checksums, and updates compatibility components before the game loads them. A new game build still needs a tested release from us. If no compatible release is available, it stops the launch with an explanation. Subscribing alone cannot perform the initial code-loader installation.

Changed components and the career directory are backed up under the game's `DualRosterBackups/update-*`. Failed writes roll back; interrupted updates are checked at the next launch. The updater does not edit career contents or award historical money.

### Accounting and removal

Reports read native ledger amounts. New travel/prize links are kept in `Save/DualRoster/finance-<slot>.json`. Native sticker history is attributed by exact tournament IDs only when its monthly sum reconciles with the wallet credits. Unlinked older/incomplete records remain separate; deleted native transactions cannot be reconstructed. Journal loss never changes balances. Include `Save/DualRoster` when moving a career.

Unsubscribe and restart to disable gameplay code; the career gate requires explicit confirmation. The base game may remove the academy's shared entry when loading without this mod. To remove the loader completely, first clear its Steam launch option, then close the game and remove `BepInEx/plugins/DualRosterWorkshop/DualRoster.Workshop.dll` and the game's `DualRoster` folder. Preserve careers, backups and other mods.

Build with .NET SDK 10 and `Build.ps1 -GamePath '<game folder>'`. See the commands above, `BUILD.txt` and `VALIDATION.txt`. Integration harnesses require an isolated marked save/game copy and are excluded from production.

## License

Original mod, launcher, installer and tests: **MIT**, copyright 2026 Danila Shubin (ShyDamn) and contributors. Dependencies keep their licenses; the complete modified LGPL runtime source and notices are included under `third-party/`. See `LICENSE` and `THIRD-PARTY.txt`.

Unofficial community mod, not affiliated with Neurona Games. No original game executables, implementation or media are distributed.