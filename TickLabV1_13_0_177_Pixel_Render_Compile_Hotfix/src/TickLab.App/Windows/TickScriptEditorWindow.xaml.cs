using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TickLab.Core.Scripting;

namespace TickLab.Desktop.Windows;

public partial class TickScriptEditorWindow : Window
{
    private readonly TickScriptCompiler _compiler = new();
    private readonly TickScriptStore _store = new();
    private readonly IReadOnlyList<ScriptKindOption> _kindOptions;
    private TickScriptEntry? _currentEntry;
    private bool _loadingDocument;
    private bool _dirty;
    private bool _revertingSelection;
    private bool _allowClose;

    public TickScriptEditorWindow()
    {
        InitializeComponent();

        _kindOptions = Enum.GetValues<TickScriptKind>()
            .Select(kind => new ScriptKindOption(kind, kind.DisplayName()))
            .ToArray();
        KindBox.ItemsSource = _kindOptions;

        RefreshScriptList();
        CreateNewDocument(TickScriptKind.Indicator);
        FooterStatusText.Text = $"Scripts save automatically to {_store.RootPath} after a successful compile.";
    }

    public void OpenEntry(TickScriptEntry entry)
    {
        if (!CanDiscardCurrentChanges())
            return;
        RefreshScriptList();
        LoadEntry(entry);
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void NewIndicatorButton_Click(object sender, RoutedEventArgs e)
    {
        if (CanDiscardCurrentChanges())
            CreateNewDocument(TickScriptKind.Indicator);
    }

    private void NewEaButton_Click(object sender, RoutedEventArgs e)
    {
        if (CanDiscardCurrentChanges())
            CreateNewDocument(TickScriptKind.ExpertAdvisor);
    }

    private void NewScriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (CanDiscardCurrentChanges())
            CreateNewDocument(GetSelectedKind());
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var help = new TickScriptHelpWindow { Owner = this };
        help.ShowDialog();
    }

