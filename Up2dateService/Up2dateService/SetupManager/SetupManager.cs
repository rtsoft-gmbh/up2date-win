﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Up2dateService.ErrorCodes;
using Up2dateShared;

namespace Up2dateService.SetupManager
{
    public class SetupManager : ISetupManager
    {
        private const string MsiExtension = ".msi";
        private const string NugetExtension = ".nupkg";
        private const string ExternalInstallLog = "install.out";

        private const int MillisecondsToWait = 1000;

        private const int MsiExecResult_Success = 0;
        private const int MsiExecResult_RestartNeeded = 3010;

        private static readonly List<string> AllowedExtensions = new List<string>
        {
            MsiExtension, NugetExtension
        };

        private readonly Func<string> downloadLocationProvider;
        private readonly EventLog eventLog;

        private readonly Action<Package, int> onSetupFinished;
        private readonly List<Package> packages = new List<Package>();
        private readonly object packagesLock = new object();

        public SetupManager(EventLog eventLog, Action<Package, int> onSetupFinished,
            Func<string> downloadLocationProvider)
        {
            this.eventLog = eventLog ?? throw new ArgumentNullException(nameof(eventLog));
            this.onSetupFinished = onSetupFinished;
            this.downloadLocationProvider = downloadLocationProvider ??
                                            throw new ArgumentNullException(nameof(downloadLocationProvider));

            RefreshPackageList();
        }

        public List<Package> GetAvaliablePackages()
        {
            RefreshPackageList();
            return SafeGetPackages();
        }

        public async Task InstallPackagesAsync(IEnumerable<Package> packages)
        {
            await InstallPackages(packages);
        }

        public bool IsPackageAvailable(string packageFile)
        {
            RefreshPackageList();
            return FindPackage(packageFile).Status != PackageStatus.Unavailable;
        }

        public bool IsPackageInstalled(string packageFile)
        {
            RefreshPackageList();
            var package = FindPackage(packageFile);
            return package.Status == PackageStatus.Installed || package.Status == PackageStatus.RestartNeeded;
        }

        public bool InstallPackage(string packageFile)
        {
            var package = FindPackage(packageFile);
            if (package.Status == PackageStatus.Unavailable) return false;
            int exitCode;
            if (package.Filepath.Contains(NugetExtension))
                try
                {
                    InstallChocoNupkg(package);
                    exitCode = 0;
                }
                catch (Exception)
                {
                    return false;
                }
            else
                exitCode = InstallPackageAsync(package, CancellationToken.None).Result;


            RefreshPackageList();
            return exitCode == 0;
        }

        public void OnDownloadStarted(string artifactFileName)
        {
            // add temporary "downloading" package item
            var package = new Package
            {
                Status = PackageStatus.Downloading,
                Filepath = Path.Combine(downloadLocationProvider(), artifactFileName)
            };
            SafeAddOrUpdatePackage(package);
        }

        public void OnDownloadFinished(string artifactFileName)
        {
            // remove temporary "downloading" package item, so refresh would be able to add "downloaded" package item instead
            SafeRemovePackage(Path.Combine(downloadLocationProvider(), artifactFileName), PackageStatus.Downloading);
            RefreshPackageList();
        }

