﻿using System.ComponentModel;
using Up2dateConsole.Dialogs.RequestCertificate;
using Up2dateConsole.Dialogs.Settings;
using Up2dateConsole.Helpers;
using Up2dateConsole.Helpers.InactivityMonitor;
using Up2dateConsole.ViewService;

namespace Up2dateConsole
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            IViewService viewService = new ViewService.ViewService();
            viewService.RegisterDialog(typeof(RequestCertificateDialogViewModel), typeof(RequestCertificateDialog));
            viewService.RegisterDialog(typeof(SettingsDialogViewModel), typeof(SettingsDialog));

            IWcfClientFactory wcfClientFactory = new WcfClientFactory();
            ISettings settings = new Settings();
            DataContext = new MainWindowViewModel(viewService, wcfClientFactory, new HookMonitor(false), settings);

            if (!CommandLineHelper.IsPresent(CommandLineHelper.VisibleMainWindowCommand))
            {
                Hide();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            var viewModel = DataContext as MainWindowViewModel;
            if (viewModel != null && viewModel.IsAdminMode && Properties.Settings.Default.LeaveAdminModeOnClose)
            {
                viewModel.LeaveAdminModeCommand.Execute(this);
                return;
            }

            Hide();
            e.Cancel = true;
            base.OnClosing(e);
        }
    }
}
