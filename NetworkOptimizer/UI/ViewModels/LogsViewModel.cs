using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using NetworkOptimizer.Core;

namespace NetworkOptimizer.UI.ViewModels;

public class LogsViewModel : ObservableObject
{
    public ReadOnlyObservableCollection<LogEntry> LogEntries => AppLogger.Entries;

    public RelayCommand ClearLogsCommand { get; }
    public RelayCommand CopyLogsCommand { get; }

    public LogsViewModel()
    {
        ClearLogsCommand = new RelayCommand(AppLogger.Clear);
        CopyLogsCommand = new RelayCommand(CopyLogs);
    }

    private void CopyLogs()
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var entry in AppLogger.Entries)
            {
                sb.AppendLine(entry.DisplayText);
            }

            Clipboard.SetText(sb.ToString());
            AppLogger.Info("Logs", "Registro de actividad copiado al portapapeles.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Logs", $"Error al copiar logs: {ex.Message}");
        }
    }
}
