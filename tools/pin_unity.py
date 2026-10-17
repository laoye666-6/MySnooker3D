import ctypes, time, sys
u = ctypes.windll.user32
u.SetProcessDPIAware()
deadline = time.time() + 90
hwnd = 0
while time.time() < deadline and not hwnd:
    hwnd = u.FindWindowW(None, None)
    # 枚举找 Unity 主窗口：按标题含 "Snooker3D" 或类名 "UnityContainerWndClass"
    hwnd = 0
    buf = ctypes.create_unicode_buffer(256)
    def cb(h, _):
        global hwnd
        if u.IsWindowVisible(h):
            u.GetWindowTextW(h, buf, 256)
            t = buf.value
            if ("Snooker" in t or "Unity" in t) and ("Linux" not in t):
                u.GetClassNameW(h, buf, 256)
                if "Unity" in buf.value or "Snooker" in t:
                    hwnd = h
                    return False
        return True
    CMPFUNC = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    u.EnumWindows(CMPFUNC(cb), 0)
    if not hwnd: time.sleep(1.5)
if not hwnd:
    print("NO WINDOW"); sys.exit(1)
u.GetWindowTextW(hwnd, buf, 256); title = buf.value
# TOPMOST 钉住 + 最大化 + 前台
u.ShowWindow(hwnd, 3)       # SW_MAXIMIZE
HWND_TOPMOST = -1; SWP_NOMOVE=2; SWP_NOSIZE=1; SWP_SHOWWINDOW=0x40
u.SetWindowPos(hwnd, HWND_TOPMOST, 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW)
u.SetForegroundWindow(hwnd)
# 把鼠标移进窗口并点一下，强制接收焦点
r = ctypes.wintypes.RECT() if hasattr(ctypes, 'wintypes') else None
class RECT(ctypes.Structure):
    _fields_=[("l",ctypes.c_long),("t",ctypes.c_long),("r",ctypes.c_long),("b",ctypes.c_long)]
r = RECT()
u.GetWindowRect(hwnd, ctypes.byref(r))
cx, cy = (r.l+r.r)//2, (r.t+r.b)//2
u.SetCursorPos(cx, cy)
u.mouse_event(2,0,0,0,0); u.mouse_event(4,0,0,0,0)   # down/up
print("PINNED:", title, hwnd, cx, cy)
