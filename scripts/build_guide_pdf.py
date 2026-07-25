# -*- coding: utf-8 -*-
"""Builds the illustrated teaching PDF (docs/SimpleRay-Guide.pdf) with colorful SVG diagrams."""
import os, tempfile
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib import colors
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (SimpleDocTemplate, Paragraph, Spacer, PageBreak,
                                KeepTogether)
from svglib.svglib import svg2rlg

OUT = r"D:\programming\VPNclient-simpleRay\docs\SimpleRay-Guide.pdf"
TMP = tempfile.mkdtemp(prefix="srguide_")

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

# --- SVG helpers (kept simple so svglib renders cleanly) -------------------
def box(x, y, w, h, fill, stroke, rx=10):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" ry="{rx}" fill="{fill}" stroke="{stroke}" stroke-width="2"/>'

def txt(x, y, s, size=13, fill=INK, anchor="start", bold=False, ff="Arial"):
    w = ' font-weight="bold"' if bold else ''
    return f'<text x="{x}" y="{y}" font-family="{ff}" font-size="{size}" fill="{fill}" text-anchor="{anchor}"{w}>{s}</text>'

def arrow(x1, y1, x2, y2, color=GRY):
    # line + small triangular head at (x2,y2), horizontal or vertical
    head = ''
    if abs(y2 - y1) < 1:  # horizontal
        d = 8 if x2 > x1 else -8
        head = f'<polygon points="{x2},{y2} {x2-d},{y2-5} {x2-d},{y2+5}" fill="{color}"/>'
    else:  # vertical
        d = 8 if y2 > y1 else -8
        head = f'<polygon points="{x2},{y2} {x2-5},{y2-d} {x2+5},{y2-d}" fill="{color}"/>'
    return f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="2.5"/>' + head

def svg(w, h, body):
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">{body}</svg>'

_counter = [0]
def draw(svg_str, max_w=460):
    _counter[0] += 1
    p = os.path.join(TMP, f"d{_counter[0]}.svg")
    open(p, "w", encoding="utf-8").write(svg_str)
    d = svg2rlg(p)
    s = max_w / d.width
    d.scale(s, s)
    d.width *= s
    d.height *= s
    d.hAlign = "CENTER"
    return d

# ---------------------------------------------------------------------------
# Diagrams
# ---------------------------------------------------------------------------
def dia_layers():
    b = ''
    b += box(20, 20, 300, 150, BLUE_L, BLUE)
    b += txt(35, 45, "SimpleRay.Core  —  «мозги» (нейтральны к ОС)", 13, BLUE, bold=True)
    for i, t in enumerate(["ShareLinkParser — разбор ссылок",
                           "SingBoxConfigGenerator — конфиг (JSON)",
                           "Models — ProfileConfig, RoutingSettings",
                           "LatencyProbe, ReleaseParser …"]):
        b += box(35, 60 + i*26, 270, 20, "#FFFFFF", BLUE, 5)
        b += txt(45, 74 + i*26, t, 11, INK)
    b += box(360, 20, 300, 150, TEAL_L, TEAL)
    b += txt(375, 45, "SimpleRay.App  —  «лицо» (Windows / WPF)", 13, TEAL, bold=True)
    for i, t in enumerate(["MainWindow.xaml + MainViewModel",
                           "SingBoxEngine — запуск sing-box.exe",
                           "Stores — profiles/settings (шифр.)",
                           "Localization, UpdateService …"]):
        b += box(375, 60 + i*26, 270, 20, "#FFFFFF", TEAL, 5)
        b += txt(385, 74 + i*26, t, 11, INK)
    b += arrow(360, 95, 320, 95, GRY)
    b += txt(340, 88, "зависит", 10, GRY, anchor="middle")
    b += box(220, 195, 240, 40, GRY_L, GRY)
    b += txt(340, 220, "Тесты — проверяют Core и App", 12, GRY, anchor="middle", bold=True)
    b += arrow(300, 195, 300, 172, GRY)
    b += arrow(400, 195, 400, 172, GRY)
    return svg(680, 245, b)

