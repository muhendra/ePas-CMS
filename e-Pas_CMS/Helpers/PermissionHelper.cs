using System.Security.Claims;

namespace e_Pas_CMS.Helpers
{
    public static class PermissionHelper
    {
        public const string PermissionClaimType = "Permission";
        public const string MenuFunctionClaimType = "MenuFunction";

        public static bool HasPermission(this ClaimsPrincipal user, params string[] permissions)
        {
            if (user?.Identity == null || !user.Identity.IsAuthenticated)
                return false;

            if (permissions == null || permissions.Length == 0)
                return false;

            var permissionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var claim in user.Claims.Where(x =>
                         x.Type == PermissionClaimType ||
                         x.Type == MenuFunctionClaimType))
            {
                if (string.IsNullOrWhiteSpace(claim.Value))
                    continue;

                var tokens = claim.Value
                    .Split('#', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x));

                foreach (var token in tokens)
                    permissionSet.Add(token);
            }

            return permissions.Any(permission => permissionSet.Contains(permission));
        }

        public static List<string> ParseMenuFunctions(string menuFunction)
        {
            if (string.IsNullOrWhiteSpace(menuFunction))
                return new List<string>();

            return menuFunction
                .Split('#', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }
    }
}