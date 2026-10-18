"""Рисует dept.gif (GIF87a, 16 цветов) — «фотографию кафедры» для реализации web-1995 примера directory.
Средство подготовки содержания, без зависимостей. Вызов: python3 scripts/make_dept_gif.py <путь к dept.gif>
"""
import random, struct, sys
W, H = 128, 80
pal = [(0,0,0),(255,255,255),(150,190,230),(170,205,240),(190,215,245),(120,120,120),(160,150,140),
       (190,180,165),(80,60,50),(40,60,110),(200,220,240),(60,120,50),(40,90,35),(110,90,60),(140,140,140),(90,90,90)]
random.seed(1995)
img = [[0]*W for _ in range(H)]
for y in range(H):
    for x in range(W):
        if y < 50:   # небо с полосами и шумом (дизеринг)
            c = 2 if y < 16 else 3 if y < 33 else 4
            if random.random() < 0.08: c = min(4, c + 1)
        else:        # газон
            c = 11 if random.random() < 0.7 else 12
        img[y][x] = c
# здание: фасад, крыша-фронтон, колонны, окна, дверь
for y in range(22, 62):
    for x in range(20, 108):
        img[y][x] = 7 if random.random() < 0.85 else 6
for y in range(10, 22):
    half = (y - 10) * 44 // 12
    for x in range(64 - half, 64 + half):
        img[y][x] = 6
for row in (26, 40):
    for x0 in range(26, 104, 12):
        for y in range(row, row + 9):
            for x in range(x0, x0 + 6):
                img[y][x] = 9 if (y - row) < 5 else 10
for x0 in range(22, 108, 10):
    for y in range(52, 62):
        img[y][x0] = 14; img[y][x0 + 1] = 1
for y in range(50, 62):
    for x in range(58, 70):
        img[y][x] = 8
for y in range(62, 66):
    for x in range(14, 114):
        img[y][x] = 5 if (x + y) % 3 else 15
# деревья
for cx, cy in ((8, 50), (118, 48)):
    for y in range(cy - 10, cy + 6):
        for x in range(cx - 7, cx + 8):
            if 0 <= x < W and (x - cx) ** 2 + (y - cy) ** 2 < 50 and random.random() < 0.9:
                img[y][x] = 12 if random.random() < 0.5 else 11
    for y in range(cy + 4, cy + 14):
        img[y][cx] = 13

def lzw(pixels, min_size=4):
    clear, end = 1 << min_size, (1 << min_size) + 1
    size, nxt = min_size + 1, end + 1
    table = {(i,): i for i in range(clear)}
    out, buf, nbits = bytearray(), 0, 0
    def emit(code):
        nonlocal buf, nbits
        buf |= code << nbits; nbits += size
        while nbits >= 8:
            out.append(buf & 255); buf >>= 8; nbits -= 8
    emit(clear)
    w = (pixels[0],)
    for p in pixels[1:]:
        wc = w + (p,)
        if wc in table:
            w = wc; continue
        emit(table[w])
        if nxt < 4096:
            table[wc] = nxt; nxt += 1
            if nxt > (1 << size) and size < 12: size += 1
        else:
            emit(clear); table = {(i,): i for i in range(clear)}; size, nxt = min_size + 1, end + 1
        w = (p,)
    emit(table[w]); emit(end)
    if nbits: out.append(buf & 255)
    return bytes(out)

data = lzw([c for row in img for c in row])
gif = bytearray(b"GIF87a" + struct.pack("<HHBBB", W, H, 0xF3, 0, 0))
for r, g, b in pal: gif += bytes((r, g, b))
gif += b"," + struct.pack("<HHHHB", 0, 0, W, H, 0) + bytes((4,))
for i in range(0, len(data), 255):
    chunk = data[i:i+255]; gif += bytes((len(chunk),)) + chunk
gif += b"\x00;"
open(sys.argv[1], "wb").write(gif)
print(len(gif), "bytes")