def dia_tun():
    b = ''
    # left: proxy (danger)
    b += box(20, 20, 300, 210, RED_L, RED)
    b += txt(170, 45, "Прокси на localhost — плохо", 13, RED, anchor="middle", bold=True)
    b += box(60, 65, 220, 34, "#FFFFFF", RED, 6)
    b += txt(170, 87, "открытый порт 127.0.0.1 (без пароля)", 10, INK, anchor="middle")
    for i, (t, c) in enumerate([("Приложение", INK), ("Вирус (!)", RED), ("Любая программа", INK)]):
        b += box(60, 115 + i*32, 220, 24, "#FFFFFF", c, 5)
        b += txt(75, 132 + i*32, t, 11, c)
        b += arrow(285, 127 + i*32, 300, 100, RED)
    b += txt(170, 222, "любой может пользоваться туннелем", 9, RED, anchor="middle")
    # right: TUN (safe)
    b += box(360, 20, 300, 210, GRN_L, GRN)
    b += txt(510, 45, "TUN-режим — безопасно", 13, GRN, anchor="middle", bold=True)
    b += box(430, 65, 160, 34, "#FFFFFF", GRN, 6)
    b += txt(510, 87, "виртуальная карта (wintun)", 10, INK, anchor="middle")
    b += txt(510, 120, "весь трафик системы", 11, INK, anchor="middle")
    b += arrow(510, 128, 510, 150, GRN)
    b += box(430, 155, 160, 30, "#FFFFFF", GRN, 6)
    b += txt(510, 175, "sing-box → сервер", 11, INK, anchor="middle")
    b += txt(510, 210, "локального порта НЕТ → дырки нет", 9, GRN, anchor="middle", bold=True)
    return svg(680, 245, b)

def dia_mvvm():
    b = ''
    b += box(250, 20, 180, 46, BLUE_L, BLUE)
    b += txt(340, 40, "View (окно)", 13, BLUE, anchor="middle", bold=True)
    b += txt(340, 58, "MainWindow.xaml", 10, INK, anchor="middle")
    b += box(40, 150, 220, 46, TEAL_L, TEAL)
    b += txt(150, 170, "ViewModel (логика)", 13, TEAL, anchor="middle", bold=True)
    b += txt(150, 188, "MainViewModel.cs", 10, INK, anchor="middle")
    b += box(430, 150, 210, 46, GRY_L, GRY)
    b += txt(535, 170, "Model (данные)", 13, GRY, anchor="middle", bold=True)
    b += txt(535, 188, "ProfileConfig, Settings", 10, INK, anchor="middle")
    b += arrow(260, 80, 175, 148, GRY)
    b += arrow(175, 148, 300, 68, BLUE)
    b += txt(190, 120, "привязки (binding)", 10, GRY, anchor="middle")
    b += arrow(260, 173, 430, 173, GRY)
    b += txt(345, 165, "читает / меняет", 10, GRY, anchor="middle")
    b += txt(340, 230, "View показывает, ViewModel решает, Model хранит", 11, INK, anchor="middle")
    return svg(680, 250, b)

def dia_pipeline():
    steps = [("vless://…", AMB, AMB_L), ("ShareLinkParser", BLUE, BLUE_L),
             ("ProfileConfig", GRY, GRY_L), ("ConfigGenerator", BLUE, BLUE_L),
             ("JSON-конфиг", GRY, GRY_L), ("sing-box.exe", TEAL, TEAL_L),
             ("TUN → сеть", GRN, GRN_L)]
    b = ''
    x = 15
    positions = []
    for t, c, cl in steps:
        w = 92
        b += box(x, 70, w, 44, cl, c, 8)
        b += txt(x + w/2, 96, t, 10, c, anchor="middle", bold=True)
        positions.append((x, w))
        x += w + 8
    for i in range(len(steps) - 1):
        x0 = positions[i][0] + positions[i][1]
        x1 = positions[i+1][0]
        b += arrow(x0, 92, x1, 92, GRY)
    b += txt(350, 45, "Путь профиля: строка → объект → JSON → движок → туннель", 12, INK, anchor="middle", bold=True)
    b += txt(350, 140, "Core только ОПИСЫВАЕТ намерение (JSON). Сеть делает sing-box.", 10, GRY, anchor="middle")
    return svg(715, 160, b)

