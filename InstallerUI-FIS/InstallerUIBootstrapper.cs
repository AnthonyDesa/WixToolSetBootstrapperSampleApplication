using Microsoft.Tools.WindowsInstallerXml.Bootstrapper;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Diagnostics;
using System.Dynamic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace InstallerUI_FIS
{
    public class InstallerUIBootstrapper : BootstrapperApplication, IUIInteractionService
    {
        private BootstrapperApplicationData bootstrapperApplicationData;

        private Window installerMainWindow;
        private IntPtr installerMainWindowHandle;
        private string[] bootstrapperArgs;

        /// <summary>
        /// Entry point for WiX.
        /// </summary>
        protected override void Run()
        {
            string[] commandLine = this.Command.GetCommandLineArgs();
            if (commandLine != null)
            {
                this.Engine.Log(LogLevel.Verbose, "command line - " + commandLine.Length);
                if (commandLine.Length > 0)
                {
                    this.Engine.Log(LogLevel.Verbose, "command line - " + commandLine[0]);
                }
            }
            this.Engine.Log(LogLevel.Verbose, "InstallerUI-FIS ... ");

            var modifyPath = GetModifyPath();
            if (!string.IsNullOrEmpty(modifyPath) && (!(commandLine.Length > 0 && commandLine[0] != null && commandLine[0].Contains("mainsetupinstall"))))
            {
                try
                {
                    this.Engine.Log(LogLevel.Verbose, modifyPath);

                    bootstrapperArgs = modifyPath.Split(' ');
                    this.Engine.Log(LogLevel.Verbose, bootstrapperArgs[0]);
                    this.Engine.Log(LogLevel.Verbose, bootstrapperArgs[1]);
                    this.Engine.Log(LogLevel.Verbose, bootstrapperArgs[2]);

                    Task.Factory.StartNew(new Action(StartBootstrapper));
                }catch (Exception ex) { this.Engine.Log(LogLevel.Verbose, ex.Message); }
                this.Engine.Quit(0);
            }
            else
            {
                this.Engine.Log(LogLevel.Verbose, "Running the custom WPF UI.");


                using (var container = this.SetupCompositionContainer())
                {
                    // Get metadata from BootstrapperApplicationData.xml and add it to the log 
                    // for demonstration purposes
                    this.bootstrapperApplicationData = new BootstrapperApplicationData();
                    this.Engine.Log(LogLevel.Verbose, JsonConvert.SerializeObject(this.bootstrapperApplicationData));

                    // Create main window with associated view model
                    this.Engine.Log(LogLevel.Verbose, "Creating a UI.");
                    this.installerMainWindow = container.GetExportedValue<Window>("InstallerMainWindow");
                    this.installerMainWindowHandle = new WindowInteropHelper(this.installerMainWindow).EnsureHandle();

                    // Kick off detect which will populate the view models.
                    this.Engine.Detect();

                    // Show UI.
                    if (this.Command.Display == Display.Passive || this.Command.Display == Display.Full)
                    {
                        this.installerMainWindow.Show();
                    }

                    System.Windows.Threading.Dispatcher.Run();

                    this.Engine.Quit(0);
                    this.Engine.Log(LogLevel.Verbose, "Exiting custom WPF UI.");
                }
            }
        }

        private void StartBootstrapper()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = $"{bootstrapperArgs[0]} {bootstrapperArgs[1]}",
                Arguments = bootstrapperArgs[2],
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = false,
                Verb = "runas"
            };
            Process processTemp = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            processTemp.Start();
            processTemp.WaitForExit();
        }

        private static string GetModifyPath()
        {
            var registryView = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            var roots = new string[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\", @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" };
            foreach (var root in roots)
            {
                using (var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
                {
                    var rootKeyLocal = localMachine.OpenSubKey(root, false);

                    if (rootKeyLocal != null)
                    {
                        foreach (var subKeyName in rootKeyLocal.GetSubKeyNames())
                        {
                            var subKey = localMachine.OpenSubKey(
                                $"{root}\\{subKeyName}");

                            if (subKey == null) continue;

                            var appName = (string)subKey.GetValue("DisplayName");

                            if (appName != null &&
                                string.Equals(appName, "Bootstrapper", StringComparison.OrdinalIgnoreCase))
                            {
                                return (string)subKey.GetValue("ModifyPath");
                            }
                        }
                    }
                }
            }
            return string.Empty;
        }

        private CompositionContainer SetupCompositionContainer()
        {
            var catalog = new AssemblyCatalog(Assembly.GetExecutingAssembly());
            var container = new CompositionContainer(catalog);
            container.ComposeExportedValue<BootstrapperApplication>(this);
            container.ComposeExportedValue<Engine>(this.Engine);
            container.ComposeExportedValue<IUIInteractionService>(this);
            return container;
        }

        public void ShowMessageBox(string message)
        {
            this.installerMainWindow.Dispatcher.BeginInvoke(new Action(() => MessageBox.Show(message)), null);
        }

        public void CloseUIAndExit()
        {
            this.installerMainWindow.Dispatcher.BeginInvoke(new Action(() => this.installerMainWindow.Close()));
        }

        public void RunOnUIThread(Action body)
        {
            this.installerMainWindow.Dispatcher.BeginInvoke(body, null);
        }

        public IntPtr GetMainWindowHandle()
        {
            return this.installerMainWindowHandle;
        }
    }
}
