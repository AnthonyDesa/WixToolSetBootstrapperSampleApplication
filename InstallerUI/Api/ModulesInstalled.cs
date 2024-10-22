using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI.Api
{
    public class ModulesInstalled
    {
        public string ModuleName { get; set; }
        public string ModuleVersion { get; set; }
        public string RegistryKey { get; set; }
    }
}
