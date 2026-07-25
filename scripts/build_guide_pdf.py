# -*- coding: utf-8 -*-
"""Builds the illustrated teaching PDF (docs/SimpleRay-Guide.pdf) with colorful SVG diagrams.

Requires: pip install reportlab svglib pillow   (Windows Arial is used for Cyrillic).
Each major section starts on its own page; diagram labels are kept clear of arrows.
"""
import os, tempfile
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib import colors
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle)
from svglib.svglib import svg2rlg

OUT = os.environ.get("SR_GUIDE_OUT", r"D:\programming\VPNclient-simpleRay\docs\SimpleRay-Guide.pdf")
TMP = tempfile.mkdtemp(prefix="srguide_")
CONTENT_W = A4[0] - 40*mm  # usable width between margins

# --- Cyrillic-capable fonts -------------------------------------------------
pdfmetrics.registerFont(TTFont("Arial", r"C:\Windows\Fonts\arial.ttf"))
pdfmetrics.registerFont(TTFont("Arial-Bold", r"C:\Windows\Fonts\arialbd.ttf"))
pdfmetrics.registerFontFamily("Arial", normal="Arial", bold="Arial-Bold",
                              italic="Arial", boldItalic="Arial-Bold")

# --- Palette ---------------------------------------------------------------
BLUE, BLUE_L = "#1565C0", "#E3F2FD"
TEAL, TEAL_L = "#00796B", "#E0F2F1"
RED,  RED_L  = "#C62828", "#FFEBEE"
GRN,  GRN_L  = "#2E7D32", "#E8F5E9"
GRY,  GRY_L  = "#455A64", "#ECEFF1"
AMB,  AMB_L  = "#EF6C00", "#FFF3E0"
INK = "#212121"

# --- SVG helpers -----------------------------------------------------------
def box(x, y, w, h, fill, stroke, rx=10):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" ry="{rx}" fill="{fill}" stroke="{stroke}" stroke-width="2"/>'

def txt(x, y, s, size=16, fill=INK, anchor="start", bold=False, ff="Arial"):
    w = ' font-weight="bold"' if bold else ''
    return f'<text x="{x}" y="{y}" font-family="{ff}" font-size="{size}" fill="{fill}" text-anchor="{anchor}"{w}>{s}</text>'

def diamond(cx, cy, w, h, fill, stroke):
    return f'<polygon points="{cx},{cy-h} {cx+w},{cy} {cx},{cy+h} {cx-w},{cy}" fill="{fill}" stroke="{stroke}" stroke-width="2"/>'

def arrow(x1, y1, x2, y2, color=GRY):
    head = ''
    if abs(y2 - y1) < 1:
        d = 10 if x2 > x1 else -10
        head = f'<polygon points="{x2},{y2} {x2-d},{y2-6} {x2-d},{y2+6}" fill="{color}"/>'
    elif abs(x2 - x1) < 1:
        d = 10 if y2 > y1 else -10
        head = f'<polygon points="{x2},{y2} {x2-6},{y2-d} {x2+6},{y2-d}" fill="{color}"/>'
    else:
        head = f'<circle cx="{x2}" cy="{y2}" r="4" fill="{color}"/>'
    return f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="2.5"/>' + head

def svg(w, h, body):
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">{body}</svg>'

_counter = [0]
def draw(svg_str, max_w=None):
    if max_w is None:
        max_w = CONTENT_W
    _counter[0] += 1
    p = os.path.join(TMP, f"d{_counter[0]}.svg")
    open(p, "w", encoding="utf-8").write(svg_str)
    d = svg2rlg(p)
    s = max_w / d.width
    d.scale(s, s); d.width *= s; d.height *= s
    d.hAlign = "CENTER"
    return d

