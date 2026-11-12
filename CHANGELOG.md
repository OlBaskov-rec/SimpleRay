# Changelog

Notable changes to SimpleRay, newest first. Each entry is written in English first,
then in Russian. `scripts/build-local.ps1` copies this file into `build/` and `releases/`.

## 0.3.10 — 2026-11-12

### English
- **Fix — switching the language blanked the routing mode.** After a language change the mode box
  went empty with a red border: the list of modes was rebuilt on every switch, so the selection
  was lost. The choices now stay the same and only their labels change.
- **Your routing lists.** The box is titled that way, and its three buttons carry icons — a program
  window (per-app rules, either way), a globe (sites going direct), a shield (sites through the
  VPN) — and are evenly spaced.
- **Clearer wording.** The DNS resolver hint now says what it resolves: the sites that go direct,
  queried straight from this computer, while every other site is resolved separately, encrypted,
  through the VPN. Its tooltip adds that a plain-UDP resolver (Yandex) needs outbound port 53. In
  Russian and Ukrainian the via-VPN button no longer says "в обход" ("bypass") — the same panel used
  that word for the opposite, direct traffic.

### Русский
- **Исправлено: смена языка сбрасывала режим маршрутизации.** После переключения языка поле режима
  становилось пустым с красной рамкой: список режимов пересоздавался при каждой смене, и выбор
  терялся. Теперь варианты остаются те же, меняются только подписи.
- **Свои списки маршрутизации.** Блок так и называется, у трёх кнопок появились значки — окно
  программы (правила для приложений, в обе стороны), глобус (сайты напрямую), щит (сайты через
  VPN), — и ровные отступы.
- **Понятнее тексты.** Подсказка DNS-резолвера объясняет, что он разрешает: сайты, которые идут
  напрямую, — запросы уходят прямо с этого компьютера; остальные сайты разрешаются отдельно,
  шифрованно, через VPN. Во всплывающей подсказке — что резолверу на UDP (Яндекс) нужен открытый
  исходящий порт 53. В русском и украинском кнопка «через VPN» больше не называется «в обход»: тем
  же словом в этом же блоке обозначался противоположный, прямой трафик.

## 0.3.9 — 2026-11-11

### English
- **Release files carry no version.** The portable archive is now `SimpleRay-win-x64.zip` and the
  installer `SimpleRay-setup.exe`, so an unzipped folder never shows an old version after an
  in-app update. The folder holds `ABOUT.txt` with the current version and the changelog; it is
  replaced on every update. Older installs still find and install updates.
- **Close goes to the tray.** The window's close button hides SimpleRay to the tray instead of
  quitting (the connection keeps running); the first time, a balloon from the tray icon says so.
  Quit with right-click on the tray icon → Exit.
- **Updates in the background.** "Updates" is now a link next to "About". The app checks GitHub
  quietly 20 s after launch and every 6 hours; when a new version is out the link turns green,
  shows the version and blinks three times. Installing still asks first. The language picker
  moved to the left edge.
- **Clearer settings.** "Directly, bypassing VPN" now comes first, with the custom lists
  (apps, direct domains, via-VPN domains) labeled in their own box; the options (ads, autostart,
  kill-switch, IPv6) follow. The auto-switch is a single checkbox — the old "manual (selector)"
  mode had no way to pick a server and only used the first grouped one.
- **Tooltips everywhere.** Every control explains itself: a bold headline, what it does, and how
  it works, in larger text, also on disabled buttons. "Refresh" is now "Refresh subscriptions",
  set further apart from the tools.
- **Lists.** Rows keep clear of the scrollbar.

### Русский
- **Файлы релиза без номера версии.** Портативный архив теперь `SimpleRay-win-x64.zip`, установщик —
  `SimpleRay-setup.exe`, поэтому распакованная папка не показывает старую версию после обновления
  из программы. В папке лежит `ABOUT.txt` с текущей версией и changelog; он заменяется при каждом
  обновлении. Уже установленные копии по-прежнему находят и ставят обновления.
