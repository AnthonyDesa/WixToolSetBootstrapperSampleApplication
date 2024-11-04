using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI
{
    public class Settings
    {
        public IList<DownloadUrl> DownloadUrls { get; set; }
        public Environment ActiveEnvironment { get; set; }
    }

    
}