def dia_routing():
    b = ''
    b += box(20, 90, 120, 40, GRY_L, GRY)
    b += txt(80, 115, "Пакет", 13, GRY, anchor="middle", bold=True)
    b += arrow(140, 110, 175, 110, GRY)
    # decision chain
    checks = [("Правило\nпо приложению?", 190), ("Правило\nпо гео (geoip)?", 190)]
    # diamond 1
    def diamond(cx, cy, w, h, fill, stroke):
        return f'<polygon points="{cx},{cy-h} {cx+w},{cy} {cx},{cy+h} {cx-w},{cy}" fill="{fill}" stroke="{stroke}" stroke-width="2"/>'
    b += diamond(250, 110, 70, 45, AMB_L, AMB)
    b += txt(250, 106, "по приложению?", 10, AMB, anchor="middle", bold=True)
    b += diamond(430, 110, 70, 45, BLUE_L, BLUE)
    b += txt(430, 106, "по гео / реклама?", 10, BLUE, anchor="middle", bold=True)
    b += arrow(320, 110, 360, 110, GRY)
    b += arrow(500, 110, 545, 110, GRY)
    # outcomes
    b += box(545, 40, 120, 34, GRN_L, GRN); b += txt(605, 62, "proxy (VPN)", 11, GRN, anchor="middle", bold=True)
    b += box(545, 92, 120, 34, GRY_L, GRY); b += txt(605, 114, "direct (напрямую)", 10, GRY, anchor="middle", bold=True)
    b += box(545, 144, 120, 34, RED_L, RED); b += txt(605, 166, "reject (блок)", 11, RED, anchor="middle", bold=True)
    b += arrow(250, 155, 250, 200, AMB); b += txt(255, 180, "да → proxy/direct", 9, AMB)
    b += txt(350, 220, "sing-box берёт ПЕРВОЕ подходящее правило (порядок важен)", 11, INK, anchor="middle")
    return svg(690, 240, b)

def dia_update():
    steps = [("Проверить\nGitHub", BLUE), ("Согласие\nпользователя", AMB),
             ("Скачать +\nSHA256", BLUE), ("Резервная\nкопия", GRY),
             ("Применить", TEAL), ("Запуск\nновой", TEAL)]
    b = ''
    x = 15
    pos = []
    for t, c in steps:
        w = 96
        cl = {BLUE: BLUE_L, AMB: AMB_L, GRY: GRY_L, TEAL: TEAL_L}[c]
        b += box(x, 45, w, 46, cl, c, 8)
        lines = t.split("\n")
        for k, ln in enumerate(lines):
            b += txt(x + w/2, 66 + k*15 - (len(lines)-1)*7, ln, 10, c, anchor="middle", bold=True)
        pos.append((x, w)); x += w + 6
    for i in range(len(steps) - 1):
        b += arrow(pos[i][0]+pos[i][1], 68, pos[i+1][0], 68, GRY)
    # health check + rollback
    b += box(pos[5][0], 120, 96, 40, GRN_L, GRN)
    b += txt(pos[5][0]+48, 145, "работает?", 11, GRN, anchor="middle", bold=True)
    b += arrow(pos[5][0]+48, 91, pos[5][0]+48, 118, GRY)
    b += box(200, 175, 300, 34, RED_L, RED)
    b += txt(350, 197, "упала → ОТКАТ на прежнюю версию", 12, RED, anchor="middle", bold=True)
    b += arrow(pos[5][0]+30, 160, 470, 175, RED)
    b += txt(350, 30, "Обновление: скачать → проверить → применить, с откатом", 12, INK, anchor="middle", bold=True)
    return svg(670, 220, b)

# ---------------------------------------------------------------------------
# Document
# ---------------------------------------------------------------------------
styles = getSampleStyleSheet()
def st(name, **kw):
    base = dict(fontName="Arial", textColor=colors.HexColor(INK), leading=16)
    base.update(kw)
    return ParagraphStyle(name, **base)

H1 = st("H1", fontName="Arial-Bold", fontSize=17, textColor=colors.HexColor(BLUE), spaceBefore=6, spaceAfter=8, leading=21)
BODY = st("BODY", fontSize=11, leading=16, spaceAfter=6, alignment=TA_LEFT)
CAP = st("CAP", fontSize=9.5, leading=13, textColor=colors.HexColor(GRY), alignment=TA_CENTER, spaceBefore=3, spaceAfter=10)
TITLE = st("TITLE", fontName="Arial-Bold", fontSize=30, textColor=colors.HexColor(BLUE), alignment=TA_CENTER, leading=36)
SUB = st("SUB", fontSize=13, alignment=TA_CENTER, textColor=colors.HexColor(GRY), leading=18)

story = []

# Cover
story += [Spacer(1, 120),
          Paragraph("SimpleRay изнутри", TITLE),
          Spacer(1, 8),
          Paragraph("Как устроена программа — иллюстрированный разбор для новичка", SUB),
          Spacer(1, 30)]
