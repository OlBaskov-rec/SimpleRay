# Проверка kill-switch (WFP)

Цель — довести нативный WFP kill-switch до рабочего состояния. Технический дизайн —
в [docs/KILL-SWITCH.md](../../../docs/KILL-SWITCH.md).

> ⚠️ **Fail-closed может заблокировать весь интернет** на этой машине — это его работа.
> Держите под рукой второе устройство (телефон). Восстановление сети: закрыть harness /
> нажать Enter в нём / **перезагрузка** (фильтры не boot-persistent).

Сейчас в приложении kill-switch **отключён** (инертен), потому что нативный `Engage`
крашил GUI. Поэтому отлаживаем его **изолированно** через harness, а в приложение вернём,
когда заработает.

---

## Этап A. Отладка WFP через harness (без GUI)

Harness гоняет только `WfpKillSwitch` — если он упадёт, пострадает лишь консоль, а не
приложение. Так мы точно увидим, на каком вызове проблема.

1. Откройте **PowerShell от администратора**.
2. Запустите harness (укажите путь к своему `sing-box.exe`):
   ```powershell
   cd D:\Development\SimpleRay
   dotnet run --project tests\manual\kill-switch\wfp-harness -- simpleray "D:\Progs\SimpleRay\SimpleRay-0.2.0-win-x64\core\sing-box.exe"
   ```
3. Смотрите вывод по шагам:
   - `[1/3] CleanupLeftovers` — должно быть `ok`.
   - `[2/3] Engage` — **тут была проблема**. Возможные исходы:
     - `ok, IsEngaged=True` — фильтры встали;
     - `Engage FAILED: …` — печатается исключение (**пришлите его целиком**);
     - консоль молча закрылась/упала — это `AccessViolation` в marshalling (**напишите, что именно так, и на каком шаге**).
4. Если Engage прошёл — в **другом** окне проверьте блокировку:
   ```powershell
   curl.exe -m 5 https://example.com     # ожидаем: НЕ проходит (заблокировано)
   .\tests\manual\kill-switch\check-filters.ps1   # покажет фильтры 'SimpleRay kill-switch'
   ```
5. Нажмите **Enter** в harness → `Disengage` → сеть вернётся. Проверьте `curl` снова — должно работать.

**Что прислать мне:** полный вывод harness (особенно шаг Engage), и — если сеть блокировалась
неправильно (например, пропала совсем даже для нужного) — вывод `check-filters.ps1` и
`Get-NetAdapter` (имя TUN-адаптера). По этому я поправлю `WfpNative.cs`.

Наиболее вероятная первая причина — раскладка структуры `FWPM_FILTER0`/`FWP_VALUE0` (union),
либо имя TUN-интерфейса ≠ `simpleray`, либо app-id sing-box.

---

## Этап B. Проверка в приложении (после того как harness заработает)

Когда Engage/Disengage в harness стабильны, я верну `WfpKillSwitch` в приложение (сейчас там
`NoOpKillSwitch`). Тогда прогоняем полный чеклист:

1. Свежая сборка из `build\`, запуск, «Подключить» (UAC).
2. Галка **kill-switch** включена, подключиться → обычный сёрфинг работает (разрешены TUN +
   sing-box + loopback). Если интернет пропал при живом туннеле — неверный permit-фильтр.
3. Убить sing-box (`Stop-Process -Name sing-box -Force`) → трафик **заблокирован** (нет утечки),
   пока watchdog переподключается. `curl` в момент разрыва не проходит.
4. IPv6-утечка: `curl.exe -6 -m 5 https://ipv6.google.com` при разрыве → нет доступа.
5. Крах приложения (`Stop-Process -Name SimpleRay -Force`) → трафик заблокирован (fail-closed);
   перезапуск приложения от админа (`CleanupLeftovers`) **или** перезагрузка → сеть вернулась.
6. Чистое «Отключить» → фильтров `SimpleRay kill-switch` не осталось (`check-filters.ps1`).

Включать kill-switch в релиз можно только когда пункты 2–6 — OK.

---

## Таблица результатов

| Шаг | Что проверяем | OK / провал | Заметки |
|-----|---------------|-------------|---------|
| A.3 | Engage в harness |  |  |
| A.4 | Блокировка при engaged |  |  |
| A.5 | Disengage вернул сеть |  |  |
| B.2 | Сёрфинг с kill-switch |  |  |
| B.3 | Блок при крахе движка |  |  |
| B.4 | Нет IPv6-утечки |  |  |
| B.5 | Fail-closed + recovery |  |  |
| B.6 | Чистое отключение без следов |  |  |
