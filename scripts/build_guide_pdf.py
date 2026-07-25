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

def dia_i18n():
    b = ''
    b += txt(355, 30, "Смена языка «на лету» — без перезапуска окна", 15, INK, anchor="middle", bold=True)
    # left: language files (embedded resources)
    b += box(20, 62, 190, 150, AMB_L, AMB)
    b += txt(115, 88, "langs/*.json", 15, AMB, anchor="middle", bold=True)
    b += txt(115, 106, "9 языков — ресурсы", 11.5, GRY, anchor="middle")
    for i, t in enumerate(["ru · en · fr", "de · es · fa", "zh · uk · tr"]):
        b += box(40, 120 + i*28, 150, 22, "#FFFFFF", AMB, 5)
        b += txt(115, 135 + i*28, t, 12, INK, anchor="middle")
    # center: manager
    b += box(290, 80, 180, 96, BLUE_L, BLUE)
    b += txt(380, 110, "Localization", 15, BLUE, anchor="middle", bold=True)
    b += txt(380, 128, "Manager", 15, BLUE, anchor="middle", bold=True)
    b += txt(380, 152, "словарь this[ключ]", 12, INK, anchor="middle")
    # right: UI
    b += box(545, 80, 150, 96, TEAL_L, TEAL)
    b += txt(620, 108, "Интерфейс", 15, TEAL, anchor="middle", bold=True)
    b += box(558, 124, 124, 26, "#FFFFFF", TEAL, 5)
    b += txt(620, 142, "{loc:Loc ключ}", 12, INK, anchor="middle")
    # read path (labels above the lines, clear of arrows)
    b += arrow(210, 128, 288, 128, GRY)
    b += txt(249, 120, "читает", 11, GRY, anchor="middle")
    b += arrow(470, 118, 543, 118, GRY)
    b += txt(506, 110, "даёт текст", 11, GRY, anchor="middle")
    # bottom: language chooser under the manager
    b += box(300, 250, 160, 34, GRY_L, GRY)
    b += txt(380, 272, "Язык  ▼", 14, GRY, anchor="middle", bold=True)
    b += arrow(360, 248, 360, 178, AMB)
    b += txt(330, 216, "1) выбор", 11.5, AMB, anchor="end")
    # manager raises "Item[]" -> UI refreshes (curve on the right, clear of arrow 1)
    b += f'<path d="M 472 152 C 545 214, 600 214, 620 178" fill="none" stroke="{GRN}" stroke-width="2.5"/>'
    b += f'<polygon points="620,178 614,190 626,190" fill="{GRN}"/>'
    b += txt(556, 240, "2) сигнал «Item[]» → весь текст обновляется", 11.5, GRN, anchor="middle", bold=True)
    return svg(710, 300, b)

def dia_storage():
    b = ''
    b += txt(355, 30, "Секреты на диске: шифрование DPAPI и атомарная запись", 14.5, INK, anchor="middle", bold=True)
    b += box(20, 76, 150, 84, TEAL_L, TEAL)
    b += txt(95, 106, "Программа", 15, TEAL, anchor="middle", bold=True)
    b += txt(95, 130, "профили и", 12, INK, anchor="middle")
    b += txt(95, 146, "настройки", 12, INK, anchor="middle")
    b += box(255, 68, 190, 100, BLUE_L, BLUE)
    b += txt(350, 96, "DPAPI (Windows)", 14.5, BLUE, anchor="middle", bold=True)
    b += box(272, 106, 156, 24, "#FFFFFF", BLUE, 5)
    b += txt(350, 123, "ProtectedData", 12, INK, anchor="middle")
    b += txt(350, 150, "ключ = учётная запись", 11.5, GRY, anchor="middle")
    b += box(530, 76, 160, 84, GRY_L, GRY)
    b += txt(610, 104, "profiles.json", 14, GRY, anchor="middle", bold=True)
    b += txt(610, 130, "нечитаемый", 12, INK, anchor="middle")
    b += txt(610, 146, "шифртекст", 12, INK, anchor="middle")
    # write path (top), read path (below, opposite direction)
    b += arrow(170, 106, 253, 106, GRY)
    b += txt(211, 98, "шифрует", 11, GRY, anchor="middle")
    b += arrow(445, 106, 528, 106, GRY)
    b += txt(486, 98, "пишет атомарно", 11, GRY, anchor="middle")
    b += arrow(528, 142, 447, 142, TEAL)
    b += txt(487, 186, "читает обратно и расшифровывает", 11, TEAL, anchor="middle")
    # atomic-write callout
    b += box(110, 198, 490, 34, AMB_L, AMB)
    b += txt(355, 220, "Запись: temp-файл → File.Replace (замена целиком) — без «полу-записанных» файлов", 11, AMB, anchor="middle", bold=True)
    # guard
    b += box(110, 242, 490, 34, RED_L, RED)
    b += txt(355, 264, "Другой пользователь или другой ПК расшифровать НЕ смогут", 12, RED, anchor="middle", bold=True)
    return svg(710, 292, b)