# ---------------------------------------------------------------------------
# Diagrams (bigger labels; text kept off arrows)
# ---------------------------------------------------------------------------
def dia_layers():
    b = ''
    b += box(20, 20, 300, 172, BLUE_L, BLUE)
    b += txt(35, 48, "SimpleRay.Core — «мозги»", 17, BLUE, bold=True)
    b += txt(35, 68, "(нейтральны к операционной системе)", 12.5, GRY)
    for i, t in enumerate(["ShareLinkParser — разбор ссылок",
                           "SingBoxConfigGenerator — конфиг (JSON)",
                           "Models — ProfileConfig, RoutingSettings",
                           "LatencyProbe, ReleaseParser …"]):
        b += box(35, 80 + i*26, 270, 22, "#FFFFFF", BLUE, 5)
        b += txt(45, 96 + i*26, t, 12.5, INK)
    b += box(380, 20, 300, 172, TEAL_L, TEAL)
    b += txt(395, 48, "SimpleRay.App — «лицо»", 17, TEAL, bold=True)
    b += txt(395, 68, "(только Windows, интерфейс WPF)", 12.5, GRY)
    for i, t in enumerate(["MainWindow.xaml + MainViewModel",
                           "SingBoxEngine — запуск sing-box.exe",
                           "Stores — профили/настройки (шифр.)",
                           "Localization, UpdateService …"]):
        b += box(395, 80 + i*26, 270, 22, "#FFFFFF", TEAL, 5)
        b += txt(405, 96 + i*26, t, 12.5, INK)
    b += arrow(380, 106, 322, 106, GRY)
    b += txt(351, 98, "зависит", 12, GRY, anchor="middle")
    b += box(230, 220, 240, 40, GRY_L, GRY)
    b += txt(350, 245, "Тесты — проверяют Core и App", 14, GRY, anchor="middle", bold=True)
    b += arrow(300, 220, 300, 194, GRY)
    b += arrow(430, 220, 430, 194, GRY)
    return svg(700, 272, b)

def dia_tun():
    b = ''
    b += box(20, 20, 310, 230, RED_L, RED)
    b += txt(175, 50, "Прокси на localhost — плохо", 16, RED, anchor="middle", bold=True)
    b += box(55, 66, 240, 36, "#FFFFFF", RED, 6)
    b += txt(175, 89, "открытый порт 127.0.0.1 (без пароля)", 12, INK, anchor="middle")
    for i, (t, c) in enumerate([("Приложение", INK), ("Вирус (!)", RED), ("Любая программа", INK)]):
        b += box(55, 120 + i*34, 175, 26, "#FFFFFF", c, 5)
        b += txt(68, 138 + i*34, t, 12.5, c)
        b += arrow(232, 133 + i*34, 292, 104, RED)
    b += txt(175, 242, "любой может пользоваться туннелем", 11, RED, anchor="middle")
    b += box(370, 20, 310, 230, GRN_L, GRN)
    b += txt(525, 50, "TUN-режим — безопасно", 16, GRN, anchor="middle", bold=True)
    b += box(430, 72, 190, 36, "#FFFFFF", GRN, 6)
    b += txt(525, 95, "виртуальная карта (wintun)", 12, INK, anchor="middle")
    b += txt(525, 132, "весь трафик системы", 13, INK, anchor="middle", bold=True)
    b += arrow(525, 140, 525, 165, GRN)
    b += box(430, 170, 190, 34, "#FFFFFF", GRN, 6)
    b += txt(525, 192, "sing-box → сервер", 13, INK, anchor="middle")
    b += txt(525, 232, "локального порта НЕТ → дырки нет", 11.5, GRN, anchor="middle", bold=True)
    return svg(700, 262, b)

def dia_mvvm():
    b = ''
    # View top, ViewModel directly below (vertical arrow between them), Model to the right
    b += box(250, 20, 180, 54, BLUE_L, BLUE)
    b += txt(340, 44, "View (окно)", 16, BLUE, anchor="middle", bold=True)
    b += txt(340, 63, "MainWindow.xaml", 12, INK, anchor="middle")
    b += box(250, 160, 180, 54, TEAL_L, TEAL)
    b += txt(340, 184, "ViewModel (логика)", 16, TEAL, anchor="middle", bold=True)
    b += txt(340, 203, "MainViewModel.cs", 12, INK, anchor="middle")
    b += box(470, 160, 200, 54, GRY_L, GRY)
    b += txt(570, 184, "Model (данные)", 16, GRY, anchor="middle", bold=True)
    b += txt(570, 203, "ProfileConfig, Settings", 12, INK, anchor="middle")
    # vertical double-headed arrow between View and ViewModel; label placed to the RIGHT
    b += arrow(340, 156, 340, 78, BLUE)
    b += arrow(360, 78, 360, 156, GRY)
    b += txt(378, 122, "привязки (binding)", 12.5, GRY)
    # horizontal arrow ViewModel -> Model (label omitted to avoid crowding the short gap)
    b += arrow(432, 187, 468, 187, GRY)
    b += txt(360, 252, "View показывает · ViewModel решает · Model хранит", 13, INK, anchor="middle")
    return svg(700, 265, b)

