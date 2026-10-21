using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using BorderlessFlatWindow.Application.Discovery;
using BorderlessFlatWindow.Application.Overview;
using BorderlessFlatWindow.Core.Canvas;
using BorderlessFlatWindow.Core.Geometry;

namespace BorderlessFlatWindow.Presentation;

/// <summary>Opt-in native-surface experiment. No desktop readback, production presenter or global input hooks.</summary>
public sealed class LiveOverviewLabSurface : Window
{
    public sealed record Source(NativeWindowIdentity Identity, RectD WorldBounds, PixelRect ScreenBounds);
    public sealed record Result(double ShowMs, double FirstCallbackMs, int Callbacks, int Updates, int Failures,
        int Created, int Released, int Reversals, bool TimedOut, double GapP95Ms, double GapMaxMs, int TextChanges,
        int LateCallbacks, bool RenderSubscriptionReleased, bool IdleVerified, bool ClosedDuringMotion);

    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register("Progress", typeof(double),
        typeof(LiveOverviewLabSurface), new PropertyMetadata(0.0));
    private readonly Grid scene = new();
    private readonly Canvas outlines = new() { IsHitTestVisible = false };
    private readonly List<(IOverviewPreview Preview, PixelRect From, PixelRect To, bool Focus)> previews = [];
    private readonly Dictionary<IOverviewPreview, (PixelRect Bounds, byte Opacity)> lastUpdates = [];
    private readonly List<double> gaps = [];
    private readonly Stopwatch clock = new();
    private readonly Source[] sources;
    private readonly PixelRect bounds;
    private readonly Func<nint, IOverviewPreviewPort> previewFactory;
    private readonly Func<nint, bool, bool> configure;
    private readonly bool interactive;
    private readonly bool closeDuringMotion;
    private readonly DispatcherTimer watchdog;
    private readonly DispatcherTimer idleCheck;
    private readonly Border footer;
    private readonly TextBox input;
    private readonly TextBlock status;
    private double previous, firstCallback, showMs;
    private int callbacks, updates, failures, created, released, reversals, phase, textChanges, lateCallbacks;
    private bool closed, targetOpen = true, subscribed, timedOut, sequenceFirst;
    private bool idleVerified;
    private bool closedDuringMotion;
    private int idleCallbacks, idleUpdates;

