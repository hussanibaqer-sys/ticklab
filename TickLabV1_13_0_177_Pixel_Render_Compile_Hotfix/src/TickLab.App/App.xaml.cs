using System.Windows;
using System.Windows.Threading;
using TickLab.Core.Diagnostics;
using TickLab.Desktop.Settings;

namespace TickLab.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                    ApplicationThemeManager.ApplyToWindow(window);
            }));
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        base.OnExit(e);
    }

    private static void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        if (IsExpectedCancellation(e.Exception))
        {
            e.Handled = true;
            return;
        }

        TickLabErrorEngine.Report(
            e.Exception,
            new TickLabErrorContext(
                "TickLab core",
                "UI dispatcher",
                "Copy the diagnostics, close this message, save any work that remains responsive, and restart TickLab. The error is permanently logged.",
                ErrorCode: "TL-CORE-UI"),
            TickLabErrorSeverity.Critical);
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(
        object? sender,
        UnhandledExceptionEventArgs e)
    {
        Exception exception = e.ExceptionObject as Exception ??
            new InvalidOperationException(Convert.ToString(e.ExceptionObject) ?? "Unknown fatal error.");
        TickLabErrorEngine.Report(
            exception,
            new TickLabErrorContext(
                "TickLab core",
                "Process failure",
                "Copy the diagnostics and restart TickLab. Check the log folder before repeating the operation.",
                ErrorCode: "TL-CORE-PROCESS"),
            TickLabErrorSeverity.Critical,
            showPopup: true);
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        if (IsExpectedCancellation(e.Exception))
        {
            e.SetObserved();
            return;
        }

        TickLabErrorEngine.Report(
            e.Exception,
            new TickLabErrorContext(
                "TickLab background task",
                "Unobserved task",
                "Copy the diagnostics. The failed background operation was stopped and its details were logged.",
                ErrorCode: "TL-CORE-TASK"),
            TickLabErrorSeverity.Error,
            showPopup: true);
        e.SetObserved();
    }
    private static bool IsExpectedCancellation(Exception exception)
    {
        if (exception is OperationCanceledException)
            return true;

        return exception is AggregateException aggregate &&
            aggregate.Flatten().InnerExceptions.Count > 0 &&
            aggregate.Flatten().InnerExceptions.All(IsExpectedCancellation);
    }

}