def dia_pipeline():
    steps = [("vless://…", AMB, AMB_L), ("ShareLinkParser", BLUE, BLUE_L),
             ("ProfileConfig", GRY, GRY_L), ("ConfigGenerator", BLUE, BLUE_L),
             ("JSON-конфиг", GRY, GRY_L), ("sing-box.exe", TEAL, TEAL_L),
             ("TUN → сеть", GRN, GRN_L)]
    b = ''
    x, w = 12, 96
    pos = []
    for t, c, cl in steps:
        b += box(x, 78, w, 50, cl, c, 8)
        b += txt(x + w/2, 108, t, 12, c, anchor="middle", bold=True)
        pos.append((x, w)); x += w + 8
    for i in range(len(steps) - 1):
        b += arrow(pos[i][0]+pos[i][1], 103, pos[i+1][0], 103, GRY)
    b += txt((x)/2, 46, "Путь профиля: строка → объект → JSON → движок → туннель", 14, INK, anchor="middle", bold=True)
    b += txt((x)/2, 156, "Core только ОПИСЫВАЕТ намерение (JSON). Сетевую работу делает sing-box.", 12, GRY, anchor="middle")
    return svg(x, 176, b)

def dia_routing():
    b = ''
    b += box(20, 96, 120, 44, GRY_L, GRY)
    b += txt(80, 123, "Пакет", 16, GRY, anchor="middle", bold=True)
    b += arrow(140, 118, 178, 118, GRY)
    b += diamond(255, 118, 76, 50, AMB_L, AMB)
    b += txt(255, 114, "по приложению?", 12, AMB, anchor="middle", bold=True)
    b += diamond(445, 118, 76, 50, BLUE_L, BLUE)
    b += txt(445, 114, "по гео / реклама?", 12, BLUE, anchor="middle", bold=True)
    b += arrow(331, 118, 367, 118, GRY)
    b += arrow(521, 118, 560, 118, GRY)
    b += box(560, 44, 130, 36, GRN_L, GRN);  b += txt(625, 67, "proxy (VPN)", 12.5, GRN, anchor="middle", bold=True)
    b += box(560, 100, 130, 36, GRY_L, GRY); b += txt(625, 123, "direct (напрямую)", 12, GRY, anchor="middle", bold=True)
    b += box(560, 156, 130, 36, RED_L, RED); b += txt(625, 179, "reject (блок)", 12.5, RED, anchor="middle", bold=True)
    b += arrow(255, 168, 255, 214, AMB)
    b += txt(268, 196, "да → proxy / direct", 11.5, AMB)
    b += txt(360, 250, "sing-box берёт ПЕРВОЕ подходящее правило (порядок важен)", 12.5, INK, anchor="middle")
    return svg(710, 268, b)

def dia_update():
    steps = [("Проверить\nGitHub", BLUE), ("Согласие\nпользователя", AMB),
             ("Скачать +\nSHA256", BLUE), ("Резервная\nкопия", GRY),
             ("Применить", TEAL), ("Запуск\nновой версии", TEAL)]
    b = ''
    x, w = 12, 108
    pos = []
    for t, c in steps:
        cl = {BLUE: BLUE_L, AMB: AMB_L, GRY: GRY_L, TEAL: TEAL_L}[c]
        b += box(x, 52, w, 54, cl, c, 8)
        lines = t.split("\n")
        for k, ln in enumerate(lines):
            b += txt(x + w/2, 78 + k*16 - (len(lines)-1)*8, ln, 12, c, anchor="middle", bold=True)
        pos.append((x, w)); x += w + 8
    for i in range(len(steps) - 1):
        b += arrow(pos[i][0]+pos[i][1], 79, pos[i+1][0], 79, GRY)
    b += box(pos[5][0], 140, w, 42, GRN_L, GRN)
    b += txt(pos[5][0]+w/2, 166, "работает?", 13, GRN, anchor="middle", bold=True)
    b += arrow(pos[5][0]+w/2, 106, pos[5][0]+w/2, 138, GRY)
    b += box(220, 200, 320, 40, RED_L, RED)
    b += txt(380, 225, "упала → ОТКАТ на прежнюю версию", 13.5, RED, anchor="middle", bold=True)
    b += arrow(pos[5][0]+30, 182, 500, 200, RED)
    b += txt(x/2, 32, "Обновление: скачать → проверить → применить, с откатом", 14, INK, anchor="middle", bold=True)
    return svg(x, 252, b)