    private LiveOverviewLabSurface(PixelRect bounds, Source[] sources, Func<nint, IOverviewPreviewPort> previewFactory,
        Func<nint, bool, bool> configure, bool interactive, bool closeDuringMotion)
    {
        this.bounds = bounds; this.sources = sources; this.previewFactory = previewFactory; this.configure = configure; this.interactive = interactive;
        this.closeDuringMotion = closeDuringMotion;
        Title = "BFW live overview surface lab"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent; AllowsTransparency = false; ShowActivated = interactive; ShowInTaskbar = interactive; Topmost = true;
        Width = bounds.Width; Height = bounds.Height; Content = scene;
        scene.Children.Add(outlines);
        footer = new Border { CornerRadius = new CornerRadius(24), Background = new SolidColorBrush(Color.FromArgb(235, 240, 240, 252)),
            BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Padding = new Thickness(24, 18, 24, 18),
            Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Bottom };
        var commands = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        status = new TextBlock { Text = "Native live surface · experiment", VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(35, 40, 60)), Margin = new Thickness(0, 0, 20, 0) };
        input = new TextBox { Width = 170, Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Text = "Input check" };
        input.TextChanged += (_, _) => textChanges++;
        var reverse = new Button { Content = "Reverse · Tab", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(12, 0, 0, 0) };
        reverse.Click += (_, _) => Retarget(!targetOpen);
        var close = new Button { Content = "Close · Esc", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(12, 0, 0, 0) };
        close.Click += (_, _) => Close();
        commands.Children.Add(status); commands.Children.Add(input); commands.Children.Add(reverse); commands.Children.Add(close);
        footer.Child = commands; scene.Children.Add(footer);
        watchdog = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(interactive ? 180 : 8) };
        watchdog.Tick += Watchdog;
        idleCheck = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
        idleCheck.Tick += VerifyIdle;
        SourceInitialized += InitializeSource;
        ContentRendered += Start;
        PreviewKeyDown += Key;
        Closed += Release;
    }

    public static Result Run(PixelRect bounds, Source[] sources, Func<nint, IOverviewPreviewPort> previewFactory,
        Func<nint, bool, bool> configure, bool interactive = false, bool closeDuringMotion = false)
    {
        if (!bounds.IsValid || sources.Length == 0) throw new ArgumentException("The lab needs a valid viewport and owned sources.");
        var window = new LiveOverviewLabSurface(bounds, sources, previewFactory, configure, interactive, closeDuringMotion);
        var frame = new DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;
        // WinForms starts this modeless WPF window. Enable the documented interop keyboard filter.
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(window);
        try
        {
            window.clock.Start();
            var started = Stopwatch.GetTimestamp(); window.Show(); window.showMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (!window.closed) Dispatcher.PushFrame(frame);
        }
        finally { if (!window.closed) window.Close(); }
        var count = window.callbacks;
        var drain = new DispatcherFrame();
        // Let deferred WPF/native cleanup run while messages keep flowing; this is outside show/motion timings.
        var drainTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(80) };
        EventHandler finishDrain = (_, _) => { drainTimer.Stop(); drain.Continue = false; };
        drainTimer.Tick += finishDrain;
        try { drainTimer.Start(); Dispatcher.PushFrame(drain); }
        finally { drainTimer.Stop(); drainTimer.Tick -= finishDrain; }
        var sorted = window.gaps.OrderBy(value => value).ToArray();
        return new(window.showMs, window.firstCallback, window.callbacks, window.updates, window.failures, window.created,
            window.released, window.reversals, window.timedOut, sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Length * .95) - 1],
            sorted.Length == 0 ? 0 : sorted[^1], window.textChanges, window.lateCallbacks + window.callbacks - count, !window.subscribed, window.idleVerified,
            window.closedDuringMotion);
    }

    private void InitializeSource(object? sender, EventArgs args)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var nativeSource = HwndSource.FromHwnd(handle);
        nativeSource.CompositionTarget.BackgroundColor = Colors.Transparent;
        // High contrast and explicitly unsupported configurations always get a readable opaque surface.
        var accepted = configure(handle, !SystemParameters.HighContrast);
        if (!accepted) scene.Background = new SolidColorBrush(Color.FromRgb(225, 231, 244));
        status.Text = accepted ? "Native API accepted · live previews" : "Solid fallback · live previews";
        var mapSize = new SizeD(bounds.Width, bounds.Height * .76);
        var projection = OverviewProjection.Fit(sources.Select(source => source.WorldBounds)
            .Append(new RectD(PointD.Zero, bounds.Size)), mapSize, 48);
        var port = previewFactory(handle);
        using var batch = port.BeginBatch();
        for (var i = 0; i < sources.Length; i++)
        {
            var source = sources[i]; var map = projection.ToMap(source.WorldBounds);
            var to = Pixels(map); var from = i == 0 ? new PixelRect(source.ScreenBounds.Left - bounds.Left, source.ScreenBounds.Top - bounds.Top,
                source.ScreenBounds.Right - bounds.Left, source.ScreenBounds.Bottom - bounds.Top) : to;
            var outline = new Rectangle { Stroke = new SolidColorBrush(i == 0 ? Color.FromRgb(170, 95, 250) : Color.FromRgb(65, 185, 230)),
                StrokeThickness = 3, RadiusX = 8, RadiusY = 8 };
            var scale = nativeSource.CompositionTarget.TransformFromDevice;
            outline.Width = to.Width * scale.M11 + 8; outline.Height = to.Height * scale.M22 + 8;
            Canvas.SetLeft(outline, to.Left * scale.M11 - 4); Canvas.SetTop(outline, to.Top * scale.M22 - 4); outlines.Children.Add(outline);
            var preview = port.Create(source.Identity);
            if (preview is null) { failures++; continue; }
            created++;
            if (preview.SourceSize is null) { preview.Dispose(); released++; failures++; continue; }
            previews.Add((preview, from, to, i == 0));
        }
        UpdatePreviews(0);
    }

    private void Start(object? sender, EventArgs args)
    {
        ContentRendered -= Start;
        watchdog.Start(); Retarget(true);
    }
    private void Render(object? sender, EventArgs args)
    {
        if (closed) { lateCallbacks++; return; }
        var now = clock.Elapsed.TotalMilliseconds;
        if (callbacks++ == 0) firstCallback = now; else if (!sequenceFirst) gaps.Add(now - previous);
        sequenceFirst = false;
        previous = now;
        var progress = (double)GetValue(ProgressProperty);
        UpdatePreviews(progress); outlines.Opacity = progress; footer.Opacity = .25 + .75 * progress;
        if (closeDuringMotion && progress > .3 && progress < 1) { closedDuringMotion = true; Close(); return; }
        if (!interactive)
        {
            if (phase == 0 && progress > .45) { phase = 1; Retarget(false); }
            else if (phase == 1 && progress < .2) { phase = 2; Retarget(true); }
            else if (phase == 2 && progress >= 1)
            { phase = 3; idleCallbacks = callbacks; idleUpdates = updates; idleCheck.Start(); }
        }
        if (progress == (targetOpen ? 1 : 0)) StopRendering();
    }
    private void Retarget(bool open)
    {
        if (closed) return;
        if (targetOpen != open) reversals++;
        targetOpen = open;
        if (!subscribed) { CompositionTarget.Rendering += Render; subscribed = true; sequenceFirst = true; }
        var current = (double)GetValue(ProgressProperty);
        var destination = open ? 1.0 : 0.0;
        var duration = Math.Max(1, (interactive ? 300 : 650) * Math.Abs(destination - current));
        // Read the WPF animated property on each callback, never a second stopwatch interpolation.
        var animation = new DoubleAnimation(current, destination, TimeSpan.FromMilliseconds(duration))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(ProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
    private void UpdatePreviews(double progress)
    {
        foreach (var item in previews)
        {
            var rect = Interpolate(item.From, item.To, progress);
            var opacity = item.Focus ? (byte)255 : (byte)Math.Clamp((int)Math.Round(progress * 255), 0, 255);
            if (lastUpdates.TryGetValue(item.Preview, out var last) && last == (rect, opacity)) continue;
            if (item.Preview.Update(rect, null, opacity)) { updates++; lastUpdates[item.Preview] = (rect, opacity); }
            else failures++;
        }
    }
    private void Key(object sender, KeyEventArgs args)
    {
        if (args.Key == System.Windows.Input.Key.Escape) { args.Handled = true; Close(); }
        else if (args.Key == System.Windows.Input.Key.Tab && (Keyboard.Modifiers & ~ModifierKeys.Shift) == 0)
        { args.Handled = true; Retarget(!targetOpen); }
    }
    private void Watchdog(object? sender, EventArgs args) { timedOut = true; Close(); }
    private void VerifyIdle(object? sender, EventArgs args)
    {
        idleCheck.Stop(); idleVerified = !subscribed && callbacks == idleCallbacks && updates == idleUpdates; Close();
    }
    private void Release(object? sender, EventArgs args)
    {
        if (closed) return;
        closed = true; watchdog.Stop(); watchdog.Tick -= Watchdog;
        idleCheck.Stop(); idleCheck.Tick -= VerifyIdle;
        StopRendering();
        BeginAnimation(ProgressProperty, null);
        foreach (var item in previews) { item.Preview.Dispose(); released++; }
        previews.Clear(); lastUpdates.Clear();
    }
    private void StopRendering() { if (subscribed) { CompositionTarget.Rendering -= Render; subscribed = false; } }
    private static PixelRect Pixels(RectD value) => new((int)Math.Round(value.Position.X), (int)Math.Round(value.Position.Y),
        (int)Math.Round(value.Right), (int)Math.Round(value.Bottom));
    private static PixelRect Interpolate(PixelRect from, PixelRect to, double progress)
    {
        int Value(int a, int b) => (int)Math.Round(a + (b - a) * progress);
        return new(Value(from.Left, to.Left), Value(from.Top, to.Top), Value(from.Right, to.Right), Value(from.Bottom, to.Bottom));
    }
}