story.append(draw(dia_layers(), 430))
story += [Spacer(1, 20),
          Paragraph("VPN-клиент на C# / .NET 8 (WPF) поверх движка sing-box", CAP),
          PageBreak()]

def section(title, dia, paras, dia_w=460):
    flow = [Paragraph(title, H1), draw(dia, dia_w)]
    flow.append(Spacer(1, 8))
    for p in paras:
        flow.append(Paragraph(p, BODY))
    flow.append(Spacer(1, 14))
    story.append(KeepTogether(flow) if False else flow[0])
    for f in flow[1:]:
        story.append(f)

section("1. Из чего собрана программа", dia_layers(), [
    "Проект делится на два: <b>Core</b> — чистая логика (разбор ссылок, генерация "
    "конфигурации, модели данных), которая ничего не знает про Windows и окна; и "
    "<b>App</b> — интерфейс на WPF, запуск процессов, работа с файлами.",
    "Такое разделение (separation of concerns) позволяет проверять «мозги» тестами "
    "без окна и переиспользовать их — например, завтра под Android. App зависит от "
    "Core, но не наоборот.",
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
    "Их связывают <b>привязки</b>: надпись на кнопке берётся из свойства ViewModel и "
    "обновляется сама, когда свойство меняется. Кнопки — это <b>команды</b> "
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
], dia_w=475)

section("5. Маршрутизация: куда пойдёт пакет", dia_routing(), [
    "VPN не обязан гнать через сервер весь трафик. Правила решают судьбу каждого "
    "пакета: сначала правила <b>по приложению</b> (по имени процесса), затем "
    "<b>по гео</b> (geoip/geosite — какой адрес к какой стране относится) и блокировка "
    "рекламы, иначе — итоговое действие режима (proxy / direct).",
    "sing-box берёт <b>первое подходящее</b> правило, поэтому порядок важен — правила "
    "по приложениям стоят раньше гео-правил (это проверяет тест <i>AntiLeakTests</i>). "
    "Ещё есть <b>группа каналов</b>: при сбое sing-box сам переключается на живой "
    "сервер (failover).",
], dia_w=475)

section("6. Обновления с откатом", dia_update(), [
    "Программа обновляет себя <b>безопасно</b>: спрашивает у GitHub последнюю версию, "
    "качает архив только с согласия пользователя и сверяет его <b>контрольную сумму "
    "SHA256</b> (защита от порчи при передаче).",
    "Переписать себя «на ходу» нельзя, поэтому копия-обновлятор ждёт закрытия "
    "программы, делает <b>резервную копию</b>, подменяет файлы и запускает новую "
    "версию. Если та сразу падает — обновлятор <b>восстанавливает старую</b>. "
    "«Не завёлся мотор — вернули прежний».",
], dia_w=470)

# Closing note
story.append(Paragraph("Что почитать в коде дальше", H1))
for t in [
    "<b>ProfileConfig.cs</b> — простой объект-«коробка» с полями профиля.",
    "<b>ShareLinkParser.cs</b> — как строка превращается в объект.",
    "<b>SingBoxConfigGenerator.cs</b> — как объект превращается в JSON.",
    "<b>MainViewModel.cs</b> — «клей» логики окна.",
    "<b>tests/</b> — каждый тест это пример «дано → ожидаем»; лучший способ понять код.",
]:
    story.append(Paragraph("•&nbsp; " + t, BODY))
story.append(Spacer(1, 10))
story.append(Paragraph("Полный текстовый разбор — в файле <b>docs/GUIDE.ru.md</b>. "
                       "Сборка: <font face='Arial-Bold'>dotnet build</font>, тесты: "
                       "<font face='Arial-Bold'>dotnet test</font>, запуск окна: "
                       "<font face='Arial-Bold'>dotnet run --project src/SimpleRay.App</font>.", BODY))

doc = SimpleDocTemplate(OUT, pagesize=A4,
                        leftMargin=20*mm, rightMargin=20*mm,
                        topMargin=18*mm, bottomMargin=16*mm,
                        title="SimpleRay — обучающий разбор", author="SimpleRay")

def footer(canvas, d):
    canvas.saveState()
    canvas.setFont("Arial", 8)
    canvas.setFillColor(colors.HexColor(GRY))
    canvas.drawCentredString(A4[0]/2, 10*mm, f"SimpleRay — обучающий разбор  ·  стр. {d.page}")
    canvas.restoreState()

doc.build(story, onFirstPage=footer, onLaterPages=footer)
print("OK ->", OUT, os.path.getsize(OUT), "bytes")
