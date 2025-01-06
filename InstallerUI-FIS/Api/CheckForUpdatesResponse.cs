using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI_FIS.Api
{
    internal class CheckForUpdatesResponse
    {
        public IList<AvailableUpdate> AvailableUpdates { get; set; }
    }
}
