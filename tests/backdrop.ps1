# A plain black full-screen window, run as its own process, to sit behind the widget while
# screenshots are taken.
#
# This is not cosmetic. The widget is translucent, so whatever is on the desktop is composited
# INTO the captured pixels - earlier contact sheets had file names and window titles showing
# faintly through the gauge. You cannot fix that afterwards; the only cure is to put something
# blank behind it at capture time.
param([int]$Seconds = 300)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.BackColor       = [System.Drawing.Color]::Black
$form.StartPosition   = 'Manual'
$form.Location        = New-Object System.Drawing.Point $screen.Left, $screen.Top
$form.Size            = New-Object System.Drawing.Size $screen.Width, $screen.Height
# TopMost, deliberately. A non-topmost backdrop still leaves the console window that
# launched the harness in front of it, and the translucent widget then composites THAT.
# The widget re-asserts its own topmost on start and on a timer, so it lands above this.
$form.TopMost         = $true
$form.ShowInTaskbar   = $false

# Self-destruct, so a crashed harness can never leave a black screen behind.
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = $Seconds * 1000
$timer.Add_Tick({ $form.Close() })
$timer.Start()

[System.Windows.Forms.Application]::Run($form)