def dia_engine():
    b = ''
    states = [("Stopped\n(выкл.)", GRY), ("Starting\n(запуск)", AMB),
              ("Running\n(работает)", GRN), ("Stopping\n(остановка)", AMB)]
    x, w = 30, 140
    cx = []
    for t, c in states:
        cl = {GRY: GRY_L, AMB: AMB_L, GRN: GRN_L}[c]
        b += box(x, 60, w, 56, cl, c, 10)
        lines = t.split("\n")
        for k, ln in enumerate(lines):
            b += txt(x + w/2, 84 + k*16 - (len(lines)-1)*8, ln, 13, c, anchor="middle", bold=True)
        cx.append(x + w/2); x += w + 30
    for i in range(len(states) - 1):
        b += arrow(cx[i] + w/2, 88, cx[i+1] - w/2, 88, GRY)
    # loop back Stopping -> Stopped (curve dips below the label so they don't touch)
    b += f'<path d="M {cx[3]} 116 C {cx[3]} 190, {cx[0]} 190, {cx[0]} 118" fill="none" stroke="{GRY}" stroke-width="2.5"/>'
    b += f'<polygon points="{cx[0]},118 {cx[0]-6},130 {cx[0]+6},130" fill="{GRY}"/>'
    b += txt((cx[0]+cx[3])/2, 158, "аккуратная остановка (Ctrl+C, иначе — kill)", 12, GRY, anchor="middle")
    # Faulted branch
    b += box(cx[2] - 70, 190, 200, 40, RED_L, RED)
    b += txt(cx[2] + 30, 215, "Faulted — сбой движка", 13, RED, anchor="middle", bold=True)
    b += arrow(cx[2], 116, cx[2] + 10, 188, RED)
    b += txt(x/2, 34, "Жизненный цикл движка (SingBoxEngine)", 15, INK, anchor="middle", bold=True)
    return svg(x, 245, b)

# ---------------------------------------------------------------------------
# Styles
# ---------------------------------------------------------------------------
def st(name, **kw):
    base = dict(fontName="Arial", textColor=colors.HexColor(INK), leading=16)
    base.update(kw)
    return ParagraphStyle(name, **base)

H1    = st("H1", fontName="Arial-Bold", fontSize=18, textColor=colors.HexColor(BLUE), spaceBefore=2, spaceAfter=12, leading=23)
BODY  = st("BODY", fontSize=11.5, leading=17, spaceAfter=7, alignment=TA_LEFT)
CAP   = st("CAP", fontSize=10, leading=14, textColor=colors.HexColor(GRY), alignment=TA_CENTER, spaceBefore=4, spaceAfter=12)
TITLE = st("TITLE", fontName="Arial-Bold", fontSize=32, textColor=colors.HexColor(BLUE), alignment=TA_CENTER, leading=38)
SUB   = st("SUB", fontSize=13.5, alignment=TA_CENTER, textColor=colors.HexColor(GRY), leading=19)

story = []

# --- Cover -----------------------------------------------------------------
story += [Spacer(1, 110),
          Paragraph("SimpleRay изнутри", TITLE),
          Spacer(1, 10),
          Paragraph("Как устроена программа — иллюстрированный разбор для новичка", SUB),
          Spacer(1, 34),
          draw(dia_layers(), CONTENT_W),
          Spacer(1, 18),
          Paragraph("VPN-клиент на C# / .NET 8 (WPF) поверх движка sing-box", CAP)]

def section(title, dia, paras, dia_w=None):
    story.append(PageBreak())
    story.append(Paragraph(title, H1))
    story.append(draw(dia, dia_w))
    story.append(Spacer(1, 12))
    for p in paras:
        story.append(Paragraph(p, BODY))

section("1. Из чего собрана программа", dia_layers(), [
    "Проект делится на два подпроекта. <b>Core</b> — чистая логика (разбор ссылок, "
    "генерация конфигурации, модели данных), которая ничего не знает про Windows и "
    "окна. <b>App</b> — интерфейс на WPF, запуск процессов, работа с файлами.",
    "Такое разделение (separation of concerns — «разделение ответственности») "
    "позволяет проверять «мозги» тестами без окна и переиспользовать их — например, "
    "завтра под Android. App зависит от Core, но не наоборот.",
])

section("2. Главная идея безопасности: TUN, а не прокси", dia_tun(), [
    "Многие клиенты открывают локальный прокси-порт на 127.0.0.1 — часто <b>без "
    "пароля</b>, и любая программа (даже вредоносная) может им пользоваться как "
    "бесплатным туннелем.",
    "SimpleRay вместо этого поднимает <b>виртуальную сетевую карту (TUN)</b> через "
    "wintun: весь трафик системы идёт через неё, а локального порта нет вовсе. "
    "Уязвимость закрыта не заплаткой, а конструкцией. Тест <i>AntiLeakTests</i> "
    "следит, чтобы входящий прокси никогда не появился в конфиге.",
])

