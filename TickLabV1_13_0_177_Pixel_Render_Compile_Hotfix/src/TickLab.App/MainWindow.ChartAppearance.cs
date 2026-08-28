using System.Windows;
using TickLab.Core.Indicators;
using TickLab.Desktop.Controls;
using TickLab.Desktop.Settings;
using TickLab.Desktop.Windows;

namespace TickLab.Desktop;

public partial class MainWindow
{
    private readonly ChartTemplateStore _chartTemplateStore = new();

    private void OpenChartSettings(CandleChartControl chart)
    {
        ActivateChartControl(chart);
        OpenChartSettingsForSelectedChart();
    }

    private void SaveChartTemplate(CandleChartControl chart)
    {
        ActivateChartControl(chart);
        ChartRuntimeContext context = FindChartContext(chart);
        var dialog = new ChartTemplateNameDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (_chartTemplateStore.Contains(dialog.TemplateName) &&
            MessageBox.Show(
                this,
                $"Template '{dialog.TemplateName}' already exists. Replace it?",
                "Replace chart template",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _chartTemplateStore.Save(dialog.TemplateName, context.Settings, CloneBuiltInIndicators(context.BuiltInIndicators));
        StatusText.Text = $"Chart template '{dialog.TemplateName}' saved.";
    }

    private void LoadChartTemplate(CandleChartControl chart)
    {
        ActivateChartControl(chart);
        IReadOnlyList<ChartTemplateEntry> templates = _chartTemplateStore.LoadAll();
        if (templates.Count == 0)
        {
            MessageBox.Show(this, "No chart templates are saved yet.", "Load template", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ChartTemplatePickerDialog("Load chart template", templates, "Load") { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        ChartTemplateEntry? selectedTemplate = dialog.SelectedTemplate;
        if (selectedTemplate is null)
            return;

        ChartRuntimeContext context = FindChartContext(chart);
        context.Settings = selectedTemplate.Settings;
        chart.Settings = context.Settings;
        context.TickChart.Settings = context.Settings;
        context.IndicatorStack.SetChartSettings(context.Settings);
        foreach (BuiltInIndicatorInstance existing in context.BuiltInIndicators.ToArray())
            context.IndicatorStack.Remove(existing);
        context.BuiltInIndicators.Clear();
        context.BuiltInIndicatorResults.Clear();
        context.BuiltInIndicators.AddRange(CloneBuiltInIndicators(selectedTemplate.BuiltInIndicators ?? Array.Empty<BuiltInIndicatorInstance>()));
        ApplyBuiltInOverlayResults(context);
        RefreshBuiltInIndicatorsForContext(context, force: true);
        ShowIndicatorsForActiveChart();
        SaveWorkspace();
        StatusText.Text = $"Template '{selectedTemplate.Name}' loaded on Chart {context.PaneId}.";
    }

    private void DeleteChartTemplate(CandleChartControl chart)
    {
        ActivateChartControl(chart);
        IReadOnlyList<ChartTemplateEntry> templates = _chartTemplateStore.LoadAll();
        if (templates.Count == 0)
        {
            MessageBox.Show(this, "No chart templates are saved yet.", "Delete template", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ChartTemplatePickerDialog("Delete chart template", templates, "Select") { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        ChartTemplateEntry? selectedTemplate = dialog.SelectedTemplate;
        if (selectedTemplate is null)
            return;

        if (MessageBox.Show(
                this,
                $"Are you sure you want to delete the template '{selectedTemplate.Name}'?",
                "Delete chart template",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _chartTemplateStore.Delete(selectedTemplate.Name);
        StatusText.Text = $"Template '{selectedTemplate.Name}' deleted.";
    }
}
