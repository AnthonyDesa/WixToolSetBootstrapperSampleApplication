using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI
{
    public class DownloadUrl
    {
        //When developer is working in their local environment they will set the value of Environment to Development
        //When developer is checking in the code to teamcity then will set the value of Environment to Staging
        //For final release build to customer teamcity will have to flip the value to Production.
        //Customer cannot use out internal environment. They are expected to download packages from public facing website
        //We have to run the final build in team city by setting some variable to indicate to the process (batch file or powershell script) that this is the final build
        //Internal resourse should be able to test the final build before it is released to customer
        public Environment Environment { get; set; }
        public string Host { get; set; }
        public IList<Package> Packages { get; set; }

    }
    public enum Environment
    {
        Development,
        Staging,
        Production
    }
}