def dia_cicd():
    b = ''
    b += txt(355, 28, "Сборка и выпуск: тесты на каждый push, релиз — по тегу", 14.5, INK, anchor="middle", bold=True)
    # left column: push -> GitHub Actions
    b += box(20, 110, 110, 46, GRY_L, GRY)
    b += txt(75, 138, "git push", 13.5, GRY, anchor="middle", bold=True)
    b += box(150, 110, 120, 46, BLUE_L, BLUE)
    b += txt(210, 132, "GitHub", 14, BLUE, anchor="middle", bold=True)
    b += txt(210, 149, "Actions", 11, GRY, anchor="middle")
    b += arrow(130, 133, 148, 133, GRY)
    # lane A (ci.yml) — top
    b += box(330, 52, 150, 46, TEAL_L, TEAL)
    b += txt(405, 73, "ci.yml", 13, TEAL, anchor="middle", bold=True)
    b += txt(405, 90, "build + test", 11, INK, anchor="middle")
    b += box(510, 52, 180, 46, GRN_L, GRN)
    b += txt(600, 73, "зелёная галка", 12, GRN, anchor="middle", bold=True)
    b += txt(600, 90, "можно сливать (merge)", 10.5, GRY, anchor="middle")
    b += arrow(480, 75, 508, 75, GRY)
    b += arrow(272, 118, 328, 88, GRY)
    b += txt(286, 76, "push / PR", 10, GRY, anchor="start")
    # lane B (release.yml) — bottom
    rs = [("build\n+ test", TEAL_L, TEAL), ("portable\n.zip", BLUE_L, BLUE),
          ("installer\n.exe", BLUE_L, BLUE), ("GitHub\nRelease", GRN_L, GRN)]
    x = 330; xs = []
    for t, cl, c in rs:
        b += box(x, 150, 86, 52, cl, c, 8)
        for k, ln in enumerate(t.split("\n")):
            b += txt(x + 43, 172 + k*15, ln, 11, c, anchor="middle", bold=True)
        xs.append(x); x += 94
    for i in range(len(rs) - 1):
        b += arrow(xs[i] + 86, 176, xs[i+1], 176, GRY)
    b += arrow(272, 148, 328, 172, GRY)
    b += txt(298, 148, "тег v*", 10, GRY, anchor="start")
    # release -> users' auto-update
    b += box(300, 236, 320, 42, AMB_L, AMB)
    b += txt(460, 254, "у пользователей: автообновление берёт", 11, AMB, anchor="middle", bold=True)
    b += txt(460, 269, "/releases/latest и сверяет SHA256", 11, AMB, anchor="middle", bold=True)
    b += arrow(xs[3] + 43, 202, 470, 234, AMB)
    return svg(710, 292, b)

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

section("8. Языки интерфейса: перевод «на лету»", dia_i18n(), [
    "Программа говорит на девяти языках, и переключение происходит <b>без "
    "перезапуска</b> окна. Все надписи лежат не в коде, а в отдельных файлах-словарях "
    "<font face='Arial-Bold'>langs/*.json</font> (по одному на язык), встроенных в "
    "программу как ресурсы.",
    "В окне вместо готового текста стоит <b>ссылка на ключ</b> — запись "
    "<font face='Arial-Bold'>{loc:Loc ключ}</font>. Её обслуживает "
    "<b>LocalizationManager</b>: он работает как словарь «ключ → перевод». Когда вы "
    "выбираете язык в списке, менеджер подаёт сигнал <b>«Item[]»</b>, и все привязки "
    "перечитывают текст сами — интерфейс мгновенно меняет язык.",
    "Тест <i>LocalizationTests</i> следит, чтобы во всех девяти файлах был "
    "<b>одинаковый набор ключей</b>, не было пустых строк и совпадали подстановки вида "
    "{0}. Поэтому любую новую надпись приходится добавлять сразу во все языки — забыть "
    "перевод не получится.",
])

section("9. Хранение секретов: DPAPI и атомарная запись", dia_storage(), [
    "Профили и настройки хранятся в вашей папке данных. Пароли и адреса серверов — это "
    "секреты, поэтому файл <b>шифруется</b> средствами Windows — <b>DPAPI</b> "
    "(ProtectedData). Ключ шифрования привязан к <b>вашей учётной записи</b>, так что "
    "другой пользователь или другой компьютер файл не прочитают.",
    "Записывается файл <b>атомарно</b>: сначала данные пишутся во временный файл, затем "
    "одной операцией <font face='Arial-Bold'>File.Replace</font> он заменяет старый. "
    "Если питание пропадёт посреди записи — останется целый прежний файл, а не "
    "«половинка».",
    "Формат помечен полем <b>schemaVersion</b> — это позволяет менять структуру в "
    "будущем и по-прежнему открывать старые файлы (обратная совместимость). Старый "
    "незашифрованный формат тоже ещё читается и молча переводится на новый.",
])

section("10. Как собирается и выпускается программа", dia_cicd(), [
    "Каждый раз, когда код отправляется на GitHub (<font face='Arial-Bold'>git "
    "push</font> или Pull Request), сервер сам запускает рабочий процесс <b>ci.yml</b>: "
    "собирает проект и прогоняет все тесты. Красная галочка вместо зелёной сразу "
    "показывает, что что-то сломалось, — ошибку видно ещё до пользователя.",
    "Выпуск версии запускается <b>по тегу</b> вида <font face='Arial-Bold'>v0.2.0</font>: "
    "процесс <b>release.yml</b> собирает и тестирует, делает <b>portable-архив</b> и "
    "<b>установщик</b>, а затем создаёт <b>релиз на GitHub</b> с этими файлами. "
    "Встроенная автообновлялка потом берёт последний релиз "
    "(<font face='Arial-Bold'>/releases/latest</font>) и сверяет контрольную сумму "
    "SHA256.",
    "Так «сборка вручную» превращается в <b>конвейер</b> (CI/CD): человек только ставит "
    "тег, а всё остальное — сборку, тесты и публикацию — сервер делает одинаково и без "
    "ошибок.",
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
    ("i18n / L10n", "internationalization / localization", "перевод интерфейса на разные языки"),
    ("PR", "Pull Request", "запрос на слияние изменений в общий код"),
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
    "<b>LocalizationManager.cs</b> — как одна смена языка обновляет весь текст сразу.",
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
