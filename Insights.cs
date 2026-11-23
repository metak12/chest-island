using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal sealed partial class ChestIsland
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private void ShowDiagnostics()
    {
        var dialog = new Window
        {
            Title = "Диагностика распознавания", Owner = window, Width = 510, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = window.Background, Foreground = window.Foreground, ShowInTaskbar = false
        };
        UseDarkTitle(dialog);
        var content = new StackPanel { Margin = new Thickness(20), Resources = window.Resources };
        content.Children.Add(new TextBlock { Text = "Диагностика", FontSize = 24, FontWeight = FontWeights.SemiBold });
        var running = new TextBlock { Foreground = Brushes.LightGray, Margin = new Thickness(0, 6, 0, 12) };
        content.Children.Add(running);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) };
        content.Children.Add(status);
        var when = new TextBlock { Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 8) };
        content.Children.Add(when);
        var message = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72, MaxHeight = 145,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10), Background = new SolidColorBrush(Color.FromRgb(33, 33, 39)),
            Foreground = window.Foreground, BorderBrush = new SolidColorBrush(Color.FromRgb(61, 58, 72))
        };
        AutomationProperties.SetName(message, "Последний текст уведомления");
        content.Children.Add(message);
        content.Children.Add(new TextBlock
        {
            Text = "Показывается последнее распознанное уведомление. Если игра не показала уведомление или текст не прочитался, здесь может остаться предыдущая запись.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 10)
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var calibrate = new Button { Content = "Настроить область", Margin = new Thickness(0, 0, 8, 0) };
        calibrate.Click += (s, e) => { dialog.Close(); OpenCalibration(); };
        actions.Children.Add(calibrate);
        actions.Children.Add(new Button { Content = "Закрыть", IsCancel = true });
        content.Children.Add(actions);
        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        Action refresh = () =>
        {
            running.Text = IsLive() ? (IsSpectatorPaused() ? "Пауза: режим наблюдения" : "Распознавание включено") : "Распознавание остановлено";
            string[] fields;
            try { fields = File.ReadAllLines(Path.Combine(data, "live-total.txt.diagnostic")); }
            catch (IOException) { fields = new string[0]; }
            if (fields.Length < 3)
            {
                status.Text = "Пока нет уведомлений.";
                when.Text = "";
                message.Text = "";
                return;
            }
            switch (fields[1])
            {
                case "counted": status.Text = "Засчитано: найдено слово «СУНДУК»."; break;
                case "spectating": status.Text = "Пропущено: открыт режим наблюдения."; break;
                case "duplicate": status.Text = "Пропущено: то же уведомление ещё на экране."; break;
                default: status.Text = "Пропущено: в прочитанном тексте нет слова «СУНДУК»."; break;
            }
            DateTimeOffset time;
            when.Text = DateTimeOffset.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
                ? time.LocalDateTime.ToString("d MMMM, HH:mm:ss", Russian) : "";
            if (message.Text != fields[2]) message.Text = fields[2];
        };
        tick.Tick += (s, e) => refresh();
        dialog.Closed += (s, e) => tick.Stop();
        refresh();
        tick.Start();
        dialog.Content = content;
        dialog.ShowDialog();
    }

    private bool IsSpectatorPaused()
    {
        return File.Exists(Path.Combine(data, "live-total.txt.spectator"));
    }

    private void OpenCalibration()
    {
        try
        {
            window.WindowState = WindowState.Minimized;
            var editor = Start("Calibration.ps1", true);
            editor.Exited += (sender, args) =>
            {
                int code = editor.ExitCode;
                editor.Dispose();
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    RestoreWindow();
                    Say(code == 0 ? "Калибровка завершена." : "Не удалось открыть калибровку. Проверь, что Fortnite запущен.");
                }));
            };
            editor.EnableRaisingEvents = true;
        }
        catch (Exception ex) { RestoreWindow(); Say(ex.Message); }
    }

    private DateTime rateChecked;
    private double cachedChestsPerHour;

    private double RecentRate()
    {
        var now = DateTime.Now;
        if (now - rateChecked < TimeSpan.FromSeconds(20)) return cachedChestsPerHour;
        double seconds = 0, count = 0;
        foreach (var session in ReadSessions())
        {
            if (session.Start.Date < now.Date.AddDays(-6) || session.Start.Date > now.Date || session.Seconds <= 0) continue;
            seconds += session.Seconds;
            count += session.Count;
        }
        rateChecked = now;
        cachedChestsPerHour = seconds >= 60 && count > 0 ? count * 3600 / seconds : 0;
        return cachedChestsPerHour;
    }

    private string GoalForecast(int total, int target)
    {
        int remaining = Math.Max(0, target - total);
        string left = string.Format(Russian, "Осталось: {0:N0}", remaining);
        double rate = RecentRate();
        if (rate <= 0 || remaining == 0) return left;
        long minutes = Math.Max(1, (long)Math.Ceiling(remaining * 60.0 / rate));
        string time = minutes < 60 ? minutes + " мин" : (minutes / 60) + " ч " + (minutes % 60) + " мин";
        return left + " · ≈ " + time + " в игре";
    }

    private UIElement BuildProgress(SortedDictionary<DateTime, long> days)
    {
        DateTime today = DateTime.Today;
        DateTime first = today.AddDays(-6);
        var week = new long[7];
        long sum = 0, peak = 0;
        for (int i = 0; i < week.Length; i++)
        {
            days.TryGetValue(first.AddDays(i), out week[i]);
            sum += week[i];
            peak = Math.Max(peak, week[i]);
        }

        long best = 0;
        double seconds = 0, chests = 0;
        foreach (var session in ReadSessions())
        {
            best = Math.Max(best, session.Count);
            if (session.Start.Date < first || session.Start.Date > today || session.Seconds <= 0) continue;
            seconds += session.Seconds;
            chests += session.Count;
        }

        var body = new StackPanel { Margin = new Thickness(10, 9, 10, 9) };
        body.Children.Add(new TextBlock
        {
            Text = string.Format(Russian, "За 7 дней: {0:N0}", sum), Foreground = window.Foreground, FontSize = 17, FontWeight = FontWeights.SemiBold
        });
        var chart = new Grid { Height = 142, Margin = new Thickness(0, 7, 0, 8) };
        for (int i = 0; i < week.Length; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            DateTime day = first.AddDays(i);
            var column = new Grid { Margin = new Thickness(4, 0, 4, 0) };
            column.RowDefinitions.Add(new RowDefinition());
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var bar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(176, 139, 238)),
                CornerRadius = new CornerRadius(4, 4, 0, 0), VerticalAlignment = VerticalAlignment.Bottom,
                Width = 22, Height = peak == 0 || week[i] == 0 ? 0 : Math.Max(3, 96.0 * week[i] / peak)
            };
            column.Children.Add(bar);
            var value = new TextBlock
            {
                Text = week[i].ToString(Russian), TextAlignment = TextAlignment.Center, Foreground = window.Foreground,
                FontSize = 11, Margin = new Thickness(0, 3, 0, 0)
            };
            Grid.SetRow(value, 1);
            column.Children.Add(value);
            var date = new TextBlock
            {
                Text = day.ToString("dd.MM", Russian), TextAlignment = TextAlignment.Center, Foreground = Brushes.LightGray, FontSize = 10
            };
            Grid.SetRow(date, 2);
            column.Children.Add(date);
            column.ToolTip = string.Format(Russian, "{0:d MMMM}: {1:N0} сундуков", day, week[i]);
            AutomationProperties.SetName(column, (string)column.ToolTip);
            Grid.SetColumn(column, i);
            chart.Children.Add(column);
        }
        body.Children.Add(chart);
        body.Children.Add(new TextBlock { Text = string.Format(Russian, "Лучший заход: {0:N0}", best), Foreground = window.Foreground });
        string rate = seconds > 0 ? (chests * 3600 / seconds).ToString("N1", Russian) + " сундуков/ч" : "нет данных";
        body.Children.Add(new TextBlock { Text = "Средний темп за 7 дней: " + rate, Foreground = window.Foreground, Margin = new Thickness(0, 5, 0, 0) });
        return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
