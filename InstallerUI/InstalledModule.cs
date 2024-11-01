using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI
{
    public class InstalledModule
    {
        public string ModuleName { get; set; }
        public string ModuleVersion { get; set; }
        public string RegistryKey { get; set; }
        public string RegistryRoot { get; set; }
        public string ModifyPath { get; set; }
        public string BundleCachePath { get; set; }
        public string QuietUninstallString { get; set; }
        public string UninstallString { get; set; }
    }
}
