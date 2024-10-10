﻿using Microsoft.Practices.Prism.Commands;
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
        private Dictionary<string,string> _userSelectionDic = new Dictionary<string,string>();

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
                    $"PlanPackageBegin Selection={_userSelectionDic[ea.PackageId]} ea.State={ea.State} ea.PackageId={ea.PackageId} Command.Action {bootstrapper.Command.Action}",
                    ea);
                if (bootstrapper.Command.Action == LaunchAction.Install ||
                    bootstrapper.Command.Action == LaunchAction.Modify ||
                    bootstrapper.Command.Action == LaunchAction.Uninstall)
                {
                    //Note LaunchAction.Install and LaunchAction.UnInstall both will have bootstrapper.Commnad.Action as Install
                    if (_userSelectionDic[ea.PackageId] == UserSelectionEnum.Skip.ToString())
                    {
                        this.LogEvent($"PlanPackageBegin::{ea.PackageId} ea.State is set to None...");
                        //Skip means do not install the package
                        ea.State = RequestState.None;
                    }

                    if (_userSelectionDic[ea.PackageId] == UserSelectionEnum.Keep.ToString())
                    {
                        this.LogEvent($"PlanPackageBegin::{ea.PackageId} ea.State is set to Present...");
                        //Keep means package is already installed do not uninstall the package
                        ea.State = RequestState.Present;
                    }

                    if (_userSelectionDic[ea.PackageId] == UserSelectionEnum.Repair.ToString())
                    {
                        this.LogEvent($"PlanPackageBegin::{ea.PackageId} ea.State is set to Repair...");
                        //Repair means package is already installed but is broken. Fix the broken package
                        ea.State = RequestState.Repair;
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
            SelectUnInstallIfInstalled();
            SelectInstallIfNotInstalled();

            //Initialize User Selection.
            Packages.GetPackageIdsAsEnum().ToList().ForEach(x => {
                if (!_userSelectionDic.ContainsKey(x.ToString()))
                    _userSelectionDic.Add(x.ToString(),engine.StringVariables[x.ToString()]);
            });
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
            string key = PackageIdEnum.FirstInstaller.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }

        public ICommand SecondInstallerCommand
        {
            get { return new DelegateCommand<string>(HandleSecondIntallCommand); }
        }

        private void HandleSecondIntallCommand(object commandParameter)
        {
            string key = PackageIdEnum.SecondInstaller.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }

        public ICommand ThirdInstallerCommand
        {
            get { return new DelegateCommand<string>(HandleThirdIntallCommand); }
        }

        private void HandleThirdIntallCommand(object commandParameter)
        {
            string key = PackageIdEnum.ThirdInstaller.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }

        public ICommand FourthInstallerCommand
        {
            get { return new DelegateCommand<string>(HandleFourthIntallCommand); }
        }

        private void HandleFourthIntallCommand(object commandParameter)
        {
            string key = PackageIdEnum.FourthInstaller.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }

        public ICommand FifthInstallerCommand
        {
            get { return new DelegateCommand<string>(HandleFifthIntallCommand); }
        }

        private void HandleFifthIntallCommand(object commandParameter)
        {
            string key = PackageIdEnum.FifthInstaller.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }

        public ICommand FIBootStapperCommand
        {
            get { return new DelegateCommand<string>(HandleFIBootStapperCommand); }
        }

        private void HandleFIBootStapperCommand(object commandParameter)
        {
            string key = PackageIdEnum.FirstInstallerBootStrapper.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }

        public ICommand SIBootStapperCommand
        {
            get { return new DelegateCommand<string>(HandleSIBootStapperCommand); }
        }

        private void HandleSIBootStapperCommand(object commandParameter)
        {
            string key = PackageIdEnum.SecondInstallerBootStrapper.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }

        public ICommand RepairCommand
        {
            get { return new DelegateCommand(HandleRepairCommand); }
        }

        private void HandleRepairCommand()
        {
            //check if any package have repair selected
            IList<string> repairSelected = new List<string>();
            if(!FirstInstallerIsRepairChecked 
               || !SecondInstallerIsRepairChecked 
               || !ThirdInstallerIsRepairChecked
               || !FourthInstallerIsRepairChecked
               || !FifthInstallerIsRepairChecked
               || !FIBootStapperInstallerIsRepairChecked
               || !SIBootStapperInstallerIsRepairChecked)
            {
                interactionService.ShowMessageBox("No package selected for Repair");
            }
            else
            {
                //If not repair then set to skip
                SetNotRepairToSkip();

                //interactionService.ShowMessageBox("Please note any package that is not set to repair is forced to skip");
                engine.Plan(LaunchAction.Repair);
            }
        }

        public ICommand ApplyCommand
        {
            get { return new DelegateCommand(HandleApplyCommand); }
        }

        private void SetNotRepairToSkip()
        {
            if (!FirstInstallerIsRepairChecked)
                FirstInstallerIsSkipChecked = true;
            if (!SecondInstallerIsRepairChecked)
                SecondInstallerIsSkipChecked = true;
            if (!ThirdInstallerIsRepairChecked)
                ThirdInstallerIsSkipChecked = true;
            if (!FourthInstallerIsRepairChecked)
                FourthInstallerIsSkipChecked = true;
            if (!FifthInstallerIsRepairChecked)
                FifthInstallerIsSkipChecked = true;
            if (!FIBootStapperInstallerIsRepairChecked)
                FIBootStapperInstallerIsSkipChecked = true;
            if (!SIBootStapperInstallerIsRepairChecked)
                SIBootStapperInstallerIsSkipChecked = true;
        }

        private void SetRepairToSkip()
        {
            if (FirstInstallerIsRepairChecked)
                FirstInstallerIsSkipChecked = true;
            if (SecondInstallerIsRepairChecked)
                SecondInstallerIsSkipChecked = true;
            if (ThirdInstallerIsRepairChecked)
                ThirdInstallerIsSkipChecked = true;
            if (FourthInstallerIsRepairChecked)
                FourthInstallerIsSkipChecked = true;
            if (FifthInstallerIsRepairChecked)
                FifthInstallerIsSkipChecked = true;
            if (FIBootStapperInstallerIsRepairChecked)
                FIBootStapperInstallerIsSkipChecked = true;
            if (SIBootStapperInstallerIsRepairChecked)
                SIBootStapperInstallerIsSkipChecked = true;
        }
        private void HandleApplyCommand()
        {
            SetRepairToSkip();
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
            Packages.GetPackageIdsAsEnum().ToList().ForEach(x => {
                userSelection.Add(x.ToString(), _userSelectionDic[x.ToString()]);
            });
            var unInstalledSelected = userSelection.Where(x => x.Value.ToLower() == UserSelectionEnum.Uninstall.ToString().ToLower()).Select(x => x.Key).ToList();
            engine.Log(LogLevel.Verbose, $"HandleApplyCommand::unInstalledSelected Modules = {string.Join(",",unInstalledSelected.ToArray())}");

            //3). Check what user is Installing
            var InstalledSelected = userSelection.Where(x => x.Value.ToLower() == UserSelectionEnum.Install.ToString().ToLower()).Select(x => x.Key).ToList();
            engine.Log(LogLevel.Verbose, $"HandleApplyCommand::InstalledSelected Modules = {string.Join(",", InstalledSelected.ToArray())}");

            //4). If nothing is left installed on Client Computer the Call Uninstall otherwise call Install
            engine.Log(LogLevel.Verbose,$"installedModules.Count={installedModules.Count} unInstalledSelected.Count={unInstalledSelected.Count} InstalledSelected.Count={InstalledSelected.Count}" );
            bool executeUninstall = false;
            if (installedModules.Count == unInstalledSelected.Count)
            {
                //User have selected to Uninstall all installed modules
                //Have user selected to Installed any new module
                if (InstalledSelected.Count == 0)
                {
                    executeUninstall = true;
                }
            }

            if (executeUninstall)
            {
                engine.Log(LogLevel.Verbose, "HandleApplyCommand::UnInstall called");
                engine.Plan(LaunchAction.Uninstall);
            }
            else
            {
                engine.Log(LogLevel.Verbose, "HandleApplyCommand::Install called");
                engine.Plan(LaunchAction.Install);
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

        #region  FirstInstaller
        
        #region IsChecked
        private bool _firstInstallerIsSkipChecked = true;
        public bool FirstInstallerIsSkipChecked
        {
            get { return this._firstInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._firstInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Skip.ToString();
                }
            }
        }
        #endregion
        
        #region Unistall
        private bool _firstInstallerIsUnInstallChecked = false;
        public bool FirstInstallerIsUnInstallChecked
        {
            get { return this._firstInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._firstInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Uninstall.ToString();
                }
            }
        }
        #endregion

        #region Repair
        private bool _firstInstallerIsRepairChecked = false;
        public bool FirstInstallerIsRepairChecked
        {
            get
            {
                return this._firstInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._firstInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Repair.ToString();
                }
            }
        }
        #endregion

        #region  IsEnabled
        private bool _firstInstallerIsInstallEnabled = true;
        public bool FirstInstallerIsInstallEnabled
        {
            get { return this._firstInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._firstInstallerIsInstallEnabled, value);
            }
        }

        private bool _firstInstallerIsUnInstallEnabled = true;
        public bool FirstInstallerIsUnInstallEnabled
        {
            get { return this._firstInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._firstInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _firstInstallerIsSkipEnabled = true;
        public bool FirstInstallerIsSkipEnabled
        {
            get { return this._firstInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._firstInstallerIsSkipEnabled, value);
            }
        }

        private bool _firstInstallerIsKeepEnabled = true;
        public bool FirstInstallerIsKeepEnabled
        {
            get { return this._firstInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._firstInstallerIsKeepEnabled, value);
            }
        }

        private bool _firstInstallerIsRepairEnabled = true;
        public bool FirstInstallerIsRepairEnabled
        {
            get { return this._firstInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._firstInstallerIsRepairEnabled, value);
            }
        }

        private bool _firstInstallerIsUpdateEnabled = true;
        public bool FirstInstallerIsUpdateEnabled
        {
            get { return this._firstInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._firstInstallerIsUpdateEnabled, value);
            }
        }
        #endregion

        #endregion

        #region SecondInstaller
        private bool _secondInstallerIsSkipChecked = true;
        public bool SecondInstallerIsSkipChecked
        {
            get { return this._secondInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._secondInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Skip.ToString();
                }
            }
        }

        private bool _secondInstallerIsUnInstallChecked = false;
        public bool SecondInstallerIsUnInstallChecked
        {
            get { return this._secondInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._secondInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Uninstall.ToString();
                }
            }
        }

        #region Repair
        private bool _secondInstallerIsRepairChecked = false;
        public bool SecondInstallerIsRepairChecked
        {
            get
            {
                return this._secondInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._secondInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Repair.ToString();
                }
            }
        }
        #endregion

        #region  IsEnabled
        private bool _secondInstallerIsInstallEnabled = true;
        public bool SecondInstallerIsInstallEnabled
        {
            get { return this._secondInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._secondInstallerIsInstallEnabled, value);
            }
        }

        private bool _secondInstallerIsUnInstallEnabled = true;
        public bool SecondInstallerIsUnInstallEnabled
        {
            get { return this._secondInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._secondInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _secondInstallerIsSkipEnabled = true;
        public bool SecondInstallerIsSkipEnabled
        {
            get { return this._secondInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._secondInstallerIsSkipEnabled, value);
            }
        }

        private bool _secondInstallerIsKeepEnabled = true;
        public bool SecondInstallerIsKeepEnabled
        {
            get { return this._secondInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._secondInstallerIsKeepEnabled, value);
            }
        }

        private bool _secondInstallerIsRepairEnabled = true;
        public bool SecondInstallerIsRepairEnabled
        {
            get { return this._secondInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._secondInstallerIsRepairEnabled, value);
            }
        }

        private bool _secondInstallerIsUpdateEnabled = true;
        public bool SecondInstallerIsUpdateEnabled
        {
            get { return this._secondInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._secondInstallerIsUpdateEnabled, value);
            }
        }
        #endregion

        #endregion

        #region  ThirdInstaller
        private bool _thirdInstallerIsSkipChecked = true;
        public bool ThirdInstallerIsSkipChecked
        {
            get { return this._thirdInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsSkipChecked, value);
                if(value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Skip.ToString();
                }
            }
        }

        private bool _thirdInstallerIsUnInstallChecked = false;
        public bool ThirdInstallerIsUnInstallChecked
        {
            get { return this._thirdInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Uninstall.ToString();
                }
            }
        }

        #region Repair
        private bool _thirdInstallerIsRepairChecked = false;
        public bool ThirdInstallerIsRepairChecked
        {
            get
            {
                return this._thirdInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Repair.ToString();
                }
            }
        }
        #endregion

        #region  IsEnabled
        private bool _thirdInstallerIsInstallEnabled = true;
        public bool ThirdInstallerIsInstallEnabled
        {
            get { return this._thirdInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsInstallEnabled, value);
            }
        }

        private bool _thirdInstallerIsUnInstallEnabled = true;
        public bool ThirdInstallerIsUnInstallEnabled
        {
            get { return this._thirdInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _thirdInstallerIsSkipEnabled = true;
        public bool ThirdInstallerIsSkipEnabled
        {
            get { return this._thirdInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsSkipEnabled, value);
            }
        }

        private bool _thirdInstallerIsKeepEnabled = true;
        public bool ThirdInstallerIsKeepEnabled
        {
            get { return this._thirdInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsKeepEnabled, value);
            }
        }

        private bool _thirdInstallerIsRepairEnabled = true;
        public bool ThirdInstallerIsRepairEnabled
        {
            get { return this._thirdInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsRepairEnabled, value);
            }
        }

        private bool _thirdInstallerIsUpdateEnabled = true;
        public bool ThirdInstallerIsUpdateEnabled
        {
            get { return this._thirdInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsUpdateEnabled, value);
            }
        }
        #endregion

        #endregion

        #region FourthInstaller
        private bool _fourthInstallerIsSkipChecked = true;
        public bool FourthInstallerIsSkipChecked
        {
            get { return this._fourthInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Skip.ToString();
                }
            }
        }

        private bool _fourthInstallerIsUnInstallChecked = false;
        public bool FourthInstallerIsUnInstallChecked
        {
            get { return this._fourthInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Uninstall.ToString();
                }
            }
        }

        #region Repair
        private bool _fourthInstallerIsRepairChecked = false;
        public bool FourthInstallerIsRepairChecked
        {
            get
            {
                return this._fourthInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Repair.ToString();
                }
            }
        }
        #endregion

        #region  IsEnabled
        private bool _fourthInstallerIsInstallEnabled = true;
        public bool FourthInstallerIsInstallEnabled
        {
            get { return this._fourthInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsInstallEnabled, value);
            }
        }

        private bool _fourthInstallerIsUnInstallEnabled = true;
        public bool FourthInstallerIsUnInstallEnabled
        {
            get { return this._fourthInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _fourthInstallerIsSkipEnabled = true;
        public bool FourthInstallerIsSkipEnabled
        {
            get { return this._fourthInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsSkipEnabled, value);
            }
        }

        private bool _fourthInstallerIsKeepEnabled = true;
        public bool FourthInstallerIsKeepEnabled
        {
            get { return this._fourthInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsKeepEnabled, value);
            }
        }

        private bool _fourthInstallerIsRepairEnabled = true;
        public bool FourthInstallerIsRepairEnabled
        {
            get { return this._fourthInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsRepairEnabled, value);
            }
        }

        private bool _fourthInstallerIsUpdateEnabled = true;
        public bool FourthInstallerIsUpdateEnabled
        {
            get { return this._fourthInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsUpdateEnabled, value);
            }
        }
        #endregion
        #endregion

        #region  FifthInstaller
        private bool _fifthInstallerIsSkipChecked = true;
        public bool FifthInstallerIsSkipChecked
        {
            get { return this._fifthInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Skip.ToString();
                }
            }
        }

        private bool _fifthInstallerIsUnInstallChecked = false;
        public bool FifthInstallerIsUnInstallChecked
        {
            get { return this._fifthInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Uninstall.ToString();
                }
            }
        }

        #region Repair
        private bool _fifthInstallerIsRepairChecked = false;
        public bool FifthInstallerIsRepairChecked
        {
            get
            {
                return this._fifthInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Repair.ToString();
                }
            }
        }
        #endregion

        #region  IsEnabled
        private bool _fifthInstallerIsInstallEnabled = true;
        public bool FifthInstallerIsInstallEnabled
        {
            get { return this._fifthInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsInstallEnabled, value);
            }
        }

        private bool _fifthInstallerIsUnInstallEnabled = true;
        public bool FifthInstallerIsUnInstallEnabled
        {
            get { return this._fifthInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _fifthInstallerIsSkipEnabled = true;
        public bool FifthInstallerIsSkipEnabled
        {
            get { return this._fifthInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsSkipEnabled, value);
            }
        }

        private bool _fifthInstallerIsKeepEnabled = true;
        public bool FifthInstallerIsKeepEnabled
        {
            get { return this._fifthInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsKeepEnabled, value);
            }
        }

        private bool _fifthInstallerIsRepairEnabled = true;
        public bool FifthInstallerIsRepairEnabled
        {
            get { return this._fifthInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsRepairEnabled, value);
            }
        }

        private bool _fifthInstallerIsUpdateEnabled = true;
        public bool FifthInstallerIsUpdateEnabled
        {
            get { return this._fifthInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsUpdateEnabled, value);
            }
        }
        #endregion
        #endregion

        #region FIBootStapper
        private bool _fIBootStapperInstallerIsSkipChecked = true;
        public bool FIBootStapperInstallerIsSkipChecked
        {
            get { return this._fIBootStapperInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._fIBootStapperInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstallerBootStrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstallerBootStrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                }
            }
        }

        private bool _fIBootStrapperInstallerIsUnInstallChecked = false;
        public bool FIBootStapperInstallerIsUnInstallChecked
        {
            get { return this._fIBootStrapperInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstallerBootStrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstallerBootStrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                }
            }
        }

        #region Repair
        private bool _fIBootStrapperInstallerIsRepairChecked = false;
        public bool FIBootStapperInstallerIsRepairChecked
        {
            get
            {
                return this._fIBootStrapperInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstallerBootStrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstallerBootStrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                }
            }
        }
        #endregion

        #region  IsEnabled
        private bool _fIBootStrapperInstallerIsInstallEnabled = true;
        public bool FIBootStrapperInstallerIsInstallEnabled //FIBootStapperInstallerIsSkipEnabled
        {
            get { return this._fIBootStrapperInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsInstallEnabled, value);
            }
        }

        private bool _fIBootStrapperInstallerIsUnInstallEnabled = true;
        public bool FIBootStrapperInstallerIsUnInstallEnabled
        {
            get { return this._fIBootStrapperInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _fIBootStrapperInstallerIsSkipEnabled = true;
        public bool FIBootStrapperInstallerIsSkipEnabled
        {
            get { return this._fIBootStrapperInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsSkipEnabled, value);
            }
        }

        private bool _fIBootStrapperInstallerIsKeepEnabled = true;
        public bool FIBootStrapperInstallerIsKeepEnabled
        {
            get { return this._fIBootStrapperInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsKeepEnabled, value);
            }
        }

        private bool _fIBootStrapperInstallerIsRepairEnabled = true;
        public bool FIBootStrapperInstallerIsRepairEnabled
        {
            get { return this._fIBootStrapperInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsRepairEnabled, value);
            }
        }

        private bool _fIBootStrapperInstallerIsUpdateEnabled = true;
        public bool FIBootStrapperInstallerIsUpdateEnabled
        {
            get { return this._fIBootStrapperInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsUpdateEnabled, value);
            }
        }
        #endregion
        #endregion

        #region SIBootStapper
        private bool _sIBootStapperInstallerIsSkipChecked = true;
        public bool SIBootStapperInstallerIsSkipChecked
        {
            get { return this._sIBootStapperInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._sIBootStapperInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstallerBootStrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstallerBootStrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                }
            }
        }

        private bool _sIBootStrapperInstallerIsUnInstallChecked = false;
        public bool SIBootStapperInstallerIsUnInstallChecked
        {
            get { return this._sIBootStrapperInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstallerBootStrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstallerBootStrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                }
            }
        }

        #region Repair
        private bool _sIBootStrapperInstallerIsRepairChecked = false;
        public bool SIBootStapperInstallerIsRepairChecked
        {
            get
            {
                return this._sIBootStrapperInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstallerBootStrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstallerBootStrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                }
            }
        }
        #endregion
        #endregion

        #region  IsEnabled
        private bool _sIBootStrapperInstallerIsInstallEnabled = true;
        public bool SIBootStrapperInstallerIsInstallEnabled
        {
            get { return this._sIBootStrapperInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsInstallEnabled, value);
            }
        }

        private bool _sIBootStrapperInstallerIsUnInstallEnabled = true;
        public bool SIBootStrapperInstallerIsUnInstallEnabled
        {
            get { return this._sIBootStrapperInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _sIBootStrapperInstallerIsSkipEnabled = true;
        public bool SIBootStrapperInstallerIsSkipEnabled
        {
            get { return this._sIBootStrapperInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsSkipEnabled, value);
            }
        }

        private bool _sIBootStrapperInstallerIsKeepEnabled = true;
        public bool SIBootStrapperInstallerIsKeepEnabled
        {
            get { return this._sIBootStrapperInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsKeepEnabled, value);
            }
        }

        private bool _sIBootStrapperInstallerIsRepairEnabled = true;
        public bool SIBootStrapperInstallerIsRepairEnabled
        {
            get { return this._sIBootStrapperInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsRepairEnabled, value);
            }
        }

        private bool _sIBootStrapperInstallerIsUpdateEnabled = true;
        public bool SIBootStrapperInstallerIsUpdateEnabled
        {
            get { return this._sIBootStrapperInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsUpdateEnabled, value);
            }
        }
        #endregion

        #endregion
        private void SelectInstallIfNotInstalled()
        {
            //Select all packages that are installed on client's computer
            var installedPackages = GetModulesInstalledOnClientComputer();
            var installedPackagesName = installedPackages.Select(x => x.Item2).ToList();
            var packageIds = Packages.GetPackageIdsAsEnum().Select(x => x.ToString()).ToList(); 

            engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::PackageIds1={string.Join(",",packageIds.ToArray())}");

            //Remove all packages that are installed on client's computer
            installedPackagesName.ForEach(x =>
            {
                engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Installed Package Name = {x}");
                if (!packageIds.Remove(x))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Remove Operation failed for {x}");
                }
            });
            engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::PackageIds2={string.Join(",", packageIds.ToArray())}");


            //Packages that are not installed on client's computer
            foreach (string x in packageIds)
            {
                engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Not Installed Package Name = {x}");
                if (x.ToLower().Equals(PackageIdEnum.FirstInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    FirstInstallerIsUnInstallEnabled = false;
                    FirstInstallerIsKeepEnabled = false;
                    FirstInstallerIsRepairEnabled = false;
                    FirstInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.SecondInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    SecondInstallerIsUnInstallEnabled = false;
                    SecondInstallerIsKeepEnabled = false;
                    SecondInstallerIsRepairEnabled = false;
                    SecondInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.ThirdInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    ThirdInstallerIsUnInstallEnabled = false;
                    ThirdInstallerIsKeepEnabled = false;
                    ThirdInstallerIsRepairEnabled = false;
                    ThirdInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FourthInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    FourthInstallerIsUnInstallEnabled = false;
                    FourthInstallerIsKeepEnabled = false;
                    FourthInstallerIsRepairEnabled = false;
                    FourthInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FifthInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    FifthInstallerIsUnInstallEnabled = false;
                    FifthInstallerIsKeepEnabled = false;
                    FifthInstallerIsRepairEnabled = false;
                    FifthInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FirstInstallerBootStrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    FIBootStrapperInstallerIsUnInstallEnabled = false;
                    FIBootStrapperInstallerIsKeepEnabled = false;
                    FIBootStrapperInstallerIsRepairEnabled = false;
                    FIBootStrapperInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.SecondInstallerBootStrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    SIBootStrapperInstallerIsUnInstallEnabled = false;
                    SIBootStrapperInstallerIsKeepEnabled = false;
                    SIBootStrapperInstallerIsRepairEnabled = false;
                    SIBootStrapperInstallerIsUpdateEnabled = false;
                }
            }
        }

        private void SelectUnInstallIfInstalled()
        {
            //Select all packages that are installed on client's computer
            var installedPackages = GetModulesInstalledOnClientComputer();
            var installedPackagesName = installedPackages.Select(x => x.Item2).ToList();
            installedPackagesName.ForEach(x =>
            {
                engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:Installed Package Name = {x}");
                if (x.ToLower().Equals(PackageIdEnum.FirstInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:1Installed Package Name = {x}");
                    FirstInstallerIsUnInstallChecked = true;
                    FirstInstallerIsInstallEnabled = false;
                    FirstInstallerIsSkipEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.SecondInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:2Installed Package Name = {x}");
                    SecondInstallerIsUnInstallChecked = true;
                    SecondInstallerIsInstallEnabled = false;
                    SecondInstallerIsSkipEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.ThirdInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:3Installed Package Name = {x}");
                    ThirdInstallerIsUnInstallChecked = true;
                    ThirdInstallerIsInstallEnabled = false;
                    ThirdInstallerIsSkipEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FourthInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:4Installed Package Name = {x}");
                    FourthInstallerIsUnInstallChecked = true;
                    FourthInstallerIsInstallEnabled = false;
                    FourthInstallerIsSkipEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FifthInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:5Installed Package Name = {x}");
                    FifthInstallerIsUnInstallChecked = true;
                    FifthInstallerIsInstallEnabled = false;
                    FifthInstallerIsSkipEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FirstInstallerBootStrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:6Installed Package Name = {x}");
                    FIBootStapperInstallerIsUnInstallChecked = true;
                    FIBootStrapperInstallerIsInstallEnabled = false;
                    FIBootStrapperInstallerIsSkipEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.SecondInstallerBootStrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:7Installed Package Name = {x}");
                    SIBootStapperInstallerIsUnInstallChecked = true;
                    SIBootStrapperInstallerIsInstallEnabled = false;
                    SIBootStrapperInstallerIsSkipEnabled = false;
                }
            });
        }
        private void DetectComplete(object sender, DetectCompleteEventArgs e)
        {
            // If necessary, parse the command line string before any planning
            // (e.g. detect installation folder)
            if (LaunchAction.Uninstall == this.bootstrapper.Command.Action)
            {
                this.engine.Log(LogLevel.Verbose, "Invoking automatic plan for uninstall");
                SelectUnInstallIfInstalled();
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
                                if (Packages.GetPackageIdsAsEnum().ToList()
                                    .Where(x => x.ToString().Contains(_softwareName)).Any())
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
