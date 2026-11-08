# Changelog

Notable changes to SimpleRay, newest first. Each entry is written in English first,
then in Russian. `scripts/build-local.ps1` copies this file into `build/` and `releases/`.

## Unreleased

### English
- **Subscriptions — management window** (Add → Manage subscriptions…). Lists every saved
  subscription with its host, server count, last update, traffic, expiry and User-Agent, and lets
  you add, refresh one or all, rename, change the User-Agent (refetches) or remove a subscription
  together with its servers (asks first).

### Русский
- **Подписки — окно управления** («Добавить → Управление подписками…»). Показывает все
  сохранённые подписки: сервер, число серверов, время обновления, трафик, срок и User-Agent; можно
  добавить, обновить одну или все, переименовать, сменить User-Agent (с повторной загрузкой) или
  удалить подписку вместе с её серверами (после подтверждения).

## 0.3.3 — 2026-11-07

### English
- **Subscriptions — Happ format.** Reads provider metadata from response headers or `#key: value`
  lines at the top of the body: `profile-title` (incl. `base64:`), `subscription-userinfo`
  (traffic used / quota, expiry), `profile-update-interval`, `support-url`,
  `profile-web-page-url`, `announce`. Title, traffic, expiry and announcement are shown under the
  status for the selected server.
- **Subscriptions — Xray JSON bodies.** A JSON array of full Xray configs (what Remnawave-style
  panels serve to Happ) is imported: vless/vmess/trojan/shadowsocks over tcp/ws/grpc/http with
  TLS or REALITY. Servers on transports sing-box can't run (xhttp, kcp…) are skipped and counted.
- **Subscriptions — import links of other clients** are accepted in the Add dialog:
  `happ://add/<url>`, `throne://subscribe?url=…`, `sing-box://import-remote-profile?url=…`,
  `clash://install-config?url=…`, `hiddify://import/…` and similar. Encrypted
  `happ://crypt…` links and AES-encrypted content are recognised and reported as unsupported.
- **Subscriptions — auto-update.** Each subscription refreshes on the provider's
  `profile-update-interval` (default 24 h); an update missed while the app was closed runs on
  launch. A refresh keeps the selected and connected server, and an empty answer no longer wipes
  the list.
- **Subscriptions — custom User-Agent.** Each subscription can carry its own User-Agent (asked
  after the URL in the Add dialog; re-add the URL to change it), for providers that serve servers
  only to the client the link was issued for.
- **Subscriptions — "what is supported" hint.** The Add → Subscription menu item and the URL
  dialog list the apps whose subscriptions are accepted, the link formats and what isn't
  supported. The input dialog now grows to fit long prompts.

### Русский
- **Подписки — формат Happ.** Метаданные провайдера читаются из заголовков ответа или строк
  `#ключ: значение` в начале тела: `profile-title` (в т.ч. `base64:`), `subscription-userinfo`
  (израсходованный трафик / лимит, срок), `profile-update-interval`, `support-url`,
  `profile-web-page-url`, `announce`. Название, трафик, срок и объявление показываются под
  статусом для выбранного сервера.
- **Подписки — тело в виде Xray JSON.** Импортируется JSON-массив полных конфигов Xray (так
  панели вроде Remnawave отдают подписку для Happ): vless/vmess/trojan/shadowsocks поверх
  tcp/ws/grpc/http с TLS или REALITY. Серверы на транспортах, которых нет в sing-box (xhttp,
  kcp…), пропускаются с подсчётом.
- **Подписки — ссылки импорта других клиентов** принимаются в диалоге добавления:
  `happ://add/<url>`, `throne://subscribe?url=…`, `sing-box://import-remote-profile?url=…`,
  `clash://install-config?url=…`, `hiddify://import/…` и подобные. Зашифрованные
  `happ://crypt…` и AES-зашифрованное содержимое распознаются и сообщаются как неподдерживаемые.
- **Подписки — автообновление.** Каждая подписка обновляется с интервалом провайдера
  `profile-update-interval` (по умолчанию 24 ч); пропущенное при закрытой программе обновление
  выполняется при запуске. Обновление сохраняет выбранный и подключённый сервер, а пустой ответ
  больше не стирает список.
- **Подписки — свой User-Agent.** У каждой подписки может быть свой User-Agent (спрашивается
  после URL в диалоге добавления; чтобы изменить — добавьте тот же URL ещё раз) — для провайдеров,
  которые отдают серверы только клиенту, для которого выдана ссылка.
- **Подписки — подсказка «что поддерживается».** Пункт «Добавить → Подписка» и окно ввода URL
  показывают, подписки каких приложений принимаются, форматы ссылок и что не поддерживается.
  Окно ввода теперь растягивается под длинный текст.

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
