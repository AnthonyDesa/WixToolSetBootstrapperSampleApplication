using System;

namespace InstallerUI_FIS
{
	public interface IUIInteractionService
	{
		void ShowMessageBox(string message);
		void CloseUIAndExit();
		void RunOnUIThread(Action body);
		IntPtr GetMainWindowHandle();
	}
}