- **Крестик сворачивает в трей.** Кнопка закрытия окна прячет SimpleRay в трей, а не закрывает
  программу (подключение продолжает работать); в первый раз об этом скажет всплывающее сообщение
  у значка в трее. Выход — правый щелчок по значку → «Выход».
- **Обновления в фоне.** «Обновления» — теперь надпись рядом с «О программе». Программа тихо
  проверяет GitHub через 20 с после запуска и раз в 6 часов; при выходе новой версии надпись
  становится зелёной, показывает версию и мигает три раза. Установка по-прежнему с вопросом.
  Выбор языка перенесён к левому краю.
- **Понятнее настройки.** Сначала идёт «Напрямую, минуя VPN», свои списки (приложения, прямые
  домены, домены через VPN) подписаны и выделены отдельным блоком; за ними — параметры (реклама,
  автозапуск, kill-switch, IPv6). Автопереключение — одна галочка: старый режим «ручной
  (selector)» не давал выбрать сервер и просто брал первый из группы.
- **Подсказки везде.** Каждый элемент объясняет себя: жирный заголовок, что делает и как
  работает, крупнее, в том числе на неактивных кнопках. «Обновить» стало «Обновить подписки» и
  стоит дальше от инструментов.
- **Списки.** Строки не заходят под полосу прокрутки.

## 0.3.8 — 2026-11-10

### English
- **Speed test.** A new "Speed" button measures the download speed through the selected server:
  up to 10 MB, no longer than 10 s, from speed.cloudflare.com, through the same throwaway local
  sing-box as "Ping via server" (the current connection is not touched). The result stays next
  to the server as a third figure, "↓ N Mbit/s". It runs only on demand, since it spends
  subscription traffic.
- **Fix — layout.** The button rows no longer stretch the server column and push the right
  column off the window.

### Русский
- **Замер скорости.** Новая кнопка «Скорость» меряет скорость скачивания через выбранный сервер:
  до 10 МБ, не дольше 10 с, с speed.cloudflare.com, через тот же временный локальный sing-box,
  что и «Пинг через сервер» (текущее подключение не затрагивается). Результат остаётся у сервера
  третьей цифрой «↓ N Мбит/с». Запускается только по кнопке, потому что тратит трафик подписки.
- **Исправление — компоновка.** Строки кнопок больше не растягивают колонку серверов и не
  выталкивают правую колонку за край окна.

## 0.3.7 — 2026-11-10

### English
- **Ping through the server.** A new "Ping via server" button checks that the VPN really works:
  through each server on screen it requests the same test page the auto-switch uses and shows a
  second figure per server (green ms, or red "no answer"); working servers are sorted first. It
  works while disconnected and also tests UDP protocols (WireGuard, Hysteria2). For the
  duration of the check a separate sing-box runs on 127.0.0.1 only, behind a random
  username/password, then is stopped and its config deleted; the current connection is not
  touched. If one server's config is broken, the others are still checked one by one.
- **Ping explained.** Both ping buttons have tooltips: the old one is a direct TCP connection to
  the server's port (port open, not "VPN works"), the new one is the real check.

### Русский
- **Пинг через сервер.** Новая кнопка «Пинг через сервер» проверяет, что VPN реально работает:
  через каждый сервер на экране запрашивается та же тестовая страница, что у автопереключателя,
  и у сервера появляется вторая цифра (зелёные мс или красное «нет ответа»); рабочие серверы
  сортируются наверх. Работает и без подключения, проверяет и UDP-протоколы (WireGuard,
  Hysteria2). На время проверки запускается отдельный sing-box только на 127.0.0.1 со случайными
  логином и паролем, затем он останавливается, а его конфиг удаляется; текущее подключение не
  затрагивается. Если конфиг одного сервера сломан, остальные всё равно проверяются по одному.
- **Пояснения к пингу.** У обеих кнопок пинга есть подсказки: старая — прямое TCP-подключение к
  порту сервера (порт открыт, а не «VPN работает»), новая — настоящая проверка.

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
