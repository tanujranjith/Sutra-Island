import math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops
from scipy import ndimage

REF = '/workspace/work/ref/'
FD = '/workspace/work/fonts/inter/extras/ttf/'
SS = 2                      # supersample
W, H = 900, 380             # final size
FPS = 20
CX = W // 2
TOP = 44

def F(name, size): return ImageFont.truetype(FD + f'Inter-{name}.ttf', int(size * SS))

# ---------- mascot prep ----------
src = np.array(Image.open(REF + 'sutra-assistant-logo.png').convert('RGB')).astype(int)
near = (src.min(axis=2) > 185) & ((src.max(axis=2) - src.min(axis=2)) < 50)
lab, _ = ndimage.label(near)
cl = {lab[0, 0], lab[0, -1], lab[-1, 0], lab[-1, -1], lab[-1, 627]} - {0}
bg = np.isin(lab, list(cl))
fg = ndimage.binary_fill_holes(~bg)
sh = np.zeros_like(fg); sh[930:] = src[930:].min(axis=2) > 140
fg &= ~sh
fg = ndimage.binary_opening(fg, iterations=3)
fg = ndimage.binary_erosion(fg, iterations=3)
lb, nb = ndimage.label(fg)
if nb > 1:
    szs = ndimage.sum(fg, lb, range(1, nb + 1)); fg = lb == (int(np.argmax(szs)) + 1)
# inpaint eyes by row interpolation
for (x0, x1) in [(489, 556), (697, 764)]:
    xa, xb = x0 - 10, x1 + 10
    for y in range(536, 682):
        l = src[y, xa].astype(float); r = src[y, xb].astype(float)
        for x in range(xa, xb + 1):
            f = (x - xa) / (xb - xa); src[y, x] = l * (1 - f) + r * f
alpha = Image.fromarray((fg * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2))
mimg = Image.fromarray(src.astype(np.uint8)).convert('RGBA')
mimg.putalpha(alpha)
mimg = mimg.crop((230, 229, 1024, 1056))
MW, MH = mimg.size
EYES = [(292.5 / MW, 379.5 / MH), (500.5 / MW, 379.5 / MH)]
EW, EH = 67 / MW, 129 / MW
_mcache = {}

def mascot(size, eyes='open', grey=False):
    """size = width in final px. returns RGBA at SS."""
    key = (round(size, 1), eyes, grey)
    if key in _mcache: return _mcache[key]
    w = max(2, int(size * SS)); h = int(w * MH / MW)
    m = mimg.resize((w, h), Image.LANCZOS)
    if grey:
        a = m.getchannel('A'); g = m.convert('L').convert('RGB')
        g = Image.blend(g, Image.new('RGB', g.size, (120, 135, 160)), 0.35)
        m = g.convert('RGBA'); m.putalpha(a)
    dr = ImageDraw.Draw(m)
    navy = (22, 30, 92, 255) if not grey else (70, 78, 96, 255)
    dx, dy = 0, 0
    if eyes in ('up',): dy = -0.035
    if eyes in ('upright',): dx, dy = 0.03, -0.04
    for ex, ey in EYES:
        cx, cy = (ex + dx) * w, (ey + dy) * h
        ew, eh = EW * w, EH * w
        if eyes == 'blink':
            dr.rounded_rectangle((cx - ew / 2, cy - eh * 0.07, cx + ew / 2, cy + eh * 0.07), radius=ew / 2, fill=navy)
        elif eyes == 'happy':
            r = ew * 0.95
            dr.arc((cx - r, cy - r * 0.55, cx + r, cy + r * 1.25), 200, 340, fill=navy, width=max(2, int(ew * 0.42)))
        elif eyes == 'flat':
            dr.rounded_rectangle((cx - ew * 0.8, cy - ew * 0.22, cx + ew * 0.8, cy + ew * 0.22), radius=ew * 0.22, fill=navy)
        else:
            dr.rounded_rectangle((cx - ew / 2, cy - eh / 2, cx + ew / 2, cy + eh / 2), radius=ew / 2, fill=navy)
            if not grey:
                dr.rounded_rectangle((cx - ew * 0.22, cy - eh * 0.36, cx + ew * 0.12, cy - eh * 0.02), radius=ew * 0.17, fill=(60, 80, 220, 255))
    _mcache[key] = m
    return m