section("3. Интерфейс: WPF и паттерн MVVM", dia_mvvm(), [
    "Окно разложено на три слоя. <b>View</b> (MainWindow.xaml) только показывает. "
    "<b>ViewModel</b> (MainViewModel.cs) содержит логику. <b>Model</b> — данные из Core.",
    "Их связывают <b>привязки</b> (binding): надпись на кнопке берётся из свойства "
    "ViewModel и обновляется сама, когда свойство меняется. Кнопки — это <b>команды</b> "
    "(RelayCommand) с методами «что делать» и «можно ли нажимать».",
])

section("4. Путь профиля: от ссылки до туннеля", dia_pipeline(), [
    "Главный сценарий-«конвейер». Ссылка вида <font face='Arial-Bold'>vless://…</font> "
    "разбирается в объект <b>ProfileConfig</b>. При нажатии «Подключить» генератор "
    "превращает его (плюс настройки маршрутизации) в большой <b>JSON</b> для sing-box, "
    "и движок запускает <b>sing-box.exe</b> с этим конфигом.",
    "Ключевая мысль: Core только <b>описывает намерение</b> в JSON; реальную сетевую "
    "работу делает sing-box. Правильность JSON проверяется командой "
    "<i>sing-box check</i> ещё до запуска.",
])

section("5. Маршрутизация: куда пойдёт пакет", dia_routing(), [
    "VPN не обязан гнать через сервер весь трафик. Правила решают судьбу каждого "
    "пакета: сначала правила <b>по приложению</b> (по имени процесса), затем "
    "<b>по гео</b> (geoip/geosite — какой адрес к какой стране относится) и блокировка "
    "рекламы, иначе — итоговое действие режима (proxy / direct).",
    "sing-box берёт <b>первое подходящее</b> правило, поэтому порядок важен — правила "
    "по приложениям стоят раньше гео-правил (это проверяет тест <i>AntiLeakTests</i>). "
    "Ещё есть <b>группа каналов</b>: при сбое sing-box сам переключается на живой "
    "сервер (failover — «отказоустойчивое переключение»).",
])

section("6. Движок: управление процессом sing-box", dia_engine(), [
    "SimpleRay не делает сеть сам — он <b>дирижирует</b> дочерним процессом sing-box. "
    "За движком стоит контракт <b>IVpnEngine</b> (Start/Stop, состояние, лог); под "
    "Windows его реализует <b>SingBoxEngine</b>, а завтра под Android будет другая "
    "реализация того же контракта.",
    "Состояния сменяются: выключен → запуск → работает → остановка. Отдельно — сбой "
    "(Faulted). <b>Аккуратная остановка</b> важна: просто «убить» процесс нельзя — "
    "sing-box не успеет убрать сетевые маршруты, и можно остаться без интернета. "
    "Поэтому сначала посылается вежливый сигнал (Ctrl+C), и только при неудаче — "
    "принудительное завершение.",
])

section("7. Обновления с откатом", dia_update(), [
    "Программа обновляет себя <b>безопасно</b>: спрашивает у GitHub последнюю версию, "
    "качает архив только с согласия пользователя и сверяет его <b>контрольную сумму "
    "SHA256</b> (защита от порчи при передаче).",
    "Переписать себя «на ходу» нельзя, поэтому копия-обновлятор ждёт закрытия "
    "программы, делает <b>резервную копию</b>, подменяет файлы и запускает новую "
    "версию. Если та сразу падает — обновлятор <b>восстанавливает старую</b>. "
    "«Не завёлся мотор — вернули прежний».",
])

# --- Abbreviations ----------------------------------------------------------
story.append(PageBreak())
story.append(Paragraph("Расшифровка сокращений", H1))
story.append(Paragraph("Технические сокращения, встречающиеся в проекте и в этом разборе.", BODY))
story.append(Spacer(1, 6))

