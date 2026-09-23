using Attendance_System.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Attendance_System.Views
{
    public partial class ShiftSettingsView : UserControl
    {
        public record TimeOption(TimeSpan Value)
        {
            public string Label => DateTime.Today.Add(Value).ToString("h:mm tt");

            // The themed ComboBox shows the selected item via ToString(); a record's
            // default ToString() prints "TimeOption { Value = ... }".
            public override string ToString() => Label;
        }

        private readonly List<TimeOption> _options =
            Enumerable.Range(0, 24 * 4).Select(i => new TimeOption(TimeSpan.FromMinutes(i * 15))).ToList();

        // Last values loaded from / saved to Supabase — used for dirty tracking and Discard.
        private (TimeSpan Start, TimeSpan Late, TimeSpan Absent, TimeSpan End) _saved;
        private bool _loading;

        public ShiftSettingsView()
        {
            InitializeComponent();
            CmbShiftStart.ItemsSource = _options;
            CmbLateCutoff.ItemsSource = _options;
            CmbAbsentCutoff.ItemsSource = _options;
            CmbShiftEnd.ItemsSource = _options;
            Loaded += async (s, e) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            SetStatus("Loading…", StatusKind.Info);
            try
            {
                var settings = await SupabaseService.Instance.GetShiftSettingsAsync();
                _saved = (ParseTime(settings.ShiftStart), ParseTime(settings.LateCutoff),
                          ParseTime(settings.AbsentCutoff), ParseTime(settings.ShiftEnd));
                ApplyToForm(_saved);
                SetStatus(settings.UpdatedAt == default ? "" : $"Last saved {settings.UpdatedAt.LocalDateTime:MMM d, yyyy h:mm tt}", StatusKind.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't load shift settings: {ex.Message}", StatusKind.Error);
            }
        }

        private void ApplyToForm((TimeSpan Start, TimeSpan Late, TimeSpan Absent, TimeSpan End) values)
        {
            _loading = true;
            CmbShiftStart.SelectedItem = OptionFor(values.Start);
            CmbLateCutoff.SelectedItem = OptionFor(values.Late);
            CmbAbsentCutoff.SelectedItem = OptionFor(values.Absent);
            CmbShiftEnd.SelectedItem = OptionFor(values.End);
            _loading = false;
            RefreshState();
        }

        // Values saved outside the 15-minute grid (e.g. edited in Supabase) still show up.
        private TimeOption OptionFor(TimeSpan value)
        {
            var match = _options.FirstOrDefault(o => o.Value == value);
            if (match != null) return match;

            match = new TimeOption(value);
            _options.Insert(_options.FindIndex(o => o.Value > value) is var i and >= 0 ? i : _options.Count, match);
            CmbShiftStart.Items.Refresh();
            CmbLateCutoff.Items.Refresh();
            CmbAbsentCutoff.Items.Refresh();
            CmbShiftEnd.Items.Refresh();
            return match;
        }

        private void Time_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading) RefreshState();
        }

        private (TimeSpan Start, TimeSpan Late, TimeSpan Absent, TimeSpan End)? CurrentValues()
        {
            if (CmbShiftStart.SelectedItem is TimeOption s &&
                CmbLateCutoff.SelectedItem is TimeOption l &&
                CmbAbsentCutoff.SelectedItem is TimeOption a &&
                CmbShiftEnd.SelectedItem is TimeOption e)
                return (s.Value, l.Value, a.Value, e.Value);
            return null;
        }

        private string? Validate((TimeSpan Start, TimeSpan Late, TimeSpan Absent, TimeSpan End) v)
        {
            if (v.Late < v.Start) return "\"Late after\" can't be earlier than the shift start.";
            if (v.Absent <= v.Late) return "\"Absent after\" must be later than \"Late after\".";
            if (v.End <= v.Absent) return "\"Shift end\" must be later than \"Absent after\".";
            return null;
        }

        private void RefreshState()
        {
            var current = CurrentValues();
            if (current is null)
            {
                BtnSave.IsEnabled = false;
                return;
            }

            var v = current.Value;
            var error = Validate(v);
            bool dirty = v != _saved;

            TxtSummary.Text = error ?? $"Mon-Fri, {Fmt(v.Start)} to {Fmt(v.End)}. A TIME-IN after {Fmt(v.Late)} is marked Late, " +
                                       $"and after {Fmt(v.Absent)} the day counts as Absent. A TIME-OUT past {Fmt(v.End)} earns " +
                                       $"overtime; earlier is undertime. Sat and Sun are rest days - all hours are overtime.";
            TxtSummary.Foreground = (Brush)FindResource(error is null ? "TextPrimaryBrush" : "DangerBrush");

            BtnSave.IsEnabled = dirty && error is null;
            BtnRevert.IsEnabled = dirty;
            if (dirty) SetStatus("Unsaved changes", StatusKind.Info);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentValues() is not { } v || Validate(v) is not null) return;

            BtnSave.IsEnabled = false;
            BtnRevert.IsEnabled = false;
            SetStatus("Saving…", StatusKind.Info);

            try
            {
                var saved = await SupabaseService.Instance.UpdateShiftSettingsAsync(v.Start, v.Late, v.Absent, v.End);
                _saved = v;
                RefreshState();
                SetStatus($"Saved. New taps use these times from now on ({saved.UpdatedAt.LocalDateTime:h:mm tt}).", StatusKind.Success);
            }
            catch (Exception ex)
            {
                RefreshState();
                SetStatus($"Couldn't save: {ex.Message}", StatusKind.Error);
            }
        }

        private void Revert_Click(object sender, RoutedEventArgs e)
        {
            ApplyToForm(_saved);
            SetStatus("Changes discarded.", StatusKind.Info);
        }

        private static TimeSpan ParseTime(string value) =>
            TimeSpan.TryParse(value, out var t) ? t : TimeSpan.Zero;

        private static string Fmt(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt");

        private enum StatusKind { Info, Success, Error }

        private void SetStatus(string message, StatusKind kind)
        {
            StatusText.Text = message;
            StatusText.Foreground = (Brush)FindResource(kind switch
            {
                StatusKind.Success => "SuccessBrush",
                StatusKind.Error => "DangerBrush",
                _ => "TextSecondaryBrush",
            });
        }
    }
}