        private int InstallChocoNupkg(Package package)
        {
            var logDirectory = downloadLocationProvider() + @"\install\";
            try
            {
                try
                {
                    if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, true);

                    Directory.CreateDirectory(logDirectory ?? throw new InvalidOperationException());
                }
                catch (Exception)
                {
                    return (int)InstallChocoNupkgErrors.FailedToCreateDirectory;
                }

                string packageId;
                string packageVersion;
                using (var zipFile = ZipFile.OpenRead(package.Filepath))
                {
                    try
                    {
                        var nuspec = zipFile.Entries.First(zipArchiveEntry => zipArchiveEntry.Name.Contains(".nuspec"));
                        using (var nuspecStream = nuspec.Open())
                        {
                            using (var sr = new StreamReader(nuspecStream, Encoding.UTF8))
                            {
                                var xmlData = sr.ReadToEnd();
                                var doc = new XmlDocument();
                                doc.LoadXml(xmlData);
                                packageId = doc.GetElementsByTagName("id")[0].InnerText;
                                packageVersion = doc.GetElementsByTagName("version")[0].InnerText;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        return (int)InstallChocoNupkgErrors.FailedToExtractNupkg;
                    }
                }

                // TODO: Possible will find better way to work with
                var chocoInstallCommand =
                    $@"Start-Process -FilePath powershell -ArgumentList('Start-Process -FilePath choco -ArgumentList(''install {packageId} --version {packageVersion} -s {downloadLocationProvider()} -y --force --no-progress'') -RedirectStandardOutput ""{logDirectory}\{ExternalInstallLog}""') -RedirectStandardOutput ""{logDirectory}\{ExternalInstallLog}""";
                var ps = PowerShell.Create();
                ps.AddScript(chocoInstallCommand);
                ps.Invoke<string>();
                var chocoProcesses = Process.GetProcessesByName("choco.exe").ToList();
                while (chocoProcesses.Count > 0)
                {
                    // TODO: Possibly need to change
                    Thread.Sleep(MillisecondsToWait);
                    chocoProcesses = Process.GetProcessesByName("choco.exe").ToList();
                }

                var installed = false;
                try
                {
                    using (var reader = File.OpenText($"{logDirectory}\\{ExternalInstallLog}"))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                            if (line.Contains("Chocolatey installed 1/1 packages. "))
                                installed = true;
                    }
                }
                catch
                {
                    return (int)InstallChocoNupkgErrors.FailedToInstallNupkg;
                }

                if (!installed) return (int)InstallChocoNupkgErrors.FailedToInstallNupkg;
                return (int)InstallChocoNupkgErrors.Ok;
            }
            catch (Exception)
            {
                return (int)InstallChocoNupkgErrors.FailedToWriteDateToArchive;
            }
        }

        private Package FindPackage(string packageFile)
        {
            return SafeGetPackages().FirstOrDefault(p =>
                Path.GetFileName(p.Filepath).Equals(packageFile, StringComparison.InvariantCultureIgnoreCase));
        }

        private List<Package> SafeGetPackages()
        {
            List<Package> lockedPackages;
            lock (packagesLock)
            {
                lockedPackages = packages.ToList();
            }

            ;
            return lockedPackages;
        }

        private void SafeUpdatePackages(IEnumerable<Package> newPackageList)
        {
            lock (packagesLock)
            {
                packages.Clear();
                packages.AddRange(newPackageList);
            }

            ;
        }

        private void SafeUpdatePackage(Package package)
        {
            lock (packagesLock)
            {
                var original = packages.FirstOrDefault(p =>
                    p.Filepath.Equals(package.Filepath, StringComparison.InvariantCultureIgnoreCase));
                if (original.Status != PackageStatus.Unavailable) packages[packages.IndexOf(original)] = package;
            }

            ;
        }

        private void SafeRemovePackage(string filepath, PackageStatus status)
        {
            lock (packagesLock)
            {
                var package = packages.FirstOrDefault(p =>
                    p.Status == status && p.Filepath.Equals(filepath, StringComparison.InvariantCultureIgnoreCase));
                if (package.Status != PackageStatus.Unavailable) packages.Remove(package);
            }

            ;
        }

        private void SafeAddOrUpdatePackage(Package package)
        {
            lock (packagesLock)
            {
                var original = packages.FirstOrDefault(p =>
                    p.Filepath.Equals(package.Filepath, StringComparison.InvariantCultureIgnoreCase));
                if (original.Status == PackageStatus.Unavailable)
                    packages.Add(package);
                else
                    packages[packages.IndexOf(original)] = package;
            }

            ;
        }

        private async Task InstallPackages(IEnumerable<Package> packagesToInstall)
        {
            foreach (var inPackage in packagesToInstall)
            {
                if (!AllowedExtensions.Contains(Path.GetExtension(inPackage.Filepath),
                        StringComparer.InvariantCultureIgnoreCase))
                    continue;

                var lockedPackages = SafeGetPackages();

                var package = lockedPackages.FirstOrDefault(p =>
                    p.Filepath.Equals(inPackage.Filepath, StringComparison.InvariantCultureIgnoreCase));
                if (package.Status == PackageStatus.Unavailable) continue;

                package.ErrorCode = 0;
                package.Status = PackageStatus.Installing;

                SafeUpdatePackage(package);
                var result = package.Filepath.Contains(NugetExtension)
                    ? InstallChocoNupkg(package)
                    : InstallPackageAsync(package, CancellationToken.None).Result;

                UpdatePackageStatus(ref package, result);
                eventLog.WriteEntry(
                    $"{Path.GetFileName(package.Filepath)} installation finished with result: {package.Status}");
                onSetupFinished?.Invoke(package, result);

                SafeUpdatePackage(package);
            }
        }

