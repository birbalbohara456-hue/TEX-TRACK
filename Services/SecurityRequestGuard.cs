namespace TexTrack.Web.Services;

public static class SecurityRequestGuard
{
    public static bool IsLocalReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '/') return false;
        if (value.Length == 1) return true;
        return value[1] is not ('/' or '\\');
    }
}
