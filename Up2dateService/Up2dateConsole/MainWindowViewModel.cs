﻿using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using Up2dateConsole.Dialogs;
using Up2dateConsole.Helpers;
using Up2dateConsole.ServiceReference;
using Up2dateConsole.StateIndicator;
using Up2dateConsole.ViewService;

namespace Up2dateConsole
{
    public enum Texts
    {
        GoodCertificateMessage,
        BadCertificateMessage,
        CannotStartInstallation,
        ServiceNotResponding,
        ServiceAccessError,
        DeviceNotInitialized,
        CertificateNotAvailable,
        NewPackageAvailable,
        NewPackageInstalled,
        ClientUnaccessible,
        NoCertificate,
        ServerUnaccessible,
        AgentOrServerFilure,
        PackageStatusUnavailable,
        PackageStatusAvailable,
        PackageStatusDownloading,
        PackageStatusDownloaded,
        PackageStatusInstalling,
        PackageStatusInstalled,
        PackageStatusRestartNeeded,
        PackageStatusFailed,
        PackageStatusUnknown,
        FailedToAcquireCertificate,
    }

    public class MainWindowViewModel : NotifyPropertyChanged
    {
        private const int InitialDelay = 1000; // milliseconds
        private const int RefreshInterval = 20000; // milliseconds

        private static bool firstTimeRefresh = true;

        private string msiFolder;
        private bool operationInProgress;
        private ServiceState serviceState;
        private string deviceId;
        private readonly Timer timer = new Timer(InitialDelay);
        private readonly IViewService viewService;
        private readonly IWcfClientFactory wcfClientFactory;

        public MainWindowViewModel(IViewService viewService, IWcfClientFactory wcfClientFactory)
        {
            this.viewService = viewService ?? throw new ArgumentNullException(nameof(viewService));
            this.wcfClientFactory = wcfClientFactory ?? throw new ArgumentNullException(nameof(wcfClientFactory));

            ShowConsoleCommand = new RelayCommand(_ => viewService.ShowMainWindow());
            QuitCommand = new RelayCommand(_ => Application.Current.Shutdown());

            EnterAdminModeCommand = new RelayCommand(ExecuteEnterAdminMode);
            RefreshCommand = new RelayCommand(async (_) => await ExecuteRefresh());
            InstallCommand = new RelayCommand(ExecuteInstall, CanInstall);
            RequestCertificateCommand = new RelayCommand(async (_) => await ExecuteRequestCertificateAsync());

            AvailablePackages = new ObservableCollection<PackageItem>();

            timer.Start();
            timer.Elapsed += async (o, e) => await Timer_Elapsed();
        }

        public ICommand EnterAdminModeCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand ShowConsoleCommand { get; }
        public ICommand QuitCommand { get; }
        public ICommand RequestCertificateCommand { get; }

        public StateIndicatorViewModel StateIndicator { get; } = new StateIndicatorViewModel();

        public bool IsAdminMode => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        public bool IsUserMode => !IsAdminMode;

