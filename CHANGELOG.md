# Changelog

Notable changes to SimpleRay, newest first. Each entry is written in English first,
then in Russian. `scripts/build-local.ps1` copies this file into `build/` and `releases/`.

## 0.3.2 — 2026-09-10

### English
- **Reliability — stale engine cleaned at startup.** A previous run that crashed while connected
  left the sing-box engine orphaned, still holding the TUN adapter, until the next Connect. The
  app now kills a stale engine at startup (right after the single-instance guard), before it
  clears the kill-switch filters.
- **Safety — kill-switch engage hardened.** The WFP transaction start is now checked, and if the
  TUN adapter never appears the kill-switch fails open (stays off, connection continues) instead
  of installing a block-all that would cut ALL traffic on the machine.

### Русский
- **Надёжность — осиротевший движок чистится на старте.** Краш предыдущего запуска во время
  подключения оставлял sing-box сиротой, держащим TUN-адаптер, до следующего «Подключить».
  Теперь приложение убивает осиротевший движок на старте (сразу после single-instance-гарда),
  до очистки фильтров kill-switch.
- **Безопасность — упрочнён engage kill-switch.** Проверяется старт WFP-транзакции, и если
  TUN-адаптер не появился, kill-switch делает fail-open (не включается, соединение продолжается)
  вместо установки block-all, который отрубил бы весь трафик на машине.

## 0.3.1 — 2026-09-10

### English
- **Fix — LAN unreachable with the kill-switch on.** The WFP kill-switch blocked all non-tunnel
  traffic, including the local network, so LAN hosts, printers and NAS were unreachable while
  connected (a LAN ping failed with "General failure"). It now permits private, link-local,
  multicast and broadcast ranges, so the LAN stays reachable while the internet is still blocked
  if the tunnel drops.

### Русский
- **Исправление — LAN недоступен при включённом kill-switch.** WFP kill-switch блокировал весь
  не-туннельный трафик, включая локальную сеть — LAN-хосты, принтеры и NAS были недоступны при
  подключении (пинг LAN падал в «Общий сбой»). Теперь разрешены приватные, link-local, multicast
  и broadcast диапазоны: LAN доступна, а интернет по-прежнему блокируется при падении туннеля.

## 0.3.0 — 2026-09-10

### English
- **Fix — LAN unreachable while connected.** `strict_route` + `auto_route` forced local
  traffic through the tunnel (a LAN ping failed with "General failure"). RFC1918, link-local,
  multicast and broadcast ranges are now excluded from the tunnel (`route_exclude_address`),
  so local hosts, printers and NAS stay reachable while the VPN is on.
- **Security — update applied only after re-verification.** The self-updater now re-checks the
  staged files at apply time (a per-file manifest passed to the updater), closing the window
  where a local process could tamper the extracted payload after download verification but
  before the (possibly elevated) apply.
- **Security — update authenticity enabled.** Releases are signed (ECDSA P-256) and the
  updater now requires a valid signature, so a compromised GitHub release can't push a forged
  update (previously the updater fell back to a forgeable SHA-256 sidecar).
- **Reliability — engine lifecycle serialized.** `SingBoxEngine` start/stop are now mutually
  exclusive with an atomic state check, so a watchdog reconnect can no longer race a manual
  connect into two processes or a TUN conflict.
- **Build — publishable artifacts staged.** `releases/` now collects the current setup +
  portable zip (+ SHA-256) ready to publish to GitHub Releases; `build/` carries this changelog.

### Русский
- **Исправление — не проходил локальный трафик при подключении.** `strict_route` + `auto_route`
  заворачивали LAN в туннель (пинг локального адреса падал в «Общий сбой»). Диапазоны RFC1918,
  link-local, multicast и broadcast теперь исключены из туннеля (`route_exclude_address`) —
  локальные хосты, принтеры и NAS снова доступны при включённом VPN.
- **Безопасность — обновление применяется только после повторной проверки.** Автообновление
  перепроверяет распакованные файлы перед применением (пофайловый манифест передаётся апдейтеру),
  закрывая окно, в котором локальный процесс мог подменить содержимое после проверки загрузки, но
  до (возможно, привилегированного) применения.
- **Безопасность — включена подлинность обновлений.** Релизы подписываются (ECDSA P-256),
  апдейтер требует валидную подпись — скомпрометированный релиз на GitHub не подсунет поддельное
  обновление (раньше был откат на подделываемый SHA-256).
- **Надёжность — сериализован жизненный цикл движка.** start/stop у `SingBoxEngine` теперь
  взаимоисключены с атомарной проверкой состояния — реконнект watchdog больше не может столкнуться
  с ручным подключением в два процесса или конфликт TUN.
- **Сборка — артефакты для публикации.** `releases/` собирает текущий setup + portable-zip
  (+ SHA-256), готовые к публикации в GitHub Releases; `build/` несёт этот changelog.
