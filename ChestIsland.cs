using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

internal sealed partial class ChestIsland
{
    private readonly string folder = AppDomain.CurrentDomain.BaseDirectory;
    private readonly string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FortniteChestCounter");
    private readonly Window window;
    private readonly DispatcherTimer timer;
    private long? initial;
    private int? previous;
    private int goal;
    private Process editor;
    private ReleaseUpdate update;

    [STAThread]
    private static void Main(string[] args)
    {
        bool first;
        using (var instance = new Mutex(true, @"Local\ChestIslandDashboard", out first))
        {
            if (!first)
            {
                SignalOpenWindow();
                return;
            }
            try { new Application().Run(new ChestIsland(args).window); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Chest Island", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    private ChestIsland(string[] args)
    {
        bool startInTray = Array.IndexOf(args, "--tray") >= 0;
        using (var layout = File.OpenRead(Path.Combine(folder, "ChestIsland.xaml")))
            window = (Window)XamlReader.Load(layout);
        using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName))
            if (icon != null) window.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        Find<Image>("BrandIcon").Source = window.Icon;
        Find<TextBlock>("Version").Text = "Версия " + ReleaseUpdate.VersionText;
        UseDarkTitle(window);
        Find<Button>("LiveToggle").Click += async (s, e) => await Toggle("Live.ps1", IsLive);
        Find<Button>("OverlayToggle").Click += async (s, e) => await Toggle("Overlay.ps1", IsOverlay);
        InitializeSettings(startInTray);
        goal = ReadGoal();
        Find<Button>("EditCount").Click += (s, e) => EditCount(false);
        Find<Button>("EditGoal").Click += (s, e) => EditCount(true);
        Find<Button>("History").Click += (s, e) => ShowHistory();
        Find<Button>("DiagnosticsButton").Click += (s, e) => ShowDiagnostics();
        Find<Button>("MoveOverlay").Click += (s, e) => Move();
        Find<Button>("UpdateButton").Click += async (s, e) => await UpdateClick();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (s, e) => Refresh();
        window.Loaded += async (s, e) =>
        {
            if (startInTray && autoStart) window.Hide();
            window.Opacity = 1;
            if (window.IsVisible && SystemParameters.ClientAreaAnimation)
                Find<Grid>("Dashboard").BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
            timer.Start();
            await CheckForUpdates(false);
            if (exiting || update != null) return;
            if (Array.IndexOf(args, "--resume-live") >= 0 && !IsLive())
                using (Start("Live.ps1", false)) { }
            if (Array.IndexOf(args, "--resume-overlay") >= 0 && !IsOverlay())
                using (Start("Overlay.ps1", false)) { }
        };
        window.Closed += (s, e) => { timer.Stop(); DisposeSettings(); };
        Refresh();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    private static void UseDarkTitle(Window target)
    {
        target.SourceInitialized += (sender, args) =>
        {
            int dark = 1;
            var handle = new System.Windows.Interop.WindowInteropHelper(target).Handle;
            if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        };
    }

    private T Find<T>(string name) where T : class { return (T)window.FindName(name); }
    private void Say(string message) { Find<TextBlock>("Feedback").Text = message; }

    private bool IsLive()
    {
        string path = Path.Combine(data, "live-total.txt.lock");
        if (!File.Exists(path)) return false;
        try { using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return false; }
        catch (IOException ex) { return (ex.HResult & 0xffff) == 32 || (ex.HResult & 0xffff) == 33; }
    }

    private static bool IsOverlay()
    {
        Mutex mutex;
        if (!Mutex.TryOpenExisting(@"Local\FortniteChestCounterOverlay", out mutex)) return false;
        mutex.Dispose();
        return true;
    }

    private void Refresh()
    {
        bool live = IsLive(), overlay = IsOverlay();
        var game = Process.GetProcessesByName("FortniteClient-Win64-Shipping");
        Find<TextBlock>("Game").Text = game.Length > 0 ? "●  Fortnite открыт" : "○  Fortnite закрыт";
        foreach (var process in game) process.Dispose();
        SyncAuto(game.Length > 0, live, overlay);
        Find<TextBlock>("LiveStatus").Text = live ? (IsSpectatorPaused() ? "Пауза · наблюдение" : "Включено") : "Остановлено";
        Find<Button>("SwitchOverlayProfile").ToolTip = "Активен профиль " + ReadOverlayProfile() + ". Нажми, чтобы переключить.";
        Find<TextBlock>("OverlayStatus").Text = overlay ? "Включён · виден в Fortnite" : "Скрыт";
        Find<Button>("LiveToggle").Content = live ? "Остановить" : "Запустить";
        Find<Button>("OverlayToggle").Content = overlay ? "Скрыть" : "Показать";
        Find<Button>("MoveOverlay").IsEnabled = editor == null || editor.HasExited;

        int value;
        ShowGoal(previous.GetValueOrDefault());
        try { if (!int.TryParse(File.ReadAllText(Path.Combine(data, "live-total.txt")), out value)) return; }
        catch (IOException) { return; }
        if (!initial.HasValue) initial = value;
        ShowGoal(value);
        if (previous == value) return;
        var count = Find<TextBlock>("Count");
        count.Text = value.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
        Find<TextBlock>("Session").Text = string.Format("За время работы приложения: {0:+0;-0;0}", value - initial.Value);
        if (previous.HasValue && SystemParameters.ClientAreaAnimation)
        {
            var scale = (ScaleTransform)count.RenderTransform;
            var animation = new DoubleAnimation(1.12, 1, TimeSpan.FromMilliseconds(290))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
        previous = value;
    }

    private async System.Threading.Tasks.Task Toggle(string script, Func<bool> active)
    {
        try
        {
            if (active())
            {
                if (Stop(script)) Say("Остановлено.");
                else Say("Закрой прежнее окно запуска, чтобы остановить этот процесс.");
                Refresh();
                return;
            }
            var button = Find<Button>(script == "Live.ps1" ? "LiveToggle" : "OverlayToggle");
            button.IsEnabled = false;
            try
            {
                if (!await AllowStart(false) || active()) { button.IsEnabled = true; return; }
            }
            catch { button.IsEnabled = true; throw; }
            Process process;
            try { process = Start(script, false); }
            catch { button.IsEnabled = true; throw; }
            Say("Запускаю…");
            var ready = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            ready.Tick += (s, e) =>
            {
                ready.Stop();
                button.IsEnabled = true;
                if (Find<TextBlock>("Feedback").Text == "Запускаю…")
                    Say(active() ? "Готово. Вернись в Fortnite." : "Запуск не подтвердился. Проверь окно запуска.");
                process.Dispose();
                Refresh();
            };
            ready.Start();
        }
        catch (Exception ex) { Say(ex.Message); }
    }

    private void EditCount(bool editingGoal)
    {
        var dialog = new Window
        {
            Title = editingGoal ? "Личная цель" : "Изменить счёт", Owner = window, Width = 360, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = window.Background, Foreground = window.Foreground, ShowInTaskbar = false
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Resources = window.Resources;
        panel.Children.Add(new TextBlock { Text = editingGoal ? "Сколько сундуков в цели?" : "Сколько сундуков уже открыто?", FontSize = 17 });
        var entry = new TextBox
        {
            Text = (editingGoal ? goal : previous.GetValueOrDefault()).ToString(CultureInfo.InvariantCulture),
            MaxLength = 10, FontSize = 22, Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(39, 34, 52)),
            Foreground = window.Foreground
        };
        panel.Children.Add(entry);
        if (editingGoal) panel.Children.Add(new TextBlock { Text = "0 скрывает цель.", Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.LightGray });
        var error = new TextBlock { Foreground = Brushes.LightPink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 9, 0, 0) };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Сохранить", IsDefault = true };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (s, e) => { entry.Focus(); entry.SelectAll(); };
        save.Click += (s, e) =>
        {
            int value;
            if (!int.TryParse(entry.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                error.Text = "Введи целое число от 0 до 2 147 483 647.";
                entry.Focus();
                return;
            }
            try
            {
                if (editingGoal)
                {
                    SaveGoal(value);
                    goal = value;
                }
                else
                {
                    int before = SaveCount(value);
                    if (initial.HasValue) initial += value - before;
                }
                dialog.DialogResult = true;
                Refresh();
                Say(editingGoal ? "Цель сохранена." : "Счёт сохранён.");
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        UseDarkTitle(dialog);
        dialog.ShowDialog();
    }

    private int ReadGoal()
    {
        try
        {
            int value;
            return int.TryParse(File.ReadAllText(Path.Combine(data, "goal.txt")), out value) && value > 0 ? value : 0;
        }
        catch (IOException) { return 0; }
    }

    private void SaveGoal(int value)
    {
        Directory.CreateDirectory(data);
        string path = Path.Combine(data, "goal.txt");
        string temp = path + ".new";
        File.WriteAllText(temp, value.ToString(CultureInfo.InvariantCulture));
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }

    private void ShowGoal(int total)
    {
        var label = Find<TextBlock>("GoalLabel");
        var bar = Find<ProgressBar>("GoalProgress");
        var forecast = Find<TextBlock>("GoalForecast");
        Find<Button>("EditGoal").Content = goal > 0 ? "Изменить цель" : "Задать цель";
        if (goal <= 0)
        {
            label.Text = "Цель не задана";
            bar.Visibility = Visibility.Collapsed;
            forecast.Visibility = Visibility.Collapsed;
            return;
        }
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        label.Text = string.Format(culture, total >= goal ? "Цель достигнута  ·  {0:N0} / {1:N0}" : "Цель  ·  {0:N0} / {1:N0}", total, goal);
        bar.Value = Math.Max(0, Math.Min(1, (double)total / goal));
        bar.Visibility = Visibility.Visible;
        forecast.Text = GoalForecast(total, goal);
        forecast.Visibility = total < goal ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowHistory()
    {
        var days = new System.Collections.Generic.SortedDictionary<DateTime, long>();
        try
        {
            using (var stream = new FileStream(Path.Combine(data, "live-total.txt.history"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    DateTime day;
                    if (!DateTime.TryParseExact(line, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) continue;
                    long count;
                    days.TryGetValue(day, out count);
                    days[day] = count + 1;
                }
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException ex) { Say("Не удалось открыть историю. " + ex.Message); return; }

        var dialog = new Window
        {
            Title = "История сундуков", Owner = window, Width = 510, Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = window.Background, Foreground = window.Foreground, ShowInTaskbar = false
        };
        var panel = new DockPanel { Margin = new Thickness(20), Resources = window.Resources };
        var summary = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(summary, Dock.Top);
        summary.Children.Add(new TextBlock { Text = "История сундуков", FontSize = 24, FontWeight = FontWeights.SemiBold });
        long today;
        days.TryGetValue(DateTime.Today, out today);
        summary.Children.Add(new TextBlock { Text = "Сегодня: " + today, Margin = new Thickness(0, 5, 0, 0) });
        summary.Children.Add(new TextBlock
        {
            Text = "Здесь только автоматические срабатывания с версии 1.1.4.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 5, 0, 0)
        });
        panel.Children.Add(summary);
        var close = new Button { Content = "Закрыть", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
        var list = new ListBox { Background = new SolidColorBrush(Color.FromRgb(33, 29, 45)), Foreground = window.Foreground, BorderThickness = new Thickness(0) };
        var rows = new System.Collections.Generic.List<DateTime>(days.Keys);
        rows.Reverse();
        foreach (var day in rows)
            list.Items.Add(string.Format(CultureInfo.GetCultureInfo("ru-RU"), "{0:d MMMM yyyy}    ·    {1:N0}", day, days[day]));
        if (rows.Count == 0) list.Items.Add("Пока нет записей.");
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "По дням", Content = list });
        tabs.Items.Add(new TabItem { Header = "Сессии", Content = SessionHistory() });
        tabs.Items.Add(new TabItem { Header = "Прогресс", Content = BuildProgress(days) });
        panel.Children.Add(tabs);
        dialog.Content = panel;
        UseDarkTitle(dialog);
        dialog.ShowDialog();
    }

    private sealed class GameSession
    {
        public DateTime Start;
        public long Seconds;
        public long Count;
    }

    private System.Collections.Generic.List<GameSession> ReadSessions()
    {
        var sessions = new System.Collections.Generic.List<GameSession>();
        string directory = Path.Combine(data, "live-total.txt.sessions");
        if (!Directory.Exists(directory)) return sessions;
        foreach (string path in Directory.EnumerateFiles(directory, "*.txt"))
        {
            try
            {
                var fields = File.ReadAllText(path).Trim().Split(',');
                long ticks, seconds, count;
                if (fields.Length != 3 || !long.TryParse(fields[0], out ticks) || ticks <= 0 || ticks > DateTime.MaxValue.Ticks ||
                    !long.TryParse(fields[1], out seconds) || seconds < 0 || !long.TryParse(fields[2], out count) || count < 0)
                    continue;
                sessions.Add(new GameSession { Start = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime(), Seconds = seconds, Count = count });
            }
            catch (IOException) { }
        }
        sessions.Sort((a, b) => b.Start.CompareTo(a.Start));
        return sessions;
    }

    private UIElement SessionHistory()
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        var sessions = ReadSessions();
        var contents = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(33, 29, 45)) };
        var description = new TextBlock
        {
            Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 10, 10, 6),
            Text = "Один запуск Fortnite — одна сессия. Время учитывается, пока включено распознавание. История сессий ведётся с версии 1.1.6."
        };
        DockPanel.SetDock(description, Dock.Top);
        contents.Children.Add(description);
        var list = new ListBox
        {
            Background = Brushes.Transparent, Foreground = window.Foreground, BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 0, 8, 8)
        };
        contents.Children.Add(list);
        if (sessions.Count == 0)
        {
            list.Items.Add("Пока нет игровых сессий.");
            return contents;
        }
        foreach (var session in sessions)
        {
            string duration = string.Format(culture, "{0} ч {1:00} мин", session.Seconds / 3600, session.Seconds / 60 % 60);
            if (session.Seconds < 60) duration = string.Format(culture, "{0} сек", session.Seconds);
            string rate = session.Seconds >= 60 ? (session.Count * 3600.0 / session.Seconds).ToString("N1", culture) + " / ч" : "— / ч";
            var row = new StackPanel { Margin = new Thickness(3, 6, 3, 6) };
            row.Children.Add(new TextBlock
            {
                Text = session.Start.ToString("d MMMM yyyy · HH:mm", culture), FontWeight = FontWeights.SemiBold
            });
            row.Children.Add(new TextBlock
            {
                Text = string.Format(culture, "Сундуки: {0:N0}   ·   {1}   ·   {2}", session.Count, duration, rate),
                Foreground = Brushes.LightGray, Margin = new Thickness(0, 3, 0, 0)
            });
            list.Items.Add(row);
        }
        return contents;
    }

    private int SaveCount(int value)
    {
        string path = Path.Combine(data, "live-total.txt");
        Directory.CreateDirectory(data);
        using (var writeLock = new Mutex(false, @"Local\FortniteChestTotalWrite"))
        {
            try
            {
                if (!writeLock.WaitOne(TimeSpan.FromSeconds(2)))
                    throw new IOException("Счётчик занят. Нажми «Сохранить» ещё раз.");
            }
            catch (AbandonedMutexException) { }
            string temp = path + ".manual";
            try
            {
                bool exists = File.Exists(path);
                int before = exists ? int.Parse(File.ReadAllText(path), CultureInfo.InvariantCulture) : 0;
                File.WriteAllText(temp, value.ToString(CultureInfo.InvariantCulture));
                if (exists) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                return before;
            }
            finally
            {
                writeLock.ReleaseMutex();
            }
        }
    }

    private void Move()
    {
        try
        {
            if (editor != null) editor.Dispose();
            editor = Start("Overlay.ps1", true);
            Say("Перетащи окно настройки и нажми «Сохранить позицию».");
            Refresh();
        }
        catch (Exception ex) { Say(ex.Message); }
    }

    private Process Start(string script, bool move)
    {
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        string args = "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + Path.Combine(folder, script) + "\"" + (move && script == "Overlay.ps1" ? " -Move" : "");
        if (move)
            return Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = folder, WindowStyle = ProcessWindowStyle.Normal });
        return Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true });
    }

    private bool Stop(string script)
    {
        var pids = new System.Collections.Generic.List<int>();
        string pattern = @"(?i)-File\s+""?" + Regex.Escape(Path.Combine(folder, script)) + @"""?\s*$";
        using (var search = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='powershell.exe'"))
        using (var rows = search.Get())
            foreach (ManagementObject row in rows)
                using (row)
                    if (Regex.IsMatch(Convert.ToString(row["CommandLine"]), pattern))
                        pids.Add(Convert.ToInt32(row["ProcessId"]));
        if (pids.Count != 1) return false;
        try { using (var worker = Process.GetProcessById(pids[0])) worker.Kill(); }
        catch (ArgumentException) { }
        return true;
    }
}
