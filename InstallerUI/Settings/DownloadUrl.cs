using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI
{
    public class DownloadUrl
    {
        public Environment Environment { get; set; }
        public string Host { get; set; }
        public IList<Package> Packages { get; set; }

    }
}
