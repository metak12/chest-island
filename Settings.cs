using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

internal sealed partial class ChestIsland
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupName = "Chest Island";
    private bool autoStart, exiting;
    private bool? gameWasOpen;
    private Process automaticLive, automaticOverlay;
    private const string OpenEvent = @"Local\ChestIslandShowWindow";
    private System.Threading.EventWaitHandle openWindowEvent;
    private System.Threading.RegisteredWaitHandle openWindowWait;
    private Forms.NotifyIcon tray;

    private static void SignalOpenWindow()
    {
        try
        {
            using (var signal = System.Threading.EventWaitHandle.OpenExisting(OpenEvent))
                signal.Set();
        }
        catch (System.Threading.WaitHandleCannotBeOpenedException) { }
    }
    private System.Drawing.Icon trayIcon;

    private string StartupCommand
    {
        get { return "\"" + Path.Combine(folder, "Chest Island.exe") + "\" --tray"; }
    }

    private void InitializeSettings(bool startInTray)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(StartupKey))
            autoStart = key != null && string.Equals(key.GetValue(StartupName) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
        if (startInTray && autoStart) window.Opacity = 0;
        Find<Button>("SettingsButton").Click += (sender, args) => OpenSettings();
        using (var process = Process.GetCurrentProcess())
            trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(process.MainModule.FileName);
        tray = new Forms.NotifyIcon { Icon = trayIcon ?? System.Drawing.SystemIcons.Application, Text = "Chest Island" };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть Chest Island", null, (sender, args) => RestoreWindow());
        menu.Items.Add("Выход", null, (sender, args) => { exiting = true; window.Close(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (sender, args) => RestoreWindow();
        tray.Visible = autoStart;
        window.Closing += HideInTray;
        openWindowEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, OpenEvent);
        openWindowWait = System.Threading.ThreadPool.RegisterWaitForSingleObject(openWindowEvent, (state, timedOut) =>
        {
            window.Dispatcher.BeginInvoke(new Action(RestoreWindow));
        }, null, -1, false);
    }

    private void HideInTray(object sender, CancelEventArgs args)
    {
        if (autoStart && !exiting)
        {
            args.Cancel = true;
            window.Hide();
        }
    }

    private void RestoreWindow()
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void SetAutoStart(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(StartupKey))
        {
            if (enabled) key.SetValue(StartupName, StartupCommand, RegistryValueKind.String);
            else if (string.Equals(key.GetValue(StartupName) as string, StartupCommand, StringComparison.OrdinalIgnoreCase))
                key.DeleteValue(StartupName, false);
        }
        if (autoStart == enabled) return;
        autoStart = enabled;
        tray.Visible = enabled;
        gameWasOpen = null;
        if (!enabled) StopAutomatic();
    }

    private void SyncAuto(bool gameOpen, bool live, bool overlay)
    {
        if (!autoStart || gameWasOpen == gameOpen) return;
        gameWasOpen = gameOpen;
        if (!gameOpen) { StopAutomatic(); return; }
        try
        {
            if (!live) automaticLive = Start("Live.ps1", false);
            if (!overlay) automaticOverlay = Start("Overlay.ps1", false);
        }
        catch (Exception ex) { Say("Автозапуск Fortnite: " + ex.Message); }
    }

    private static void StopOwned(ref Process worker)
    {
        if (worker == null) return;
        try { if (!worker.HasExited) worker.Kill(); }
        catch (InvalidOperationException) { }
        finally { worker.Dispose(); worker = null; }
    }

    private void StopAutomatic()
    {
        StopOwned(ref automaticLive);
        StopOwned(ref automaticOverlay);
    }

    private void DisposeSettings()
    {
        StopAutomatic();
        openWindowWait.Unregister(null);
        openWindowEvent.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip.Dispose();
        tray.Dispose();
        if (trayIcon != null) trayIcon.Dispose();
    }

    private void ReadOverlaySettings(out int size, out int opacity, out string mode)
    {
        size = 1;
        opacity = 89;
        mode = "goal";
        try
        {
            var parts = File.ReadAllText(Path.Combine(data, "overlay-settings.txt")).Trim().Split(',');
            int parsedSize, parsedOpacity;
            if (parts.Length != 3 || !int.TryParse(parts[0], out parsedSize) || !int.TryParse(parts[1], out parsedOpacity)) return;
            if (parsedSize >= 0 && parsedSize <= 2) size = parsedSize;
            if (parsedOpacity >= 30 && parsedOpacity <= 100) opacity = parsedOpacity;
            if (parts[2] == "total" || parts[2] == "today") mode = parts[2];
        }
        catch (IOException) { }
    }

    private long CountToday()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        long count = 0;
        try
        {
            using (var stream = new FileStream(Path.Combine(data, "live-total.txt.history"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null) if (line == today) count++;
            }
        }
        catch (IOException) { }
        return count;
    }

    private void OpenSettings()
    {
        int savedSize, savedOpacity;
        string savedMode;
        ReadOverlaySettings(out savedSize, out savedOpacity, out savedMode);
        var sizes = new[] { "Маленький", "Средний", "Большой" };
        var modes = new[] { "Общий счёт", "Прогресс цели", "Сегодня" };
        var modeKeys = new[] { "total", "goal", "today" };
        var dialog = new Window
        {
            Title = "Настройки Chest Island", Owner = window, Width = 440,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = window.Background,
            Foreground = window.Foreground, ShowInTaskbar = false
        };
        var panel = new StackPanel { Margin = new Thickness(20), Resources = window.Resources };
        panel.Children.Add(new TextBlock { Text = "Настройки", FontSize = 24, FontWeight = FontWeights.SemiBold });
        var startup = new CheckBox
        {
            Content = "Запускать с Windows и ждать Fortnite в трее",
            Foreground = window.Foreground, IsChecked = autoStart, Margin = new Thickness(0, 12, 0, 0)
        };
        panel.Children.Add(startup);
        panel.Children.Add(new TextBlock
        {
            Text = "При запуске Fortnite автоматически включаются распознавание и оверлей.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 5, 0, 0)
        });
        panel.Children.Add(new TextBlock { Text = "Размер", Margin = new Thickness(0, 13, 0, 4) });
        var size = new ComboBox { ItemsSource = sizes, SelectedIndex = savedSize, Height = 27 };
        panel.Children.Add(size);
        panel.Children.Add(new TextBlock { Text = "Режим", Margin = new Thickness(0, 10, 0, 4) });
        var mode = new ComboBox { ItemsSource = modes, SelectedIndex = Array.IndexOf(modeKeys, savedMode), Height = 27 };
        panel.Children.Add(mode);
        var opacityLabel = new TextBlock { Margin = new Thickness(0, 10, 0, 4) };
        panel.Children.Add(opacityLabel);
        var opacity = new Slider { Minimum = 30, Maximum = 100, Value = savedOpacity, TickFrequency = 1, IsSnapToTickEnabled = true };
        panel.Children.Add(opacity);
        var previewText = new TextBlock { TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
        var preview = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(23, 25, 32)), Child = previewText,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 13, 0, 6), CornerRadius = new CornerRadius(6)
        };
        panel.Children.Add(preview);
        long today = CountToday();
        Action previewChanged = () =>
        {
            int selectedSize = Math.Max(0, size.SelectedIndex);
            preview.Width = new[] { 172, 218, 280 }[selectedSize];
            preview.Height = new[] { 32, 42, 54 }[selectedSize];
            previewText.FontSize = new[] { 16, 20, 25 }[selectedSize];
            preview.Opacity = opacity.Value / 100;
            opacityLabel.Text = "Непрозрачность: " + (int)opacity.Value + "%";
            int total = previous.GetValueOrDefault();
            if (mode.SelectedIndex == 2) previewText.Text = "Сегодня: " + today;
            else if (mode.SelectedIndex == 1 && goal > 0) previewText.Text = total + " / " + goal;
            else previewText.Text = "Сундуки: " + total;
        };
        size.SelectionChanged += (s, e) => previewChanged();
        mode.SelectionChanged += (s, e) => previewChanged();
        opacity.ValueChanged += (s, e) => previewChanged();
        previewChanged();
        var error = new TextBlock { Foreground = Brushes.LightPink, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Сохранить", IsDefault = true };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        panel.Children.Add(actions);
        save.Click += (s, e) =>
        {
            try
            {
                Directory.CreateDirectory(data);
                string path = Path.Combine(data, "overlay-settings.txt");
                string pending = path + ".new";
                File.WriteAllText(pending, size.SelectedIndex + "," + (int)opacity.Value + "," + modeKeys[mode.SelectedIndex]);
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
                SetAutoStart(startup.IsChecked == true);
                dialog.DialogResult = true;
                Refresh();
                Say("Настройки сохранены.");
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        dialog.Content = panel;
        dialog.ShowDialog();
    }
}
