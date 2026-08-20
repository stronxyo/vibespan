# Spike results

Five window-management assumptions were tested against each other before any application
code was written, because each one could have invalidated the design. Two were refuted.
The spikes live in `spikes/` and are self-reporting — build and run one and it writes its
own `report*.txt`.

Measured on Windows 11 Home 25H2 (build 26200.9168), .NET Framework 4.8.1, both monitors
at 96 DPI, compiled with the in-box `csc.exe` at `/langversion:5`.

---

## 1. `ShowInTaskbar=false` creates an owner window that sinks Z-order — CONFIRMED

```
ShowInTaskbar=TRUE  + WS_EX_TOOLWINDOW   GW_OWNER = 0            (no owner window)
ShowInTaskbar=FALSE                      GW_OWNER = 721118
                                         owner EXSTYLE = 0x00000100
                                         owner WS_EX_TOPMOST : no      <-- sinks us
```

WPF implements `ShowInTaskbar=false` with a hidden owner window that never receives
`WS_EX_TOPMOST`, and an owned window's band follows its owner's.

**Decision:** don't patch it — delete it. `WS_EX_TOOLWINDOW` alone removes the taskbar
button *and* the Alt-Tab entry with **no owner window created at all**. The app sets
`ShowInTaskbar = true` and applies `WS_EX_TOOLWINDOW` in `SourceInitialized`.

---

## 2. `WM_NCHITTEST` → `HTBOTTOMRIGHT` resize — **REFUTED**

```
ResizeMode=NoResize, WindowStyle=None
  STYLE          = 0x16080000
  WS_THICKFRAME  : no      <-- DefWindowProc's size/move loop bails without it
  WS_CAPTION     : no
  WS_SYSMENU     : YES
```

`DefWindowProc` turns `HTBOTTOMRIGHT` into `WM_SYSCOMMAND`/`SC_SIZE`, and the modal
size loop returns immediately when `WS_THICKFRAME` is absent — which `ResizeMode=NoResize`
strips. Returning `HTBOTTOMRIGHT` changes the cursor and does nothing else.

Forcing `WS_THICKFRAME` back on would re-enable Aero Snap, Win+Arrow and
double-click-maximize on a 204×44 widget, and would make the corner **non-client**, i.e. a
dead zone for the right-click menu that is the app's only settings UI.

**Decision:** no OS resize loop. See spike 5.

---

## 3. `LayoutTransform` scale drives the HWND — CONFIRMED

```
scale 1.00  ->  HWND 182 x 42     scale 1.50  ->  HWND 273 x 63
scale 1.25  ->  HWND 228 x 53     scale 2.00  ->  HWND 364 x 84
scale 0.75  ->  HWND 137 x 32
```

The HWND rect matched WPF's `ActualWidth`/`ActualHeight` exactly at every step. A
`ScaleTransform` in the **content** element's `LayoutTransform` (never the `Window`'s)
participates in `Measure`, so `SizeToContent=WidthAndHeight` resizes the real window.

**Decision:** one `Scale` double is the single source of truth for size.

---

## 4. Context-menu dismissal — CONFIRMED broken, then fixed

WPF `Popup` dismissal is **capture**-based, and only a foreground window gets full mouse
capture. A window with `ShowActivated=false` whose menu is opened programmatically is never
foreground, so outside clicks are never delivered and the menu never closes.

Tested against a genuinely **foreign** window in a second process — an early version clicked
a window in the same process, which a captured popup can see even when not foreground, and
that produced false passes.

```
1  real right-click, no hook                         foreground YES   dismissal WORKS
2  real right-click + selective WM_MOUSEACTIVATE     foreground YES   dismissal WORKS
3  programmatic open + SetForegroundWindow           foreground YES   dismissal WORKS
4  LEFT press with the hook                          foreground no    drag keeps focus elsewhere
```

**Decisions:**
- **No `WS_EX_NOACTIVATE` as a permanent style.** Instead hook `WM_MOUSEACTIVATE` and return
  `MA_NOACTIVATE` when the trigger is `WM_LBUTTONDOWN`, `MA_ACTIVATE` otherwise. Variant 4
  proves dragging still never steals focus; variant 2 proves the menu still works.
- **The tray menu must call `SetForegroundWindow` before opening** (variant 3) — it is opened
  programmatically and would otherwise never dismiss.

---

## 5. Resize feedback loop — FOUND and fixed

Applying the scale inside `Thumb.DragDelta` is **unstable**. Resizing the window moves the
grip under the cursor, `Thumb` raises another `DragDelta` from the changed geometry, and it
resizes again:

```
   move 01  expect=1.070  actual=1.000
   move 02  expect=1.140  actual=1.330
   move 03  expect=1.210  actual=0.600      <-- slammed into the min clamp
   move 04  expect=1.280  actual=3.000      <-- and the max clamp
   ...
   drag events received : 1526              for 12 cursor moves
```

The mapping maths was fine — `expect` rose smoothly and monotonically. The *event source* was
the problem. The cure is to decouple the driver from the geometry it perturbs:

- the grip only **captures the mouse** and records a screen-space anchor + starting scale
- the scale is applied on **`CompositionTarget.Rendering`**, reading `GetCursorPos` per frame
- the mapping is **absolute** from the anchor, never incremental, so it is *idempotent* —
  re-running it with an unchanged cursor yields an unchanged scale, which is what kills
  the loop

```
   move 07  d=(84,35)   expect=1.595  actual=1.595  MATCH  frames+20 applies+1
   move 08  d=(96,40)   expect=1.680  actual=1.680  MATCH  frames+21 applies+1
   move 09  d=(108,45)  expect=1.765  actual=1.765  MATCH  frames+21 applies+1
   move 10  d=(120,50)  expect=1.850  actual=1.850  MATCH  frames+19 applies+1
```

Exactly one apply per real cursor change, and `expect == actual` on every move.

Two further traps worth recording:
- **Never use `e.HorizontalChange`.** It is expressed in the Thumb's own
  `LayoutTransform`-scaled space, so as the scale grows the reported delta grows with it and
  the value diverges. Use raw `GetCursorPos` screen pixels.
- **Never `Thread.Sleep` on the UI thread** to "wait for a frame" — it blocks the dispatcher
  and therefore `CompositionTarget.Rendering` itself. An early run of this spike reported
  `frames: 0` for exactly that reason.

---

## 6. A uiAccess binary outside a secure location will not START — found during install

Documented behaviour is that Windows "silently ignores" the `uiAccess` flag when its
conditions are not met. That is not what happens. Running a `uiAccess="true"` binary that is
neither signed nor in an admin-only directory fails outright:

```
[ERROR] This command cannot be run due to the error: A referral was returned from the server.
```

The process never starts — there is no fall back to a normal window. This surfaced when the
per-user (`-NoUiAccess`) install path was still building against the uiAccess manifest.

**Consequence:** the two install modes need two different manifests. `Installer.ps1` keeps one
manifest as the source of truth and patches `uiAccess="true"` to `"false"` in a temp copy for
the per-user build.

It also means the failure mode for a *broken* uiAccess install is louder than expected — a
re-signed or moved binary will refuse to launch rather than quietly losing its Z-order. The
startup self-check (`GetTokenInformation`/`TokenUIAccess`, logged at every start) still earns
its keep for the case where the flag is granted but something else is wrong.

---

## Install verification (real install, not a spike)

Measured after `Installer.ps1` ran elevated on this machine.

```
uiAccess granted           yes          (logged at startup by the app's own self-check)
signature                  Valid, timestamped
Cert:\LocalMachine\My      absent       <-- private key destroyed after signing
Cert:\LocalMachine\Root    present, privateKey=False
integrity level            High         (inherent: a uiAccess process launched by a member
                                        of Administrators runs High IL - documented, and
                                        confirmed here by a medium-IL shell being denied
                                        both Stop-Process and Win32_Process queries on it)
files written by the app   owner = RZR-PC-SF\dj_el   <-- NOT BUILTIN\Administrators
```

That last line is the one that matters. The predecessor was launched directly from its
elevated installer, inherited a full admin token, and left `~/.claude/.credentials.json`
owned by `BUILTIN\Administrators`. Launching via `explorer.exe` hands the widget the
logged-on user's own token instead: it still gets High integrity from uiAccess, but its
default token owner is the user, so everything it writes stays user-owned.

A side effect worth knowing while testing: once uiAccess is granted, **synthetic input from
a medium-integrity process no longer reaches the widget**. UIPI only lets a uiAccess process
drive a higher-integrity UI. Scripted click/drag tests work against a `-NoUiAccess` build and
silently do nothing against an installed one - which looks exactly like a broken feature if
you do not know why.

---

## Testing notes

Two mistakes cost a re-run each and are worth remembering:

- **`mouse_event` with `MOUSEEVENTF_ABSOLUTE` normalises against the PRIMARY monitor**, not
  the virtual desktop, unless `MOUSEEVENTF_VIRTUALDESK` is also set. On this two-monitor
  layout that silently delivered clicks hundreds of pixels away. `SetCursorPos` plus
  button-only `mouse_event` avoids the whole problem.
- **`MOUSEEVENTF_MOVE` is relative and goes through pointer ballistics**, so a requested
  `(9,3)` becomes an unpredictable and much larger jump. Use absolute positioning in tests.

---

## Still outstanding

- **Tray icon + balloon tips at High IL.** Cannot be reproduced without the full
  sign → Program Files → `uiAccess` install, because that is the only way to obtain the
  integrity level. Its only kill criterion affects the *balloon* half of alerting; in-widget
  alerts are primary and unaffected, so it is safe to defer until the installer exists.
- **`SetWinEventHook` lifetime.** Low risk and well documented: keep the delegate in a static
  field (never an inline lambda), `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`, and
  retain a demoted 2–5 s fallback poll because the hook does not fire when a fullscreen app
  changes its own rect.
