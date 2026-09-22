using System.Windows;
using NetworkOptimizer.Config;
using NetworkOptimizer.Core;
using NetworkOptimizer.Licensing;

namespace NetworkOptimizer.UI.ViewModels;

public class LicensingViewModel : ObservableObject
{
    private readonly LicenseService _licenseService;

    private string _licenseKeyInput = string.Empty;
    private LicenseStatusInfo _statusInfo;
    private bool _isBusy;
    private string _feedbackMessage = string.Empty;

    public string ProductSlug => AppConfig.ProductSlug;
    public string ProductName => AppConfig.ProductName;

    public string LicenseKeyInput
    {
        get => _licenseKeyInput;
        set => SetField(ref _licenseKeyInput, value);
    }

    public LicenseStatusInfo StatusInfo
    {
        get => _statusInfo;
        set => SetField(ref _statusInfo, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public string FeedbackMessage
    {
        get => _feedbackMessage;
        set => SetField(ref _feedbackMessage, value);
    }

    public AsyncRelayCommand ValidateLicenseCommand { get; }
    public AsyncRelayCommand ActivateKeyCommand { get; }
    public RelayCommand DeactivateCommand { get; }
    public RelayCommand CopyHwidCommand { get; }

    public LicensingViewModel() : this(new LicenseService())
    {
    }

    public LicensingViewModel(LicenseService licenseService)
    {
        _licenseService = licenseService;
        _statusInfo = _licenseService.GetCachedStatus();

        ValidateLicenseCommand = new AsyncRelayCommand(ValidateAsync, () => !IsBusy);
        ActivateKeyCommand = new AsyncRelayCommand(ActivateAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(LicenseKeyInput));
        DeactivateCommand = new RelayCommand(Deactivate, () => !IsBusy && StatusInfo.IsLicensed);
        CopyHwidCommand = new RelayCommand(CopyHwid);

        _licenseService.LicenseStateChanged += _ =>
        {
            StatusInfo = _licenseService.GetCachedStatus();
            DeactivateCommand.RaiseCanExecuteChanged();
        };
    }

    private async Task ValidateAsync()
    {
        IsBusy = true;
        FeedbackMessage = "Conectando con el servidor de licencias oficial...";
        try
        {
            StatusInfo = await _licenseService.ValidateCurrentLicenseAsync(LicenseKeyInput);
            FeedbackMessage = StatusInfo.StatusText;
            DeactivateCommand.RaiseCanExecuteChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ActivateAsync()
    {
        IsBusy = true;
        FeedbackMessage = "Enviando solicitud de activación...";
        try
        {
            var (success, msg, status) = await _licenseService.ActivateKeyAsync(LicenseKeyInput);
            StatusInfo = status;
            FeedbackMessage = msg;
            if (success)
            {
                LicenseKeyInput = string.Empty;
            }
            DeactivateCommand.RaiseCanExecuteChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Deactivate()
    {
        _licenseService.Deactivate();
        StatusInfo = _licenseService.GetCachedStatus();
        FeedbackMessage = "Licencia local desvinculada. Funciones protegidas bloqueadas.";
        DeactivateCommand.RaiseCanExecuteChanged();
    }

    private void CopyHwid()
    {
        try
        {
            if (!string.IsNullOrEmpty(StatusInfo.HardwareId))
            {
                Clipboard.SetText(StatusInfo.HardwareId);
                FeedbackMessage = "Identificador de hardware (HWID) copiado al portapapeles.";
            }
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"No se pudo copiar HWID: {ex.Message}";
        }
    }
}
