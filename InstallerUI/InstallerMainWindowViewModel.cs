﻿using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Prism.Mvvm;
using Microsoft.Tools.WindowsInstallerXml.Bootstrapper;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Xml.Serialization;
using Microsoft.Win32;
using InstallerUI.Api;
using OperatingSystemEnvironment = System.Environment;

namespace InstallerUI
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class InstallerMainWindowViewModel : BindableBase
    {
        private BootstrapperApplication bootstrapper;
        private Engine engine;
        private CheckForUpdatesResponse _apiResponse; 
        private Dictionary<string,string> _userSelectionDic = new Dictionary<string,string>();
        private Dictionary<string, string> _originalVersion = new Dictionary<string, string>();

        [Import] private IUIInteractionService interactionService = null;

        [ImportingConstructor]
        public InstallerMainWindowViewModel(BootstrapperApplication bootstrapper, Engine engine)
        {
            try
            {
                this.bootstrapper = bootstrapper;
                this.engine = engine;
                //var request = GetApiRequest();
                _apiResponse = CheckForUpdates.CheckForUpdateInformation(null);

                //Original Version shipped with Setup
                _originalVersion.Add(PackageIdEnum.FirstInstallerBootstrapper.ToString(), "1.0.0.0");
                _originalVersion.Add(PackageIdEnum.SecondInstallerBootstrapper.ToString(), "1.0.0.0");
                _originalVersion.Add(PackageIdEnum.ThirdInstallerBootstrapper.ToString(), "1.0.0.0");
                _originalVersion.Add(PackageIdEnum.FourthInstallerBootstrapper.ToString(), "1.0.0.0");
                _originalVersion.Add(PackageIdEnum.FifthInstallerBootstrapper.ToString(), "1.0.0.0");
                _originalVersion.Add(PackageIdEnum.SixthInstallerBootstrapper.ToString(), "1.0.0.0");
                _originalVersion.Add(PackageIdEnum.FirstInstaller.ToString(), "1.0.0.0");
                _originalVersion.Add(PackageIdEnum.SecondInstaller.ToString(), "1.0.0.0");
                _originalVersion.Add(PackageIdEnum.ThirdInstaller.ToString(), "1.0.0.0");
                _originalVersion.Add(PackageIdEnum.FourthInstaller.ToString(), "1.0.0.0");
                _originalVersion.Add(PackageIdEnum.FifthInstaller.ToString(), "1.0.0.0");

                // For demo purposes, we set two variables here. They are passed on to the chained MSIs.
                engine.StringVariables["Prerequisite"] = "1";
                engine.StringVariables["InstallLevel"] = "100";
                if(engine.StringVariables.Contains("PackagesToShow"))
                    engine.Log(LogLevel.Verbose, $"PackagesToShow={engine.StringVariables["PackagesToShow"]}");
                engine.Log(LogLevel.Verbose, "Setting Default to skip for StringVariable For Action");
                Packages.GetPackageIdsAsEnum().ToList().ForEach(x =>
                {
                    engine.StringVariables[x.ToString()] = UserSelectionEnum.Skip.ToString();
                });
                engine.Log(LogLevel.Verbose, "Setting Default to skip for StringVariable For Action");

                // Setup commands
                this.InstallCommandValue = new DelegateCommand(
                    () => { engine.Plan(LaunchAction.Install); },
                    () => !this.Installing); // && this.State == InstallationState.DetectedAbsent);

                this.UninstallCommandValue = new DelegateCommand(
                    () => engine.Plan(LaunchAction.Uninstall),
                    () => !this.Installing); // && this.State == InstallationState.DetectedPresent);

                //this.FirstInstallerCommandValue = new DelegateCommand<string>(HandleFirstIntallCommand);

                RepairCommandValue = new DelegateCommand(HandleRepairCommand, CanRepairCommandExecute);
                ApplyCommandValue = new DelegateCommand(HandleApplyCommand, CanApplyCommandExecute);
                ShowNewModulesCommandValue =
                    new DelegateCommand(HandleShowNewModulesCommand, CanShowNewModulesCommandExecute);

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
                    this.LogEvent("ResolveSource::", ea);
                    if (!File.Exists(ea.LocalSource) && !string.IsNullOrEmpty(ea.DownloadSource))
                    {
                        //Get Original Version shipped with Setup
                        string version = "1.0.0.0";
                        if (_originalVersion.ContainsKey(ea.PayloadId))
                            version = _originalVersion[ea.PayloadId];
                        string server = "pc-swd-1455.absciexdev.local";
                        string downloadFileNameWithExtension = $"{ea.PayloadId}.exe";
                        engine.Log(LogLevel.Verbose,
                            $"ResolveSource::version={version} server={server} downloadFileNameWithExtension={downloadFileNameWithExtension}");
                        //Check if user have selected to update the package
                        if (_userSelectionDic.Where(x => x.Key.ToLower() == ea.PayloadId.ToLower()
                                                         && x.Value.ToLower() ==
                                                         UserSelectionEnum.Update.ToString().ToLower()).Any())
                        {
                            //Get Version from Api Response
                            engine.Log(LogLevel.Verbose, $"ResolveSource::Getting version from api...");
                            var apiResponse = _apiResponse.AvailableUpdates
                                .Where(x => x.PackageId.ToString().ToLower() == ea.PayloadId.ToLower())
                                .FirstOrDefault();
                            if (apiResponse != null)
                            {
                                version = apiResponse.Version;
                                server = apiResponse.ServerName;
                                downloadFileNameWithExtension = apiResponse.DownloadFileNameWithExtension;
                                engine.Log(LogLevel.Verbose,
                                    $"ResolveSource::New=>version={version} server={server} downloadFileNameWithExtension={downloadFileNameWithExtension}");
                            }
                        }
                        else if (_userSelectionDic.Where(x => x.Key.ToLower() == ea.PayloadId.ToLower()
                                                              && x.Value.ToLower() != UserSelectionEnum.Install
                                                                  .ToString().ToLower()).Any())
                        {
                            version = GetInstalledVersion(ea.PayloadId);
                            this.LogEvent($"ResolveSource::GetInstalledVersion={version}");

                        }
                        else
                        {
                            version = "1.0.0.0";
                        }

                        this.LogEvent($"ResolveSource::ExistingDownloadSource={ea.DownloadSource}");
                        //string newUrl = string.Format(ea.DownloadSource, server, version, downloadFileNameWithExtension);
                        var buildNumber = GetBuildNumber();
                        string newUrl = GetDownloadUrl(ea.PayloadId, version, downloadFileNameWithExtension, buildNumber);
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

                        //if (_userSelectionDic[ea.PackageId] == UserSelectionEnum.Uninstall.ToString())
                        //{
                        //    this.LogEvent($"PlanPackageBegin::{ea.PackageId} ea.State is set to Present...");
                        //    //Repair means package is already installed but is broken. Fix the broken package
                        //    //Tried and did not worked ea.State
                        //    //RequestState.Present;
                        //    //RequestState.ForceAbsent;
                        //    //RequestState.Absent was default
                        //    //Request.None is default for skip so its not going to work
                        //    ea.State = RequestState.Present;
                        //}
                    }
                };

                this.bootstrapper.PlanRelatedBundle += (_, ea) => { this.LogEvent("PlanRelatedBundle", ea); };

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
                this.engine.Log(LogLevel.Verbose, "Setting _userSelectionDic Begins");
                Packages.GetPackageIdsAsEnum().ToList().ForEach(x =>
                {
                    if (!_userSelectionDic.ContainsKey(x.ToString()))
                        _userSelectionDic.Add(x.ToString(), engine.StringVariables[x.ToString()]);
                });
                this.engine.Log(LogLevel.Verbose, "Setting _userSelectionDic Ends");
                this.engine.Log(LogLevel.Verbose, $"SetUpExecutionLocation::{SetUpLocation}");
            }catch(Exception ex)
            {
                engine.Log(LogLevel.Error, $"InstallerMainWindowViewModel::Exception={ex.ToString()}");
                MessageBox.Show(ex.ToString());
            }
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
            //this.bootstrapper.PlanRelatedBundle += (_, ea) => this.LogEvent("PlanRelatedBundle", ea);
            this.bootstrapper.PlanTargetMsiPackage += (_, ea) => this.LogEvent("PlanTargetMsiPackage", ea);
            this.bootstrapper.Progress += (_, ea) => this.LogEvent("Progress", ea);
            this.bootstrapper.RegisterBegin += (_, ea) => this.LogEvent("RegisterBegin");
            this.bootstrapper.RegisterComplete += (_, ea) => this.LogEvent("RegisterComplete", ea);
            this.bootstrapper.RestartRequired += (_, ea) => this.LogEvent("RestartRequired", ea);
            this.bootstrapper.UnregisterBegin += (_, ea) => this.LogEvent("UnregisterBegin", ea);
            this.bootstrapper.UnregisterComplete += (_, ea) => this.LogEvent("UnregisterComplete", ea);
        }

        private string SetUpLocation
        {
            get { return System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); }
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

        #region Installer Commands
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
            string key = PackageIdEnum.FirstInstallerBootstrapper.ToString();
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
            string key = PackageIdEnum.SecondInstallerBootstrapper.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }

        public ICommand ThirdIBootStapperCommand
        {
            get { return new DelegateCommand<string>(HandleThirdIBootStapperCommand); }
        }

        private void HandleThirdIBootStapperCommand(object commandParameter)
        {
            string key = PackageIdEnum.ThirdInstallerBootstrapper.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }

        public ICommand FourthIBootStapperCommand
        {
            get { return new DelegateCommand<string>(HandleFourthIBootStapperCommand); }
        }

        private void HandleFourthIBootStapperCommand(object commandParameter)
        {
            string key = PackageIdEnum.FourthInstallerBootstrapper.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }

        public ICommand FifthIBootStapperCommand
        {
            get { return new DelegateCommand<string>(HandleFifthIBootStapperCommand); }
        }

        private void HandleFifthIBootStapperCommand(object commandParameter)
        {
            string key = PackageIdEnum.FifthInstallerBootstrapper.ToString();
            engine.StringVariables[key] = commandParameter.ToString();
            _userSelectionDic[key] = commandParameter.ToString();
            engine.Log(LogLevel.Verbose, $"::{key} = {_userSelectionDic[key]} & commandParameter={commandParameter}");
        }


        #endregion Installer Commands

        #region Repair Command
        private DelegateCommand RepairCommandValue;
        public ICommand RepairCommand
        {
            get { return RepairCommandValue; }
        }

        private bool CanRepairCommandExecute()
        {
            return IsAtleastOneRepairOptionSelected();
        }

        private bool IsAtleastOneRepairOptionSelected()
        {
            if (FirstInstallerIsRepairChecked
                || SecondInstallerIsRepairChecked
                || ThirdInstallerIsRepairChecked
                || FourthInstallerIsRepairChecked
                || FifthInstallerIsRepairChecked
                || FIBootStapperInstallerIsRepairChecked
                || SIBootStapperInstallerIsRepairChecked
                || ThirdIBootStapperInstallerIsRepairChecked
                || FourthIBootStapperInstallerIsRepairChecked
                || FifthIBootStapperInstallerIsRepairChecked
                || SixthIBootStapperInstallerIsRepairChecked)
            {
                return true;
            }
            return false;
        }

        private bool IsNoRepairOptionSelected()
        {
            if(!FirstInstallerIsRepairChecked
                && !SecondInstallerIsRepairChecked
                && !ThirdInstallerIsRepairChecked
                && !FourthInstallerIsRepairChecked
                && !FifthInstallerIsRepairChecked
                && !FIBootStapperInstallerIsRepairChecked
                && !SIBootStapperInstallerIsRepairChecked
                && !ThirdIBootStapperInstallerIsRepairChecked
                && !FourthIBootStapperInstallerIsRepairChecked
                && !FifthIBootStapperInstallerIsRepairChecked) { return true; }
            return false;
        }

        private void HandleRepairCommand()
        {
            engine.Log(LogLevel.Verbose, "HandleRepairCommand::Begin");
            if (IfNotRepairThenSetToKeepOrSkip())
            {
                interactionService.ShowMessageBox("When Repair is selected then Install,Uninstall,Update operations are not permitted. Your selection is update appropriately, please review you updated selection and submit again...");
            }
            else
            {
                engine.Plan(LaunchAction.Repair);
                //GetApiRequest();
            }
        }
        //Called from Repair Command
        //If Installed then set to keep 
        //If Not Installed then set to skip
        private bool IfNotRepairThenSetToKeepOrSkip()
        {
            bool isSelectionChanged = false;
            if (!FirstInstallerIsRepairChecked)
            {
                if (FirstInstallerIsInstallEnabled)
                {
                    if (!FirstInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        FirstInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!FirstInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        FirstInstallerIsKeepChecked = true;
                    }
                }
            }

            if (!SecondInstallerIsRepairChecked)
            {
                if (SecondInstallerIsInstallEnabled)
                {
                    if (!SecondInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        SecondInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!SecondInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        SecondInstallerIsKeepChecked = true;
                    }
                }
            }

            if (!ThirdInstallerIsRepairChecked)
            {
                if (ThirdInstallerIsInstallEnabled)
                {
                    if (!ThirdInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        ThirdInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!ThirdInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        ThirdInstallerIsKeepChecked = true;
                    }
                }
            }

            if (!FourthInstallerIsRepairChecked)
            {
                if (FourthInstallerIsInstallEnabled)
                {
                    if (!FourthInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        FourthInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!FourthInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        FourthInstallerIsKeepChecked = true;
                    }
                }
            }

            if (!FifthInstallerIsRepairChecked)
            {
                if (FifthInstallerIsInstallEnabled)
                {
                    if (!FifthInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        FifthInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!FifthInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        FifthInstallerIsKeepChecked = true;
                    }
                }
            }

            if (!FIBootStapperInstallerIsRepairChecked)
            {
                if (FIBootStrapperInstallerIsInstallEnabled)
                {
                    if (!FIBootStapperInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        FIBootStapperInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!FIBootStapperInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        FIBootStapperInstallerIsKeepChecked = true;
                    }
                }
            }


            if (!SIBootStapperInstallerIsRepairChecked)
            {
                if (SIBootStrapperInstallerIsInstallEnabled)
                {
                    if (!SIBootStapperInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        SIBootStapperInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!SIBootStapperInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        SIBootStapperInstallerIsKeepChecked = true;
                    }
                }
            }

            if (!ThirdIBootStapperInstallerIsRepairChecked)
            {
                if (ThirdIBootStrapperInstallerIsInstallEnabled)
                {
                    if (!ThirdIBootStapperInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        ThirdIBootStapperInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!ThirdIBootStapperInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        ThirdIBootStapperInstallerIsKeepChecked = true;
                    }
                }
            }

            if (!FourthIBootStapperInstallerIsRepairChecked)
            {
                if (FourthIBootStrapperInstallerIsInstallEnabled)
                {
                    if (!FourthIBootStapperInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        FourthIBootStapperInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!FourthIBootStapperInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        FourthIBootStapperInstallerIsKeepChecked = true;
                    }
                }
            }

            if (!FifthIBootStapperInstallerIsRepairChecked)
            {
                if (FifthIBootStrapperInstallerIsInstallEnabled)
                {
                    if (!FifthIBootStapperInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        FifthIBootStapperInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!FifthIBootStapperInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        FifthIBootStapperInstallerIsKeepChecked = true;
                    }
                }
            }

            if (!SixthIBootStapperInstallerIsRepairChecked)
            {
                if (SixthIBootStrapperInstallerIsInstallEnabled)
                {
                    if (!SixthIBootStapperInstallerIsSkipChecked)
                    {
                        isSelectionChanged = true;
                        SixthIBootStapperInstallerIsSkipChecked = true;
                    }
                }
                else
                {
                    if (!SixthIBootStapperInstallerIsKeepChecked)
                    {
                        isSelectionChanged = true;
                        SixthIBootStapperInstallerIsKeepChecked = true;
                    }
                }
            }

            return isSelectionChanged;

        }

        //Called from Repair Command
        private void SetNotRepairToKeep()
        {
            if (!FirstInstallerIsRepairChecked)
                FirstInstallerIsKeepChecked = true;
            if (!SecondInstallerIsRepairChecked)
                SecondInstallerIsKeepChecked = true;
            if (!ThirdInstallerIsRepairChecked)
                ThirdInstallerIsKeepChecked = true;
            if (!FourthInstallerIsRepairChecked)
                FourthInstallerIsKeepChecked = true;
            if (!FifthInstallerIsRepairChecked)
                FifthInstallerIsKeepChecked = true;
            if (!FIBootStapperInstallerIsRepairChecked)
                FIBootStapperInstallerIsKeepChecked = true;
            if (!SIBootStapperInstallerIsRepairChecked)
                SIBootStapperInstallerIsKeepChecked = true;
            if (!ThirdIBootStapperInstallerIsRepairChecked)
                ThirdIBootStapperInstallerIsKeepChecked = true;
            if (!FourthIBootStapperInstallerIsRepairChecked)
                FourthIBootStapperInstallerIsKeepChecked = true;
            if (!FifthIBootStapperInstallerIsRepairChecked)
                FifthIBootStapperInstallerIsKeepChecked = true;
            if (!SixthIBootStapperInstallerIsRepairChecked)
                SixthIBootStapperInstallerIsKeepChecked = true;
        }
        #endregion Repair Command

        #region Apply Command

        private DelegateCommand ApplyCommandValue;
        public ICommand ApplyCommand
        {
            get { return ApplyCommandValue; }
        }

        private bool CanApplyCommandExecute()
        {
            return !IsAtleastOneRepairOptionSelected();
        }

        private void RaiseCanExecuteCommandForRepairAndApply()
        {
            RepairCommandValue.RaiseCanExecuteChanged();
            ApplyCommandValue.RaiseCanExecuteChanged();
        }

        private void HandleApplyCommand()
        {
            //SetRepairToSkip();
            //1). Check what is already installed on users computer
            //2). Check What user is Uninstalling
            //3). Check what user is Installing
            //4). Check if anything is left installed
            //5). If nothing is left installed on Client Computer then Call Uninstall otherwise call Install

            //1). Check what is already installed on users computer (Registry is not updated immediately after calling the Install command)
            var installedModules = GetModulesInstalledOnClientComputer();
            var installedModuleName = string.Join(",", installedModules.Select(x => x.ModuleName).ToArray());
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

            var updateSelected = userSelection.Where(x => x.Value.ToLower() == UserSelectionEnum.Update.ToString().ToLower()).Select(x => x.Key).ToList();
            if(updateSelected.Count > 0)
            {
                engine.Log(LogLevel.Verbose, $"HandleApplyCommand::UpdateSelected Modules = {string.Join(",", updateSelected.ToArray())}");
                UpdateDetectConditionForPackageSelectedForUpdate(updateSelected);
                //DeleteInstalledRegistryKey(updateSelected);
            }

            //4). If nothing is left installed on Client Computer the Call Uninstall otherwise call Install
            engine.Log(LogLevel.Verbose,$"installedModules.Count={installedModules.Count} unInstalledSelected.Count={unInstalledSelected.Count} InstalledSelected.Count={InstalledSelected.Count}" );
            bool executeUninstall = false;
            //-1 is for Bootstrapper
            if (installedModules.Count == unInstalledSelected.Count)
            {
                //User have selected to Uninstall all installed modules
                //Have user selected to Installed any new module
                if (InstalledSelected.Count == 0)
                {
                    executeUninstall = true;
                }
            }

            //DetectCondition is executed before the UI is displayed to the user
            //If user selects Update for OnlineSetup then the detect condition will not fire again 
            //We change the detect condition based on user selection.
            //We force the detect condition to be executed again for the package selected for update
            //This way we get both i.e. When the main bundle is uninstalled then exepackage (child) will also be uninstalled 
            //and when the exepackage is updated then update will also happens (both condition depends upon DetectCondition.)
            bootstrapper.Engine.Detect();

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
        #endregion

        #region ShowNewModules Command
        private DelegateCommand ShowNewModulesCommandValue;
        public ICommand ShowNewModulesCommand
        {
            get { return ShowNewModulesCommandValue; }
        }
        private bool CanShowNewModulesCommandExecute()
        {
            return true;
        }

        private Settings _settings = null;
        private Settings GetSettings()
        {
            if (_settings != null)
                return _settings;
            string settingFile = $@"{SetUpLocation}\Settings.json";
            if (File.Exists(settingFile))
            {
                File.ReadAllText(settingFile);
                _settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingFile));
                return _settings;
            }
            return null;
        }

        private string GetBuildNumber()
        {
            //Build# is availabe to application via dll file property.
            //Application current saves the build# in registry
            //We have to use the same mechanism of retrieve the build# from file properties in this function and
            //return it. Let's hard code for not to complete the full flow
            return 123456.ToString();
        }

        private string GetDownloadUrl(string packageId, string version, string packageFileNameWithExtension, string buildNumber)
        {
            var settings = GetSettings();
            if (settings != null)
            {
                //Get Setting specific to Active Environment
                var downloadUrl = settings.DownloadUrls.Where(x => x.Environment.Equals(settings.ActiveEnvironment)).FirstOrDefault();
                if (downloadUrl != null)
                {
                    //Get Setting specific to PackageId
                    var downloadUrlForPackage = downloadUrl.Packages.Where(x => x.PackageId.ToLower() == packageId.ToLower()).FirstOrDefault();
                    if(downloadUrlForPackage != null)
                    {
                        //if ActiveEnvironment is Staging and the operation is Update then set buildNumber to empty to force URL to use new version 
                        //which is not associated with current build number but is generic to any build number (It is tied to version#)
                        //This approach will allow us to maintain new version artifacts separately from team city posted builds (Which is expected to be under build number)
                        if(settings.ActiveEnvironment.ToString().ToLower() == "staging" && _userSelectionDic[packageId] == UserSelectionEnum.Update.ToString())
                        {
                            buildNumber = string.Empty;
                        }
                        return string.Format(downloadUrlForPackage.DownloadUrl,downloadUrl.Host,version,packageFileNameWithExtension, buildNumber);
                    }
                    else
                    {
                        engine.Log(LogLevel.Error, $"GetDownloadUrl1::DownloadUrl not found for packageId={packageId}");
                    }
                }
                else
                {
                    engine.Log(LogLevel.Error, $"GetDownloadUrl2::DownloadUrl not found for packageId={packageId}");
                }
            }
            engine.Log(LogLevel.Error, $"GetDownloadUrl3::DownloadUrl not found for packageId={packageId}");
            return string.Empty;
        }
        
        private void HandleShowNewModulesCommand()
        {
            //Show the new modules that are available for installation
            SixthIBootStrapperInstallerLabelContent = "6th Bootstrapper";
            SixthIBootStrapperInstallerLabelVisibility = Visibility.Visible.ToString();
            SixthIBootStrapperInstallerSkipVisibility = Visibility.Visible.ToString();
            SixthIBootStrapperInstallerKeepVisibility = Visibility.Visible.ToString();
            SixthIBootStrapperInstallerInstallVisibility = Visibility.Visible.ToString();
            SixthIBootStrapperInstallerUnInstallVisibility = Visibility.Visible.ToString();
            SixthIBootStrapperInstallerUpdateVisibility = Visibility.Visible.ToString();
            SixthIBootStrapperInstallerRepairVisibility = Visibility.Visible.ToString();
        }

        #endregion ShowNew Modules Command

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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Keep
        private bool _firstInstallerIsKeepChecked = false;
        public bool FirstInstallerIsKeepChecked
        {
            get
            {
                return this._firstInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._firstInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Install
        private bool _firstInstallerIsInstallChecked = false;
        public bool FirstInstallerIsInstallChecked
        {
            get { return this._firstInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._firstInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _firstInstallerIsUpdateChecked = false;
        public bool FirstInstallerIsUpdateChecked
        {
            get
            {
                return this._firstInstallerIsUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._firstInstallerIsUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstaller.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
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
                    RaiseCanExecuteCommandForRepairAndApply();
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
        #region Checked
        #region Skip
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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion 

        #region Keep
        private bool _secondInstallerIsKeepChecked = false;
        public bool SecondInstallerIsKeepChecked
        {
            get
            {
                return this._secondInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._secondInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region install
        private bool _secondInstallerIsInstallChecked = false;
        public bool SecondInstallerIsInstallChecked
        {
            get { return this._secondInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._secondInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Uninstall
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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _secondInstallerIsUpdateChecked = false;
        public bool SecondInstallerIsUpdateChecked
        {
            get
            {
                return this._secondInstallerIsUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._secondInstallerIsUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstaller.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion
        #endregion Checked


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
        #region Skip
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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Keep
        private bool _thirdInstallerIsKeepChecked = false;
        public bool ThirdInstallerIsKeepChecked
        {
            get
            {
                return this._thirdInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Install
        private bool _thirdInstallerIsInstallChecked = false;
        public bool ThirdInstallerIsInstallChecked
        {
            get { return this._thirdInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region UnInstall
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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _thirdInstallerIsUpdateChecked = false;
        public bool ThirdInstallerIsUpdateChecked
        {
            get
            {
                return this._thirdInstallerIsUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._thirdInstallerIsUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstaller.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

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
                    RaiseCanExecuteCommandForRepairAndApply();
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
        #region Checked
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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Keep
        private bool _fourthInstallerIsKeepChecked = false;
        public bool FourthInstallerIsKeepChecked
        {
            get
            {
                return this._fourthInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Install
        private bool _fourthInstallerIsInstallChecked = false;
        public bool FourthInstallerIsInstallChecked
        {
            get { return this._fourthInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._fourthInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion


        #region Uninstall
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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _fourthInstallerUpdateChecked = false;
        public bool FourthInstallerIsUpdateChecked
        {
            get
            {
                return this._fourthInstallerUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._fourthInstallerUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstaller.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

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
                    RaiseCanExecuteCommandForRepairAndApply();
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
        #region Skip
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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion


        #region Keep
        private bool _fifthInstallerIsKeepChecked = false;
        public bool FifthInstallerIsKeepChecked
        {
            get
            {
                return this._fifthInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Install
        private bool _fifthInstallerIsInstallChecked = false;
        public bool FifthInstallerIsInstallChecked
        {
            get { return this._fifthInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion



        #region Uninstall
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
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _fifthInstallerIsUpdateChecked = false;
        public bool FifthInstallerIsUpdateChecked
        {
            get
            {
                return this._fifthInstallerIsUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._fifthInstallerIsUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstaller.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion


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
                    RaiseCanExecuteCommandForRepairAndApply();
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
        #region Skip
        private bool _fIBootStapperInstallerIsSkipChecked = true;
        public bool FIBootStapperInstallerIsSkipChecked
        {
            get { return this._fIBootStapperInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._fIBootStapperInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion Skip

        #region Keep
        private bool _fIBootStrapperInstallerIsKeepChecked = false;
        public bool FIBootStapperInstallerIsKeepChecked
        {
            get
            {
                return this._fIBootStrapperInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Install
        private bool _fIBootStrapperInstallerIsInstallChecked = false;
        public bool FIBootStapperInstallerIsInstallChecked
        {
            get { return this._fIBootStrapperInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion 


        #region Uninstall
        private bool _fIBootStrapperInstallerIsUnInstallChecked = false;
        public bool FIBootStapperInstallerIsUnInstallChecked
        {
            get { return this._fIBootStrapperInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _fIBootStrapperInstallerIsUpdateChecked = false;
        public bool FIBootStapperInstallerIsUpdateChecked
        {
            get
            {
                return this._fIBootStrapperInstallerIsUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._fIBootStrapperInstallerIsUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion


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
                    engine.StringVariables[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.FirstInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
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
        #region Skip
        private bool _sIBootStapperInstallerIsSkipChecked = true;
        public bool SIBootStapperInstallerIsSkipChecked
        {
            get { return this._sIBootStapperInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._sIBootStapperInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion skip

        #region Keep
        private bool _sIBootStrapperInstallerIsKeepChecked = false;
        public bool SIBootStapperInstallerIsKeepChecked
        {
            get
            {
                return this._sIBootStrapperInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Install
        private bool _sIBootStrapperInstallerIsInstallChecked = false;
        public bool SIBootStapperInstallerIsInstallChecked
        {
            get { return this._sIBootStrapperInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Uninstall
        private bool _sIBootStrapperInstallerIsUnInstallChecked = false;
        public bool SIBootStapperInstallerIsUnInstallChecked
        {
            get { return this._sIBootStrapperInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _sIBootStrapperInstallerIsUpdateChecked = false;
        public bool SIBootStapperInstallerIsUpdateChecked
        {
            get
            {
                return this._sIBootStrapperInstallerIsUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._sIBootStrapperInstallerIsUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

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
                    engine.StringVariables[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.SecondInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
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

        #region ThirdIBootStapper
        #region Skip
        private bool _thirdIBootStapperInstallerIsSkipChecked = true;
        public bool ThirdIBootStapperInstallerIsSkipChecked
        {
            get { return this._thirdIBootStapperInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._thirdIBootStapperInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Keep
        private bool _thirdIBootStrapperInstallerIsKeepChecked = false;
        public bool ThirdIBootStapperInstallerIsKeepChecked
        {
            get
            {
                return this._thirdIBootStrapperInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Install
        private bool _thirdIBootStrapperInstallerIsInstallChecked = false;
        public bool ThirdIBootStapperInstallerIsInstallChecked
        {
            get { return this._thirdIBootStrapperInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Uninstall
        private bool _thirdIBootStrapperInstallerIsUnInstallChecked = false;
        public bool ThirdIBootStapperInstallerIsUnInstallChecked
        {
            get { return this._thirdIBootStrapperInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _thirdIBootStrapperInstallerIsUpdateChecked = false;
        public bool ThirdIBootStapperInstallerIsUpdateChecked
        {
            get
            {
                return this._thirdIBootStrapperInstallerIsUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Repair
        private bool _thirdIBootStrapperInstallerIsRepairChecked = false;
        public bool ThirdIBootStapperInstallerIsRepairChecked
        {
            get
            {
                return this._thirdIBootStrapperInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.ThirdInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region  IsEnabled
        private bool _thirdIBootStrapperInstallerIsInstallEnabled = true;
        public bool ThirdIBootStrapperInstallerIsInstallEnabled
        {
            get { return this._thirdIBootStrapperInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsInstallEnabled, value);
            }
        }

        private bool _thirdIBootStrapperInstallerIsUnInstallEnabled = true;
        public bool ThirdIBootStrapperInstallerIsUnInstallEnabled
        {
            get { return this._thirdIBootStrapperInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _thirdIBootStrapperInstallerIsSkipEnabled = true;
        public bool ThirdIBootStrapperInstallerIsSkipEnabled
        {
            get { return this._thirdIBootStrapperInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsSkipEnabled, value);
            }
        }

        private bool _thirdIBootStrapperInstallerIsKeepEnabled = true;
        public bool ThirdIBootStrapperInstallerIsKeepEnabled
        {
            get { return this._thirdIBootStrapperInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsKeepEnabled, value);
            }
        }

        private bool _thirdIBootStrapperInstallerIsRepairEnabled = true;
        public bool ThirdIBootStrapperInstallerIsRepairEnabled
        {
            get { return this._thirdIBootStrapperInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsRepairEnabled, value);
            }
        }

        private bool _thirdIBootStrapperInstallerIsUpdateEnabled = true;
        public bool ThirdIBootStrapperInstallerIsUpdateEnabled
        {
            get { return this._thirdIBootStrapperInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._thirdIBootStrapperInstallerIsUpdateEnabled, value);
            }
        }
        #endregion

        #endregion

        #region FourthIBootStapper
        #region Skip
        private bool _fourthIBootStapperInstallerIsSkipChecked = true;
        public bool FourthIBootStapperInstallerIsSkipChecked
        {
            get { return this._fourthIBootStapperInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._fourthIBootStapperInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Keep
        private bool _fourthIBootStrapperInstallerIsKeepChecked = false;
        public bool FourthIBootStapperInstallerIsKeepChecked
        {
            get
            {
                return this._fourthIBootStrapperInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Install
        private bool _fourthIBootStrapperInstallerIsInstallChecked = false;
        public bool FourthIBootStapperInstallerIsInstallChecked
        {
            get { return this._fourthIBootStrapperInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion


        #region Uninstall
        private bool _fourthIBootStrapperInstallerIsUnInstallChecked = false;
        public bool FourthIBootStapperInstallerIsUnInstallChecked
        {
            get { return this._fourthIBootStrapperInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _fourthIBootStrapperInstallerIsUpdateChecked = false;
        public bool FourthIBootStapperInstallerIsUpdateChecked
        {
            get
            {
                return this._fourthIBootStrapperInstallerIsUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Repair
        private bool _fourthIBootStrapperInstallerIsRepairChecked = false;
        public bool FourthIBootStapperInstallerIsRepairChecked
        {
            get
            {
                return this._fourthIBootStrapperInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.FourthInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region  IsEnabled
        private bool _fourthIBootStrapperInstallerIsInstallEnabled = true;
        public bool FourthIBootStrapperInstallerIsInstallEnabled
        {
            get { return this._fourthIBootStrapperInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsInstallEnabled, value);
            }
        }

        private bool _fourthIBootStrapperInstallerIsUnInstallEnabled = true;
        public bool FourthIBootStrapperInstallerIsUnInstallEnabled
        {
            get { return this._fourthIBootStrapperInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _fourthIBootStrapperInstallerIsSkipEnabled = true;
        public bool FourthIBootStrapperInstallerIsSkipEnabled
        {
            get { return this._fourthIBootStrapperInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsSkipEnabled, value);
            }
        }

        private bool _fourthIBootStrapperInstallerIsKeepEnabled = true;
        public bool FourthIBootStrapperInstallerIsKeepEnabled
        {
            get { return this._fourthIBootStrapperInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsKeepEnabled, value);
            }
        }

        private bool _fourthIBootStrapperInstallerIsRepairEnabled = true;
        public bool FourthIBootStrapperInstallerIsRepairEnabled
        {
            get { return this._fourthIBootStrapperInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsRepairEnabled, value);
            }
        }

        private bool _fourthIBootStrapperInstallerIsUpdateEnabled = true;
        public bool FourthIBootStrapperInstallerIsUpdateEnabled
        {
            get { return this._fourthIBootStrapperInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._fourthIBootStrapperInstallerIsUpdateEnabled, value);
            }
        }
        #endregion
        #endregion

        #region FifthIBootStapper
        #region Skip
        private bool _fifthIBootStapperInstallerIsSkipChecked = true;
        public bool FifthIBootStapperInstallerIsSkipChecked
        {
            get { return this._fifthIBootStapperInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._fifthIBootStapperInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion 

        #region Keep
        private bool _fifthIBootStrapperInstallerIsKeepChecked = false;
        public bool FifthIBootStapperInstallerIsKeepChecked
        {
            get
            {
                return this._fifthIBootStrapperInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Install
        private bool _fifthIBootStrapperInstallerIsInstallChecked = false;
        public bool FifthIBootStapperInstallerIsInstallChecked
        {
            get { return this._fifthIBootStrapperInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Uninstall
        private bool _fifthIBootStrapperInstallerIsUnInstallChecked = false;
        public bool FifthIBootStapperInstallerIsUnInstallChecked
        {
            get { return this._fifthIBootStrapperInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _fifthIBootStrapperInstallerIsUpdateChecked = false;
        public bool FifthIBootStapperInstallerIsUpdateChecked
        {
            get
            {
                return this._fifthIBootStrapperInstallerIsUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Repair
        private bool _fifthIBootStrapperInstallerIsRepairChecked = false;
        public bool FifthIBootStapperInstallerIsRepairChecked
        {
            get
            {
                return this._fifthIBootStrapperInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.FifthInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region  IsEnabled
        private bool _fifthIBootStrapperInstallerIsInstallEnabled = true;
        public bool FifthIBootStrapperInstallerIsInstallEnabled
        {
            get { return this._fifthIBootStrapperInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsInstallEnabled, value);
            }
        }

        private bool _fifthIBootStrapperInstallerIsUnInstallEnabled = true;
        public bool FifthIBootStrapperInstallerIsUnInstallEnabled
        {
            get { return this._fifthIBootStrapperInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _fifthIBootStrapperInstallerIsSkipEnabled = true;
        public bool FifthIBootStrapperInstallerIsSkipEnabled
        {
            get { return this._fifthIBootStrapperInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsSkipEnabled, value);
            }
        }

        private bool _fifthIBootStrapperInstallerIsKeepEnabled = true;
        public bool FifthIBootStrapperInstallerIsKeepEnabled
        {
            get { return this._fifthIBootStrapperInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsKeepEnabled, value);
            }
        }

        private bool _fifthIBootStrapperInstallerIsRepairEnabled = true;
        public bool FifthIBootStrapperInstallerIsRepairEnabled
        {
            get { return this._fifthIBootStrapperInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsRepairEnabled, value);
            }
        }

        private bool _fifthIBootStrapperInstallerIsUpdateEnabled = true;
        public bool FifthIBootStrapperInstallerIsUpdateEnabled
        {
            get { return this._fifthIBootStrapperInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._fifthIBootStrapperInstallerIsUpdateEnabled, value);
            }
        }
        #endregion
        #endregion

        #region SixthIBootStrapper

        #region Label Content 
        private string _sixthIBootStrapperInstallerLabelContent = "Not Available";

        public string SixthIBootStrapperInstallerLabelContent
        {
            get { return this._sixthIBootStrapperInstallerLabelContent; }
            set { this.SetProperty(ref this._sixthIBootStrapperInstallerLabelContent, value); }
        }

        #endregion

        #region Visibility
        private string _sixthIBootStrapperInstallerLabelVisibility = Visibility.Collapsed.ToString();
        public string SixthIBootStrapperInstallerLabelVisibility
        {
            get { return this._sixthIBootStrapperInstallerLabelVisibility; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerLabelVisibility, value);
            }
        }

        private string _sixthIBootStrapperInstallerSkipVisibility = Visibility.Collapsed.ToString();
        public string SixthIBootStrapperInstallerSkipVisibility
        {
            get { return this._sixthIBootStrapperInstallerSkipVisibility; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerSkipVisibility, value);
            }
        }

        private string _sixthIBootStrapperInstallerKeepVisibility = Visibility.Collapsed.ToString();
        public string SixthIBootStrapperInstallerKeepVisibility
        {
            get { return this._sixthIBootStrapperInstallerKeepVisibility; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerKeepVisibility, value);
            }
        }

        private string _sixthIBootStrapperInstallerInstallVisibility = Visibility.Collapsed.ToString();
        public string SixthIBootStrapperInstallerInstallVisibility
        {
            get { return this._sixthIBootStrapperInstallerInstallVisibility; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerInstallVisibility, value);
            }
        }

        private string _sixthIBootStrapperInstallerUnInstallVisibility = Visibility.Collapsed.ToString();
        public string SixthIBootStrapperInstallerUnInstallVisibility
        {
            get { return this._sixthIBootStrapperInstallerUnInstallVisibility; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerUnInstallVisibility, value);
            }
        }

        private string _sixthIBootStrapperInstallerUpdateVisibility = Visibility.Collapsed.ToString();
        public string SixthIBootStrapperInstallerUpdateVisibility
        {
            get { return this._sixthIBootStrapperInstallerUpdateVisibility; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerUpdateVisibility, value);
            }
        }

        private string _sixthIBootStrapperInstallerRepairVisibility = Visibility.Collapsed.ToString();
        public string SixthIBootStrapperInstallerRepairVisibility
        {
            get { return this._sixthIBootStrapperInstallerRepairVisibility; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerRepairVisibility, value);
            }
        }

        #endregion

        #region Skip
        private bool _sixthIBootStapperInstallerIsSkipChecked = true;
        public bool SixthIBootStapperInstallerIsSkipChecked
        {
            get { return this._sixthIBootStapperInstallerIsSkipChecked; }
            set
            {
                this.SetProperty(ref this._sixthIBootStapperInstallerIsSkipChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    _userSelectionDic[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Skip.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Keep
        private bool _sixthIBootStrapperInstallerIsKeepChecked = false;
        public bool SixthIBootStapperInstallerIsKeepChecked
        {
            get
            {
                return this._sixthIBootStrapperInstallerIsKeepChecked;
            }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsKeepChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    _userSelectionDic[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Keep.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Install
        private bool _sixthIBootStrapperInstallerIsInstallChecked = false;
        public bool SixthIBootStapperInstallerIsInstallChecked
        {
            get { return this._sixthIBootStrapperInstallerIsInstallChecked; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    _userSelectionDic[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Install.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Uninstall
        private bool _sixthIBootStrapperInstallerIsUnInstallChecked = false;
        public bool SixthIBootStapperInstallerIsUnInstallChecked
        {
            get { return this._sixthIBootStrapperInstallerIsUnInstallChecked; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsUnInstallChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    _userSelectionDic[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Uninstall.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Update
        private bool _sixthIBootStrapperInstallerIsUpdateChecked = false;
        public bool SixthIBootStapperInstallerIsUpdateChecked
        {
            get
            {
                return this._sixthIBootStrapperInstallerIsUpdateChecked;
            }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsUpdateChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    _userSelectionDic[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Update.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region Repair
        private bool _sixthIBootStrapperInstallerIsRepairChecked = false;
        public bool SixthIBootStapperInstallerIsRepairChecked
        {
            get
            {
                return this._sixthIBootStrapperInstallerIsRepairChecked;
            }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsRepairChecked, value);
                if (value)
                {
                    engine.StringVariables[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    _userSelectionDic[PackageIdEnum.SixthInstallerBootstrapper.ToString()] = UserSelectionEnum.Repair.ToString();
                    RaiseCanExecuteCommandForRepairAndApply();
                }
            }
        }
        #endregion

        #region  IsEnabled
        private bool _sixthIBootStrapperInstallerIsInstallEnabled = true;
        public bool SixthIBootStrapperInstallerIsInstallEnabled
        {
            get { return this._sixthIBootStrapperInstallerIsInstallEnabled; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsInstallEnabled, value);
            }
        }

        private bool _sixthIBootStrapperInstallerIsUnInstallEnabled = true;
        public bool SixthIBootStrapperInstallerIsUnInstallEnabled
        {
            get { return this._sixthIBootStrapperInstallerIsUnInstallEnabled; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsUnInstallEnabled, value);
            }
        }

        private bool _sixthIBootStrapperInstallerIsSkipEnabled = true;
        public bool SixthIBootStrapperInstallerIsSkipEnabled
        {
            get { return this._sixthIBootStrapperInstallerIsSkipEnabled; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsSkipEnabled, value);
            }
        }

        private bool _sixthIBootStrapperInstallerIsKeepEnabled = true;
        public bool SixthIBootStrapperInstallerIsKeepEnabled
        {
            get { return this._sixthIBootStrapperInstallerIsKeepEnabled; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsKeepEnabled, value);
            }
        }

        private bool _sixthIBootStrapperInstallerIsRepairEnabled = true;
        public bool SixthIBootStrapperInstallerIsRepairEnabled
        {
            get { return this._sixthIBootStrapperInstallerIsRepairEnabled; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsRepairEnabled, value);
            }
        }

        private bool _sixthIBootStrapperInstallerIsUpdateEnabled = true;
        public bool SixthIBootStrapperInstallerIsUpdateEnabled
        {
            get { return this._sixthIBootStrapperInstallerIsUpdateEnabled; }
            set
            {
                this.SetProperty(ref this._sixthIBootStrapperInstallerIsUpdateEnabled, value);
            }
        }
        #endregion
        #endregion


        #endregion Properties for data binding

        private void SelectInstallIfNotInstalled()
        {
            //Select all packages that are installed on client's computer
            var installedPackages = GetModulesInstalledOnClientComputer();
            var installedPackagesName = installedPackages.Select(x => x.ModuleName).ToList();
            var packageIds = Packages.GetPackageIdsAsEnum().Select(x => x.ToString()).ToList(); 

            engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::PackageIds1={string.Join(",",packageIds.ToArray())}");

            //Remove all packages that are installed on client's computer
            installedPackagesName.ForEach(x =>
            {
                engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Installed Package Name = {x}");
                var packageToRemove = x;
                if (!packageIds.Contains(x))
                {
                    //For Dynamic Packages we have to get the PackagdId from PackageName (It may be different)
                    packageToRemove = _apiResponse.AvailableUpdates.Where(z => z.PackageNameToShowInAddRemoveProgram.ToLower().Equals(x.ToLower()))
                        .Select(y => y.PackageId.ToString()).FirstOrDefault();
                }
                if (!packageIds.Remove(packageToRemove))
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
                if (x.ToLower().Equals(PackageIdEnum.FirstInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    FIBootStrapperInstallerIsUnInstallEnabled = false;
                    FIBootStrapperInstallerIsKeepEnabled = false;
                    FIBootStrapperInstallerIsRepairEnabled = false;
                    FIBootStrapperInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.SecondInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    SIBootStrapperInstallerIsUnInstallEnabled = false;
                    SIBootStrapperInstallerIsKeepEnabled = false;
                    SIBootStrapperInstallerIsRepairEnabled = false;
                    SIBootStrapperInstallerIsUpdateEnabled = false;
                }

                if (x.ToLower().Equals(PackageIdEnum.ThirdInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    ThirdIBootStrapperInstallerIsUnInstallEnabled = false;
                    ThirdIBootStrapperInstallerIsKeepEnabled = false;
                    ThirdIBootStrapperInstallerIsRepairEnabled = false;
                    ThirdIBootStrapperInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FourthInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    FourthIBootStrapperInstallerIsUnInstallEnabled = false;
                    FourthIBootStrapperInstallerIsKeepEnabled = false;
                    FourthIBootStrapperInstallerIsRepairEnabled = false;
                    FourthIBootStrapperInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FifthInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    FifthIBootStrapperInstallerIsUnInstallEnabled = false;
                    FifthIBootStrapperInstallerIsKeepEnabled = false;
                    FifthIBootStrapperInstallerIsRepairEnabled = false;
                    FifthIBootStrapperInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.SixthInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectInstallIfNotInstalled::Disabling {x}");
                    SixthIBootStrapperInstallerIsUnInstallEnabled = false;
                    SixthIBootStrapperInstallerIsKeepEnabled = false;
                    SixthIBootStrapperInstallerIsRepairEnabled = false;
                    SixthIBootStrapperInstallerIsUpdateEnabled = false;
                }

            }
        }

        private void SelectUnInstallIfInstalled()
        {
            engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled::Begin Execution");
            //Select all packages that are installed on client's computer
            var installedPackages = GetModulesInstalledOnClientComputer();
            var installedPackagesName = installedPackages.Select(x => x.ModuleName).ToList();
            installedPackagesName.ForEach(x =>
            {
                engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:Installed Package Name = {x}");
                if (x.ToLower().Equals(PackageIdEnum.FirstInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:1Installed Package Name = {x}");
                    FirstInstallerIsUnInstallChecked = true;
                    FirstInstallerIsInstallEnabled = false;
                    FirstInstallerIsSkipEnabled = false;

                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    if(_originalVersion[x] == versionInstalled) 
                        FirstInstallerIsUpdateEnabled = false;

                }
                if (x.ToLower().Equals(PackageIdEnum.SecondInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:2Installed Package Name = {x}");
                    SecondInstallerIsUnInstallChecked = true;
                    SecondInstallerIsInstallEnabled = false;
                    SecondInstallerIsSkipEnabled = false;
                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    if (_originalVersion[x] == versionInstalled)
                        SecondInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.ThirdInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:3Installed Package Name = {x}");
                    ThirdInstallerIsUnInstallChecked = true;
                    ThirdInstallerIsInstallEnabled = false;
                    ThirdInstallerIsSkipEnabled = false;
                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    if (_originalVersion[x] == versionInstalled)
                        ThirdInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FourthInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:4Installed Package Name = {x}");
                    FourthInstallerIsUnInstallChecked = true;
                    FourthInstallerIsInstallEnabled = false;
                    FourthInstallerIsSkipEnabled = false;
                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    if (_originalVersion[x] == versionInstalled)
                        FourthInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FifthInstaller.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:5Installed Package Name = {x}");
                    FifthInstallerIsUnInstallChecked = true;
                    FifthInstallerIsInstallEnabled = false;
                    FifthInstallerIsSkipEnabled = false;
                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    if (_originalVersion[x] == versionInstalled)
                        FifthInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.FirstInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:6Installed Package Name = {x}");
                    FIBootStapperInstallerIsUnInstallChecked = true;
                    FIBootStrapperInstallerIsInstallEnabled = false;
                    FIBootStrapperInstallerIsSkipEnabled = false;
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.FirstInstallerBootstrapper)}"] = "yes";
                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    var apiVersion = _apiResponse.AvailableUpdates.Where(z => z.PackageId.ToString().ToLower().Equals(x.ToLower())).Select(z => z.Version).FirstOrDefault();
                    if (apiVersion == versionInstalled)
                        FIBootStrapperInstallerIsUpdateEnabled = false;
                }
                if (x.ToLower().Equals(PackageIdEnum.SecondInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:7Installed Package Name = {x}");
                    SIBootStapperInstallerIsUnInstallChecked = true;
                    SIBootStrapperInstallerIsInstallEnabled = false;
                    SIBootStrapperInstallerIsSkipEnabled = false;
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.SecondInstallerBootstrapper)}"] = "yes";
                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    var apiVersion = _apiResponse.AvailableUpdates.Where(z => z.PackageId.ToString().ToLower().Equals(x.ToLower())).Select(z => z.Version).FirstOrDefault();
                    if (versionInstalled == apiVersion)
                    {
                        SIBootStrapperInstallerIsUpdateEnabled = false;
                    }
                }

                if (x.ToLower().Equals(PackageIdEnum.ThirdInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:8Installed Package Name = {x}");
                    ThirdIBootStapperInstallerIsUnInstallChecked = true;
                    ThirdIBootStrapperInstallerIsInstallEnabled = false;
                    ThirdIBootStrapperInstallerIsSkipEnabled = false;
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.ThirdInstallerBootstrapper)}"] = "yes";
                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    var apiVersion = _apiResponse.AvailableUpdates.Where(z => z.PackageId.ToString().ToLower().Equals(x.ToLower())).Select(z => z.Version).FirstOrDefault();
                    if (versionInstalled == apiVersion)
                    {
                        ThirdIBootStrapperInstallerIsInstallEnabled = false;
                    }
                }
                if (x.ToLower().Equals(PackageIdEnum.FourthInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:9Installed Package Name = {x}");
                    FourthIBootStapperInstallerIsUnInstallChecked = true;
                    FourthIBootStrapperInstallerIsInstallEnabled = false;
                    FourthIBootStrapperInstallerIsSkipEnabled = false;
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.FourthInstallerBootstrapper)}"] = "yes";
                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    var apiVersion = _apiResponse.AvailableUpdates.Where(z => z.PackageId.ToString().ToLower().Equals(x.ToLower())).Select(z => z.Version).FirstOrDefault();
                    if (versionInstalled == apiVersion)
                    {
                        FourthIBootStrapperInstallerIsUpdateEnabled = false;
                    }
                }
                if (x.ToLower().Equals(PackageIdEnum.FifthInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:10Installed Package Name = {x}");
                    FifthIBootStapperInstallerIsUnInstallChecked = true;
                    FifthIBootStrapperInstallerIsInstallEnabled = false;
                    FifthIBootStrapperInstallerIsSkipEnabled = false;
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.FifthInstallerBootstrapper)}"] = "yes";
                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    var apiVersion = _apiResponse.AvailableUpdates.Where(z => z.PackageId.ToString().ToLower().Equals(x.ToLower())).Select(z => z.Version).FirstOrDefault();
                    if (versionInstalled == apiVersion)
                    {
                        FifthIBootStrapperInstallerIsUpdateEnabled = false;
                    }
                }

                //Sixth Installer is not installed and is only a place holder
                var newSixthModule = _apiResponse.AvailableUpdates.Where(z => x.ToLower().Equals(z.PackageNameToShowInAddRemoveProgram.ToLower())
                                                                                 && z.PackageId.ToString().ToLower().Equals(PackageIdEnum.SixthInstallerBootstrapper.ToString().ToLower()))
                                                                                .FirstOrDefault();
                if (newSixthModule != null)
                {
                    engine.Log(LogLevel.Verbose, $"SelectUnInstallIfInstalled:10Installed Package Name = {x}");
                    SixthIBootStapperInstallerIsUnInstallChecked = true;
                    SixthIBootStrapperInstallerIsInstallEnabled = false;
                    SixthIBootStrapperInstallerIsSkipEnabled = false;
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.SixthInstallerBootstrapper)}"] = "yes";
                    //If the installed version and available version is same then disable the update button
                    var versionInstalled = installedPackages.Where(y => y.ModuleName == x).Select(z => z.ModuleVersion).FirstOrDefault();
                    if (newSixthModule.Version == versionInstalled)
                        SixthIBootStrapperInstallerIsUpdateEnabled = false;
                    HandleShowNewModulesCommand();
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

        private IList<InstalledModule> GetModulesInstalledOnClientComputer(bool includeMainBundle=false)
        {
            IList<InstalledModule> installedModuleLst = new List<InstalledModule>();
            InstalledModule installedModule = new InstalledModule();
            string packageNames = string.Join("-", _apiResponse.AvailableUpdates.Select(z => z.PackageNameToShowInAddRemoveProgram).ToArray());
            engine.Log(LogLevel.Verbose, $"PackageName={packageNames}");
            var registryView = OperatingSystemEnvironment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
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
                                var newModule = _apiResponse.AvailableUpdates.Where(z => z.PackageNameToShowInAddRemoveProgram.ToLower().Equals(_softwareName.ToLower())).FirstOrDefault();
                                string newModulePackageId = newModule != null ? newModule.PackageId.ToString() : string.Empty;
                                if (Packages.GetPackageIdsAsEnum().ToList()
                                    .Where(x => x.ToString().ToLower().Equals((_softwareName.ToLower())) || x.ToString().ToLower().Equals((newModulePackageId.ToLower()))).Any()
                                    )
                                {
                                    string displayVersion = subkey.GetValue("DisplayVersion") != null ? subkey.GetValue("DisplayVersion").ToString() : string.Empty;
                                    installedModule = new InstalledModule();
                                    installedModule.RegistryKey = a;
                                    installedModule.ModuleName = _softwareName;
                                    installedModule.ModuleVersion = displayVersion;
                                    installedModule.RegistryRoot = root;
                                    installedModule.BundleCachePath = subkey.GetValue("BundleCachePath") != null ? subkey.GetValue("BundleCachePath").ToString() :string.Empty;
                                    installedModule.ModifyPath = subkey.GetValue("ModifyPath") != null ? subkey.GetValue("ModifyPath").ToString() : string.Empty;
                                    installedModule.UninstallString = subkey.GetValue("UninstallString") != null ? subkey.GetValue("UninstallString").ToString() : string.Empty;
                                    installedModule.QuietUninstallString = subkey.GetValue("QuietUninstallString") != null ? subkey.GetValue("QuietUninstallString").ToString() : string.Empty;
                                    installedModuleLst.Add(installedModule);
                                }
                                else
                                {
                                    if (includeMainBundle)
                                    {
                                        if(_softwareName.ToLower().Equals("Bootstrapper".ToLower()))
                                        {
                                            string displayVersion = subkey.GetValue("DisplayVersion") != null ? subkey.GetValue("DisplayVersion").ToString() : string.Empty;
                                            installedModule = new InstalledModule();
                                            installedModule.RegistryKey = a;
                                            installedModule.ModuleName = _softwareName;
                                            installedModule.ModuleVersion = displayVersion;
                                            installedModule.RegistryRoot = root;
                                            installedModule.BundleCachePath = subkey.GetValue("BundleCachePath") != null ? subkey.GetValue("BundleCachePath").ToString() : string.Empty;
                                            installedModule.ModifyPath = subkey.GetValue("ModifyPath") != null ? subkey.GetValue("ModifyPath").ToString() : string.Empty;
                                            installedModule.UninstallString = subkey.GetValue("UninstallString") != null ? subkey.GetValue("UninstallString").ToString() : string.Empty;
                                            installedModule.QuietUninstallString = subkey.GetValue("QuietUninstallString") != null ? subkey.GetValue("QuietUninstallString").ToString() : string.Empty;
                                            installedModuleLst.Add(installedModule);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            foreach (var module in installedModuleLst)
            {
                engine.Log(LogLevel.Verbose,$"GetModulesInstalledOnClientComputer:: " +
                                            $"RegistryRoot={module.RegistryRoot} " +
                                            $"RegistryKey={module.RegistryKey} " +
                                            $"ModuleName={module.ModuleName} " +
                                            $"ModuleVersion={module.ModuleVersion} " +
                                            $"BundleCachePath={module.BundleCachePath} " +
                                            $"ModifyPath={module.ModifyPath} " +
                                            $"UninstallString={module.UninstallString} " +
                                            $"QuietUninstallString={module.QuietUninstallString} ");
            }
            return installedModuleLst;
        }


        private void DeleteInstalledRegistryKey(IList<string> registryKeyToDeleteList)
        {
            var registryView = OperatingSystemEnvironment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, registryView);
            //Get SciexOS Module Installed Version
            foreach (string keyToDelete in registryKeyToDeleteList)
            {
                var root = $@"Software\MySciex\{keyToDelete}";
                RegistryKey regKey = key.OpenSubKey(root, true);
                if (regKey != null)
                {
                    //If keyName found then delete 
                    //Only ExePackage will have these keys.
                    //Msi package will not have these keys
                    string keyName = "installed";
                    if (regKey.GetValue(keyName) != null)
                    {
                        regKey.DeleteValue(keyName);
                    }
                }
            }
        }

        private void UpdateDetectConditionForPackageSelectedForUpdate(IList<string> packageIds)
        {
            //If package is selected for update then set isdetected to no
            //This will force the package download even if the package is installed on client's computer
            const string isdetected = "no";
            foreach (string packageId in packageIds)
            {
                if (packageId.ToLower().Equals(PackageIdEnum.FirstInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.FirstInstallerBootstrapper)}"] = isdetected;
                }
                if (packageId.ToLower().Equals(PackageIdEnum.SecondInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.SecondInstallerBootstrapper)}"] = isdetected;
                }
                if (packageId.ToLower().Equals(PackageIdEnum.ThirdInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.ThirdInstallerBootstrapper)}"] = isdetected;
                }
                if (packageId.ToLower().Equals(PackageIdEnum.FourthInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.FourthInstallerBootstrapper)}"] = isdetected;
                }
                if (packageId.ToLower().Equals(PackageIdEnum.FifthInstallerBootstrapper.ToString().ToLower()))
                {
                    engine.StringVariables[$"{Packages.GetInstalledPackageName(PackageIdEnum.FifthInstallerBootstrapper)}"] = isdetected;
                }
            }
        }

        private string GetInstalledVersion(string PackageID)
        {
            var registryView = OperatingSystemEnvironment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            var roots = new string[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\", @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" };
            foreach (var root in roots)
            {
                using (var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
                {
                    var rootKeyLocal = localMachine.OpenSubKey(root, false);

                    try
                    {
                        foreach (var subKeyName in rootKeyLocal.GetSubKeyNames())
                        {
                            var subKey = localMachine.OpenSubKey(string.Format("{0}{1}",root,subKeyName));
                                
                            if (subKey == null) continue;

                            var appName = (string)subKey.GetValue("DisplayName");

                            if (appName != null && string.Equals(appName, PackageID, StringComparison.OrdinalIgnoreCase))
                            {
                                return (string)subKey.GetValue("DisplayVersion"); ;
                            }
                        }
                    }catch(Exception ex) 
                    {
                        MessageBox.Show(ex.Message);
                    }
                }
            }
            return string.Empty;
        }
        private CheckForUpdatesRequest GetApiRequest()
        {
            CheckForUpdatesRequest request = new CheckForUpdatesRequest();
            request.InstalledModule = GetModulesInstalledOnClientComputer(true);
            return request;
        }

    }
}
