using System;
using System.Threading.Tasks;
using NetworkOptimizer.Config;
using NetworkOptimizer.Core;
using NetworkOptimizer.Updates;
using NetworkOptimizer.Updates.Models;

namespace NetworkOptimizer.UI.ViewModels;

public class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _updateService;
    private UpdateManifest? _availableManifest;

    private bool _isChecking;
    private bool _isUpdateAvailable;
    private string _availableVersion = string.Empty;
    private bool _isDownloading;
    private double _downloadProgress;
    private string _statusMessage = string.Empty;
    private string _updateButtonText = "Actualizar";

    public string CurrentVersion => AppConfig.AppVersion;
    public string CurrentVersionDisplay => $"v{AppConfig.AppVersion}";

    public bool IsChecking
    {
        get => _isChecking;
        set => SetField(ref _isChecking, value);
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set => SetField(ref _isUpdateAvailable, value);
    }

    public string AvailableVersion
    {
        get => _availableVersion;
        set => SetField(ref _availableVersion, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (SetField(ref _isDownloading, value))
            {
                OnPropertyChanged(nameof(CanApplyUpdate));
                ApplyUpdateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetField(ref _downloadProgress, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string UpdateButtonText
    {
        get => _updateButtonText;
        set => SetField(ref _updateButtonText, value);
    }

    public bool CanApplyUpdate => IsUpdateAvailable && !IsDownloading;

    public AsyncRelayCommand CheckForUpdateCommand { get; }
    public AsyncRelayCommand ApplyUpdateCommand { get; }

    public UpdateViewModel() : this(new UpdateService())
    {
    }

    public UpdateViewModel(UpdateService updateService)
    {
        _updateService = updateService;

        CheckForUpdateCommand = new AsyncRelayCommand(CheckForUpdateAsync, () => !IsChecking && !IsDownloading);
        ApplyUpdateCommand = new AsyncRelayCommand(ApplyUpdateAsync, () => CanApplyUpdate);

        // Comprobación automática en segundo plano sin bloquear el arranque
        _ = CheckForUpdateInBackgroundAsync();
    }

    private async Task CheckForUpdateInBackgroundAsync()
    {
        // Pequeña pausa inicial de cortesía para no competir con el renderizado inicial de WPF
        await Task.Delay(1500);
        await CheckForUpdateAsync();
    }

    public async Task CheckForUpdateAsync()
    {
        if (IsChecking || IsDownloading) return;

        IsChecking = true;
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            if (result.IsAvailable && result.Manifest != null)
            {
                _availableManifest = result.Manifest;
                AvailableVersion = $"v{result.NewVersion}";
                IsUpdateAvailable = true;
                UpdateButtonText = "Actualizar";
                StatusMessage = $"Nueva versión disponible: {AvailableVersion}";
            }
            else
            {
                IsUpdateAvailable = false;
                StatusMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Info("Actualizador", $"Comprobación de versión omitida: {ex.Message}");
            IsUpdateAvailable = false;
        }
        finally
        {
            IsChecking = false;
            OnPropertyChanged(nameof(CanApplyUpdate));
            ApplyUpdateCommand.RaiseCanExecuteChanged();
        }
    }

    public async Task ApplyUpdateAsync()
    {
        if (_availableManifest == null || IsDownloading) return;

        IsDownloading = true;
        UpdateButtonText = "Descargando actualización...";
        StatusMessage = $"Descargando {AvailableVersion}...";
        DownloadProgress = 0;

        var progress = new Progress<double>(pct =>
        {
            DownloadProgress = pct;
            StatusMessage = $"Descargando actualización ({pct:F0}%)...";
        });

        try
        {
            var downloadResult = await _updateService.DownloadAndVerifyUpdateAsync(_availableManifest, progress);
            if (downloadResult.Success && !string.IsNullOrEmpty(downloadResult.DownloadedFilePath))
            {
                StatusMessage = "Verificación SHA-256 correcta. Iniciando actualizador...";
                UpdateButtonText = "Instalando...";

                // Dar un breve instante para que la UI refleje el estado antes de cerrar
                await Task.Delay(400);

                var launched = _updateService.LaunchUpdaterAndExit(downloadResult.DownloadedFilePath, restart: true);
                if (!launched)
                {
                    StatusMessage = "No se pudo iniciar el proceso de actualización.";
                    UpdateButtonText = "Reintentar";
                    IsDownloading = false;
                }
            }
            else
            {
                StatusMessage = downloadResult.ErrorMessage ?? "Fallo en la verificación del paquete.";
                UpdateButtonText = "Reintentar";
                IsDownloading = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al actualizar: {ex.Message}";
            UpdateButtonText = "Reintentar";
            IsDownloading = false;
        }
    }
}