    private void CompileButton_Click(object sender, RoutedEventArgs e) =>
        CompileAndSave();

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        TickScriptEntry? entry = _currentEntry ?? ScriptList.SelectedItem as TickScriptEntry;
        if (entry is null)
        {
            SetStatus("Select a saved script to delete.", false);
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            this,
            $"Delete '{entry.Name}' from TickLab {entry.Kind.FolderName()}?",
            "Delete TickScript",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            _store.Delete(entry);
            RefreshScriptList();
            CreateNewDocument(entry.Kind);
            SetStatus($"Deleted {entry.Name}.", true);
        }
        catch (Exception exception)
        {
            SetStatus($"Delete failed: {exception.Message}", false);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        TickScriptKind kind = GetSelectedKind();
        string folder = _store.GetFolder(kind);
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
            SetStatus($"Opened {kind.FolderName()} folder.", true);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not open folder: {exception.Message}", false);
        }
    }

    private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDocument || _revertingSelection)
            return;
        if (ScriptList.SelectedItem is not TickScriptEntry entry)
            return;

        if (!CanDiscardCurrentChanges())
        {
            _revertingSelection = true;
            ScriptList.SelectedItem = _currentEntry;
            _revertingSelection = false;
            return;
        }

        LoadEntry(entry);
    }

    private void Metadata_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingDocument)
            return;
        MarkDirty();
    }

    private void CodeEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingDocument)
            MarkDirty();
        UpdateCursorStatus();
    }

    private void CodeEditor_SelectionChanged(object sender, RoutedEventArgs e) =>
        UpdateCursorStatus();

    private void DiagnosticsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DiagnosticsGrid.SelectedItem is not TickScriptDiagnostic diagnostic || diagnostic.Line <= 0)
            return;

        int offset = GetLineOffset(CodeEditor.Text, diagnostic.Line);
        int lineLength = GetLineLength(CodeEditor.Text, offset);
        int columnOffset = Math.Clamp(diagnostic.Column - 1, 0, lineLength);
        CodeEditor.Focus();
        CodeEditor.CaretIndex = Math.Min(CodeEditor.Text.Length, offset + columnOffset);
        CodeEditor.ScrollToLine(Math.Max(0, diagnostic.Line - 1));
        UpdateCursorStatus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 ||
            (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            CompileAndSave();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.N && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (CanDiscardCurrentChanges())
                CreateNewDocument(GetSelectedKind());
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_dirty)
            return;

        MessageBoxResult answer = MessageBox.Show(
            this,
            "This script has uncompiled changes. Close without saving them?",
            "Uncompiled TickScript",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    private void CompileAndSave()
    {
        string name = NameBox.Text.Trim();
        TickScriptKind kind = GetSelectedKind();
        TickScriptCompileResult result = _compiler.Compile(name, kind, CodeEditor.Text);
        DiagnosticsGrid.ItemsSource = result.Diagnostics;

        if (!result.Success)
        {
            TickScriptDiagnostic? firstError = result.Diagnostics.FirstOrDefault(
                item => item.Severity == TickScriptDiagnosticSeverity.Error);
            SetStatus(
                firstError is null
                    ? "Compile failed."
                    : $"Compile failed: {firstError.Message}",
                false);
            return;
        }

        try
        {
            TickScriptEntry saved = _store.SaveCompiled(name, kind, CodeEditor.Text, result);
            _currentEntry = saved;
            _dirty = false;
            RefreshScriptList(saved.SourcePath);
            SetStatus(
                $"Compiled and saved {saved.Name} in TickLab\\{saved.Kind.FolderName()}.",
                true);
        }
        catch (Exception exception)
        {
            var diagnostics = result.Diagnostics
                .Concat(new[]
                {
                    new TickScriptDiagnostic(
                        TickScriptDiagnosticSeverity.Error,
                        0,
                        0,
                        "TLS9000",
                        $"Compile passed, but saving failed: {exception.Message}")
                })
                .ToArray();
            DiagnosticsGrid.ItemsSource = diagnostics;
            SetStatus($"Saving failed: {exception.Message}", false);
        }
    }

    private void CreateNewDocument(TickScriptKind kind)
    {
        string name = $"My {kind.DisplayName()}";
        SetDocument(
            null,
            name,
            kind,
            TickScriptStore.CreateTemplate(kind, name));
        DiagnosticsGrid.ItemsSource = Array.Empty<TickScriptDiagnostic>();
        SetStatus($"New {kind.DisplayName()} ready.", true);
    }

    private void LoadEntry(TickScriptEntry entry)
    {
        try
        {
            string source = _store.LoadSource(entry);
            TickScriptKind kind = TickScriptStore.DetectKind(source, entry.Kind);
            SetDocument(entry, entry.Name, kind, source);
            DiagnosticsGrid.ItemsSource = Array.Empty<TickScriptDiagnostic>();
            SetStatus($"Loaded {entry.Name}.", true);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not load script: {exception.Message}", false);
        }
    }

    private void SetDocument(
        TickScriptEntry? entry,
        string name,
        TickScriptKind kind,
        string source)
    {
        _loadingDocument = true;
        try
        {
            _currentEntry = entry;
            NameBox.Text = name;
            KindBox.SelectedItem = _kindOptions.First(option => option.Kind == kind);
            CodeEditor.Text = source;
            CodeEditor.CaretIndex = 0;
            CodeEditor.ScrollToHome();
            ScriptList.SelectedItem = entry;
            _dirty = false;
            Title = entry is null
                ? "TickLab Script Editor — New script"
                : $"TickLab Script Editor — {entry.Name}";
        }
        finally
        {
            _loadingDocument = false;
        }
        UpdateCursorStatus();
    }

    private void RefreshScriptList(string? selectSourcePath = null)
    {
        IReadOnlyList<TickScriptEntry> scripts = _store.GetScripts();
        ScriptList.ItemsSource = scripts;
        ScriptCountText.Text = scripts.Count == 1 ? "1 script" : $"{scripts.Count} scripts";

        if (!string.IsNullOrWhiteSpace(selectSourcePath))
        {
            TickScriptEntry? selected = scripts.FirstOrDefault(item =>
                string.Equals(item.SourcePath, selectSourcePath, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                _loadingDocument = true;
                ScriptList.SelectedItem = selected;
                _loadingDocument = false;
                _currentEntry = selected;
            }
        }
    }

    private bool CanDiscardCurrentChanges()
    {
        if (!_dirty)
            return true;

        MessageBoxResult answer = MessageBox.Show(
            this,
            "The current script has uncompiled changes. Discard them?",
            "Discard changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
    }

    private TickScriptKind GetSelectedKind() =>
        KindBox.SelectedItem is ScriptKindOption option
            ? option.Kind
            : TickScriptKind.Indicator;

    private void MarkDirty()
    {
        _dirty = true;
        CompileDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        HeaderStatusText.Text = "Uncompiled changes";
        if (!Title.EndsWith(" *", StringComparison.Ordinal))
            Title += " *";
    }

    private void SetStatus(string message, bool success)
    {
        HeaderStatusText.Text = success ? "Ready" : "Needs attention";
        CompileDot.Fill = new SolidColorBrush(success
            ? Color.FromRgb(38, 194, 129)
            : Color.FromRgb(239, 68, 68));
        FooterStatusText.Text = message;
        if (!_dirty)
            Title = _currentEntry is null
                ? "TickLab Script Editor — New script"
                : $"TickLab Script Editor — {_currentEntry.Name}";
    }

    private void UpdateCursorStatus()
    {
        int caret = Math.Clamp(CodeEditor.CaretIndex, 0, CodeEditor.Text.Length);
        int line = CodeEditor.GetLineIndexFromCharacterIndex(caret);
        if (line < 0)
            line = 0;
        int lineStart = CodeEditor.GetCharacterIndexFromLineIndex(line);
        if (lineStart < 0)
            lineStart = 0;
        CursorText.Text = $"Ln {line + 1}, Col {caret - lineStart + 1}";
    }

    private static int GetLineOffset(string text, int oneBasedLine)
    {
        if (oneBasedLine <= 1)
            return 0;

        int currentLine = 1;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
                continue;
            currentLine++;
            if (currentLine == oneBasedLine)
                return index + 1;
        }
        return text.Length;
    }

    private static int GetLineLength(string text, int offset)
    {
        int newline = text.IndexOf('\n', offset);
        return (newline < 0 ? text.Length : newline) - offset;
    }

    private sealed record ScriptKindOption(TickScriptKind Kind, string Label);
}
