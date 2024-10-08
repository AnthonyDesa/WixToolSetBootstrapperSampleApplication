using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Prism.Mvvm;
using Microsoft.Tools.WindowsInstallerXml.Bootstrapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;

namespace InstallerUI
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class InstallerMainWindowViewModel : BindableBase
    {
        private BootstrapperApplication bootstrapper;
        private Engine engine;

        [Import] private IUIInteractionService interactionService = null;

        [ImportingConstructor]
        public InstallerMainWindowViewModel(BootstrapperApplication bootstrapper, Engine engine)
        {
            this.bootstrapper = bootstrapper;
            this.engine = engine;

            // For demo purposes, we set two variables here. They are passed on to the chained MSIs.
            engine.StringVariables["Prerequisite"] = "1";
            engine.StringVariables["InstallLevel"] = "100";
            
            // Setup commands
            this.InstallCommandValue = new DelegateCommand(
                () =>
                {
                    //if (engine.StringVariables["FirstInstallerBootStrapper"] == "Update")
                    //{
                    //    File.Delete(@"C:\\Program Files (x86)\\CustomBurnUISample\\First Installer\\FirstInstallerReadMe.txt");
                    //}
                    engine.Plan(LaunchAction.Install);
                },
                () => !this.Installing); // && this.State == InstallationState.DetectedAbsent);

            this.UninstallCommandValue = new DelegateCommand(
                () => engine.Plan(LaunchAction.Uninstall),
                () => !this.Installing); // && this.State == InstallationState.DetectedPresent);

            //this.FirstInstallerCommandValue = new DelegateCommand<string>(HandleFirstIntallCommand);

            // Setup event handlers
            bootstrapper.DetectBegin += (_, ea) =>
            {
                this.LogEvent("DetectBegin", ea);

                // Set installation state that controls the install/uninstall buttons
                this.interactionService.RunOnUIThread(
                    () => this.State =
                        ea.Installed ? InstallationState.DetectedPresent : InstallationState.DetectedAbsent);
            };
            bootstrapper.DetectRelatedBundle += (_, ea) =>
            {
                this.LogEvent("DetectRelatedBundle", ea);

                // Save flag indicating whether this is a downgrade operation
                this.interactionService.RunOnUIThread(
                    () => this.Downgrade |= ea.Operation == RelatedOperation.Downgrade);
            };
            bootstrapper.DetectComplete += (s, ea) =>
            {
                this.LogEvent("DetectComplete");
                this.DetectComplete(s, ea);
            };
            bootstrapper.PlanComplete += (_, ea) =>
            {
                this.LogEvent("PlanComplete", ea);

                // Start apply phase
                if (ea.Status >= 0 /* Success */)
                {
                    this.engine.Apply(this.interactionService.GetMainWindowHandle());
                }
            };
            bootstrapper.ApplyBegin += (_, ea) =>
            {
                this.LogEvent("ApplyBegin");

                // Set flag indicating that apply phase is running
                this.interactionService.RunOnUIThread(() => this.Installing = true);
            };
            bootstrapper.ExecutePackageBegin += (_, ea) =>
            {
                this.LogEvent("ExecutePackageBegin", ea);
                // Trigger display of currently processed package
                this.interactionService.RunOnUIThread(() => this.CurrentPackage = ea.PackageId);
            };
            bootstrapper.ExecutePackageComplete += (_, ea) =>
            {
                this.LogEvent("ExecutePackageComplete", ea);
                // Remove currently processed package
                this.interactionService.RunOnUIThread(() => this.CurrentPackage = string.Empty);
            };
            bootstrapper.ExecuteProgress += (_, ea) =>
            {
                this.LogEvent("ExecuteProgress", ea);

                // Update progress indicator
                this.interactionService.RunOnUIThread(() =>
                {
                    this.LocalProgress = ea.ProgressPercentage;
                    this.GlobalProgress = ea.OverallPercentage;
                });
            };
            bootstrapper.ApplyComplete += (_, ea) =>
            {
                this.LogEvent("ApplyComplete", ea);

                // Everything is done, let's close the installer
                this.interactionService.CloseUIAndExit();
            };
            bootstrapper.ResolveSource += (_, ea) =>
            {
                this.LogEvent("ResolveSource", ea);
                if (!File.Exists(ea.LocalSource) && !string.IsNullOrEmpty(ea.DownloadSource))
                {
                    this.LogEvent($"ResolveSource::ExistingDownloadSource={ea.DownloadSource}");
                    string newUrl = string.Format(ea.DownloadSource, "localhost");
                    this.LogEvent($"ResolveSource::NewURL={newUrl}");
                    engine.SetDownloadSource(ea.PackageOrContainerId, ea.PayloadId, newUrl, null, null);
                }

                ea.Result = Result.Download;
            };

            bootstrapper.PlanPackageBegin += (_, ea) =>
            {
                this.LogEvent(
                    $"PlanPackageBegin Selection={engine.StringVariables[ea.PackageId]} ea.State={ea.State} ea.PackageId={ea.PackageId} Command.Action {bootstrapper.Command.Action}",
                    ea);
                if (bootstrapper.Command.Action == LaunchAction.Install ||
                    bootstrapper.Command.Action == LaunchAction.Modify ||
                    bootstrapper.Command.Action == LaunchAction.Uninstall)
                {
                    //Note LaunchAction.Install and LaunchAction.UnInstall both will have bootstrapper.Commnad.Action as Install
                    if (engine.StringVariables[ea.PackageId] == "Skip")
                    {
                        this.LogEvent($"PlanPackageBegin::{ea.PackageId} ea.State is set to None...");
                        //Keep and Skip means do not install\uninstall the package
                        ea.State = RequestState.None;
                    }

                    if (engine.StringVariables[ea.PackageId] == "Keep")
                    {
                        this.LogEvent($"PlanPackageBegin::{ea.PackageId} ea.State is set to Present...");
                        //Keep and Skip means do not install\uninstall the package
                        ea.State = RequestState.Present;
                    }
                }
            };

#region sample code
            //bootstrapper.DetectRelatedMsiPackage += (_, ea) =>
            //{
            //    this.LogEvent($"DetectRelatedMsiPackage", ea);
            //    var existingPackageProductCode = ea.ProductCode;
            //    var actionToBeAppliedToExistingPackage = ea.Operation;
            //    var existingPackageId = ea.PackageId;
            //    var existingPackageVersion = ea.Version;

            //    this.LogEvent(string.Format(
            //        "Detected existing related package {0} (product: {1}) at version {2}, which will be {3}",
            //        existingPackageId, existingPackageProductCode, existingPackageVersion,
            //        actionToBeAppliedToExistingPackage));

            //    //if (actionToBeAppliedToExistingPackage == RelatedOperation.MajorUpgrade)
            //    //{

            //    //requires reference to WiX Toolset\SDK\Microsoft.Deployment.WindowsInstaller.dll
            //    var installedPackage =
            //        new Microsoft.Deployment.WindowsInstaller.ProductInstallation(existingPackageProductCode);
            //    if (!installedPackage.IsInstalled)
            //    {
            //        LogEvent(string.Format(
            //            "Migrating Package {0}, which is not installed, so marking it and it's features as Absent",
            //            existingPackageId));
            //        //TODO: add logic to store state so that during Plan phase can set package with package with product code = existingPackageProductCode to PackageState.Absent
            //    }
            //    else
            //    {
            //        LogEvent(string.Format("Migrating features for MajorUpgrade of Package {0}", existingPackageId));

            //        foreach (var currentInstallFeature in installedPackage.Features)
            //        {
            //            if (currentInstallFeature.State == InstallState.Local)
            //            {
            //                LogEvent(string.Format("Migrating feature {1} of Package {0} - marking as Present",
            //                    existingPackageId, currentInstallFeature.FeatureName));
            //                //TODO: add logic to store state so that during Plan phase can set package and feature states based on this info
            //            }
            //            else
            //            {
            //                LogEvent(string.Format("Migrating feature {1} of Package {0} - marking as Absent",
            //                    existingPackageId, currentInstallFeature.FeatureName));
            //                //TODO: add logic to store state so that during Plan phase can set package and feature states based on this info
            //            }
            //        }
            //    }
            //    //}
            //};
#endregion

            SetupEventHandlersForLogging();
        }


        private void SetupEventHandlersForLogging()
        {
            this.bootstrapper.Startup += (_, ea) => this.LogEvent("Startup");
            this.bootstrapper.Shutdown += (_, ea) => this.LogEvent("Shutdown");
            this.bootstrapper.SystemShutdown += (_, ea) => this.LogEvent("SystemShutdown", ea);
            this.bootstrapper.DetectCompatiblePackage += (_, ea) => this.LogEvent("DetectCompatiblePackage", ea);
            this.bootstrapper.DetectForwardCompatibleBundle +=
                (_, ea) => this.LogEvent("DetectForwardCompatibleBundle", ea);
            this.bootstrapper.DetectMsiFeature += (_, ea) => this.LogEvent("DetectMsiFeature", ea);
            this.bootstrapper.DetectPackageBegin += (_, ea) => this.LogEvent("DetectPackageBegin", ea);
            this.bootstrapper.DetectPackageComplete += (_, ea) => this.LogEvent("DetectPackageComplete", ea);
            this.bootstrapper.DetectPriorBundle += (_, ea) => this.LogEvent("DetectPriorBundle", ea);
            //this.bootstrapper.DetectRelatedMsiPackage += (_, ea) => this.LogEvent("DetectRelatedMsiPackage", ea);
            this.bootstrapper.DetectTargetMsiPackage += (_, ea) => this.LogEvent("DetectTargetMsiPackage", ea);
            this.bootstrapper.DetectUpdate += (_, ea) => this.LogEvent("DetectUpdate", ea);
            this.bootstrapper.DetectUpdateBegin += (_, ea) => this.LogEvent("DetectUpdateBegin", ea);
            this.bootstrapper.DetectUpdateComplete += (_, ea) => this.LogEvent("DetectUpdateComplete", ea);
            this.bootstrapper.Elevate += (_, ea) => this.LogEvent("Elevate", ea);
            this.bootstrapper.Error += (_, ea) => this.LogEvent("Error", ea);
            this.bootstrapper.ExecuteBegin += (_, ea) => this.LogEvent("ExecuteBegin", ea);
            this.bootstrapper.ExecuteComplete += (_, ea) => this.LogEvent("ExecuteComplete", ea);
            this.bootstrapper.ExecuteFilesInUse += (_, ea) => this.LogEvent("ExecuteFilesInUse", ea);
            this.bootstrapper.ExecuteMsiMessage += (_, ea) => this.LogEvent("ExecuteMsiMessage", ea);
            this.bootstrapper.ExecutePatchTarget += (_, ea) => this.LogEvent("ExecutePatchTarget", ea);
            this.bootstrapper.LaunchApprovedExeBegin += (_, ea) => this.LogEvent("LaunchApprovedExeBegin");
            this.bootstrapper.LaunchApprovedExeComplete += (_, ea) => this.LogEvent("LaunchApprovedExeComplete", ea);
            this.bootstrapper.PlanBegin += (_, ea) => this.LogEvent("PlanBegin", ea);
            this.bootstrapper.PlanCompatiblePackage += (_, ea) => this.LogEvent("PlanCompatiblePackage", ea);
            this.bootstrapper.PlanMsiFeature += (_, ea) => this.LogEvent("PlanMsiFeature", ea);
            //this.bootstrapper.PlanPackageBegin += (_, ea) => this.LogEvent("PlanPackageBegin", ea);
            this.bootstrapper.PlanPackageComplete += (_, ea) => this.LogEvent("PlanPackageComplete", ea);
            this.bootstrapper.PlanRelatedBundle += (_, ea) => this.LogEvent("PlanRelatedBundle", ea);
            this.bootstrapper.PlanTargetMsiPackage += (_, ea) => this.LogEvent("PlanTargetMsiPackage", ea);
            this.bootstrapper.Progress += (_, ea) => this.LogEvent("Progress", ea);
            this.bootstrapper.RegisterBegin += (_, ea) => this.LogEvent("RegisterBegin");
            this.bootstrapper.RegisterComplete += (_, ea) => this.LogEvent("RegisterComplete", ea);
            this.bootstrapper.RestartRequired += (_, ea) => this.LogEvent("RestartRequired", ea);
            this.bootstrapper.UnregisterBegin += (_, ea) => this.LogEvent("UnregisterBegin", ea);
            this.bootstrapper.UnregisterComplete += (_, ea) => this.LogEvent("UnregisterComplete", ea);
        }

        #region Properties for data binding

        private DelegateCommand InstallCommandValue;

        public ICommand InstallCommand
        {
            get { return this.InstallCommandValue; }
        }

        private DelegateCommand UninstallCommandValue;

        public ICommand UninstallCommand
        {
            get { return this.UninstallCommandValue; }
        }

        public ICommand FirstInstallerCommand
        {
            get { return new DelegateCommand<string>(HandleFirstIntallCommand); }
        }

        private void HandleFirstIntallCommand(object commandParameter)
        {
            engine.StringVariables["FirstInstaller"] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose,
                $"::FirstInstaller = {engine.StringVariables["FirstInstaller"]} & commandParameter={commandParameter}");
        }

        public ICommand SecondInstallerCommand
        {
            get { return new DelegateCommand<string>(HandleSecondIntallCommand); }
        }

        private void HandleSecondIntallCommand(object commandParameter)
        {
            engine.StringVariables["SecondInstaller"] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose,
                $"::SecondInstaller = {engine.StringVariables["SecondInstaller"]} & commandParameter={commandParameter}");
        }

        public ICommand ThirdInstallerCommand
        {
            get { return new DelegateCommand<string>(HandleThirdIntallCommand); }
        }

        private void HandleThirdIntallCommand(object commandParameter)
        {
            engine.StringVariables["ThirdInstaller"] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose,
                $"::ThirdInstaller = {engine.StringVariables["ThirdInstaller"]} & commandParameter={commandParameter}");
        }

        public ICommand FIBootStapperCommand
        {
            get { return new DelegateCommand<string>(HandleFIBootStapperCommand); }
        }

        private void HandleFIBootStapperCommand(object commandParameter)
        {
            engine.StringVariables["FirstInstallerBootStrapper"] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose,
                $"::FirstInstallerBootStrapper = {engine.StringVariables["FirstInstallerBootStrapper"]} & commandParameter={commandParameter}");
        }

        public ICommand SIBootStapperCommand
        {
            get { return new DelegateCommand<string>(HandleSIBootStapperCommand); }
        }

        private void HandleSIBootStapperCommand(object commandParameter)
        {
            engine.StringVariables["SecondInstallerBootStrapper"] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose,
                $"::SecondInstallerBootStrapper. = {engine.StringVariables["SecondInstallerBootStrapper"]} & commandParameter={commandParameter}");
        }

        public ICommand ApplyCommand
        {
            get { return new DelegateCommand(HandleApplyCommand); }
        }

        private void HandleApplyCommand()
        {
            //1). Check what is already installed on users computer
            //2). Check What user is Uninstalling
            //3). Check what user is Installing
            //4). Check if anything is left installed
            //5). If nothing is left installed on Client Computer then Call Uninstall otherwise call Install

            //1). Check what is already installed on users computer (Registry is not updated immediately after calling the Install command)
            var installedModules = GetModulesInstalledOnClientComputer();
            var installedModuleName = string.Join(",", installedModules.Select(x => x.Item2).ToArray());
            engine.Log(LogLevel.Verbose, $"HandleApplyCommand::Installed Modules = {installedModuleName}");

            //2). Check What user is Uninstalling
            var userSelection = new Dictionary<string, string>();
            userSelection.Add("FirstInstaller", engine.StringVariables["FirstInstaller"]);
            userSelection.Add("SecondInstaller", engine.StringVariables["SecondInstaller"]);
            userSelection.Add("ThirdInstaller", engine.StringVariables["ThirdInstaller"]);
            userSelection.Add("FourthInstaller", engine.StringVariables["FourthInstaller"]);
            userSelection.Add("FifthInstaller", engine.StringVariables["FifthInstaller"]);
            userSelection.Add("FirstInstallerBootStrapper", engine.StringVariables["FirstInstallerBootStrapper"]);
            userSelection.Add("SecondInstallerBootStrapper", engine.StringVariables["SecondInstallerBootStrapper"]);
            var unInstalledSelected = userSelection.Where(x => x.Value.ToLower() == "UnInstall".ToLower()).Select(x => x.Key).ToList();
            engine.Log(LogLevel.Verbose, $"HandleApplyCommand::unInstalledSelected Modules = {string.Join(",",unInstalledSelected.ToArray())}");

            //3). Check what user is Installing
            var InstalledSelected = userSelection.Where(x => x.Value.ToLower() == "Install".ToLower()).Select(x => x.Key).ToList();
            engine.Log(LogLevel.Verbose, $"HandleApplyCommand::InstalledSelected Modules = {string.Join(",", InstalledSelected.ToArray())}");

            engine.Log(LogLevel.Verbose, "HandleApplyCommand::Install called");
            engine.Plan(LaunchAction.Install);

            //4). If nothing is left installed on Client Computer the Call Uninstall otherwise call Install
            engine.Log(LogLevel.Verbose,$"installedModules.Count={installedModules.Count} unInstalledSelected.Count={unInstalledSelected.Count} InstalledSelected.Count={InstalledSelected.Count}" );
            if (installedModules.Count == unInstalledSelected.Count)
            {
                //User have selected to Uninstall all installed modules
                //Have user selected to Installed any new module
                if (InstalledSelected.Count == 0)
                {
                    engine.Log(LogLevel.Verbose, "HandleApplyCommand::UnInstall called");
                    engine.Plan(LaunchAction.Uninstall);
                }
            }
        }

        private InstallationState StateValue;

        public InstallationState State
        {
            get { return this.StateValue; }
            set
            {
                this.SetProperty(ref this.StateValue, value);
                this.InstallCommandValue.RaiseCanExecuteChanged();
                this.UninstallCommandValue.RaiseCanExecuteChanged();
            }
        }

        private bool DowngradeValue;

        public bool Downgrade
        {
            get { return this.DowngradeValue; }
            set { this.SetProperty(ref this.DowngradeValue, value); }
        }

        private int LocalProgressValue;

        public int LocalProgress
        {
            get { return this.LocalProgressValue; }
            set { this.SetProperty(ref this.LocalProgressValue, value); }
        }

        private int GlobalProgressValue;

        public int GlobalProgress
        {
            get { return this.GlobalProgressValue; }
            set { this.SetProperty(ref this.GlobalProgressValue, value); }
        }

        private string CurrentPackageValue;

        public string CurrentPackage
        {
            get { return this.CurrentPackageValue; }
            set { this.SetProperty(ref this.CurrentPackageValue, value); }
        }

        private bool InstallingValue;

        public bool Installing
        {
            get { return this.InstallingValue; }
            set
            {
                this.SetProperty(ref this.InstallingValue, value);
                this.InstallCommandValue.RaiseCanExecuteChanged();
                this.UninstallCommandValue.RaiseCanExecuteChanged();
            }
        }

        #endregion

        private void DetectComplete(object sender, DetectCompleteEventArgs e)
        {
            // If necessary, parse the command line string before any planning
            // (e.g. detect installation folder)
            if (LaunchAction.Uninstall == this.bootstrapper.Command.Action)
            {
                this.engine.Log(LogLevel.Verbose, "Invoking automatic plan for uninstall");
                //this.engine.Plan(LaunchAction.Uninstall);
            }
            else if (e.Status >= 0 /* Success */)
            {
                if (this.Downgrade)
                {
                    // What do you want to do in case of downgrade?
                    // Here: Stop installation

                    string message = "Sorry, we do not support downgrades.";
                    this.engine.Log(LogLevel.Verbose, message);
                    if (this.bootstrapper.Command.Display == Display.Full)
                    {
                        this.interactionService.ShowMessageBox(message);
                        this.interactionService.CloseUIAndExit();
                    }
                }

                if (this.bootstrapper.Command.Action == LaunchAction.Layout)
                {
                    // Copies all of the Bundle content to a specified directory
                    this.engine.Plan(LaunchAction.Layout);
                }
                else if (this.bootstrapper.Command.Display != Display.Full)
                {
                    // If we're not waiting for the user to click install, dispatch plan with the default action.
                    this.engine.Log(LogLevel.Verbose, "Invoking automatic plan for non-interactive mode.");
                    this.engine.Plan(LaunchAction.Install);
                }
            }
        }

        private void LogEvent(string eventName, EventArgs arguments = null)
        {
            this.engine.Log(
                LogLevel.Verbose,
                arguments == null
                    ? string.Format("EVENT: {0}", eventName)
                    : string.Format("EVENT: {0} ({1})", eventName, JsonConvert.SerializeObject(arguments)));
        }

        private List<Tuple<string, string, string>> GetModulesInstalledOnClientComputer()
        {
            List<Tuple<string, string, string>> installedModules = new List<Tuple<string, string, string>>();
            var registryView = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            //Get SciexOS Module Installed Version
            //string registryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            var roots = new string[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\", @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" };
            RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView); //Registry.LocalMachine.OpenSubKey(registryKey);
            foreach (var root in roots)
            {
                RegistryKey regKey = key.OpenSubKey(root);
                //key = Registry.LocalMachine.OpenSubKey(registryKey);
                if (regKey != null)
                {
                    foreach (String a in regKey.GetSubKeyNames())
                    {
                        RegistryKey subkey = regKey.OpenSubKey(a);
                        if (subkey.GetValue("DisplayName") != null)
                        {
                            string _softwareName = subkey.GetValue("DisplayName").ToString();
                            if (!string.IsNullOrWhiteSpace(_softwareName))
                            {
                                if (_softwareName.ToLower().Contains("FirstInstaller".ToLower())
                                    || _softwareName.ToLower().Contains("SecondInstaller".ToLower())
                                    || _softwareName.ToLower().Contains("ThirdInstaller".ToLower())
                                    || _softwareName.ToLower().Contains("FourthInstaller".ToLower())
                                    || _softwareName.ToLower().Contains("FifthInstaller".ToLower())
                                    || _softwareName.ToLower().Contains("FirstInstallerBootStrapper".ToLower())
                                    || _softwareName.ToLower().Contains("SecondInstallerBootStrapper".ToLower())
                                    )
                                {
                                    installedModules.Add(new Tuple<string, string, string>(a, _softwareName,
                                        subkey.GetValue("DisplayVersion").ToString()));
                                }
                            }
                        }
                    }
                }
            }
            return installedModules;
        }
    }
}