        public ServiceState ServiceState
        {
            get => serviceState;
            set
            {
                if (serviceState == value) return;
                serviceState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TaskbarIcon));
                OnPropertyChanged(nameof(TaskbarIconText));
                StateIndicator.SetState(value);
                ThreadHelper.SafeInvoke(CommandManager.InvalidateRequerySuggested);
            }
        }


        public string DeviceId
        {
            get => deviceId;
            private set
            {
                if (deviceId == value) return;
                deviceId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDeviceIdAvailable));
            }
        }

        public bool IsDeviceIdAvailable => !string.IsNullOrEmpty(DeviceId);

        public string MsiFolder
        {
            get => msiFolder;
            set
            {
                if (msiFolder == value) return;
                msiFolder = value;
                OnPropertyChanged();
            }
        }

        public bool OperationInProgress
        {
            get => operationInProgress;
            set
            {
                if (operationInProgress == value) return;
                operationInProgress = value;
                OnPropertyChanged();
                if (operationInProgress)
                {
                    ServiceState = ServiceState.Accessing;
                }
            }
        }

        public ObservableCollection<PackageItem> AvailablePackages { get; }

        private async Task Timer_Elapsed()
        {
            if (timer.Interval == InitialDelay)
            {
                timer.Interval = RefreshInterval;
            }

            if (!OperationInProgress)
            {
                await ExecuteRefresh();
            }
        }

        private async Task ExecuteRequestCertificateAsync()
        {
            await RequestCertificateAsync(showExplanation: false);
        }

        private async Task RequestCertificateAsync(bool showExplanation)
        {
            RequestCertificateDialogViewModel vm = new RequestCertificateDialogViewModel(viewService, wcfClientFactory, showExplanation);
            bool success = viewService.ShowDialog(vm);
            if (success)
            {
                await ExecuteRefresh();
                if (ServiceState == ServiceState.ServerUnaccessible)
                {
                    string message = string.Format(GetText(Texts.BadCertificateMessage), vm.DeviceId);
                    viewService.ShowMessageBox(message);
                }
                else
                {
                    string message = string.Format(GetText(Texts.GoodCertificateMessage), vm.DeviceId);
                    viewService.ShowMessageBox(message);
                }
            }
        }

        private void ExecuteEnterAdminMode(object _)
        {
            if (IsAdminMode) return;

            using (Process p = new Process())
            {
                p.StartInfo.FileName = Assembly.GetEntryAssembly().Location;
                p.StartInfo.Arguments = CommandLineHelper.AllowSecondInstanceCommand + " " + CommandLineHelper.VisibleMainWindowCommand;
                p.StartInfo.Verb = "runas";

                bool started = false;
                try
                {
                    started = p.Start();
                }
                catch (Exception)
                {
                    started = false;
                }

                if (started)
                {
                    Application.Current.Shutdown();
                }
            };
        }

        private bool CanInstall(object _)
        {
            if (ServiceState != ServiceState.Active) return false;

            List<PackageItem> selected = AvailablePackages.Where(p => p.IsSelected).ToList();
            return selected.Any() && selected.All(p => p.Package.Status == PackageStatus.Downloaded);
        }

        private async void ExecuteInstall(object _)
        {
            OperationInProgress = true;
            IWcfService service = null;

            Package[] selectedPackages = AvailablePackages
                .Where(p => p.IsSelected && p.Package.Status == PackageStatus.Downloaded)
                .Select(p => p.Package)
                .ToArray();
            try
            {
                service = wcfClientFactory.CreateClient();
                await service.StartInstallationAsync(selectedPackages);
                ServiceState = ServiceState.Active;
            }
            catch (System.ServiceModel.EndpointNotFoundException)
            {
                ServiceState = ServiceState.ClientUnaccessible;
                var message = GetText(Texts.CannotStartInstallation);
                StateIndicator.SetInfo(message);
                viewService.ShowMessageBox($"{GetText(Texts.CannotStartInstallation)}\n{message}");
            }
            catch (Exception e)
            {
                ServiceState = ServiceState.ClientUnaccessible;
                var message = e.Message;
                StateIndicator.SetInfo(message);
                viewService.ShowMessageBox($"{GetText(Texts.CannotStartInstallation)}\n{message}\n\n{e.StackTrace}");
            }
            finally
            {
                wcfClientFactory.CloseClient(service);
                OperationInProgress = false;
            }

            await ExecuteRefresh();
        }

        private async Task ExecuteRefresh()
        {
            OperationInProgress = true;
            IWcfService service = null;
            Package[] packages = null;
            ClientState clientState;
            try
            {
                service = wcfClientFactory.CreateClient();
                packages = await service.GetPackagesAsync();
                MsiFolder = await service.GetMsiFolderAsync();
                clientState = service.GetClientState();
                DeviceId = await service.GetDeviceIdAsync();
            }
            catch (System.ServiceModel.EndpointNotFoundException)
            {
                ServiceState = ServiceState.ClientUnaccessible;
                StateIndicator.SetInfo(GetText(Texts.ServiceNotResponding));
                DeviceId = null;
                OperationInProgress = false;
                return;
            }
            catch (Exception e)
            {
                ServiceState = ServiceState.ClientUnaccessible;
                StateIndicator.SetInfo($"GetText(Texts.ServiceAccessError)\n{e.Message}\n\n{e.StackTrace}");
                DeviceId = null;
                OperationInProgress = false;
                return;
            }
            finally
            {
                wcfClientFactory.CloseClient(service);
            }

            List<Package> selected = AvailablePackages.Where(p => p.IsSelected).Select(p => p.Package).ToList();

            List<PackageItem> packageItems = new List<PackageItem>();
            foreach (Package p in packages)
            {
                bool wasSelected = selected.Any(s => s.Filepath.Equals(p.Filepath, StringComparison.InvariantCultureIgnoreCase));
                packageItems.Add(new PackageItem(p, ConvertToString) { IsSelected = wasSelected });
            }

            if (!firstTimeRefresh)
            {
                NotifyAboutChanges(AvailablePackages, packageItems);
            }

            ThreadHelper.SafeInvoke(() => // collection view can be updated only from UI thread!
            {
                AvailablePackages.Clear();
                foreach (var pi in packageItems)
                {
                    AvailablePackages.Add(pi);
                }
            });

            UpdateState(clientState);

            if (firstTimeRefresh)
            {
                firstTimeRefresh = false;
                PromptIfCertificateNotAvailable();
            }

            OperationInProgress = false;
        }

        private string ConvertToString(PackageStatus status)
        {
            switch (status)
            {
                case PackageStatus.Unavailable:
                    return GetText(Texts.PackageStatusUnavailable);
                case PackageStatus.Available:
                    return GetText(Texts.PackageStatusAvailable);
                case PackageStatus.Downloading:
                    return GetText(Texts.PackageStatusDownloading);
                case PackageStatus.Downloaded:
                    return GetText(Texts.PackageStatusDownloaded);
                case PackageStatus.Installing:
                    return GetText(Texts.PackageStatusInstalling);
                case PackageStatus.Installed:
                    return GetText(Texts.PackageStatusInstalled);
                case PackageStatus.RestartNeeded:
                    return GetText(Texts.PackageStatusRestartNeeded);
                case PackageStatus.Failed:
                    return GetText(Texts.PackageStatusFailed);
                default:
                    return GetText(Texts.PackageStatusUnknown);
            }
        }

        private void PromptIfCertificateNotAvailable()
        {
            if (ServiceState == ServiceState.NoCertificate)
            {
                ThreadHelper.SafeInvoke(async () =>
                {
                    if (IsAdminMode)
                    {
                        await RequestCertificateAsync(showExplanation: true);
                    }
                    else
                    {
                        MessageBoxResult r = viewService.ShowMessageBox(GetText(Texts.DeviceNotInitialized), buttons: MessageBoxButton.OKCancel);
                        if (r == MessageBoxResult.OK)
                        {
                            ExecuteEnterAdminMode(null);
                        }
                    }
                });
            }
        }

        private void UpdateState(ClientState clientState)
        {
            switch (clientState.Status)
            {
                case ClientStatus.Running:
                    ServiceState = ServiceState.Active;
                    break;
                case ClientStatus.Stopped:
                    ServiceState = ServiceState.Error;
                    StateIndicator.SetInfo($"{GetText(Texts.CertificateNotAvailable)} {clientState.LastError}");
                    break;
                case ClientStatus.CannotAccessServer:
                    ServiceState = ServiceState.ServerUnaccessible;
                    StateIndicator.SetInfo($"{GetText(Texts.CertificateNotAvailable)} {clientState.LastError}");
                    break;
                case ClientStatus.NoCertificate:
                    ServiceState = ServiceState.NoCertificate;
                    StateIndicator.SetInfo(GetText(Texts.CertificateNotAvailable));
                    break;
                default:
                    throw new InvalidOperationException($"unsupported status {clientState.Status}");
            }
        }

        private void NotifyAboutChanges(IReadOnlyList<PackageItem> oldList, IReadOnlyList<PackageItem> newList)
        {
            IEnumerable<PackageItem> GetNewItemsWithStatus(PackageStatus status)
            {
                return newList.Where(p => p.Package.Status == status && !oldList
                                .Any(pi => pi.Package.Status == status
                                    && pi.Package.Filepath.Equals(p.Package.Filepath, StringComparison.InvariantCultureIgnoreCase)));
            }

            var newDownloaded = GetNewItemsWithStatus(PackageStatus.Downloaded).ToList();
            if (newDownloaded.Any())
            {
                TryShowToastNotification(GetText(Texts.NewPackageAvailable), newDownloaded.Select(p => p.ProductName));
            }

            var newInstalled = GetNewItemsWithStatus(PackageStatus.Installed).ToList();
            if (newInstalled.Any())
            {
                TryShowToastNotification(GetText(Texts.NewPackageInstalled), newInstalled.Select(p => p.ProductName));

            }
        }

        private void TryShowToastNotification(string title, IEnumerable<string> details = null)
        {
            try
            {
                ToastContentBuilder builder = new ToastContentBuilder().AddText(title);
                if (details != null)
                {
                    foreach (string text in details)
                    {
                        builder.AddText(text);
                    }
                }
                builder.Show();
            }
            catch
            {
                // Just ignore if cannot pop up the tost
            }
        }

        public string TaskbarIcon
        {
            get
            {
                string iconPath = null;
                switch (ServiceState)
                {
                    case ServiceState.Active:
                    case ServiceState.Unknown:
                    case ServiceState.Accessing:
                        iconPath = "/Images/Active.ico";
                        break;
                    case ServiceState.ClientUnaccessible:
                        iconPath = "/Images/ClientUnaccessible.ico";
                        break;
                    case ServiceState.NoCertificate:
                    case ServiceState.ServerUnaccessible:
                        iconPath = "/Images/ServerUnaccessible.ico";
                        break;
                    case ServiceState.Error:
                        iconPath = "/Images/Error.ico";
                        break;
                }
                return iconPath;
            }
        }

        public string TaskbarIconText
        {
            get
            {
                string extraText = string.Empty;
                switch (ServiceState)
                {
                    case ServiceState.ClientUnaccessible:
                        extraText = GetText(Texts.ClientUnaccessible);
                        break;
                    case ServiceState.NoCertificate:
                        extraText = GetText(Texts.NoCertificate);
                        break;
                    case ServiceState.ServerUnaccessible:
                        extraText = GetText(Texts.ServerUnaccessible);
                        break;
                    case ServiceState.Error:
                        extraText = GetText(Texts.AgentOrServerFilure);
                        break;
                    default:
                        break;
                }
                return "RITMS UP2DATE" + (string.IsNullOrEmpty(extraText) ? string.Empty : "\n" + extraText);
            }
        }

        private string GetText(Texts text)
        {
            return viewService.GetText(text);
        }
    }
}