art = Image.open(REF + 'current-island-preview.png').convert('RGB').crop((16, 6, 59, 48))
art = art.resize((int(86 * SS), int(84 * SS)), Image.LANCZOS)
amask = Image.new('L', art.size, 0)
ImageDraw.Draw(amask).rounded_rectangle((0, 0, art.size[0] - 1, art.size[1] - 1), radius=16 * SS, fill=255)

# ---------- helpers ----------
def clamp(x, a=0, b=1): return max(a, min(b, x))
def lerp(a, b, t): return a + (b - a) * t
def ease(t): t = clamp(t); return t * t * (3 - 2 * t)
def ease_out(t): t = clamp(t); return 1 - (1 - t) ** 3
def ease_back(t, s=1.9):
    t = clamp(t) - 1; return t * t * ((s + 1) * t + s) + 1
def seg(t, a, b): return clamp((t - a) / (b - a))
def S(v): return int(round(v * SS))

def background():
    g = np.linspace(0, 1, H * SS)[:, None]
    top = np.array([12, 22, 48]); bot = np.array([6, 12, 30])
    arr = (top * (1 - g) + bot * g)[:, None, :].repeat(W * SS, axis=1) if False else None
    col = top[None, :] * (1 - g) + bot[None, :] * g
    arr = np.repeat(col[:, None, :], W * SS, axis=1).astype(np.uint8)
    return Image.fromarray(arr, 'RGB').convert('RGBA')
BG = background()

def text_layer(size_xy, xy, txt, font, fill, anchor='la'):
    L = Image.new('RGBA', size_xy, (0, 0, 0, 0))
    ImageDraw.Draw(L).text(xy, txt, font=font, fill=fill, anchor=anchor)
    return L

def fade(img, a):
    if a >= 0.999: return img
    r = img.copy(); r.putalpha(r.getchannel('A').point(lambda v: int(v * a))); return r

fTitle, fSub, fSmall, fBig, fMid = F('Bold', 25), F('Regular', 21), F('SemiBold', 13), F('SemiBold', 25), F('Medium', 24)
fAns, fHint, fLabel = F('Medium', 23), F('Medium', 15), F('SemiBold', 13)

def idle_content(alpha):
    """compact island contents, coordinates relative to 500x110 pill"""
    L = Image.new('RGBA', (S(500), S(110)), (0, 0, 0, 0))
    a = Image.new('RGBA', art.size); a.paste(art, (0, 0)); a.putalpha(amask)
    L.alpha_composite(a, (S(32), S(13)))
    d = ImageDraw.Draw(L)
    d.text((S(150), S(32)), 'The Rem\u2026', font=fTitle, fill=(255, 255, 255, 255), anchor='lm')
    d.text((S(150), S(72)), 'Drake  |  Ch\u2026', font=fSub, fill=(150, 160, 178, 255), anchor='lm')
    d.line((S(312), S(24), S(312), S(86)), fill=(70, 74, 84, 255), width=S(1.5))
    d.text((S(364), S(34)), 'RAM', font=fSmall, fill=(150, 160, 178, 255), anchor='mm')
    d.text((S(364), S(66)), '91%', font=fBig, fill=(214, 160, 92, 255), anchor='mm')
    d.text((S(476), S(36)), '6:50', font=fTitle, fill=(255, 255, 255, 255), anchor='rm')
    d.rounded_rectangle((S(410), S(64), S(430), S(74)), radius=S(3), outline=(170, 175, 185, 255), width=S(1.5))
    d.text((S(476), S(70)), '100%', font=F('Medium', 17), fill=(200, 205, 214, 255), anchor='rm')
    return fade(L, alpha)

