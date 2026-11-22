$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class ChestCalibration : Form
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Edges edges);
    [StructLayout(LayoutKind.Sequential)] public struct Edges { public int Left, Top, Right, Bottom; }

    private readonly string file;
    private Point origin;
    private Rectangle selection;
    private bool dragging;
    private const int Minimum = 20;
    private static readonly double[] Defaults = { .60, .24, .19, .18 };

    public ChestCalibration(Rectangle bounds, string file)
    {
        this.file = file;
        Text = "Область распознавания Chest Island";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        Opacity = .55;
        BackColor = Color.FromArgb(23, 21, 31);
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        selection = FromFractions(Defaults);
        try
        {
            string[] fields = File.ReadAllText(file).Trim().Split(',');
            var value = new double[4];
            if (fields.Length == 4)
            {
                bool valid = true;
                for (int i = 0; i < 4; i++)
                    valid &= double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value[i]);
                if (valid && value[0] >= 0 && value[1] >= 0 && value[2] > 0 && value[3] > 0 &&
                    value[0] + value[2] <= 1.00001 && value[1] + value[3] <= 1.00001)
                    selection = FromFractions(value);
            }
        }
        catch (IOException) { }
    }

    private Rectangle FromFractions(double[] value)
    {
        return new Rectangle((int)(Width * value[0]), (int)(Height * value[1]),
            (int)(Width * value[2]), (int)(Height * value[3]));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var pen = new Pen(Color.FromArgb(224, 199, 255), 4))
        using (var fill = new SolidBrush(Color.FromArgb(70, 160, 116, 240)))
        using (var font = new Font("Segoe UI Semibold", 15))
        {
            e.Graphics.FillRectangle(fill, selection);
            e.Graphics.DrawRectangle(pen, selection);
            e.Graphics.DrawString("Выдели область уведомления мышью  •  Enter — сохранить  •  R — сбросить  •  Esc — отмена",
                font, Brushes.White, new PointF(16, 15));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        origin = e.Location;
        dragging = true;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        int x = Math.Max(0, Math.Min(Width, e.X)), y = Math.Max(0, Math.Min(Height, e.Y));
        selection = Rectangle.FromLTRB(Math.Min(x, origin.X), Math.Min(y, origin.Y),
            Math.Max(x, origin.X), Math.Max(y, origin.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        dragging = false;
        Capture = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
        if (e.KeyCode == Keys.R) { selection = FromFractions(Defaults); Invalidate(); }
        if (e.KeyCode != Keys.Enter || selection.Width < Minimum || selection.Height < Minimum) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string pending = file + ".new";
            File.WriteAllText(pending, string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2:F6},{3:F6}",
                (double)selection.X / Width, (double)selection.Y / Height,
                (double)selection.Width / Width, (double)selection.Height / Height));
            if (File.Exists(file)) File.Replace(pending, file, null);
            else File.Move(pending, file);
            Close();
        }
        catch (IOException ex) { MessageBox.Show(this, ex.Message, Text); }
    }
}
"@
[void][ChestCalibration]::SetProcessDPIAware()
$game = Get-Process FortniteClient-Win64-Shipping -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
$edges = [ChestCalibration+Edges]::new()
if (!$game -or ![ChestCalibration]::GetWindowRect($game.MainWindowHandle, [ref]$edges) -or $edges.Right -le $edges.Left -or $edges.Bottom -le $edges.Top) {
    [void][Windows.Forms.MessageBox]::Show('Открой Fortnite и попробуй ещё раз.','Калибровка Chest Island')
    exit 1
}
$bounds = [Drawing.Rectangle]::FromLTRB($edges.Left,$edges.Top,$edges.Right,$edges.Bottom)
$regionFile = Join-Path $env:APPDATA 'FortniteChestCounter/live-total.txt.region'
[void][ChestCalibration]::SetForegroundWindow($game.MainWindowHandle)
$form = [ChestCalibration]::new($bounds,$regionFile)
try { [Windows.Forms.Application]::Run($form) }
finally { $form.Dispose() }
