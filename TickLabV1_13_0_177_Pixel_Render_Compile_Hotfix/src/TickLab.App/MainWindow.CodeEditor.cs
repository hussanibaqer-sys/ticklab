using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using TickLab.Core.Scripting;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private sealed record InlineScriptKindOption(TickScriptKind Kind, string Label);
    private readonly TickScriptCompiler _inlineCodeCompiler = new();
    private readonly TickScriptStore _inlineCodeStore = new();
    private IReadOnlyList<InlineScriptKindOption> _inlineCodeKinds = Array.Empty<InlineScriptKindOption>();
    private double _codeEditorExpandedWidth = 520;
    private bool _codeEditorHandleDragging;
    private bool _codeEditorHandleMoved;
    private double _codeEditorHandleStartX;
    private double _codeEditorHandleStartWidth;

    private void InitializeInlineCodeEditor()
    {
        _inlineCodeKinds = Enum.GetValues<TickScriptKind>()
            .Select(kind => new InlineScriptKindOption(kind, kind.DisplayName()))
            .ToArray();
        InlineCodeKindBox.ItemsSource = _inlineCodeKinds;
        InlineCodeKindBox.SelectedItem = _inlineCodeKinds.First(item => item.Kind == TickScriptKind.Indicator);
        CreateInlineCodeDocument(TickScriptKind.Indicator);
    }

    private void CodePanelButton_Click(object sender, RoutedEventArgs e) =>
        ShowCodeEditorPanel(CodeEditorPanelBorder.Visibility != Visibility.Visible);

    private void CodeEditorSlideButton_Click(object sender, RoutedEventArgs e)
    {
        if (_codeEditorHandleMoved)
            return;
        ShowCodeEditorPanel(CodeEditorPanelBorder.Visibility != Visibility.Visible);
    }

    private void CodeEditorHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(sender, CodeEditorSlideButton) || e.ChangedButton != MouseButton.Left)
            return;
        CancelOtherRightHandleInteractions(CodeEditorSlideButton);
        _codeEditorHandleDragging = true;
        _codeEditorHandleMoved = false;
        _codeEditorHandleStartX = e.GetPosition(this).X;
        _codeEditorHandleStartWidth = CodeEditorPanelBorder.Visibility == Visibility.Visible
            ? Math.Max(0.0, CodeEditorColumn.ActualWidth)
            : 0.0;
    }


    private void CancelOtherRightHandleInteractions(UIElement owner)
    {
        if (Mouse.Captured is UIElement captured && !ReferenceEquals(captured, owner))
            Mouse.Capture(null);

        if (!ReferenceEquals(owner, CodeEditorSlideButton))
        {
            _codeEditorHandleDragging = false;
            _codeEditorHandleMoved = false;
        }
        if (!ReferenceEquals(owner, DemoTradeSlideButton))
        {
            _demoSlideDragging = false;
            _demoSlideMoved = false;
        }
        if (!ReferenceEquals(owner, RightWorkspaceToggleButton))
        {
            _rightWorkspaceHandleDragging = false;
            _rightWorkspaceHandleMoved = false;
        }
    }

    private void CodeEditorHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_codeEditorHandleDragging || sender is not UIElement handle)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishCodeEditorHandleInteraction(handle);
            return;
        }

        double delta = e.GetPosition(this).X - _codeEditorHandleStartX;
        if (!_codeEditorHandleMoved && Math.Abs(delta) >= SystemParameters.MinimumHorizontalDragDistance)
        {
            _codeEditorHandleMoved = true;
            Mouse.Capture(handle, CaptureMode.Element);
        }
        if (!_codeEditorHandleMoved)
            return;

        SetCodeEditorDragWidth(_codeEditorHandleStartWidth - delta);
        e.Handled = true;
    }

    private void CodeEditorHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_codeEditorHandleDragging || sender is not UIElement handle || e.ChangedButton != MouseButton.Left)
            return;
        if (_codeEditorHandleMoved)
        {
            FinishCodeEditorHandleInteraction(handle);
            e.Handled = true;
        }
        else
        {
            _codeEditorHandleDragging = false;
        }
    }

    private void CodeEditorHandle_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_codeEditorHandleDragging && _codeEditorHandleMoved)
            FinishCodeEditorHandleInteraction(sender as UIElement);
    }

    private void CodeEditorHandle_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space))
            return;
        ShowCodeEditorPanel(CodeEditorPanelBorder.Visibility != Visibility.Visible);
        e.Handled = true;
    }

    private void SetCodeEditorDragWidth(double requestedWidth)
    {
        double width = Math.Clamp(requestedWidth, 0.0, 760.0);
        if (width < 72.0)
        {
            CodeEditorColumn.Width = new GridLength(0);
            CodeEditorSplitterColumn.Width = new GridLength(0);
            CodeEditorPanelBorder.Visibility = Visibility.Collapsed;
            CodeEditorSplitter.Visibility = Visibility.Collapsed;
            CodePanelButton.Tag = null;
            CodeEditorSlideButton.ToolTip = "Click to open Code Editor; drag left to open or resize";
            return;
        }

        width = Math.Clamp(width, 360.0, 760.0);
        _codeEditorExpandedWidth = width;
        CodeEditorColumn.Width = new GridLength(width);
        CodeEditorSplitterColumn.Width = new GridLength(5);
        CodeEditorPanelBorder.Visibility = Visibility.Visible;
        CodeEditorSplitter.Visibility = Visibility.Visible;
        CodePanelButton.Tag = "Active";
        CodeEditorSlideButton.ToolTip = "Click to close Code Editor; drag left or right to resize";
    }

    private void FinishCodeEditorHandleInteraction(UIElement? handle)
    {
        _codeEditorHandleDragging = false;
        bool moved = _codeEditorHandleMoved;
        _codeEditorHandleMoved = false;
        if (handle?.IsMouseCaptured == true)
            Mouse.Capture(null);
        if (!moved)
            return;

        if (CodeEditorColumn.ActualWidth < 72.0)
            ShowCodeEditorPanel(false);
        else
        {
            _codeEditorExpandedWidth = Math.Clamp(CodeEditorColumn.ActualWidth, 360.0, 760.0);
            ShowCodeEditorPanel(true);
        }
    }

    private void CloseCodePanelButton_Click(object sender, RoutedEventArgs e) => ShowCodeEditorPanel(false);

    private void ShowCodeEditorPanel(bool show)
    {
        if (show)
        {
            CodeEditorColumn.Width = new GridLength(Math.Clamp(_codeEditorExpandedWidth, 360, 760));
            CodeEditorSplitterColumn.Width = new GridLength(5);
            CodeEditorPanelBorder.Visibility = Visibility.Visible;
            CodeEditorSplitter.Visibility = Visibility.Visible;
            CodePanelButton.Tag = "Active";
            CodeEditorSlideButton.ToolTip = "Click to close Code Editor; drag left or right to resize";
            InlineCodeEditorBox.Focus();
        }
        else
        {
            if (CodeEditorColumn.ActualWidth > 100)
                _codeEditorExpandedWidth = CodeEditorColumn.ActualWidth;
            CodeEditorColumn.Width = new GridLength(0);
            CodeEditorSplitterColumn.Width = new GridLength(0);
            CodeEditorPanelBorder.Visibility = Visibility.Collapsed;
            CodeEditorSplitter.Visibility = Visibility.Collapsed;
            CodePanelButton.Tag = null;
            CodeEditorSlideButton.ToolTip = "Click to open Code Editor; drag left to open or resize";
        }
    }

    private TickScriptKind GetInlineCodeKind() =>
        InlineCodeKindBox.SelectedItem is InlineScriptKindOption option
            ? option.Kind
            : TickScriptKind.Indicator;

    private void InlineCodeNewIndicatorButton_Click(object sender, RoutedEventArgs e) =>
        CreateInlineCodeDocument(TickScriptKind.Indicator);

    private void InlineCodeNewEaButton_Click(object sender, RoutedEventArgs e) =>
        CreateInlineCodeDocument(TickScriptKind.ExpertAdvisor);

    private void CreateInlineCodeDocument(TickScriptKind kind)
    {
        InlineCodeKindBox.SelectedItem = _inlineCodeKinds.First(item => item.Kind == kind);
        string name = $"My {kind.DisplayName()}";
        InlineCodeNameBox.Text = name;
        InlineCodeEditorBox.Text = TickScriptStore.CreateTemplate(kind, name);
        InlineCodeEditorBox.CaretIndex = 0;
        InlineCodeEditorBox.ScrollToHome();
        InlineCodeEditorStatusText.Text = $"New {kind.DisplayName()}";
    }

    private void InlineCodeCompileButton_Click(object sender, RoutedEventArgs e)
    {
        string name = InlineCodeNameBox.Text.Trim();
        TickScriptKind kind = GetInlineCodeKind();
        TickScriptCompileResult result = _inlineCodeCompiler.Compile(name, kind, InlineCodeEditorBox.Text);
        if (!result.Success)
        {
            TickScriptDiagnostic? first = result.Diagnostics.FirstOrDefault(item =>
                item.Severity == TickScriptDiagnosticSeverity.Error);
            InlineCodeEditorStatusText.Text = first is null ? "Compile failed" : $"Line {first.Line}: {first.Message}";
            InlineCodeFooterText.Text = first?.Code ?? "Compile failed";
            return;
        }

        try
        {
            TickScriptEntry saved = _inlineCodeStore.SaveCompiled(name, kind, InlineCodeEditorBox.Text, result);
            InlineCodeEditorStatusText.Text = "Compiled";
            InlineCodeFooterText.Text = $"Saved {saved.Name} in TickLab\\{saved.Kind.FolderName()}";
            _indicatorsWindow?.Refresh();
        }
        catch (Exception exception)
        {
            InlineCodeEditorStatusText.Text = "Save failed";
            InlineCodeFooterText.Text = exception.Message;
        }
    }

    private void OpenFullCodeEditorButton_Click(object sender, RoutedEventArgs e) => OpenFullCodeEditor();

    private void OpenFullCodeEditor()
    {
        if (_scriptEditorWindow is not null)
        {
            if (_scriptEditorWindow.WindowState == WindowState.Minimized)
                _scriptEditorWindow.WindowState = WindowState.Normal;
            _scriptEditorWindow.Activate();
            return;
        }

        var editor = new TickLab.Desktop.Windows.TickScriptEditorWindow { Owner = this };
        editor.Closed += (_, _) => _scriptEditorWindow = null;
        _scriptEditorWindow = editor;
        editor.Show();
    }

    private void InlineCodeOpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = _inlineCodeStore.GetFolder(GetInlineCodeKind());
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            InlineCodeFooterText.Text = exception.Message;
        }
    }
}
