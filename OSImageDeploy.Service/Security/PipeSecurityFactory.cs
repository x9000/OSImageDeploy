using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace OSImageDeploy.Service.Security
{
	public static class PipeSecurityFactory
	{
		public static PipeSecurity CreateReadOnlyServiceSecurity()
		{
			PipeSecurity security = new PipeSecurity();
			PipeAccessRights clientAccessRights =
				PipeAccessRights.ReadWrite |
				PipeAccessRights.Synchronize;

			using WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
			SecurityIdentifier serviceIdentity = currentIdentity.User ??
				throw new InvalidOperationException(
					"The service process does not have a Windows user identity.");

			security.SetAccessRuleProtection(
				isProtected: true,
				preserveInheritance: false);

			security.AddAccessRule(
				new PipeAccessRule(
					new SecurityIdentifier(
						WellKnownSidType.NetworkSid,
						null),
					PipeAccessRights.FullControl,
					AccessControlType.Deny));

			security.AddAccessRule(
				new PipeAccessRule(
					new SecurityIdentifier(
						WellKnownSidType.LocalSystemSid,
						null),
					PipeAccessRights.FullControl,
					AccessControlType.Allow));

			security.AddAccessRule(
				new PipeAccessRule(
					serviceIdentity,
					PipeAccessRights.FullControl,
					AccessControlType.Allow));

			security.AddAccessRule(
				new PipeAccessRule(
					new SecurityIdentifier(
						WellKnownSidType.BuiltinAdministratorsSid,
						null),
					clientAccessRights,
					AccessControlType.Allow));

			security.AddAccessRule(
				new PipeAccessRule(
					new SecurityIdentifier(
						WellKnownSidType.AuthenticatedUserSid,
						null),
					clientAccessRights,
					AccessControlType.Allow));

			return security;
		}
	}
}
