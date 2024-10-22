using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI.Api
{
    public class CheckForUpdatesRequest
    {
        IList<ModulesInstalled> ModulesInstalled { get; set; }
    }
}
