using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI.Api
{
    internal static class CheckForUpdates
    {
        public static CheckForUpdatesResponse CheckForUpdateInformation(CheckForUpdatesRequest request)
        {
            CheckForUpdatesResponse response = new CheckForUpdatesResponse();
            string serverName = "pc-swd-1455.absciexdev.local";
            IList<AvailableUpdate> availableUpdates = new List<AvailableUpdate>();
            AvailableUpdate availableUpdate = new AvailableUpdate();
            availableUpdate.PackageId = PackageIdEnum.FirstInstallerBootstrapper;
            availableUpdate.PackageNameToShowInAddRemoveProgram = availableUpdate.PackageId.ToString();
            availableUpdate.Version = "1.1.0.0";
            availableUpdate.IsNewModule = false;
            availableUpdate.ServerName = serverName;
            availableUpdate.DownloadFileNameWithExtension = $"{availableUpdate.PackageId}.exe";
            availableUpdates.Add(availableUpdate);

            availableUpdate = new AvailableUpdate();
            availableUpdate.PackageId = PackageIdEnum.SecondInstallerBootstrapper;
            availableUpdate.PackageNameToShowInAddRemoveProgram = availableUpdate.PackageId.ToString();
            availableUpdate.Version = "1.2.0.0";
            availableUpdate.IsNewModule = false;
            availableUpdate.ServerName = serverName;
            availableUpdate.DownloadFileNameWithExtension = $"{availableUpdate.PackageId}.exe";
            availableUpdates.Add(availableUpdate);

            availableUpdate = new AvailableUpdate();
            availableUpdate.PackageId = PackageIdEnum.ThirdInstallerBootstrapper;
            availableUpdate.PackageNameToShowInAddRemoveProgram = availableUpdate.PackageId.ToString();
            availableUpdate.Version = "1.3.0.0";
            availableUpdate.IsNewModule = false;
            availableUpdate.ServerName = serverName;
            availableUpdate.DownloadFileNameWithExtension = $"{availableUpdate.PackageId}.exe";
            availableUpdates.Add(availableUpdate);

            availableUpdate = new AvailableUpdate();
            availableUpdate.PackageId = PackageIdEnum.FourthInstallerBootstrapper;
            availableUpdate.PackageNameToShowInAddRemoveProgram = availableUpdate.PackageId.ToString(); 
            availableUpdate.Version = "1.4.0.0";
            availableUpdate.IsNewModule = false;
            availableUpdate.ServerName = serverName;
            availableUpdate.DownloadFileNameWithExtension = $"{availableUpdate.PackageId}.exe";
            availableUpdates.Add(availableUpdate);

            availableUpdate = new AvailableUpdate();
            availableUpdate.PackageId = PackageIdEnum.FifthInstallerBootstrapper;
            availableUpdate.PackageNameToShowInAddRemoveProgram = availableUpdate.PackageId.ToString();
            availableUpdate.Version = "1.5.0.0";
            availableUpdate.IsNewModule = false;
            availableUpdate.ServerName = serverName;
            availableUpdate.DownloadFileNameWithExtension = $"{availableUpdate.PackageId}.exe";
            availableUpdates.Add(availableUpdate);

            availableUpdate = new AvailableUpdate();
            availableUpdate.PackageId = PackageIdEnum.SixthInstallerBootstrapper;
            availableUpdate.Version = "1.0.0.0";
            availableUpdate.IsNewModule = true;
            availableUpdate.ServerName = serverName;
            availableUpdate.PackageNameToDisplayInSetUpUI = "6th Bootstrapper";
            availableUpdate.PackageNameToShowInAddRemoveProgram = "6thBootstrapper";
            availableUpdate.DownloadFileNameWithExtension = $"{availableUpdate.PackageId}.exe";
            availableUpdates.Add(availableUpdate);

            response.AvailableUpdates = availableUpdates;
            return response;
        }
    }
}
