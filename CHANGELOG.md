# Changelog

Notable changes to SimpleRay, newest first. Each entry is written in English first,
then in Russian. `scripts/build-local.ps1` copies this file into `build/` and `releases/`.

## 0.3.6 — 2026-11-09

### English
- **Main window — subscriptions panel.** A collapsible panel on the left lists "All servers",
  "Added manually" and every subscription (server count, traffic, expiry, provider message, last
  update); picking one filters the server list, whose header says what is shown. A vertical
  toggle hides/shows the panel (remembered); adding a subscription opens it on the new one. The
  separate subscriptions window is gone.
- **One smart "Add".** Clipboard, file, QR (file / clipboard / webcam) and a new "Enter
  manually…" all sort the input themselves: subscription URLs and other apps' import links
  become subscriptions; server links, WireGuard .conf, Xray JSON and base64 bodies become
  servers; mixed input is split.
- **Buttons in groups**, one row each: Add · Rename / Remove (act on the list you clicked last: a
  subscription — name and User-Agent / with its servers — or a server) · Ping / Refresh (the
  selected subscription, or all). Ping measures only the servers on screen. The window is larger
  by default so everything fits.

### Русский
- **Главное окно — панель подписок.** Слева сворачиваемая панель: «Все серверы», «Добавленные
  вручную» и каждая подписка (число серверов, трафик, срок, сообщение провайдера, время
  обновления); выбор пункта фильтрует список серверов, в заголовке которого видно, что показано.
  Вертикальная кнопка скрывает и показывает панель (запоминается); при добавлении подписки панель
  открывается на ней. Отдельного окна подписок больше нет.
- **Одна умная кнопка «Добавить».** Буфер, файл, QR (файл / буфер / камера) и новый пункт «Ввести
  вручную…» сами разбирают ввод: URL подписок и ссылки импорта других приложений становятся
  подписками; ссылки серверов, WireGuard .conf, Xray JSON и base64-содержимое — серверами;
  смешанный ввод разделяется.
- **Кнопки по группам**, каждая на своей строке: «Добавить» · «Переименовать» / «Удалить»
  (действуют на список, где вы кликнули последним: подписку — название и User-Agent / вместе с
  серверами — или сервер) · «Пинг» / «Обновить» (выбранную подписку или все). Пинг меряет только
  серверы на экране. Окно по умолчанию больше, чтобы всё помещалось.

## 0.3.5 — 2026-11-08

### English
- **Hysteria2 — port hopping.** Links with a port list in the address
  (`hysteria2://…@host:443,20000-30000`) or an `mport=` parameter are now imported instead of
  skipped, with an optional `hop_interval`; they become sing-box `server_ports`.

### Русский
- **Hysteria2 — смена портов (port hopping).** Ссылки со списком портов в адресе
  (`hysteria2://…@host:443,20000-30000`) или параметром `mport=` теперь импортируются, а не
  пропускаются, вместе с необязательным `hop_interval`; в sing-box они становятся `server_ports`.

## 0.3.4 — 2026-11-08

### English
- **Subscriptions — management window** (Add → Manage subscriptions…). Lists every saved
  subscription with its host, server count, last update, traffic, expiry and User-Agent, and lets
  you add, refresh one or all, rename, change the User-Agent (refetches) or remove a subscription
  together with its servers (asks first).
- **Subscriptions — provider stubs.** Placeholder "servers" that providers send as messages (port
  1, 0.0.0.0/127.0.0.1, all-zero UUID — e.g. "App not supported", "Subscription expired") are no
  longer imported as servers; their text is shown as a provider message. A subscription that
  returns only such a stub is still saved, so its User-Agent can be fixed in the window.
- **Protocols — Hysteria2 and SOCKS5.** `hysteria2://` / `hy2://` links (auth, salamander obfs,
  SNI, insecure, ALPN, bandwidth hints) and `socks://` / `socks5://` links (v2rayN base64 or
  plain `user:pass`), plus SOCKS outbounds in Xray JSON subscriptions. Port-hopping Hysteria2
  links are not supported yet. Ping shows "UDP" for WireGuard/Hysteria2 instead of a false dash.
- **Security — subscriptions encrypted at rest.** `subscriptions.json` (URLs with access tokens)
  is now DPAPI-encrypted like the profiles; an existing plaintext file is read and encrypted on
  the next save. Older versions can't read the encrypted file, so going back would lose the
  subscription list (servers are kept).
- **Subscriptions — provider links.** When the provider sends `support-url` /
  `profile-web-page-url`, "Support" and "Website" links appear under the selected server's
  subscription info and as buttons in the subscriptions window. Links open in the default browser
  without admin rights.

### Русский
- **Подписки — окно управления** («Добавить → Управление подписками…»). Показывает все
  сохранённые подписки: сервер, число серверов, время обновления, трафик, срок и User-Agent; можно
  добавить, обновить одну или все, переименовать, сменить User-Agent (с повторной загрузкой) или
  удалить подписку вместе с её серверами (после подтверждения).
- **Подписки — заглушки провайдера.** Фиктивные «серверы», которыми провайдеры передают
  сообщения (порт 1, 0.0.0.0/127.0.0.1, нулевой UUID — например «App not supported», «Подписка
  истекла»), больше не импортируются как серверы; их текст показывается как сообщение провайдера.
  Подписка, вернувшая только заглушку, всё равно сохраняется, чтобы в окне можно было сменить ей
  User-Agent.
- **Протоколы — Hysteria2 и SOCKS5.** Ссылки `hysteria2://` / `hy2://` (пароль, обфускация
  salamander, SNI, insecure, ALPN, подсказки скорости) и `socks://` / `socks5://` (base64 как в
  v2rayN или обычный `user:pass`), а также SOCKS в подписках Xray JSON. Ссылки Hysteria2 с
  диапазоном портов пока не поддерживаются. Пинг для WireGuard/Hysteria2 показывает «UDP» вместо
  ложного прочерка.
- **Безопасность — подписки шифруются на диске.** `subscriptions.json` (URL с токенами доступа)
  теперь шифруется DPAPI, как и профили; существующий открытый файл читается и шифруется при
  следующем сохранении. Старые версии не прочитают зашифрованный файл, поэтому при откате список
  подписок потеряется (серверы останутся).
- **Подписки — ссылки провайдера.** Если провайдер присылает `support-url` /
  `profile-web-page-url`, под информацией о подписке выбранного сервера появляются ссылки
  «Поддержка» и «Сайт», а в окне подписок — такие же кнопки. Ссылки открываются в браузере по
  умолчанию без прав администратора.

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