ABBR = [
    ("VPN", "Virtual Private Network", "виртуальная частная сеть — защищённый канал до сервера"),
    ("TUN", "network TUNnel (адаптер)", "виртуальная сетевая карта; через неё идёт весь трафик"),
    ("WPF", "Windows Presentation Foundation", "технология окон для Windows"),
    ("XAML", "eXtensible App Markup Language", "язык разметки окон (похож на HTML)"),
    ("MVVM", "Model – View – ViewModel", "раскладка интерфейса на три слоя"),
    ("UI", "User Interface", "пользовательский интерфейс"),
    ("JSON", "JavaScript Object Notation", "текстовый формат данных (конфиг sing-box)"),
    ("DPAPI", "Data Protection API", "встроенное шифрование Windows под учётную запись"),
    ("TLS", "Transport Layer Security", "шифрование соединения («замок» в браузере)"),
    ("DNS", "Domain Name System", "«телефонная книга» интернета: имя → адрес"),
    ("SHA256", "Secure Hash Algorithm 256", "«отпечаток» файла для проверки целостности"),
    ("CI/CD", "Continuous Integration / Delivery", "авто-сборка, тесты и выпуск на сервере"),
    ("SDK", "Software Development Kit", "набор инструментов разработчика"),
    ("API", "Application Programming Interface", "как одна программа обращается к другой"),
    ("DLL", "Dynamic-Link Library", "библиотека кода, подключаемая на лету (wintun.dll)"),
    ("geoip / geosite", "geo-IP / geo-site", "базы соответствия «адрес/сайт → страна»"),
    ("QR", "Quick Response code", "квадратный штрих-код"),
    ("RTL", "Right-To-Left", "письмо справа налево (арабский, фарси)"),
]
cell = st("cell", fontSize=10.5, leading=13)
cellb = st("cellb", fontName="Arial-Bold", fontSize=10.5, leading=13, textColor=colors.HexColor(BLUE))
head = st("head", fontName="Arial-Bold", fontSize=10.5, leading=13, textColor=colors.white)
rows = [[Paragraph("Сокр.", head), Paragraph("Расшифровка", head), Paragraph("Что значит простыми словами", head)]]
for a, full, mean in ABBR:
    rows.append([Paragraph(a, cellb), Paragraph(full, cell), Paragraph(mean, cell)])
tbl = Table(rows, colWidths=[CONTENT_W*0.16, CONTENT_W*0.34, CONTENT_W*0.50])
tbl.setStyle(TableStyle([
    ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor(BLUE)),
    ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F5F7FA")]),
    ("GRID", (0, 0), (-1, -1), 0.5, colors.HexColor("#CFD8DC")),
    ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
    ("LEFTPADDING", (0, 0), (-1, -1), 6), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
    ("TOPPADDING", (0, 0), (-1, -1), 4), ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
]))
story.append(tbl)

# --- Where to start in the code --------------------------------------------
story.append(PageBreak())
story.append(Paragraph("Что почитать в коде дальше", H1))
for t in [
    "<b>ProfileConfig.cs</b> — простой объект-«коробка» с полями профиля.",
    "<b>ShareLinkParser.cs</b> — как строка превращается в объект.",
    "<b>SingBoxConfigGenerator.cs</b> — как объект превращается в JSON.",
    "<b>MainViewModel.cs</b> — «клей» логики окна.",
    "<b>tests/</b> — каждый тест это пример «дано → ожидаем»; лучший способ понять код.",
]:
    story.append(Paragraph("•&nbsp; " + t, BODY))
story.append(Spacer(1, 12))
story.append(Paragraph("Полный текстовый разбор — в файле <b>docs/GUIDE.ru.md</b>. Сборка: "
                       "<font face='Arial-Bold'>dotnet build</font>, тесты: "
                       "<font face='Arial-Bold'>dotnet test</font>, запуск окна: "
                       "<font face='Arial-Bold'>dotnet run --project src/SimpleRay.App</font>. "
                       "Для настоящего подключения нужны права администратора (TUN) и рабочий сервер.", BODY))

# ---------------------------------------------------------------------------
doc = SimpleDocTemplate(OUT, pagesize=A4,
                        leftMargin=20*mm, rightMargin=20*mm,
                        topMargin=18*mm, bottomMargin=18*mm,
                        title="SimpleRay — обучающий разбор", author="SimpleRay")

def footer(canvas, d):
    canvas.saveState()
    canvas.setFont("Arial", 8)
    canvas.setFillColor(colors.HexColor(GRY))
    canvas.drawCentredString(A4[0]/2, 10*mm, f"SimpleRay — обучающий разбор  ·  стр. {d.page}")
    canvas.restoreState()

doc.build(story, onFirstPage=footer, onLaterPages=footer)
print("OK ->", OUT, os.path.getsize(OUT), "bytes")