        private void UpdatePackageStatus(ref Package package, int result)
        {
            switch (result)
            {
                case MsiExecResult_Success:
                    package.Status = PackageStatus.Installed;
                    break;
                case MsiExecResult_RestartNeeded:
                    package.Status = PackageStatus.RestartNeeded;
                    break;
                default:
                    package.Status = PackageStatus.Failed;
                    break;
            }

            package.ErrorCode = result;
        }

        private Task<int> InstallPackageAsync(Package package, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                const int cancellationCheckPeriodMs = 1000;

                using (var p = new Process())
                {
                    p.StartInfo.FileName = "msiexec.exe";
                    p.StartInfo.Arguments = $"/i \"{package.Filepath}\" ALLUSERS=1 /qn";
                    p.StartInfo.UseShellExecute = false;
                    //p.StartInfo.RedirectStandardOutput = true;
                    _ = p.Start();
                    //string output = p.StandardOutput.ReadToEnd();

                    do
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    } while (!p.WaitForExit(cancellationCheckPeriodMs));

                    return p.ExitCode;
                }
            }, cancellationToken);
        }

        private void RefreshPackageList()
        {
            var lockedPackages = SafeGetPackages();

            var msiFolder = downloadLocationProvider();
            var files = Directory.GetFiles(msiFolder).ToList();

            var packagesToRemove = lockedPackages.Where(p =>
                p.Status != PackageStatus.Downloading &&
                !files.Any(f => p.Filepath.Equals(f, StringComparison.InvariantCultureIgnoreCase))).ToList();
            foreach (var package in packagesToRemove) _ = lockedPackages.Remove(package);

            foreach (var file in files)
            {
                var package = lockedPackages.FirstOrDefault(p =>
                    p.Filepath.Equals(file, StringComparison.InvariantCultureIgnoreCase));
                if (!lockedPackages.Contains(package))
                {
                    package.Filepath = file;
                    var info = MsiHelper.GetInfo(file);
                    package.ProductCode = info?.ProductCode;
                    package.ProductName = info?.ProductName;
                    lockedPackages.Add(package);
                    package.Status = PackageStatus.Downloaded;
                }
            }

            var installationChecker = new ProductInstallationChecker();


            for (var i = 0; i < lockedPackages.Count; i++)
            {
                var updatedPackage = lockedPackages[i];
                if (installationChecker.IsPackageInstalled(updatedPackage.ProductCode))
                {
                    installationChecker.UpdateInfo(ref updatedPackage);
                    updatedPackage.Status = PackageStatus.Installed;
                }
                else
                {
                    updatedPackage.DisplayName = null;
                    updatedPackage.Publisher = null;
                    updatedPackage.DisplayVersion = null;
                    updatedPackage.Version = null;
                    updatedPackage.InstallDate = null;
                    updatedPackage.EstimatedSize = null;
                    updatedPackage.UrlInfoAbout = null;
                    if (updatedPackage.Status != PackageStatus.Downloading &&
                        updatedPackage.Status != PackageStatus.Installing)
                        updatedPackage.Status = PackageStatus.Downloaded;
                }

                if (updatedPackage.Filepath.Contains(NugetExtension))
                    using (var zipFile = ZipFile.OpenRead(updatedPackage.Filepath))
                    {
                        var nuspec = zipFile.Entries.First(zipArchiveEntry => zipArchiveEntry.Name.Contains(".nuspec"));
                        string packageId;
                        using (var nuspecStream = nuspec.Open())
                        {
                            using (var sr = new StreamReader(nuspecStream, Encoding.UTF8))
                            {
                                var xmlData = sr.ReadToEnd();
                                var doc = new XmlDocument();
                                doc.LoadXml(xmlData);
                                packageId = doc.GetElementsByTagName("id")[0].InnerText;
                                updatedPackage.DisplayName = doc.GetElementsByTagName("title")[0].InnerText;
                                updatedPackage.ProductName = doc.GetElementsByTagName("title")[0].InnerText;
                                updatedPackage.DisplayVersion = doc.GetElementsByTagName("version")[0].InnerText;
                                updatedPackage.Publisher = doc.GetElementsByTagName("authors")[0].InnerText;
                            }
                        }

                        using (var ps = PowerShell.Create())
                        {
                            const string psCommand = @"choco list -li";
                            ps.AddScript(psCommand);
                            var result = ps.Invoke<string>();
                            if (result.Any(item => item.Contains(packageId)))
                            {
                                updatedPackage.Status = PackageStatus.Installed;
                            }
                        }
                    }

                lockedPackages[i] = updatedPackage;
            }

            SafeUpdatePackages(lockedPackages);
        }
    }
}