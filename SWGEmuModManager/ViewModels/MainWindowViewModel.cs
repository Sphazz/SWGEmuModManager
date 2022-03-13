﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Toolkit.Mvvm.Input;
using SWGEmuModManager.Models;
using SWGEmuModManager.Util;
using SWGEmuModManager.Views;
using Application = System.Windows.Application;

namespace SWGEmuModManager.ViewModels
{
    internal class MainWindowViewModel : MainWindowViewModelProperties
    {
        public IAsyncRelayCommand GenerateModManifestMenuItem { get; }
        public IRelayCommand SetSwgDirectoryMenuItem { get; set; }
        public IAsyncRelayCommand DownloadModButton { get; }
        public bool ConflictContinue { get; set; }
        public IRelayCommand CloseButton { get; set; }

        public MainWindowViewModel()
        {
            Task.Run(InitializeAsync);

            GenerateModManifestMenuItem = new AsyncRelayCommand(GenerateModManifestAsync);
            SetSwgDirectoryMenuItem = new RelayCommand(SetSwgDirectory);
            DownloadModButton = new AsyncRelayCommand<int>(GetModDataAsync);
            CloseButton = new RelayCommand(() => Environment.Exit(0));

            MainWindowModel.OnDownloadProgressUpdated += DownloadProgressUpdated;
            ZipArchiveExtension.OnInstallStarted += InstallStarted;
            ZipArchiveExtension.OnInstallProgressUpdated += InstallProgressUpdated;
            ZipArchiveExtension.OnInstallDone += InstallDone;
            MainWindowModel.OnUninstallDone += UninstallDone;
            ConflictDialogWindowViewModel.ClickedContinueButton += ClickedContinueButton;
            ConflictDialogWindowViewModel.ClickedCancelButton += ClickedCancelButton;
        }

        private async Task InitializeAsync()
        {
            ProgressBarVisibility = Visibility.Collapsed;
            await RefreshModDisplay();
            InstallButtonText = "Install";
        }

        private async Task GenerateModManifestAsync()
        {
            using FolderBrowserDialog dialog = new();
            DialogResult result = dialog.ShowDialog();

            if (result.ToString().Trim() == "OK")
            {
                await ManifestGenerator.GenerateModManifestAsync(modsDirectory: dialog.SelectedPath.Replace(oldValue: "\\", newValue: "/"));
            }
        }

        private void SetSwgDirectory()
        {
            MainWindowModel.SetSwgDirectory();
        }

        private async Task GetModDataAsync(int id)
        {
            ConfigFile config = ConfigFile.GetConfig()!;

            // Uninstall mod
            if (MainWindowModel.ModIsInstalled(id))
            {
                UninstallRequestResponse uninstallResponse = await ApiHandler.UninstallModAsync(id);

                /*List<int> allowedConflicts = MainWindowModel.CheckConflictList(uninstallResponse.ConflictList);

                allowedConflicts.ForEach(conflict =>
                {
                    Trace.WriteLine(conflict);
                });*/

                ProgressBarVisibility = Visibility.Visible;

                if (!string.IsNullOrEmpty(config.SwgDirectory))
                {
                    if (uninstallResponse.Result == "Success")
                    {
                        if (uninstallResponse.FileList!.Count > 0)
                        {
                            ProgressBarStatusLabel = $"Uninstalling {ModList!.Where(x => x.Id == id).Select(x => x.Name).FirstOrDefault()}...";
                            await MainWindowModel.UninstallMod(id, uninstallResponse.FileList);
                        }
                    }
                    else
                    {
                        System.Windows.Forms.MessageBox.Show(text: $"Uninstall request response: {uninstallResponse.Result} - Reason: {uninstallResponse.Reason}",
                            caption: "Error!", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        App.log.Error(message: $"Uninstall request response: {uninstallResponse.Result} - Reason: {uninstallResponse.Reason}");
                    }
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show(text: "No SWG directory set! Please set your SWG location in Config -> Set SWG Directory and try again.",
                        caption: "No SWG Directory Set", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }

                return;
            }

            // Install Mod
            InstallRequestResponse installResponse = await ApiHandler.InstallModAsync(id);

            new ConflictDialogWindow(
                MainWindowModel.GetConflictNames(installResponse.ConflictList!, ModList!, id))
                .ShowDialog();

            if (!ConflictContinue) return;

            ProgressBarVisibility = Visibility.Visible;

            if (!string.IsNullOrEmpty(config.SwgDirectory) && 
                    !string.IsNullOrEmpty(installResponse.DownloadUrl) && 
                    !string.IsNullOrEmpty(installResponse.Archive))
            {
                if (installResponse.Result == "Success")
                {
                    ProgressBarStatusLabel = $"Downloading {ModList!.Where(x => x.Id == id).Select(x => x.Name).FirstOrDefault()}...";
                    await MainWindowModel.DownloadModAsync(id, installResponse.DownloadUrl, installResponse.Archive);
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show(text: $"Install request response: {installResponse.Result} - Reason: {installResponse.Reason}",
                        caption: "Error!", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    App.log.Error(message: $"Install request response: {installResponse.Result} - Reason: {installResponse.Reason}");
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show(text: "No SWG directory set! Please set your SWG location in Config -> Set SWG Directory and try again.",
                    caption: "No SWG Directory Set", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void DownloadProgressUpdated(long bytesReceived, long totalBytesToReceive, int progressPercentage)
        {
            ProgressBarPercentage = progressPercentage;
        }

        private void InstallStarted()
        {
            ProgressBarStatusLabel = ProgressBarStatusLabel!.Replace(oldValue: "Downloading", newValue: "Installing");
        }

        private void InstallProgressUpdated(int currentFile, int totalFiles)
        {
            ProgressBarPercentage = (currentFile / totalFiles) * 1000;
        }

        private async void InstallDone()
        {
            System.Threading.Thread.Sleep(millisecondsTimeout: 1000);
            ProgressBarVisibility = Visibility.Collapsed;

            await RefreshModDisplay();
        }

        private async void UninstallDone()
        {
            System.Threading.Thread.Sleep(millisecondsTimeout: 1000);
            ProgressBarVisibility = Visibility.Collapsed;

            await RefreshModDisplay();
        }

        private async Task RefreshModDisplay()
        {
            ModList = await MainWindowModel.SetModDisplay(await ApiHandler.GetModsAsync(1, 10));
        }

        private void ClickedContinueButton()
        {
            ConflictContinue = true;
        }

        private void ClickedCancelButton()
        {
            ConflictContinue = false;
        }
    }
}