TRANSCRIPT = 'turn the volume down to 5%'.split()
ANSWER1 = "Done. I'll remind you"
ANSWER2 = 'Thursday at 4 PM.'

random.seed(7)
bar_noise = [[random.random() for _ in range(5)] for _ in range(400)]

def frame(t):
    img = BG.copy()
    # ----- timeline -----
    T_WAKE, T_LIST, T_SPEECH_END, T_THINK, T_GROW, T_SPEAK, T_COLLAPSE, T_END = 1.0, 1.8, 4.0, 4.2, 5.0, 5.3, 8.0, 8.8
    wake = ease(seg(t, T_WAKE, T_WAKE + 0.6))
    out = ease(seg(t, T_COLLAPSE, T_COLLAPSE + 0.7))
    active = wake * (1 - out)
    grow = 0.0
    pw = lerp(500, 690, active)
    ph = lerp(110, 150, grow)
    x0, y0 = CX - pw / 2, TOP
    state = 'IDLE'
    if T_WAKE <= t < T_LIST: state = 'WAKE'
    elif T_LIST <= t < T_THINK: state = 'LISTENING'
    elif T_THINK <= t < T_GROW: state = 'THINKING'
    elif T_GROW <= t < T_COLLAPSE: state = 'DOING IT'
    elif T_COLLAPSE <= t < T_COLLAPSE + 0.8: state = 'BACK TO ISLAND'

    # glow behind mascot while speaking
    mx = x0 + 18 + 42
    my = y0 + 55
    # pill
    pill = Image.new('RGBA', img.size, (0, 0, 0, 0))
    pd = ImageDraw.Draw(pill)
    r = 55
    pd.rounded_rectangle((S(x0), S(y0), S(x0 + pw), S(y0 + ph)), radius=S(r), fill=(0, 0, 0, 255))
    # rim
    rim_a = active
    if rim_a > 0.01:
        rim = Image.new('RGBA', img.size, (0, 0, 0, 0))
        rd = ImageDraw.Draw(rim)
        rd.rounded_rectangle((S(x0 - 1), S(y0 - 1), S(x0 + pw + 1), S(y0 + ph + 1)), radius=S(r + 1), outline=(185, 166, 255, int(210 * rim_a)), width=S(2.2))
        glow = rim.filter(ImageFilter.GaussianBlur(S(5)))
        img.alpha_composite(glow)
        img.alpha_composite(pill)
        img.alpha_composite(rim)
    else:
        img.alpha_composite(pill)

    # idle content (fades as mascot arrives, back on collapse)
    idle_a = 1 - ease(seg(t, T_WAKE, T_WAKE + 0.28)) + ease(seg(t, T_COLLAPSE + 0.35, T_COLLAPSE + 0.8))
    idle_a = clamp(idle_a)
    if idle_a > 0.01:
        ic = idle_content(idle_a)
        # keep centered on pill
        img.alpha_composite(ic, (S(CX - 250), S(TOP)))

    # ----- mascot -----
    in_a = ease_out(seg(t, T_WAKE + 0.15, T_WAKE + 0.65))
    in_s = ease_back(seg(t, T_WAKE + 0.15, T_WAKE + 0.75), 1.3)
    out_rise = ease(seg(t, T_COLLAPSE, T_COLLAPSE + 0.45))
    mvis = t >= T_WAKE and out_rise < 1
    if mvis:
        eyes = 'open'
        if T_LIST + 0.2 <= t < T_SPEECH_END: eyes = 'up'
        if T_THINK <= t < T_GROW: eyes = 'upright'
        if T_GROW + 0.15 <= t < T_COLLAPSE: eyes = 'happy'
        for bt in (1.75, 3.2, T_COLLAPSE + 0.05):
            if bt <= t < bt + 0.12: eyes = 'blink'
        size = 72
        bob = 0
        if T_THINK <= t < T_GROW: bob = math.sin((t - T_THINK) * 9) * 2.0
        if T_GROW <= t < T_COLLAPSE: bob = math.sin((t - T_GROW) * 7) * 1.6; size = 72 + 2.5 * max(0, math.sin((t - T_GROW) * 7))
        size = size * lerp(0.72, 1.0, in_s) * lerp(1.0, 0.8, out_rise)
        slot_cx = x0 + 22 + 36 + lerp(-16, 0, ease_out(seg(t, T_WAKE + 0.15, T_WAKE + 0.7))) - 12 * out_rise
        slot_cy = y0 + 55 + bob
        my_top = slot_cy - size * MH / MW / 2
        mx_left = slot_cx - size / 2
        m_alpha = in_a * (1 - out_rise)
        mcx, mcy = mx_left + size / 2, my_top + size * MH / MW / 2
        # speaking glow
        if T_GROW <= t < T_COLLAPSE + 0.3:
            ga = ease(seg(t, T_GROW, T_GROW + 0.4)) * (1 - out) * (0.75 + 0.25 * math.sin((t - T_GROW) * 7))
            gl = Image.new('RGBA', img.size, (0, 0, 0, 0))
            ImageDraw.Draw(gl).ellipse((S(mcx - 46), S(mcy - 46), S(mcx + 46), S(mcy + 46)), fill=(140, 100, 255, int(150 * ga)))
            img.alpha_composite(gl.filter(ImageFilter.GaussianBlur(S(14))))
        # listening ripples
        if T_LIST <= t < T_SPEECH_END + 0.3:
            rl = Image.new('RGBA', img.size, (0, 0, 0, 0)); rdd = ImageDraw.Draw(rl)
            fadeall = 1 - seg(t, T_SPEECH_END, T_SPEECH_END + 0.3)
            for k in range(2):
                ph_ = ((t - T_LIST) * 0.9 + k * 0.5) % 1.0
                rr = 41 + ph_ * 24
                a = int(170 * (1 - ph_) * fadeall * ease(seg(t, T_LIST, T_LIST + 0.3)))
                rdd.ellipse((S(mcx - rr), S(mcy - rr), S(mcx + rr), S(mcy + rr)), outline=(80, 215, 255, a), width=S(2))
            img.alpha_composite(rl)
        # thinking orbit
        if T_THINK <= t < T_GROW + 0.2:
            oa = ease(seg(t, T_THINK, T_THINK + 0.25)) * (1 - seg(t, T_GROW - 0.05, T_GROW + 0.2))
            ol = Image.new('RGBA', img.size, (0, 0, 0, 0)); od = ImageDraw.Draw(ol)
            rr = 45; ang = (t - T_THINK) * 420
            for i in range(28):
                f = i / 28
                c = (int(lerp(70, 160, f)), int(lerp(210, 110, f)), 255, int(255 * oa * f))
                od.arc((S(mcx - rr), S(mcy - rr), S(mcx + rr), S(mcy + rr)), ang + i * 7, ang + i * 7 + 8, fill=c, width=S(4))
            img.alpha_composite(ol.filter(ImageFilter.GaussianBlur(S(0.6))))
        m = fade(mascot(size, eyes), m_alpha)
        mlay = Image.new('RGBA', img.size, (0, 0, 0, 0))
        mlay.alpha_composite(m, (S(mx_left), S(my_top)))
        # clip mascot to pill once settled? allow drop from above: no clip
        img.alpha_composite(mlay)

    # ----- transcript / thinking text -----
    tx = x0 + 116
    if T_WAKE + 0.4 <= t < T_GROW + 0.25:
        words_shown = 0
        speech = seg(t, T_LIST + 0.3, T_SPEECH_END - 0.3)
        nwords = speech * len(TRANSCRIPT)
        words_shown = int(nwords)
        partial = nwords - words_shown
        txt = ' '.join(TRANSCRIPT[:words_shown])
        nxt = TRANSCRIPT[words_shown] if words_shown < len(TRANSCRIPT) else None
        tl = Image.new('RGBA', img.size, (0, 0, 0, 0)); td = ImageDraw.Draw(tl)
        thinking = t >= T_THINK
        fade_out = 1 - seg(t, T_GROW - 0.05, T_GROW + 0.25)
        base = (255, 255, 255, int(255 * fade_out))
        if thinking: base = (130, 138, 156, int(255 * fade_out))
        td.text((S(tx), S(y0 + 55)), txt, font=fMid, fill=base, anchor='lm')
        if words_shown == 0 and partial < 0.3:
            pa = ease(seg(t, T_WAKE + 0.5, T_LIST + 0.1))
            td.text((S(tx), S(y0 + 55)), 'Listening…', font=fMid, fill=(120, 128, 150, int(255 * pa)), anchor='lm')
        if nxt and not thinking:
            wpos = td.textlength(txt + (' ' if txt else ''), font=fMid)
            td.text((S(tx) + wpos, S(y0 + 55)), nxt, font=fMid, fill=(255, 255, 255, int(255 * partial * 0.8)), anchor='lm')
        if thinking:
            # shimmer sweep
            mask = Image.new('L', img.size, 0)
            ImageDraw.Draw(mask).text((S(tx), S(y0 + 55)), txt, font=fMid, fill=255, anchor='lm')
            tw = ImageDraw.Draw(mask).textlength(txt, font=fMid) / SS
            ph_ = ((t - T_THINK) / 0.9) % 1.0
            bx = tx - 60 + ph_ * (tw + 120)
            band = Image.new('L', img.size, 0)
            ImageDraw.Draw(band).rectangle((S(bx - 30), 0, S(bx + 30), img.size[1]), fill=255)
            band = band.filter(ImageFilter.GaussianBlur(S(18)))
            sh = ImageChops.multiply(mask, band).point(lambda v: int(v * fade_out))
            white = Image.new('RGBA', img.size, (255, 255, 255, 0)); white.putalpha(sh)
            tl.alpha_composite(white)
        img.alpha_composite(tl)

    # ----- voice bars -----
    if T_LIST <= t < T_THINK:
        bl = Image.new('RGBA', img.size, (0, 0, 0, 0)); bd = ImageDraw.Draw(bl)
        level = ease(seg(t, T_LIST, T_LIST + 0.3)) * (1 - ease(seg(t, T_SPEECH_END - 0.25, T_SPEECH_END + 0.05)))
        fi = int(t * FPS)
        cols = [(70, 220, 255), (80, 190, 255), (100, 150, 255), (130, 120, 255), (150, 100, 255)]
        fadeb = 1 - seg(t, T_SPEECH_END + 0.05, T_THINK)
        bx0 = x0 + pw - 104
        for i in range(5):
            n = bar_noise[fi % 400][i]
            n2 = bar_noise[(fi + 1) % 400][i]
            amp = 0.35 + 0.65 * (0.5 * n + 0.5 * math.sin(t * 11 + i * 1.3) ** 2)
            if i in (0, 4): amp *= 0.6
            hh = 8 + 40 * amp * level
            cx_ = bx0 + i * 15
            bd.rounded_rectangle((S(cx_ - 3.5), S(y0 + 55 - hh / 2), S(cx_ + 3.5), S(y0 + 55 + hh / 2)), radius=S(3.5), fill=cols[i] + (int(255 * fadeb),))
        img.alpha_composite(bl)

    # ----- volume action -----
    if T_GROW <= t < T_COLLAPSE + 0.5:
        al = Image.new('RGBA', img.size, (0, 0, 0, 0)); ad = ImageDraw.Draw(al)
        aa = ease(seg(t, T_GROW + 0.22, T_GROW + 0.5)) * (1 - ease(seg(t, T_COLLAPSE - 0.05, T_COLLAPSE + 0.25)))
        A = lambda v: int(v * aa)
        lv = lerp(62, 5, ease(seg(t, T_GROW + 0.55, T_GROW + 1.55)))
        # speaker icon
        ix, iy = tx + 2, y0 + 50
        ad.polygon([(S(ix), S(iy - 6)), (S(ix + 7), S(iy - 6)), (S(ix + 15), S(iy - 13)), (S(ix + 15), S(iy + 13)), (S(ix + 7), S(iy + 6)), (S(ix), S(iy + 6))], fill=(255, 255, 255, A(255)))
        waves = 3 if lv > 45 else (2 if lv > 20 else 1)
        for k in range(3):
            rr = 7 + k * 6
            on = k < waves
            ad.arc((S(ix + 13 - rr), S(iy - rr), S(ix + 13 + rr), S(iy + rr)), -45, 45, fill=(255, 255, 255, A(255 if on else 50)), width=S(2.2))
        # labels
        sx0, sx1 = tx + 50, x0 + pw - 150
        ad.text((S(sx0), S(y0 + 34)), 'Volume', font=F('SemiBold', 17), fill=(255, 255, 255, A(255)), anchor='lm')
        ad.text((S(sx0 + 72), S(y0 + 34)), 'Speakers · System output', font=F('Regular', 14), fill=(140, 148, 168, A(255)), anchor='lm')
        sy = y0 + 66
        ad.rounded_rectangle((S(sx0), S(sy - 3.5), S(sx1), S(sy + 3.5)), radius=S(3.5), fill=(60, 62, 74, A(255)))
        kx = lerp(sx0, sx1, lv / 100)
        ad.rounded_rectangle((S(sx0), S(sy - 3.5), S(max(kx, sx0 + 7)), S(sy + 3.5)), radius=S(3.5), fill=(185, 166, 255, A(255)))
        ad.ellipse((S(kx - 9), S(sy - 9), S(kx + 9), S(sy + 9)), fill=(255, 255, 255, A(255)))
        # percent
        ad.text((S(x0 + pw - 40), S(y0 + 56)), f'{int(round(lv))}%', font=F('SemiBold', 34), fill=(255, 255, 255, A(255)), anchor='rm')
        img.alpha_composite(al)

    # ----- state label -----
    ll = Image.new('RGBA', img.size, (0, 0, 0, 0)); ld = ImageDraw.Draw(ll)
    ld.text((S(CX), S(H - 34)), state, font=fLabel, fill=(120, 132, 160, 255), anchor='mm')
    hint = {'WAKE': 'hold the hotkey or say "Hey Sutra"', 'LISTENING': 'you talk, it transcribes live',
            'THINKING': 'request locked in, model working', 'DOING IT': 'system commands show the result live, then get out of the way',
            'BACK TO ISLAND': 'auto-collapses when done', 'IDLE': 'compact island'}[state]
    ld.text((S(CX), S(H - 16)), hint, font=F('Regular', 12), fill=(80, 92, 120, 255), anchor='mm')
    img.alpha_composite(ll)
    return img.convert('RGB').resize((W, H), Image.LANCZOS), 8.8

if __name__ == '__main__':
    import sys
    if len(sys.argv) > 1:
        for tt in sys.argv[1:]:
            frame(float(tt))[0].save(f'/workspace/work/orbgif/vtest_{tt}.png')
    else:
        frames = []
        total = 8.8
        n = int(total * FPS)
        for i in range(n):
            frames.append(frame(i / FPS)[0])
        frames[0].save('/workspace/work/orbgif/frames_ok.txt'.replace('frames_ok.txt', 'first.png'))
        import pickle
        for i, f in enumerate(frames): f.save(f'/workspace/work/orbgif/fv/{i:04d}.png')
        print('frames', n)
